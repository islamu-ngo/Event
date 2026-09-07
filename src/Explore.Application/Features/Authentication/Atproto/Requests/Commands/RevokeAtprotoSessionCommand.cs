using Explore.Application.Features.Authentication.Atproto.Models;
using MediatR;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Commands;

public sealed record RevokeAtprotoSessionCommand(AtprotoCurrentSessionIdentity Identity)
    : IRequest<AtprotoSessionRevocationResult>;
