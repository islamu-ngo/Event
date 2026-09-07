using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.AdmissionTickets;
using Explore.Application.Features.AdmissionTickets.Requests.Commands;
using Explore.Application.Services.Registration;
using MediatR;

namespace Explore.Application.Features.AdmissionTickets.Handlers.Commands;

public sealed class RequestAdmissionTicketRecoveryCommandHandler(
    AdmissionRecoveryService recoveryService,
    ITenantContext tenantContext) :
    IRequestHandler<RequestAdmissionTicketRecoveryCommand, AdmissionTicketRecoveryRequestResultDto>
{
    public async Task<AdmissionTicketRecoveryRequestResultDto> Handle(
        RequestAdmissionTicketRecoveryCommand request,
        CancellationToken cancellationToken)
    {
        _ = await recoveryService.RequestAsync(
            new AdmissionRecoveryRequest(
                tenantContext.TenantId,
                request.Email,
                AdmissionRecoveryPurpose.TicketRecovery),
            cancellationToken);
        return new AdmissionTicketRecoveryRequestResultDto(true, true);
    }
}

public sealed class RedeemAdmissionTicketRecoveryCommandHandler(
    AdmissionRecoveryRedemptionService redemptionService,
    IAdmissionTicketPresentationResolver presentationResolver,
    ITenantContext tenantContext) :
    IRequestHandler<RedeemAdmissionTicketRecoveryCommand, AdmissionTicketRecoveryConsumeResultDto>
{
    public async Task<AdmissionTicketRecoveryConsumeResultDto> Handle(
        RedeemAdmissionTicketRecoveryCommand request,
        CancellationToken cancellationToken)
    {
        AdmissionRecoveryConsumeResult result = await redemptionService.RedeemAsync(
            tenantContext.TenantId,
            request.Capability,
            cancellationToken);
        AdmissionRecoveryTicketDocument? document = result.Document;
        if (result.Outcome != AdmissionRecoveryConsumeOutcome.Consumed || document is null)
        {
            return null!;
        }

        AdmissionTicketPresentation presentation =
            await AdmissionTicketDeliveryDtoMapper.ResolveAsync(
                presentationResolver,
                tenantContext.TenantId,
                document.TicketId,
                cancellationToken);
        return new AdmissionTicketRecoveryConsumeResultDto(
            result.RecoveryRecordId,
            AdmissionTicketDeliveryDtoMapper.Recovery(document, presentation));
    }
}
