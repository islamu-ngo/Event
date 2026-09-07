using Explore.Domain;

namespace Explore.Application.Contracts.Admissions;

public interface IAdmissionTicketAccountRepository
{
    Task<IReadOnlyList<AdmissionTicket>> ListCurrentAsync(
        Guid tenantId,
        Guid accountUserId,
        CancellationToken cancellationToken);

    Task<AdmissionTicket?> GetOwnedAsync(
        Guid tenantId,
        Guid accountUserId,
        Guid admissionTicketId,
        CancellationToken cancellationToken);
}
