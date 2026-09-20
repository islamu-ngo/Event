using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Domain.Constants;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public sealed class GetInstanceOnboardingJourneyQueryHandler(
    IQueryHandler<GetInstanceOnboardingStatusQuery, InstanceOnboardingStatusDto> statusQuery,
    IQueryHandler<GetOnboardingPreflightQuery, OnboardingPreflightDto> preflightQuery,
    IQueryHandler<GetInstanceOperatorIdentityQuery, InstanceOperatorIdentityDocumentDto> identityQuery,
    IAuthProviderConfigurationService authentication,
    IAuthorizationProviderConfigurationService authorization,
    ISystemSettingRepository settings,
    ILogger<GetInstanceOnboardingJourneyQueryHandler> logger)
    : IQueryHandler<GetInstanceOnboardingJourneyQuery, InstanceOnboardingJourneyDto>
{
    public async Task<InstanceOnboardingJourneyDto> QueryAsync(GetInstanceOnboardingJourneyQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var first = await ReadAsync(request, cancellationToken);
            if (first.State == "Failed") return first;
            var confirmed = await ReadAsync(request, cancellationToken);
            return first.Generation == confirmed.Generation ? confirmed : new() { ReasonCode = "snapshot_changed" };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning("Onboarding journey unavailable. FailureType={FailureType}", exception.GetType().Name);
            return new();
        }
    }

    private async Task<InstanceOnboardingJourneyDto> ReadAsync(GetInstanceOnboardingJourneyQuery request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bootstrap = await statusQuery.QueryAsync(new() { SetupPrincipal = request.SetupPrincipal }, cancellationToken);
        var auth = await authentication.ReadConfigurationAsync();
        var authz = await authorization.ReadConfigurationAsync();
        var profile = new SelfHostOnboardingProfileDto
        {
            SiteName = await ReadSettingAsync(GovernanceSettingKeys.Branding.DisplayName) ?? string.Empty,
            SupportEmail = await ReadSettingAsync(GovernanceSettingKeys.Branding.SupportEmail),
            CanonicalUrl = await ReadSettingAsync(GovernanceSettingKeys.Domains.InstanceBaseDomain),
            Locale = await ReadSettingAsync(GovernanceSettingKeys.Localization.DefaultLanguage) ?? "en"
        };
        var preflight = await preflightQuery.QueryAsync(new(), cancellationToken);
        var identity = await identityQuery.QueryAsync(new(), cancellationToken);
        return Project(bootstrap, auth, authz, profile, preflight, identity);
    }

    private async Task<string?> ReadSettingAsync(string key)
    {
        var setting = await settings.GetByKey(key);
        return string.IsNullOrWhiteSpace(setting?.Value) ? null : JsonSerializer.Deserialize<string>(setting.Value);
    }

    public static InstanceOnboardingJourneyDto Project(
        InstanceOnboardingStatusDto? bootstrap, AuthProviderConfigurationDto? authentication,
        AuthorizationProviderConfigurationDto? authorization, SelfHostOnboardingProfileDto? profile,
        OnboardingPreflightDto? preflight, InstanceOperatorIdentityDocumentDto? identity = null)
    {
        if (bootstrap is null || authentication is null || authorization is null || profile is null || preflight is null)
            return new();

        var authProvider = (Explore.Domain.Enums.AuthenticationProviderKind)authentication.PrimaryProviderId;
        var providerName = authProvider.ToString();
        var bootstrapValid = bootstrap.State is "InteractivePending" or "ConfiguredAdministratorPending" or "Completed";
        var authorizationState = authorization.AuthorizationProviderBootstrapStatus;
        if (!bootstrapValid || bootstrap.IsCompleted != (bootstrap.State == "Completed")
            || bootstrap.SelectedDeploymentMode is not ("SingleTenant" or "MultiTenant")
            || bootstrap.SelectedDeploymentMode != preflight.DeploymentMode
            || !Enum.IsDefined(authProvider)
            || !string.Equals(providerName, authentication.PrimaryProviderCode, StringComparison.OrdinalIgnoreCase)
            || (!bootstrap.IsCompleted && bootstrap.Provider != providerName)
            || authorization.Provider is not ("local" or "cerbos")
            || (authorization.AuthorizationProviderManagedByDeployment
                && (authorizationState is not ("ready" or "pending" or "failed")
                    || authorization.AuthorizationProviderConfigured != (authorizationState == "ready"))))
            return new() { ReasonCode = "source_contradiction" };

        var authReady = authProvider switch
        {
            Explore.Domain.Enums.AuthenticationProviderKind.Local => true,
            Explore.Domain.Enums.AuthenticationProviderKind.Keycloak => !string.IsNullOrWhiteSpace(authentication.KeycloakAuthority)
                && !string.IsNullOrWhiteSpace(authentication.KeycloakClientId),
            Explore.Domain.Enums.AuthenticationProviderKind.Atproto => authentication.AtprotoLoginEnabled
                && !string.IsNullOrWhiteSpace(authentication.AtprotoPublicUrl),
            Explore.Domain.Enums.AuthenticationProviderKind.Google => authentication.GoogleSsoEnabled
                && !string.IsNullOrWhiteSpace(authentication.GoogleClientId),
            _ => false
        };
        var auth = Readiness(providerName, authReady ? "Ready" : authentication.LockPrimaryProvider
            ? "DeploymentRestartRequired" : "ActionRequired", authentication.LockPrimaryProvider, "manage-authentication");
        var authz = Readiness(authorization.Provider,
            authorization.AuthorizationProviderConfigured ? "Ready"
                : authorizationState == "failed" ? "Failed"
                : authorization.AuthorizationProviderManagedByDeployment ? "DeploymentRestartRequired" : "ActionRequired",
            authorization.AuthorizationProviderManagedByDeployment, "manage-authorization");
        var projectedPreflight = preflight with
        {
            BlockingChecks = preflight.BlockingChecks.Select(check => check.Code == "auth_config"
                ? check with { Status = auth.State == "Ready" ? "Pass" : "Fail", ReasonCode = auth.ReasonCode,
                    RemediationAuthority = auth.RemediationAuthority, RestartRequired = auth.RestartRequired,
                    ActionRelation = auth.ActionRelation }
                : check).Append(new OnboardingPreflightCheckDto
                {
                    Code = "authorization_config", Name = "Authorization configuration",
                    Status = authz.State == "Ready" ? "Pass" : "Fail", ReasonCode = authz.ReasonCode,
                    RemediationAuthority = authz.RemediationAuthority, RestartRequired = authz.RestartRequired,
                    ActionRelation = authz.ActionRelation, Message = "Selected authorization provider readiness."
                }).ToArray()
        };
        var snapshot = new InstanceOnboardingJourneyDto
        {
            State = "Available", ReasonCode = "snapshot_available", Bootstrap = bootstrap,
            Authentication = auth, Authorization = authz, Profile = profile,
            Preflight = projectedPreflight, OperatorIdentity = identity
        };
        return snapshot with { Generation = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot))) };
    }

    private static OnboardingProviderReadinessDto Readiness(string provider, string state, bool deploymentManaged, string relation) => new()
    {
        Provider = provider, State = state,
        RemediationAuthority = deploymentManaged ? "Deployment" : "SetupOperator",
        RestartRequired = state == "DeploymentRestartRequired", ActionRelation = relation,
        ReasonCode = state switch
        {
            "Ready" => "provider_ready", "Failed" => "provider_failed",
            "DeploymentRestartRequired" => "deployment_restart_required", _ => "provider_configuration_required"
        }
    };
}
