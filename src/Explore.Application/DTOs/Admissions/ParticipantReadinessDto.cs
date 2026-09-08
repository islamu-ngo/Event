using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.Admissions;

public sealed record ParticipantReadinessDto
{
    public required Guid RegistrationTicketAssignmentId
    {
        get;
        init;
    }

    public required string StatusCode { get; init; }
    public required string SupportCode { get; init; }
    public required bool ActiveAdmissionAvailable { get; init; }

    [JsonIgnore]
    public bool CanComplete { get; init; }

    [JsonIgnore]
    public bool CanApprove { get; init; }

    [JsonIgnore]
    public bool CanRevoke { get; init; }

    [JsonIgnore]
    public Guid EventId { get; init; }

    [JsonIgnore]
    public Guid RegistrationOrderId { get; init; }

    [JsonIgnore]
    public Guid ParticipantId { get; init; }
}
