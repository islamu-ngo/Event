using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Learning;

/// <summary>Exposes tour state and explicit guidance transitions to components.</summary>
public interface ILearningOnboardingCoordinator
{
    event Action? Changed;
    HalResourceOfLearningTourPlanDto? Plan { get; }
    bool IsActive { get; }
    bool IsBusy { get; }
    string? Failure { get; }
    bool CanStart { get; }
    Task OfferAsync(bool automatic = false);
    Task StartAsync();
    Task NextAsync();
    Task SkipAsync();
    Task SnoozeAsync(LearningSnoozeDuration duration);
    Task CancelAsync();
    Task CloseAsync();
}
