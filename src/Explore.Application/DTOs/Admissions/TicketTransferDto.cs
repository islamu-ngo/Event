using System.Text.Json.Serialization;

namespace Explore.Application.DTOs.Admissions;

public sealed record TicketTransferDto
{
    public required Guid Id { get; init; }
    public required Guid AdmissionTicketId { get; init; }
    public required string StatusCode { get; init; }
    public required string SupportCode { get; init; }
    public required int TransferHop { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required int CredentialGeneration { get; init; }

    [JsonIgnore]
    public bool CanOffer { get; init; }

    [JsonIgnore]
    public bool CanAccept { get; init; }

    [JsonIgnore]
    public bool CanCancel { get; init; }

    [JsonIgnore]
    public bool CanCorrect { get; init; }

    [JsonIgnore]
    public bool CanReissue { get; init; }

    [JsonIgnore]
    public Guid EventId { get; init; }
}

public sealed record TicketTransferOfferDto
{
    public required TicketTransferDto Transfer { get; init; }

    public required string ClaimCapability { get; init; }

    public override string ToString() =>
        "TicketTransferOfferDto(<redacted>)";
}

public sealed record TicketTransferAcceptanceDto
{
    public required TicketTransferDto Transfer { get; init; }

    public required string Credential { get; init; }

    public override string ToString() =>
        "TicketTransferAcceptanceDto(<redacted>)";
}
