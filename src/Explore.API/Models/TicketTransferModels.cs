using Explore.API.Hateoas;
using Explore.Application.DTOs.Admissions;
using Explore.Application.Hateoas;

namespace Explore.API.Models;

public sealed record AcceptTicketTransferRequest
{
    public required Guid RecipientParticipantId { get; init; }
}

public sealed record TicketTransferOfferResponse
{
    public required HalResource<TicketTransferDto> Transfer
    {
        get;
        init;
    }

    public required string ClaimCapability { get; init; }

    public override string ToString() =>
        "TicketTransferOfferResponse(<redacted>)";
}

public sealed record TicketTransferCredentialResponse
{
    public required HalResource<TicketTransferDto> Transfer
    {
        get;
        init;
    }

    public required string Credential { get; init; }

    public override string ToString() =>
        "TicketTransferCredentialResponse(<redacted>)";
}
