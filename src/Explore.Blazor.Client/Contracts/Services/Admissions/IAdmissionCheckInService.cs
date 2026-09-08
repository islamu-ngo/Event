using ISLAMU.Wire.Contracts.Admissions;

namespace Explore.Blazor.Client.Contracts.Services.Admissions;

public interface IAdmissionCheckInService
{
    Task<AdmissionCheckInUiResult> CheckInAsync(
        Guid eventId,
        Guid targetId,
        AdmissionCredentialBearer credential,
        CancellationToken cancellationToken);
}
