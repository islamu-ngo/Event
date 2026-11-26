using Explore.Application.Features.Learning.Requests.Commands;
using Explore.Domain.Learning;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Issues only the original TenantSetting/update action for tenant.learning_guidance.</summary>
public interface IHumanLearningPolicyMutationPreparation
{
    IHumanLearningPolicyMutation? CurrentMutation { get; }
    Task<ResolvedLearningGuidancePolicy> ExecuteAsync(
        UpdateLearningGuidancePolicyCommand command, CancellationToken cancellationToken);
}

/// <summary>Detached source facts; only the issuing store can admit their original observation.</summary>
public interface ILearningPolicyOriginalSources
{
    LearningGuidanceDocumentSource? Document { get; }
    bool AllowTenantEdits { get; }
}

/// <summary>
/// An issuer-owned, single-attempt action. Implementing this interface does not grant write authority:
/// the store requires the preparation's current private instance and its own exact observation.
/// </summary>
public interface IHumanLearningPolicyMutation
{
    UpdateLearningGuidancePolicyCommand OriginalCommand { get; }
    Guid TenantId { get; }
    Guid SubjectId { get; }
    Guid CandidateDocumentId { get; }
    Guid CandidateStamp { get; }
    DateTime ChangedAtUtc { get; }
    LearningGuidancePolicy Policy { get; }
    ILearningPolicyOriginalSources OriginalSources { get; }
    Task RequireOriginalAsync(CancellationToken cancellationToken);
    Task AcquireAsync(CancellationToken cancellationToken);
    Task VerifyAsync(CancellationToken cancellationToken);
    void RequireCurrentAt(DateTimeOffset utcNow);
}

/// <summary>Closed storage operation; no caller callback, arbitrary key or external transaction is accepted.</summary>
public interface IHumanLearningPolicyMutationStore
{
    Task<ILearningPolicyOriginalSources> ObserveAsync(
        UpdateLearningGuidancePolicyCommand command, Guid tenantId, CancellationToken cancellationToken);
    Task<ResolvedLearningGuidancePolicy> ExecuteAsync(
        IHumanLearningPolicyMutation mutation, CancellationToken cancellationToken);
}
