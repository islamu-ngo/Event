namespace Explore.Application.Features.ConfigurationManifest.Application;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;

public interface IConfigurationImportTenantIdentityMutationBoundary
{
    Task ApplyInCurrentTransactionAsync(
        Guid tenantId,
        string displayName,
        Guid actorUserId,
        DateTime occurredAt,
        CancellationToken cancellationToken);
}

public sealed class ConfigurationImportTenantIdentityMutationBoundary(
    ITenantRepository tenants) : IConfigurationImportTenantIdentityMutationBoundary
{
    public async Task ApplyInCurrentTransactionAsync(
        Guid tenantId,
        string displayName,
        Guid actorUserId,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(actorUserId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (occurredAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC timestamp required.", nameof(occurredAt));
        Tenant tenant = await tenants.GetById(tenantId)
            ?? throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.TargetMismatch);
        tenant.FullName = displayName.Trim();
        tenant.UpdatedAt = occurredAt;
        tenant.UpdatedBy = actorUserId;
        await tenants.Update(tenant);
    }
}
