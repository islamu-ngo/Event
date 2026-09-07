// ABOUTME: Defines validated security and token settings for the embedded local Identity provider.
// ABOUTME: Keeps signing material deployment-supplied while bounding lockout and token lifetimes.

namespace Explore.Application.Configuration;

public sealed class LocalIdentityOptions
{
    public const string SectionName = "Authentication:Local";
    public const string Issuer = "islamu-event-local";
    public const string Audience = "islamu-event-api";
    public const int MinimumPasswordLength = 12;
    public const int MaximumPasswordLength = 128;

    public string? JwtKey { get; set; }

    public int LockoutThreshold { get; set; } = 5;

    public int LockoutDurationMinutes { get; set; } = 15;

    public int AccessTokenLifetimeMinutes { get; set; } = 30;

    public static bool IsValid(LocalIdentityOptions options) =>
        options.LockoutThreshold is >= 1 and <= 20
        && options.LockoutDurationMinutes is >= 1 and <= 1_440
        && options.AccessTokenLifetimeMinutes is >= 5 and <= 60;
}
