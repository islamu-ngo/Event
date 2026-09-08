using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Features.EventTemplates.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventTemplates.Handlers.Commands;

public class DeleteEventTemplateCommandHandler : IRequestHandler<DeleteEventTemplateCommand, bool>
{
    private readonly IEventTemplateRepository _eventTemplateRepository;
    private readonly HybridCache _cache;

    public DeleteEventTemplateCommandHandler(
        IEventTemplateRepository eventTemplateRepository,
        HybridCache cache)
    {
        _eventTemplateRepository = eventTemplateRepository;
        _cache = cache;
    }

    public async Task<bool> Handle(DeleteEventTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await _eventTemplateRepository.GetTemplateWithDetails(request.Id);
        if (template == null)
        {
            return false;
        }

        var deleted = await _eventTemplateRepository.DeleteTemplate(request.Id, cancellationToken);
        if (!deleted)
        {
            return false;
        }

        await _cache.RemoveAsync(
            $"event-templates:list:{template.TenantId}:{(int?)null}:1:{PaginatedResult<object>.DefaultPageSize}",
            cancellationToken);
        await _cache.RemoveAsync($"event-templates:detail:{template.Id}", cancellationToken);

        return true;
    }
}
