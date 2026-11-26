using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services.Learning;

/// <summary>Presentation only; no claim, policy or durable progress authority.</summary>
public interface ILearningTourRenderer
{
    Task ShowAsync(LearningTourStepDto step, LearningTarget target, CancellationToken cancellationToken);
    Task CloseAsync();
}
