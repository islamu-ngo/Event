using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Modules;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Domain.Settings.Documents.Payloads;

namespace Explore.Application.Features.Management;

public sealed class ManagedTenantProvisioningPreflight(
    ITenantRepository tenantRepository,
    ITenantPlanRepository tenantPlanRepository,
    IModuleDefinitionRepository moduleDefinitionRepository,
    ITenantSettingRepository tenantSettingRepository,
    ISystemSettingRepository systemSettingRepository,
    ITenantBrandingSettingsDocumentLockService brandingLockService,
    TenantPlanStorageQuotaCeilingPolicy storageQuotaCeilingPolicy,
    ILocalCredentialAdministration credentials)
{
    public const string DomainNamespaceMutationKey = "domains.tenant_host_namespace";

    private static readonly HashSet<string> AllowedInitialSettingKeys =
    [
        AppearanceSettingDefinitions.Language.Key,
        AppearanceSettingDefinitions.ThemeMode.Key,
        EventSettingDefinitions.MaxSessionsPerEvent.Key,
        EventSettingDefinitions.UserSubmissionEnabled.Key,
        EventSettingDefinitions.RequireApproval.Key,
        EventSettingDefinitions.OrganizationSubmissionEnabled.Key,
        EventSettingDefinitions.GroupSubmissionEnabled.Key,
        RoutingSettingDefinitions.DefaultPublicHomePage.Key,
        PublicExperienceSettingDefinitions.EventCatalogLabel.Key,
        TenantSettingDefinitions.WhiteLabelingEnabled.Key
    ];

    public async Task<ManagedTenantProvisioningPreflightResult> EvaluateAsync(
        ManagementTenantProvisioningRequestDto request,
        bool requireProvisionablePlan,
        CancellationToken cancellationToken)
    {
        Tenant? existingTenant = await tenantRepository.GetTenantBySlug(request.TenantSlug);
        if (existingTenant is not null)
        {
            return ManagedTenantProvisioningPreflightResult.Fail(
                "tenant_slug_conflict",
                "The requested tenant slug is already in use.");
        }

        TenantPlanVersion? planVersion = await tenantPlanRepository.GetVersionAsync(
            request.Plan.VersionId,
            cancellationToken);
        if (planVersion is null
            || !string.Equals(planVersion.TenantPlan.Key, request.Plan.Key, StringComparison.Ordinal))
        {
            return ManagedTenantProvisioningPreflightResult.Fail(
                "tenant_plan_not_found",
                "The requested tenant plan version was not found for the supplied plan key.");
        }

        if (requireProvisionablePlan
            && (planVersion.TenantPlanStatusId != (int)TenantPlanStatusEnum.Published
                || !planVersion.IsActiveForProvisioning))
        {
            return ManagedTenantProvisioningPreflightResult.Fail(
                "tenant_plan_not_provisionable",
                "The requested tenant plan version is not active for provisioning.");
        }

        if (!QuotasMatch(request.Plan.Quotas, planVersion.Quotas))
        {
            return ManagedTenantProvisioningPreflightResult.Fail(
                "tenant_plan_quota_mismatch",
                "The requested quotas do not match the immutable tenant plan version.");
        }

        string? quotaError = await storageQuotaCeilingPolicy.ValidateAsync(
            planVersion.Quotas,
            cancellationToken);
        if (quotaError is not null)
        {
            return ManagedTenantProvisioningPreflightResult.Fail(
                quotaError,
                "The requested plan storage quota exceeds the Event instance ceiling.");
        }

        IReadOnlyList<ModuleDefinition> modules = await moduleDefinitionRepository.GetActiveByKeysAsync(
            request.ApprovedModules,
            cancellationToken);
        if (modules.Count != request.ApprovedModules.Count)
        {
            return ManagedTenantProvisioningPreflightResult.Fail(
                "tenant_module_unavailable",
                "One or more requested modules are unavailable on this Event instance.");
        }

        ManagedTenantProvisioningPreflightResult? domainFailure = await ValidateDomainIntentAsync(
            request,
            cancellationToken);
        if (domainFailure is not null)
        {
            return domainFailure;
        }

        if (request.Administrator.LocalIdentity is { } local
            && await credentials.ReadLinkedIdentityAsync(local.LocalSubjectId, cancellationToken) is null)
        {
            return ManagedTenantProvisioningPreflightResult.Fail(
                "tenant_local_administrator_unavailable",
                "An existing linked Local administrator in ChangeRequired or Ready state is required.");
        }

        BrandingSettings branding = MapBranding(request);
        var brandingLockState = await brandingLockService.GetLockStateAsync(cancellationToken);
        IReadOnlyList<string> brandingErrors = brandingLockService.ValidateAllowedChanges(
            new BrandingSettings { DisplayName = request.TenantName },
            branding,
            brandingLockState);
        if (brandingErrors.Count > 0)
        {
            return ManagedTenantProvisioningPreflightResult.Fail(
                "tenant_branding_policy_denied",
                brandingErrors[0]);
        }

        ManagedTenantProvisioningSettingsResult settings = await ResolveSettingsAsync(
            request,
            planVersion,
            cancellationToken);
        if (!settings.Success)
        {
            return ManagedTenantProvisioningPreflightResult.Fail(settings.FailureCode!, settings.Error!);
        }

        return ManagedTenantProvisioningPreflightResult.Pass(
            new ManagedTenantProvisioningResolvedBootstrap(
                planVersion.TenantPlan,
                planVersion,
                modules,
                settings.Settings,
                branding));
    }

    private async Task<ManagedTenantProvisioningPreflightResult?> ValidateDomainIntentAsync(
        ManagementTenantProvisioningRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.Domain is null)
        {
            return null;
        }

        string? subdomain = request.Domain.Subdomain;
        string? customDomain = request.Domain.CustomDomain;
        if (!string.IsNullOrWhiteSpace(subdomain)
            && !string.IsNullOrWhiteSpace(customDomain)
            && string.Equals(subdomain, customDomain, StringComparison.OrdinalIgnoreCase))
        {
            return ManagedTenantProvisioningPreflightResult.Fail(
                "tenant_domain_conflict",
                "The requested subdomain and custom domain must use different hosts.");
        }

        foreach (string host in new[] { subdomain, customDomain }.OfType<string>())
        {
            TenantSetting? collision = await tenantSettingRepository.GetByDomainHostAsync(
                host,
                cancellationToken);
            if (collision is not null)
            {
                return ManagedTenantProvisioningPreflightResult.Fail(
                    "tenant_domain_conflict",
                    "The requested tenant domain host is already in use.");
            }
        }

        if (string.IsNullOrWhiteSpace(customDomain))
        {
            return null;
        }

        SystemSetting? allowCustomDomain = await systemSettingRepository.GetByKey(
            GovernanceSettingKeys.Domains.AllowTenantCustomDomain,
            cancellationToken);
        if (!SettingValueSerializer.DeserializeBool(allowCustomDomain?.Value, true))
        {
            return ManagedTenantProvisioningPreflightResult.Fail(
                "tenant_custom_domain_disabled",
                "Custom tenant domains are disabled by instance policy.");
        }

        return null;
    }

    private async Task<ManagedTenantProvisioningSettingsResult> ResolveSettingsAsync(
        ManagementTenantProvisioningRequestDto request,
        TenantPlanVersion planVersion,
        CancellationToken cancellationToken)
    {
        var settings = new Dictionary<string, TenantSettingOverrideUpsert>(StringComparer.Ordinal);
        foreach (TenantPlanVersionSetting planSetting in planVersion.Settings)
        {
            ManagedTenantProvisioningSettingsResult? failure = await TryAddSettingAsync(
                settings,
                planSetting.SettingKey,
                planSetting.JsonValue,
                planSetting.IsLocked,
                allowPlanSetting: true,
                cancellationToken);
            if (failure is not null)
            {
                return failure;
            }
        }

        foreach (ManagementTenantInitialSettingDto initialSetting in request.InitialSettings)
        {
            ManagedTenantProvisioningSettingsResult? failure = await TryAddSettingAsync(
                settings,
                initialSetting.Key,
                initialSetting.JsonValue,
                isLocked: false,
                allowPlanSetting: false,
                cancellationToken);
            if (failure is not null)
            {
                return failure;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Domain?.Subdomain))
        {
            ManagedTenantProvisioningSettingsResult? failure = await TryAddSettingAsync(
                settings,
                GovernanceSettingKeys.Domains.TenantSubdomain,
                JsonSerializer.Serialize(request.Domain.Subdomain),
                isLocked: false,
                allowPlanSetting: true,
                cancellationToken);
            if (failure is not null)
            {
                return failure;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Domain?.CustomDomain))
        {
            ManagedTenantProvisioningSettingsResult? failure = await TryAddSettingAsync(
                settings,
                GovernanceSettingKeys.Domains.TenantCustomDomain,
                JsonSerializer.Serialize(request.Domain.CustomDomain),
                isLocked: false,
                allowPlanSetting: true,
                cancellationToken);
            if (failure is not null)
            {
                return failure;
            }
        }

        return ManagedTenantProvisioningSettingsResult.Pass(
            settings.Values.OrderBy(setting => setting.SettingKey, StringComparer.Ordinal).ToArray());
    }

    private async Task<ManagedTenantProvisioningSettingsResult?> TryAddSettingAsync(
        IDictionary<string, TenantSettingOverrideUpsert> settings,
        string key,
        string value,
        bool isLocked,
        bool allowPlanSetting,
        CancellationToken cancellationToken)
    {
        SettingDefinition? definition = SettingRegistry.Get(key);
        if (definition is null
            || definition.IsSensitive
            || definition.MinScope > SettingScope.Tenant
            || definition.MaxScope < SettingScope.Tenant
            || (!allowPlanSetting && !AllowedInitialSettingKeys.Contains(key)))
        {
            return ManagedTenantProvisioningSettingsResult.Fail(
                "tenant_setting_not_allowed",
                $"Setting '{key}' is not allowed during managed tenant provisioning.");
        }

        if (!IsValidValue(value, definition))
        {
            return ManagedTenantProvisioningSettingsResult.Fail(
                "tenant_setting_invalid",
                $"Setting '{key}' does not match its registered value contract.");
        }

        if (await systemSettingRepository.IsLocked(key, cancellationToken))
        {
            return ManagedTenantProvisioningSettingsResult.Fail(
                "tenant_setting_locked",
                $"Setting '{key}' is locked by instance policy.");
        }

        if (!settings.TryAdd(key, new TenantSettingOverrideUpsert(key, value, isLocked)))
        {
            return ManagedTenantProvisioningSettingsResult.Fail(
                "tenant_setting_conflict",
                $"Setting '{key}' is supplied by more than one bootstrap source.");
        }

        return null;
    }

    private static bool IsValidValue(string value, SettingDefinition definition)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            JsonElement root = document.RootElement;
            bool typeMatches = definition.ValueType switch
            {
                SettingValueType.String => root.ValueKind == JsonValueKind.String,
                SettingValueType.Boolean => root.ValueKind is JsonValueKind.True or JsonValueKind.False,
                SettingValueType.Integer => root.ValueKind == JsonValueKind.Number && root.TryGetInt32(out _),
                SettingValueType.Long => root.ValueKind == JsonValueKind.Number && root.TryGetInt64(out _),
                SettingValueType.Decimal => root.ValueKind == JsonValueKind.Number && root.TryGetDecimal(out _),
                SettingValueType.DateTime => root.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(root.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
                SettingValueType.Json => true,
                _ => false
            };
            if (!typeMatches || definition.AllowedValues is null)
            {
                return typeMatches;
            }

            string? allowedValue = root.ValueKind == JsonValueKind.String
                ? root.GetString()
                : root.GetRawText();
            return definition.AllowedValues.Contains(allowedValue, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool QuotasMatch(
        IReadOnlyList<ManagementTenantQuotaDto> requested,
        IEnumerable<TenantPlanVersionQuota> configured)
    {
        ManagementTenantQuotaDto[] configuredValues = configured
            .Select(quota => new ManagementTenantQuotaDto(quota.QuotaKey, quota.Limit))
            .OrderBy(quota => quota.Key, StringComparer.Ordinal)
            .ToArray();
        return requested.Count == configuredValues.Length
            && requested.OrderBy(quota => quota.Key, StringComparer.Ordinal)
                .SequenceEqual(configuredValues);
    }

    private static BrandingSettings MapBranding(ManagementTenantProvisioningRequestDto request) => new()
    {
        DisplayName = request.Branding?.DisplayName ?? request.TenantName,
        LogoUrl = request.Branding?.LogoUrl,
        FaviconUrl = request.Branding?.FaviconUrl,
        CustomCssUrl = request.Branding?.CustomCssUrl
    };
}

public sealed record ManagedTenantProvisioningResolvedBootstrap(
    TenantPlan Plan,
    TenantPlanVersion PlanVersion,
    IReadOnlyList<ModuleDefinition> Modules,
    IReadOnlyList<TenantSettingOverrideUpsert> Settings,
    BrandingSettings Branding);

public sealed record ManagedTenantProvisioningPreflightResult(
    bool Success,
    string? FailureCode,
    string? Error,
    ManagedTenantProvisioningResolvedBootstrap? Resolved)
{
    public static ManagedTenantProvisioningPreflightResult Pass(
        ManagedTenantProvisioningResolvedBootstrap resolved) => new(true, null, null, resolved);

    public static ManagedTenantProvisioningPreflightResult Fail(string failureCode, string error) =>
        new(false, failureCode, error, null);
}

internal sealed record ManagedTenantProvisioningSettingsResult(
    bool Success,
    string? FailureCode,
    string? Error,
    IReadOnlyList<TenantSettingOverrideUpsert> Settings)
{
    public static ManagedTenantProvisioningSettingsResult Pass(
        IReadOnlyList<TenantSettingOverrideUpsert> settings) => new(true, null, null, settings);

    public static ManagedTenantProvisioningSettingsResult Fail(string failureCode, string error) =>
        new(false, failureCode, error, []);
}
