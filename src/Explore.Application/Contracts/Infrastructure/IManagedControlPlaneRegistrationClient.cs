using Explore.Application.DTOs.Management;

namespace Explore.Application.Contracts.Infrastructure;

public interface IManagedControlPlaneRegistrationClient
{
    Task<CompleteManagedInstanceRegistrationResponseDto> CompleteRegistrationAsync(
        Uri controlPlaneUrl,
        CompleteManagedInstanceRegistrationRequestDto request,
        CancellationToken cancellationToken);
}
