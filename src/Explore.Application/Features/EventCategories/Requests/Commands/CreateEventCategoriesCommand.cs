using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventCategories;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCategories.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record CreateEventCategoriesCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public required CreateEventCategoriesDto EventCategoriesDto { get; init; }

    string? ISecureRequest.ResourceId => EventCategoriesDto.EventId.ToString();
}
