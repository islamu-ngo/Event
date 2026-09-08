using System.Collections.Immutable;
using Explore.Application.Contracts.Admissions;
using Explore.Application.DTOs.AdmissionTickets;

namespace Explore.Application.Features.AdmissionTickets.Handlers;

internal static class AdmissionTicketDeliveryDtoMapper
{
    internal static AdmissionTicketRecoveryDeliveryDto Recovery(
        AdmissionRecoveryTicketDocument document,
        AdmissionTicketPresentation presentation) =>
        new(
            document.TicketId,
            document.TicketId,
            document.EventId,
            document.StatusCode,
            document.DisplayReference,
            presentation.HolderDisplayName,
            presentation.TicketTypeName,
            Entitlements(presentation),
            document.ManualCode,
            document.ManualCodeClassificationCode,
            document.QrRepresentation,
            document.PrintModel);

    internal static AdmissionTicketQrDeliveryDto Qr(
        AdmissionRecoveryTicketDocument document,
        AdmissionTicketPresentation presentation) =>
        new(
            document.TicketId,
            document.TicketId,
            document.EventId,
            document.StatusCode,
            document.DisplayReference,
            presentation.HolderDisplayName,
            presentation.TicketTypeName,
            Entitlements(presentation),
            document.ManualCode,
            document.ManualCodeClassificationCode,
            document.QrRepresentation,
            document.PrintModel,
            "qr");

    internal static AdmissionTicketPrintDeliveryDto Print(
        AdmissionRecoveryTicketDocument document,
        AdmissionTicketPresentation presentation) =>
        new(
            document.TicketId,
            document.TicketId,
            document.EventId,
            document.StatusCode,
            document.DisplayReference,
            presentation.HolderDisplayName,
            presentation.TicketTypeName,
            Entitlements(presentation),
            document.ManualCode,
            document.ManualCodeClassificationCode,
            document.QrRepresentation,
            document.PrintModel,
            "print");

    internal static async Task<AdmissionTicketPresentation> ResolveAsync(
        IAdmissionTicketPresentationResolver resolver,
        Guid tenantId,
        Guid ticketId,
        CancellationToken cancellationToken)
    {
        var presentation = await resolver.ResolveAsync(
            tenantId,
            [ticketId],
            cancellationToken);
        return presentation.GetValueOrDefault(
            ticketId,
            AdmissionTicketPresentation.Empty);
    }

    private static ImmutableArray<AdmissionTicketEntitlementDto> Entitlements(
        AdmissionTicketPresentation presentation) =>
        presentation.Entitlements
            .Select(entitlement => new AdmissionTicketEntitlementDto(
                entitlement.ScopeCode,
                entitlement.EventTitle,
                entitlement.DayLabel,
                entitlement.LocalDate,
                entitlement.SessionTitle,
                entitlement.IncludedQuantity))
            .ToImmutableArray();
}
