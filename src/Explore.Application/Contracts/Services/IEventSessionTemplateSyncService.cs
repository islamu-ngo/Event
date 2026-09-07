using Explore.Application.DTOs.EventSessionTemplateSync;

namespace Explore.Application.Contracts.Services;

public interface IEventSessionTemplateSyncService
{
    Task<TemplateSyncOutcomeDto> ApplySyncAsync(
        Guid eventSessionId,
        TemplateSyncPlanDto plan,
        int baseProvenanceVersion,
        CancellationToken cancellationToken);
}
