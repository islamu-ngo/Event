using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Events;

public interface IPrivateHomeOwnershipService
{
    Task<BaseCommandResponseOfGuid> ClassifyAsPrivateHomeAsync(
        Guid locationId,
        Guid expectedConcurrencyStamp,
        PrivateHomeOwnershipConsentDto consent,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> AcceptOwnershipAsync(
        Guid locationId,
        Guid expectedConcurrencyStamp,
        PrivateHomeOwnershipConsentDto consent,
        CancellationToken cancellationToken = default);
}
