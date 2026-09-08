using Explore.Application.DTOs.Analytics;
using Explore.Application.DTOs.Storage;
using Explore.Application.Models.Common;
using Explore.Domain.Enums.Analytics;

namespace Explore.Application.DTOs.Instance;

public sealed record PatchInstanceStorageSettingsDto
{
    public OptionalUpdate<InstanceStoragePolicyWriteDto> Policy { get; init; } = OptionalUpdate<InstanceStoragePolicyWriteDto>.Unspecified();
    public OptionalUpdate<InstanceS3ConfigurationWriteDto> S3Configuration { get; init; } = OptionalUpdate<InstanceS3ConfigurationWriteDto>.Unspecified();
    public bool HasChanges() => Policy.HasValue || S3Configuration.HasValue;
}

public sealed record InstanceStoragePolicyWriteDto
{
    private IReadOnlyList<StorageRouteSettingsDto> _routes =
        Array.AsReadOnly(Array.Empty<StorageRouteSettingsDto>());

    public string Provider { get; init; } = string.Empty;
    public long DefaultMaxUploadBytes { get; init; }
    public long DefaultTenantQuotaBytes { get; init; }
    public long InstanceMaxUploadBytes { get; init; }
    public bool LockTenantStorage { get; init; }
    public IReadOnlyList<StorageRouteSettingsDto> Routes
    {
        get => _routes;
        init => _routes = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
}

public sealed record InstanceS3ConfigurationWriteDto
{
    public string Endpoint { get; init; } = string.Empty;
    public string PublicEndpoint { get; init; } = string.Empty;
    public string BucketName { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public bool ForcePathStyle { get; init; }
    public int UploadUrlExpirationMinutes { get; init; }
}

public sealed record PatchInstanceSmtpSettingsDto
{
    public OptionalUpdate<bool> DeliveryEnabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<InstanceSmtpConfigurationWriteDto> Configuration { get; init; } = OptionalUpdate<InstanceSmtpConfigurationWriteDto>.Unspecified();
    public bool HasChanges() => DeliveryEnabled.HasValue || Configuration.HasValue;
}

public sealed record InstanceSmtpConfigurationWriteDto
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Security { get; init; } = string.Empty;
    public string FromAddress { get; init; } = string.Empty;
    public string FromName { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; }
    public bool SkipCertificateValidation { get; init; }
}

public sealed record PatchResolverConfigurationDto
{
    public OptionalUpdate<bool> HeaderEnabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> SubdomainEnabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> CustomDomainEnabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> PathEnabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<string?> PathPrefix { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<string?> InstanceBaseDomain { get; init; } = OptionalUpdate<string?>.Unspecified();
    public OptionalUpdate<bool> AllowTenantCustomDomains { get; init; } = OptionalUpdate<bool>.Unspecified();
    public bool HasChanges() => HeaderEnabled.HasValue || SubdomainEnabled.HasValue || CustomDomainEnabled.HasValue
        || PathEnabled.HasValue || PathPrefix.HasValue || InstanceBaseDomain.HasValue || AllowTenantCustomDomains.HasValue;
}

public sealed record PatchAnalyticsGovernanceSettingsDto
{
    public OptionalUpdate<bool> CookieConsentEnabled { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<DeclineBehavior> DeclineBehavior { get; init; } = OptionalUpdate<DeclineBehavior>.Unspecified();
    public OptionalUpdate<int> ConsentCookieLifetimeDays { get; init; } = OptionalUpdate<int>.Unspecified();
    public OptionalUpdate<bool> GlobalDisableClientTracking { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<PosthogCookielessMode> PosthogCookielessMode { get; init; } = OptionalUpdate<PosthogCookielessMode>.Unspecified();
    public OptionalUpdate<PosthogPersonProfiles> PosthogPersonProfiles { get; init; } = OptionalUpdate<PosthogPersonProfiles>.Unspecified();
    public OptionalUpdate<bool> PosthogSessionReplay { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> PosthogAutocapture { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> PosthogHeatmaps { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> PosthogToolbar { get; init; } = OptionalUpdate<bool>.Unspecified();
    public bool HasChanges() => CookieConsentEnabled.HasValue || DeclineBehavior.HasValue || ConsentCookieLifetimeDays.HasValue
        || GlobalDisableClientTracking.HasValue || PosthogCookielessMode.HasValue || PosthogPersonProfiles.HasValue
        || PosthogSessionReplay.HasValue || PosthogAutocapture.HasValue || PosthogHeatmaps.HasValue || PosthogToolbar.HasValue;
}

public sealed record PatchAuthProviderConfigurationDto
{
    public OptionalUpdate<AuthProviderConfigurationWriteDto> Configuration { get; init; } = OptionalUpdate<AuthProviderConfigurationWriteDto>.Unspecified();
    public bool HasChanges() => Configuration.HasValue;
}

public sealed record AuthProviderConfigurationWriteDto
{
    public int PrimaryProviderId { get; init; } =
        (int)global::Explore.Domain.Enums.AuthenticationProviderKind.Local;
    public bool LockPrimaryProvider { get; init; }
    public string KeycloakAuthority { get; init; } = string.Empty;
    public string KeycloakClientId { get; init; } = string.Empty;
    public string KeycloakClientSecret { get; init; } = string.Empty;
    public bool AtprotoLoginEnabled { get; init; }
    public string AtprotoPublicUrl { get; init; } = string.Empty;
    public bool GoogleSsoEnabled { get; init; }
    public string GoogleClientId { get; init; } = string.Empty;
    public string GoogleClientSecret { get; init; } = string.Empty;
    public bool LockAtprotoLoginEnabled { get; init; }
    public bool LockGoogleSsoEnabled { get; init; }
    public global::Explore.Domain.Enums.PublicOnboardingPolicy KeycloakPublicOnboardingPolicy { get; init; }
    public string KeycloakPublicSignupUrl { get; init; } = string.Empty;
    public global::Explore.Domain.Enums.PublicOnboardingPolicy GooglePublicOnboardingPolicy { get; init; }
    public string GooglePublicSignupUrl { get; init; } = string.Empty;
}

public sealed record PatchAuthorizationProviderConfigurationDto
{
    public OptionalUpdate<AuthorizationProviderConfigurationWriteDto> Configuration { get; init; } = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>.Unspecified();
    public bool HasChanges() => Configuration.HasValue;
}

public sealed record AuthorizationProviderConfigurationWriteDto
{
    public string Provider { get; init; } = string.Empty;
    public string CerbosGrpcEndpoint { get; init; } = string.Empty;
    public string CerbosAdminEndpoint { get; init; } = string.Empty;
}
