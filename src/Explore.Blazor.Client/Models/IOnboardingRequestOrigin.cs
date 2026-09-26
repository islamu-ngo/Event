namespace Explore.Blazor.Client.Models;

/// <summary>The trusted request origin captured by the server for onboarding defaults.</summary>
public interface IOnboardingRequestOrigin
{
    string? Url { get; }
}
