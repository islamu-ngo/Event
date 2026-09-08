using System.Text.Json.Serialization;
using Explore.Application.DTOs.TenantSettings;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.Management;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantProvisioningRequestDto
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string ExternalRequestId { get; init; }
    public required string ExternalCustomerReference { get; init; }
    public required string TenantName { get; init; }
    public required string TenantSlug { get; init; }
    public required ManagementTenantAdministratorDto Administrator { get; init; }
    public TenantDirectoryOperatorIdentityInputDto? DirectoryOperatorIdentity { get; init; }
    public required ManagementTenantPlanDto Plan { get; init; }
    public IReadOnlyList<string> ApprovedModules { get; init; } = [];
    public ManagementTenantDomainIntentDto? Domain { get; init; }
    public ManagementTenantBrandingIntentDto? Branding { get; init; }
    public IReadOnlyList<ManagementTenantInitialSettingDto> InitialSettings { get; init; } = [];
    public ManagementTenantCallbackMetadataDto? Callback { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantAdministratorDto
{
    public ManagementTenantExternalIdentityDto? ExternalIdentity { get; init; }
    public ManagementTenantLocalIdentityDto? LocalIdentity { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantExternalIdentityDto
{
    public required string IdentityProvider { get; init; }
    public required string Subject { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? DisplayName { get; init; }
    public bool EmailVerified { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantLocalIdentityDto
{
    public Guid LocalSubjectId { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantPlanDto
{
    public required string Key { get; init; }
    public Guid VersionId { get; init; }
    public IReadOnlyList<ManagementTenantQuotaDto> Quotas { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantQuotaDto(string Key, long Limit);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantDomainIntentDto
{
    public string? Subdomain { get; init; }
    public string? CustomDomain { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantBrandingIntentDto
{
    public string? DisplayName { get; init; }
    public string? LogoUrl { get; init; }
    public string? FaviconUrl { get; init; }
    public string? CustomCssUrl { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantInitialSettingDto(string Key, string JsonValue);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantCallbackMetadataDto
{
    public string? CorrelationId { get; init; }
    public string? CallbackReference { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantProvisioningCapacityDto(
    int MaximumTenants,
    int ActiveTenants,
    int ReservedOperations,
    int AvailableSlots,
    bool ProvisioningAvailable,
    string? BlockerCode);

public sealed record ManagementTenantProvisioningOperationDto(
    Guid OperationId,
    string ExternalRequestId,
    string ExternalCustomerReference,
    string TenantSlug,
    string Status,
    Guid? TenantId,
    Guid? TenantAdministratorUserId,
    string? FailureCode,
    string? CorrelationId,
    bool CanCancel,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? FailedAt,
    DateTime? CancelledAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantProvisioningBlockerDto(string Code, string Message);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantResolvedSettingDto(
    string Key,
    string JsonValue,
    bool IsLocked);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagementTenantProvisioningPreflightDto(
    int SchemaVersion,
    string ManagementApiVersion,
    string EventVersion,
    Guid ManagedInstanceId,
    Guid? EventInstanceId,
    string RegistrationState,
    DeploymentMode DeploymentMode,
    string NormalizedRequestHash,
    string TenantSlug,
    bool Ready,
    bool RequiresSchedulingRevalidation,
    IReadOnlyList<ManagementTenantProvisioningBlockerDto> Blockers,
    ManagementTenantProvisioningCapacityDto? Capacity,
    ManagementTenantPlanDto? ResolvedPlan,
    IReadOnlyList<string> AcceptedModules,
    ManagementTenantDomainIntentDto? AcceptedDomain,
    ManagementTenantBrandingIntentDto? AcceptedBranding,
    IReadOnlyList<ManagementTenantResolvedSettingDto> AcceptedSettings,
    string? CorrelationId,
    DateTime AssessedAt);
