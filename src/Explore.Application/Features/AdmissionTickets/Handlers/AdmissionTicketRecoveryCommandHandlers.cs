using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.AdmissionTickets;
using Explore.Application.Features.AdmissionTickets.Requests.Commands;
using Explore.Application.Services.Registration;

namespace Explore.Application.Features.AdmissionTickets.Handlers.Commands;

public sealed class RequestAdmissionTicketRecoveryCommandHandler(
    AdmissionRecoveryService recoveryService,
    ITenantContext tenantContext) :
    ICommandHandler<RequestAdmissionTicketRecoveryCommand, AdmissionTicketRecoveryRequestResultDto>
{
    public async Task<AdmissionTicketRecoveryRequestResultDto> ExecuteAsync(
        RequestAdmissionTicketRecoveryCommand command,
        CancellationToken cancellationToken = default)
    {
        _ = await recoveryService.RequestAsync(
            new AdmissionRecoveryRequest(
                tenantContext.TenantId,
                command.Email,
                AdmissionRecoveryPurpose.TicketRecovery),
            cancellationToken);
        return new AdmissionTicketRecoveryRequestResultDto(true, true);
    }
}

public sealed class RedeemAdmissionTicketRecoveryCommandHandler(
    AdmissionRecoveryRedemptionService redemptionService,
    IAdmissionTicketPresentationResolver presentationResolver,
    ITenantContext tenantContext) :
    ICommandHandler<RedeemAdmissionTicketRecoveryCommand, AdmissionTicketRecoveryConsumeResultDto>
{
    public async Task<AdmissionTicketRecoveryConsumeResultDto> ExecuteAsync(
        RedeemAdmissionTicketRecoveryCommand command,
        CancellationToken cancellationToken = default)
    {
        AdmissionRecoveryConsumeResult result = await redemptionService.RedeemAsync(
            tenantContext.TenantId,
            command.Capability,
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
