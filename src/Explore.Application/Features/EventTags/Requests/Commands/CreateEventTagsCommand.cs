using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventTags;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventTags.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record CreateEventTagsCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventTagsDto EventTagsDto { get; init; }

    string? ISecureRequest.ResourceId => EventTagsDto.EventId.ToString();
}
