using Explore.Application.DTOs.EventTemplateSync;

namespace Explore.Application.Contracts.Services;

public interface IEventTemplateDiffService
{
    Task<TemplateDiffDto> ComputeDiffAsync(
        Guid eventId,
        int targetTemplateVersion,
        CancellationToken cancellationToken);
}
