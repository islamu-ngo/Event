using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTemplates.Requests.Queries;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventTemplates.Handlers.Queries;

public class GetEventTemplateDetailsRequestHandler : IRequestHandler<GetEventTemplateDetailsRequest, EventTemplateDto>
{
    private readonly IEventTemplateRepository _eventTemplateRepository;
    private readonly IMapper _mapper;

    public GetEventTemplateDetailsRequestHandler(
        IEventTemplateRepository eventTemplateRepository,
        IMapper mapper)
    {
        _eventTemplateRepository = eventTemplateRepository;
        _mapper = mapper;
    }

    public async Task<EventTemplateDto> Handle(GetEventTemplateDetailsRequest request, CancellationToken cancellationToken)
    {
        var template = await _eventTemplateRepository.GetTemplateWithDetails(request.Id);
        if (template == null)
        {
            throw new NotFoundException(nameof(EventTemplate), request.Id);
        }

        return _mapper.Map<EventTemplateDto>(template);
    }
}
