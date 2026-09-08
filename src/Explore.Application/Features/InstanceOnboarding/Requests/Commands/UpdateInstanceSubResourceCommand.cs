using Explore.Application.DTOs.Instance;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

public sealed record UpdateModuleSettingsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchModuleSettingsDto Patch { get; init; }
}

public sealed record UpdateEventPolicyCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchEventPolicyDto Patch { get; init; }
}

public sealed record UpdateOrganizationPolicyCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchOrganizationPolicyDto Patch { get; init; }
}

public sealed record UpdateBrandingSettingsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchBrandingSettingsDto Patch { get; init; }
}

public sealed record UpdateDomainSettingsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchDomainSettingsDto Patch { get; init; }
}

public sealed record UpdateTenantDelegationSettingsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchTenantDelegationSettingsDto Patch { get; init; }
}

public sealed record UpdateAdminPortalSettingsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchAdminPortalSettingsDto Patch { get; init; }
}

public sealed record UpdateMcpGovernanceSettingsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchMcpGovernanceSettingsDto Patch { get; init; }
}

public sealed record UpdateAiAssistantGovernanceSettingsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchAiAssistantGovernanceSettingsDto Patch { get; init; }
}

public sealed record UpdateRenderPolicySettingsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchRenderPolicySettingsDto Patch { get; init; }
}
