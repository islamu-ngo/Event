using Explore.Domain.Enums;

namespace Explore.Application.DTOs.Instance;

public sealed record DeploymentModeDto
{
    public DeploymentMode Mode { get; init; } = DeploymentMode.SingleTenant;
}
