using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Domain;

namespace Explore.Application.Features.Management.Handlers.Commands;

public sealed class RotateManagedControlPlaneCredentialCommandHandler(
    IManagedControlPlaneRegistrationRepository registrationRepository)
    : ICommandHandler<RotateManagedControlPlaneCredentialCommand, bool>
{
    public async Task<bool> ExecuteAsync(
        RotateManagedControlPlaneCredentialCommand request,
        CancellationToken cancellationToken = default)
    {
        var registration = await registrationRepository.GetCurrentAsync(cancellationToken);
        if (registration?.Status != ManagedControlPlaneRegistrationStatus.Registered
            || !IsSha256Hash(request.Request.SecretHash)
            || request.Request.ExpiresAt.Kind != DateTimeKind.Utc
            || request.Request.ExpiresAt <= DateTime.UtcNow
            || request.Request.ExpiresAt > DateTime.UtcNow.AddDays(365))
        {
            return false;
        }

        registration.RotateControlPlaneCredential(
            request.Request.KeyId,
            request.Request.SecretHash,
            request.Request.ExpiresAt,
            DateTime.UtcNow);
        await registrationRepository.Update(registration);
        return true;
    }

    private static bool IsSha256Hash(string value)
    {
        try
        {
            return Convert.FromBase64String(value).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class RevokeManagedControlPlaneRegistrationCommandHandler(
    IManagedControlPlaneRegistrationRepository registrationRepository)
    : ICommandHandler<RevokeManagedControlPlaneRegistrationCommand, bool>
{
    public async Task<bool> ExecuteAsync(
        RevokeManagedControlPlaneRegistrationCommand request,
        CancellationToken cancellationToken = default)
    {
        var registration = await registrationRepository.GetCurrentAsync(cancellationToken);
        if (registration?.Status != ManagedControlPlaneRegistrationStatus.Registered)
        {
            return false;
        }

        registration.Revoke(DateTime.UtcNow);
        await registrationRepository.Update(registration);
        return true;
    }
}
