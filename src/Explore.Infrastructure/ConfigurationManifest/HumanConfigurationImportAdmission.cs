using System.Collections.Immutable;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.ConfigurationManifest;

/// <summary>
/// Prepares the exact import settings action outside its owner. A rejection gets a new
/// selected-provider decision, but never a new subject, credential or support-session selector.
/// </summary>
public sealed class HumanConfigurationImportAdmission(
    IServiceProvider services,
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor httpContext,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    TimeProvider clock,
    CerbosAuthorizationService cerbos,
    IOptions<AuthorizationProviderDeploymentOptions> authorizationOptions,
    IOptions<CerbosSettings> cerbosOptions) : IHumanConfigurationImportAdmission
{
    private readonly AsyncLocal<EffectProof?> current = new();

    public IHumanConfigurationImportEffectProof? CurrentProof =>
        current.Value is { Active: true } proof ? proof : null;

    public async Task<T> ExecuteJoinedAsync<T>(SettingsMutationOperation operation,
        ImmutableArray<HumanConfigurationImportSettingEffect> effects,
        Func<IHumanConfigurationImportEffectProof, CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var parent = RequireActive(CurrentProof);
        if (operation != parent.Apply.Operation || effects.IsDefaultOrEmpty
            || effects.Distinct().Count() != effects.Length
            || effects.Any(effect => !parent.Effects.Contains(effect)))
            throw new UnauthorizedAccessException("The import did not admit these exact joined effects.");
        await ValidateActiveAsync(parent, cancellationToken);
        var child = new EffectProof(this, parent.Apply, effects, async token =>
        {
            if (!parent.Active)
                throw new UnauthorizedAccessException("The original import callback has ended.");
            await parent.ValidateOwner(token);
        });
        current.Value = child;
        try { return await mutation(child, cancellationToken); }
        finally { child.Active = false; current.Value = parent; }
    }

    public Task RequireCurrentOwnedAsync(SettingScope scope, Guid? tenantId, Guid? userId, string key,
        HumanSettingsMutationKind kind, string? value = null, bool? isLocked = null,
        CancellationToken cancellationToken = default)
    {
        if (userId is not null)
            throw new UnauthorizedAccessException("Configuration import does not admit user setting effects.");
        return RequireOwnedAsync(RequireActive(CurrentProof),
            new(scope, tenantId, key, kind, value, isLocked), cancellationToken);
    }

    public Task RequireOwnedAsync(IHumanConfigurationImportEffectProof proof,
        HumanConfigurationImportSettingEffect effect, CancellationToken cancellationToken = default)
    {
        var issued = RequireActive(proof);
        if (!issued.Effects.Contains(effect))
            throw new UnauthorizedAccessException("The exact import setting effect was not admitted.");
        return ValidateActiveAsync(issued, cancellationToken);
    }

    public Task RequireReadAsync(IHumanConfigurationImportEffectProof proof, SettingScope scope, Guid? tenantId,
        string key, CancellationToken cancellationToken = default)
    {
        var issued = RequireActive(proof);
        bool exact = issued.Effects.Any(effect => effect.Scope == scope && effect.TenantId == tenantId
            && effect.Key == key);
        bool coordinated = issued.Effects.Any(effect =>
            (scope == SettingScope.Instance && tenantId is null
                || scope == effect.Scope && tenantId == effect.TenantId)
            && (EventResourceSettingMutationGuard.Handles(effect.Key)
                    && EventResourceSettingMutationGuard.Keys.Contains(key, StringComparer.Ordinal)
                || VisitorAccessCapabilityResolver.AuthoritySettingKeys.Contains(effect.Key, StringComparer.Ordinal)
                    && VisitorAccessCapabilityResolver.AuthoritySettingKeys.Contains(key, StringComparer.Ordinal)));
        if (!exact && !coordinated)
            throw new UnauthorizedAccessException("The import does not admit this exact settings read.");
        return ValidateActiveAsync(issued, cancellationToken);
    }

    public Task RequirePublicationAsync(HumanConfigurationImportPublication publication,
        CancellationToken cancellationToken = default)
    {
        var proof = RequireActive(CurrentProof);
        if (!proof.Apply.Publications.Any(item => ReferenceEquals(item, publication))
            || !ReferenceEquals(publication.Subject, proof.Apply.Subject)
            || publication.OperationId != proof.Apply.OperationId || publication.Target != proof.Apply.Target
            || publication.ArtifactDigest != proof.Apply.ArtifactDigest
            || !ReferenceEquals(publication.Store, proof.Apply.PublicationStore)
            || publication.Mutations.IsDefaultOrEmpty
            || publication.Mutations.Any(mutation =>
                !proof.Effects.Contains(new(mutation.TenantId.HasValue ? SettingScope.Tenant : SettingScope.Instance,
                    mutation.TenantId, mutation.Key, HumanSettingsMutationKind.SetValue, mutation.JsonValue))
                || mutation.IsLocked.HasValue && !proof.Effects.Contains(new(SettingScope.Instance, null,
                    mutation.Key, HumanSettingsMutationKind.SetLock, IsLocked: mutation.IsLocked))))
            throw new UnauthorizedAccessException("Publication requires the original accepted import capsule.");
        return ValidateActiveAsync(proof, cancellationToken);
    }

    private EffectProof RequireActive(IHumanConfigurationImportEffectProof? proof) =>
        proof is EffectProof issued && ReferenceEquals(issued.Issuer, this) && issued.Active
            && ReferenceEquals(current.Value, issued)
            ? issued : throw new UnauthorizedAccessException("The original active import effect issuer is required.");

    private async Task ValidateActiveAsync(EffectProof proof, CancellationToken cancellationToken)
    {
        RequireActive(proof);
        cancellationToken.ThrowIfCancellationRequested();
        await proof.ValidateOwner(cancellationToken);
        RequireActive(proof);
    }

    private sealed class EffectProof(HumanConfigurationImportAdmission issuer, HumanConfigurationImportApply apply,
        ImmutableArray<HumanConfigurationImportSettingEffect> effects, Func<CancellationToken, Task> validateOwner)
        : IHumanConfigurationImportEffectProof
    {
        internal HumanConfigurationImportAdmission Issuer => issuer;
        internal HumanConfigurationImportApply Apply => apply;
        internal ImmutableArray<HumanConfigurationImportSettingEffect> Effects => effects;
        internal Func<CancellationToken, Task> ValidateOwner => validateOwner;
        internal volatile bool Active = true;
    }

    public async Task<IPreparedHumanConfigurationImport> PrepareAsync(
        HumanCommitSubject subject, ConfigurationImportTarget target,
        HumanConfigurationImportAuthority? original, CancellationToken cancellationToken)
        => await PrepareCoreAsync(subject, target, original, HumanConfigurationImportObservationAccess.Update,
            cancellationToken);

    public Task<T> ExecuteSessionAsync<T>(IPreparedHumanConfigurationImport preparedOwner,
        HumanConfigurationImportSessionOperation<T> operation, CancellationToken cancellationToken)
    {
        if (preparedOwner is not Prepared prepared || !ReferenceEquals(prepared.Issuer, this))
            throw new UnauthorizedAccessException("The session requires its exact issuing import admission.");
        return prepared.ExecuteSessionAsync(operation, cancellationToken);
    }

    public async Task<T> ExecuteObservationAsync<T>(HumanConfigurationImportObservation<T> observation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var prepared = await PrepareCoreAsync(observation.Subject, observation.Target, null,
            observation.Access, cancellationToken);
        var factory = services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            as IHumanConfigurationImportOwnerFactory
            ?? throw new InvalidOperationException("Import observation requires its actual selected owner factory.");
        var owner = factory.GetImportSettlementBoundary(prepared.Authority.Lifetime);
        var settlement = await owner.ExecuteAsync(observation, async token =>
        {
            await prepared.AcquireAsync(token);
            prepared.BeginSessionCallback();
            T result;
            try { result = await observation.Observe(prepared, token); }
            catch (Exception exception) when (ContainsRollbackMarker(exception))
            {
                throw new InvalidOperationException("An import observation cannot acknowledge a physical rollback.");
            }
            finally { prepared.EndSessionCallback(); }
            return OriginalOwnerDecision<T>.Commit(result);
        }, cancellationToken);
        return owner.Consume(observation, settlement, OriginalOwnerDisposition.Committed);
    }

    private static bool ContainsRollbackMarker(Exception exception) =>
        exception is HumanMutationAttemptRolledBackException
        || exception.InnerException is { } inner && ContainsRollbackMarker(inner)
        || exception is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsRollbackMarker);

    private async Task<Prepared> PrepareCoreAsync(
        HumanCommitSubject subject, ConfigurationImportTarget target,
        HumanConfigurationImportAuthority? original, HumanConfigurationImportObservationAccess access,
        CancellationToken cancellationToken)
    {
        var request = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated import request is required.");
        var authenticatedPrincipal = request.User;
        void RequireCustody()
        {
            if (!ReferenceEquals(httpContext.HttpContext, request)
                || !ReferenceEquals(request.User, authenticatedPrincipal)
                || !ReferenceEquals(humanScope.Subject, subject))
                throw new UnauthorizedAccessException("The original import request custody has changed.");
        }
        RequireCustody();
        if (!ReferenceEquals(humanScope.Subject, subject)
            || original is not null && (!ReferenceEquals(original.Subject, subject) || original.Target != target))
            throw new UnauthorizedAccessException("Import authority must retain its original selected human and target.");
        var lifetime = original?.Lifetime ?? await lifetimeObserver.ObserveAsync(
            httpContext.HttpContext?.User
                ?? throw new UnauthorizedAccessException("The original authenticated request is required."),
            cancellationToken) ?? throw new UnauthorizedAccessException("Original credential custody is required.");
        if (lifetime.AccountKey != subject.AccountKey || !lifetime.IsValidAt(clock.GetUtcNow()))
            throw new UnauthorizedAccessException("The original import credential has expired.");
        var sourceSet = services.GetRequiredService<IPolicySourceSetFence>();
        var principalSource = services.GetRequiredService<IHumanPrincipalSourceFence>();
        var routeSource = services.GetRequiredService<IHumanAuthorizationRouteSource>();
        var supportSource = await services.GetRequiredService<IHumanSupportAccessSourceFactory>()
            .CreateAsync(cancellationToken);
        var principal = await principalSource.ObserveAsync(cancellationToken);
        var routeFacts = await routeSource.ObserveAsync(cancellationToken);
        var support = await supportSource.ObserveAsync(cancellationToken);
        if (principal.SelectedTenantId != target.TenantId || routeFacts.SelectedTenantId != target.TenantId
            || routeFacts.PrincipalId != subject.PrincipalId)
            throw new UnauthorizedAccessException("Import sources must belong to the original target.");
        if (original is not null && !SameForwarding(original.Forwarding, support.Forwarding))
            throw new UnauthorizedAccessException("The original support-session selector has changed.");
        var custody = original ?? new(subject, lifetime, target, support);
        HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, principal);
        var check = target.Scope == ConfigurationImportScope.Instance
            ? new AuthorizationRequest(ResourceKinds.InstanceSetting,
                CreateInstanceConfigurationImportSessionCommand.ResourceKey,
                access == HumanConfigurationImportObservationAccess.Update
                    ? AuthorizationActions.InstanceSettings.Update : AuthorizationActions.InstanceSettings.View,
                Facts: InstanceScopedAuthorizationFacts.Instance)
            : new AuthorizationRequest(ResourceKinds.TenantSetting,
                CreateTenantConfigurationImportSessionCommand.ResourceKey,
                access == HumanConfigurationImportObservationAccess.Update
                    ? AuthorizationActions.TenantSettings.Update : AuthorizationActions.TenantSettings.View,
                Facts: new TenantSettingAuthorizationFacts(target.TenantId!.Value,
                    CreateTenantConfigurationImportSessionCommand.ResourceKey));
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        var route = HumanAuthorizationRouteProjector.Project(routeFacts, deployment, settingsGovernance: true,
            localDecisionSourcesCoLocated: lifetime.LocalSession is null || principal.LocalSession is not null);
        var observedAt = clock.GetUtcNow();
        RequireSupport(custody.Support, observedAt, check);
        RequireSupport(support, observedAt, check);
        var projection = CerbosPrincipalBuilder.BuildPrincipalFromSources(principal);
        var serialized = CerbosPrincipalBuilder.CapturePrincipal(projection);
        bool localAllowed = LocalAllowed(principal, target, check.Action);
        bool allowed = false;
        if (route.Provider == HumanAuthorizationRouteProvider.Local)
            allowed = localAllowed;
        else if (route.Provider == HumanAuthorizationRouteProvider.CerbosInstance)
        {
            var decision = await cerbos.AuthorizePreparedHumanAsync(projection, check,
                target.TenantId ?? Guid.Empty, deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
            if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                throw new AuthorizationProviderUnavailableException(check.ResourceKind, check.Action);
            allowed = decision.IsAllowed;
        }
        if (!allowed)
            throw new AuthorizationException(check.ResourceKind, check.Action);
        var preparedAt = clock.GetUtcNow();
        if (!lifetime.IsValidAt(preparedAt))
            throw new UnauthorizedAccessException("The original import credential has expired.");
        RequireSupport(custody.Support, preparedAt, check);
        RequireSupport(support, preparedAt, check);
        RequireCustody();
        return new Prepared(this, services, humanScope, clock, custody, check, sourceSet, principalSource, routeSource,
            supportSource, deployment, route, serialized, localAllowed, original is not null, access, RequireCustody);
    }

    private static bool LocalAllowed(HumanPrincipalSourceFacts facts, ConfigurationImportTarget target, string action) =>
        target.Scope == ConfigurationImportScope.Instance
            || action == AuthorizationActions.TenantSettings.View && facts.PlatformGrants.Any(grant =>
                facts.Roles.Any(role => role.Id == grant.RoleId
                    && role.RoleScopeId == (int)RoleScopeEnum.Platform && role.MatchesPlatformAdministratorCode))
            ? facts.PlatformGrants.Any(grant => facts.Roles.Any(role =>
                role.Id == grant.RoleId && role.RoleScopeId == (int)RoleScopeEnum.Platform
                && role.MatchesPlatformAdministratorCode))
            : facts.TenantGrants.Any(grant => grant.TenantId == target.TenantId
                && grant.RoleId == (int)RoleEnum.TenantAdmin && grant.RevokedAt is null
                && facts.TenantMemberships.Any(member => member.Id == grant.TenantUserId
                    && member.TenantId == target.TenantId && !member.IsDeleted
                    && member.StatusId == (int)TenantUserStatusEnum.Active));

    private static bool SameForwarding(HumanSupportForwardingObservation left, HumanSupportForwardingObservation right) =>
        ReferenceEquals(left.Subject, right.Subject) && left.WasForwarded == right.WasForwarded
        && left.WasActive == right.WasActive && left.SessionId == right.SessionId
        && left.ResolvedTenantId == right.ResolvedTenantId;

    private static void RequireSupport(HumanSupportAccessSourceFacts facts, DateTimeOffset now,
        AuthorizationRequest check)
    {
        if (HumanSupportAccessProjection.BoundaryDenialReason(facts, now, check.ResourceKind, check.Action,
                AuthorizationFactAttributeProjection.ToAttributes(check.Facts)) is not null)
            throw new UnauthorizedAccessException("The original support boundary does not permit import.");
    }

    private sealed class Prepared(
        HumanConfigurationImportAdmission issuer,
        IServiceProvider services, IHumanCommitScopePreparation humanScope, TimeProvider clock,
        HumanConfigurationImportAuthority authority, AuthorizationRequest check,
        IPolicySourceSetFence sourceSet,
        IHumanPrincipalSourceFence principalSource, IHumanAuthorizationRouteSource routeSource,
        IHumanSupportAccessSourceFence supportSource, HumanAuthorizationRouteDeployment deployment,
        HumanAuthorizationRoute route, Cerbos.Api.V1.Engine.Principal serialized, bool localAllowed, bool evidenceOnly,
        HumanConfigurationImportObservationAccess access,
        Action requireCustody)
        : IPreparedHumanConfigurationImport
    {
        private int consumed;
        private int applied;
        private int sessionCallback;
        private int sessionExecuting;
        private int sessionFailed;
        private Func<CancellationToken, Task>? validateOwner;
        private Action<DateTimeOffset>? validateTime;
        internal HumanConfigurationImportAdmission Issuer => issuer;
        public HumanConfigurationImportAuthority Authority => authority;

        internal void BeginSessionCallback()
        {
            if (evidenceOnly || validateOwner is null || Interlocked.CompareExchange(ref sessionCallback, 1, 0) != 0)
                throw new UnauthorizedAccessException("Session participation requires its acquired observation.");
        }

        internal void EndSessionCallback() => Interlocked.Exchange(ref sessionCallback, 2);

        private void RequireSessionSettlement()
        {
            if (Volatile.Read(ref sessionFailed) != 0 || Volatile.Read(ref sessionExecuting) != 0)
                throw new InvalidOperationException("A failed or unfinished import session operation cannot commit.");
        }

        internal async Task<T> ExecuteSessionAsync<T>(HumanConfigurationImportSessionOperation<T> operation,
            CancellationToken cancellationToken)
        {
            bool executing = false;
            try
            {
                if (operation is null || evidenceOnly || validateOwner is null
                    || Volatile.Read(ref sessionCallback) != 1
                    || operation.Target != authority.Target
                    || operation.Target.GetSessionStorageKey() != operation.Target.AuthorityKey
                    || operation.Kind is not (HumanConfigurationImportSessionOperationKind.Create
                        or HumanConfigurationImportSessionOperationKind.PreparePreview
                        or HumanConfigurationImportSessionOperationKind.ReadArtifact
                        or HumanConfigurationImportSessionOperationKind.Cancel)
                    || access != HumanConfigurationImportObservationAccess.Update
                        && operation.Kind != HumanConfigurationImportSessionOperationKind.ReadArtifact)
                    throw new UnauthorizedAccessException("The active observation does not permit this session operation.");
                RequireSessionSettlement();
                if (Interlocked.CompareExchange(ref sessionExecuting, 1, 0) != 0)
                    throw new UnauthorizedAccessException("Session operations cannot overlap on one owner.");
                executing = true;
                cancellationToken.ThrowIfCancellationRequested();
                await validateOwner(cancellationToken);
                if (Volatile.Read(ref sessionCallback) != 1)
                    throw new UnauthorizedAccessException("The original observation callback has ended.");
                var result = await operation.Execute(cancellationToken);
                await validateOwner(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (Volatile.Read(ref sessionCallback) != 1)
                    throw new UnauthorizedAccessException("The original observation callback has ended.");
                return result;
            }
            catch
            {
                Interlocked.Exchange(ref sessionFailed, 1);
                throw;
            }
            finally
            {
                if (executing)
                    Interlocked.Exchange(ref sessionExecuting, 0);
            }
        }

        public async Task<ImmutableArray<SettingChangedNotification>> ExecuteApplyAsync(
            HumanConfigurationImportApply apply, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(apply);
            if (evidenceOnly || validateOwner is null || Interlocked.Exchange(ref applied, 1) != 0
                || !ReferenceEquals(apply.Subject, authority.Subject) || apply.Target != authority.Target
                || apply.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(apply.ArtifactDigest)
                || issuer.CurrentProof is not null)
                throw new UnauthorizedAccessException("An import effect requires its original acquired apply attempt.");
            async Task ValidateApplyAsync(CancellationToken token)
            {
                await validateOwner(token);
                await apply.ValidateSourcesAsync(token);
                token.ThrowIfCancellationRequested();
                validateTime!(clock.GetUtcNow());
            }
            await ValidateApplyAsync(cancellationToken);
            var proof = new EffectProof(issuer, apply, apply.Effects, ValidateApplyAsync);
            issuer.current.Value = proof;
            try
            {
                if (apply.PublicationStore is { } store)
                {
                    await store.CompileAsync(apply.Publications, cancellationToken);
                    services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async token =>
                    {
                        await validateOwner(token);
                        await apply.ValidateSourcesAsync(token);
                        await store.RequireCompletedAsync(token);
                    }, instant => validateTime!(instant));
                }
                var result = await apply.ApplyAsync(cancellationToken);
                if (apply.PublicationStore is { } completed)
                    await completed.RequireCompletedAsync(cancellationToken);
                await ValidateApplyAsync(cancellationToken);
                return result;
            }
            finally
            {
                proof.Active = false;
                issuer.current.Value = null;
            }
        }

        public async Task AcquireAsync(CancellationToken cancellationToken)
        {
            requireCustody();
            if (Interlocked.Exchange(ref consumed, 1) != 0)
                throw new InvalidOperationException("An import admission belongs to one physical attempt.");
            if (!ReferenceEquals(humanScope.Subject, authority.Subject))
                throw new UnauthorizedAccessException("The original import subject has changed.");
            var exclusion = await sourceSet.AcquireAsync(cancellationToken);
            var principal = await principalSource.AcquireAsync(exclusion, cancellationToken);
            var currentRoute = await routeSource.AcquireAsync(exclusion, cancellationToken);
            var support = await supportSource.AcquireAsync(exclusion, principal, cancellationToken: cancellationToken);
            Compare(principal.Facts, currentRoute.Facts);
            var acquiredAt = clock.GetUtcNow();
            RequireSupport(authority.Support, acquiredAt, check);
            RequireSelectedSupport(support.Facts, acquiredAt);
            if (!authority.Lifetime.IsValidAt(acquiredAt))
                throw new UnauthorizedAccessException("The original import credential has expired.");
            HumanSupportAccessSourceFacts? finalSupport = null;
            async Task ReadSourcesAsync(CancellationToken token)
            {
                await exclusion.ValidateAsync(token);
                var finalPrincipal = await principal.ValidateAsync(token);
                var finalRoute = await currentRoute.ValidateAsync(token);
                finalSupport = await support.ValidateAsync(token);
                Compare(finalPrincipal, finalRoute);
            }
            void RequireCurrent(DateTimeOffset now)
            {
                requireCustody();
                if (!ReferenceEquals(humanScope.Subject, authority.Subject))
                    throw new UnauthorizedAccessException("The original import subject has changed.");
                if (!authority.Lifetime.IsValidAt(now))
                    throw new UnauthorizedAccessException("The original import credential has expired.");
                RequireSupport(authority.Support, now, check);
                RequireSelectedSupport(finalSupport
                    ?? throw new InvalidOperationException("Final import sources have not been read."), now);
            }
            services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async token =>
            {
                RequireSessionSettlement();
                await ReadSourcesAsync(token);
            }, now =>
            {
                RequireSessionSettlement();
                RequireCurrent(now);
            });
            validateTime = RequireCurrent;
            validateOwner = async token =>
            {
                await ReadSourcesAsync(token);
                RequireCurrent(clock.GetUtcNow());
            };
        }

        private void RequireSelectedSupport(HumanSupportAccessSourceFacts support, DateTimeOffset now)
        {
            if (!SameForwarding(authority.Forwarding, support.Forwarding))
                throw new UnauthorizedAccessException("The original support-session selector has changed.");
            RequireSupport(support, now, check);
        }

        private void Compare(HumanPrincipalSourceFacts principal, HumanAuthorizationRouteFacts facts)
        {
            requireCustody();
            if (!ReferenceEquals(humanScope.Subject, authority.Subject)
                || principal.SelectedTenantId != authority.Target.TenantId
                || facts.SelectedTenantId != authority.Target.TenantId
                || facts.PrincipalId != authority.Subject.PrincipalId)
                throw new UnauthorizedAccessException("The original import subject or target has changed.");
            HumanPrincipalSourceValidation.RequireCurrent(authority.Subject, authority.Lifetime.LocalSession, principal);
            var current = HumanAuthorizationRouteProjector.Project(facts, deployment, true,
                authority.Lifetime.LocalSession is null || principal.LocalSession is not null);
            bool matches = route.Provider == HumanAuthorizationRouteProvider.Local
                ? localAllowed == LocalAllowed(principal, authority.Target, check.Action)
                : serialized.Equals(CerbosPrincipalBuilder.CapturePrincipal(
                    CerbosPrincipalBuilder.BuildPrincipalFromSources(principal)));
            if (!route.IsSemanticallyEquivalentTo(current) || !matches)
                throw new UnauthorizedAccessException("The original import policy inputs have changed.");
        }
    }
}
