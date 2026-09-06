// ABOUTME: Requests a non-consuming protected lookup before OAuth tenant recovery or with an expected tenant.
// ABOUTME: Only initial OAuth-state read permits an absent tenant; handoff never does.

using Explore.Domain;
using Explore.Application.Features.Authentication.Atproto.Models;
using MediatR;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Queries;

public sealed record ReadAtprotoTransientQuery(AtprotoTransientPurpose Purpose, string TokenDigest,
    Guid? ExpectedTenantId) : IRequest<AtprotoTransientValue?>
{
    public override string ToString() => nameof(ReadAtprotoTransientQuery);
}
