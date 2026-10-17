using System.Collections.Immutable;
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
    TimeProvider clock) : IHumanSettingsMutationPreparation
{
    private readonly AsyncLocal<Prepared?> _currentOwner = new();

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(UpdateLocalizationGovernanceCommand command,
        CancellationToken cancellationToken = default) =>
        PrepareCoreAsync(command, SettingScope.Instance, null, Authority.Instance, null,
            (_, _) => OrdinarySettingsMutation.CaptureLocalization(command), [], cancellationToken);

    public IOrdinarySettingsSource? CurrentOrdinarySource =>
        _currentOwner.Value is { Active: true } current ? current.OrdinarySource : null;

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
            [], token);
        var writer = services.GetRequiredService<IStorageConfigurationWriter>();
        var finalizer = services.GetRequiredService<IHumanActionCommitFinalizer>();
        return new Prepared(this, mutation, issued.Effects, issued.CoordinatedReadKeys, issued.Owner, async ct =>
        {
            var validate = await issued.Acquire(ct);
            // Mandatory before any caller callback can escape. An equal generic/native
            // scalar write cannot mark this private selected writer complete.
            finalizer.Register(final => writer.RequireAppliedAsync(mutation, final), _ =>
            {
                mutation.RequireActive();
                mutation.Source.RequireTime();
            });
            return validate;
        });
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
        });
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

    private async Task<T> ExecuteDerivedAsync<T>(Prepared parent, ImmutableArray<HumanSettingsMutation> slice,
        Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation, CancellationToken cancellationToken)
    {
        await ValidateActiveAsync(parent, cancellationToken);
        var child = new Prepared(this, parent.Original, slice, CoordinatedKeys(slice.Select(effect => effect.Key)), parent.Owner,
            _ => throw new UnauthorizedAccessException("A joined Settings proof cannot acquire a new owner."))
        {
            Consumed = 1,
            Active = true,
            OrdinarySource = parent.OrdinarySource,
            OrdinaryStore = parent.OrdinaryStore,
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

    private async Task<IPreparedHumanSettingsMutation> PrepareCoreAsync(object original, SettingScope scope,
        Guid? explicitTenant, Authority? explicitAuthority, Guid? auditActor,
        Func<Guid?, Guid, ImmutableArray<HumanSettingsMutation>> capture,
        ImmutableArray<string> coordinatedReadKeys, CancellationToken token)
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
        var supportSource = await services.GetRequiredService<IHumanSupportAccessSourceFactory>().CreateAsync(token);
        var finalizer = services.GetRequiredService<IHumanActionCommitFinalizer>();
        var owner = services.GetRequiredService<ISerializableAttemptOwner>();
        var subjectAuthority = authority == Authority.Subject ? services.GetRequiredService<IHumanSubjectCommitAuthority>() : null;
        var tenantAuthority = authority is Authority.Tenant or Authority.TenantOrInstance
            ? services.GetRequiredService<ITenantAdministratorCommitAuthority>() : null;
        var instanceAuthority = authority is Authority.Instance or Authority.TenantOrInstance
            ? services.GetRequiredService<IInstanceAdministratorCommitAuthority>() : null;
        var principalFacts = await principalSource.ObserveAsync(token);
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
        var ordinaryStore = ordinary is null ? null : services.GetRequiredService<IOrdinarySettingsMutationStore>();
        var ordinarySource = ordinaryStore is null ? null
            : await ordinaryStore.ObserveAsync(ordinary!, subject, token);

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

        void Compare(HumanPrincipalSourceFacts facts) => RequirePrincipal(facts);

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
            var exclusion = await sourceSet.AcquireAsync(cancellationToken);
            if (ordinarySource is not null)
                await ordinaryStore!.AcquireAsync(ordinarySource, cancellationToken);
            var principal = await principalSource.AcquireAsync(exclusion, cancellationToken);
            var support = await supportSource.AcquireAsync(exclusion, principal, cancellationToken: cancellationToken);
            Compare(principal.Facts);
            await RequireAuthorityAsync(cancellationToken);
            RequireTimeAndSupport(support.Facts, clock.GetUtcNow());
            HumanSupportAccessSourceFacts? finalSupport = null;

            async Task ReadSourcesAsync(CancellationToken finalToken, bool terminal)
            {
                RequireCustody();
                // Data awaits precede the original human-source reads and all terminal
                // deadline checks. No callback can replace the private writer's expected image.
                if (ordinarySource is not null)
                {
                    if (terminal)
                        await ordinaryStore!.RequireCompleteAsync(ordinarySource, finalToken);
                    else
                        await ordinaryStore!.VerifyAsync(ordinarySource, finalToken);
                }
                // All of these leases are acquired here, never supplied by the caller. Each
                // reentry enforces the original context/pool/connection/transaction identity.
                await exclusion.ValidateAsync(finalToken);
                var currentPrincipal = await principal.ValidateAsync(finalToken);
                finalSupport = await support.ValidateAsync(finalToken);
                Compare(currentPrincipal);
                await RequireAuthorityAsync(finalToken);
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
            OrdinaryStore = ordinaryStore
        };
    }

    public async Task<T> ExecutePreparedAsync<T>(IPreparedHumanSettingsMutation prepared,
        Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var issued = RequireIssued(prepared);
        if (Interlocked.Exchange(ref issued.Consumed, 1) != 0)
            throw new InvalidOperationException("A Settings preparation belongs to one physical attempt.");
        try
        {
            return await issued.Owner.ExecuteAsync(async token =>
            {
                issued.ValidateOwner = await issued.Acquire(token);
                issued.Active = true;
                var previous = _currentOwner.Value;
                _currentOwner.Value = issued;
                try { return await InvokeAsync(issued, mutation, token); }
                finally { _currentOwner.Value = previous; issued.Active = false; }
            }, cancellationToken);
        }
        finally
        {
            issued.Active = false;
            issued.ValidateOwner = null;
            if (issued.OrdinarySource is not null)
                issued.OrdinaryStore!.Expire(issued.OrdinarySource);
        }
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

    private enum Authority { Subject, Tenant, Instance, TenantOrInstance }
    private static HumanSettingsOperation OperationFor(SettingScope scope) => scope switch
    {
        SettingScope.User => HumanSettingsOperation.UserSettings,
        SettingScope.Tenant => HumanSettingsOperation.TenantSettings,
        SettingScope.Instance => HumanSettingsOperation.InstanceSettings,
        _ => throw new UnauthorizedAccessException("This setting scope has no ordinary mutation authority.")
    };

    private sealed record SupportCheck(string Kind, string Action, IAuthorizationFacts? Facts);

    private sealed class Prepared(HumanSettingsMutationPreparation issuer, object original,
        ImmutableArray<HumanSettingsMutation> effects, ImmutableArray<string> coordinatedReadKeys,
        ISerializableAttemptOwner owner, Func<CancellationToken, Task<Func<CancellationToken, Task>>> acquire)
        : IPreparedHumanSettingsMutation
    {
        internal HumanSettingsMutationPreparation Issuer => issuer;
        internal object Original => original;
        internal ImmutableArray<HumanSettingsMutation> Effects => effects;
        internal ImmutableArray<string> CoordinatedReadKeys => coordinatedReadKeys;
        internal ISerializableAttemptOwner Owner => owner;
        internal Func<CancellationToken, Task<Func<CancellationToken, Task>>> Acquire => acquire;
        internal Func<CancellationToken, Task>? ValidateOwner { get; set; }
        internal IOrdinarySettingsSource? OrdinarySource { get; init; }
        internal IOrdinarySettingsMutationStore? OrdinaryStore { get; init; }
        internal bool Active { get; set; }
        internal int Consumed;
    }
}
