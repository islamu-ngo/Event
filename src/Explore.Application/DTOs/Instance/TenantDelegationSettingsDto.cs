namespace Explore.Application.DTOs.Instance;

public sealed record TenantDelegationSettingsDto
{
    public bool AllowTenantSelfServiceRegistration { get; set; }
    public bool AllowTenantWhiteLabeling { get; set; }
    public string DefaultPublicHomePage { get; set; } = "EventList";
    public bool LockTenantHomePagePreference { get; set; }
    public bool LockTenantSmtp { get; set; } = true;
    public bool LockTenantStorage { get; set; } = true;
    public bool LockTenantAnalytics { get; set; } = true;
    public bool LockTenantAiAssistant { get; set; } = true;
    public string AuthorizationProvider { get; init; } = "local";
}
