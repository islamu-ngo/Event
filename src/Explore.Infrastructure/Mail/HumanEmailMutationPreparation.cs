using System.Collections.Immutable;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Settings;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Mail;

/// <summary>
/// Complete source admission on the selected EmailInstance/EmailTenant owner. Original policy
/// choices finish while idle. No ordinary source fallback, policy call or retry occurs under locks.
/// </summary>
public sealed partial class HumanEmailMutationPreparation(
    IHumanEmailSourceFactory sources,
    IHumanCommitScopePreparation humanScope,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    IHttpContextAccessor httpContext,
    ITenantContext tenantContext,
    ISerializableAttemptOwner owner,
    ISettingMutationLock mutationLock,
    IHumanActionCommitFinalizer finalizer,
    IInstanceAdministratorCommitAuthority instanceAuthority,
    ITenantAdministratorCommitAuthority tenantAuthority,
    TimeProvider clock,
    CerbosAuthorizationService cerbos,
    IOptions<AuthorizationProviderDeploymentOptions> authorizationOptions,
    IOptions<CerbosSettings> cerbosOptions,
    Func<IHumanSecuritySourceGate> securityGates,
    IServiceProvider services) : IHumanEmailMutationPreparation, IAsyncDisposable
{
    private readonly AsyncLocal<Prepared?> _current = new();
    private readonly HashSet<Prepared> _issued = [];
    private bool _disposed;

    public Task<IPreparedHumanEmailMutation> PreparePreviewAsync(
        Guid? tenantId, CancellationToken cancellationToken = default) =>
        PrepareAsync(Operation.Preview, tenantId, [], null, null, null, cancellationToken);

    public Task<IPreparedHumanEmailMutation> PrepareApplyAsync(
        ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        CancellationToken cancellationToken = default) =>
        PrepareAsync(Operation.Apply, null, mutations, null, actorUserId, null, cancellationToken);

    public Task<IPreparedHumanEmailMutation> PrepareDisableAsync(
        EmailDeliveryDisableConfirmation confirmation, CancellationToken cancellationToken = default) =>
        PrepareAsync(Operation.Disable, confirmation.TenantId, [], confirmation,
            confirmation.ActorUserId, null, cancellationToken);

    public Task<IPreparedHumanEmailMutation> PrepareAsync(SetInstanceTenantSettingCommand command, string storedValue,
        CancellationToken cancellationToken = default) =>
        PrepareSelectedAsync(command, new(command.TenantId, command.Key,
            EmailDeliverySettingMutationKind.SetValue, storedValue), cancellationToken);

    public Task<IPreparedHumanEmailMutation> PrepareAsync(LockInstanceTenantSettingCommand command,
        CancellationToken cancellationToken = default) =>
        PrepareSelectedAsync(command, new(command.TenantId, command.Key,
            EmailDeliverySettingMutationKind.SetLock, IsLocked: true), cancellationToken);

    public Task<IPreparedHumanEmailMutation> PrepareAsync(UnlockInstanceTenantSettingCommand command,
        CancellationToken cancellationToken = default) =>
        PrepareSelectedAsync(command, new(command.TenantId, command.Key,
            EmailDeliverySettingMutationKind.SetLock, IsLocked: false), cancellationToken);

    private Task<IPreparedHumanEmailMutation> PrepareSelectedAsync(ISecureRequest command,
        EmailDeliverySettingMutation effect, CancellationToken token) =>
        PrepareAsync(Operation.InstanceTenantApply, effect.TenantId, [effect], null, null,
            new(ResourceKinds.InstanceSetting, command.ResourceId!,
                AuthorizationActions.InstanceSettings.Update, Facts: command.AuthorizationFacts), token);

    private async Task<IPreparedHumanEmailMutation> PrepareAsync(Operation operation, Guid? target,
        ImmutableArray<EmailDeliverySettingMutation> effects, EmailDeliveryDisableConfirmation? confirmation,
        Guid? auditActor, AuthorizationRequest? selectedCheck, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (target == Guid.Empty || auditActor == Guid.Empty || effects.IsDefault
            || effects.Any(effect => effect is null || !EmailDeliverySettingKeys.Contains(effect.Key)
                || effect.TenantId == Guid.Empty || !Enum.IsDefined(effect.Kind))
            || effects.Select(effect => (effect.TenantId, effect.Key)).Distinct().Count() != effects.Length)
            throw new ArgumentException("Email admission requires an exact immutable original effect.");
        await humanScope.PrepareAsync(token);
        var securityGate = securityGates();
        var ownedSecurityGate = securityGate.TryRequireCurrent()
            ? null : await securityGate.AcquireAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            securityGate.RequireCurrent();
            var prepared = await PrepareRetainedAsync(operation, target, effects, confirmation,
                auditActor, selectedCheck, securityGate, ownedSecurityGate, token);
            lock (_issued)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _issued.Add(prepared);
            }
            return prepared;
        }
        catch
        {
            if (ownedSecurityGate is not null) await ownedSecurityGate.DisposeAsync();
            throw;
        }
    }

    private async Task<Prepared> PrepareRetainedAsync(Operation operation, Guid? target,
        ImmutableArray<EmailDeliverySettingMutation> effects, EmailDeliveryDisableConfirmation? confirmation,
        Guid? auditActor, AuthorizationRequest? selectedCheck, IHumanSecuritySourceGate securityGate,
        IAsyncDisposable? ownedSecurityGate, CancellationToken token)
    {
        var source = await sources.CreateAsync(token);
        var subject = source.Subject;
        var lifetime = source.Lifetime;
        var request = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated Email request is required.");
        var principal = request.User;
        if (!ReferenceEquals(await lifetimeObserver.ObserveAsync(principal, token), lifetime))
            throw new UnauthorizedAccessException("Email requires its originally validated credential observation.");
        Guid ambientTenant = tenantContext.TenantId;
        Guid? selectedTenant = source.SelectedTenantId;
        if (operation == Operation.Apply)
            target = selectedTenant;
        if (!ReferenceEquals(humanScope.Subject, subject) || subject.AccountKey != lifetime.AccountKey
            || auditActor.HasValue && auditActor != subject.PrincipalId || target != selectedTenant
            || (source.Purpose == HumanEmailSourcePurpose.Tenant) != selectedTenant.HasValue
            || selectedTenant.HasValue && selectedTenant != ambientTenant
            || effects.Any(effect => effect.TenantId != selectedTenant)
            || operation == Operation.InstanceTenantApply && (!selectedTenant.HasValue || selectedCheck is null))
            throw new UnauthorizedAccessException("The Email effect must belong to its original selected owner.");

        void RequireCustody()
        {
            securityGate.RequireCurrent();
            if (!ReferenceEquals(humanScope.Subject, subject) || tenantContext.TenantId != ambientTenant
                || !ReferenceEquals(httpContext.HttpContext, request) || !ReferenceEquals(request.User, principal))
                throw new UnauthorizedAccessException("The original Email request custody has changed.");
        }

        var preparation = await source.PrepareAsync(selectedCheck is not null, token);
        var original = preparation.Facts;
        string kind = selectedCheck?.ResourceKind
            ?? (selectedTenant.HasValue ? ResourceKinds.TenantSetting : ResourceKinds.InstanceSetting);
        string action = selectedCheck?.Action
            ?? (operation == Operation.Preview ? AuthorizationActions.View : AuthorizationActions.Update);
        IAuthorizationFacts supportFacts = selectedCheck?.Facts
            ?? (selectedTenant is { } tenant ? new TenantScopedAuthorizationFacts(tenant)
                : InstanceScopedAuthorizationFacts.Instance);
        var supportAttributes = AuthorizationFactAttributeProjection.ToAttributes(supportFacts);

        void RequirePrincipal(HumanEmailSourceFacts current)
        {
            RequireCustody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, current.Principal);
            if (current.Principal.SelectedTenantId != selectedTenant
                || !ReferenceEquals(current.Support.Forwarding, source.Forwarding)
                || !ReferenceEquals(source.Forwarding.Subject, subject))
                throw new UnauthorizedAccessException("The original Email source selection has changed.");
        }

        void RequireTime(HumanEmailSourceFacts current, DateTimeOffset now)
        {
            RequireCustody();
            if (!lifetime.IsValidAt(now)
                || HumanSupportAccessProjection.BoundaryDenialReason(original.Support, now, kind, action, supportAttributes) is not null
                || HumanSupportAccessProjection.BoundaryDenialReason(current.Support, now, kind, action, supportAttributes) is not null)
                throw new UnauthorizedAccessException("The original Email credential or support boundary no longer permits this action.");
        }

        RequirePrincipal(original);
        RequireTime(original, clock.GetUtcNow());
        HumanAuthorizationRouteDeployment? deployment = null;
        HumanAuthorizationRoute? route = null;
        Cerbos.Api.V1.Engine.Principal? serialized = null;
        AuthorizationDecision? decision = null;
        bool localInput = false;
        if (selectedCheck is not null)
        {
            if (original.Route is not { } originalRoute || originalRoute.SelectedTenantId != selectedTenant
                || originalRoute.PrincipalId != subject.PrincipalId)
                throw new UnauthorizedAccessException("The original Email selected route is required.");
            deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
            route = HumanAuthorizationRouteProjector.Project(originalRoute, deployment, settingsGovernance: true,
                localDecisionSourcesCoLocated: lifetime.LocalSession is null || original.Principal.LocalSession is not null);
            var projection = CerbosPrincipalBuilder.BuildPrincipalFromSources(original.Principal);
            serialized = CerbosPrincipalBuilder.CapturePrincipal(projection);
            localInput = InstancePolicyInput(original.Principal);
            if (route.Provider == HumanAuthorizationRouteProvider.Local)
                decision = localInput ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
                    : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local);
            else if (route.Provider == HumanAuthorizationRouteProvider.CerbosInstance)
                decision = await cerbos.AuthorizePreparedHumanAsync(projection, selectedCheck,
                    ambientTenant, deployment.UsePolicyScope, route.Endpoint!, token);
            if (decision?.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                throw new AuthorizationProviderUnavailableException(selectedCheck.ResourceKind, selectedCheck.Action);
            if (decision?.IsAllowed != true)
                throw new AuthorizationException(selectedCheck.ResourceKind, selectedCheck.Action);
        }
        RequireTime(original, clock.GetUtcNow());

        void Compare(HumanEmailSourceFacts current)
        {
            RequirePrincipal(current);
            if (selectedCheck is null) return;
            if (decision?.IsAllowed != true || current.Route is not { } currentRoute
                || currentRoute.SelectedTenantId != selectedTenant || currentRoute.PrincipalId != subject.PrincipalId)
                throw new UnauthorizedAccessException("The original selected Email decision is required.");
            var projected = HumanAuthorizationRouteProjector.Project(currentRoute, deployment!, settingsGovernance: true,
                localDecisionSourcesCoLocated: lifetime.LocalSession is null || current.Principal.LocalSession is not null);
            bool matches = route!.Provider == HumanAuthorizationRouteProvider.Local
                ? localInput == InstancePolicyInput(current.Principal)
                : serialized!.Equals(CerbosPrincipalBuilder.CapturePrincipal(
                    CerbosPrincipalBuilder.BuildPrincipalFromSources(current.Principal)));
            if (!route.IsSemanticallyEquivalentTo(projected) || !matches)
                throw new UnauthorizedAccessException("The originally selected Email policy inputs have changed.");
        }

        async Task RequireAuthorityAsync(CancellationToken ct)
        {
            // Apply preserves tenant-admin-only; preview/disable preserve the existing
            // instance-admin OR current-tenant-admin disjunction. Selected instance-tenant
            // actions retain instance-admin-only plus their actual policy decision.
            bool allowed = operation == Operation.Apply && selectedTenant.HasValue
                ? await tenantAuthority.IsTenantAdministratorForCommitAsync(subject.PrincipalId, selectedTenant.Value, ct)
                : await instanceAuthority.IsInstanceAdministratorForCommitAsync(subject.PrincipalId, ct)
                    || operation is Operation.Preview or Operation.Disable && selectedTenant.HasValue
                        && await tenantAuthority.IsTenantAdministratorForCommitAsync(subject.PrincipalId, selectedTenant.Value, ct);
            if (!allowed)
                throw new UnauthorizedAccessException("The original Email action's administrator authority is required.");
        }

        return new Prepared(this, operation, selectedTenant, subject.PrincipalId, effects, confirmation,
            securityGate, ownedSecurityGate, async ct =>
        {
            RequireCustody();
            var held = await source.AcquireAsync(preparation, ct);
            Compare(held.Facts);
            await RequireAuthorityAsync(ct);
            RequireTime(held.Facts, clock.GetUtcNow());
            HumanEmailSourceFacts? terminal = null;
            async Task ReadSourcesAsync(CancellationToken finalToken)
            {
                terminal = await source.RequireOwnedAsync(held, subject, lifetime, source.Purpose, selectedTenant, finalToken);
                Compare(terminal);
                await RequireAuthorityAsync(finalToken);
            }
            finalizer.Register(ReadSourcesAsync, now => RequireTime(terminal
                ?? throw new InvalidOperationException("Terminal Email source reads did not finish."), now));
            return async guardToken =>
            {
                await ReadSourcesAsync(guardToken);
                RequireTime(terminal!, clock.GetUtcNow());
            };
        });
    }

    public async Task<T> ExecutePreparedAsync<T>(IPreparedHumanEmailMutation preparation,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var issued = RequireIssued(preparation);
        if (Interlocked.Exchange(ref issued.Consumed, 1) != 0)
            throw new InvalidOperationException("Email preparation belongs to one physical attempt.");
        try
        {
            issued.SecurityGate.RequireCurrent();
            return await mutationLock.ExecuteOrderedGroupsAsync([EmailDeliverySettingKeys.All],
                token => owner.ExecuteAsync(async inner =>
                {
                    issued.Validate = await issued.Acquire(inner);
                    issued.Active = true;
                    var previous = _current.Value;
                    _current.Value = issued;
                    try { return await operation(inner); }
                    catch (Exception exception) when (ContainsRollbackMarker(exception))
                    {
                        throw new InvalidOperationException("An Email callback cannot certify physical rollback.");
                    }
                    finally
                    {
                        _current.Value = previous;
                        issued.Active = false;
                        issued.Validate = null;
                    }
                }, token), cancellationToken);
        }
        finally
        {
            await ReleaseSecurityGateAsync(issued);
        }
    }

    public async Task<T> ExecuteApplyAsync<T>(ImmutableArray<EmailDeliverySettingMutation> mutations,
        Guid? actorUserId, bool instanceTenantAction, Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        if (_current.Value is not null)
        {
            await RequireApplyAsync(mutations, actorUserId, instanceTenantAction, cancellationToken);
            return await operation(cancellationToken);
        }
        if (instanceTenantAction)
            throw new UnauthorizedAccessException("An instance-tenant Email write requires its original selected-policy proof.");
        var prepared = await PrepareApplyAsync(mutations, actorUserId, cancellationToken);
        return await ExecutePreparedAsync(prepared, operation, cancellationToken);
    }

    public async Task<T> ExecuteDisableAsync<T>(EmailDeliveryDisableConfirmation confirmation,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        if (_current.Value is not null)
        {
            await RequireDisableAsync(confirmation, cancellationToken);
            return await operation(cancellationToken);
        }
        var prepared = await PrepareDisableAsync(confirmation, cancellationToken);
        return await ExecutePreparedAsync(prepared, operation, cancellationToken);
    }

    public Task<Guid> RequireApplyAsync(ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId,
        bool instanceTenantAction, CancellationToken cancellationToken = default)
    {
        var issued = RequireCurrent();
        if (issued.Operation != (instanceTenantAction ? Operation.InstanceTenantApply : Operation.Apply)
            || actorUserId.HasValue && actorUserId != issued.Actor || mutations.IsDefault
            || !issued.Effects.SequenceEqual(mutations))
            throw new UnauthorizedAccessException("The exact original Email configuration effect is required.");
        return ValidateAsync(issued, cancellationToken);
    }

    public Task<Guid> RequireDisableAsync(EmailDeliveryDisableConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        var issued = RequireCurrent();
        if (issued.Operation != Operation.Disable || issued.Confirmation != confirmation)
            throw new UnauthorizedAccessException("The exact original Email disable confirmation is required.");
        return ValidateAsync(issued, cancellationToken);
    }

    public Task<Guid> RequireImpactAsync(Guid? tenantId, CancellationToken cancellationToken = default)
    {
        var issued = RequireCurrent();
        if (issued.Operation is not (Operation.Preview or Operation.Disable) || issued.Tenant != tenantId)
            throw new UnauthorizedAccessException("An original Email preview or disable proof for this scope is required.");
        return ValidateAsync(issued, cancellationToken);
    }

    private Prepared RequireIssued(IPreparedHumanEmailMutation preparation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return preparation is Prepared issued && ReferenceEquals(issued.Issuer, this)
            ? issued : throw new UnauthorizedAccessException("The original private Email producer is required.");
    }

    private async ValueTask ReleaseSecurityGateAsync(Prepared issued)
    {
        lock (_issued) _issued.Remove(issued);
        var owned = Interlocked.Exchange(ref issued.OwnedSecurityGate, null);
        if (owned is not null) await owned.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Prepared[] abandoned;
        lock (_issued)
        {
            _disposed = true;
            abandoned = _issued.ToArray();
        }
        List<Exception>? failures = null;
        foreach (var issued in abandoned)
        {
            try { await ReleaseSecurityGateAsync(issued); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
        if (failures is not null) throw new AggregateException("Email source-gate cleanup failed.", failures);
    }

    private Prepared RequireCurrent()
    {
        var issued = _current.Value
            ?? throw new UnauthorizedAccessException("An acquired original Email effect proof is required.");
        RequireIssued(issued);
        if (!issued.Active || issued.Validate is null)
            throw new UnauthorizedAccessException("The original Email effect proof is no longer active.");
        return issued;
    }

    private async Task<Guid> ValidateAsync(Prepared issued, CancellationToken token)
    {
        await issued.Validate!(token);
        if (!ReferenceEquals(RequireCurrent(), issued))
            throw new UnauthorizedAccessException("The original Email effect owner changed.");
        return issued.Actor;
    }

    private static bool InstancePolicyInput(HumanPrincipalSourceFacts facts) =>
        facts.PlatformGrants.Any(grant => facts.Roles.Any(role => role.Id == grant.RoleId
            && role.RoleScopeId == (int)RoleScopeEnum.Platform && role.MatchesPlatformAdministratorCode));

    private static bool ContainsRollbackMarker(Exception exception) =>
        exception is HumanMutationAttemptRolledBackException
        || exception.InnerException is { } inner && ContainsRollbackMarker(inner)
        || exception is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsRollbackMarker);

    private enum Operation { Preview, Apply, InstanceTenantApply, Disable }

    private sealed class Prepared(HumanEmailMutationPreparation issuer, Operation operation, Guid? tenant,
        Guid actor, ImmutableArray<EmailDeliverySettingMutation> effects, EmailDeliveryDisableConfirmation? confirmation,
        IHumanSecuritySourceGate securityGate, IAsyncDisposable? ownedSecurityGate,
        Func<CancellationToken, Task<Func<CancellationToken, Task>>> acquire) : IPreparedHumanEmailMutation
    {
        internal HumanEmailMutationPreparation Issuer => issuer;
        internal Operation Operation => operation;
        internal Guid? Tenant => tenant;
        internal Guid Actor => actor;
        internal ImmutableArray<EmailDeliverySettingMutation> Effects => effects;
        internal EmailDeliveryDisableConfirmation? Confirmation => confirmation;
        internal IHumanSecuritySourceGate SecurityGate => securityGate;
        internal IAsyncDisposable? OwnedSecurityGate = ownedSecurityGate;
        internal Func<CancellationToken, Task<Func<CancellationToken, Task>>> Acquire => acquire;
        internal Func<CancellationToken, Task>? Validate { get; set; }
        internal bool Active { get; set; }
        internal int Consumed;
    }
}

public static class HumanEmailMutationPreparationRegistration
{
    public static IServiceCollection AddHumanEmailMutationPreparation(this IServiceCollection services)
    {
        services.AddScoped<IHumanEmailMutationPreparation, HumanEmailMutationPreparation>();
        return services;
    }
}
