namespace Explore.Application.DTOs.RegistrationOrders;

using System.Text.Json.Serialization;

public sealed record RegistrationParticipantDto(
    Guid Id,
    Guid RegistrationOrderId,
    int ParticipantTypeId,
    Guid? GuardianParticipantId,
    string? DisplayName,
    string? Email,
    string? Phone);

public sealed record RegistrationTicketAssignmentDto(
    Guid Id,
    Guid RegistrationOrderLineId,
    int Ordinal,
    Guid? ParticipantId,
    int AssignmentStatusId,
    DateTime? AssignmentDeadline);

public sealed record RegistrationParticipantOrderLineDto(
    Guid Id,
    string TicketTypeName,
    int Quantity,
    int ParticipantDataCollectionModeId,
    string ParticipantDataCollectionModeCode,
    bool RequiresGuardian);

public sealed record RegistrationOrderParticipantsDto(
    Guid RegistrationOrderId,
    IReadOnlyList<RegistrationParticipantOrderLineDto> Lines,
    IReadOnlyList<RegistrationParticipantDto> Participants,
    IReadOnlyList<RegistrationTicketAssignmentDto> Assignments)
{
    public RegistrationOrderParticipantsDto(
        Guid registrationOrderId,
        IReadOnlyList<RegistrationParticipantDto> participants,
        IReadOnlyList<RegistrationTicketAssignmentDto> assignments)
        : this(registrationOrderId, [], participants, assignments)
    {
    }

    [JsonIgnore]
    public bool CanManage { get; init; }

    [JsonIgnore]
    public bool CanImportCompanyCsv { get; init; }
}

public sealed record ParticipantDetailsDto(string? DisplayName, string? Email, string? Phone);

public sealed record TicketParticipantAssignmentInputDto(Guid RegistrationOrderLineId, int Ordinal, Guid ParticipantId);

public sealed record TicketDeferralInputDto(Guid RegistrationOrderLineId, int Ordinal);

public sealed record CompanyRegistrationAssignmentInputDto(
    Guid RegistrationOrderLineId,
    int Ordinal,
    int ParticipantTypeId,
    string DisplayName,
    string? Email,
    string? Phone);
