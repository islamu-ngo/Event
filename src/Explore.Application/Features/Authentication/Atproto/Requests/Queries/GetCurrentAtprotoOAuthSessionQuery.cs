using Explore.Application.Features.Authentication.Atproto.Models;
using MediatR;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Queries;

public sealed record GetCurrentAtprotoOAuthSessionQuery(AtprotoCurrentSessionIdentity Identity)
    : IRequest<AtprotoCurrentOAuthSession?>;
