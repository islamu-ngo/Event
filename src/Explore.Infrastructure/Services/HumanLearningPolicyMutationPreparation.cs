using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Learning;
using Explore.Application.Features.Learning.Requests.Commands;
using Explore.Domain.Enums;
using Explore.Domain.Learning;
using Explore.Domain.Settings.Documents;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>Retains one exact guidance action and its idle-owner decision through physical settlement.</summary>
public sealed class HumanLearningPolicyMutationPreparation(
    IServiceProvider services,
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor contexts,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    TimeProvider clock,
    CerbosAuthorizationService cerbos,
    IOptions<AuthorizationProviderDeploymentOptions> authorizationOptions,
    IOptions<CerbosSettings> cerbosOptions) : IHumanLearningPolicyMutationPreparation
{
    private Mutation? _current;
    private int _used;
    public IHumanLearningPolicyMutation? CurrentMutation => _current;

    public async Task<ResolvedLearningGuidancePolicy> ExecuteAsync(
        UpdateLearningGuidancePolicyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (Interlocked.Exchange(ref _used, 1) != 0)
            throw new InvalidOperationException("A guidance preparation permits one physical attempt.");
        await humanScope.PrepareAsync(cancellationToken);
        var gate = services.GetRequiredService<IHumanSecuritySourceGate>();
        bool settled = false;
        try
        {
            await using var heldGate = gate.TryRequireCurrent() ? null : await gate.AcquireAsync(cancellationToken);
            gate.RequireCurrent();
            var subject = humanScope.Subject ?? throw new UnauthorizedAccessException("An original human is required.");
            var request = contexts.HttpContext ?? throw new UnauthorizedAccessException("An original request is required.");
            var authenticated = request.User;
            var lifetime = await lifetimeObserver.ObserveAsync(authenticated, cancellationToken)
                ?? throw new UnauthorizedAccessException("Original credential lifetime is required.");
            var custody = services.GetRequiredService<IHumanOriginalCredentialCustody>();
            var machine = services.GetRequiredService<IMachinePrincipalAccessor>();
            var tenant = services.GetRequiredService<ITenantContext>();
            Guid tenantId = tenant.TenantId;
            if (tenantId == Guid.Empty || subject.PrincipalId == Guid.Empty || subject.AccountKey != lifetime.AccountKey)
                throw new UnauthorizedAccessException("Guidance requires its original selected-tenant human.");
            var acceptedPolicy = LearningGuidancePolicyCodec.FromPayload(command.Input.Policy);
            void RequireCustody(DateTimeOffset now)
            {
                gate.RequireCurrent();
                custody.RequireOriginalCredential(lifetime);
                if (machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                    || !ReferenceEquals(contexts.HttpContext, request) || !ReferenceEquals(request.User, authenticated)
                    || tenant.TenantId != tenantId || !lifetime.IsValidAt(now))
                    throw new UnauthorizedAccessException("Original guidance execution custody changed.");
            }
            RequireCustody(clock.GetUtcNow());
            var sourceSet = services.GetRequiredService<IPolicySourceSetFence>();
            var principalSource = services.GetRequiredService<IHumanPrincipalSourceFence>();
            var routeSource = services.GetRequiredService<IHumanAuthorizationRouteSource>();
            var supportSource = await services.GetRequiredService<IHumanSupportAccessSourceFactory>()
                .CreateAsync(cancellationToken);
            var administrator = services.GetRequiredService<ITenantAdministratorCommitAuthority>();
            var originalPrincipal = await principalSource.ObserveAsync(cancellationToken);
            var originalRoute = await routeSource.ObserveAsync(cancellationToken);
            var originalSupport = await supportSource.ObserveAsync(cancellationToken);
            var check = new AuthorizationRequest(ResourceKinds.TenantSetting, tenantId.ToString("D"),
                AuthorizationActions.Update,
                Facts: new TenantSettingAuthorizationFacts(tenantId, SettingsDocumentKeys.Tenant.LearningGuidance));
            var deployment = HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value);
            RequireFacts(originalPrincipal, originalRoute);
            RequireSupport(originalSupport, clock.GetUtcNow());
            var route = HumanAuthorizationRouteProjector.Project(originalRoute, deployment, settingsGovernance: true,
                localDecisionSourcesCoLocated: lifetime.LocalSession is null || originalPrincipal.LocalSession is not null);
            var principal = CerbosPrincipalBuilder.BuildPrincipalFromSources(originalPrincipal);
            var serialized = CerbosPrincipalBuilder.CapturePrincipal(principal);
            if (route.Provider == HumanAuthorizationRouteProvider.CerbosInstance)
            {
                var decision = await cerbos.AuthorizePreparedHumanAsync(principal, check, tenantId,
                    deployment.UsePolicyScope, route.Endpoint!, cancellationToken);
                if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                    throw new AuthorizationProviderUnavailableException(check.ResourceKind, check.Action);
                if (!decision.IsAllowed) throw new AuthorizationException(check.ResourceKind, check.Action);
            }
            else if (route.Provider != HumanAuthorizationRouteProvider.Local)
                throw new AuthorizationException(check.ResourceKind, check.Action);
            RequireCustody(clock.GetUtcNow());
            var store = services.GetRequiredService<IHumanLearningPolicyMutationStore>();
            var original = await store.ObserveAsync(command, tenantId, cancellationToken);
            if (!original.AllowTenantEdits)
                throw new AuthorizationException("Instance policy does not permit tenant guidance changes.");
            IPolicySourceSetLease? exclusion = null;
            IHumanPrincipalSourceLease? heldPrincipal = null;
            IHumanAuthorizationRouteLease? heldRoute = null;
            IHumanSupportAccessSourceLease? heldSupport = null;
            HumanSupportAccessSourceFacts? finalSupport = null;
            DateTimeOffset at = clock.GetUtcNow();
            at = new DateTimeOffset(at.UtcTicks - at.UtcTicks % 10, TimeSpan.Zero);
            Mutation? mutation = null;
            mutation = new(command, tenantId, subject.PrincipalId, Guid.CreateVersion7(at), Guid.CreateVersion7(at),
                at.UtcDateTime, acceptedPolicy, original, RequireOriginalAsync, AcquireAsync, VerifyAsync, RequireCurrentAt);
            _current = mutation;
            var result = await store.ExecuteAsync(mutation, cancellationToken);
            settled = true;
            RequireCustody(clock.GetUtcNow());
            return result;

            void RequireCurrentReference()
            {
                if (mutation is null || !ReferenceEquals(_current, mutation)
                    || !ReferenceEquals(mutation.OriginalCommand, command))
                    throw new UnauthorizedAccessException("The original guidance command is no longer current.");
            }
            Task RequireOriginalAsync(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                RequireCurrentReference();
                RequireCustody(clock.GetUtcNow());
                return Task.CompletedTask;
            }
            async Task AcquireAsync(CancellationToken token)
            {
                await RequireOriginalAsync(token);
                if (exclusion is not null)
                    throw new InvalidOperationException("Guidance sources may be acquired only once.");
                exclusion = await sourceSet.AcquireAsync(token);
                heldPrincipal = await principalSource.AcquireAsync(exclusion, token);
                heldRoute = await routeSource.AcquireAsync(exclusion, token);
                heldSupport = await supportSource.AcquireAsync(exclusion, heldPrincipal, cancellationToken: token);
                await VerifyAsync(token);
            }
            async Task VerifyAsync(CancellationToken token)
            {
                await RequireOriginalAsync(token);
                if (exclusion is null || heldPrincipal is null || heldRoute is null || heldSupport is null)
                    throw new UnauthorizedAccessException("Original acquired guidance authority is required.");
                await exclusion.ValidateAsync(token);
                var currentPrincipal = await heldPrincipal.ValidateAsync(token);
                var currentRoute = await heldRoute.ValidateAsync(token);
                finalSupport = await heldSupport.ValidateAsync(token);
                RequireFacts(currentPrincipal, currentRoute);
                if (deployment != HumanAuthorizationRouteDeployment.Capture(authorizationOptions.Value, cerbosOptions.Value))
                    throw new UnauthorizedAccessException("The original guidance route declaration changed.");
                var current = HumanAuthorizationRouteProjector.Project(currentRoute, deployment, settingsGovernance: true,
                    localDecisionSourcesCoLocated: lifetime.LocalSession is null || currentPrincipal.LocalSession is not null);
                if (!route.IsSemanticallyEquivalentTo(current)
                    || !serialized.Equals(CerbosPrincipalBuilder.CapturePrincipal(
                        CerbosPrincipalBuilder.BuildPrincipalFromSources(currentPrincipal))))
                    throw new UnauthorizedAccessException("The original guidance policy inputs changed.");
                if (!await administrator.IsTenantAdministratorForCommitAsync(subject.PrincipalId, tenantId, token))
                    throw new AuthorizationException(check.ResourceKind, check.Action);
                RequireCurrentAt(clock.GetUtcNow());
            }
            void RequireCurrentAt(DateTimeOffset now)
            {
                RequireCurrentReference();
                RequireCustody(now);
                RequireSupport(originalSupport, now);
                RequireSupport(finalSupport ?? throw new UnauthorizedAccessException("Final guidance support sources are absent."), now);
            }
            void RequireFacts(HumanPrincipalSourceFacts facts, HumanAuthorizationRouteFacts routeFacts)
            {
                RequireCustody(clock.GetUtcNow());
                HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, facts);
                if (facts.SelectedTenantId != tenantId || routeFacts.SelectedTenantId != tenantId
                    || routeFacts.PrincipalId != subject.PrincipalId)
                    throw new UnauthorizedAccessException("Guidance sources belong to another subject or tenant.");
                if (!facts.TenantGrants.Any(grant => grant.TenantId == tenantId
                        && grant.RoleId == (int)RoleEnum.TenantAdmin && grant.RevokedAt is null
                        && facts.TenantMemberships.Any(member => member.Id == grant.TenantUserId
                            && member.StatusId == (int)TenantUserStatusEnum.Active && !member.IsDeleted)))
                    throw new AuthorizationException(check.ResourceKind, check.Action);
            }
            void RequireSupport(HumanSupportAccessSourceFacts facts, DateTimeOffset now)
            {
                RequireCustody(now);
                if (!ReferenceEquals(facts.Forwarding.Subject, subject) || facts.Forwarding.ResolvedTenantId != tenantId
                    || facts.Forwarding.WasActive || facts.Forwarding.WasForwarded
                    || !ReferenceEquals(facts.Forwarding, originalSupport.Forwarding)
                    || !facts.Sessions.SequenceEqual(originalSupport.Sessions)
                    || !facts.Settings.SequenceEqual(originalSupport.Settings)
                    || HumanSupportAccessProjection.BoundaryDenialReason(facts, now, check.ResourceKind, check.Action,
                        AuthorizationFactAttributeProjection.ToAttributes(check.Facts)) is not null)
                    throw new UnauthorizedAccessException("Original support custody does not permit private guidance changes.");
            }
        }
        catch (Exception exception) when (settled && exception is not HumanMutationOutcomeUnknownException)
        {
            throw new HumanMutationOutcomeUnknownException(exception);
        }
        finally { _current = null; }
    }

    private sealed record Mutation(
        UpdateLearningGuidancePolicyCommand OriginalCommand,
        Guid TenantId, Guid SubjectId, Guid CandidateDocumentId, Guid CandidateStamp, DateTime ChangedAtUtc,
        LearningGuidancePolicy Policy, ILearningPolicyOriginalSources OriginalSources,
        Func<CancellationToken, Task> RequireOriginal,
        Func<CancellationToken, Task> Acquire,
        Func<CancellationToken, Task> Verify,
        Action<DateTimeOffset> RequireAt) : IHumanLearningPolicyMutation
    {
        public Task RequireOriginalAsync(CancellationToken cancellationToken) => RequireOriginal(cancellationToken);
        public Task AcquireAsync(CancellationToken cancellationToken) => Acquire(cancellationToken);
        public Task VerifyAsync(CancellationToken cancellationToken) => Verify(cancellationToken);
        public void RequireCurrentAt(DateTimeOffset utcNow) => RequireAt(utcNow);
        public override string ToString() => nameof(Mutation);
    }
}
