using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Pages.Studio;

namespace Explore.Blazor.Client.Services;

public sealed class EventPromotionService(IEventPromotionsClient apiClient) : IEventPromotionService
{
    public async Task<PromotionManagementCollectionState?> GetPromotionsAsync(Guid eventId, Guid ticketCatalogVersionId, CancellationToken cancellationToken = default)
    {
        HalCollectionResourceOfPromotionManagementDto resource = await apiClient.GetEventPromotionsAsync(eventId, ticketCatalogVersionId, cancellationToken: cancellationToken);
        return PromotionManagementCollectionState.TryParse(resource, eventId, ticketCatalogVersionId, out PromotionManagementCollectionState? state) ? state : null;
    }

    public Task<PromotionCodeIssuedCommandResponseDto> CreateDraftAsync(Guid eventId, CreatePromotionDraftRequest request, CancellationToken cancellationToken = default) =>
        apiClient.CreateEventPromotionDraftAsync(eventId, NewIdempotencyKey(), request, cancellationToken: cancellationToken);

    public Task<PromotionManagementCommandResponseDto> ReviseAsync(Guid eventId, Guid definitionId, RevisePromotionRequest request, CancellationToken cancellationToken = default) =>
        apiClient.ReviseEventPromotionAsync(eventId, definitionId, NewIdempotencyKey(), request, cancellationToken: cancellationToken);

    public Task<PromotionManagementCommandResponseDto> PublishAsync(Guid eventId, Guid definitionId, PromotionCodeRequest request, CancellationToken cancellationToken = default) =>
        apiClient.PublishEventPromotionAsync(eventId, definitionId, NewIdempotencyKey(), request, cancellationToken: cancellationToken);

    public Task<PromotionManagementCommandResponseDto> RevokeAsync(Guid eventId, Guid definitionId, RevokePromotionRequest request, CancellationToken cancellationToken = default) =>
        apiClient.RevokeEventPromotionAsync(eventId, definitionId, NewIdempotencyKey(), request, cancellationToken: cancellationToken);

    public Task<PromotionCodeIssuedCommandResponseDto> RotateCodeAsync(Guid eventId, Guid definitionId, PromotionCodeRequest request, CancellationToken cancellationToken = default) =>
        apiClient.RotateEventPromotionCodeAsync(eventId, definitionId, NewIdempotencyKey(), request, cancellationToken: cancellationToken);

    private static string NewIdempotencyKey() => Guid.CreateVersion7().ToString("D");
}
