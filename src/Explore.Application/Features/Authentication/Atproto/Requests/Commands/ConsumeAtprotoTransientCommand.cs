using Explore.Application.Contracts.Operations;
using Explore.Domain;
using Explore.Application.Features.Authentication.Atproto.Models;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Commands;

public sealed record ConsumeAtprotoTransientCommand(Guid CandidateId, AtprotoTransientPurpose Purpose,
    string TokenDigest, Guid ExpectedTenantId) : ICommand<AtprotoTransientCommandResult>
{
    public override string ToString() => nameof(ConsumeAtprotoTransientCommand);
}
