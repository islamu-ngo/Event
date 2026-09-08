using Explore.Application.DTOs.EventTemplateSync;

namespace Explore.Application.Contracts.Services;

public interface IEventTemplateSyncService
{
    Task<TemplateSyncOutcomeDto> ApplySyncAsync(
        Guid eventId,
        TemplateSyncPlanDto plan,
        int baseProvenanceVersion,
        CancellationToken cancellationToken);
}
