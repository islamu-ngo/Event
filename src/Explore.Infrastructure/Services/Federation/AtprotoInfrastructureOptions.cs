namespace Explore.Infrastructure.Services.Federation;

public sealed class AtprotoInfrastructureOptions
{
    public const string SectionName = "Atproto";
    public string PublicUrl { get; set; } = string.Empty;
    public string CallbackPath { get; set; } = "/signin-atproto";
    public bool AllowDevelopmentLoopback { get; set; }
}

public sealed record AtprotoInfrastructureReadiness(bool IsReady, string? FailureCode);
