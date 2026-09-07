using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services.Http;

namespace Explore.Blazor.Client.Contracts.Services.Admissions;

public interface IAdmissionRecoveryBffClient
{
    Task<ApiResult<AdmissionTicketRecoveryDeliveryDto>> ConsumeAsync(
        string capability,
        CancellationToken cancellationToken = default);
}
