using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IEventOrganizerClaimService
{
    Task<ICollection<HalResourceOfEventOrganizerClaimDto>> GetClaimantOrganizerClaimsAsync(Guid claimantActorId, CancellationToken cancellationToken = default);
    Task<bool> SubmitEventOrganizerClaimAsync(Guid eventId, SubmitEventOrganizerClaimDto request, CancellationToken cancellationToken = default);
    Task<bool> WithdrawEventOrganizerClaimAsync(Guid eventId, Guid claimId, Guid? concurrencyStamp, CancellationToken cancellationToken = default);
}
