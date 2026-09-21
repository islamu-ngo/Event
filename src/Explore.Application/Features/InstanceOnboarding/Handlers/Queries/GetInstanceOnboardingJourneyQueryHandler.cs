using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public sealed class GetInstanceOnboardingJourneyQueryHandler(
    IQueryHandler<GetInstanceOnboardingStatusQuery, InstanceOnboardingStatusDto> statusQuery,
    IQueryHandler<GetOnboardingPreflightQuery, OnboardingPreflightDto> preflightQuery,
    IQueryHandler<GetInstanceOperatorIdentityQuery, InstanceOperatorIdentityDocumentDto> identityQuery,
    IAuthProviderConfigurationService authentication,
    IAuthorizationProviderConfigurationService authorization,
    IInstanceOnboardingGenerationReader generationReader,
    ILogger<GetInstanceOnboardingJourneyQueryHandler> logger)
    : IQueryHandler<GetInstanceOnboardingJourneyQuery, InstanceOnboardingJourneyDto>
{
    private const string Ready = "Ready";
    private const string DeploymentRestartRequired = "DeploymentRestartRequired";

    public async Task<InstanceOnboardingJourneyDto> QueryAsync(GetInstanceOnboardingJourneyQuery request, CancellationToken cancellationToken = default)
    {
        try
        {
            var durable = await generationReader.ReadSnapshotAsync(cancellationToken);
            var snapshot = await ReadAsync(request, durable.Profile, cancellationToken);
            if (snapshot.State == "Failed") return snapshot;
            var confirmed = await generationReader.ReadCurrentAsync(cancellationToken);
            return durable.Generation == confirmed
                ? snapshot with { Generation = confirmed }
                : new() { ReasonCode = "snapshot_changed" };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning("Onboarding journey unavailable. FailureType={FailureType}", exception.GetType().Name);
            return new();
        }
    }

    private async Task<InstanceOnboardingJourneyDto> ReadAsync(GetInstanceOnboardingJourneyQuery request,
        SelfHostOnboardingProfileDto profile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bootstrap = await statusQuery.QueryAsync(new() { SetupPrincipal = request.SetupPrincipal }, cancellationToken);
        var auth = await authentication.ReadConfigurationAsync();
        var authz = await authorization.ReadConfigurationAsync();
        var preflight = await preflightQuery.QueryAsync(new(), cancellationToken);
        var identity = await identityQuery.QueryAsync(new(), cancellationToken);
        return Project(bootstrap, auth, authz, profile, preflight, identity);
    }

    public static InstanceOnboardingJourneyDto Project(
        InstanceOnboardingStatusDto? bootstrap, AuthProviderConfigurationDto? authentication,
        AuthorizationProviderConfigurationDto? authorization, SelfHostOnboardingProfileDto? profile,
        OnboardingPreflightDto? preflight, InstanceOperatorIdentityDocumentDto? identity = null)
    {
        if (bootstrap is null || authentication is null || authorization is null || profile is null || preflight is null)
            return new();

        var authProvider = (AuthenticationProviderKind)authentication.PrimaryProviderId;
        var providerName = authProvider.ToString();
        if (!IsConsistent(bootstrap, authentication, authorization, preflight, authProvider))
            return new() { ReasonCode = "source_contradiction" };

        var auth = Readiness(providerName, AuthenticationState(authentication, authProvider),
            authentication.LockPrimaryProvider, "manage-authentication");
        var authz = Readiness(authorization.Provider, AuthorizationState(authorization),
            authorization.AuthorizationProviderManagedByDeployment, "manage-authorization");
        var projectedPreflight = preflight with
        {
            BlockingChecks = preflight.BlockingChecks.Select(check => ProjectAuthenticationCheck(check, auth))
                .Append(new OnboardingPreflightCheckDto
                {
                    Code = "authorization_config",
                    Name = "Authorization configuration",
                    Status = authz.State == Ready ? "Pass" : "Fail",
                    ReasonCode = authz.ReasonCode,
                    RemediationAuthority = authz.RemediationAuthority,
                    RestartRequired = authz.RestartRequired,
                    ActionRelation = authz.ActionRelation,
                    Message = "Selected authorization provider readiness."
                }).ToArray()
        };
        return new InstanceOnboardingJourneyDto
        {
            State = "Available",
            ReasonCode = "snapshot_available",
            Bootstrap = bootstrap,
            Authentication = auth,
            Authorization = authz,
            Profile = profile,
            Preflight = projectedPreflight,
            OperatorIdentity = identity
        };
    }

    private static OnboardingPreflightCheckDto ProjectAuthenticationCheck(
        OnboardingPreflightCheckDto check, OnboardingProviderReadinessDto authentication)
    {
        if (check.Code != "auth_config") return check;
        return check with
        {
            Status = authentication.State == Ready ? "Pass" : "Fail",
            ReasonCode = authentication.ReasonCode,
            RemediationAuthority = authentication.RemediationAuthority,
            RestartRequired = authentication.RestartRequired,
            ActionRelation = authentication.ActionRelation
        };
    }

    private static bool IsConsistent(InstanceOnboardingStatusDto bootstrap,
        AuthProviderConfigurationDto authentication, AuthorizationProviderConfigurationDto authorization,
        OnboardingPreflightDto preflight, AuthenticationProviderKind provider)
    {
        var providerName = provider.ToString();
        return bootstrap.State is "InteractivePending" or "ConfiguredAdministratorPending" or "Completed"
            && bootstrap.IsCompleted == (bootstrap.State == "Completed")
            && bootstrap.SelectedDeploymentMode is "SingleTenant" or "MultiTenant"
            && bootstrap.SelectedDeploymentMode == preflight.DeploymentMode
            && Enum.IsDefined(provider)
            && string.Equals(providerName, authentication.PrimaryProviderCode, StringComparison.OrdinalIgnoreCase)
            && (bootstrap.IsCompleted || bootstrap.Provider == providerName)
            && IsAuthorizationConsistent(authorization);
    }

    private static bool IsAuthorizationConsistent(AuthorizationProviderConfigurationDto authorization)
    {
        var state = authorization.AuthorizationProviderBootstrapStatus;
        return authorization.Provider is "local" or "cerbos"
            && (!authorization.AuthorizationProviderManagedByDeployment
                || state is "ready" or "pending" or "failed"
                    && authorization.AuthorizationProviderConfigured == (state == "ready"));
    }

    private static string AuthenticationState(AuthProviderConfigurationDto authentication, AuthenticationProviderKind provider)
    {
        var ready = provider switch
        {
            AuthenticationProviderKind.Local => true,
            AuthenticationProviderKind.Keycloak => !string.IsNullOrWhiteSpace(authentication.KeycloakAuthority)
                && !string.IsNullOrWhiteSpace(authentication.KeycloakClientId),
            AuthenticationProviderKind.Atproto => authentication.AtprotoLoginEnabled
                && !string.IsNullOrWhiteSpace(authentication.AtprotoPublicUrl),
            AuthenticationProviderKind.Google => authentication.GoogleSsoEnabled
                && !string.IsNullOrWhiteSpace(authentication.GoogleClientId),
            _ => false
        };
        if (ready) return Ready;
        return ConfigurationRequiredState(authentication.LockPrimaryProvider);
    }

    private static string AuthorizationState(AuthorizationProviderConfigurationDto authorization)
    {
        if (authorization.AuthorizationProviderConfigured) return Ready;
        if (authorization.AuthorizationProviderBootstrapStatus == "failed") return "Failed";
        return ConfigurationRequiredState(authorization.AuthorizationProviderManagedByDeployment);
    }

    private static string ConfigurationRequiredState(bool deploymentManaged) =>
        deploymentManaged ? DeploymentRestartRequired : "ActionRequired";

    private static OnboardingProviderReadinessDto Readiness(string provider, string state, bool deploymentManaged, string relation) => new()
    {
        Provider = provider,
        State = state,
        RemediationAuthority = deploymentManaged ? "Deployment" : "SetupOperator",
        RestartRequired = state == DeploymentRestartRequired,
        ActionRelation = relation,
        ReasonCode = state switch
        {
            Ready => "provider_ready",
            "Failed" => "provider_failed",
            DeploymentRestartRequired => "deployment_restart_required",
            _ => "provider_configuration_required"
        }
    };
}
