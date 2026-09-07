using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;

namespace Explore.Blazor.Client.Services;

public class EventOrganizerClaimService(
    IEventOrganizerClaimClient organizerClaimClient,
    ILogger<EventOrganizerClaimService> logger) : IEventOrganizerClaimService
{
    public async Task<ICollection<HalResourceOfEventOrganizerClaimDto>> GetClaimantOrganizerClaimsAsync(
        Guid claimantActorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await organizerClaimClient.GetClaimantOrganizerClaimsAsync(claimantActorId, cancellationToken: cancellationToken);
            return response._embedded?.Items ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching organizer claims for claimant {ClaimantActorId}", claimantActorId);
            return [];
        }
    }

    public async Task<bool> SubmitEventOrganizerClaimAsync(
        Guid eventId,
        SubmitEventOrganizerClaimDto request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await organizerClaimClient.SubmitEventOrganizerClaimAsync(eventId, request, cancellationToken: cancellationToken);
            return response.Success == true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error submitting organizer claim for event {EventId}", eventId);
            return false;
        }
    }

    public async Task<bool> WithdrawEventOrganizerClaimAsync(
        Guid eventId,
        Guid claimId,
        Guid? concurrencyStamp,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await organizerClaimClient.WithdrawEventOrganizerClaimAsync(
                eventId,
                claimId,
                concurrencyStamp?.ToString(),
                cancellationToken: cancellationToken);
            return response.Success == true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error withdrawing organizer claim {ClaimId} for event {EventId}", claimId, eventId);
            return false;
        }
    }
}
