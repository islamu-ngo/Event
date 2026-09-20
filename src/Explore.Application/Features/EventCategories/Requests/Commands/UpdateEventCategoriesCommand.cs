using System;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventCategories;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCategories.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record UpdateEventCategoriesCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventCategoryId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public required UpdateEventCategoriesDto EventCategoriesDto { get; init; }
    public Guid EventId { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
