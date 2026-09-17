using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Atproto.Models;

namespace Explore.Application.Features.Authentication.Atproto.Requests.Commands;

public sealed record RevokeAtprotoSessionCommand(AtprotoCurrentSessionIdentity Identity)
    : ICommand<AtprotoSessionRevocationResult>;
