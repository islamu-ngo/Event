namespace Explore.Application.DTOs.Onboarding;

public sealed record SelfHostOnboardingProfileDto
{
    public string SiteName { get; set; } = string.Empty;
    public string? SupportEmail { get; init; }
    public string? CanonicalUrl { get; init; }
    public string Locale { get; init; } = "en";
    public string TimeZone { get; init; } = "UTC";
    public string? Purpose { get; init; }
}
