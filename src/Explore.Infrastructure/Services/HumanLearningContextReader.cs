using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Exceptions;
using Explore.Application.Features.Learning;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Reads only the original active learner's state under one held human, support and
/// optional event-assignment source image. Reading never creates learning state.
/// </summary>
public sealed class HumanLearningContextReader(
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor contexts,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    IMachinePrincipalAccessor machine,
    ITenantContext tenantContext,
    TimeProvider clock,
    Func<IHumanOriginalCredentialCustody> resolveCredentialCustody,
    Func<IHumanPrincipalSourceFence> resolvePrincipal,
    Func<IHumanSupportAccessSourceFactory> resolveSupport,
    Func<IHumanEventAssignmentSourceFactory> resolveAssignments,
    Func<IPolicySourceSetFence> resolveSourceSet,
    Func<ISerializableAttemptOwner> resolveAttempt,
    Func<IHumanActionCommitFinalizer> resolveFinalizer,
    Func<ILearningRepository> resolveLearning,
    Func<IActorRepository> resolveActors,
    Func<IAiActorContextRepository> resolveAvailableActors,
    Func<ITenantAdministratorCommitAuthority> resolveAdministrator,
    Func<IHierarchicalSettingsResolver> resolveSettings,
    Func<IAiProviderCapabilityResolver> resolveAiCapabilities,
    Func<IPrivacyIdentityFenceAuthority> resolvePrivacy) : IHumanLearningContextReader
{
    public Task<LearningOperationState> ReadContextAsync(
        string contextKey, Guid? eventContextId, CancellationToken cancellationToken) =>
        ReadAsync(contextKey, eventContextId, null, cancellationToken);

    public Task<LearningOperationState> ReadPracticeAsync(
        Guid practiceId, CancellationToken cancellationToken)
    {
        if (practiceId == Guid.Empty)
            throw new NotFoundException("Learning practice", practiceId);
        return ReadAsync(null, null, practiceId, cancellationToken);
    }

    private async Task<LearningOperationState> ReadAsync(
        string? contextKey, Guid? eventContextId, Guid? practiceId, CancellationToken cancellationToken)
    {
        if (machine.IsMachineCaller || tenantContext.TenantId == Guid.Empty)
            throw new UnauthorizedAccessException("Learning reads require the original selected-tenant human.");
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("An original resolved human is required.");
        var request = contexts.HttpContext
            ?? throw new UnauthorizedAccessException("An original authenticated request is required.");
        var principal = request.User;
        var lifetime = await lifetimeObserver.ObserveAsync(principal, cancellationToken)
            ?? throw new UnauthorizedAccessException("Authentic credential lifetime is required.");
        Guid tenantId = tenantContext.TenantId;
        Guid userId = subject.PrincipalId;
        if (tenantId == Guid.Empty || userId == Guid.Empty || subject.AccountKey != lifetime.AccountKey)
            throw new UnauthorizedAccessException("Learning reads cannot substitute another human.");

        var credentialCustody = resolveCredentialCustody();
        void RequireCustody(DateTimeOffset now)
        {
            credentialCustody.RequireOriginalCredential(lifetime);
            if (machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(contexts.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || tenantContext.TenantId != tenantId || !lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("Original learning request credential or custody changed.");
        }
        RequireCustody(clock.GetUtcNow());
        var workspaces = await LearningWorkspaceAvailability.ObserveAsync(
            resolveSettings(), resolveAiCapabilities(), tenantId, cancellationToken);
        RequireCustody(clock.GetUtcNow());
        if (practiceId is null) RequireCanonicalContext(contextKey);
        if (eventContextId == Guid.Empty)
            throw new NotFoundException("Learning event context", eventContextId);

        var principalSource = resolvePrincipal();
        var supportSource = await resolveSupport().CreateAsync(cancellationToken);
        var assignmentSource = eventContextId is { } eventId
            ? resolveAssignments().Create(tenantId, [eventId]) : null;
        var sourceSet = resolveSourceSet();
        var originalPrincipal = await principalSource.ObserveAsync(cancellationToken);
        var originalSupport = await supportSource.ObserveAsync(cancellationToken);
        var originalAssignments = assignmentSource is null
            ? null : await assignmentSource.ObserveAsync(cancellationToken);
        RequirePrincipal(originalPrincipal);
        RequireSupport(originalSupport);
        RequireEvent(originalAssignments, clock.GetUtcNow());

        var privacy = resolvePrivacy() as IOriginalPrivacyIdentityFenceAuthority
            ?? throw new InvalidOperationException("Private learning reads require original retained authority.");
        async Task RequireUnfencedAsync(CancellationToken token)
        {
            RequireCustody(clock.GetUtcNow());
            if (await privacy.IsSubjectFencedAsync(userId, token))
                throw new UnauthorizedAccessException("Retained erasure excludes private learning reads.");
        }
        return await privacy.ExecuteOriginalEnrollmentAsync(gateToken =>
            resolveAttempt().ExecuteAsync<LearningOperationState>(async token =>
        {
            RequireCustody(clock.GetUtcNow());
            await RequireUnfencedAsync(token);
            var exclusion = await sourceSet.AcquireAsync(token);
            var heldPrincipal = await principalSource.AcquireAsync(exclusion, token);
            RequirePrincipal(heldPrincipal.Facts);
            var heldSupport = await supportSource.AcquireAsync(exclusion, heldPrincipal, cancellationToken: token);
            RequireSupport(heldSupport.Facts);
            var heldAssignments = assignmentSource is null
                ? null : await assignmentSource.AcquireAsync(exclusion, token);
            RequireEvent(heldAssignments?.Facts, clock.GetUtcNow());
            HumanSupportAccessSourceFacts? finalSupport = null;
            HumanEventAssignmentSourceFacts? finalAssignments = null;
            resolveFinalizer().Register(async finalToken =>
            {
                await RequireUnfencedAsync(finalToken);
                await exclusion.ValidateAsync(finalToken);
                RequirePrincipal(await heldPrincipal.ValidateAsync(finalToken));
                finalSupport = await heldSupport.ValidateAsync(finalToken);
                RequireSupport(finalSupport);
                finalAssignments = heldAssignments is null
                    ? null : await heldAssignments.ValidateAsync(finalToken);
                RequireEvent(finalAssignments, clock.GetUtcNow());
            }, now =>
            {
                RequireCustody(now);
                RequireSupport(finalSupport
                    ?? throw new UnauthorizedAccessException("Final learning support sources are missing."));
                RequireEvent(finalAssignments, now);
            });

            var repository = resolveLearning();
            var practice = practiceId is { } selectedPractice
                ? await repository.GetPracticeAsync(tenantId, userId, selectedPractice, token)
                    ?? throw new NotFoundException("Learning practice", selectedPractice)
                : null;
            string selectedContext = practice is null ? contextKey! : practice.ContextKey;
            Guid? actorId = RequireCanonicalContext(selectedContext);
            if (actorId is { } selectedActor)
            {
                var actor = await resolveActors().GetForModerationAsync(selectedActor, token);
                if (actor is null || actor.IsDeleted || actor.IsSuspended
                    || !LearningAccessPolicy.IsActorMember(heldPrincipal.Facts, actor))
                    throw new NotFoundException("Learning context", selectedContext);
            }
            var space = await repository.GetSpaceAsync(tenantId, tracked: false, token);
            if (practice is not null && (space is null || space.IsRemoved))
                throw new NotFoundException("Learning practice", practice.Id);
            var state = await repository.GetStateAsync(tenantId, userId, tracked: false, token);
            var availableActors = await resolveAvailableActors().GetAvailableActorsAsync(token);
            bool canManageSpace = await resolveAdministrator()
                .IsTenantAdministratorForCommitAsync(userId, tenantId, token);
            RequireCustody(clock.GetUtcNow());
            return new(tenantId, userId, selectedContext, eventContextId, space, state, practice,
                availableActors, canManageSpace, eventContextId.HasValue)
            {
                StudioAvailable = workspaces.Studio(availableActors),
                NormalAiAvailable = workspaces.NormalAi,
                ObservedAtUtc = clock.GetUtcNow().UtcDateTime
            };
        }, gateToken), RequireUnfencedAsync, RequireCustody, cancellationToken);

        void RequirePrincipal(HumanPrincipalSourceFacts current)
        {
            RequireCustody(clock.GetUtcNow());
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, current);
            if (!LearningAccessPolicy.IsActiveLearner(current, tenantId)
                || current.PrincipalId != userId || !Same(originalPrincipal, current))
                throw new UnauthorizedAccessException("Original learning membership or authority sources changed.");
        }

        void RequireSupport(HumanSupportAccessSourceFacts current)
        {
            if (!ReferenceEquals(current.Forwarding.Subject, subject)
                || current.Forwarding.ResolvedTenantId != tenantId
                || current.Forwarding.WasActive || current.Forwarding.WasForwarded
                || !ReferenceEquals(originalSupport.Forwarding, current.Forwarding)
                || !originalSupport.Sessions.SequenceEqual(current.Sessions)
                || !originalSupport.Settings.SequenceEqual(current.Settings))
                throw new UnauthorizedAccessException("Support authority cannot read private learning state.");
        }

        void RequireEvent(HumanEventAssignmentSourceFacts? current, DateTimeOffset now)
        {
            RequireCustody(now);
            if (eventContextId is not { } selectedEvent) return;
            if (current is null || current.PrincipalId != userId || current.TenantId != tenantId
                || !Same(originalAssignments, current))
                throw new UnauthorizedAccessException("Original learning event-assignment sources changed.");
            var target = current.Events.SingleOrDefault(value => value.Id == selectedEvent
                && value.TenantId == tenantId && !value.IsDeleted);
            if (target is null || !current.Actors.Any(value => value.Id == target.ActorId
                    && !value.IsDeleted && !value.IsSuspended)
                || !HumanEventAssignmentProjection.Authority(current, now.UtcDateTime).Events
                    .TryGetValue(selectedEvent, out var authority)
                || !(authority.IsOwner || authority.IsManager
                    || authority.PermissionCodes.Contains(PermissionCodes.EventUpdate)
                    || authority.PermissionCodes.Contains(PermissionCodes.EventRegistrationManage)
                    || authority.PermissionCodes.Contains(PermissionCodes.EventCheckInManage)))
                throw new NotFoundException("Learning event context", selectedEvent);
        }
    }

    private static Guid? RequireCanonicalContext(string? contextKey)
    {
        if (contextKey == "self") return null;
        if (contextKey is null || !contextKey.StartsWith("actor:", StringComparison.Ordinal)
            || !Guid.TryParseExact(contextKey.AsSpan(6), "D", out Guid actorId)
            || actorId == Guid.Empty || contextKey != $"actor:{actorId:D}")
            throw new ArgumentException("Use the canonical self or actor context key.", nameof(contextKey));
        return actorId;
    }

    // Native source records retain the complete candidate image, including negative rows.
    private static bool Same<T>(T expected, T current) =>
        string.Equals(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(current), StringComparison.Ordinal);
}
