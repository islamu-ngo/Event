using System.Text.Json.Serialization;
using Explore.Application.DTOs.TenantSettings;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.Onboarding;

public sealed record CompleteInstanceOnboardingRequest
{
    public const string EmbeddedAdministrationAccess = "Embedded";
    public const string DedicatedAdminHostAdministrationAccess = "DedicatedAdminHost";
    public const string SeparateControlPlaneAppAdministrationAccess = "SeparateControlPlaneApp";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DeploymentMode DeploymentMode { get; set; } = DeploymentMode.SingleTenant;
    public SelfHostOnboardingProfileDto SiteProfile { get; init; } = new();
    public TenantDirectoryOperatorIdentityInputDto? DirectoryOperatorIdentity { get; init; }
    public string AdministrationAccessMode { get; init; } = EmbeddedAdministrationAccess;
    public string? AdminHost { get; init; }
    public string? InstanceName { get; init; }
}
