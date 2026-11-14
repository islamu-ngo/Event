using Explore.Application.Contracts.Operations;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Application.Features.Authentication.Atproto.Models;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Queries;

public sealed record ReadAtprotoTransientQuery(AtprotoTransientPurpose Purpose, string TokenDigest,
    Guid? ExpectedTenantId, AtprotoAuthenticationFlowKind FlowKind) : IQuery<AtprotoTransientValue?>
{
    public override string ToString() => nameof(ReadAtprotoTransientQuery);
}
