using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionTicketAccountDeliveryService(
    IAdmissionTicketAccountRepository accountRepository,
    IAdmissionRecoveryTicketDocumentService documentService,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IUserContext userContext)
{
    public async Task<AdmissionRecoveryTicketDocument?> ReissueAsync(
        Guid ticketId,
        CancellationToken cancellationToken)
    {
        AdmissionRecoveryTicketDocument? result = null;
        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                AdmissionTicket? owned = await accountRepository.GetOwnedAsync(
                    tenantContext.TenantId,
                    userContext.GetRequiredUserId(),
                    ticketId,
                    token);
                if (owned is null ||
                    owned.AdmissionTicketStatusId != (int)AdmissionTicketStatusEnum.Active)
                {
                    return;
                }

                result = await documentService.RotateAndCreateAsync(
                    tenantContext.TenantId,
                    ticketId,
                    token);
            },
            cancellationToken);
        return result;
    }
}
