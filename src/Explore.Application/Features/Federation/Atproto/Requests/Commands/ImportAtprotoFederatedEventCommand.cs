using Explore.Application.Contracts.Persistence;
using MediatR;

namespace Explore.Application.Features.Federation.Atproto.Requests.Commands;

public sealed record ImportAtprotoFederatedEventCommand(
    AtprotoJetstreamApplyRequest ApplyRequest) : IRequest<bool>;
