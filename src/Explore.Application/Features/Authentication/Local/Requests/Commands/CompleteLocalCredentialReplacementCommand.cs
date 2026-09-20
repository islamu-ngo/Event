
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Local.Requests.Commands;

public sealed record CompleteLocalCredentialReplacementCommand : ICommand<BaseCommandResponse<Guid>>
{
    public CompleteLocalCredentialReplacementCommand(LocalCredentialReplacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
    }

    public LocalCredentialReplacementRequest Request { get; }

    public override string ToString() => nameof(CompleteLocalCredentialReplacementCommand);
}
