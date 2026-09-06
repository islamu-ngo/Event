// ABOUTME: Requests creation of a bounded protected pre-authentication value for a target tenant.
// ABOUTME: Tenant metadata is authenticated machine intent, never ambient user or tenant authority.

using Explore.Domain;
using Explore.Application.Features.Authentication.Atproto.Models;
using MediatR;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Commands;

public sealed record CreateAtprotoTransientCommand(AtprotoTransientPurpose Purpose, string TokenDigest,
    Guid TenantId, string ProtectedPayload, long ExpiresAtUnixMilliseconds) : IRequest<AtprotoTransientCommandResult>
{
    public override string ToString() => nameof(CreateAtprotoTransientCommand);
}
