using Explore.Application.DTOs.TagTypeTags;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypeTags.Requests.Commands;

public sealed record UpdateTagTypeTagsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public Guid TagTypeTagsId { get; init; }
    public required UpdateTagTypeTagsDto TagTypeTagsDto { get; init; }
}
