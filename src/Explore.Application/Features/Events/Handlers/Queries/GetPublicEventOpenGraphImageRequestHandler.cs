using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.Events.OpenGraph;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Events.Handlers.Queries;

public sealed class GetPublicEventOpenGraphImageRequestHandler(
    IEventRepository eventRepository,
    ITenantPolicySettingService tenantPolicySettingService,
    IStorageObjectContentReader contentReader,
    IEventOpenGraphImageRenderer renderer)
    : IRequestHandler<GetPublicEventOpenGraphImageRequest, EventOpenGraphImageRenderResult?>
{
    public async Task<EventOpenGraphImageRenderResult?> Handle(
        GetPublicEventOpenGraphImageRequest request,
        CancellationToken cancellationToken)
    {
        string? publicCode = ExtractPublicCode(request.SlugCode);
        if (publicCode is null)
            return null;

        Event? eventEntity = await eventRepository.GetPublicEventForOpenGraphAsync(publicCode, cancellationToken);
        if (eventEntity is null ||
            eventEntity.EventStatusId != (int)EventStatusEnum.Published ||
            eventEntity.VisibilityTypeId != (int)VisibilityTypeEnum.Public)
        {
            return null;
        }

        TenantPolicySettingsDto settings = await tenantPolicySettingService
            .ReadEffectiveTenantSettingsAsync(eventEntity.TenantId);
        StorageObjectContentResult? featuredImage = eventEntity.FeaturedImageId is Guid featuredImageId && featuredImageId != Guid.Empty
            ? await contentReader.OpenAsync(featuredImageId, publicImagesOnly: true, cancellationToken)
            : null;
        using Stream? featuredImageStream = featuredImage?.Content;

        return await renderer.RenderAsync(
            new EventOpenGraphImageRenderRequest(
                eventEntity.Title,
                eventEntity.FirstSessionDate,
                eventEntity.LastSessionDate,
                settings.BrandDisplayName,
                featuredImageStream,
                featuredImage?.ContentType),
            cancellationToken);
    }

    private static string? ExtractPublicCode(string slugCode)
    {
        if (string.IsNullOrWhiteSpace(slugCode))
            return null;

        var separatorIndex = slugCode.LastIndexOf('-');
        if (separatorIndex < 0 || separatorIndex == slugCode.Length - 1)
            return null;

        return slugCode[(separatorIndex + 1)..];
    }
}
