using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventTags;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventTags.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record CreateEventTagsCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventTagsDto EventTagsDto { get; init; }

    string? ISecureRequest.ResourceId => EventTagsDto.EventId.ToString();
}
