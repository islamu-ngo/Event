using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services.Learning;

namespace Explore.Blazor.Client.Contracts.Services.Learning;

public interface ILearningClientService : IDisposable
{
    event Action? Changed;

    LearningClientScope? Scope { get; }
    HalResourceOfLearningContextDto? Context { get; }
    HalResourceOfLearningPracticeDto? Practice { get; }
    LearningClientFailure Failure { get; }
    bool IsBusy { get; }
    bool IsSaving { get; }
    bool RequiresContextRefresh { get; }
    bool? LastMutationApplied { get; }
    bool CanSubmit { get; }
    IEnumerable<LearningLessonDto> Lessons { get; }
    IEnumerable<LearningPracticeSummaryDto> OwnedPractices { get; }

    bool HasContextAction(string relation);
    bool HasPracticeAction(string relation);
    bool CanPreview(LearningPracticeSummaryDto practice);
    void SetTenantContext(Guid? tenantId);
    Task LoadContextAsync(CancellationToken cancellationToken = default);
    Task LoadPracticeAsync(Guid practiceId, CancellationToken cancellationToken = default);
    Task<bool> UpdatePreferencesAsync(
        bool suppressAutomaticTours, bool hideLearning, bool pinLearning,
        CancellationToken cancellationToken = default);
    Task<bool> AcknowledgeRequirementAsync(
        LearningRequirementDto requirement,
        CancellationToken cancellationToken = default);
    Task<Guid?> StartPracticeAsync(LearningLessonDto lesson, CancellationToken cancellationToken = default);
    Task<bool> ApplyPracticeActionAsync(
        LearningPracticeAction action, string? choiceKey = null,
        CancellationToken cancellationToken = default);
    Task<bool> ClearPracticeAsync(
        LearningPracticeSummaryDto practice, CancellationToken cancellationToken = default);
    Task<bool> ManageSpaceAsync(
        LearningSpaceAction action, CancellationToken cancellationToken = default);
    void Clear();
}
