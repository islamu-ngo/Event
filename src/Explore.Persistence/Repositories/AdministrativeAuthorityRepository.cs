using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

namespace Explore.Persistence.Repositories;

public sealed class AdministrativeAuthorityRepository(
    IGlobalExploreDbContextFactory globalFactory) : IAdministrativeAuthorityRepository
{
    public async Task<bool> IsInstanceAdminAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await AdministrativeAuthorityQuery.IsInstanceAdminAsync(context, userId, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetAdminTenantIdsAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await AdministrativeAuthorityQuery.GetAdminTenantIdsAsync(context, userId, cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> GetAdminOrganizationIdsAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        GetAdminOrganizationIdsAsync(userId, null, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetAdminOrganizationIdsAsync(
        Guid userId, Guid? tenantId, CancellationToken cancellationToken = default)
    {
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await AdministrativeAuthorityQuery.GetAdminOrganizationIdsAsync(context, userId, tenantId, cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> GetAdminGroupIdsAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        GetAdminGroupIdsAsync(userId, null, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetAdminGroupIdsAsync(
        Guid userId, Guid? tenantId, CancellationToken cancellationToken = default)
    {
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await AdministrativeAuthorityQuery.GetAdminGroupIdsAsync(context, userId, tenantId, cancellationToken);
    }
}
