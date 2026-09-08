using Explore.Application.DTOs.Footer;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Footer.Requests.Commands;

public sealed record UpdateFooterGovernanceSettingsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid UserId { get; init; }
    public required PatchFooterGovernanceSettingsDto Patch { get; init; }
}
