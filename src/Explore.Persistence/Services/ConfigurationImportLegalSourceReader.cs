namespace Explore.Persistence.Services;

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class ConfigurationImportLegalSourceReader(ExploreDbContext context)
    : IConfigurationImportLegalSourceReader
{
    public async Task<IReadOnlyList<LegalDocumentPortableSource>> ReadAsync(
        LegalDocumentScope scope, Guid? tenantId, CancellationToken ct)
    {
        string authorityKey = scope switch
        {
            LegalDocumentScope.Instance when tenantId is null => "instance",
            LegalDocumentScope.Tenant when tenantId is { } id && id != Guid.Empty => $"tenant:{id:N}",
            _ => throw new ArgumentException("Legal source scope and tenant are inconsistent.", nameof(tenantId))
        };
        context.ValidateTenantLease();
        return await ConfigurationImportLegalSourceQuery.ReadAsync(context, scope, tenantId, authorityKey, ct);
    }
}
