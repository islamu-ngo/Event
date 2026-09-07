namespace Explore.Application.Contracts.Services;

public interface IPrivacyErasureProviderLocatorProtector
{
    int CurrentVersion { get; }
    string Protect(string locator, TimeSpan lifetime);
    string Unprotect(string protectedLocator);
}
