using Explore.Application.DTOs.TagTypeTags;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Requests.Commands;

public sealed record UpdateTagTypeTagsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid TagTypeTagsId { get; init; }
    public required UpdateTagTypeTagsDto TagTypeTagsDto { get; init; }
}
