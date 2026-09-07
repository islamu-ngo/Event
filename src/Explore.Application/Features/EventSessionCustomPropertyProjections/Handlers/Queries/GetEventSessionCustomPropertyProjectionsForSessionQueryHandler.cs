using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Features.EventSessionCustomPropertyProjections.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionCustomPropertyProjections.Handlers.Queries;

public class GetEventSessionCustomPropertyProjectionsForSessionQueryHandler
    : IRequestHandler<GetEventSessionCustomPropertyProjectionsForSessionQuery, BaseCommandResponse<IReadOnlyList<EventSessionCustomPropertyProjectionDto>>>
{
    private readonly IEventSessionCustomPropertyProjectionRepository _projectionRepository;
    private readonly IMapper _mapper;

    public GetEventSessionCustomPropertyProjectionsForSessionQueryHandler(
        IEventSessionCustomPropertyProjectionRepository projectionRepository,
        IMapper mapper)
    {
        _projectionRepository = projectionRepository;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<IReadOnlyList<EventSessionCustomPropertyProjectionDto>>> Handle(
        GetEventSessionCustomPropertyProjectionsForSessionQuery request,
        CancellationToken cancellationToken)
    {
        if (request.EventSessionId == Guid.Empty)
        {
            return BaseCommandResponse.Validation<IReadOnlyList<EventSessionCustomPropertyProjectionDto>>(
                ["EventSessionId is required."],
                "EventSessionId is required.");
        }

        var projections = await _projectionRepository.GetForSessionAsync(
            request.EventSessionId,
            request.ExposureCeiling,
            cancellationToken);

        var dtos = _mapper.Map<List<EventSessionCustomPropertyProjectionDto>>(projections);

        return BaseCommandResponse.Success<IReadOnlyList<EventSessionCustomPropertyProjectionDto>>(
            dtos,
            "Session projection rows retrieved.");
    }
}
