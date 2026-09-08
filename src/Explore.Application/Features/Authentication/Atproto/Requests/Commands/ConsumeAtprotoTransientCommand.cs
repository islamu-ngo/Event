using Explore.Domain;
using Explore.Application.Features.Authentication.Atproto.Models;
using MediatR;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Commands;

public sealed record ConsumeAtprotoTransientCommand(Guid CandidateId, AtprotoTransientPurpose Purpose,
    string TokenDigest, Guid ExpectedTenantId) : IRequest<AtprotoTransientCommandResult>
{
    public override string ToString() => nameof(ConsumeAtprotoTransientCommand);
}
