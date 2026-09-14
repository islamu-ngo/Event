using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.AdmissionTickets;
using Explore.Application.Features.AdmissionTickets.Requests.Commands;
using Explore.Application.Services.Registration;

namespace Explore.Application.Features.AdmissionTickets.Handlers.Commands;

public sealed class ReissueCurrentAdmissionTicketQrCommandHandler(
    AdmissionTicketAccountDeliveryService deliveryService,
    IAdmissionTicketPresentationResolver presentationResolver,
    ITenantContext tenantContext) :
    ICommandHandler<ReissueCurrentAdmissionTicketQrCommand, AdmissionTicketQrDeliveryDto>
{
    public async Task<AdmissionTicketQrDeliveryDto> ExecuteAsync(
        ReissueCurrentAdmissionTicketQrCommand command,
        CancellationToken cancellationToken = default)
    {
        AdmissionRecoveryTicketDocument? document = await deliveryService.ReissueAsync(
            command.TicketId,
            cancellationToken);
        if (document is null)
        {
            return null!;
        }

        AdmissionTicketPresentation presentation =
            await AdmissionTicketDeliveryDtoMapper.ResolveAsync(
                presentationResolver,
                tenantContext.TenantId,
                document.TicketId,
                cancellationToken);
        return AdmissionTicketDeliveryDtoMapper.Qr(document, presentation);
    }
}

public sealed class ReissueCurrentAdmissionTicketPrintCommandHandler(
    AdmissionTicketAccountDeliveryService deliveryService,
    IAdmissionTicketPresentationResolver presentationResolver,
    ITenantContext tenantContext) :
    ICommandHandler<ReissueCurrentAdmissionTicketPrintCommand, AdmissionTicketPrintDeliveryDto>
{
    public async Task<AdmissionTicketPrintDeliveryDto> ExecuteAsync(
        ReissueCurrentAdmissionTicketPrintCommand command,
        CancellationToken cancellationToken = default)
    {
        AdmissionRecoveryTicketDocument? document = await deliveryService.ReissueAsync(
            command.TicketId,
            cancellationToken);
        if (document is null)
        {
            return null!;
        }

        AdmissionTicketPresentation presentation =
            await AdmissionTicketDeliveryDtoMapper.ResolveAsync(
                presentationResolver,
                tenantContext.TenantId,
                document.TicketId,
                cancellationToken);
        return AdmissionTicketDeliveryDtoMapper.Print(document, presentation);
    }
}
