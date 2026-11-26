using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Learning;

namespace Explore.Blazor.Client.Services.Learning;

/// <summary>Connects the single mounted host to the scoped coordinator.</summary>
public sealed class GuideFlowLearningTourRenderer : ILearningTourRenderer, ILearningTourHost
{
    private Func<LearningTourStepDto, LearningTarget, CancellationToken, Task>? _show;
    private Func<Task>? _close;

    public void Attach(
        Func<LearningTourStepDto, LearningTarget, CancellationToken, Task> show, Func<Task> close)
    {
        if (_show is not null)
            throw new InvalidOperationException("Only one learning tour host may own presentation.");
        _show = show;
        _close = close;
    }

    public void Detach()
    {
        _show = null;
        _close = null;
    }

    public Task ShowAsync(LearningTourStepDto step, LearningTarget target, CancellationToken cancellationToken) =>
        _show?.Invoke(step, target, cancellationToken)
            ?? throw new InvalidOperationException("The interactive learning tour host is not mounted.");

    public Task CloseAsync() => _close?.Invoke() ?? Task.CompletedTask;
}
