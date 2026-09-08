using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionTemplates.Requests.Queries;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventSessionTemplates.Handlers.Queries;

public class GetEventSessionTemplateDetailsRequestHandler : IRequestHandler<GetEventSessionTemplateDetailsRequest, EventSessionTemplateDto>
{
    private readonly IEventSessionTemplateRepository _sessionTemplateRepository;
    private readonly IMapper _mapper;

    public GetEventSessionTemplateDetailsRequestHandler(
        IEventSessionTemplateRepository sessionTemplateRepository,
        IMapper mapper)
    {
        _sessionTemplateRepository = sessionTemplateRepository;
        _mapper = mapper;
    }

    public async Task<EventSessionTemplateDto> Handle(GetEventSessionTemplateDetailsRequest request, CancellationToken cancellationToken)
    {
        var sessionTemplate = await _sessionTemplateRepository.GetSessionTemplateWithDetails(request.Id);
        if (sessionTemplate == null)
        {
            throw new NotFoundException(nameof(EventSessionTemplate), request.Id);
        }

        return _mapper.Map<EventSessionTemplateDto>(sessionTemplate);
    }
}
