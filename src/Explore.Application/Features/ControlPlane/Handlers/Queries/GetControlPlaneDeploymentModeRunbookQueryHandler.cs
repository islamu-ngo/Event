using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class GetControlPlaneDeploymentModeRunbookQueryHandler(
    IDeploymentModeProvider deploymentModeProvider,
    ITenantRepository tenantRepository) : IRequestHandler<GetControlPlaneDeploymentModeRunbookQuery, ControlPlaneDeploymentModeRunbookDto>
{
    public async Task<ControlPlaneDeploymentModeRunbookDto> Handle(
        GetControlPlaneDeploymentModeRunbookQuery request,
        CancellationToken cancellationToken)
    {
        var currentMode = await deploymentModeProvider.GetCurrentModeAsync(cancellationToken);
        var activeTenantCount = await tenantRepository.GetActiveTenantCountAsync();

        return new ControlPlaneDeploymentModeRunbookDto
        {
            CurrentMode = currentMode.ToString(),
            ActiveTenantCount = activeTenantCount,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            TargetOptions = BuildTargetOptions(currentMode, activeTenantCount),
            Steps = BuildSteps(currentMode, activeTenantCount)
        };
    }

    private static List<ControlPlaneDeploymentModeTargetOptionDto> BuildTargetOptions(
        DeploymentMode currentMode,
        int activeTenantCount) => currentMode switch
        {
            DeploymentMode.SingleTenant =>
            [
                new ControlPlaneDeploymentModeTargetOptionDto
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
                new ControlPlaneDeploymentModeTargetOptionDto
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

    private static List<ControlPlaneDeploymentModeRunbookStepDto> BuildSteps(
        DeploymentMode currentMode,
        int activeTenantCount) =>
    [
        new ControlPlaneDeploymentModeRunbookStepDto
        {
            Key = "review-current-mode",
            Title = "Review current mode",
            Description = $"This instance is currently running in {currentMode} mode with {activeTenantCount} active tenant(s).",
            Severity = "info"
        },
        new ControlPlaneDeploymentModeRunbookStepDto
        {
            Key = "validate-preconditions",
            Title = "Validate preconditions",
            Description = currentMode == DeploymentMode.MultiTenant && activeTenantCount > 1
                ? "Multi-tenant to single-tenant migration is blocked until only zero or one tenant remains active."
                : "The server-side runbook will validate tenant-count preconditions again before committing the mode change.",
            Severity = currentMode == DeploymentMode.MultiTenant && activeTenantCount > 1 ? "error" : "success"
        },
        new ControlPlaneDeploymentModeRunbookStepDto
        {
            Key = "typed-confirmation",
            Title = "Require typed confirmation",
            Description = "The operator must type the exact target deployment mode before the server commits the change.",
            Severity = "warning"
        }
    ];
}
