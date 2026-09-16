using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.PaidEventPolicies.Requests.Queries;
using Explore.Domain;

namespace Explore.Application.Features.PaidEventPolicies.Handlers.Queries;

public sealed class GetInstancePaidEventPolicyQueryHandler(IPaidEventPolicyRepository policies)
    : IQueryHandler<GetInstancePaidEventPolicyQuery, PaidEventPolicyDto?>
{
    public async Task<PaidEventPolicyDto?> QueryAsync(GetInstancePaidEventPolicyQuery query, CancellationToken cancellationToken = default) =>
        (await policies.GetActiveInstanceAsync(cancellationToken)) is { } policy ? PaidEventPolicyMapper.ToDto(policy) : null;
}

public sealed class GetTenantPaidEventPolicyQueryHandler(IPaidEventPolicyRepository policies)
    : IQueryHandler<GetTenantPaidEventPolicyQuery, PaidEventPolicyDto?>
{
    public async Task<PaidEventPolicyDto?> QueryAsync(
        GetTenantPaidEventPolicyQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.TenantId == Guid.Empty)
        {
            return null;
        }

        PaidEventPolicyVersion? policy =
            await policies.GetActiveTenantAsync(
                query.TenantId,
                cancellationToken);
        return policy?.TenantId == query.TenantId
            ? PaidEventPolicyMapper.ToDto(policy)
            : null;
    }
}

public sealed class GetTenantPaidEventPolicyConfigurationQueryHandler(IPaidEventPolicyRepository policies)
    : IQueryHandler<GetTenantPaidEventPolicyConfigurationQuery, TenantPaidEventPolicyConfigurationDto?>
{
    public async Task<TenantPaidEventPolicyConfigurationDto?> QueryAsync(GetTenantPaidEventPolicyConfigurationQuery query, CancellationToken cancellationToken = default)
    {
        if (query.TenantId == Guid.Empty)
        {
            return null;
        }

        var instancePolicy = await policies.GetActiveInstanceAsync(cancellationToken);
        if (instancePolicy is null
            || !instancePolicy.IsActive
            || instancePolicy.TenantId is not null)
        {
            return null;
        }

        var tenantPolicy = await policies.GetActiveTenantAsync(query.TenantId, cancellationToken);
        if (tenantPolicy is not null && tenantPolicy.TenantId != query.TenantId)
        {
            return null;
        }
        PaidEventPolicyDto instanceDto = PaidEventPolicyMapper.ToDto(instancePolicy);
        PaidEventPolicyDto? tenantDto = tenantPolicy is null ? null : PaidEventPolicyMapper.ToDto(tenantPolicy);

        return new TenantPaidEventPolicyConfigurationDto
        {
            TenantId = query.TenantId,
            ActiveInstanceCeiling = instanceDto,
            ActiveTenantOverride = tenantDto,
            EffectivePolicy = tenantDto ?? instanceDto,
            Authority = new PaidEventPolicyAuthorityDto
            {
                InstancePolicyVersion = instancePolicy.VersionNumber,
                EffectiveValuesInherited = tenantPolicy is null,
                HasTenantNarrowing = tenantPolicy is not null,
                ManifestOwnedFields =
                    PaidEventPolicyAuthorityMetadata.ManifestOwnedFields,
                SovereignLockedFields =
                    PaidEventPolicyAuthorityMetadata.SovereignLockedFields
            }
        };
    }
}
