using System.Reflection;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Secrets;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Configuration;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Queries;

public sealed class GetInstanceOverviewQueryHandler(
    IDeploymentModeProvider deploymentModeProvider,
    ITenantRepository tenantRepository,
    IInstanceGovernanceSettingService governanceSettingService,
    IAuthProviderConfigurationService authProviderConfigurationService,
    IAuthorizationProviderConfigurationService authorizationProviderConfigurationService,
    IInstanceStorageSettingService storageSettingService,
    IInstanceSmtpSettingService smtpSettingService,
    ISecretAuthorityStatusReader secretAuthorityStatusReader,
    IConfiguration configuration)
    : IQueryHandler<GetInstanceOverviewQuery, InstanceOverviewDto>
{
    public async Task<InstanceOverviewDto> QueryAsync(
        GetInstanceOverviewQuery request,
        CancellationToken cancellationToken)
    {
        _ = request;

        var deploymentMode = await deploymentModeProvider.GetCurrentModeAsync(cancellationToken);
        var tenants = await tenantRepository.GetAll();
        var governanceSettings = await governanceSettingService.ReadSettingsAsync();
        var authProviderConfigured = await authProviderConfigurationService.IsConfiguredAsync();
        var authProviderConfiguration = await authProviderConfigurationService.ReadConfigurationAsync();
        var authorizationProviderConfigured = await authorizationProviderConfigurationService.IsConfiguredAsync();
        var authorizationProviderConfiguration = await authorizationProviderConfigurationService.ReadConfigurationAsync();
        var storageSettings = await storageSettingService.ReadSettingsAsync(cancellationToken);
        var smtpSettings = await smtpSettingService.ReadSettingsAsync();
        var secretAuthority = await secretAuthorityStatusReader.ReadAsync(cancellationToken);

        var statusCounts = BuildTenantStatusCounts(tenants);
        var publicOrigin = FirstConfiguredValue(
            configuration["PublicBaseUrl"],
            configuration["App:PublicBaseUrl"]);
        var adminOrigin = FirstConfiguredValue(
            configuration["InstanceAdmin:PublicOrigin"],
            configuration["Bff:PublicOrigin"],
            configuration["INSTANCE_ADMIN_PUBLIC_ORIGIN"]);

        return new InstanceOverviewDto
        {
            Version = ResolveApplicationVersion(),
            DeploymentMode = deploymentMode.ToString(),
            PublicOrigin = publicOrigin,
            AdminOrigin = adminOrigin,
            InstanceBaseDomain = NullIfWhiteSpace(governanceSettings.Domains.InstanceBaseDomain),
            TotalTenantCount = tenants.Count,
            ActiveTenantCount = statusCounts.Single(status => status.Status == nameof(TenantStatusEnum.Active)).Count,
            TenantStatusCounts = statusCounts,
            ProviderSummaries = BuildProviderSummaries(
                authProviderConfigured,
                authProviderConfiguration,
                authorizationProviderConfigured,
                authorizationProviderConfiguration,
                storageSettings,
                smtpSettings,
                secretAuthority),
            Warnings = BuildWarnings(
                deploymentMode,
                publicOrigin,
                governanceSettings.Domains.InstanceBaseDomain,
                authProviderConfigured,
                authorizationProviderConfigured,
                storageSettings,
                smtpSettings,
                secretAuthority)
        };
    }

    private static IReadOnlyList<InstanceTenantStatusCountDto> BuildTenantStatusCounts(
        IReadOnlyList<Tenant> tenants) =>
        Enum.GetValues<TenantStatusEnum>()
            .Select(status => new InstanceTenantStatusCountDto
            {
                Status = status.ToString(),
                Count = tenants.Count(tenant => tenant.TenantStatusId == (int)status)
            })
            .ToArray();

    private static IReadOnlyList<InstanceProviderSummaryDto> BuildProviderSummaries(
        bool authProviderConfigured,
        AuthProviderConfigurationDto authProviderConfiguration,
        bool authorizationProviderConfigured,
        AuthorizationProviderConfigurationDto authorizationProviderConfiguration,
        InstanceStorageSettingsDto storageSettings,
        InstanceSmtpSettingsDto smtpSettings,
        SecretAuthorityStatusSnapshot secretAuthority) =>
        [
            new()
            {
                Key = "secret-authority",
                DisplayName = "Secret authority",
                Configured = string.Equals(secretAuthority.Status, "configured", StringComparison.Ordinal),
                Status = secretAuthority.Status,
                Message = secretAuthority.Provider
            },
            new()
            {
                Key = "authentication",
                DisplayName = "Authentication",
                Configured = authProviderConfigured,
                Status = authProviderConfigured ? "configured" : "missing",
                Message = ResolveAuthProviderSummary(authProviderConfiguration)
            },
            new()
            {
                Key = "authorization",
                DisplayName = "Authorization",
                Configured = authorizationProviderConfigured || authorizationProviderConfiguration.AuthorizationProviderConfigured,
                Status = ResolveAuthorizationStatus(authorizationProviderConfiguration),
                Message = authorizationProviderConfiguration.Provider
            },
            new()
            {
                Key = "storage",
                DisplayName = "Storage",
                Configured = true,
                Status = ResolveStorageStatus(storageSettings.ProviderStatus),
                Message = storageSettings.Provider
            },
            new()
            {
                Key = "email",
                DisplayName = "Email",
                Configured = IsSmtpConfigured(smtpSettings),
                Status = IsSmtpConfigured(smtpSettings) ? "configured" : "missing"
            }
        ];

    private static IReadOnlyList<InstanceWarningDto> BuildWarnings(
        DeploymentMode deploymentMode,
        string? publicOrigin,
        string? instanceBaseDomain,
        bool authProviderConfigured,
        bool authorizationProviderConfigured,
        InstanceStorageSettingsDto storageSettings,
        InstanceSmtpSettingsDto smtpSettings,
        SecretAuthorityStatusSnapshot secretAuthority)
    {
        var warnings = new List<InstanceWarningDto>();

        if (!string.Equals(secretAuthority.Status, "configured", StringComparison.Ordinal))
        {
            warnings.Add(new InstanceWarningDto
            {
                Code = secretAuthority.RemediationCode,
                Severity = secretAuthority.Status == "required" ? "critical" : "warning",
                Message = $"The selected secret authority is {secretAuthority.Status}.",
                Remediation = "Follow the secret-provider recovery runbook; do not add a fallback source."
            });
        }

        if (deploymentMode != DeploymentMode.MultiTenant)
        {
            warnings.Add(new InstanceWarningDto
            {
                Code = "single_tenant_mode",
                Severity = "info",
                Message = "Instance administration is running this instance in single-tenant mode; tenant-fleet controls stay hidden.",
                Remediation = "Use the Operations deployment-mode runbook for a deliberate migration to multi-tenant mode when needed."
            });
        }

        if (string.IsNullOrWhiteSpace(publicOrigin) && string.IsNullOrWhiteSpace(instanceBaseDomain))
        {
            warnings.Add(new InstanceWarningDto
            {
                Code = "public_host_missing",
                Severity = "warning",
                Message = "No public origin or instance base domain is configured.",
                Remediation = "Set PublicBaseUrl or the instance base domain before creating tenant DNS records."
            });
        }

        if (!authProviderConfigured)
        {
            warnings.Add(new InstanceWarningDto
            {
                Code = "authentication_provider_missing",
                Severity = "critical",
                Message = "No authentication provider is configured.",
                Remediation = "Complete authentication-provider setup and verify OIDC discovery before enabling tenant onboarding."
            });
        }

        if (!authorizationProviderConfigured)
        {
            warnings.Add(new InstanceWarningDto
            {
                Code = "authorization_provider_missing",
                Severity = "warning",
                Message = "No explicit authorization provider configuration has been saved.",
                Remediation = "Save the intended authorization provider configuration; local authorization remains the default until changed."
            });
        }

        if (!IsSmtpConfigured(smtpSettings))
        {
            warnings.Add(new InstanceWarningDto
            {
                Code = "email_provider_missing",
                Severity = "warning",
                Message = "SMTP is not configured for platform email delivery.",
                Remediation = "Configure SMTP in instance settings before relying on platform email delivery."
            });
        }

        if (!storageSettings.ProviderStatus.IsAvailable
            && !string.IsNullOrWhiteSpace(storageSettings.ProviderStatus.FailureCode))
        {
            warnings.Add(new InstanceWarningDto
            {
                Code = "storage_provider_unavailable",
                Severity = "critical",
                Message = "The configured storage provider is reporting an unavailable state.",
                Remediation = "Verify storage provider settings, credentials, bucket or root reachability, and health checks before allowing upload-heavy operations."
            });
        }

        return warnings;
    }

    private static string ResolveAuthProviderSummary(AuthProviderConfigurationDto configuration)
    {
        var providers = new List<string>();

        if (configuration.PrimaryProviderId == (int)AuthenticationProviderKind.Local)
        {
            providers.Add(nameof(AuthenticationProviderKind.Local));
        }

        if (configuration.PrimaryProviderId == (int)AuthenticationProviderKind.Keycloak
            && !string.IsNullOrWhiteSpace(configuration.KeycloakAuthority)
            && !string.IsNullOrWhiteSpace(configuration.KeycloakClientId))
        {
            providers.Add("Keycloak");
        }

        if (configuration.AtprotoLoginEnabled)
        {
            providers.Add("ATProto");
        }

        if (configuration.GoogleSsoEnabled)
        {
            providers.Add("Google");
        }

        return providers.Count == 0 ? "None" : string.Join(", ", providers);
    }

    private static string ResolveAuthorizationStatus(AuthorizationProviderConfigurationDto configuration)
    {
        if (string.Equals(configuration.Provider, "cerbos", StringComparison.OrdinalIgnoreCase))
        {
            return configuration.CerbosEndpointVerified ? "verified" : "configured";
        }

        return configuration.AuthorizationProviderConfigured ? "configured" : "default";
    }

    private static string ResolveStorageStatus(InstanceStorageProviderStatusDto status)
    {
        if (status.IsAvailable)
        {
            return "available";
        }

        return string.IsNullOrWhiteSpace(status.FailureCode) ? "unverified" : "unavailable";
    }

    private static bool IsSmtpConfigured(InstanceSmtpSettingsDto settings) =>
        !string.IsNullOrWhiteSpace(settings.Host)
        && !string.IsNullOrWhiteSpace(settings.FromAddress);

    private static string ResolveApplicationVersion()
    {
        var assembly = typeof(GetInstanceOverviewQueryHandler).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString() ?? "unknown"
            : informationalVersion;
    }

    private static string? FirstConfiguredValue(params string?[] values) =>
        values.Select(NullIfWhiteSpace).FirstOrDefault(value => value is not null);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
