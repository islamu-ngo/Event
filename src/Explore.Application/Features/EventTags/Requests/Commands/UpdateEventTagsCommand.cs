using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventTags;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventTags.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record UpdateEventTagsCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventTagId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public required UpdateEventTagsDto EventTagsDto { get; init; }
    public Guid EventId { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
