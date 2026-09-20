using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Atproto.Models;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Queries;

public sealed record GetCurrentAtprotoOAuthSessionQuery(AtprotoCurrentSessionIdentity Identity)
    : IQuery<AtprotoCurrentOAuthSession?>;
