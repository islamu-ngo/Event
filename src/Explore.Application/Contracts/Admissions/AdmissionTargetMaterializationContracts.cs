using Explore.Domain;

namespace Explore.Application.Contracts.Admissions;

public interface IAdmissionTargetMaterializer
{
    Task MaterializeAsync(
        Event eventTarget,
        EventTicketCatalogVersion catalog,
        CancellationToken cancellationToken);
}

public interface IAdmissionTargetMaterializationRepository
{
    Task<IReadOnlyList<EventSession>> ListScheduleSessionsForUpdateAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdmissionTarget>> ListTargetsForUpdateAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdmissionCheckInPolicy>> ListPoliciesAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken);

    Task AddTargetsAsync(
        IReadOnlyCollection<AdmissionTarget> targets,
        CancellationToken cancellationToken);

    Task AddPoliciesAsync(
        IReadOnlyCollection<AdmissionCheckInPolicy> policies,
        CancellationToken cancellationToken);
}
