using System.Collections.Immutable;

namespace Explore.Application.DTOs.AdmissionTickets;

public sealed record AdmissionTicketDto
{
    public AdmissionTicketDto(
        Guid id,
        Guid ticketId,
        Guid eventId,
        string statusCode,
        string displayReference,
        Guid registrationOrderId,
        string? holderDisplayName,
        string ticketTypeName,
        DateTime issuedAtUtc,
        IReadOnlyList<AdmissionTicketEntitlementDto>? entitlements)
    {
        Id = id;
        TicketId = ticketId;
        EventId = eventId;
        StatusCode = statusCode;
        DisplayReference = displayReference;
        RegistrationOrderId = registrationOrderId;
        HolderDisplayName = holderDisplayName;
        TicketTypeName = ticketTypeName;
        IssuedAtUtc = issuedAtUtc;
        Entitlements = Snapshot(entitlements);
    }

    public Guid Id { get; }
    public Guid TicketId { get; }
    public Guid EventId { get; }
    public string StatusCode { get; }
    public string DisplayReference { get; }
    public Guid RegistrationOrderId { get; }
    public string? HolderDisplayName { get; }
    public string TicketTypeName { get; }
    public DateTime IssuedAtUtc { get; }
    public IReadOnlyList<AdmissionTicketEntitlementDto> Entitlements { get; }

    private static ImmutableArray<AdmissionTicketEntitlementDto> Snapshot(
        IEnumerable<AdmissionTicketEntitlementDto>? entitlements) =>
        entitlements?.ToImmutableArray() ?? [];
}

public sealed record AdmissionTicketEntitlementDto(
    string ScopeCode,
    string EventTitle,
    string? DayLabel,
    DateOnly? LocalDate,
    string? SessionTitle,
    int IncludedQuantity);

public sealed record AdmissionTicketRecoveryRequestResultDto(bool Accepted, bool Success);

public sealed record AdmissionTicketRecoveryDeliveryDto
{
    public AdmissionTicketRecoveryDeliveryDto(
        Guid id,
        Guid ticketId,
        Guid eventId,
        string statusCode,
        string displayReference,
        string? holderDisplayName,
        string ticketTypeName,
        IReadOnlyList<AdmissionTicketEntitlementDto>? entitlements,
        string manualCode,
        string manualCodeClassificationCode,
        string qrRepresentation,
        string printModel)
    {
        Id = id;
        TicketId = ticketId;
        EventId = eventId;
        StatusCode = statusCode;
        DisplayReference = displayReference;
        HolderDisplayName = holderDisplayName;
        TicketTypeName = ticketTypeName;
        Entitlements = Snapshot(entitlements);
        ManualCode = manualCode;
        ManualCodeClassificationCode = manualCodeClassificationCode;
        QrRepresentation = qrRepresentation;
        PrintModel = printModel;
    }

    public Guid Id { get; }
    public Guid TicketId { get; }
    public Guid EventId { get; }
    public string StatusCode { get; }
    public string DisplayReference { get; }
    public string? HolderDisplayName { get; }
    public string TicketTypeName { get; }
    public IReadOnlyList<AdmissionTicketEntitlementDto> Entitlements { get; }
    public string ManualCode { get; }
    public string ManualCodeClassificationCode { get; }
    public string QrRepresentation { get; }
    public string PrintModel { get; }

    private static ImmutableArray<AdmissionTicketEntitlementDto> Snapshot(
        IEnumerable<AdmissionTicketEntitlementDto>? entitlements) =>
        entitlements?.ToImmutableArray() ?? [];
}

public sealed record AdmissionTicketRecoveryConsumeResultDto(
    Guid RecoveryRecordId,
    AdmissionTicketRecoveryDeliveryDto Delivery);

public sealed record AdmissionTicketQrDeliveryDto
{
    public AdmissionTicketQrDeliveryDto(
        Guid id,
        Guid ticketId,
        Guid eventId,
        string statusCode,
        string displayReference,
        string? holderDisplayName,
        string ticketTypeName,
        IReadOnlyList<AdmissionTicketEntitlementDto>? entitlements,
        string manualCode,
        string manualCodeClassificationCode,
        string qrRepresentation,
        string printModel,
        string deliverySurface)
    {
        Id = id;
        TicketId = ticketId;
        EventId = eventId;
        StatusCode = statusCode;
        DisplayReference = displayReference;
        HolderDisplayName = holderDisplayName;
        TicketTypeName = ticketTypeName;
        Entitlements = entitlements?.ToImmutableArray() ?? [];
        ManualCode = manualCode;
        ManualCodeClassificationCode = manualCodeClassificationCode;
        QrRepresentation = qrRepresentation;
        PrintModel = printModel;
        DeliverySurface = deliverySurface;
    }

    public Guid Id { get; }
    public Guid TicketId { get; }
    public Guid EventId { get; }
    public string StatusCode { get; }
    public string DisplayReference { get; }
    public string? HolderDisplayName { get; }
    public string TicketTypeName { get; }
    public IReadOnlyList<AdmissionTicketEntitlementDto> Entitlements { get; }
    public string ManualCode { get; }
    public string ManualCodeClassificationCode { get; }
    public string QrRepresentation { get; }
    public string PrintModel { get; }
    public string DeliverySurface { get; }
}

public sealed record AdmissionTicketPrintDeliveryDto
{
    public AdmissionTicketPrintDeliveryDto(
        Guid id,
        Guid ticketId,
        Guid eventId,
        string statusCode,
        string displayReference,
        string? holderDisplayName,
        string ticketTypeName,
        IReadOnlyList<AdmissionTicketEntitlementDto>? entitlements,
        string manualCode,
        string manualCodeClassificationCode,
        string qrRepresentation,
        string printModel,
        string deliverySurface)
    {
        Id = id;
        TicketId = ticketId;
        EventId = eventId;
        StatusCode = statusCode;
        DisplayReference = displayReference;
        HolderDisplayName = holderDisplayName;
        TicketTypeName = ticketTypeName;
        Entitlements = entitlements?.ToImmutableArray() ?? [];
        ManualCode = manualCode;
        ManualCodeClassificationCode = manualCodeClassificationCode;
        QrRepresentation = qrRepresentation;
        PrintModel = printModel;
        DeliverySurface = deliverySurface;
    }

    public Guid Id { get; }
    public Guid TicketId { get; }
    public Guid EventId { get; }
    public string StatusCode { get; }
    public string DisplayReference { get; }
    public string? HolderDisplayName { get; }
    public string TicketTypeName { get; }
    public IReadOnlyList<AdmissionTicketEntitlementDto> Entitlements { get; }
    public string ManualCode { get; }
    public string ManualCodeClassificationCode { get; }
    public string QrRepresentation { get; }
    public string PrintModel { get; }
    public string DeliverySurface { get; }
}
