using Explore.Application.DTOs.Footer;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Footer.Requests.Commands;

public sealed record UpdateFooterGovernanceSettingsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchFooterGovernanceSettingsDto Patch { get; init; }
}
