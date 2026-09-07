using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.PaidEventPolicies.Requests.Queries;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.PaidEventPolicies.Handlers.Queries;

public sealed class GetInstancePaidEventPolicyQueryHandler(IPaidEventPolicyRepository policies)
    : IRequestHandler<GetInstancePaidEventPolicyQuery, PaidEventPolicyDto?>
{
    public async Task<PaidEventPolicyDto?> Handle(GetInstancePaidEventPolicyQuery request, CancellationToken cancellationToken) =>
        (await policies.GetActiveInstanceAsync(cancellationToken)) is { } policy ? PaidEventPolicyMapper.ToDto(policy) : null;
}

public sealed class GetTenantPaidEventPolicyQueryHandler(IPaidEventPolicyRepository policies)
    : IRequestHandler<GetTenantPaidEventPolicyQuery, PaidEventPolicyDto?>
{
    public async Task<PaidEventPolicyDto?> Handle(
        GetTenantPaidEventPolicyQuery request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
        {
            return null;
        }

        PaidEventPolicyVersion? policy =
            await policies.GetActiveTenantAsync(
                request.TenantId,
                cancellationToken);
        return policy?.TenantId == request.TenantId
            ? PaidEventPolicyMapper.ToDto(policy)
            : null;
    }
}

public sealed class GetTenantPaidEventPolicyConfigurationQueryHandler(IPaidEventPolicyRepository policies)
    : IRequestHandler<GetTenantPaidEventPolicyConfigurationQuery, TenantPaidEventPolicyConfigurationDto?>
{
    public async Task<TenantPaidEventPolicyConfigurationDto?> Handle(GetTenantPaidEventPolicyConfigurationQuery request, CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
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

        var tenantPolicy = await policies.GetActiveTenantAsync(request.TenantId, cancellationToken);
        if (tenantPolicy is not null && tenantPolicy.TenantId != request.TenantId)
        {
            return null;
        }
        PaidEventPolicyDto instanceDto = PaidEventPolicyMapper.ToDto(instancePolicy);
        PaidEventPolicyDto? tenantDto = tenantPolicy is null ? null : PaidEventPolicyMapper.ToDto(tenantPolicy);

        return new TenantPaidEventPolicyConfigurationDto
        {
            TenantId = request.TenantId,
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
