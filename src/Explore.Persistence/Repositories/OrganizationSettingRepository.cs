namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

public class OrganizationSettingRepository(ExploreDbContext dbContext)
    : IOrganizationSettingRepository
{
    public Task<OrganizationSetting?> GetByOrganizationAndKey(
        Guid tenantId,
        Guid organizationId,
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(tenantId, organizationId);
        return dbContext.OrganizationSettingOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(setting =>
                setting.TenantId == tenantId &&
                setting.OrganizationTenant.OrganizationId == organizationId &&
                setting.SettingKey == key,
                cancellationToken);
    }

    public Task<List<OrganizationSetting>> GetAllForOrganization(
        Guid tenantId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(tenantId, organizationId);
        return dbContext.OrganizationSettingOverrides
            .AsNoTracking()
            .Where(setting =>
                setting.TenantId == tenantId &&
                setting.OrganizationTenant.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
    }

    public async Task SetValueAsync(
        Guid tenantId,
        Guid organizationId,
        string key,
        string value,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(tenantId, organizationId);
        OrganizationSetting? setting = await dbContext.OrganizationSettingOverrides
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == tenantId &&
                candidate.OrganizationTenant.OrganizationId == organizationId &&
                candidate.SettingKey == key,
                cancellationToken);
        if (setting is not null)
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
            setting.UpdatedBy = actorId;
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        OrganizationTenant participation = await dbContext.OrganizationTenants
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == tenantId &&
                candidate.OrganizationId == organizationId,
                cancellationToken)
            ?? throw new InvalidOperationException("Organization is not available in the specified tenant.");
        dbContext.OrganizationSettingOverrides.Add(new OrganizationSetting
        {
            OrganizationTenantId = participation.Id,
            OrganizationTenant = participation,
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = key,
            Value = value,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorId
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveOverride(
        Guid tenantId,
        Guid organizationId,
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(tenantId, organizationId);
        OrganizationSetting? setting = await dbContext.OrganizationSettingOverrides
            .FirstOrDefaultAsync(candidate =>
                candidate.TenantId == tenantId &&
                candidate.OrganizationTenant.OrganizationId == organizationId &&
                candidate.SettingKey == key,
                cancellationToken);
        if (setting is null)
        {
            return false;
        }

        dbContext.OrganizationSettingOverrides.Remove(setting);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void ValidateScope(Guid tenantId, Guid organizationId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant scope is required.", nameof(tenantId));
        }

        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization scope is required.", nameof(organizationId));
        }
    }
}
