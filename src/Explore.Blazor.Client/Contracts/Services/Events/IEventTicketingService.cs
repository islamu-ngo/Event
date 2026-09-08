using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Pages.Studio;

namespace Explore.Blazor.Client.Contracts.Services.Events;

public interface IEventTicketingService
{
    Task<EventTicketCatalogState?> GetCatalogAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<HalResourceOfPaidEventPublicationPreflightDto> GetPaidPublicationPreflightAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> CreateDraftAsync(Guid eventId, CreateEventTicketCatalogDraftCommand request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> CloneDraftAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> CreateTicketTypeAsync(Guid eventId, ManageEventTicketTypeDto request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> UpdateTicketTypeAsync(Guid eventId, Guid ticketTypeId, ManageEventTicketTypeDto request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> DeleteTicketTypeAsync(Guid eventId, Guid ticketTypeId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> CreateCapacityPoolAsync(Guid eventId, ManageEventCapacityPoolDto request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> UpdateCapacityPoolAsync(Guid eventId, Guid capacityPoolId, ManageEventCapacityPoolDto request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> DeleteCapacityPoolAsync(Guid eventId, Guid capacityPoolId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> UpdateCommercialDisclosuresAsync(Guid eventId, UpdateEventTicketCatalogCommercialDisclosuresCommand request, CancellationToken cancellationToken = default);
    Task<HalResourceOfEventOrganizerPaymentConnectionManagementDto> GetPaymentConnectionAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfOrganizerPaymentOnboardingLinkResult> StartPaymentOnboardingAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task ReturnPaymentOnboardingAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task RefreshPaymentOnboardingAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> PublishAsync(Guid eventId, CancellationToken cancellationToken = default);
}
