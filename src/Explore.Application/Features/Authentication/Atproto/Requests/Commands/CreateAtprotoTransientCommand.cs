using Explore.Application.Contracts.Operations;
using Explore.Domain;
using Explore.Application.Features.Authentication.Atproto.Models;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Commands;

public sealed record CreateAtprotoTransientCommand(AtprotoTransientPurpose Purpose, string TokenDigest,
    Guid TenantId, string ProtectedPayload, long ExpiresAtUnixMilliseconds) : ICommand<AtprotoTransientCommandResult>
{
    public override string ToString() => nameof(CreateAtprotoTransientCommand);
}
