using Explore.Application.Exceptions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Responses;
using Explore.Domain.Learning;
using ISLAMU.Wire.Contracts.Learning;
using WireFamily = ISLAMU.Wire.Contracts.Learning.LearningTourFamily;
using WireOutcome = ISLAMU.Wire.Contracts.Learning.LearningTourOutcome;

namespace Explore.Application.Features.Learning;

/// <summary>Maps one authorized operation image without resolving authority or adding HAL links.</summary>
internal static class LearningRepresentation
{
    internal static LearningContextDto Context(LearningOperationState operation)
    {
        UserLearningState? state = operation.State;
        LearningSourceFacts sources = operation.Sources
            ?? throw new InvalidOperationException("Learning representation requires its retained source image.");
        bool currentPack = operation.Space is null
            || operation.Space is
            {
                IsRemoved: false, PackKey: LearningLessonCatalog.PackKey,
                PackVersion: LearningLessonCatalog.MaterialVersion
            };
        LearningGuidancePolicy policy = LearningGuidancePolicyCodec.Decode(sources.GuidanceDocument);
        bool canPractice = LearningAccessPolicy.CanPractice(policy, true, operation.CanManageSpace);
        LearningDisplayTextDto? practiceUnavailableReason = !currentPack
            ? Reason("source-unavailable", "The shared learning source is removed or its material is unavailable.")
            : !policy.Enabled ? Reason("policy-disabled", "Interactive guidance is disabled by the tenant policy.")
            : !canPractice ? Reason("manual-audience", "The tenant policy does not allow practice for the current member.")
            : null;
        bool canClaimTours = false;
        bool canInviteTours = false;
        var tours = new List<LearningTourProgressDto>();
        foreach (WireFamily family in Enum.GetValues<WireFamily>())
        {
            LearningTourProgress[] history = state?.Tours
                .Where(tour => (int)tour.Family == (int)family)
                .OrderByDescending(tour => tour.MaterialVersion).ToArray() ?? [];
            var currentPlan = LearningTourPlanResolver.Resolve(operation, sources, family);
            bool available = currentPlan.Plan.IsAvailable;
            canClaimTours |= currentPlan.Plan.CanClaim;
            canInviteTours |= currentPlan.Plan.CanInvite;
            bool covered = currentPlan.Definition is not null && state?.HasTourCoverage(currentPlan.Definition) == true;
            foreach (LearningTourProgress tour in history)
                tours.Add(new()
                {
                    Family = family,
                    MaterialVersion = tour.MaterialVersion,
                    Outcome = Outcome(tour.Outcome),
                    LastStepKey = tour.LastStepKey,
                    SnoozedUntilUtc = tour.SnoozedUntilUtc is { } snoozed
                        ? new DateTimeOffset(DateTime.SpecifyKind(snoozed, DateTimeKind.Utc)) : null,
                    CurrentCoverage = tour.MaterialVersion == LearningExperienceCatalog.MaterialVersion && covered,
                    CompletedSteps = tour.Steps.Select(step => new LearningTourStepCompletionDto
                    {
                        StepKey = step.StepKey,
                        StepVersion = step.StepVersion,
                        CompletedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(step.CompletedAtUtc, DateTimeKind.Utc))
                    }).ToArray(),
                    IsAvailable = available && tour.MaterialVersion == LearningExperienceCatalog.MaterialVersion,
                    CanInvite = currentPlan.Plan.CanInvite && tour.MaterialVersion == LearningExperienceCatalog.MaterialVersion,
                    UnavailableReason = tour.MaterialVersion != LearningExperienceCatalog.MaterialVersion
                        ? Reason("material-unavailable", "This historical tour material is not currently available.")
                        : currentPlan.Plan.UnavailableReason
                });
            if (history.All(tour => tour.MaterialVersion != LearningExperienceCatalog.MaterialVersion))
                tours.Add(new()
                {
                    Family = family,
                    MaterialVersion = LearningExperienceCatalog.MaterialVersion,
                    CurrentCoverage = covered,
                    IsAvailable = available,
                    CanInvite = currentPlan.Plan.CanInvite,
                    UnavailableReason = currentPlan.Plan.UnavailableReason
                });
        }

