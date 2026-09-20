using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventAddOns;

namespace Explore.Application.Features.EventAddOns.Requests.Commands;

public sealed record EventAddOnSelection(Guid CatalogItemId, int Quantity);

public sealed record CreateEventAddOnCatalogDraftCommand(
    Guid EventId,
    string CurrencyCode) : ICommand<EventAddOnCatalogDto?>;

public sealed record AddEventAddOnCatalogItemCommand(
    Guid EventId,
    string Name,
    string? Description,
    long UnitPriceMinor,
    int InventoryCapacity,
    string FulfillmentDisclosure,
    string RefundDisclosure) : ICommand<EventAddOnCatalogDto?>;

public sealed record PublishEventAddOnCatalogCommand(
    Guid EventId,
    DateTime PublishedAtUtc) : ICommand<EventAddOnCatalogDto?>;

public sealed record RetireEventAddOnCatalogCommand(
    Guid EventId,
    DateTime RetiredAtUtc) : ICommand<EventAddOnCatalogDto?>;

public sealed record ReserveRegistrationOrderAddOnsCommand :
    ICommand<RegistrationOrderAddOnSummaryDto?>
{
    public ReserveRegistrationOrderAddOnsCommand(
        Guid eventId,
        Guid registrationOrderId,
        Guid catalogId,
        IEnumerable<EventAddOnSelection> selections,
        Guid operationId,
        DateTime reservedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(selections);
        EventId = eventId;
        RegistrationOrderId = registrationOrderId;
        CatalogId = catalogId;
        Selections = selections.ToArray();
        OperationId = operationId;
        ReservedAtUtc = reservedAtUtc;
    }

    public Guid EventId { get; }
    public Guid RegistrationOrderId { get; }
    public Guid CatalogId { get; }
    public IReadOnlyList<EventAddOnSelection> Selections { get; }
    public Guid OperationId { get; }
    public DateTime ReservedAtUtc { get; }
}

public sealed record FulfillRegistrationOrderAddOnCommand(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationOrderAddOnLineId,
    Guid OperationId,
    DateTime FulfilledAtUtc) : ICommand<RegistrationOrderAddOnSummaryDto?>;

public sealed record RefundRegistrationOrderAddOnCommand(
    Guid EventId,
    Guid RegistrationOrderId,
    Guid RegistrationOrderAddOnLineId,
    Guid OperationId,
    int Quantity,
    DateTime AllocatedAtUtc) : ICommand<RegistrationOrderAddOnSummaryDto?>;
