using Explore.Application.Exceptions;
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
        bool currentPack = operation.Space is null
            || operation.Space is { IsRemoved: false, PackKey: LearningLessonCatalog.PackKey,
                PackVersion: LearningLessonCatalog.MaterialVersion };
        bool hasLiveClaim = state?.ActiveClaimId is not null
            && (operation.ObservedAtUtc == default || state.ClaimExpiresAtUtc > operation.ObservedAtUtc);
        var tours = new List<LearningTourProgressDto>();
        foreach (WireFamily family in Enum.GetValues<WireFamily>())
        {
            LearningTourProgress[] history = state?.Tours
                .Where(tour => (int)tour.Family == (int)family)
                .OrderByDescending(tour => tour.MaterialVersion).ToArray() ?? [];
            bool available = currentPack && IsFamilyAvailable(operation, family);
            foreach (LearningTourProgress tour in history)
                tours.Add(new()
                {
                    Family = family,
                    MaterialVersion = tour.MaterialVersion,
                    Outcome = Outcome(tour.Outcome),
                    StepIndex = tour.Step,
                    IsAvailable = available && tour.MaterialVersion == LearningLessonCatalog.MaterialVersion,
                    UnavailableReason = tour.MaterialVersion != LearningLessonCatalog.MaterialVersion
                        ? Reason("material-unavailable", "This historical tour material is not currently available.")
                        : available ? null : TourUnavailableReason(family, currentPack)
                });
            if (history.All(tour => tour.MaterialVersion != LearningLessonCatalog.MaterialVersion))
                tours.Add(new()
                {
                    Family = family,
                    MaterialVersion = LearningLessonCatalog.MaterialVersion,
                    StepIndex = 0,
                    IsAvailable = available,
                    UnavailableReason = available ? null : TourUnavailableReason(family, currentPack)
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
            Preferences = new()
            {
                SuppressAutomaticTours = state?.SuppressAutomaticTours ?? false,
                HideLearning = state?.IsHidden ?? false,
                PinLearning = state?.IsPinned ?? false
            },
            Capabilities = new()
            {
                CanLearn = true,
                CanClaimTours = currentPack && !hasLiveClaim,
                CanSimulate = currentPack,
                CanClearPractice = state?.Practices.Count > 0,
                CanManageSpace = operation.CanManageSpace,
                UnavailableReason = currentPack ? null
                    : Reason("source-unavailable", "The shared learning source is removed or its material is unavailable.")
            },
            Claim = Claim(operation),
            Tours = tours,
            Lessons = currentPack ? LearningLessonCatalog.Lessons : [],
            OwnedPractices = state?.Practices.Select(practice => new LearningPracticeSummaryDto
            {
                Id = practice.Id,
                ContextKey = practice.ContextKey,
                RecipeKey = practice.RecipeKey,
                MaterialVersion = practice.RecipeVersion,
                Revision = practice.Revision,
                PreviewAvailable = currentPack
                    && LearningLessonCatalog.TryGetLesson(practice.RecipeKey, practice.RecipeVersion, out _)
                    && (practice.ContextKey == "self" || practice.ContextKey == operation.ContextKey
                        || operation.AvailableActors.Any(actor =>
                        !actor.IsDeleted && !actor.IsSuspended && practice.ContextKey == $"actor:{actor.Id:D}"))
            }).ToArray() ?? []
        };
    }

    internal static LearningPracticeDto Practice(LearningOperationState operation)
    {
        LearningPracticeState practice = operation.Practice
            ?? throw new NotFoundException("Learning practice", operation.ContextKey);
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
            CanSimulate = operation.Space is { IsRemoved: false, PackKey: LearningLessonCatalog.PackKey,
                PackVersion: LearningLessonCatalog.MaterialVersion },
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
        return new()
        {
            Token = token,
            Family = (WireFamily)tour.Family,
            MaterialVersion = tour.MaterialVersion,
            ExpiresAtUtc = new DateTimeOffset(expires, TimeSpan.Zero),
            Revision = state.Revision
        };
    }

    private static bool IsFamilyAvailable(LearningOperationState operation, WireFamily family) => family switch
    {
        WireFamily.Shell => true,
        WireFamily.Studio => operation.StudioAvailable,
        WireFamily.EventManagement => operation.CanManageEvent,
        WireFamily.NormalAi => operation.NormalAiAvailable,
        _ => false
    };

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

    private static LearningDisplayTextDto TourUnavailableReason(WireFamily family, bool currentPack) =>
        !currentPack
            ? Reason("source-unavailable", "The shared learning source is removed or its material is unavailable.")
            : family == WireFamily.NormalAi
                ? Reason("normal-ai-unavailable", "Normal AI is unavailable in the current context.")
                : Reason("context-unavailable", "The current context does not expose this tour capability.");

    private static LearningDisplayTextDto Reason(string key, string fallback) =>
        new($"Learning.Availability.{key}", fallback);
}
