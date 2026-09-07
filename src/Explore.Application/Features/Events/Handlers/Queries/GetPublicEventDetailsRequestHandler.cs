using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Events.Handlers.Queries;

public sealed class GetPublicEventDetailsRequestHandler(
    IEventDetailsProjectionService detailsProjectionService)
    : IRequestHandler<GetPublicEventDetailsRequest, EventDto?>
{
    public async Task<EventDto?> Handle(GetPublicEventDetailsRequest request, CancellationToken cancellationToken)
    {
        var publicCode = ExtractPublicCode(request.SlugCode);
        if (publicCode is null)
            return null;

        var eventDto = await detailsProjectionService.BuildByPublicCodeAsync(publicCode, cancellationToken);

        if (eventDto is null)
            return null;

        if (eventDto.EventStatusId is not (int)EventStatusEnum.Published ||
            eventDto.VisibilityTypeId is not (int)VisibilityTypeEnum.Public)
            return null;

        eventDto.IsPubliclyEligible = true;
        eventDto.IsManagementView = false;
        await detailsProjectionService.ResolveImageUrlsAsync(eventDto, cancellationToken);
        return eventDto;
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
