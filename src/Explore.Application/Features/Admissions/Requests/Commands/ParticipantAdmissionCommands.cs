using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Admissions.Requests.Commands;

public interface IParticipantAdmissionCommand
{
    Guid EventId { get; }
    Guid RegistrationOrderId { get; }
    Guid RegistrationTicketAssignmentId { get; }
    Guid ParticipantId { get; }
}

public sealed record CompleteParticipantAdmissionCommand(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationTicketAssignmentId,
    Guid ParticipantId) :
    IRequest<BaseCommandResponse<Guid>>,
    IParticipantAdmissionCommand;

[AuthorizeResource(
    ResourceKinds.Event,
    AuthorizationActions.Events.ManageAttendees)]
public sealed record ApproveParticipantAdmissionCommand(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationTicketAssignmentId,
    Guid ParticipantId) :
    IRequest<BaseCommandResponse<Guid>>,
    IParticipantAdmissionCommand,
    ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        EventId == Guid.Empty ? null : EventId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}

[AuthorizeResource(
    ResourceKinds.Event,
    AuthorizationActions.Events.ManageAttendees)]
public sealed record RevokeParticipantAdmissionCommand(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationTicketAssignmentId,
    Guid ParticipantId) :
    IRequest<BaseCommandResponse<Guid>>,
    IParticipantAdmissionCommand,
    ISecureRequest
{
    string? ISecureRequest.ResourceId =>
        EventId == Guid.Empty ? null : EventId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
