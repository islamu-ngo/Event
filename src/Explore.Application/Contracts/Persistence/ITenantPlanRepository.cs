using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ITenantPlanRepository : IGenericRepository<TenantPlan, Guid>
{
    Task<IReadOnlyList<TenantPlan>> ListWithVersionsAsync(CancellationToken cancellationToken = default);

    Task<TenantPlan?> GetByKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<TenantPlanVersion?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken = default);

    Task<TenantPlanVersion?> GetVersionForUpdateAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task CreateVersionAsync(TenantPlanVersion version, CancellationToken cancellationToken = default);

    Task ReplaceVersionContentAsync(TenantPlanVersion version, CancellationToken cancellationToken = default);

    Task UpdateVersionAsync(TenantPlanVersion version, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantPlanAssignment>> ListActiveAssignmentsForPlanAsync(
        Guid tenantPlanId,
        CancellationToken cancellationToken = default);

    Task<TenantPlanAssignment?> GetActiveAssignmentForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<TenantPlanAssignment?> GetPreviousEligibleAssignmentForTenantAsync(
        Guid tenantId,
        Guid currentAssignmentId,
        CancellationToken cancellationToken = default);

    Task<TenantPlanAssignment?> GetAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default);

    Task<TenantPlanAssignment> CreateAssignmentAsync(
        TenantPlanAssignment assignment,
        CancellationToken cancellationToken = default);

    Task UpdateAssignmentAsync(TenantPlanAssignment assignment, CancellationToken cancellationToken = default);
}
