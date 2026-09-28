using System;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Events.Requests.Commands;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Delete)]
public sealed record DeleteEventCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid Id { get; init; }
    public required string UserId { get; init; }

    string? ISecureRequest.ResourceId => Id.ToString();
}

public static class DeleteEventFailureCodes
{
    public const string AuthorityDenied = "event_delete_authority_denied";
    public const string PaidEvidenceConflict = "event_delete_paid_evidence_conflict";
}
