using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IRegistrationAnswerAnalyticsRepository
{
    Task<RegistrationAnswerAnalyticsProjection?> GetEventFormVersionAnalyticsAsync(
        Guid tenantId,
        Guid eventId,
        Guid formId,
        Guid formVersionId,
        int minimumCellSize,
        CancellationToken cancellationToken);
}
