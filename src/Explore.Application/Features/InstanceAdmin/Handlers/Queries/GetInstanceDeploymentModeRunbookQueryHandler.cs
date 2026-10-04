using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Queries;

public sealed class GetInstanceDeploymentModeRunbookQueryHandler(
    IDeploymentModeProvider deploymentModeProvider,
    ITenantRepository tenantRepository) : IQueryHandler<GetInstanceDeploymentModeRunbookQuery, InstanceDeploymentModeRunbookDto>
{
    public async Task<InstanceDeploymentModeRunbookDto> QueryAsync(
        GetInstanceDeploymentModeRunbookQuery request,
        CancellationToken cancellationToken)
    {
        var currentMode = await deploymentModeProvider.GetCurrentModeAsync(cancellationToken);
        var activeTenantCount = await tenantRepository.GetActiveTenantCountAsync();

        return new InstanceDeploymentModeRunbookDto
        {
            CurrentMode = currentMode.ToString(),
            ActiveTenantCount = activeTenantCount,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            TargetOptions = BuildTargetOptions(currentMode, activeTenantCount),
            Steps = BuildSteps(currentMode, activeTenantCount)
        };
    }

    private static List<InstanceDeploymentModeTargetOptionDto> BuildTargetOptions(
        DeploymentMode currentMode,
        int activeTenantCount) => currentMode switch
        {
            DeploymentMode.SingleTenant =>
            [
                new InstanceDeploymentModeTargetOptionDto
            {
                TargetMode = DeploymentMode.MultiTenant.ToString(),
                Label = "Switch to multi-tenant mode",
                Description = "Enable tenant-fleet routing and tenant lifecycle administration for this instance.",
                Allowed = true,
                ConfirmationText = DeploymentMode.MultiTenant.ToString()
            }
            ],
            DeploymentMode.MultiTenant =>
            [
                new InstanceDeploymentModeTargetOptionDto
            {
                TargetMode = DeploymentMode.SingleTenant.ToString(),
                Label = "Switch to single-tenant mode",
                Description = "Return this instance to the default single tenant after reducing the active tenant set.",
                Allowed = activeTenantCount <= 1,
                ConfirmationText = DeploymentMode.SingleTenant.ToString(),
                BlockingReason = activeTenantCount <= 1
                    ? null
                    : "Single-tenant mode requires zero or one active tenant.",
                Remediation = activeTenantCount <= 1
                    ? null
                    : "Suspend or archive extra active tenants before reverting to single-tenant mode."
            }
            ],
            _ => []
        };

    private static List<InstanceDeploymentModeRunbookStepDto> BuildSteps(
        DeploymentMode currentMode,
        int activeTenantCount) =>
    [
        new InstanceDeploymentModeRunbookStepDto
        {
            Key = "review-current-mode",
            Title = "Review current mode",
            Description = $"This instance is currently running in {currentMode} mode with {activeTenantCount} active tenant(s).",
            Severity = "info"
        },
        new InstanceDeploymentModeRunbookStepDto
        {
            Key = "validate-preconditions",
            Title = "Validate preconditions",
            Description = currentMode == DeploymentMode.MultiTenant && activeTenantCount > 1
                ? "Multi-tenant to single-tenant migration is blocked until only zero or one tenant remains active."
                : "The server-side runbook will validate tenant-count preconditions again before committing the mode change.",
            Severity = currentMode == DeploymentMode.MultiTenant && activeTenantCount > 1 ? "error" : "success"
        },
        new InstanceDeploymentModeRunbookStepDto
        {
            Key = "typed-confirmation",
            Title = "Require typed confirmation",
            Description = "The operator must type the exact target deployment mode before the server commits the change.",
            Severity = "warning"
        }
    ];
}
