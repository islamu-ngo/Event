namespace Explore.Application.Features.Moderation.Requests.Commands;

internal sealed record SyntheticUnreviewedModerationCommand :
    Explore.Application.Contracts.Operations.ICommand<Explore.Application.Responses.BaseCommandResponse<Guid>>;
