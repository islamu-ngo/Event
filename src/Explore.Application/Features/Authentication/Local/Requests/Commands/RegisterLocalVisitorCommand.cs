using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Local.Requests.Commands;

public sealed record RegisterLocalVisitorCommand(LocalVisitorRegistrationRequestDto Request, string BrowserProof)
    : ICommand<BaseCommandResponse<Guid>>
{
    public override string ToString() => nameof(RegisterLocalVisitorCommand);
}
