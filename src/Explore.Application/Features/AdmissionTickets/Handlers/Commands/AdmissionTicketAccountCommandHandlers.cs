using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.AdmissionTickets;
using Explore.Application.Features.AdmissionTickets.Requests.Commands;
using Explore.Application.Services.Registration;
using MediatR;

namespace Explore.Application.Features.AdmissionTickets.Handlers.Commands;

public sealed class ReissueCurrentAdmissionTicketQrCommandHandler(
    AdmissionTicketAccountDeliveryService deliveryService,
    IAdmissionTicketPresentationResolver presentationResolver,
    ITenantContext tenantContext) :
    IRequestHandler<ReissueCurrentAdmissionTicketQrCommand, AdmissionTicketQrDeliveryDto>
{
    public async Task<AdmissionTicketQrDeliveryDto> Handle(
        ReissueCurrentAdmissionTicketQrCommand request,
        CancellationToken cancellationToken)
    {
        AdmissionRecoveryTicketDocument? document = await deliveryService.ReissueAsync(
            request.TicketId,
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
    IRequestHandler<ReissueCurrentAdmissionTicketPrintCommand, AdmissionTicketPrintDeliveryDto>
{
    public async Task<AdmissionTicketPrintDeliveryDto> Handle(
        ReissueCurrentAdmissionTicketPrintCommand request,
        CancellationToken cancellationToken)
    {
        AdmissionRecoveryTicketDocument? document = await deliveryService.ReissueAsync(
            request.TicketId,
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
