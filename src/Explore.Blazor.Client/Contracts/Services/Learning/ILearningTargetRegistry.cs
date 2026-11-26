using Explore.Blazor.Client.Services.Learning;

namespace Explore.Blazor.Client.Contracts.Services.Learning;

/// <summary>Publishes rendered targets and observes their current presentation scope.</summary>
public interface ILearningTargetRegistry
{
    event Action? Changed;
    LearningPresentationContext? Current { get; }
    bool IsBlocked { get; }
    void Publish(object owner, LearningPresentationContext context,
        bool blocked, params LearningTarget[] targets);
    void Remove(object owner);
    LearningTarget? Find(LearningPresentationContext context, string key);
    Task<LearningTarget> WaitAsync(LearningPresentationContext context,
        string key, CancellationToken cancellationToken);
}
