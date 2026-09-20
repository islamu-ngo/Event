using Explore.Application.Contracts.Operations;
using Explore.Domain;
using Explore.Application.Features.Authentication.Atproto.Models;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Queries;

public sealed record ReadAtprotoTransientQuery(AtprotoTransientPurpose Purpose, string TokenDigest,
    Guid? ExpectedTenantId) : IQuery<AtprotoTransientValue?>
{
    public override string ToString() => nameof(ReadAtprotoTransientQuery);
}
