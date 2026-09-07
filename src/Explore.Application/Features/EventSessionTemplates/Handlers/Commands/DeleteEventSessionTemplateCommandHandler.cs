using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.Features.EventSessionTemplates.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionTemplates.Handlers.Commands;

public class DeleteEventSessionTemplateCommandHandler : IRequestHandler<DeleteEventSessionTemplateCommand, bool>
{
    private readonly IEventSessionTemplateRepository _sessionTemplateRepository;
    private readonly HybridCache _cache;

    public DeleteEventSessionTemplateCommandHandler(
        IEventSessionTemplateRepository sessionTemplateRepository,
        HybridCache cache)
    {
        _sessionTemplateRepository = sessionTemplateRepository;
        _cache = cache;
    }

    public async Task<bool> Handle(DeleteEventSessionTemplateCommand request, CancellationToken cancellationToken)
    {
        var sessionTemplate = await _sessionTemplateRepository.GetSessionTemplateWithDetails(request.Id);
        if (sessionTemplate == null)
        {
            return false;
        }

        var deleted = await _sessionTemplateRepository.DeleteSessionTemplate(request.Id, cancellationToken);
        if (!deleted)
        {
            return false;
        }

        await _cache.RemoveAsync(
            $"session-templates:list:{sessionTemplate.EventTemplateId}:1:{PaginatedResult<object>.DefaultPageSize}",
            cancellationToken);
        await _cache.RemoveAsync($"session-templates:detail:{sessionTemplate.Id}", cancellationToken);

        return true;
    }
}
