using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventSessionTemplates.Requests.Queries;
using Explore.Application.Contracts.Operations;
using Explore.Domain;

namespace Explore.Application.Features.EventSessionTemplates.Handlers.Queries;

public class GetEventSessionTemplateDetailsRequestHandler : IQueryHandler<GetEventSessionTemplateDetailsRequest, EventSessionTemplateDto>
{
    private readonly IEventSessionTemplateRepository _sessionTemplateRepository;

    public GetEventSessionTemplateDetailsRequestHandler(
        IEventSessionTemplateRepository sessionTemplateRepository)
    {
        _sessionTemplateRepository = sessionTemplateRepository;
    }

    public async Task<EventSessionTemplateDto> QueryAsync(GetEventSessionTemplateDetailsRequest request, CancellationToken cancellationToken)
    {
        var sessionTemplate = await _sessionTemplateRepository.GetSessionTemplateWithDetails(request.Id);
        if (sessionTemplate == null)
        {
            throw new NotFoundException(nameof(EventSessionTemplate), request.Id);
        }

        return CustomPropertyMapper.ToDetail(sessionTemplate);
    }
}
