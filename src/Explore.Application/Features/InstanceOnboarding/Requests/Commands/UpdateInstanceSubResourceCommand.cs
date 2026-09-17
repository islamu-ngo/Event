using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Instance;
using Explore.Application.Responses;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record UpdateModuleSettingsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchModuleSettingsDto Patch { get; init; }
}

public sealed record UpdateEventPolicyCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchEventPolicyDto Patch { get; init; }
}

public sealed record UpdateOrganizationPolicyCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchOrganizationPolicyDto Patch { get; init; }
}

public sealed record UpdateBrandingSettingsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchBrandingSettingsDto Patch { get; init; }
}

public sealed record UpdateDomainSettingsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchDomainSettingsDto Patch { get; init; }
}

public sealed record UpdateTenantDelegationSettingsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchTenantDelegationSettingsDto Patch { get; init; }
}

public sealed record UpdateAdminPortalSettingsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchAdminPortalSettingsDto Patch { get; init; }
}

public sealed record UpdateMcpGovernanceSettingsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchMcpGovernanceSettingsDto Patch { get; init; }
}

public sealed record UpdateAiAssistantGovernanceSettingsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchAiAssistantGovernanceSettingsDto Patch { get; init; }
}

public sealed record UpdateRenderPolicySettingsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchRenderPolicySettingsDto Patch { get; init; }
}
