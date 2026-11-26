using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services.Learning;

namespace Explore.Blazor.Client.Contracts.Services.Learning;

/// <summary>Connects the mounted host callbacks to the scoped tour renderer.</summary>
public interface ILearningTourHost
{
    void Attach(
        Func<LearningTourStepDto, LearningTarget, CancellationToken, Task> show, Func<Task> close);
    void Detach();
}
