using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

/// <summary>Retains current caller publication restrictions through the owning native commit.</summary>
public interface IEventPublicationMutationBoundary
{
    Task<BaseCommandResponse<Guid>> ExecuteAsync(Guid eventId,
        Func<CancellationToken, Task<BaseCommandResponse<Guid>>> operation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Acquires publication restrictions inside the selected original human owner without
    /// starting another transaction. The implementation must retain its source fences and
    /// register terminal validation with that owner's actual primary and shared privacy commit.
    /// </summary>
    Task<BaseCommandResponse<Guid>> ExecuteInCurrentTransactionAsync(Guid eventId,
        Func<CancellationToken, Task<BaseCommandResponse<Guid>>> operation,
        CancellationToken cancellationToken);
}
