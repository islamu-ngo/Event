using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Requests.Commands;

public interface IRegistrationParticipantMutation : IRequest<BaseCommandResponse<Guid>>
{
    Guid RegistrationOrderId { get; }
}

public sealed record AddRegistrationParticipantCommand(
    Guid RegistrationOrderId,
    int ParticipantTypeId,
    Guid? GuardianParticipantId,
    ParticipantDetailsDto Details) : IRegistrationParticipantMutation;

public sealed record UpdateRegistrationParticipantCommand(
    Guid RegistrationOrderId,
    Guid ParticipantId,
    int ParticipantTypeId,
    Guid? GuardianParticipantId,
    ParticipantDetailsDto Details) : IRegistrationParticipantMutation;

public sealed record AssignRegistrationTicketCommand(
    Guid RegistrationOrderId,
    Guid RegistrationOrderLineId,
    int Ordinal,
    Guid ParticipantId) : IRegistrationParticipantMutation;

public sealed record BulkAssignRegistrationTicketsCommand(
    Guid RegistrationOrderId,
    IReadOnlyCollection<TicketParticipantAssignmentInputDto> Assignments) : IRegistrationParticipantMutation;

public sealed record DeferRegistrationTicketCommand(
    Guid RegistrationOrderId,
    Guid RegistrationOrderLineId,
    int Ordinal,
    DateTime AssignmentDeadline) : IRegistrationParticipantMutation;

public sealed record BulkDeferRegistrationTicketsCommand(
    Guid RegistrationOrderId,
    IReadOnlyCollection<TicketDeferralInputDto> Assignments,
    DateTime AssignmentDeadline) : IRegistrationParticipantMutation;

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrations)]
public sealed record ImportCompanyRegistrationAssignmentsCsvCommand(
    Guid EventId,
    Guid RegistrationOrderId,
    string CsvUtf8,
    string LineageKey) : IRequest<BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString("D");

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
