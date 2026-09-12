using Explore.Application.DTOs.TagTypeTags;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypeTags.Requests.Commands;

public sealed record CreateTagTypeTagsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required CreateTagTypeTagsDto TagTypeTagsDto { get; init; }
}
