using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

/// <summary>Retains current caller publication restrictions through the owning native commit.</summary>
public interface IEventPublicationMutationBoundary
{
    Task<BaseCommandResponse<Guid>> ExecuteAsync(Guid eventId,
        Func<CancellationToken, Task<BaseCommandResponse<Guid>>> operation,
        CancellationToken cancellationToken);
}
