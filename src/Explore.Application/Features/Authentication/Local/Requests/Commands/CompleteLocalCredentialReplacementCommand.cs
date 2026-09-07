// ABOUTME: Carries authenticated replacement authority and a transient new password into the Local credential boundary.
// ABOUTME: Keeps user-selected identity fields out of the request and prevents secret-bearing diagnostic formatting.

using Explore.Application.Contracts.Identity;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Requests.Commands;

public sealed record CompleteLocalCredentialReplacementCommand : IRequest<BaseCommandResponse<Guid>>
{
    public CompleteLocalCredentialReplacementCommand(LocalCredentialReplacementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
    }

    public LocalCredentialReplacementRequest Request { get; }

    public override string ToString() => nameof(CompleteLocalCredentialReplacementCommand);
}
