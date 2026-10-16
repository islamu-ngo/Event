namespace Explore.Application.Configuration;

public sealed class LocalIdentityOptions
{
    public const string SectionName = "Authentication:Local";
    public const string Issuer = "islamu-event-local";
    public const string Audience = "islamu-event-api";
    public const int MinimumPasswordLength = Explore.Domain.Authentication.LocalPasswordPolicy.MinimumScalars;
    public const int MaximumPasswordLength = Explore.Domain.Authentication.LocalPasswordPolicy.MaximumScalars;
    public const int MinimumHashIterations = 100_000;
    public const int MaximumHashIterations = 2_000_000;

    public string? PasswordBlocklistPath { get; set; }

    public string? PasswordBlocklistSha256 { get; set; }

    public int PasswordHashIterations { get; set; } = 210_000;

    public string? JwtKey { get; set; }

    public int LockoutThreshold { get; set; } = 5;

    public int LockoutDurationMinutes { get; set; } = 15;

    public int AccessTokenLifetimeMinutes { get; set; } = 30;

    public static bool IsValid(LocalIdentityOptions options) =>
        options.LockoutThreshold is >= 1 and <= 20
        && options.LockoutDurationMinutes is >= 1 and <= 1_440
        && options.AccessTokenLifetimeMinutes is >= 5 and <= 60
        && options.PasswordHashIterations is >= MinimumHashIterations and <= MaximumHashIterations;
}
