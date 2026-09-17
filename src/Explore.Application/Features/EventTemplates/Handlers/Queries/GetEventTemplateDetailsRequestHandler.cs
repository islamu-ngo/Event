using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventTemplates.Requests.Queries;
using Explore.Application.Contracts.Operations;
using Explore.Domain;

namespace Explore.Application.Features.EventTemplates.Handlers.Queries;

public class GetEventTemplateDetailsRequestHandler : IQueryHandler<GetEventTemplateDetailsRequest, EventTemplateDto>
{
    private readonly IEventTemplateRepository _eventTemplateRepository;

    public GetEventTemplateDetailsRequestHandler(
        IEventTemplateRepository eventTemplateRepository)
    {
        _eventTemplateRepository = eventTemplateRepository;
    }

    public async Task<EventTemplateDto> QueryAsync(GetEventTemplateDetailsRequest request, CancellationToken cancellationToken)
    {
        var template = await _eventTemplateRepository.GetTemplateWithDetails(request.Id);
        if (template == null)
        {
            throw new NotFoundException(nameof(EventTemplate), request.Id);
        }

        return CustomPropertyMapper.ToDetail(template);
    }
}
