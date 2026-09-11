using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionSpeakers.Handlers.Queries;

public class GetEventSessionSpeakerDetailsRequestHandler : IRequestHandler<GetEventSessionSpeakerDetailsRequest, EventSessionSpeakerDto>
{
    private readonly IEventSessionSpeakerRepository _speakerRepository;

    public GetEventSessionSpeakerDetailsRequestHandler(
        IEventSessionSpeakerRepository speakerRepository)
    {
        _speakerRepository = speakerRepository;
    }

    public async Task<EventSessionSpeakerDto> Handle(GetEventSessionSpeakerDetailsRequest request, CancellationToken cancellationToken)
    {
        var speaker = await _speakerRepository.GetById(request.Id);
        return speaker is null ? null! : EventSessionMapper.ToDetail(speaker);
    }
}
