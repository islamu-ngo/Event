using Explore.Application.DTOs.EventSessionTemplateSync;

namespace Explore.Application.Contracts.Services;

public interface IEventSessionTemplateDiffService
{
    Task<TemplateDiffDto> ComputeDiffAsync(
        Guid eventSessionId,
        int targetTemplateVersion,
        CancellationToken cancellationToken);
}
