using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Admissions;

public interface IAdmissionTicketService
{
    Task<IReadOnlyList<HalResourceOfAdmissionTicketDto>> GetCurrentAsync(
        CancellationToken cancellationToken = default);

    Task<HalResourceOfAdmissionTicketDto?> GetAsync(
        Guid ticketId,
        CancellationToken cancellationToken = default);

    Task<AdmissionTicketQrDeliveryDto?> ReissueQrAsync(
        HalResourceOfAdmissionTicketDto ticket,
        CancellationToken cancellationToken = default);

    Task<AdmissionTicketPrintDeliveryDto?> ReissuePrintAsync(
        HalResourceOfAdmissionTicketDto ticket,
        CancellationToken cancellationToken = default);

    Task<bool> RequestRecoveryAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<AdmissionRecoveryUiResult> ConsumeRecoveryAsync(
        string capability,
        CancellationToken cancellationToken = default);
}
