namespace Explore.Blazor.Client.Contracts.Services.Events;

/// <summary>
/// Version of the household-consent statement rendered by the client. Bumping the wording must bump
/// this constant so stored consents stay traceable to the exact text the owner agreed to.
/// </summary>
public static class PrivateHomeConsentStatement
{
    public const string CurrentVersion = "private-home-consent/2026-08";
}
