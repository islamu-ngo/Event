using Explore.Application.Management;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Management;

internal sealed class ManagedControlPlaneOptionsValidator : IValidateOptions<ManagedControlPlaneOptions>
{
    public ValidateOptionsResult Validate(string? name, ManagedControlPlaneOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        if (options.ControlPlaneUrl is not { IsAbsoluteUri: true } endpoint
            || (endpoint.Scheme != Uri.UriSchemeHttps
                && !(endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)))
        {
            return ValidateOptionsResult.Fail(
                "ManagedControlPlane:ControlPlaneUrl must be an HTTPS origin or an HTTP loopback URL.");
        }

        if (options.ManagedInstanceId == Guid.Empty)
        {
            return ValidateOptionsResult.Fail("ManagedControlPlane:ManagedInstanceId is required.");
        }

        if (options.RegistrationToken.Length is < 32 or > 512)
        {
            return ValidateOptionsResult.Fail(
                "ManagedControlPlane:RegistrationToken must contain between 32 and 512 characters.");
        }

        if (options.CredentialLifetime < TimeSpan.FromDays(1)
            || options.CredentialLifetime > TimeSpan.FromDays(365))
        {
            return ValidateOptionsResult.Fail(
                "ManagedControlPlane:CredentialLifetime must be between one and 365 days.");
        }

        if (options.MaximumTenantCount is < 0 or > 100_000)
        {
            return ValidateOptionsResult.Fail(
                "ManagedControlPlane:MaximumTenantCount must be between zero and 100000.");
        }

        if (options.TenantAdministratorSignInUrl is { } signInUrl
            && (!signInUrl.IsAbsoluteUri
                || (signInUrl.Scheme != Uri.UriSchemeHttps
                    && !(signInUrl.Scheme == Uri.UriSchemeHttp && signInUrl.IsLoopback))))
        {
            return ValidateOptionsResult.Fail(
                "ManagedControlPlane:TenantAdministratorSignInUrl must be HTTPS or an HTTP loopback URL.");
        }

        return ValidateOptionsResult.Success;
    }
}
