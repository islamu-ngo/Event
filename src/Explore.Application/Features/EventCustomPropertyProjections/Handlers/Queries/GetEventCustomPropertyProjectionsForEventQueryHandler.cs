using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.EventCustomPropertyProjections.Requests.Queries;
using Explore.Application.Mappings;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventCustomPropertyProjections.Handlers.Queries;

public class GetEventCustomPropertyProjectionsForEventQueryHandler
    : IRequestHandler<GetEventCustomPropertyProjectionsForEventQuery, BaseCommandResponse<IReadOnlyList<EventCustomPropertyProjectionDto>>>
{
    private readonly IEventCustomPropertyProjectionRepository _projectionRepository;

    public GetEventCustomPropertyProjectionsForEventQueryHandler(
        IEventCustomPropertyProjectionRepository projectionRepository)
    {
        _projectionRepository = projectionRepository;
    }

    public async Task<BaseCommandResponse<IReadOnlyList<EventCustomPropertyProjectionDto>>> Handle(
        GetEventCustomPropertyProjectionsForEventQuery request,
        CancellationToken cancellationToken)
    {
        if (request.EventId == Guid.Empty)
        {
            return BaseCommandResponse.Validation<IReadOnlyList<EventCustomPropertyProjectionDto>>(
                ["EventId is required."],
                "EventId is required.");
        }

        var projections = await _projectionRepository.GetForEventAsync(
            request.EventId,
            request.ExposureCeiling,
            cancellationToken);

        var dtos = projections.Select(CustomPropertyProjectionMapper.ToEventRow).ToList();

        return BaseCommandResponse.Success<IReadOnlyList<EventCustomPropertyProjectionDto>>(
            dtos,
            "Projection rows retrieved.");
    }
}
