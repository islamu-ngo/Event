using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Collections.Immutable;
using System.Security.Claims;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Original human webhook requester preparation. Source construction follows canonical scope
/// selection; provider I/O precedes transactions. No assignment source is needed: the original
/// webhook principal has no Event enrichment and its Local branch reads no Event assignments.
/// </summary>
public sealed class HumanWebhookMutationPreparation(
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor httpContext,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    ITenantContext tenantContext,
    TimeProvider clock,
    CerbosAuthorizationService cerbos,
    IOptions<AuthorizationProviderDeploymentOptions> authorizationOptions,
    IOptions<CerbosSettings> cerbosOptions,
    Func<IHumanPrincipalSourceFence> resolvePrincipal,
    Func<IHumanAuthorizationRouteSource> resolveRoute,
    Func<IHumanSupportAccessSourceFactory> resolveSupport,
    Func<IHumanWebhookResourceSourceFactory> resolveWebhooks,
    Func<IPolicySourceSetFence> resolveExclusion,
    Func<IHumanActionCommitFinalizer> resolveFinalizer,
    Func<ISerializableAttemptOwner> resolveOwner) : IHumanWebhookMutationPreparation
{
    public Task<T> ExecuteAsync<T>(ISecureRequest command, Func<CancellationToken, Task<T>> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        return ExecuteAsync(command, async (prepared, token) =>
        {
            await prepared.AcquireAsync(command, token);
            return await mutation(token);
        }, cancellationToken);
    }

    public async Task<T> ExecuteAsync<T>(ISecureRequest command,
        Func<IPreparedHumanWebhookMutation, CancellationToken, Task<T>> acquireAndMutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acquireAndMutate);
        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = await PrepareAsync(command, cancellationToken);
            var owner = resolveOwner();
            try
            {
                return await owner.ExecuteAsync(async token =>
                {
                    try
                    {
                        var result = await acquireAndMutate(prepared, token);
                        if (!((Prepared)prepared).Acquired)
                            throw new InvalidOperationException("The original webhook capability must be acquired before commit.");
                        if (result is BaseCommandResponse<Guid> { IsSuccess: false })
                            throw new RejectedMutation<T>(result);
                        return result;
                    }
                    catch (Exception exception) when (ContainsBorrowedReceipt(exception))
                    {
                        // Strip the ENTIRE chain. The owner recursively classifies provider
                        // conflicts; retaining an inner receipt would launder a nested rollback.
                        throw new InvalidOperationException(
                            "A callback cannot certify rollback of the original webhook owner.");
                    }
                }, cancellationToken);
            }
            catch (HumanMutationAttemptRolledBackException) when (attempt < 4)
            {
                // Only the actual owner's acknowledged receipt reaches here. Reobserve and
                // obtain a fresh selected-provider decision outside the disposed transaction.
            }
            catch (RejectedMutation<T> rejection)
            {
                return rejection.Result;
            }
        }
    }

    public async Task<IPreparedHumanWebhookMutation> PrepareAsync(ISecureRequest command,
        CancellationToken cancellationToken = default)
    {
        if (command is OpenSvixAppPortalCommand)
            throw new NotSupportedException("Portal access requires separate remote requester checkpoints.");
        return await PrepareCoreAsync(command, cancellationToken);
    }

    public async Task<IPreparedHumanWebhookCancellation> PrepareCancellationAsync(
        CancelWebhookBulkReplayCommand command, CancellationToken cancellationToken = default) =>
        new CancellationPrepared(this, command, await PrepareCoreAsync(command, cancellationToken,
            allowCancellationAbsence: true));

    public async Task<IPreparedHumanWebhookPortalRequester> PreparePortalAsync(OpenSvixAppPortalCommand command,
        CancellationToken cancellationToken = default) => await PrepareCoreAsync(command, cancellationToken);

    public async Task<IPreparedHumanWebhookRepairRequester> PrepareRepairAsync(
        RepairWebhookProviderBindingCommand command, CancellationToken cancellationToken = default) =>
        new RepairPrepared(command, await PrepareCoreAsync(command, cancellationToken));

    private async Task<Prepared> PrepareCoreAsync(ISecureRequest command, CancellationToken cancellationToken,
        CancellationCustody? original = null, bool allowCancellationAbsence = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        var target = original?.Target ?? HumanWebhookResourceTarget.From(command);
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("An original authenticated human is required.");
        var request = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated request is required.");
        var authenticatedPrincipal = request.User;
        if (original is not null && (!ReferenceEquals(original.Subject, subject)
            || !ReferenceEquals(original.Request, request) || !ReferenceEquals(original.Principal, authenticatedPrincipal)
            || !ReferenceEquals(original.Command, command) || tenantContext.TenantId != original.Tenant))
            throw new UnauthorizedAccessException("Original cancellation requester custody changed.");
        var lifetime = original?.Lifetime ?? await lifetimeObserver.ObserveAsync(authenticatedPrincipal, cancellationToken)
            ?? throw new UnauthorizedAccessException("Original credential lifetime evidence is required.");
        Guid ambientTenant = tenantContext.TenantId;
        Guid? selectedTenant = ambientTenant == Guid.Empty ? null : ambientTenant;
        if (subject.AccountKey != lifetime.AccountKey || !lifetime.IsValidAt(clock.GetUtcNow()))
            throw new UnauthorizedAccessException("The original credential is no longer valid.");

        var principalSource = resolvePrincipal();
        var routeSource = resolveRoute();
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var webhookSource = resolveWebhooks().Create(target);
        var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
        var principalFacts = await principalSource.ObserveAsync(cancellationToken);
        var routeFacts = await routeSource.ObserveAsync(cancellationToken);
        var supportFacts = await supportSource.ObserveAsync(cancellationToken);
        var webhookFacts = await webhookSource.ObserveAsync(cancellationToken);
        // Fresh evidence preparation keeps the FIRST attempt's support mode and expiry.
        // A new observation can restrict that boundary, never replace or extend it.
        var originalSupport = original?.Support ?? supportFacts;
        if (original is not null)
        {
            // Retention deletion or any intervening source transition is not evidence authority.
            // This is deliberately stricter than cancellation's allowed own transition.
            HumanWebhookMutationContinuity.Require(command, original.Webhooks, webhookFacts, false);
        }

        void RequireCustody()
        {
            if (!ReferenceEquals(humanScope.Subject, subject) || tenantContext.TenantId != ambientTenant
                || !ReferenceEquals(httpContext.HttpContext, request)
                || !ReferenceEquals(request.User, authenticatedPrincipal))
                throw new UnauthorizedAccessException("Original webhook requester custody changed.");
        }

        void RequireSources(HumanPrincipalSourceFacts p, HumanAuthorizationRouteFacts r,
            HumanWebhookResourceSourceFacts w)
        {
            RequireCustody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, p);
            if (p.SelectedTenantId != selectedTenant || r.SelectedTenantId != selectedTenant
                || w.SelectedTenantId != selectedTenant || r.PrincipalId != subject.PrincipalId
                || w.PrincipalId != subject.PrincipalId || !ReferenceEquals(w.Target, target))
                throw new UnauthorizedAccessException("Sources do not belong to the selected original webhook requester.");
        }

        HumanWebhookResourceInput? Resource(HumanPrincipalSourceFacts p, HumanWebhookResourceSourceFacts w)
        {
            var projected = HumanWebhookResourceProjection.Resource(w);
            if (projected is not null) return projected;
            // Cancellation alone can describe absence, not ownership of an invented operation.
            // P proves the exact selected tenant exists and is active; W's closed selector
            // observes this tenant/id, and acquisition excludes concurrent insertion/deletion.
            // No Global selection, alternative action, or other command receives this authority.
            return allowCancellationAbsence && command is CancelWebhookBulkReplayCommand cancel
                && target.Operation == HumanWebhookResourceOperation.CancelBulkReplay
                && w.SelectedTenantId == cancel.TenantId && w.Target.Id == cancel.OperationId
                && w.BulkReplays.IsEmpty
                && p.Tenants.Any(tenant => tenant.Id == cancel.TenantId && tenant.IsActiveState)
                ? new(ResourceKinds.Webhook, cancel.OperationId.ToString("D"),
                    AuthorizationActions.Webhooks.BulkReplay, new TenantScopedAuthorizationFacts(cancel.TenantId))
                : null;
        }

        RequireSources(principalFacts, routeFacts, webhookFacts);
        var resource = Resource(principalFacts, webhookFacts)
            ?? throw new UnauthorizedAccessException("The original webhook resource lineage is unavailable.");
        var check = new AuthorizationRequest(resource.ResourceKind, resource.ResourceId, resource.Action, Facts: resource.Facts);
        void RequireSupportBoundary(HumanSupportAccessSourceFacts current, DateTimeOffset now)
        {
            var forwarding = current.Forwarding;
            var retained = originalSupport.Forwarding;
            if (!ReferenceEquals(forwarding.Subject, retained.Subject)
                || forwarding.WasForwarded != retained.WasForwarded || forwarding.WasActive != retained.WasActive
                || forwarding.SessionId != retained.SessionId || forwarding.ResolvedTenantId != retained.ResolvedTenantId)
                throw new UnauthorizedAccessException("Original webhook support selection changed.");
            // Intersect immutable original authority with fresh persisted authority at ONE
            // trusted time, after every blocking source read. Own writes cannot widen it.
            RequireSupport(originalSupport, now, check);
            RequireSupport(current, now, check);
        }

        var route = HumanAuthorizationRouteProjector.Project(routeFacts, deployment, false,
            lifetime.LocalSession is null || principalFacts.LocalSession is not null);
        RequireSupportBoundary(supportFacts, clock.GetUtcNow());
        var principal = CerbosPrincipalBuilder.BuildPrincipalFromSources(principalFacts);
        var serialized = CerbosPrincipalBuilder.CapturePrincipal(principal);
        var local = FallbackAuthorizationService.PreparedWebhookInputs(principalFacts, check, ambientTenant);
        bool allowed;
        if (route.Provider == HumanAuthorizationRouteProvider.Local)
            allowed = FallbackAuthorizationService.EvaluatePreparedWebhook(principalFacts, check, ambientTenant);
        else if (route.Provider is HumanAuthorizationRouteProvider.CerbosInstance or HumanAuthorizationRouteProvider.CerbosTenant)
        {
            var decision = await cerbos.AuthorizePreparedHumanAsync(principal, check, ambientTenant,
                deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
            if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                throw new AuthorizationProviderUnavailableException(check.ResourceKind, check.Action);
            allowed = decision.IsAllowed;
        }
        else allowed = false;
        if (!allowed) throw new AuthorizationException(check.ResourceKind, check.Action);
        RequireCustody();
        var preparedAt = clock.GetUtcNow();
        if (!lifetime.IsValidAt(preparedAt))
            throw new UnauthorizedAccessException("The original credential expired during preparation.");
        RequireSupportBoundary(supportFacts, preparedAt);

        void Compare(HumanPrincipalSourceFacts p, HumanAuthorizationRouteFacts r,
            HumanWebhookResourceSourceFacts w, bool afterMutation)
        {
            RequireSources(p, r, w);
            var currentResource = Resource(p, w);
            var currentRoute = HumanAuthorizationRouteProjector.Project(r, deployment, false,
                lifetime.LocalSession is null || p.LocalSession is not null);
            if (resource != currentResource || !route.IsSemanticallyEquivalentTo(currentRoute))
                throw new UnauthorizedAccessException("The original webhook policy input or selected route changed.");
            bool samePrincipal = route.Provider == HumanAuthorizationRouteProvider.Local
                ? local == FallbackAuthorizationService.PreparedWebhookInputs(p, check, ambientTenant)
                : serialized.Equals(CerbosPrincipalBuilder.CapturePrincipal(
                    CerbosPrincipalBuilder.BuildPrincipalFromSources(p)));
            if (!samePrincipal)
                throw new UnauthorizedAccessException("The selected provider's original webhook principal changed.");
            HumanWebhookMutationContinuity.Require(command, webhookFacts, w, original is null && afterMutation);
        }

        async Task Acquire(CancellationToken token)
        {
            RequireCustody();
            var exclusion = await resolveExclusion().AcquireAsync(token);
            var p = await principalSource.AcquireAsync(exclusion, token);
            var r = await routeSource.AcquireAsync(exclusion, token);
            var h = await supportSource.AcquireAsync(exclusion, p, cancellationToken: token);
            var w = await webhookSource.AcquireAsync(exclusion, p, h, cancellationToken: token);
            Compare(p.Facts, r.Facts, w.Facts, false);
            var acquiredAt = clock.GetUtcNow();
            if (!lifetime.IsValidAt(acquiredAt))
                throw new UnauthorizedAccessException("The original credential expired before acquisition.");
            RequireSupportBoundary(h.Facts, acquiredAt);
            FinalFacts? final = null;
            resolveFinalizer().Register(async finalToken =>
            {
                // Every validation can block and returns a fresh observation of own persisted
                // writes. Finish ALL P/route/H/W reads before authentic lifetime/support time.
                var freshPrincipal = await p.ValidateAsync(finalToken);
                var freshRoute = await r.ValidateAsync(finalToken);
                var freshSupport = await h.ValidateAsync(finalToken);
                var freshWebhooks = await w.ValidateAsync(finalToken);
                Compare(freshPrincipal, freshRoute, freshWebhooks, true);
                final = new(freshPrincipal, freshRoute, freshSupport, freshWebhooks);
            }, now =>
            {
                var fresh = final ?? throw new InvalidOperationException("Final webhook source admission is missing.");
                Compare(fresh.Principal, fresh.Route, fresh.Webhooks, true);
                if (!lifetime.IsValidAt(now))
                    throw new UnauthorizedAccessException("The original credential expired before commit.");
                RequireSupportBoundary(fresh.Support, now);
                HumanWebhookMutationContinuity.RequireFinalTime(command, webhookFacts, fresh.Webhooks, now);
            });
        }

        async Task Checkpoint(CancellationToken token)
        {
            // These ports require idle original ownership before and after observation.
            // This is deliberately not a transaction spanning a remote provider await.
            var p = await principalSource.ObserveAsync(token);
            var r = await routeSource.ObserveAsync(token);
            var h = await supportSource.ObserveAsync(token);
            var w = await webhookSource.ObserveAsync(token);
            Compare(p, r, w, false);
            var now = clock.GetUtcNow();
            if (!lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("The original portal requester credential expired.");
            RequireSupportBoundary(h, now);
        }

        async Task PortalCheckpoint(Func<CancellationToken, Task> readProvider,
            Action<DateTimeOffset> validateProviderAtRelease, CancellationToken token)
        {
            RequireCustody();
            await readProvider(token);
            var p = await principalSource.ObserveAsync(token);
            var r = await routeSource.ObserveAsync(token);
            var h = await supportSource.ObserveAsync(token);
            var w = await webhookSource.ObserveAsync(token);
            Compare(p, r, w, false);
            if (!ReferenceEquals(h.Forwarding.Subject, supportFacts.Forwarding.Subject)
                || h.Forwarding.WasForwarded != supportFacts.Forwarding.WasForwarded
                || h.Forwarding.WasActive != supportFacts.Forwarding.WasActive
                || h.Forwarding.SessionId != supportFacts.Forwarding.SessionId
                || h.Forwarding.ResolvedTenantId != supportFacts.Forwarding.ResolvedTenantId
                || !h.Sessions.SequenceEqual(supportFacts.Sessions))
                throw new UnauthorizedAccessException("Original portal support custody changed.");
            token.ThrowIfCancellationRequested();
            // No await may follow this sample: provider reads and every human source
            // observation (including connection cleanup) have already completed.
            var now = clock.GetUtcNow();
            if (!lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("The original portal requester credential expired.");
            RequireSupportBoundary(h, now);
            validateProviderAtRelease(now);
        }

        return new Prepared(command, HumanWebhookResourceProjection.PortalConsumer(webhookFacts), Acquire, Checkpoint,
            new(command, subject, lifetime, request, authenticatedPrincipal, ambientTenant, target,
                webhookFacts, originalSupport))
        { PortalCheckpoint = PortalCheckpoint };
    }

    private static bool ContainsBorrowedReceipt(Exception exception) =>
        exception is HumanMutationAttemptRolledBackException
        || exception is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsBorrowedReceipt)
        || exception.InnerException is { } inner && ContainsBorrowedReceipt(inner);

    private static void RequireSupport(HumanSupportAccessSourceFacts facts, DateTimeOffset now, AuthorizationRequest check)
    {
        if (HumanSupportAccessProjection.BoundaryDenialReason(facts, now, check.ResourceKind, check.Action,
                AuthorizationFactAttributeProjection.ToAttributes(check.Facts)) is not null)
            throw new UnauthorizedAccessException("The original support boundary does not permit this webhook action.");
    }

    private sealed class RejectedMutation<T>(T result) : Exception
    {
        internal T Result { get; } = result;
    }

    private sealed record FinalFacts(HumanPrincipalSourceFacts Principal, HumanAuthorizationRouteFacts Route,
        HumanSupportAccessSourceFacts Support, HumanWebhookResourceSourceFacts Webhooks);

    private sealed record CancellationCustody(ISecureRequest Command, HumanCommitSubject Subject,
        HumanAuthenticationLifetime Lifetime, HttpContext Request, ClaimsPrincipal Principal, Guid Tenant,
        HumanWebhookResourceTarget Target, HumanWebhookResourceSourceFacts Webhooks,
        HumanSupportAccessSourceFacts Support)
    {
        public override string ToString() => nameof(CancellationCustody);
    }

    private sealed class CancellationPrepared(HumanWebhookMutationPreparation owner,
        CancelWebhookBulkReplayCommand command, Prepared prepared) : IPreparedHumanWebhookCancellation
    {
        private int _evidencePrepared;
        public Guid PrincipalId => prepared.Custody.Subject.PrincipalId;
        public HumanWebhookBulkReplayFact? Operation => prepared.Acquired
            ? prepared.Custody.Webhooks.BulkReplays.SingleOrDefault(row => row.Id == command.OperationId
                && row.TenantId == command.TenantId)
            : throw new InvalidOperationException("Cancellation sources must be acquired before business classification.");

        public Task AcquireAsync(ISecureRequest originalCommand, CancellationToken cancellationToken = default) =>
            prepared.AcquireAsync(originalCommand, cancellationToken);

        public async Task<IPreparedHumanWebhookMutation> PrepareEvidenceAsync(CancellationToken cancellationToken = default)
        {
            if (!prepared.Acquired || Interlocked.Exchange(ref _evidencePrepared, 1) != 0)
                throw new InvalidOperationException("Original cancellation evidence can be prepared once after acquisition.");
            return await owner.PrepareCoreAsync(command, cancellationToken, prepared.Custody,
                allowCancellationAbsence: true);
        }
    }

    private sealed class RepairPrepared(RepairWebhookProviderBindingCommand command, Prepared prepared)
        : IPreparedHumanWebhookRepairRequester
    {
        public Guid PrincipalId => prepared.Custody.Subject.PrincipalId;
        public HumanWebhookConsumerFact Consumer =>
            prepared.Custody.Webhooks.Consumers.Single(row => row.Id == command.ConsumerId);
        public ImmutableArray<HumanWebhookBindingFact> Bindings => prepared.Custody.Webhooks.Bindings;

        public Task AcquireAsync(ISecureRequest originalCommand, CancellationToken cancellationToken = default) =>
            prepared.AcquireAsync(originalCommand, cancellationToken);

        public Task ValidateAsync(RepairWebhookProviderBindingCommand originalCommand,
            CancellationToken cancellationToken = default) =>
            prepared.ValidateRepairAsync(originalCommand, cancellationToken);
    }

    private sealed class Prepared(ISecureRequest command, WebhookPortalConsumerCustody? consumer,
        Func<CancellationToken, Task> acquire, Func<CancellationToken, Task> checkpoint, CancellationCustody custody)
        : IPreparedHumanWebhookMutation, IPreparedHumanWebhookPortalRequester
    {
        private int _consumed;
        internal bool Acquired { get; private set; }
        internal CancellationCustody Custody { get; } = custody;
        internal Func<Func<CancellationToken, Task>, Action<DateTimeOffset>, CancellationToken, Task>? PortalCheckpoint { get; init; }
        public WebhookPortalConsumerCustody Consumer => consumer
            ?? throw new InvalidOperationException("This capability is not a portal requester checkpoint.");

        public async Task AcquireAsync(ISecureRequest originalCommand, CancellationToken cancellationToken = default)
        {
            if (command is OpenSvixAppPortalCommand)
                throw new NotSupportedException("Portal requester checkpoints cannot authorize transactional remote calls.");
            if (Interlocked.Exchange(ref _consumed, 1) != 0)
                throw new InvalidOperationException("A prepared webhook action belongs to one attempt only.");
            if (!ReferenceEquals(command, originalCommand))
                throw new UnauthorizedAccessException("The prepared webhook action belongs to another command instance.");
            cancellationToken.ThrowIfCancellationRequested();
            await acquire(cancellationToken);
            Acquired = true;
        }

        public Task ValidateAsync(OpenSvixAppPortalCommand originalCommand, CancellationToken cancellationToken = default)
        {
            if (consumer is null || !ReferenceEquals(command, originalCommand))
                throw new UnauthorizedAccessException("The portal checkpoint belongs to another command instance.");
            cancellationToken.ThrowIfCancellationRequested();
            return checkpoint(cancellationToken);
        }

        internal Task ValidateRepairAsync(RepairWebhookProviderBindingCommand originalCommand,
            CancellationToken cancellationToken)
        {
            if (command is not RepairWebhookProviderBindingCommand || !ReferenceEquals(command, originalCommand))
                throw new UnauthorizedAccessException("The repair checkpoint belongs to another command instance.");
            if (Volatile.Read(ref _consumed) != 0)
                throw new InvalidOperationException("Repair remote checkpoints precede original owner acquisition.");
            cancellationToken.ThrowIfCancellationRequested();
            return checkpoint(cancellationToken);
        }

        public Task ValidateAsync(OpenSvixAppPortalCommand originalCommand,
            Func<CancellationToken, Task> readProvider, Action<DateTimeOffset> validateProviderAtRelease,
            CancellationToken cancellationToken = default)
        {
            if (command is not OpenSvixAppPortalCommand || consumer is null
                || !ReferenceEquals(command, originalCommand) || PortalCheckpoint is null)
                throw new UnauthorizedAccessException("The portal checkpoint belongs to another command instance.");
            cancellationToken.ThrowIfCancellationRequested();
            return PortalCheckpoint(readProvider, validateProviderAtRelease, cancellationToken);
        }
    }
}
