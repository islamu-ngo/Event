namespace Explore.Application.Management;

using Explore.Domain.Constants;

public sealed class ManagedControlPlaneOptions
{
    public const string SectionName = "ManagedControlPlane";

    public bool Enabled { get; set; }
    public Uri? ControlPlaneUrl { get; set; }
    public Guid ManagedInstanceId { get; set; }
    public string RegistrationToken { get; set; } = string.Empty;
    public TimeSpan CredentialLifetime { get; set; } = TimeSpan.FromDays(90);
    public int MaximumTenantCount { get; set; }
    public Uri? TenantAdministratorSignInUrl { get; set; }
}

public static class ManagedControlPlaneContract
{
    public const string ManagementApiVersion = "1.0";
    public const string CredentialSecretSettingKey =
        InfrastructureSecretSettingKeys.Management.ControlPlaneRegistrationCredentials;
    public const string EventToControlPlaneScope = "event-instance:register";
    public const string ControlPlaneReadScope = "event-management:read";
    public const string ControlPlaneWriteScope = "event-management:write";

    public static readonly IReadOnlyList<string> Capabilities =
    [
        "instance.registration",
        "instance.read",
        "instance.health.read",
        "instance.version.read",
        "instance.upgrade.preflight",
        "instance.upgrade.postflight",
        "credentials.directional"
    ];

    public static readonly IReadOnlyList<string> TenantProvisioningCapabilities =
    [
        "tenant.provision.preflight",
        "tenant.provision",
        "tenant.provision.status",
        "tenant.provision.cancel"
    ];
}
