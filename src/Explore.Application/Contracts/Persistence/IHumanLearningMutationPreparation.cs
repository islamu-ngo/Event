using Explore.Application.Features.Learning;
using Explore.Application.Features.Learning.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Learning;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Prepares exactly one closed learning intent under the original authenticated human.</summary>
public interface IHumanLearningMutationPreparation
{
    IHumanLearningMutation? CurrentMutation { get; }

    Task<LearningOperationState> ExecuteAsync(ClaimLearningTourCommand command, CancellationToken cancellationToken);
    Task<LearningOperationState> ExecuteAsync(RecordLearningTourOutcomeCommand command, CancellationToken cancellationToken);
    Task<LearningOperationState> ExecuteAsync(UpdateLearningPreferencesCommand command, CancellationToken cancellationToken);
    Task<LearningOperationState> ExecuteAsync(ApplyLearningPracticeActionCommand command, CancellationToken cancellationToken);
    Task<LearningOperationState> ExecuteAsync(ManageTenantLearningSpaceCommand command, CancellationToken cancellationToken);
}

/// <summary>The store accepts only its registered preparation's current reference, never an implemented grant.</summary>
public interface IHumanLearningMutation
{
    Guid SubjectId { get; }
    Guid TenantId { get; }
    string ContextKey { get; }
    Guid? EventContextId { get; }
    DateTime UpdatedAtUtc { get; }
    Guid CandidateStateId { get; }
    Guid CandidateSpaceId { get; }
    Guid CandidateChildId { get; }
    Guid CandidateClaimId { get; }
    LearningMutationIntent Intent { get; }
    IReadOnlyList<Actor> AvailableActors { get; }
    bool CanManageSpace { get; }
    bool CanManageEvent { get; }

    Task RequireOriginalAsync(CancellationToken cancellationToken);
    Task RequireCurrentAsync(CancellationToken cancellationToken);
    void RequireCurrentAt(DateTimeOffset utcNow);
}

/// <summary>Owns the original physical settlement and the exact permitted learning completion image.</summary>
public interface IHumanLearningMutationStore
{
    Task<LearningOperationState> ExecuteAsync(IHumanLearningMutation mutation, CancellationToken cancellationToken);
    Task RequireCompleteAsync(IHumanLearningMutation mutation, CancellationToken cancellationToken);
}

public abstract record LearningMutationIntent(long ExpectedRevision);

public sealed record LearningTourClaimIntent(
    long ExpectedRevision, Guid IntentId, LearningTourFamily Family, int MaterialVersion,
    string ContextKey, Guid? EventContextId) : LearningMutationIntent(ExpectedRevision);

public sealed record LearningTourOutcomeIntent(
    long ExpectedRevision, Guid IntentId, Guid? ClaimToken, LearningTourFamily Family,
    int MaterialVersion, LearningTourOutcome Outcome, int StepIndex) : LearningMutationIntent(ExpectedRevision);

public sealed record LearningPreferencesIntent(
    long ExpectedRevision, bool SuppressAutomaticTours, bool HideLearning, bool PinLearning)
    : LearningMutationIntent(ExpectedRevision);

public sealed record LearningPracticeIntent(
    long ExpectedRevision, Guid IntentId, long ExpectedPracticeRevision, Guid? PracticeId,
    string ContextKey, string RecipeKey, int MaterialVersion, LearningPracticeAction Action, string? ChoiceKey)
    : LearningMutationIntent(ExpectedRevision);

public enum LearningSpaceMutationAction { Remove, Recreate }

public sealed record LearningSpaceIntent(
    long ExpectedRevision, LearningSpaceMutationAction Action) : LearningMutationIntent(ExpectedRevision);
