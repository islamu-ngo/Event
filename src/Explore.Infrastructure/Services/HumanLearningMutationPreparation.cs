using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Learning;
using Explore.Application.Features.Learning.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Learning;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Wire = ISLAMU.Wire.Contracts.Learning;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Retains one original human intent while the learning store owns its physical settlement.
/// Native workspace readiness is observed only before that settlement, never under its owner.
/// </summary>
public sealed class HumanLearningMutationPreparation(
    IServiceProvider services,
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor contexts,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    TimeProvider clock)
    : IHumanLearningMutationPreparation
{
    private Mutation? _current;
    private int _consumed;

    public IHumanLearningMutation? CurrentMutation => _current;

    public Task<LearningOperationState> ExecuteAsync(
        ClaimLearningTourCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(new LearningTourClaimIntent(command.Input.ExpectedRevision, command.Input.IntentId,
            Map(command.Input.Family), command.Input.MaterialVersion, command.Input.ContextKey,
            command.Input.EventContextId), command.Input.ExpectedScopeStamp, cancellationToken);

    public Task<LearningOperationState> ExecuteAsync(
        RecordLearningTourOutcomeCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(new LearningTourOutcomeIntent(command.Input.ExpectedRevision, command.Input.IntentId,
            command.Input.ClaimToken, Map(command.Input.Family), command.Input.MaterialVersion,
            Map(command.Input.Outcome), command.Input.StepIndex),
            command.Input.ExpectedScopeStamp, cancellationToken);

    public Task<LearningOperationState> ExecuteAsync(
        UpdateLearningPreferencesCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(new LearningPreferencesIntent(command.Input.ExpectedRevision,
            command.Input.SuppressAutomaticTours, command.Input.HideLearning, command.Input.PinLearning),
            command.Input.ExpectedScopeStamp, cancellationToken);

    public Task<LearningOperationState> ExecuteAsync(
        ApplyLearningPracticeActionCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(new LearningPracticeIntent(command.Input.ExpectedRevision, command.Input.IntentId,
            command.Input.ExpectedPracticeRevision, command.Input.PracticeId, command.Input.ContextKey,
            command.Input.RecipeKey, command.Input.MaterialVersion, Map(command.Input.Action),
            command.Input.ChoiceKey), command.Input.ExpectedScopeStamp, cancellationToken);

    public Task<LearningOperationState> ExecuteAsync(
        ManageTenantLearningSpaceCommand command, CancellationToken cancellationToken) =>
        ExecuteAsync(new LearningSpaceIntent(command.Input.ExpectedRevision, command.Input.Action switch
        {
            Wire.LearningSpaceAction.Remove => LearningSpaceMutationAction.Remove,
            Wire.LearningSpaceAction.Recreate => LearningSpaceMutationAction.Recreate,
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        }), command.Input.ExpectedScopeStamp, cancellationToken);

    private async Task<LearningOperationState> ExecuteAsync(
        LearningMutationIntent intent, string scopeStamp, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _consumed, 1) != 0)
            throw new InvalidOperationException("A learning preparation permits one physical attempt.");
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("An original resolved human is required.");
        var request = contexts.HttpContext
            ?? throw new UnauthorizedAccessException("An original authenticated request is required.");
        var principal = request.User;
        var lifetime = await lifetimeObserver.ObserveAsync(principal, cancellationToken)
            ?? throw new UnauthorizedAccessException("Authentic credential lifetime is required.");

        // Tenant-bound consumers must not be constructed before trusted human preparation.
        var machine = services.GetRequiredService<IMachinePrincipalAccessor>();
        var tenantContext = services.GetRequiredService<ITenantContext>();
        var credentialCustody = services.GetRequiredService<IHumanOriginalCredentialCustody>();
        Guid tenantId = tenantContext.TenantId;
        Guid userId = subject.PrincipalId;
        if (tenantId == Guid.Empty || userId == Guid.Empty || subject.AccountKey != lifetime.AccountKey)
            throw new UnauthorizedAccessException("Learning effects require the original selected-tenant human.");
        if (!string.Equals(scopeStamp, LearningAccessPolicy.ScopeStamp(tenantId, userId), StringComparison.Ordinal))
            throw new ConcurrencyConflictException("learning_scope_changed",
                "The private learning scope changed. Refresh the current context.");

        void RequireCustody(DateTimeOffset now)
        {
            credentialCustody.RequireOriginalCredential(lifetime);
            if (machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(contexts.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || tenantContext.TenantId != tenantId || !lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("Original learning credential or request custody changed.");
        }

        RequireCustody(clock.GetUtcNow());
        var repository = services.GetRequiredService<ILearningRepository>();
        string contextKey = "self";
        Guid? eventContextId = null;
        switch (intent)
        {
            case LearningTourClaimIntent claim:
                contextKey = claim.ContextKey;
                eventContextId = claim.EventContextId;
                if ((claim.Family == LearningTourFamily.EventManagement) != eventContextId.HasValue)
                    throw new ArgumentException("Only event-management claims require an event context.");
                break;
            case LearningTourOutcomeIntent outcome:
                var state = await repository.GetStateAsync(tenantId, userId, tracked: false, cancellationToken);
                var retained = state?.Tours.SingleOrDefault(row => row.Family == outcome.Family
                    && row.MaterialVersion == outcome.MaterialVersion && row.LastIntentId == outcome.IntentId);
                // A retained exact outcome is self-owned; released claims retain no contextual grant.
                if (retained is null && state?.ActiveClaimId is not null)
                {
                    contextKey = state.ActiveContextKey
                        ?? throw new InvalidOperationException("The active learning context is missing.");
                    eventContextId = state.ActiveEventContextId;
                }
                break;
            case LearningPracticeIntent practice:
                contextKey = practice.ContextKey;
                if (practice.PracticeId is { } practiceId)
                {
                    var owned = await repository.GetPracticeAsync(tenantId, userId, practiceId, cancellationToken);
                    if (owned is not null && (owned.ContextKey != contextKey
                        || owned.RecipeKey != practice.RecipeKey || owned.RecipeVersion != practice.MaterialVersion))
                        throw new InvalidOperationException("The practice does not match its original context and recipe.");
                    if (owned is null)
                    {
                        var owner = await repository.GetStateAsync(
                            tenantId, userId, tracked: false, cancellationToken);
                        if (practice.Action != LearningPracticeAction.Clear || owner?.LastIntentId != practice.IntentId)
                            throw new NotFoundException("Learning practice", practiceId);
                    }
                }
                break;
        }
        Guid? actorId = RequireCanonicalContext(contextKey);
        if (eventContextId == Guid.Empty)
            throw new NotFoundException("Learning event context", eventContextId);
        bool requiresContextAuthority = intent is LearningTourClaimIntent
            or LearningTourOutcomeIntent { Outcome: LearningTourOutcome.InProgress }
            or LearningPracticeIntent { Action: not LearningPracticeAction.Clear };
        var workspaces = await LearningWorkspaceAvailability.ObserveAsync(
            services.GetRequiredService<IHierarchicalSettingsResolver>(),
            services.GetRequiredService<IAiProviderCapabilityResolver>(), tenantId, cancellationToken);
        RequireCustody(clock.GetUtcNow());
        if (intent is LearningTourClaimIntent { Family: LearningTourFamily.NormalAi } && !workspaces.NormalAi)
            throw new AuthorizationException("The native AI workspace is not ready.");

        var principalSource = services.GetRequiredService<IHumanPrincipalSourceFence>();
        var supportSource = await services.GetRequiredService<IHumanSupportAccessSourceFactory>()
            .CreateAsync(cancellationToken);
        var assignmentSource = eventContextId is { } eventId
            ? services.GetRequiredService<IHumanEventAssignmentSourceFactory>().Create(tenantId, [eventId]) : null;
        var sourceSet = services.GetRequiredService<IPolicySourceSetFence>();
        var originalPrincipal = await principalSource.ObserveAsync(cancellationToken);
        var originalSupport = await supportSource.ObserveAsync(cancellationToken);
        var originalAssignments = assignmentSource is null
            ? null : await assignmentSource.ObserveAsync(cancellationToken);
        RequirePrincipal(originalPrincipal, clock.GetUtcNow());
        RequireSupport(originalSupport);
        RequireAssignments(originalAssignments, clock.GetUtcNow());

        IPolicySourceSetLease? exclusion = null;
        IHumanPrincipalSourceLease? heldPrincipal = null;
        IHumanSupportAccessSourceLease? heldSupport = null;
        IHumanEventAssignmentSourceLease? heldAssignments = null;
        HumanPrincipalSourceFacts? finalPrincipal = null;
        HumanSupportAccessSourceFacts? finalSupport = null;
        HumanEventAssignmentSourceFacts? finalAssignments = null;
        DateTimeOffset preparedAt = clock.GetUtcNow();
        preparedAt = new DateTimeOffset(preparedAt.UtcTicks - preparedAt.UtcTicks % 10, TimeSpan.Zero);
        Mutation? mutation = null;
        mutation = new Mutation(userId, tenantId, contextKey, eventContextId, preparedAt.UtcDateTime,
            Guid.CreateVersion7(preparedAt), Guid.CreateVersion7(preparedAt),
            Guid.CreateVersion7(preparedAt), Guid.CreateVersion7(preparedAt), intent,
            RequireOriginalAsync, RequireCurrentAsync, RequireCurrentAt);
        _current = mutation;
        try
        {
            // The store acquires its original owner, calls RequireCurrentAsync and registers finalization.
            var result = await services.GetRequiredService<IHumanLearningMutationStore>()
                .ExecuteAsync(mutation, cancellationToken);
            return result with
            {
                StudioAvailable = workspaces.Studio(mutation.AvailableActors),
                NormalAiAvailable = workspaces.NormalAi,
                ObservedAtUtc = clock.GetUtcNow().UtcDateTime
            };
        }
        finally { _current = null; }

        Task RequireOriginalAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            RequireCustody(clock.GetUtcNow());
            return Task.CompletedTask;
        }

        async Task RequireCurrentAsync(CancellationToken token)
        {
            Mutation currentMutation = RequireCurrentReference();
            RequireCustody(clock.GetUtcNow());
            if (exclusion is null)
            {
                exclusion = await sourceSet.AcquireAsync(token);
                heldPrincipal = await principalSource.AcquireAsync(exclusion, token);
                RequirePrincipal(heldPrincipal.Facts, clock.GetUtcNow());
                heldSupport = await supportSource.AcquireAsync(exclusion, heldPrincipal, cancellationToken: token);
                RequireSupport(heldSupport.Facts);
                heldAssignments = assignmentSource is null
                    ? null : await assignmentSource.AcquireAsync(exclusion, token);
                RequireAssignments(heldAssignments?.Facts, clock.GetUtcNow());
            }
            await exclusion.ValidateAsync(token);
            var currentPrincipal = await heldPrincipal!.ValidateAsync(token);
            RequirePrincipal(currentPrincipal, clock.GetUtcNow());
            var currentSupport = await heldSupport!.ValidateAsync(token);
            RequireSupport(currentSupport);
            var currentAssignments = heldAssignments is null ? null : await heldAssignments.ValidateAsync(token);
            RequireAssignments(currentAssignments, clock.GetUtcNow());

            if (requiresContextAuthority && actorId is { } selectedActor)
            {
                var actor = await services.GetRequiredService<IActorRepository>()
                    .GetForModerationAsync(selectedActor, token);
                if (actor is null || actor.IsDeleted || actor.IsSuspended
                    || !LearningAccessPolicy.IsActorMember(currentPrincipal, actor))
                    throw new NotFoundException("Learning context", contextKey);
            }
            var actors = await services.GetRequiredService<IAiActorContextRepository>().GetAvailableActorsAsync(token);
            currentMutation.AvailableActors = Array.AsReadOnly(actors.Where(actor => !actor.IsDeleted && !actor.IsSuspended
                && LearningAccessPolicy.IsActorMember(currentPrincipal, actor)).ToArray());
            currentMutation.CanManageSpace = await services.GetRequiredService<ITenantAdministratorCommitAuthority>()
                .IsTenantAdministratorForCommitAsync(userId, tenantId, token);
            currentMutation.CanManageEvent = HasEventAuthority(currentAssignments, clock.GetUtcNow());
            if (intent is LearningSpaceIntent && !currentMutation.CanManageSpace)
                throw new AuthorizationException("Current tenant administrator authority is required.");
            if (intent is LearningTourClaimIntent { Family: LearningTourFamily.Studio }
                && !workspaces.Studio(currentMutation.AvailableActors))
                throw new AuthorizationException("The native Studio workspace is not available.");
            finalPrincipal = currentPrincipal;
            finalSupport = currentSupport;
            finalAssignments = currentAssignments;
            RequireCurrentAt(clock.GetUtcNow());
        }

        Mutation RequireCurrentReference()
        {
            if (mutation is null || !ReferenceEquals(_current, mutation))
                throw new UnauthorizedAccessException("The original learning mutation is no longer current.");
            return mutation;
        }

        void RequireCurrentAt(DateTimeOffset now)
        {
            RequireCurrentReference();
            RequireCustody(now);
            RequirePrincipal(finalPrincipal
                ?? throw new UnauthorizedAccessException("Current learning principal sources are missing."), now);
            RequireSupport(finalSupport
                ?? throw new UnauthorizedAccessException("Current learning support sources are missing."));
            RequireAssignments(finalAssignments, now);
        }

        void RequirePrincipal(HumanPrincipalSourceFacts current, DateTimeOffset now)
        {
            RequireCustody(now);
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, current);
            if (current.PrincipalId != userId || !LearningAccessPolicy.IsActiveLearner(current, tenantId)
                || !Same(originalPrincipal, current))
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
                throw new UnauthorizedAccessException("Support authority cannot mutate private learning state.");
        }

        void RequireAssignments(HumanEventAssignmentSourceFacts? current, DateTimeOffset now)
        {
            RequireCustody(now);
            if (eventContextId is null) return;
            if (current is null || current.PrincipalId != userId || current.TenantId != tenantId
                || !Same(originalAssignments, current))
                throw new UnauthorizedAccessException("Original learning event sources changed.");
            if (requiresContextAuthority && !HasEventAuthority(current, now))
                throw new NotFoundException("Learning event context", eventContextId);
        }

        bool HasEventAuthority(HumanEventAssignmentSourceFacts? current, DateTimeOffset now)
        {
            if (current is null || eventContextId is not { } selectedEvent) return false;
            var target = current.Events.SingleOrDefault(row => row.Id == selectedEvent
                && row.TenantId == tenantId && !row.IsDeleted);
            return target is not null
                && current.Actors.Any(row => row.Id == target.ActorId && !row.IsDeleted && !row.IsSuspended)
                && HumanEventAssignmentProjection.Authority(current, now.UtcDateTime).Events
                    .TryGetValue(selectedEvent, out var authority)
                && (authority.IsOwner || authority.IsManager
                    || authority.PermissionCodes.Contains(PermissionCodes.EventUpdate)
                    || authority.PermissionCodes.Contains(PermissionCodes.EventRegistrationManage)
                    || authority.PermissionCodes.Contains(PermissionCodes.EventCheckInManage));
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

    private static LearningTourFamily Map(Wire.LearningTourFamily family) => family switch
    {
        Wire.LearningTourFamily.Shell => LearningTourFamily.Shell,
        Wire.LearningTourFamily.Studio => LearningTourFamily.Studio,
        Wire.LearningTourFamily.EventManagement => LearningTourFamily.EventManagement,
        Wire.LearningTourFamily.NormalAi => LearningTourFamily.NormalAi,
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };

    private static LearningTourOutcome Map(Wire.LearningTourOutcome outcome) => outcome switch
    {
        Wire.LearningTourOutcome.InProgress => LearningTourOutcome.InProgress,
        Wire.LearningTourOutcome.Completed => LearningTourOutcome.Completed,
        Wire.LearningTourOutcome.Skipped => LearningTourOutcome.Skipped,
        Wire.LearningTourOutcome.Interrupted => LearningTourOutcome.Interrupted,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    private static LearningPracticeAction Map(Wire.LearningPracticeAction action) => action switch
    {
        Wire.LearningPracticeAction.Start => LearningPracticeAction.Start,
        Wire.LearningPracticeAction.Choose => LearningPracticeAction.Choose,
        Wire.LearningPracticeAction.Confirm => LearningPracticeAction.Confirm,
        Wire.LearningPracticeAction.Reset => LearningPracticeAction.Reset,
        Wire.LearningPracticeAction.Clear => LearningPracticeAction.Clear,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static bool Same<T>(T expected, T current) =>
        string.Equals(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(current), StringComparison.Ordinal);

    private sealed class Mutation(
        Guid subjectId, Guid tenantId, string contextKey, Guid? eventContextId, DateTime updatedAtUtc,
        Guid candidateStateId, Guid candidateSpaceId, Guid candidateChildId, Guid candidateClaimId,
        LearningMutationIntent intent, Func<CancellationToken, Task> requireOriginal,
        Func<CancellationToken, Task> requireCurrent, Action<DateTimeOffset> requireCurrentAt)
        : IHumanLearningMutation
    {
        public Guid SubjectId { get; } = subjectId;
        public Guid TenantId { get; } = tenantId;
        public string ContextKey { get; } = contextKey;
        public Guid? EventContextId { get; } = eventContextId;
        public DateTime UpdatedAtUtc { get; } = updatedAtUtc;
        public Guid CandidateStateId { get; } = candidateStateId;
        public Guid CandidateSpaceId { get; } = candidateSpaceId;
        public Guid CandidateChildId { get; } = candidateChildId;
        public Guid CandidateClaimId { get; } = candidateClaimId;
        public LearningMutationIntent Intent { get; } = intent;
        public IReadOnlyList<Actor> AvailableActors { get; set; } = Array.AsReadOnly(Array.Empty<Actor>());
        public bool CanManageSpace { get; set; }
        public bool CanManageEvent { get; set; }
        public Task RequireOriginalAsync(CancellationToken cancellationToken) => requireOriginal(cancellationToken);
        public Task RequireCurrentAsync(CancellationToken cancellationToken) => requireCurrent(cancellationToken);
        public void RequireCurrentAt(DateTimeOffset utcNow) => requireCurrentAt(utcNow);
        public override string ToString() => nameof(Mutation);
    }
}
