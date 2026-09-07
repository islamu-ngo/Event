using Explore.Application.Contracts.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Services;

public sealed class PrivacyErasureProviderLocatorProtector(IDataProtectionProvider dataProtectionProvider)
    : IPrivacyErasureProviderLocatorProtector
{
    public int CurrentVersion => 1;
    private const string Purpose = "ISLAMU.PrivacyErasure.ProviderLocator.v1";

    private readonly ITimeLimitedDataProtector _protector = dataProtectionProvider
        .CreateProtector(Purpose)
        .ToTimeLimitedDataProtector();

    public string Protect(string locator, TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locator);
        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        return _protector.Protect(locator, lifetime);
    }

    public string Unprotect(string protectedLocator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedLocator);
        return _protector.Unprotect(protectedLocator);
    }
}
