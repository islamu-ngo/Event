using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Explore.Application.Features.Localization.Requests.Commands;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Application.Settings;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Explore.Application.Features.InstanceOnboarding.Commands;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.Settings;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Issues single-attempt Settings capabilities from original idle-owner sources. The actual
/// Serializable owner acquires S/P/optional route/H before exposing any data callback.
/// Neither a public interface implementation nor a supplied transaction is proof of admission.
/// </summary>
public sealed partial class HumanSettingsMutationPreparation(
    IServiceProvider services,
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor httpContext,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    ITenantContext tenantContext,
    TimeProvider clock,
    CerbosAuthorizationService cerbos,
    IOptions<AuthorizationProviderDeploymentOptions> authorizationOptions,
    IOptions<CerbosSettings> cerbosOptions) : IHumanSettingsMutationPreparation
{
    private readonly AsyncLocal<Prepared?> _currentOwner = new();

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateLocalizationGovernanceCommand command,
        CancellationToken cancellationToken = default) =>
        PrepareCoreAsync(command, SettingScope.Instance, null, Authority.Instance, null,
            (_, _) => OrdinarySettingsMutation.CaptureLocalization(command), [], cancellationToken);

    public IOrdinarySettingsSource? CurrentOrdinarySource =>
        _currentOwner.Value is { Active: true } current ? current.OrdinarySource : null;

    public IAtprotoFederationSettingsSource? CurrentAtprotoFederationSource =>
        _currentOwner.Value is { Active: true } current ? current.FederationSource : null;

    public async Task<T> ExecuteMixedJoinedAsync<T>(MixedSettingsBatchMutation mutation,
        Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        mutation.RequireActive();
        services.GetRequiredService<Explore.Application.Features.EmailDispatch.IHumanEmailMutationPreparation>()
            .RequireMixedSource(mutation.Source);
        if (_currentOwner.Value is not null || mutation.Actor != humanScope.Subject?.PrincipalId
            || mutation.TenantId.HasValue && mutation.TenantId != tenantContext.TenantId
            || !ReferenceEquals(mutation.Owner, services.GetRequiredService<ISerializableAttemptOwner>()))
            throw new UnauthorizedAccessException("A mixed Settings slice must borrow its original physical owner.");
        var effects = mutation.SettingsEffects();
        var issued = new Prepared(this, mutation.Original, effects,
            CoordinatedKeys(effects.Select(effect => effect.Key)), mutation.Owner,
            _ => throw new UnauthorizedAccessException("A mixed Settings slice cannot acquire another owner."))
        {
            Consumed = 1,
            Active = true,
            OwnsLifetime = false,
            FederationStore = mutation.FederationStore,
            FederationSource = mutation.FederationSource,
            ValidateOwner = token =>
            {
                mutation.RequireActive();
                return mutation.Source.RequireSourcesAsync(token);
            }
        };
        _currentOwner.Value = issued;
        try { return await InvokeAsync(issued, operation, cancellationToken); }
        finally { _currentOwner.Value = null; issued.Active = false; issued.ValidateOwner = null; }
    }

    public async Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateResolverConfigurationCommand command,
        ResolverConfigurationMutation mutation, CancellationToken cancellationToken = default)
    {
        services.GetRequiredService<IResolverConfigurationMutationAuthority>().RequireIssued(mutation.Source);
        mutation.RequireActive();
        if (!ReferenceEquals(command, mutation.OriginalCommand) || command.UserId == Guid.Empty
            || command.UserId != mutation.Actor)
            throw new UnauthorizedAccessException("Resolver effects require their actual original human command.");
        // The real resolver PATCH has an instance-admin predicate, not a PDP annotation.
        // Its fixed metadata/lock effects belong to its dedicated writer, not generic scalar access.
        var issued = (Prepared)await PrepareCoreAsync(mutation, SettingScope.Instance, null, Authority.Instance, command.UserId,
            (_, _) => mutation.Effects.Select(effect => new HumanSettingsMutation(
                HumanSettingsOperation.InstanceSettings, SettingScope.Instance, null, null,
                effect.Key, HumanSettingsMutationKind.SetValue, effect.Value)).ToImmutableArray(),
            [], cancellationToken);
        try
        {
            var writer = services.GetRequiredService<IResolverConfigurationSettingsWriter>();
            var finalizer = services.GetRequiredService<IHumanActionCommitFinalizer>();
            // Only this wrapper escapes preparation. It retains the same physical owner and
            // original acquisition, and installs completion admission before exposing any callback.
            // An exact-value scalar write cannot bypass the capsule's metadata/projection checks.
            return new Prepared(this, mutation, issued.Effects, issued.CoordinatedReadKeys, issued.Owner, async token =>
            {
                var validate = await issued.Acquire(token);
                finalizer.Register(finalToken => writer.RequireAppliedAsync(mutation, finalToken), _ =>
                {
                    mutation.RequireActive();
                    mutation.Source.RequireTime();
                });
                return validate;
            }).TakeCoordinationFrom(issued);
        }
        catch (Exception failure)
        {
            await FinishSettingsAttemptAsync(failure, false, issued.TakeCoordination());
            throw;
        }
    }

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(SaveInstanceOperatorIdentityCommand command,
        OperatorIdentityMutation mutation, CancellationToken cancellationToken = default) =>
        PrepareOperatorIdentityAsync(command, mutation, null, cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(ImportInstanceOperatorIdentityCommand command,
        OperatorIdentityMutation mutation, CancellationToken cancellationToken = default)
    {
        ISecureRequest original = command;
        return PrepareOperatorIdentityAsync(command, mutation,
            new AuthorizationRequest(ResourceKinds.InstanceSetting, original.ResourceId!,
                AuthorizationActions.InstanceSettings.Update, Facts: original.AuthorizationFacts),
            cancellationToken);
    }

    private Task<IPreparedHumanSettingsMutation> PrepareOperatorIdentityAsync(object command,
        OperatorIdentityMutation mutation, AuthorizationRequest? check, CancellationToken token)
    {
        services.GetRequiredService<IOperatorIdentityMutationAuthority>().RequireIssued(mutation.Source);
        if (!ReferenceEquals(command, mutation.OriginalCommand) || !mutation.Source.IsHuman
            || mutation.Actor is not { } actor || actor == Guid.Empty
            || check is not null && mutation.Source.IsSetup)
            throw new UnauthorizedAccessException("Operator identity must retain its actual human command and source.");
        // Save has no PDP annotation. Import retains its real InstanceSetting/update annotation.
        // A human using the accepted setup-secret branch keeps Subject/P/H, not a fabricated
        // administrator grant. The dedicated writer also fences that exact pending generation.
        return PrepareCoreAsync(mutation, SettingScope.Instance, null,
            mutation.Source.IsSetup ? Authority.Subject : Authority.Instance, actor,
            (_, _) => [new(HumanSettingsOperation.InstanceSettings, SettingScope.Instance, null, null,
                InstanceOperatorIdentitySettingKeys.OperatorIdentity, HumanSettingsMutationKind.SetValue,
                mutation.After.Value)], [], token, check);
    }

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateInstanceStorageSettingsCommand command,
        StorageSettingsMutation mutation, CancellationToken cancellationToken = default)
    {
        if (mutation.TenantId is not null)
            throw new UnauthorizedAccessException("Instance storage requires its original instance owner.");
        return PrepareStorageAsync(command, command.UserId, mutation, cancellationToken);
    }

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(PatchTenantStorageSettingsCommand command,
        StorageSettingsMutation mutation, CancellationToken cancellationToken = default)
    {
        if (mutation.TenantId is null || mutation.TenantId != tenantContext.TenantId)
            throw new UnauthorizedAccessException("Tenant storage requires its original current tenant.");
        return PrepareStorageAsync(command, command.UserId, mutation, cancellationToken);
    }

    private async Task<IPreparedHumanSettingsMutation> PrepareStorageAsync(object command, Guid actor,
        StorageSettingsMutation mutation, CancellationToken token)
    {
        services.GetRequiredService<IStorageConfigurationAuthority>().RequireIssued(mutation.Source);
        mutation.RequireActive();
        if (!ReferenceEquals(command, mutation.OriginalCommand) || actor == Guid.Empty || actor != mutation.Actor)
            throw new UnauthorizedAccessException("Storage effects require their actual original administrator command.");
        var scope = mutation.TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance;
        // Neither actual storage command has a PDP annotation. Retain its administrator
        // predicate, including the tenant action's existing tenant-or-instance alternative.
        var issued = (Prepared)await PrepareCoreAsync(mutation, scope, mutation.TenantId,
            mutation.TenantId.HasValue ? Authority.TenantOrInstance : Authority.Instance, actor,
            (_, _) => mutation.Values.Select(pair => new HumanSettingsMutation(OperationFor(scope), scope,
                mutation.TenantId, null, pair.Key, HumanSettingsMutationKind.SetValue, pair.Value)).ToImmutableArray(),
            [], token, requireOriginalRequest: mutation.Source.RequireRequestAsync,
            requireOriginalFacts: mutation.Source.RequireAcquiredFacts);
        try
        {
            var writer = services.GetRequiredService<IStorageConfigurationWriter>();
            var finalizer = services.GetRequiredService<IHumanActionCommitFinalizer>();
            return new Prepared(this, mutation, issued.Effects, issued.CoordinatedReadKeys, issued.Owner, async ct =>
            {
                // Mandatory before any caller callback can escape. An equal generic/native
                // scalar write cannot mark this private selected writer complete.
                finalizer.Register(final => writer.RequireAppliedAsync(mutation, final), _ =>
                {
                    mutation.RequireActive();
                    mutation.Source.RequireTime();
                });
                return await issued.Acquire(ct);
            }).TakeCoordinationFrom(issued);
        }
        catch (Exception failure)
        {
            await FinishSettingsAttemptAsync(failure, false, issued.TakeCoordination());
            throw;
        }
    }

    public async Task<T> ExecuteStorageJoinedAsync<T>(IPreparedHumanSettingsMutation owner,
        StorageSettingsMutation storage, Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default)
    {
        var parent = RequireActive(owner);
        storage.RequireActive();
        if (!ReferenceEquals(parent.Original, storage) || storage.Actor != humanScope.Subject?.PrincipalId)
            throw new UnauthorizedAccessException("Storage effects must retain their original prepared command.");
        var effects = parent.Effects;
        if (storage.TenantId is null)
            effects = effects.AddRange(parent.Effects.Select(effect => effect with
            {
                Kind = HumanSettingsMutationKind.SetLock,
                Value = null,
                IsLocked = false
            }));
        // Instance group writes historically clear these locks; tenant leaf writes preserve
        // them. This private derivation must never confer tenant lock or removal authority.
        return await ExecuteDerivedAsync(parent, effects, mutation, cancellationToken);
    }

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateAuthProviderConfigurationCommand command,
        ProviderConfigurationMutation mutation, CancellationToken cancellationToken = default) =>
        PrepareProviderAsync(command, command.UserId, mutation, cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateAuthorizationProviderConfigurationCommand command,
        ProviderConfigurationMutation mutation, CancellationToken cancellationToken = default) =>
        PrepareProviderAsync(command, command.UserId, mutation, cancellationToken);

    private async Task<IPreparedHumanSettingsMutation> PrepareProviderAsync(object command, Guid actor,
        ProviderConfigurationMutation mutation, CancellationToken token)
    {
        mutation.RequireActive();
        services.GetRequiredService<IProviderConfigurationMutationAuthority>().RequireIssued(mutation.Source);
        if (!ReferenceEquals(command, mutation.OriginalCommand) || mutation.IsSetup || actor == Guid.Empty)
            throw new UnauthorizedAccessException("Provider effects require their actual original administrator command.");
        // These actual commands are administrator-only, not ISecureRequest/PDP actions.
        // Their fixed paired lock effects do not select a replacement lock policy.
        var issued = (Prepared)await PrepareCoreAsync(mutation, SettingScope.Instance, null, Authority.Instance, actor,
            (_, _) => mutation.Effects.Select(effect => new HumanSettingsMutation(
                HumanSettingsOperation.InstanceSettings, SettingScope.Instance, null, null,
                effect.Key, HumanSettingsMutationKind.SetValue, effect.Value)).ToImmutableArray(),
            CoordinatedKeys(mutation.Effects.Select(effect => effect.Key)), token);
        try
        {
            var writer = services.GetRequiredService<IProviderConfigurationSettingsWriter>();
            var finalizer = services.GetRequiredService<IHumanActionCommitFinalizer>();
            return new Prepared(this, mutation, issued.Effects, issued.CoordinatedReadKeys, issued.Owner, async ct =>
            {
                var validate = await issued.Acquire(ct);
                finalizer.Register(final => writer.RequireAppliedAsync(mutation, final), _ =>
                {
                    mutation.RequireActive();
                    mutation.Source.RequireTime();
                });
                return validate;
            }).TakeCoordinationFrom(issued);
        }
        catch (Exception failure)
        {
            await FinishSettingsAttemptAsync(failure, false, issued.TakeCoordination());
            throw;
        }
    }

    public async Task<T> ExecuteProviderJoinedAsync<T>(IPreparedHumanSettingsMutation owner,
        ProviderConfigurationMutation provider,
        Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default)
    {
        var parent = RequireActive(owner);
        provider.RequireActive();
        if (!ReferenceEquals(parent.Original, provider) || provider.IsSetup
            || provider.Actor != humanScope.Subject?.PrincipalId)
            throw new UnauthorizedAccessException("Provider effects must retain their original prepared command.");
        var effects = ImmutableArray.CreateBuilder<HumanSettingsMutation>();
        foreach (var setting in provider.Effects)
        {
            var value = new HumanSettingsMutation(HumanSettingsOperation.InstanceSettings, SettingScope.Instance,
                null, null, setting.Key, HumanSettingsMutationKind.SetValue, setting.Value);
            if (!parent.Effects.Contains(value))
                throw new UnauthorizedAccessException("Provider values cannot be substituted after preparation.");
            effects.Add(value);
            effects.Add(value with { Kind = HumanSettingsMutationKind.SetLock, Value = null, IsLocked = setting.IsLocked });
            if (VisitorAccessSettingMutationGuard.Handles(setting.Key))
                effects.Add(value with { IsLocked = setting.IsLocked });
        }
        return await ExecuteDerivedAsync(parent, effects.ToImmutable(), mutation, cancellationToken);
    }

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateSettingCommand command, string storedValue,
        CancellationToken cancellationToken = default) =>
        PrepareScalarAsync(command, command.Scope, command.Key, HumanSettingsMutationKind.SetValue,
            storedValue, null, cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(ResetSettingCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Scope == SettingScope.Instance)
            throw new UnauthorizedAccessException("Instance settings cannot be reset through ordinary reset.");
        return PrepareScalarAsync(command, command.Scope, command.Key, HumanSettingsMutationKind.Remove,
            null, null, cancellationToken);
    }

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(LockSettingCommand command,
        CancellationToken cancellationToken = default) =>
        PrepareLockAsync(command, command.Scope, command.Key, true, cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UnlockSettingCommand command,
        CancellationToken cancellationToken = default) =>
        PrepareLockAsync(command, command.Scope, command.Key, false, cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateSettingBatchCommand command,
        ImmutableDictionary<string, string> storedValues, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(storedValues);
        if (command.Values is null || storedValues.Keys.Any(key => !command.Values.ContainsKey(key)))
            throw new UnauthorizedAccessException("Prepared batch effects must belong to the original request.");
        // BestEffort may exclude invalid keys. The immutable original command retains category,
        // input values and batch mode; only validated stored effects can receive mutation proof.
        return PrepareCoreAsync(command, command.Scope, null, null, null,
            (tenant, actor) => storedValues.Select(pair => new HumanSettingsMutation(OperationFor(command.Scope), command.Scope,
                command.Scope == SettingScope.Instance ? null : tenant,
                command.Scope == SettingScope.User ? actor : null, pair.Key,
                HumanSettingsMutationKind.SetValue, pair.Value)).ToImmutableArray(),
            CoordinatedKeys(storedValues.Keys), cancellationToken);
    }

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(SetInstanceTenantSettingCommand command,
        string storedValue, CancellationToken cancellationToken = default) =>
        PrepareInstanceTenantAsync(command, command.TenantId, command.Key,
            HumanSettingsMutationKind.SetValue, storedValue, null, cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(LockInstanceTenantSettingCommand command,
        CancellationToken cancellationToken = default) =>
        PrepareInstanceTenantAsync(command, command.TenantId, command.Key,
            HumanSettingsMutationKind.SetLock, null, true, cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UnlockInstanceTenantSettingCommand command,
        CancellationToken cancellationToken = default) =>
        PrepareInstanceTenantAsync(command, command.TenantId, command.Key,
            HumanSettingsMutationKind.SetLock, null, false, cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareResourceAsync(
        ImmutableArray<EventResourceSettingMutation> mutations, SettingsMutationOperation operation,
        Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        if (mutations.IsDefault || mutations.Any(mutation => mutation is null
                || !Enum.IsDefined(mutation.Kind) || !EventResourceSettingMutationGuard.Handles(mutation.Key)))
            throw new ArgumentException("Exact resource setting mutations are required.", nameof(mutations));
        return PrepareWriterAsync(mutations, operation, actorUserId,
            mutations.Select(mutation => new HumanSettingsMutation(OperationFor(operation),
                mutation.TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance,
                mutation.TenantId, null, mutation.Key, mutation.Kind switch
                {
                    EventResourceSettingMutationKind.SetValue => HumanSettingsMutationKind.SetValue,
                    EventResourceSettingMutationKind.SetLock => HumanSettingsMutationKind.SetLock,
                    EventResourceSettingMutationKind.Remove => HumanSettingsMutationKind.Remove,
                    _ => throw new ArgumentOutOfRangeException(nameof(mutations))
                }, mutation.Value, mutation.IsLocked)).ToImmutableArray(),
            EventResourceSettingMutationGuard.Keys, cancellationToken);
    }

    public Task<IPreparedHumanSettingsMutation> PrepareVisitorAsync(
        ImmutableArray<VisitorAccessSettingMutation> mutations, SettingsMutationOperation operation,
        Guid? actorUserId, CancellationToken cancellationToken = default)
    {
        if (mutations.IsDefault || mutations.Any(mutation => mutation is null
                || !Enum.IsDefined(mutation.Kind) || !VisitorAccessSettingMutationGuard.Handles(mutation.Key)))
            throw new ArgumentException("Exact visitor setting mutations are required.", nameof(mutations));
        return PrepareWriterAsync(mutations, operation, actorUserId,
            mutations.Select(mutation => new HumanSettingsMutation(OperationFor(operation),
                mutation.TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance,
                mutation.TenantId, null, mutation.Key, mutation.Kind switch
                {
                    VisitorAccessSettingMutationKind.SetValue => HumanSettingsMutationKind.SetValue,
                    VisitorAccessSettingMutationKind.SetLock => HumanSettingsMutationKind.SetLock,
                    VisitorAccessSettingMutationKind.Remove => HumanSettingsMutationKind.Remove,
                    _ => throw new ArgumentOutOfRangeException(nameof(mutations))
                }, mutation.Value, mutation.IsLocked)).ToImmutableArray(),
            Explore.Application.Services.VisitorAccessCapabilityResolver.AuthoritySettingKeys.ToImmutableArray(),
            cancellationToken);
    }

    private Task<IPreparedHumanSettingsMutation> PrepareWriterAsync(object original,
        SettingsMutationOperation operation, Guid? actor, ImmutableArray<HumanSettingsMutation> effects,
        ImmutableArray<string> coordinatedReadKeys, CancellationToken token)
    {
        // These are three closed existing administrator actions. Import is deliberately not a
        // fourth boolean mode: its producer must prove its original acquired policy decision.
        var scope = operation switch
        {
            SettingsMutationOperation.InstanceSettings => SettingScope.Instance,
            SettingsMutationOperation.TenantSettings or SettingsMutationOperation.InstanceTenantSettings => SettingScope.Tenant,
            _ => throw new UnauthorizedAccessException("Import settings require the original producer's acquired effect proof.")
        };
        Guid? target = scope == SettingScope.Instance ? null : tenantContext.TenantId;
        if (effects.Any(effect => effect.TenantId != target))
            throw new UnauthorizedAccessException("Settings effects must retain their original selected tenant.");
        return PrepareCoreAsync(original, scope, target,
            operation == SettingsMutationOperation.InstanceTenantSettings ? Authority.Instance : null,
            actor, (_, _) => effects, coordinatedReadKeys, token);
    }

    private Task<IPreparedHumanSettingsMutation> PrepareLockAsync(object command, SettingScope scope,
        string key, bool locked, CancellationToken token)
    {
        if (scope is not (SettingScope.Instance or SettingScope.Tenant))
            throw new UnauthorizedAccessException("Only the existing administrator setting scopes can lock settings.");
        return PrepareScalarAsync(command, scope, key, HumanSettingsMutationKind.SetLock, null, locked, token);
    }

    private Task<IPreparedHumanSettingsMutation> PrepareScalarAsync(object command, SettingScope scope,
        string key, HumanSettingsMutationKind kind, string? value, bool? locked, CancellationToken token) =>
        PrepareCoreAsync(command, scope, null, null, null,
            (tenant, actor) => [new(OperationFor(scope), scope, scope == SettingScope.Instance ? null : tenant,
                scope == SettingScope.User ? actor : null, key, kind, value, locked)], CoordinatedKeys([key]), token);

    private Task<IPreparedHumanSettingsMutation> PrepareInstanceTenantAsync(ISecureRequest command, Guid tenant,
        string key, HumanSettingsMutationKind kind, string? value, bool? locked, CancellationToken token)
    {
        if (tenant == Guid.Empty)
            throw new ArgumentException("An exact instance-managed tenant is required.", nameof(tenant));
        // Only the three typed public overloads reach this seam. Their actual annotation is
        // InstanceSetting/update, even for lock/unlock, and their resource ID is not the data key.
        var check = new AuthorizationRequest(ResourceKinds.InstanceSetting, command.ResourceId!,
            AuthorizationActions.InstanceSettings.Update, Facts: command.AuthorizationFacts);
        return PrepareCoreAsync(command, SettingScope.Tenant, tenant, Authority.Instance, null,
            (_, _) => [new(HumanSettingsOperation.InstanceTenantSettings, SettingScope.Tenant, tenant, null, key, kind, value, locked)],
            CoordinatedKeys([key]), token, check);
    }

    private static ImmutableArray<string> CoordinatedKeys(IEnumerable<string> keys)
    {
        var captured = keys.ToHashSet(StringComparer.Ordinal);
        var reads = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        if (captured.Any(EventResourceSettingMutationGuard.Handles))
            reads.UnionWith(EventResourceSettingMutationGuard.Keys);
        if (captured.Any(VisitorAccessSettingMutationGuard.Handles))
            reads.UnionWith(Explore.Application.Services.VisitorAccessCapabilityResolver.AuthoritySettingKeys);
        if (captured.Overlaps(PublicationPolicySettingKeys.All))
            reads.UnionWith(PublicationPolicySettingKeys.All);
        reads.UnionWith(LocationPrivacySettingDefinitions.All.Where(item => captured.Contains(item.Key)).Select(item => item.Key));
        return reads.ToImmutableArray();
    }

    private async Task<IPreparedHumanSettingsMutation> PrepareCoreAsync(object original, SettingScope scope,
        Guid? explicitTenant, Authority? explicitAuthority, Guid? auditActor,
        Func<Guid?, Guid, ImmutableArray<HumanSettingsMutation>> capture,
        ImmutableArray<string> coordinatedReadKeys, CancellationToken token, AuthorizationRequest? selectedCheck = null,
        bool observeRouteOnly = false, Func<CancellationToken, Task>? requireOriginalRequest = null,
        Action<HumanPrincipalSourceFacts, HumanSupportAccessSourceFacts>? requireOriginalFacts = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        var authority = explicitAuthority ?? scope switch
        {
            SettingScope.User => Authority.Subject,
            SettingScope.Tenant => Authority.Tenant,
            SettingScope.Instance => Authority.Instance,
            _ => throw new UnauthorizedAccessException("This setting scope has no ordinary human mutation authority.")
        };
        await humanScope.PrepareAsync(token);
        SettingsCoordination? coordination = null;
        IOrdinarySettingsMutationStore? ordinaryStore = null;
        IAtprotoFederationSettingsMutationStore? federationStore = null;
        IOrdinarySettingsSource? ordinarySource = null;
        IAtprotoFederationSettingsSource? federationSource = null;
        try
        {
            if (scope is SettingScope.Instance or SettingScope.Tenant)
                coordination = await SettingsCoordination.AcquireAsync(services, token);
            var subject = humanScope.Subject ?? throw new UnauthorizedAccessException("An original human is required.");
            var request = httpContext.HttpContext ?? throw new UnauthorizedAccessException("An original authenticated request is required.");
            var authenticatedPrincipal = request.User;
            var lifetime = await lifetimeObserver.ObserveAsync(authenticatedPrincipal, token)
                ?? throw new UnauthorizedAccessException("Original credential lifetime evidence is required.");
            Guid ambientTenant = tenantContext.TenantId;
            var operation = scope == SettingScope.Tenant && authority == Authority.Instance
                ? HumanSettingsOperation.InstanceTenantSettings : OperationFor(scope);
            if (auditActor is { } actor && actor != subject.PrincipalId
                || lifetime.AccountKey != subject.AccountKey)
                throw new UnauthorizedAccessException("The original setting actor and credential must agree.");

            void RequireCustody()
            {
                token.ThrowIfCancellationRequested();
                coordination?.RequireCurrent();
                if (!ReferenceEquals(humanScope.Subject, subject) || tenantContext.TenantId != ambientTenant
                    || !ReferenceEquals(httpContext.HttpContext, request)
                    || !ReferenceEquals(request.User, authenticatedPrincipal))
                    throw new UnauthorizedAccessException("The original Settings execution custody has changed.");
            }
            RequireCustody();
            // These constructors bind the context, pool lease and connection while idle. Resolving
            // any one of them inside the Serializable callback would violate that binding contract.
            var sourceSet = services.GetRequiredService<IPolicySourceSetFence>();
            var principalSource = services.GetRequiredService<IHumanPrincipalSourceFence>();
            var routeSource = selectedCheck is null && !observeRouteOnly ? null
                : services.GetRequiredService<IHumanAuthorizationRouteSource>();
            var supportSource = await services.GetRequiredService<IHumanSupportAccessSourceFactory>().CreateAsync(token);
            var finalizer = services.GetRequiredService<IHumanActionCommitFinalizer>();
            var owner = services.GetRequiredService<ISerializableAttemptOwner>();
            var subjectAuthority = authority == Authority.Subject ? services.GetRequiredService<IHumanSubjectCommitAuthority>() : null;
            var tenantAuthority = authority is Authority.Tenant or Authority.TenantOrInstance
                ? services.GetRequiredService<ITenantAdministratorCommitAuthority>() : null;
            var instanceAuthority = authority is Authority.Instance or Authority.TenantOrInstance
                ? services.GetRequiredService<IInstanceAdministratorCommitAuthority>() : null;
            var principalFacts = await principalSource.ObserveAsync(token);
            var routeFacts = routeSource is null ? null : await routeSource.ObserveAsync(token);
            var originalSupport = await supportSource.ObserveAsync(token);
            // Source-owned selection, not the ambient single-tenant fallback, determines whether
            // this is Runtime or ordinary Global. The ambient value is retained separately because
            // it is an actual selected-provider input.
            Guid? selectedTenant = principalFacts.SelectedTenantId;
            if (scope == SettingScope.Instance && selectedTenant.HasValue
                || scope == SettingScope.Tenant && selectedTenant is null
                || scope != SettingScope.Instance && selectedTenant.HasValue && selectedTenant != ambientTenant
                || explicitTenant.HasValue && explicitTenant != selectedTenant)
                throw new UnauthorizedAccessException("The setting action must match its original selected owner.");
            var effects = capture(selectedTenant, subject.PrincipalId);
            if (effects.IsDefault || effects.Any(effect => string.IsNullOrWhiteSpace(effect.Key)
                    || effect.Operation != operation || effect.Scope != scope || effect.TenantId != selectedTenant
                    || effect.UserId != (scope == SettingScope.User ? subject.PrincipalId : (Guid?)null)
                    || !Enum.IsDefined(effect.Kind)
                    || effect.Kind == HumanSettingsMutationKind.SetValue && (effect.Value is null || effect.IsLocked.HasValue)
                    || effect.Kind == HumanSettingsMutationKind.SetLock && (effect.Value is not null || !effect.IsLocked.HasValue)
                    || effect.Kind == HumanSettingsMutationKind.Remove && (effect.Value is not null || effect.IsLocked.HasValue))
                || effects.Select(effect => effect.Key).Distinct(StringComparer.Ordinal).Count() != effects.Length)
                throw new ArgumentException("Settings preparation requires immutable, exact, nonduplicated storage effects.");

            var ordinary = OrdinarySettingsMutation.Capture(original, effects);
            // Keep the full original envelope. Each concrete store privately derives
            // its exact executable partition; no reconstructed command is admitted.
            ordinaryStore = ordinary is null || !ordinary.Effects.IsEmpty
                    && ordinary.Effects.All(effect => AtprotoFederationSettingPolicy.IsDedicatedKey(effect.Key))
                ? null : services.GetRequiredService<IOrdinarySettingsMutationStore>();
            federationStore = ordinary?.Effects.Any(effect => AtprotoFederationSettingPolicy.IsDedicatedKey(effect.Key)) == true
                ? services.GetRequiredService<IAtprotoFederationSettingsMutationStore>() : null;
            if (ordinaryStore is not null)
                ordinarySource = await ordinaryStore.ObserveAsync(ordinary!, subject, token);
            if (federationStore is not null)
                federationSource = await federationStore.ObserveAsync(ordinary!, subject, token);

            void RequirePrincipal(HumanPrincipalSourceFacts facts)
            {
                RequireCustody();
                HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, facts);
                if (facts.SelectedTenantId != selectedTenant)
                    throw new UnauthorizedAccessException("Settings sources belong to another selected tenant.");
            }
            RequirePrincipal(principalFacts);
            var supportChecks = effects.Select(effect => new SupportCheck(
                scope == SettingScope.Instance || authority == Authority.Instance ? ResourceKinds.InstanceSetting
                    : scope == SettingScope.User ? ResourceKinds.User : ResourceKinds.TenantSetting,
                effect.Kind switch
                {
                    HumanSettingsMutationKind.Remove => AuthorizationActions.Delete,
                    HumanSettingsMutationKind.SetLock => effect.IsLocked == true ? AuthorizationActions.Lock : AuthorizationActions.Unlock,
                    _ => AuthorizationActions.Update
                },
                authority != Authority.Instance && selectedTenant.HasValue
                    ? new TenantScopedAuthorizationFacts(selectedTenant.Value) : InstanceScopedAuthorizationFacts.Instance))
                .Distinct().ToImmutableArray();
            // Empty batches still release an administrative result and must retain their boundary.
            if (supportChecks.IsEmpty)
                supportChecks = [new(authority == Authority.Instance ? ResourceKinds.InstanceSetting
                : scope == SettingScope.User ? ResourceKinds.User : ResourceKinds.TenantSetting,
                AuthorizationActions.Update, authority != Authority.Instance && selectedTenant.HasValue
                    ? new TenantScopedAuthorizationFacts(selectedTenant.Value) : InstanceScopedAuthorizationFacts.Instance)];
            if (selectedCheck is not null)
                supportChecks = [new(selectedCheck.ResourceKind, selectedCheck.Action, selectedCheck.Facts)];

            void RequireTimeAndSupport(HumanSupportAccessSourceFacts current, DateTimeOffset now)
            {
                RequireCustody();
                if (!lifetime.IsValidAt(now) || !SameForwarding(originalSupport.Forwarding, current.Forwarding)
                    || !ReferenceEquals(originalSupport.Forwarding.Subject, subject))
                    throw new UnauthorizedAccessException("The original Settings credential or support selection has changed.");
                foreach (var check in supportChecks)
                {
                    RequireSupport(originalSupport, now, check);
                    RequireSupport(current, now, check);
                }
            }
            RequireTimeAndSupport(originalSupport, clock.GetUtcNow());

            HumanAuthorizationRouteDeployment? deployment = null;
            HumanAuthorizationRoute? route = null;
            Cerbos.Api.V1.Engine.Principal? serialized = null;
            AuthorizationDecision? acceptedDecision = null;
            bool localInput = false;
            if (observeRouteOnly)
            {
                if (routeFacts!.SelectedTenantId != selectedTenant || routeFacts.PrincipalId != subject.PrincipalId)
                    throw new UnauthorizedAccessException("The original administrative route selection changed.");
                deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
                route = HumanAuthorizationRouteProjector.Project(routeFacts, deployment, settingsGovernance: true,
                    localDecisionSourcesCoLocated: lifetime.LocalSession is null || principalFacts.LocalSession is not null);
            }
            if (selectedCheck is not null)
            {
                if (routeFacts!.SelectedTenantId != selectedTenant || routeFacts.PrincipalId != subject.PrincipalId)
                    throw new UnauthorizedAccessException("The original Settings route belongs to another actor or tenant.");
                deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
                route = HumanAuthorizationRouteProjector.Project(routeFacts, deployment, settingsGovernance: true,
                    localDecisionSourcesCoLocated: lifetime.LocalSession is null || principalFacts.LocalSession is not null);
                var projection = CerbosPrincipalBuilder.BuildPrincipalFromSources(principalFacts);
                serialized = CerbosPrincipalBuilder.CapturePrincipal(projection);
                localInput = InstancePolicyInput(principalFacts);
                if (route.Provider == HumanAuthorizationRouteProvider.Local)
                    acceptedDecision = localInput ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
                        : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local);
                else if (route.Provider == HumanAuthorizationRouteProvider.CerbosInstance)
                    acceptedDecision = await cerbos.AuthorizePreparedHumanAsync(projection, selectedCheck,
                        ambientTenant, deployment.UsePolicyScope, route.Endpoint!, token);
                if (acceptedDecision?.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                    throw new AuthorizationProviderUnavailableException(selectedCheck.ResourceKind, selectedCheck.Action);
                if (acceptedDecision?.IsAllowed != true)
                    throw new AuthorizationException(selectedCheck.ResourceKind, selectedCheck.Action);
            }
            RequireTimeAndSupport(originalSupport, clock.GetUtcNow());

            void Compare(HumanPrincipalSourceFacts facts, HumanAuthorizationRouteFacts? currentRoute)
            {
                RequirePrincipal(facts);
                if (observeRouteOnly && deployment != HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value))
                    throw new UnauthorizedAccessException("The original administrative route declaration changed.");
                if (routeSource is null) return;
                if (selectedCheck is not null && acceptedDecision?.IsAllowed != true || currentRoute is null
                    || currentRoute.SelectedTenantId != selectedTenant || currentRoute.PrincipalId != subject.PrincipalId)
                    throw new UnauthorizedAccessException("The original selected Settings decision is required.");
                var projected = HumanAuthorizationRouteProjector.Project(currentRoute, deployment!, settingsGovernance: true,
                    localDecisionSourcesCoLocated: lifetime.LocalSession is null || facts.LocalSession is not null);
                bool matches = selectedCheck is null || (route!.Provider == HumanAuthorizationRouteProvider.Local
                    ? localInput == InstancePolicyInput(facts)
                    : serialized!.Equals(CerbosPrincipalBuilder.CapturePrincipal(
                        CerbosPrincipalBuilder.BuildPrincipalFromSources(facts))));
                if (!route!.IsSemanticallyEquivalentTo(projected) || !matches)
                    throw new UnauthorizedAccessException("The original selected Settings policy inputs have changed.");
            }

            async Task RequireAuthorityAsync(CancellationToken cancellationToken)
            {
                bool allowed = authority switch
                {
                    Authority.Subject => await subjectAuthority!.IsSubjectCurrentForCommitAsync(subject.PrincipalId, cancellationToken),
                    Authority.Tenant => await tenantAuthority!.IsTenantAdministratorForCommitAsync(subject.PrincipalId, selectedTenant!.Value, cancellationToken),
                    Authority.TenantOrInstance =>
                        await tenantAuthority!.IsTenantAdministratorForCommitAsync(subject.PrincipalId, selectedTenant!.Value, cancellationToken)
                        || await instanceAuthority!.IsInstanceAdministratorForCommitAsync(subject.PrincipalId, cancellationToken),
                    Authority.Instance => await instanceAuthority!.IsInstanceAdministratorForCommitAsync(subject.PrincipalId, cancellationToken),
                    _ => false
                };
                if (!allowed)
                    throw new UnauthorizedAccessException("The original Settings action's current authority is required.");
            }

            return new Prepared(this, original, effects, coordinatedReadKeys, owner, async cancellationToken =>
            {
                RequireCustody();
                if (requireOriginalRequest is not null) await requireOriginalRequest(cancellationToken);
                var exclusion = await sourceSet.AcquireAsync(cancellationToken);
                if (ordinarySource is not null)
                    await ordinaryStore!.AcquireAsync(ordinarySource, cancellationToken);
                if (federationSource is not null)
                    await federationStore!.AcquireAsync(federationSource, cancellationToken);
                var principal = await principalSource.AcquireAsync(exclusion, cancellationToken);
                var currentRoute = routeSource is null ? null : await routeSource.AcquireAsync(exclusion, cancellationToken);
                var support = await supportSource.AcquireAsync(exclusion, principal, cancellationToken: cancellationToken);
                Compare(principal.Facts, currentRoute?.Facts);
                await RequireAuthorityAsync(cancellationToken);
                requireOriginalFacts?.Invoke(principal.Facts, support.Facts);
                RequireTimeAndSupport(support.Facts, clock.GetUtcNow());
                HumanSupportAccessSourceFacts? finalSupport = null;

                async Task ReadSourcesAsync(CancellationToken finalToken, bool terminal)
                {
                    RequireCustody();
                    // Data awaits precede the original human-source reads and all terminal
                    // deadline checks. No callback can replace the private writer's expected image.
                    if (federationSource is not null)
                    {
                        if (terminal)
                            await federationStore!.RequireCompleteAsync(federationSource, finalToken);
                        else
                            await federationStore!.VerifyAsync(federationSource, finalToken);
                    }
                    if (ordinarySource is not null)
                    {
                        if (terminal)
                            await ordinaryStore!.RequireCompleteAsync(ordinarySource, finalToken);
                        else
                            await ordinaryStore!.VerifyAsync(ordinarySource, finalToken);
                    }
                    // All of these leases are acquired here, never supplied by the caller. Each
                    // reentry enforces the original context/pool/connection/transaction identity.
                    if (requireOriginalRequest is not null) await requireOriginalRequest(finalToken);
                    await exclusion.ValidateAsync(finalToken);
                    var currentPrincipal = await principal.ValidateAsync(finalToken);
                    var finalRoute = currentRoute is null ? null : await currentRoute.ValidateAsync(finalToken);
                    finalSupport = await support.ValidateAsync(finalToken);
                    Compare(currentPrincipal, finalRoute);
                    await RequireAuthorityAsync(finalToken);
                    requireOriginalFacts?.Invoke(currentPrincipal, finalSupport);
                }
                finalizer.Register(token => ReadSourcesAsync(token, terminal: true), now => RequireTimeAndSupport(finalSupport
                    ?? throw new InvalidOperationException("Final Settings sources were not read."), now));
                return async guardToken =>
                {
                    await ReadSourcesAsync(guardToken, terminal: false);
                    RequireTimeAndSupport(finalSupport!, clock.GetUtcNow());
                };
            })
            {
                OrdinarySource = ordinarySource,
                OrdinaryStore = ordinaryStore,
                FederationSource = federationSource,
                FederationStore = federationStore,
                Coordination = coordination
            };
        }
        catch (Exception failure)
        {
            await FinishSettingsAttemptAsync(failure, false, coordination,
                () => { if (federationSource is not null) federationStore!.Expire(federationSource); },
                () => { if (ordinarySource is not null) ordinaryStore!.Expire(ordinarySource); });
            throw;
        }
    }

    public async Task<T> ExecutePreparedAsync<T>(IPreparedHumanSettingsMutation prepared,
        Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var issued = RequireIssued(prepared);
        lock (issued.LifetimeSync)
        {
            if (!issued.OwnsLifetime || issued.Consumed != 0 || issued.Cleanup is not null)
                throw new InvalidOperationException("A Settings preparation belongs to one physical attempt.");
            issued.Consumed = 1;
            issued.Executing = true;
        }
        Exception? failure = null;
        bool settled = false;
        try
        {
            issued.Coordination?.RequireCurrent();
            var result = await issued.Owner.ExecuteAsync(async token =>
            {
                issued.ValidateOwner = await issued.Acquire(token);
                issued.Active = true;
                var previous = _currentOwner.Value;
                _currentOwner.Value = issued;
                try { return await InvokeAsync(issued, mutation, token); }
                finally { _currentOwner.Value = previous; issued.Active = false; }
            }, cancellationToken);
            settled = true;
            issued.Coordination?.RequireCurrent();
            return result;
        }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            issued.Active = false;
            issued.ValidateOwner = null;
            Task cleanup;
            lock (issued.LifetimeSync)
            {
                issued.Executing = false;
                cleanup = CloseOwnedAsync(issued, failure, settled);
            }
            await cleanup;
        }
    }

    public Task CloseAsync(IPreparedHumanSettingsMutation prepared, Exception? primaryFailure)
    {
        var issued = RequireIssued(prepared);
        lock (issued.LifetimeSync)
        {
            if (!issued.OwnsLifetime || issued.Executing || issued.Active)
                throw new UnauthorizedAccessException("Only an inactive original Settings lifetime can close.");
            return CloseOwnedAsync(issued, primaryFailure, false);
        }
    }

    private static Task CloseOwnedAsync(Prepared issued, Exception? failure, bool settled)
    {
        // The first closing owner observes every failure. An outer caller closing
        // after ExecutePrepared must not replay that already-observed cleanup error.
        if (issued.Cleanup?.IsCompleted == true) return Task.CompletedTask;
        issued.Consumed = 1;
        return issued.Cleanup ??= FinishSettingsAttemptAsync(failure, settled, issued.TakeCoordination(),
            () => { if (issued.OrdinarySource is not null) issued.OrdinaryStore!.Expire(issued.OrdinarySource); },
            () => { if (issued.FederationSource is not null) issued.FederationStore!.Expire(issued.FederationSource); },
            () => { if (issued.TypedOriginal is not null) issued.TypedStore!.Close(issued.TypedOriginal); },
            () => { if (issued.ResourceProviderOriginal is not null) issued.ResourceProviderStore!.Close(issued.ResourceProviderOriginal); });
    }

    public Task<T> ExecuteCurrentJoinedAsync<T>(ImmutableArray<HumanSettingsMutation> slice,
        Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default) =>
        ExecuteJoinedAsync(_currentOwner.Value
            ?? throw new UnauthorizedAccessException("An actual original Settings producer is required."),
            slice, mutation, cancellationToken);

    public async Task<T> ExecuteJoinedAsync<T>(IPreparedHumanSettingsMutation owner,
        ImmutableArray<HumanSettingsMutation> slice,
        Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var parent = RequireActive(owner);
        if (slice.IsDefault || slice.Any(effect => !parent.Effects.Contains(effect))
            || slice.Distinct().Count() != slice.Length)
            throw new UnauthorizedAccessException("A joined Settings effect must be captured by its original owner.");
        return await ExecuteDerivedAsync(parent, slice, mutation, cancellationToken);
    }

    private async Task<T> ExecuteDerivedAsync<T>(Prepared parent, ImmutableArray<HumanSettingsMutation> slice,
        Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation, CancellationToken cancellationToken)
    {
        await ValidateActiveAsync(parent, cancellationToken);
        var child = new Prepared(this, parent.Original, slice, CoordinatedKeys(slice.Select(effect => effect.Key)), parent.Owner,
            _ => throw new UnauthorizedAccessException("A joined Settings proof cannot acquire a new owner."))
        {
            Consumed = 1,
            Active = true,
            OwnsLifetime = false,
            OrdinarySource = parent.OrdinarySource,
            OrdinaryStore = parent.OrdinaryStore,
            FederationSource = parent.FederationSource,
            FederationStore = parent.FederationStore,
            ValidateOwner = async token =>
            {
                RequireActive(parent);
                await ValidateActiveAsync(parent, token);
            }
        };
        var previous = _currentOwner.Value;
        _currentOwner.Value = child;
        try { return await InvokeAsync(child, mutation, cancellationToken); }
        finally { _currentOwner.Value = previous; child.Active = false; child.ValidateOwner = null; }
    }

    public async Task<HumanSettingsMutation> RequirePublicationReadAsync(Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        var parent = RequireActive(_currentOwner.Value
            ?? throw new UnauthorizedAccessException("Publication reads require the original Settings owner."));
        var anchor = parent.Effects.FirstOrDefault(effect => effect.TenantId == tenantId
            && effect.Scope == (tenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance)
            && PublicationPolicySettingKeys.All.Contains(effect.Key, StringComparer.Ordinal))
            ?? throw new UnauthorizedAccessException("This owner has no publication-policy read closure.");
        await ValidateActiveAsync(parent, cancellationToken);
        return anchor;
    }

    public async Task<T> ExecutePublicationJoinedAsync<T>(ImmutableArray<string> keys, Guid? tenantId,
        Guid actorUserId, PublicationPolicyLockedSystemBehavior lockedSystemBehavior,
        Func<ImmutableArray<PublicationPolicySettingMutation>, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var parent = RequireActive(_currentOwner.Value
            ?? throw new UnauthorizedAccessException("Publication requires its original Settings producer."));
        if (humanScope.Subject?.PrincipalId != actorUserId || keys.IsDefaultOrEmpty
            || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length
            || !Enum.IsDefined(lockedSystemBehavior)
            || !tenantId.HasValue && lockedSystemBehavior != PublicationPolicyLockedSystemBehavior.Reject)
            throw new UnauthorizedAccessException("Publication requires its original actor and exact keys.");
        await RequirePublicationReadAsync(tenantId, cancellationToken);
        var originals = keys.Select(key => parent.Effects.SingleOrDefault(effect => effect.Key == key
            && effect.TenantId == tenantId && PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
            ?? throw new UnauthorizedAccessException("Publication effects must belong to the original command.")).ToImmutableArray();
        var settings = services.GetRequiredService<ISystemSettingRepository>();
        var derived = ImmutableArray.CreateBuilder<HumanSettingsMutation>();
        var mutations = ImmutableArray.CreateBuilder<PublicationPolicySettingMutation>();
        foreach (var original in originals)
        {
            var row = await settings.GetByKey(original.Key, cancellationToken);
            if (original.Kind == HumanSettingsMutationKind.Remove
                || tenantId.HasValue && original.Kind == HumanSettingsMutationKind.SetValue
                    && row?.IsLocked == true
                    && lockedSystemBehavior == PublicationPolicyLockedSystemBehavior.RemoveOverride)
            {
                derived.Add(original with { Kind = HumanSettingsMutationKind.Remove, Value = null, IsLocked = null });
                mutations.Add(new(original.Key, PublicationPolicyMutationKind.Remove, null, tenantId, null));
                continue;
            }
            if (tenantId.HasValue && original.Kind == HumanSettingsMutationKind.SetLock)
                throw new UnauthorizedAccessException("Publication policy cannot be locked at tenant scope.");
            if (original.Kind == HumanSettingsMutationKind.SetLock && row is null)
                throw new UnauthorizedAccessException("Publication locking requires the original system setting row.");
            string value = original.Kind == HumanSettingsMutationKind.SetValue ? original.Value!
                : row?.Value ?? SettingRegistry.Get(original.Key)!.DefaultValue;
            bool? locked = tenantId.HasValue ? null
                : original.Kind == HumanSettingsMutationKind.SetLock ? original.IsLocked : row?.IsLocked ?? false;
            derived.Add(original with { Kind = HumanSettingsMutationKind.SetValue, Value = value, IsLocked = null });
            if (locked.HasValue)
                derived.Add(original with { Kind = HumanSettingsMutationKind.SetLock, Value = null, IsLocked = locked });
            mutations.Add(new(original.Key, PublicationPolicyMutationKind.Set, value, tenantId, locked));
        }
        return await ExecuteDerivedAsync(parent, derived.ToImmutable(),
            (_, token) => mutation(mutations.ToImmutable(), token), cancellationToken);
    }

    public async Task<T> ExecuteLocationJoinedAsync<T>(string key, SettingScope scope, Guid? tenantId,
        Guid actorUserId,
        Func<IPreparedHumanSettingsMutation, HumanSettingsMutation, string, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var parent = RequireActive(_currentOwner.Value
            ?? throw new UnauthorizedAccessException("Location governance requires the original Settings producer."));
        if (humanScope.Subject?.PrincipalId != actorUserId
            || !LocationPrivacySettingDefinitions.All.Any(item => item.Key == key)
            || scope is not (SettingScope.Instance or SettingScope.Tenant))
            throw new UnauthorizedAccessException("Location governance requires its original actor, scope and key.");
        var effect = parent.Effects.SingleOrDefault(item => item.Key == key && item.Scope == scope
            && item.TenantId == tenantId)
            ?? throw new UnauthorizedAccessException("Location governance must belong to the original command.");
        if (effect.Kind is not (HumanSettingsMutationKind.SetValue or HumanSettingsMutationKind.Remove))
            throw new UnauthorizedAccessException("Location projection governance requires a value or reset action.");
        await ValidateActiveAsync(parent, cancellationToken);
        string effectiveValue = effect.Kind == HumanSettingsMutationKind.SetValue ? effect.Value!
            : scope == SettingScope.Instance ? SettingRegistry.Get(key)!.DefaultValue
            : (await services.GetRequiredService<ISystemSettingRepository>().GetByKey(key, cancellationToken))?.Value
                ?? SettingRegistry.Get(key)!.DefaultValue;
        return await ExecuteDerivedAsync(parent, [effect],
            (proof, token) => mutation(proof, effect, effectiveValue, token), cancellationToken);
    }

    public Task RequireCurrentOwnedAsync(SettingScope scope, Guid? tenantId, Guid? userId, string key,
        HumanSettingsMutationKind kind, string? value = null, bool? isLocked = null,
        CancellationToken cancellationToken = default)
    {
        var issued = RequireActive(_currentOwner.Value
            ?? throw new UnauthorizedAccessException("The original acquired Settings producer is required."));
        if (!issued.Effects.Any(effect => effect.Scope == scope && effect.TenantId == tenantId
                && effect.UserId == userId && effect.Key == key && effect.Kind == kind
                && effect.Value == value && effect.IsLocked == isLocked))
            throw new UnauthorizedAccessException("The exact Settings effect was not admitted by its original owner.");
        return ValidateActiveAsync(issued, cancellationToken);
    }

    public Task RequireOwnedAsync(IPreparedHumanSettingsMutation prepared, HumanSettingsMutation mutation,
        CancellationToken cancellationToken = default)
    {
        var issued = RequireActive(prepared);
        if (!issued.Effects.Contains(mutation))
            throw new UnauthorizedAccessException("The exact Settings key, target and mutation were not prepared.");
        return ValidateActiveAsync(issued, cancellationToken);
    }

    public Task RequireReadAsync(IPreparedHumanSettingsMutation prepared, SettingScope scope, Guid? tenantId,
        Guid? userId, string key, CancellationToken cancellationToken = default)
    {
        var issued = RequireActive(prepared);
        bool permitted = issued.Effects.Any(effect => effect.Scope == scope && effect.TenantId == tenantId
            && effect.UserId == userId && effect.Key == key);
        // Coordinated resource/visitor writers read their fixed source family plus instance
        // ceilings. This never grants across-tenant queries or a general settings enumeration.
        permitted |= issued.CoordinatedReadKeys.Contains(key, StringComparer.Ordinal) && userId is null
            && (scope == SettingScope.Instance && tenantId is null
                || scope == SettingScope.Tenant && tenantId.HasValue
                    && issued.Effects.Any(effect => effect.TenantId == tenantId));
        if (!permitted)
            throw new UnauthorizedAccessException("The Settings owner does not admit this data read.");
        return ValidateActiveAsync(issued, cancellationToken);
    }

    private Prepared RequireIssued(IPreparedHumanSettingsMutation prepared) =>
        prepared is Prepared issued && ReferenceEquals(issued.Issuer, this)
            ? issued : throw new UnauthorizedAccessException("The original scoped Settings issuer is required.");

    private Prepared RequireActive(IPreparedHumanSettingsMutation prepared)
    {
        var issued = RequireIssued(prepared);
        if (!issued.Active || issued.ValidateOwner is null)
            throw new UnauthorizedAccessException("An active acquired Settings owner is required.");
        return issued;
    }

    private async Task ValidateActiveAsync(Prepared issued, CancellationToken token)
    {
        RequireActive(issued);
        await issued.ValidateOwner!(token);
        RequireActive(issued);
    }

    private static async Task<T> InvokeAsync<T>(Prepared issued,
        Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation, CancellationToken token)
    {
        try { return await mutation(issued, token); }
        catch (Exception exception) when (ContainsRollbackMarker(exception))
        {
            // Do not retain the forged marker as InnerException: outer retry owners traverse it.
            throw new InvalidOperationException("A Settings callback cannot issue a physical rollback acknowledgment.");
        }
    }

    private static bool ContainsRollbackMarker(Exception exception) =>
        exception is HumanMutationAttemptRolledBackException
        || exception.InnerException is { } inner && ContainsRollbackMarker(inner)
        || exception is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsRollbackMarker);

    private static bool InstancePolicyInput(HumanPrincipalSourceFacts facts) =>
        facts.PlatformGrants.Any(grant => facts.Roles.Any(role => role.Id == grant.RoleId
            && role.RoleScopeId == (int)RoleScopeEnum.Platform && role.MatchesPlatformAdministratorCode));

    private static bool SameForwarding(HumanSupportForwardingObservation original, HumanSupportForwardingObservation current) =>
        ReferenceEquals(original.Subject, current.Subject) && original.WasForwarded == current.WasForwarded
        && original.WasActive == current.WasActive && original.SessionId == current.SessionId
        && original.ResolvedTenantId == current.ResolvedTenantId;

    private static void RequireSupport(HumanSupportAccessSourceFacts facts, DateTimeOffset now, SupportCheck check)
    {
        if (HumanSupportAccessProjection.BoundaryDenialReason(facts, now, check.Kind, check.Action,
                AuthorizationFactAttributeProjection.ToAttributes(check.Facts)) is not null)
            throw new UnauthorizedAccessException("The original support boundary does not permit this Settings action.");
    }

    // Both ordinary and mixed Settings retain these cooperative resources on their
    // already selected owner. This does not issue human or storage authority.
    internal sealed class SettingsCoordination(
        IHumanSecuritySourceGate human, IEventDiscoveryEnrollmentGate discovery) : IAsyncDisposable
    {
        private IAsyncDisposable? _ownedHuman;
        private IAsyncDisposable? _ownedDiscovery;
        private readonly object _cleanupSync = new();
        private Task? _cleanup;

        internal static async Task<SettingsCoordination> AcquireAsync(IServiceProvider services, CancellationToken token)
        {
            var retained = new SettingsCoordination(services.GetRequiredService<IHumanSecuritySourceGate>(),
                services.GetRequiredService<IEventDiscoveryEnrollmentGate>());
            try
            {
                if (!retained.Human.TryRequireCurrent())
                {
                    if (retained.Discovery.TryRequireCurrent())
                        throw new InvalidOperationException("Settings cannot acquire Human behind retained Discovery custody.");
                    retained._ownedHuman = await retained.Human.AcquireAsync(token);
                }
                if (!retained.Discovery.TryRequireCurrent())
                    retained._ownedDiscovery = await retained.Discovery.AcquireAsync(token);
                retained.RequireCurrent();
                return retained;
            }
            catch (Exception failure)
            {
                await FinishSettingsAttemptAsync(failure, false, retained);
                throw;
            }
        }

        private IHumanSecuritySourceGate Human => human;
        private IEventDiscoveryEnrollmentGate Discovery => discovery;
        internal void RequireCurrent()
        {
            human.RequireCurrent();
            discovery.RequireCurrent();
        }

        public ValueTask DisposeAsync()
        {
            lock (_cleanupSync)
                return new ValueTask(_cleanup ??= ReleaseAsync());
        }

        private async Task ReleaseAsync()
        {
            var ownedDiscovery = Interlocked.Exchange(ref _ownedDiscovery, null);
            var ownedHuman = Interlocked.Exchange(ref _ownedHuman, null);
            List<Exception> failures = [];
            foreach (var owned in new[] { ownedDiscovery, ownedHuman })
            {
                if (owned is null) continue;
                try { await owned.DisposeAsync(); }
                catch (Exception failure) { failures.Add(failure); }
            }
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("Settings owned gate cleanup failed.", failures);
        }
    }

    internal static async Task FinishSettingsAttemptAsync(Exception? primary, bool settled,
        IAsyncDisposable? coordination, params Action[] closeSources)
    {
        List<Exception> failures = [];
        foreach (var close in closeSources)
        {
            try { close(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (coordination is not null)
        {
            try { await coordination.DisposeAsync(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 0) return;
        if (primary is not null) failures.Insert(0, primary);
        var combined = new AggregateException("Settings operation and cleanup evidence.", failures);
        if (primary is HumanMutationOutcomeUnknownException)
            throw new HumanMutationOutcomeUnknownException(combined);
        if (settled)
            throw new InvalidOperationException("settings_committed_cleanup_failed", combined);
        throw combined;
    }

    private enum Authority { Subject, Tenant, Instance, TenantOrInstance }
    private static HumanSettingsOperation OperationFor(SettingScope scope) => scope switch
    {
        SettingScope.User => HumanSettingsOperation.UserSettings,
        SettingScope.Tenant => HumanSettingsOperation.TenantSettings,
        SettingScope.Instance => HumanSettingsOperation.InstanceSettings,
        _ => throw new UnauthorizedAccessException("This setting scope has no ordinary mutation authority.")
    };

    private static HumanSettingsOperation OperationFor(SettingsMutationOperation operation) => operation switch
    {
        SettingsMutationOperation.InstanceSettings => HumanSettingsOperation.InstanceSettings,
        SettingsMutationOperation.TenantSettings => HumanSettingsOperation.TenantSettings,
        SettingsMutationOperation.InstanceTenantSettings => HumanSettingsOperation.InstanceTenantSettings,
        _ => throw new UnauthorizedAccessException("Import requires its producer's original acquired effect proof.")
    };
    private sealed record SupportCheck(string Kind, string Action, IAuthorizationFacts? Facts);

    private sealed class Prepared(HumanSettingsMutationPreparation issuer, object original,
        ImmutableArray<HumanSettingsMutation> effects, ImmutableArray<string> coordinatedReadKeys,
        ISerializableAttemptOwner owner, Func<CancellationToken, Task<Func<CancellationToken, Task>>> acquire)
        : IPreparedHumanTenantPlanAssignment
    {
        public TenantPlanAssignmentSelection PlanSelection => TypedOriginal?.PlanAssignment is not null
            ? ((ITypedTenantPlanOriginalStore)TypedStore!).SelectPlan(TypedOriginal)
            : throw new UnauthorizedAccessException("The original plan selection is required.");
        internal HumanSettingsMutationPreparation Issuer => issuer;
        internal object Original => original;
        internal ImmutableArray<HumanSettingsMutation> Effects { get; set; } = effects;
        internal TypedSettingsOriginal? TypedOriginal { get; init; }
        internal ITypedSettingsOriginalStore? TypedStore { get; init; }
        internal EventResourceProviderOriginal? ResourceProviderOriginal { get; init; }
        internal IEventResourceProviderOriginalStore? ResourceProviderStore { get; init; }
        internal ImmutableArray<string> CoordinatedReadKeys => coordinatedReadKeys;
        internal ISerializableAttemptOwner Owner => owner;
        internal Func<CancellationToken, Task<Func<CancellationToken, Task>>> Acquire => acquire;
        internal Func<CancellationToken, Task>? ValidateOwner { get; set; }
        internal IOrdinarySettingsSource? OrdinarySource { get; init; }
        internal IOrdinarySettingsMutationStore? OrdinaryStore { get; init; }
        internal IAtprotoFederationSettingsSource? FederationSource { get; init; }
        internal IAtprotoFederationSettingsMutationStore? FederationStore { get; init; }
        internal SettingsCoordination? Coordination { get; set; }
        internal object LifetimeSync { get; } = new();
        internal Task? Cleanup { get; set; }
        internal bool Executing { get; set; }
        internal bool OwnsLifetime { get; set; } = true;
        internal Prepared TakeCoordinationFrom(Prepared original)
        {
            Coordination = original.TakeCoordination();
            original.OwnsLifetime = false;
            return this;
        }
        internal SettingsCoordination? TakeCoordination()
        {
            var retained = Coordination;
            Coordination = null;
            return retained;
        }
        internal bool Active { get; set; }
        internal int Consumed;
    }
}
