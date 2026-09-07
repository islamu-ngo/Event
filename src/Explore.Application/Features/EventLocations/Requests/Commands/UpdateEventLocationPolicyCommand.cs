using Explore.Application.Authorization;
using Explore.Application.DTOs.Location;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventLocations.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record UpdateEventLocationPolicyCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid EventLocationId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public int ExpectedPolicyVersion { get; init; }
    public UpdateEventLocationDisclosureFieldsDto? Fields { get; init; }
    public UpdateEventLocationDisclosureAudienceDto? Audience { get; init; }
    public bool NeedsPrivacyReview { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString("D");
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Update)]
public sealed record ConfirmEventLocationRemediationCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid EventLocationId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public int ExpectedPolicyVersion { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString("D");
}
