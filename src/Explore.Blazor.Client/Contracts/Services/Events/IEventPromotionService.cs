using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Pages.Studio;

namespace Explore.Blazor.Client.Contracts.Services.Events;

public interface IEventPromotionService
{
    Task<PromotionManagementCollectionState?> GetPromotionsAsync(Guid eventId, Guid ticketCatalogVersionId, CancellationToken cancellationToken = default);
    Task<PromotionCodeIssuedCommandResponseDto> CreateDraftAsync(Guid eventId, CreatePromotionDraftRequest request, CancellationToken cancellationToken = default);
    Task<PromotionManagementCommandResponseDto> ReviseAsync(Guid eventId, Guid definitionId, RevisePromotionRequest request, CancellationToken cancellationToken = default);
    Task<PromotionManagementCommandResponseDto> PublishAsync(Guid eventId, Guid definitionId, PromotionCodeRequest request, CancellationToken cancellationToken = default);
    Task<PromotionManagementCommandResponseDto> RevokeAsync(Guid eventId, Guid definitionId, RevokePromotionRequest request, CancellationToken cancellationToken = default);
    Task<PromotionCodeIssuedCommandResponseDto> RotateCodeAsync(Guid eventId, Guid definitionId, PromotionCodeRequest request, CancellationToken cancellationToken = default);
}
