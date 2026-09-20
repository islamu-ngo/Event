using System;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventSessions.Requests.Commands;

[AuthorizeResource(ResourceKinds.EventSession, AuthorizationActions.Update)]
public sealed record UpdateEventSessionCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventSessionId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public required UpdateEventSessionDto EventSessionDto { get; init; }

    string? ISecureRequest.ResourceId => EventSessionId.ToString();
}
