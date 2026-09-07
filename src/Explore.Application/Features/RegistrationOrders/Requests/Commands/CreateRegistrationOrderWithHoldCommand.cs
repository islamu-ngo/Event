using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Requests.Commands;

public sealed record CreateRegistrationOrderWithHoldCommand : IRequest<BaseCommandResponse<Guid>>
{
    public Guid EventId { get; init; }

    public Guid TicketCatalogVersionId { get; init; }

    public Guid? AccountUserId { get; init; }

    public Guid? PurchaserActorId { get; init; }

    public string? VerifiedContactNormalizedEmail { get; init; }

    public BookingPartyTypeEnum BookingPartyType { get; init; }

    public CapabilityTokenHash? GuestAccessTokenHash { get; init; }

    public int? PlatformContributionBasisPoints { get; init; }

    private IReadOnlyList<RegistrationOrderLineSelection> _lines =
        Array.AsReadOnly(Array.Empty<RegistrationOrderLineSelection>());

    public required IReadOnlyList<RegistrationOrderLineSelection> Lines
    {
        get => _lines;
        init => _lines = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
}

public sealed record RegistrationOrderLineSelection(
    Guid TicketTypeId,
    int Quantity,
    long? ChosenUnitPriceMinor);
