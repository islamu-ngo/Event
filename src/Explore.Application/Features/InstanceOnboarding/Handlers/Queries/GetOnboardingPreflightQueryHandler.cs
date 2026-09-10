using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Models.Storage;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public sealed class GetOnboardingPreflightQueryHandler(
    IInstanceBootstrapStateRepository instanceBootstrapStateRepository,
    IDeploymentModeProvider deploymentModeProvider,
    ISetupSecretProvider setupSecretProvider,
    ITenantRepository tenantRepository,
    ISystemSettingRepository systemSettingRepository,
    IConfiguration configuration,
    IAuthenticationProviderDispatcher authenticationProviders,
    IS3ConfigResolver? s3ConfigResolver = null,
    ISmtpConfigResolver? smtpConfigResolver = null,
    IS3PreflightVerifier? s3PreflightVerifier = null)
    : IRequestHandler<GetOnboardingPreflightQuery, OnboardingPreflightDto>
{
    public async Task<OnboardingPreflightDto> Handle(GetOnboardingPreflightQuery request, CancellationToken cancellationToken)
    {
        var result = new OnboardingPreflightDto();
        var bootstrap = await instanceBootstrapStateRepository.GetCurrent(cancellationToken);
        var onboardingCompleted = bootstrap?.Status == InstanceBootstrapStatus.Completed;
        var deploymentMode = onboardingCompleted
            ? bootstrap!.DeploymentMode
            : await deploymentModeProvider.GetConfiguredOnboardingModeAsync(cancellationToken);

        result.DeploymentMode = deploymentMode.ToString();

        AddSetupSecretCheck(result, onboardingCompleted);
        AddRepositoryReachabilityCheck(result, bootstrap);
        AddMigrationCheck(result);
        AddDeploymentModeCheck(result, deploymentMode);
        await AddDefaultTenantCheckAsync(result, deploymentMode, onboardingCompleted);
        await AddAuthConfigurationCheckAsync(result, cancellationToken);
        await AddCanonicalHostCheckAsync(result);
        await AddDnsChecklistWarningsAsync(result, deploymentMode);
        await AddOperationalWarningsAsync(result, cancellationToken);

        return result;
    }

    private void AddSetupSecretCheck(OnboardingPreflightDto result, bool onboardingCompleted)
    {
        if (onboardingCompleted)
        {
            AddBlocking(result, "setup_secret", "Setup secret", OnboardingPreflightCheckStatus.Pass, "Onboarding is already completed and setup mode is locked.");
            return;
        }

        if (!setupSecretProvider.IsSetupModeActive)
        {
            AddBlocking(result, "setup_secret", "Setup secret", OnboardingPreflightCheckStatus.Fail, "Setup mode is not active.", "The setup secret provider is locked or has not initialized setup state.");
            return;
        }

        var source = setupSecretProvider.IsFromEnvironmentVariable ? "environment" : "internal generated fallback";
        AddBlocking(result, "setup_secret", "Setup secret", OnboardingPreflightCheckStatus.Pass, $"Setup secret is active from {source}.");
    }

    private static void AddRepositoryReachabilityCheck(OnboardingPreflightDto result, object? bootstrap)
    {
        AddBlocking(
            result,
            "database_reachable",
            "Database reachable",
            OnboardingPreflightCheckStatus.Pass,
            bootstrap is null
                ? "Database read completed and no completed bootstrap state exists yet."
                : "Database read completed and bootstrap state is available.");
    }

    private static void AddMigrationCheck(OnboardingPreflightDto result)
    {
        AddWarning(result, "database_migrations", "Database migrations", "Application repositories are reachable; migration freshness is enforced by the migration service and build pipeline.");
    }

    private static void AddDeploymentModeCheck(OnboardingPreflightDto result, DeploymentMode deploymentMode)
    {
        AddBlocking(result, "deployment_mode", "Deployment mode", OnboardingPreflightCheckStatus.Pass, $"Deployment mode resolved to {deploymentMode}.");
    }

    private async Task AddDefaultTenantCheckAsync(OnboardingPreflightDto result, DeploymentMode deploymentMode, bool onboardingCompleted)
    {
        if (deploymentMode != DeploymentMode.SingleTenant)
        {
            AddBlocking(result, "default_tenant", "Default tenant", OnboardingPreflightCheckStatus.Pass, "Default tenant is not required for MultiTenant preflight.");
            return;
        }

        var defaultTenant = await tenantRepository.GetById(PlatformDefaults.DefaultTenantId);
        if (!onboardingCompleted && defaultTenant is null)
        {
            AddBlocking(result, "default_tenant", "Default tenant", OnboardingPreflightCheckStatus.Pass, "SingleTenant default tenant will be created during instance completion.");
            return;
        }

        AddBlocking(
            result,
            "default_tenant",
            "Default tenant",
            defaultTenant is null ? OnboardingPreflightCheckStatus.Fail : OnboardingPreflightCheckStatus.Pass,
            defaultTenant is null
                ? "SingleTenant default tenant has not been created yet."
                : "SingleTenant default tenant exists.",
            defaultTenant is null ? "Complete instance onboarding to create the internal default tenant." : null);
    }

    private async Task AddAuthConfigurationCheckAsync(OnboardingPreflightDto result, CancellationToken cancellationToken)
    {
        var localReady = await authenticationProviders.GetActivePrimaryProviderAsync(cancellationToken) == AuthenticationProviderKind.Local;
        var keycloakReady = HasConfigurationValue("Keycloak:Authority")
            && (HasConfigurationValue("Keycloak:ClientId") || HasConfigurationValue("Keycloak:Audience"));
        var storedKeycloak = await IsKeycloakPrimaryProviderAsync()
            && await HasSettingValueAsync(GovernanceSettingKeys.Authentication.KeycloakAuthority)
            && await HasSettingValueAsync(GovernanceSettingKeys.Authentication.KeycloakClientId);
        var atprotoReady = await HasEnabledSettingAsync(GovernanceSettingKeys.Authentication.AtprotoLoginEnabled)
            && await HasSettingValueAsync(GovernanceSettingKeys.Authentication.AtprotoPublicUrl);
        var googleReady = await HasEnabledSettingAsync(GovernanceSettingKeys.Authentication.GoogleSsoEnabled)
            && await HasSettingValueAsync(GovernanceSettingKeys.Authentication.GoogleClientId);

        AddBlocking(
            result,
            "auth_config",
            "Authentication configuration",
            localReady || keycloakReady || storedKeycloak || atprotoReady || googleReady
                ? OnboardingPreflightCheckStatus.Pass
                : OnboardingPreflightCheckStatus.Fail,
            localReady || keycloakReady || storedKeycloak || atprotoReady || googleReady
                ? "At least one authentication provider has enough configuration to continue."
                : "No authentication provider appears ready for admin sign-in.",
            localReady || keycloakReady || storedKeycloak || atprotoReady || googleReady
                ? null
                : "Configure Keycloak, AT Protocol, or Google SSO before completing onboarding.");
    }

    private async Task AddCanonicalHostCheckAsync(OnboardingPreflightDto result)
    {
        var configuredHost = configuration["PublicBaseUrl"]
            ?? configuration["App:PublicBaseUrl"]
            ?? configuration["ASPNETCORE_URLS"];
        var storedDomain = await HasSettingValueAsync(GovernanceSettingKeys.Domains.InstanceBaseDomain);

        AddBlocking(
            result,
            "canonical_host",
            "Canonical host",
            !string.IsNullOrWhiteSpace(configuredHost) || storedDomain
                ? OnboardingPreflightCheckStatus.Pass
                : OnboardingPreflightCheckStatus.Fail,
            !string.IsNullOrWhiteSpace(configuredHost) || storedDomain
                ? "Canonical host/domain is available from configuration or Site Profile settings."
                : "Canonical host/domain is not configured.",
            !string.IsNullOrWhiteSpace(configuredHost) || storedDomain
                ? null
                : "Set the canonical URL in Site Profile or provide a public base URL configuration before launch.");
    }

    private async Task AddDnsChecklistWarningsAsync(OnboardingPreflightDto result, DeploymentMode deploymentMode)
    {
        if (deploymentMode != DeploymentMode.MultiTenant)
        {
            return;
        }

        var configuredPublicHost = HostFromValue(configuration["PublicBaseUrl"])
            ?? HostFromValue(configuration["App:PublicBaseUrl"])
            ?? HostFromValue(configuration["ASPNETCORE_URLS"]);
        var instanceBaseDomain = await GetSettingValueAsync(GovernanceSettingKeys.Domains.InstanceBaseDomain);
        var publicHost = configuredPublicHost ?? NormalizeHost(instanceBaseDomain);
        var adminHost = HostFromValue(configuration["ControlPlane:PublicOrigin"])
            ?? HostFromValue(configuration["Bff:PublicOrigin"])
            ?? HostFromValue(configuration["CONTROL_PLANE_PUBLIC_ORIGIN"])
            ?? NormalizeHost(await GetSettingValueAsync(GovernanceSettingKeys.Domains.AdminHost));
        var customDomainsEnabled = await HasEnabledSettingAsync(GovernanceSettingKeys.Domains.AllowTenantCustomDomain);

        AddWarning(
            result,
            "dns_public_platform",
            "Public platform DNS",
            string.IsNullOrWhiteSpace(publicHost)
                ? "Public platform host is not configured yet; add the canonical URL in Site Profile before creating DNS records."
                : $"Point the public platform host {publicHost} at the Blazor/BFF entry point before launch.",
            "Create an A/AAAA or CNAME record at your edge provider, then verify TLS termination and forwarded headers.");

        AddWarning(
            result,
            "dns_wildcard_tenant",
            "Wildcard tenant DNS",
            string.IsNullOrWhiteSpace(publicHost)
                ? "Wildcard tenant DNS is skipped until the public platform host is known."
                : $"Point *.{publicHost} at the Blazor/BFF entry point so tenant subdomains can resolve.",
            "Use a wildcard CNAME when possible; otherwise document the per-tenant DNS process before creating tenants.");

        AddWarning(
            result,
            "dns_control_plane",
            "Control-plane host DNS",
            string.IsNullOrWhiteSpace(adminHost)
                ? "Dedicated control-plane host is not configured; embedded administration remains available after launch."
                : $"Point the dedicated control-plane host {adminHost} at the admin/BFF entry point.",
            "Use a host restricted to instance admins. If you keep embedded administration, no extra DNS record is required now.");

        AddWarning(
            result,
            "dns_custom_domain_cname",
            "Tenant custom-domain CNAME",
            customDomainsEnabled
                ? "Tenant custom domains are enabled; publish CNAME guidance for tenant-owned hostnames before inviting tenants."
                : "Tenant custom domains are disabled; custom-domain CNAME guidance can wait until the feature is enabled.",
            string.IsNullOrWhiteSpace(publicHost)
                ? "Use the platform host or documented edge target once it is configured."
                : $"Tenants should CNAME their hostnames to {publicHost} or the documented edge target.");
    }

    private async Task AddOperationalWarningsAsync(OnboardingPreflightDto result, CancellationToken cancellationToken)
    {
        if (!await IsSmtpConfiguredAsync(cancellationToken))
        {
            AddWarning(result, "smtp", "SMTP", "SMTP is not configured; email delivery features may be unavailable after launch.");
        }

        await AddObjectStorageWarningAsync(result, cancellationToken);

        AddWarning(result, "backups", "Backups", "Backup verification is not configured in application settings; confirm database backups operationally.");
        AddWarning(result, "observability", "Logs, metrics, and health", "Confirm logs, metrics, and health endpoint monitoring in the hosting environment.");
        AddWarning(result, "public_exposure", "Public exposure", "Review public URL, search visibility, and signup/submission policies before announcing the site.");
    }

    private async Task AddObjectStorageWarningAsync(
        OnboardingPreflightDto result,
        CancellationToken cancellationToken)
    {
        var config = s3ConfigResolver is null
            ? null
            : await s3ConfigResolver.ResolveAsync(cancellationToken);
        if (config is not null && s3PreflightVerifier is not null)
        {
            var preflight = await s3PreflightVerifier.VerifyAsync(
                new S3PreflightRequest { Configuration = config },
                cancellationToken);
            if (!preflight.IsSuccess)
            {
                var failure = preflight.Steps.FirstOrDefault(step =>
                    step.Status is S3PreflightStepStatus.Failed or S3PreflightStepStatus.Warning);
                AddWarning(
                    result,
                    "object_storage",
                    "Object storage",
                    failure?.Message ?? "S3-compatible object storage preflight did not pass.",
                    failure?.Detail ?? "Verify the endpoint, bucket, region, and credentials, then run Test Provider.");
            }

            return;
        }

        if (!await IsObjectStorageConfiguredAsync(cancellationToken))
        {
            AddWarning(result, "object_storage", "Object storage", "Object storage is not configured; media uploads may be limited after launch.");
        }
    }

    private async Task<bool> IsObjectStorageConfiguredAsync(CancellationToken cancellationToken)
    {
        return s3ConfigResolver is not null
            && await s3ConfigResolver.IsConfiguredAsync(cancellationToken);
    }

    private async Task<bool> IsSmtpConfiguredAsync(CancellationToken cancellationToken)
    {
        if (smtpConfigResolver is not null && await smtpConfigResolver.ResolveAsync(cancellationToken) is not null)
        {
            return true;
        }

        if (await HasSettingValueAsync(GovernanceSettingKeys.Email.SmtpHost))
        {
            return true;
        }

        return false;
    }

    private bool HasConfigurationValue(string key) => !string.IsNullOrWhiteSpace(configuration[key]);

    private async Task<bool> HasEnabledSettingAsync(string key)
    {
        var value = await GetSettingValueAsync(key);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "\"true\"", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> HasSettingValueAsync(string key) => !string.IsNullOrWhiteSpace(await GetSettingValueAsync(key));

    private async Task<bool> IsKeycloakPrimaryProviderAsync()
    {
        var value = await GetSettingValueAsync(GovernanceSettingKeys.Authentication.PrimaryProviderId);
        return int.TryParse(
                   value,
                   System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var providerId)
               && providerId == (int)AuthenticationProviderKind.Keycloak;
    }

    private async Task<string?> GetSettingValueAsync(string key)
    {
        var setting = await systemSettingRepository.GetByKey(key);
        return string.IsNullOrWhiteSpace(setting?.Value)
            ? null
            : setting.Value.Trim('"');
    }

    private static string? HostFromValue(string? value)
    {
        var normalized = NormalizeHost(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var firstUrl = normalized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return NormalizeHost(firstUrl);
    }

    private static string? NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return uri.Host.Trim().ToLowerInvariant();
        }

        return candidate
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim('/')
            .ToLowerInvariant();
    }

    private static void AddBlocking(OnboardingPreflightDto result, string code, string name, string status, string message, string? detail = null)
    {
        result.AddBlockingCheck(new OnboardingPreflightCheckDto
        {
            Code = code,
            Name = name,
            Severity = OnboardingPreflightCheckSeverity.Blocking,
            Status = status,
            Message = message,
            Detail = detail
        });
    }

    private static void AddWarning(OnboardingPreflightDto result, string code, string name, string message, string? detail = null)
    {
        result.AddWarningCheck(new OnboardingPreflightCheckDto
        {
            Code = code,
            Name = name,
            Severity = OnboardingPreflightCheckSeverity.Warning,
            Status = OnboardingPreflightCheckStatus.Warning,
            Message = message,
            Detail = detail
        });
    }
}
