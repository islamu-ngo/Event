// ABOUTME: Public AT Protocol OAuth client configuration for the Blazor BFF.
// ABOUTME: Defines the canonical URL-derived client identity and optional presentation metadata.

using Microsoft.AspNetCore.Authentication;

namespace Explore.Blazor.Authentication;

public class AtprotoAuthenticationOptions : AuthenticationSchemeOptions
{
    public string PublicUrl { get; set; } = string.Empty;

    public string CallbackPath { get; set; } = "/signin-atproto";

    public bool AllowDevelopmentLoopback { get; set; }

    public int StateLifetimeSeconds { get; set; } = 300;

    public List<AtprotoTenantOrigin> TenantOrigins { get; set; } = [];

    public string? ClientName { get; set; }

    public string? ClientUri { get; set; }

    public string? LogoUri { get; set; }

    public string? PolicyUri { get; set; }

    public string? TermsOfServiceUri { get; set; }
}

public sealed class AtprotoTenantOrigin
{
    public string Origin { get; set; } = string.Empty;

    public Guid TenantId { get; set; }

    public string TenantSlug { get; set; } = string.Empty;
}
