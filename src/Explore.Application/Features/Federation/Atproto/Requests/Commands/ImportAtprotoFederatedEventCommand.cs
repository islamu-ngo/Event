using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Federation.Atproto.Requests.Commands;

public sealed record ImportAtprotoFederatedEventCommand(
    AtprotoJetstreamApplyRequest ApplyRequest) : ICommand<bool>;
