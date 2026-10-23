using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Features.Footer.Requests.Queries;

namespace Explore.Application.Operations;

/// <summary>Closed owner selection. Public configuration and every non-Footer purpose are excluded.</summary>
public static class FooterOperationScope
{
    public static bool Handles(Type request) =>
        request == typeof(UpdateFooterGovernanceSettingsCommand)
        || request == typeof(GetFooterGovernanceSettingsQuery)
        || request == typeof(GetTenantFooterSettingsQuery)
        || request == typeof(GetFooterLinkGroupListQuery)
        || request == typeof(GetFooterLinkGroupDetailsQuery)
        || request == typeof(CreateFooterLinkCommand)
        || request == typeof(CreateFooterLinkGroupCommand)
        || request == typeof(UpdateFooterLinkCommand)
        || request == typeof(UpdateFooterLinkGroupCommand)
        || request == typeof(DeleteFooterLinkCommand)
        || request == typeof(DeleteFooterLinkGroupCommand)
        || request == typeof(ReorderFooterLinkGroupsCommand)
        || request == typeof(PatchTenantFooterSettingsCommand);

    public static PolicyMutationScope Select(object request, ITenantContextAccessor? tenant)
    {
        if (!Handles(request.GetType()))
            throw new InvalidOperationException("Only the closed Footer operations select a Footer owner.");
        if (request is UpdateFooterGovernanceSettingsCommand or GetFooterGovernanceSettingsQuery)
            return PolicyMutationScope.Instance;
        if (tenant?.TenantId is not { } tenantId || tenantId == Guid.Empty)
            throw new UnauthorizedAccessException("Footer requires the exact authenticated tenant.");
        return new(tenantId);
    }
}
