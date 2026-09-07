using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Locations.Requests.Commands;

/// <summary>
/// Classifies a Location as a Private Home and records the authenticated actor as its consenting owner.
/// </summary>
[AuthorizeResource(ResourceKinds.Location, AuthorizationActions.Update)]
public sealed record ClassifyLocationAsPrivateHomeCommand
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid LocationId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }

    /// <summary>Version of the household-consent statement the actor agreed to.</summary>
    public string ConsentVersion { get; init; } = string.Empty;

    /// <summary>Must be explicitly true; an unchecked box is never treated as consent.</summary>
    public bool ConsentAcknowledged { get; init; }

    string? ISecureRequest.ResourceId => LocationId.ToString();
}

/// <summary>
/// Moves Private Home ownership to the authenticated actor. The domain requires the consenting user and
/// the new owner to be the same person, so ownership is accepted by the incoming owner rather than
/// pushed by the outgoing one.
/// </summary>
[AuthorizeResource(ResourceKinds.Location, AuthorizationActions.Update)]
public sealed record AcceptPrivateHomeOwnershipCommand
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid LocationId { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public string ConsentVersion { get; init; } = string.Empty;
    public bool ConsentAcknowledged { get; init; }

    string? ISecureRequest.ResourceId => LocationId.ToString();
}