        return new()
        {
            TenantId = operation.TenantId,
            UserId = operation.UserId,
            ScopeStamp = LearningAccessPolicy.ScopeStamp(operation.TenantId, operation.UserId),
            ContextKey = operation.ContextKey,
            EventContextId = operation.EventContextId,
            StateId = state?.Id,
            ResourceRevision = operation.Space?.Revision ?? 0,
            LearnerRevision = state?.Revision ?? 0,
            SpaceId = operation.Space?.Id,
            SpaceStatus = SpaceStatus(operation.Space),
            Requirement = LearningAccessPolicy.Requirement(operation, policy),
            Preferences = new()
            {
                SuppressAutomaticTours = state?.SuppressAutomaticTours ?? false,
                HideLearning = state?.IsHidden ?? false,
                PinLearning = state?.IsPinned ?? false
            },
            Capabilities = new()
            {
                CanLearn = true,
                CanClaimTours = canClaimTours,
                CanInviteTours = canInviteTours,
                CanSimulate = currentPack && canPractice,
                CanClearPractice = state?.Practices.Count > 0,
                CanManageSpace = operation.CanManageSpace,
                UnavailableReason = practiceUnavailableReason
            },
            Claim = Claim(operation),
            Tours = tours,
            Lessons = currentPack && canPractice ? LearningLessonCatalog.Lessons : [],
            OwnedPractices = state?.Practices.Select(practice => new LearningPracticeSummaryDto
            {
                Id = practice.Id,
                ContextKey = practice.ContextKey,
                RecipeKey = practice.RecipeKey,
                MaterialVersion = practice.RecipeVersion,
                Revision = practice.Revision,
                PreviewAvailable = currentPack && canPractice
                    && LearningLessonCatalog.TryGetLesson(practice.RecipeKey, practice.RecipeVersion, out _)
                    && (practice.ContextKey == "self" || practice.ContextKey == operation.ContextKey
                        || operation.AvailableActors.Any(actor =>
                        !actor.IsDeleted && !actor.IsSuspended && practice.ContextKey == $"actor:{actor.Id:D}")),
                UnavailableReason = practiceUnavailableReason
                    ?? (!LearningLessonCatalog.TryGetLesson(practice.RecipeKey, practice.RecipeVersion, out _)
                        ? Reason("material-unavailable", "This practice material is not currently available.")
                        : practice.ContextKey == "self" || practice.ContextKey == operation.ContextKey
                            || operation.AvailableActors.Any(actor => !actor.IsDeleted && !actor.IsSuspended
                                && practice.ContextKey == $"actor:{actor.Id:D}") ? null
                        : Reason("context-unavailable", "The current context does not expose this practice."))
            }).ToArray() ?? []
        };
    }

    internal static LearningPracticeDto Practice(LearningOperationState operation)
    {
        LearningPracticeState practice = operation.Practice
            ?? throw new NotFoundException("Learning practice", operation.ContextKey);
        LearningSourceFacts sources = operation.Sources
            ?? throw new InvalidOperationException("Practice projection requires its retained source image.");
        if (!LearningAccessPolicy.CanPractice(LearningGuidancePolicyCodec.Decode(sources.GuidanceDocument),
                true, operation.CanManageSpace))
            throw new AuthorizationException("The current guidance policy does not allow this practice preview.");
        LearningPracticeChoiceSlot slot = practice.Choice switch
        {
            LearningPracticeChoice.None => LearningPracticeChoiceSlot.None,
            LearningPracticeChoice.Primary => LearningPracticeChoiceSlot.Primary,
            LearningPracticeChoice.Secondary => LearningPracticeChoiceSlot.Secondary,
            LearningPracticeChoice.Tertiary => LearningPracticeChoiceSlot.Tertiary,
            _ => throw new InvalidOperationException("The persisted learning choice is not supported.")
        };
        if (!LearningLessonCatalog.TryRestore(practice.RecipeKey, practice.RecipeVersion,
                slot, practice.Step, practice.Value, practice.IsConfirmed, out var simulation))
            throw new NotFoundException("Learning material for practice", practice.Id);

        return new()
        {
            Id = practice.Id,
            ScopeStamp = LearningAccessPolicy.ScopeStamp(operation.TenantId, operation.UserId),
            ContextKey = practice.ContextKey,
            RecipeKey = practice.RecipeKey,
            MaterialVersion = practice.RecipeVersion,
            Revision = practice.Revision,
            LearnerRevision = operation.State?.Revision
                ?? throw new InvalidOperationException("An owned practice requires its learner state."),
            CanSimulate = operation.Space is
            {
                IsRemoved: false, PackKey: LearningLessonCatalog.PackKey,
                PackVersion: LearningLessonCatalog.MaterialVersion
            },
            CanClearPractice = true,
            Simulation = simulation
        };
    }

    internal static LearningMutationResult Mutation(
        LearningOperationState operation, Guid affectedId,
        bool spaceMutation = false, bool cleared = false)
    {
        LearningContextDto context = Context(operation);
        LearningPracticeDto? practice = !cleared && operation.Practice is not null ? Practice(operation) : null;
        return new(BaseCommandResponse.Success(affectedId), new()
        {
            Id = affectedId,
            ScopeStamp = context.ScopeStamp,
            ResourceRevision = spaceMutation
                ? operation.Space?.Revision ?? throw new InvalidOperationException("A space mutation requires its settled space.")
                : practice?.Revision ?? operation.State?.Revision ?? 0,
            LearnerRevision = context.LearnerRevision,
            State = context,
            Practice = practice,
            Claim = context.Claim,
            Space = operation.Space is { } space ? new()
            {
                Id = space.Id,
                Status = SpaceStatus(space),
                PackKey = space.PackKey,
                MaterialVersion = space.PackVersion,
                Revision = space.Revision
            } : null,
            SimulationStatus = cleared ? LearningSimulationStatus.Cleared : practice?.Simulation.Status
        });
    }

    private static LearningTourClaimDto? Claim(LearningOperationState operation)
    {
        UserLearningState? state = operation.State;
        if (state?.ActiveClaimId is not { } token || state.ClaimExpiresAtUtc is not { } expires
            || operation.ObservedAtUtc == default
            || expires <= operation.ObservedAtUtc
            || state.ActiveContextKey != operation.ContextKey
            || state.ActiveEventContextId != operation.EventContextId)
            return null;
        LearningTourProgress tour = state.Tours.Single(value => value.Id == state.ActiveTourId);
        if (tour.MaterialVersion != LearningExperienceCatalog.MaterialVersion
            || state.ActivePlanFingerprint is null || state.ActivePolicySourceStamp is null)
            return null;
        return new()
        {
            Token = token,
            Family = (WireFamily)tour.Family,
            MaterialVersion = tour.MaterialVersion,
            ExpiresAtUtc = new DateTimeOffset(expires, TimeSpan.Zero),
            Revision = state.Revision,
            PlanFingerprint = state.ActivePlanFingerprint,
            PolicySourceStamp = state.ActivePolicySourceStamp
        };
    }

    private static LearningSpaceStatus SpaceStatus(TenantLearningSpace? space) =>
        space is null ? LearningSpaceStatus.Absent
        : space.IsRemoved ? LearningSpaceStatus.Removed : LearningSpaceStatus.Active;

    private static WireOutcome? Outcome(Explore.Domain.Learning.LearningTourOutcome outcome) => outcome switch
    {
        Explore.Domain.Learning.LearningTourOutcome.None => null,
        Explore.Domain.Learning.LearningTourOutcome.InProgress => WireOutcome.InProgress,
        Explore.Domain.Learning.LearningTourOutcome.Completed => WireOutcome.Completed,
        Explore.Domain.Learning.LearningTourOutcome.Skipped => WireOutcome.Skipped,
        Explore.Domain.Learning.LearningTourOutcome.Interrupted => WireOutcome.Interrupted,
        _ => throw new InvalidOperationException("The persisted learning tour outcome is not supported.")
    };

    private static LearningDisplayTextDto Reason(string key, string fallback) =>
        new($"Learning.Availability.{key}", fallback);
}
