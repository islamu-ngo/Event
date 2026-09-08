// ABOUTME: Client service for instance onboarding and governance through the generated API client.
// ABOUTME: Exposes generated request and response DTOs for all onboarding settings.

using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Helpers;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Services;

public interface IInstanceOnboardingService
{
    Task<InstanceOnboardingStartupStatus> GetStartupStatusAsync(CancellationToken cancellationToken = default);
    Task<SystemOnboardingStatusDto?> GetSystemOnboardingStatusAsync();
    Task<OnboardingPreflightDto?> GetOnboardingPreflightAsync();
    Task<InstanceOnboardingStatusDto?> GetStatusAsync();
    Task<SetupSecretValidationResultDto> ValidateSecretAsync(string secret);
    Task<BaseCommandResponseOfGuid> CompleteAsync(CompleteInstanceOnboardingRequest completion);
    Task<BaseCommandResponseOfGuid> CompleteLocalAsync(CompleteLocalInstanceOnboardingRequestDto completion, CancellationToken cancellationToken = default);

    Task<DeploymentModeDto> GetDeploymentModeAsync();
    Task<ModuleSettingsDto> GetModuleSettingsAsync();
    Task<EventPolicyDto> GetEventPolicyAsync();
    Task<OrganizationPolicyDto> GetOrganizationPolicyAsync();
    Task<BrandingSettingsDto> GetBrandingSettingsAsync();
    Task<DomainSettingsDto> GetDomainSettingsAsync();
    Task<TenantDelegationSettingsDto> GetTenantDelegationAsync();
    Task<RenderPolicySettingsDto> GetRenderPolicyAsync();
    Task<McpGovernanceSettingsDto> GetMcpGovernanceSettingsAsync();
    Task<AiAssistantGovernanceSettingsDto> GetAiAssistantGovernanceSettingsAsync();

    Task<BaseCommandResponseOfGuid> UpdateDeploymentModeAsync(string deploymentMode);
    Task<BaseCommandResponseOfGuid> UpdateModuleSettingsAsync(ModuleSettingsDto settings);
    Task<BaseCommandResponseOfGuid> UpdateEventPolicyAsync(EventPolicyDto settings);
    Task<BaseCommandResponseOfGuid> UpdateOrganizationPolicyAsync(OrganizationPolicyDto settings);
    Task<BaseCommandResponseOfGuid> UpdateBrandingSettingsAsync(BrandingSettingsDto settings);
    Task<BaseCommandResponseOfGuid> UpdateDomainSettingsAsync(DomainSettingsDto settings);
    Task<BaseCommandResponseOfGuid> UpdateTenantDelegationAsync(TenantDelegationSettingsDto settings);
    Task<BaseCommandResponseOfGuid> UpdateRenderPolicyAsync(RenderPolicySettingsDto settings);
    Task<BaseCommandResponseOfGuid> UpdateMcpGovernanceSettingsAsync(McpGovernanceSettingsDto settings);
    Task<BaseCommandResponseOfGuid> UpdateAiAssistantGovernanceSettingsAsync(AiAssistantGovernanceSettingsDto settings);
    Task<BaseCommandResponseOfGuid> UpdateAiAssistantProviderConfigurationAsync(AiAssistantProviderConfigurationWriteDto settings);

    Task<HalResourceOfInstanceStorageSettingsDto> GetStorageSettingsAsync();
    Task<BaseCommandResponseOfGuid> UpdateStorageSettingsAsync(HalResourceOfInstanceStorageSettingsDto settings);
    Task<InstanceStorageProviderStatusDto> TestStorageConnectionAsync();
    Task<InstanceStorageUsageDto?> RecalculateStorageUsageAsync();
    Task<HalResourceOfInstanceSmtpSettingsDto> GetSmtpSettingsAsync();
    Task<BaseCommandResponseOfGuid> UpdateSmtpSettingsAsync(InstanceSmtpConfigurationWriteDto settings);
    Task<SmtpConnectionTestResultDto> TestSmtpConnectionAsync();
    Task<int> GetActiveTenantCountAsync();

    Task<AuthProviderConfigurationDto> GetAuthProviderConfigurationAsync();
    Task<AuthProviderConfigurationDto> GetAuthProviderConfigurationAsAdminAsync();
    Task<BaseCommandResponseOfGuid> BootstrapKeycloakRealmAsync(KeycloakBootstrapRequestDto request);
    Task<KeycloakRealmDoctorResultDto> RunKeycloakRealmDoctorAsync(KeycloakRealmDoctorRequestDto request);
    Task<KeycloakRealmSyncPlanDto> PreviewKeycloakRealmSyncAsync(KeycloakRealmSyncPreviewRequestDto request);
    Task<KeycloakRealmSyncPlanDto> ApplyKeycloakRealmSyncAsync(KeycloakRealmSyncApplyRequestDto request);
    Task<KeycloakClientSecretRotationResultDto> RotateKeycloakClientSecretAsync(KeycloakClientSecretRotationRequestDto request);
    Task<BaseCommandResponseOfGuid> UpdateAuthProviderConfigurationAsAdminAsync(AuthProviderConfigurationDto config);
    Task<bool> IsAuthProviderConfiguredAsync();
    Task<bool?> GetAuthProviderConfiguredStateAsync();
    Task RefreshAuthSchemesAsync();
    Task<bool> RefreshAuthSessionAsync();

    Task<AuthorizationProviderConfigurationDto> GetAuthorizationProviderConfigurationAsync();
    Task<AuthorizationProviderConfigurationDto> GetAuthorizationProviderConfigurationAsAdminAsync();
    Task<BaseCommandResponseOfGuid> UpdateAuthorizationProviderConfigurationAsAdminAsync(AuthorizationProviderConfigurationDto config);
    Task<BaseCommandResponseOfGuid> SyncAuthorizationPolicyPackageAsync(AuthorizationPolicyPackageSyncRequestDto? request = null);
    Task<BaseCommandResponseOfGuid> SyncAuthorizationPolicyPackageAsAdminAsync(AuthorizationPolicyPackageSyncRequestDto? request = null);
    Task<BaseCommandResponseOfGuid> VerifyCerbosEndpointAsync(string grpcEndpoint);
    Task<bool> IsAuthorizationProviderConfiguredAsync();
    Task<bool?> GetAuthorizationProviderConfiguredStateAsync();
    Task<bool> ShouldSkipAuthorizationProviderStepAsync();

    Task<AnalyticsGovernanceSettingsDto> GetAnalyticsGovernanceSettingsAsync();
    Task<BaseCommandResponseOfGuid> UpdateAnalyticsGovernanceSettingsAsync(AnalyticsGovernanceSettingsDto settings);
    Task<FooterGovernanceSettingsDto> GetFooterGovernanceSettingsAsync();
    Task<BaseCommandResponseOfGuid> UpdateFooterGovernanceSettingsAsync(FooterGovernanceSettingsDto settings);
}

public sealed class InstanceOnboardingService(
    IInstanceAuthenticationSettingsClient authenticationClient,
    IInstanceAuthorizationSettingsClient authorizationClient,
    IInstanceGovernanceSettingsClient governanceClient,
    IInstanceMessagingSettingsClient messagingClient,
    IInstanceOnboardingClient onboardingClient,
    IInstancePresentationSettingsClient presentationClient,
    IInstanceStorageSettingsClient storageClient,
    ISystemClient systemClient,
    ITenantClient tenantClient,
    IBffAuthApi bffAuthApi,
    ILogger<InstanceOnboardingService> logger,
    NavigationManager navigation) : IInstanceOnboardingService
{
    public Task<SystemOnboardingStatusDto?> GetSystemOnboardingStatusAsync() =>
        GetOptionalAsync(ct => systemClient.GetSystemOnboardingStatusAsync(cancellationToken: ct), "system onboarding status");

    public async Task<InstanceOnboardingStartupStatus> GetStartupStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var resource = await GetOptionalAsync(
            ct => onboardingClient.GetInstanceOnboardingStatusAsync(cancellationToken: ct),
            "instance onboarding startup status",
            cancellationToken);
        return InstanceOnboardingStartupStatusAdapter.FromGenerated(resource.ToDto());
    }

    public Task<OnboardingPreflightDto?> GetOnboardingPreflightAsync() =>
        GetOptionalAsync(ct => systemClient.GetSystemOnboardingPreflightAsync(cancellationToken: ct), "onboarding preflight");

    public async Task<InstanceOnboardingStatusDto?> GetStatusAsync()
    {
        var resource = await GetOptionalAsync(
            ct => onboardingClient.GetInstanceOnboardingStatusAsync(cancellationToken: ct),
            "instance onboarding status");
        return resource.ToDto();
    }

    public async Task<SetupSecretValidationResultDto> ValidateSecretAsync(string secret)
    {
        try
        {
            return await onboardingClient.ValidateInstanceSetupSecretAsync(
                new ValidateSetupSecretRequest { Secret = secret },
                cancellationToken: CancellationToken.None);
        }
        catch (ApiException ex) when (ex.StatusCode == 429)
        {
            logger.LogWarning("Setup secret validation rate-limited (429).");
            return new SetupSecretValidationResultDto { Valid = false };
        }
        catch (ApiException ex) when (ex.StatusCode == 410)
        {
            return new SetupSecretValidationResultDto { Valid = false };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to validate setup secret.");
            return new SetupSecretValidationResultDto { Valid = false };
        }
    }

    public async Task<BaseCommandResponseOfGuid> CompleteAsync(CompleteInstanceOnboardingRequest completion)
    {
        try
        {
            return await onboardingClient.CompleteInstanceOnboardingAsync(completion, cancellationToken: CancellationToken.None);
        }
        catch (ApiException<ValidationProblemDetails> ex)
        {
            logger.LogError(ex, "Failed to complete onboarding due to validation errors. HTTP Status={StatusCode}", ex.StatusCode);
            return MapValidationProblemDetails(ex.Result);
        }
        catch (ApiException ex) when (ex.StatusCode == 401)
        {
            if (await RefreshAuthSessionAsync())
            {
                return await SendCommandAsync(
                    ct => onboardingClient.CompleteInstanceOnboardingAsync(completion, cancellationToken: ct));
            }

            return MapCommandApiException(ex);
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to complete onboarding.");
            return MapCommandApiException(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to complete onboarding.");
            return FailedCommandResponse("Request failed.");
        }
    }

    public async Task<BaseCommandResponseOfGuid> CompleteLocalAsync(
        CompleteLocalInstanceOnboardingRequestDto completion, CancellationToken cancellationToken = default)
    {
        try
        {
            return await onboardingClient.CompleteLocalInstanceOnboardingAsync(completion, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException exception)
        {
            logger.LogWarning("Local setup completion failed. StatusCode={StatusCode}", exception.StatusCode);
            return FailedCommandResponse("Local setup did not complete. Refresh setup status and retry with the same account details.");
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Local setup completion transport failed.");
            return FailedCommandResponse("Local setup could not be confirmed. Refresh setup status before retrying.");
        }
        finally
        {
            completion.TemporaryPassword = string.Empty;
        }
    }

    public Task<DeploymentModeDto> GetDeploymentModeAsync() =>
        GetSettingsAsync(ct => presentationClient.GetInstanceDeploymentModeAsync(cancellationToken: ct), () => new());

    public Task<ModuleSettingsDto> GetModuleSettingsAsync() =>
        GetSettingsAsync(ct => governanceClient.GetInstanceModuleSettingsAsync(cancellationToken: ct), () => new());

    public Task<EventPolicyDto> GetEventPolicyAsync() =>
        GetSettingsAsync(ct => governanceClient.GetInstanceEventPolicyAsync(cancellationToken: ct), () => new());

    public Task<OrganizationPolicyDto> GetOrganizationPolicyAsync() =>
        GetSettingsAsync(ct => governanceClient.GetInstanceOrganizationPolicyAsync(cancellationToken: ct), () => new());

    public Task<BrandingSettingsDto> GetBrandingSettingsAsync() =>
        GetSettingsAsync(ct => presentationClient.GetInstanceBrandingSettingsAsync(cancellationToken: ct), () => new());

    public Task<DomainSettingsDto> GetDomainSettingsAsync() =>
        GetSettingsAsync(ct => presentationClient.GetInstanceDomainSettingsAsync(cancellationToken: ct), () => new());

    public Task<TenantDelegationSettingsDto> GetTenantDelegationAsync() =>
        GetSettingsAsync(ct => governanceClient.GetInstanceTenantDelegationSettingsAsync(cancellationToken: ct), () => new());

    public Task<RenderPolicySettingsDto> GetRenderPolicyAsync() =>
        GetSettingsAsync(ct => presentationClient.GetInstanceRenderPolicySettingsAsync(cancellationToken: ct), () => new());

    public Task<McpGovernanceSettingsDto> GetMcpGovernanceSettingsAsync() =>
        GetSettingsAsync(ct => governanceClient.GetInstanceMcpGovernanceSettingsAsync(cancellationToken: ct), () => new());

    public Task<AiAssistantGovernanceSettingsDto> GetAiAssistantGovernanceSettingsAsync() =>
        GetSettingsAsync(ct => governanceClient.GetInstanceAiAssistantGovernanceSettingsAsync(cancellationToken: ct), () => new());

    public Task<BaseCommandResponseOfGuid> UpdateDeploymentModeAsync(string deploymentMode) =>
        SendCommandAsync(ct => presentationClient.UpdateInstanceDeploymentModeAsync(
            new UpdateDeploymentModeRequest { DeploymentMode = deploymentMode }, cancellationToken: ct));

    public Task<BaseCommandResponseOfGuid> UpdateModuleSettingsAsync(ModuleSettingsDto settings) =>
        SendCommandAsync(ct => governanceClient.UpdateInstanceModuleSettingsAsync(ToPatch(settings), cancellationToken: ct));

    public Task<BaseCommandResponseOfGuid> UpdateEventPolicyAsync(EventPolicyDto settings) =>
        SendCommandAsync(ct => governanceClient.UpdateInstanceEventPolicyAsync(ToPatch(settings), cancellationToken: ct));

    public Task<BaseCommandResponseOfGuid> UpdateOrganizationPolicyAsync(OrganizationPolicyDto settings) =>
        SendCommandAsync(ct => governanceClient.UpdateInstanceOrganizationPolicyAsync(ToPatch(settings), cancellationToken: ct));

    public Task<BaseCommandResponseOfGuid> UpdateBrandingSettingsAsync(BrandingSettingsDto settings) =>
        SendCommandAsync(ct => presentationClient.UpdateInstanceBrandingSettingsAsync(ToPatch(settings), cancellationToken: ct));

    public Task<BaseCommandResponseOfGuid> UpdateDomainSettingsAsync(DomainSettingsDto settings) =>
        SendCommandAsync(ct => presentationClient.UpdateInstanceDomainSettingsAsync(ToPatch(settings), cancellationToken: ct));

    public Task<BaseCommandResponseOfGuid> UpdateTenantDelegationAsync(TenantDelegationSettingsDto settings) =>
        SendCommandAsync(ct => governanceClient.UpdateInstanceTenantDelegationSettingsAsync(ToPatch(settings), cancellationToken: ct));

    public Task<BaseCommandResponseOfGuid> UpdateRenderPolicyAsync(RenderPolicySettingsDto settings) =>
        SendCommandAsync(ct => presentationClient.UpdateInstanceRenderPolicySettingsAsync(ToPatch(settings), cancellationToken: ct));

    public Task<BaseCommandResponseOfGuid> UpdateMcpGovernanceSettingsAsync(McpGovernanceSettingsDto settings) =>
        SendCommandAsync(ct => governanceClient.UpdateInstanceMcpGovernanceSettingsAsync(ToPatch(settings), cancellationToken: ct));

    public Task<BaseCommandResponseOfGuid> UpdateAiAssistantGovernanceSettingsAsync(AiAssistantGovernanceSettingsDto settings) =>
        SendCommandAsync(ct => governanceClient.UpdateInstanceAiAssistantGovernanceSettingsAsync(ToPatch(settings), cancellationToken: ct));

    public Task<BaseCommandResponseOfGuid> UpdateAiAssistantProviderConfigurationAsync(AiAssistantProviderConfigurationWriteDto settings) =>
        SendCommandAsync(ct => governanceClient.UpdateInstanceAiAssistantGovernanceSettingsAsync(new PatchAiAssistantGovernanceSettingsDto
        {
            ProviderConfiguration = new OptionalUpdateOfAiAssistantProviderConfigurationWriteDto
            {
                HasValue = true,
                Value = settings
            }
        }, cancellationToken: ct));

    public async Task<HalResourceOfInstanceStorageSettingsDto> GetStorageSettingsAsync()
    {
        try
        {
            return (await storageClient.GetInstanceStorageSettingsAsync(
                cancellationToken: CancellationToken.None)).InitializeForEditing();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch storage settings.");
            return new HalResourceOfInstanceStorageSettingsDto().InitializeForEditing();
        }
    }

    public Task<BaseCommandResponseOfGuid> UpdateStorageSettingsAsync(HalResourceOfInstanceStorageSettingsDto settings) =>
        settings.HasLink("edit")
            ? SendCommandAsync(ct => storageClient.UpdateInstanceStorageSettingsAsync(settings.ToUpdateRequest(), cancellationToken: ct))
            : Task.FromResult(FailedCommandResponse("The API did not expose a storage settings edit affordance."));

    public async Task<InstanceStorageProviderStatusDto> TestStorageConnectionAsync()
    {
        try
        {
            return await storageClient.TestInstanceStorageConnectionAsync(cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to test storage connection.");
            return new InstanceStorageProviderStatusDto
            {
                IsAvailable = false,
                Message = "Storage provider test failed."
            };
        }
    }

    public async Task<InstanceStorageUsageDto?> RecalculateStorageUsageAsync()
    {
        try
        {
            return await storageClient.RecalculateInstanceStorageUsageAsync(cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to recalculate storage usage.");
            return null;
        }
    }

    public Task<HalResourceOfInstanceSmtpSettingsDto> GetSmtpSettingsAsync() =>
        GetSettingsAsync(ct => messagingClient.GetInstanceSmtpSettingsAsync(cancellationToken: ct), () => new());

    public Task<BaseCommandResponseOfGuid> UpdateSmtpSettingsAsync(InstanceSmtpConfigurationWriteDto settings) =>
        SendCommandAsync(ct => messagingClient.UpdateInstanceSmtpSettingsAsync(new PatchInstanceSmtpSettingsDto
        {
            Configuration = new OptionalUpdateOfInstanceSmtpConfigurationWriteDto
            {
                HasValue = true,
                Value = settings
            }
        }, cancellationToken: ct));

    public async Task<SmtpConnectionTestResultDto> TestSmtpConnectionAsync()
    {
        try
        {
            return await messagingClient.TestInstanceSmtpConnectionAsync(cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to test SMTP connection.");
            return new SmtpConnectionTestResultDto { Success = false, Message = "SMTP connection test failed." };
        }
    }

    public async Task<int> GetActiveTenantCountAsync()
    {
        try
        {
            return await tenantClient.GetActiveTenantCountAsync(cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve active tenant count.");
            return 0;
        }
    }

    public Task<AuthProviderConfigurationDto> GetAuthProviderConfigurationAsync() =>
        GetSettingsAsync(async ct => (await onboardingClient.GetInstanceOnboardingAuthProviderConfigurationAsync(cancellationToken: ct)).ToDto() ?? new(), () => new());

    public Task<AuthProviderConfigurationDto> GetAuthProviderConfigurationAsAdminAsync() =>
        GetSettingsAsync(ct => authenticationClient.GetInstanceAuthProviderConfigurationAsync(cancellationToken: ct), () => new());

    public async Task<BaseCommandResponseOfGuid> BootstrapKeycloakRealmAsync(KeycloakBootstrapRequestDto request)
    {
        ApplyKeycloakBootstrapBrowserDefaults(request);
        var result = await SendCommandAsync(
            ct => onboardingClient.BootstrapInstanceOnboardingKeycloakRealmAsync(request, cancellationToken: ct));
        if (result.Success == true)
        {
            await RefreshAuthSchemesAsync();
        }

        return result;
    }

    public Task<KeycloakRealmDoctorResultDto> RunKeycloakRealmDoctorAsync(KeycloakRealmDoctorRequestDto request) =>
        GetSettingsAsync(
            ct => authenticationClient.RunInstanceKeycloakRealmDoctorAsync(request, cancellationToken: ct),
            () => BlockedDoctor("Keycloak diagnostics failed. Check admin access and retry."));

    public Task<KeycloakRealmSyncPlanDto> PreviewKeycloakRealmSyncAsync(KeycloakRealmSyncPreviewRequestDto request) =>
        GetSettingsAsync(
            ct => authenticationClient.PreviewInstanceKeycloakRealmSyncAsync(request, cancellationToken: ct),
            () => BlockedPlan("Keycloak sync preview failed. Check admin access and retry."));

    public Task<KeycloakRealmSyncPlanDto> ApplyKeycloakRealmSyncAsync(KeycloakRealmSyncApplyRequestDto request) =>
        GetSettingsAsync(
            ct => authenticationClient.ApplyInstanceKeycloakRealmSyncAsync(request, cancellationToken: ct),
            () => BlockedPlan("Keycloak sync apply failed. Check admin access and retry."));

    public Task<KeycloakClientSecretRotationResultDto> RotateKeycloakClientSecretAsync(KeycloakClientSecretRotationRequestDto request) =>
        GetSettingsAsync(
            ct => authenticationClient.RotateInstanceKeycloakClientSecretAsync(request, cancellationToken: ct),
            () => BlockedRotation("Keycloak client-secret rotation failed. Check admin access and retry."));

    public Task<BaseCommandResponseOfGuid> UpdateAuthProviderConfigurationAsAdminAsync(AuthProviderConfigurationDto config) =>
        SendCommandAsync(ct => authenticationClient.UpdateInstanceAuthProviderConfigurationAsync(ToPatch(config), cancellationToken: ct));

    public async Task<bool> IsAuthProviderConfiguredAsync() =>
        await GetAuthProviderConfiguredStateAsync() ?? false;

    public async Task<bool?> GetAuthProviderConfiguredStateAsync()
    {
        try
        {
            return (await authenticationClient.GetInstanceAuthProviderConfigurationStatusAsync(
                cancellationToken: CancellationToken.None)).Configured;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check auth provider configuration status.");
            return null;
        }
    }

    public Task<AuthorizationProviderConfigurationDto> GetAuthorizationProviderConfigurationAsync() =>
        onboardingClient.GetInstanceOnboardingAuthorizationProviderConfigurationInternalAsync(cancellationToken: CancellationToken.None);

    public Task<AuthorizationProviderConfigurationDto> GetAuthorizationProviderConfigurationAsAdminAsync() =>
        GetSettingsAsync(
            ct => authorizationClient.GetInstanceAuthorizationProviderConfigurationAsync(cancellationToken: ct),
            () => new());

    public Task<BaseCommandResponseOfGuid> UpdateAuthorizationProviderConfigurationAsAdminAsync(AuthorizationProviderConfigurationDto config) =>
        SendCommandAsync(ct => authorizationClient.UpdateInstanceAuthorizationProviderConfigurationAsync(ToPatch(config), cancellationToken: ct));

    public Task<BaseCommandResponseOfGuid> SyncAuthorizationPolicyPackageAsync(AuthorizationPolicyPackageSyncRequestDto? request = null) =>
        SendCommandAsync(ct => onboardingClient.SyncInstanceOnboardingAuthorizationPolicyPackageAsync(request ?? new(), cancellationToken: ct));

    public Task<BaseCommandResponseOfGuid> SyncAuthorizationPolicyPackageAsAdminAsync(AuthorizationPolicyPackageSyncRequestDto? request = null) =>
        SendCommandAsync(ct => authorizationClient.SyncInstanceAuthorizationPolicyPackageAsync(request ?? new(), cancellationToken: ct));

    public Task<BaseCommandResponseOfGuid> VerifyCerbosEndpointAsync(string grpcEndpoint) =>
        SendCommandAsync(ct => onboardingClient.VerifyInstanceOnboardingAuthorizationProviderEndpointAsync(
            body: new VerifyCerbosEndpointRequest { GrpcEndpoint = grpcEndpoint }, cancellationToken: ct));

    public async Task<bool> IsAuthorizationProviderConfiguredAsync() =>
        await GetAuthorizationProviderConfiguredStateAsync() ?? false;

    public async Task<bool> ShouldSkipAuthorizationProviderStepAsync()
    {
        // Use the lightweight, non-rate-limited status endpoint rather than the full
        // GetAuthorizationProviderConfigurationAsync() call, which hits the
        // SetupSecretPolicy-rate-limited /internal endpoint (5 req/60s per IP).
        // Both this method and the authz-provider page fire on the same render pass,
        // so using the full config endpoint here causes 429s.
        try
        {
            var status = await authorizationClient.GetInstanceAuthorizationProviderConfigurationStatusAsync(
                cancellationToken: CancellationToken.None);
            var deploymentFailed = string.Equals(
                status.AuthorizationProviderBootstrapStatus,
                "failed",
                StringComparison.OrdinalIgnoreCase);

            return status.Configured == true ||
                   (status.AuthorizationProviderManagedByDeployment == true && !deploymentFailed);
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Failed to resolve the authorization-provider setup destination. FailureType={FailureType}",
                ex.GetType().Name);
            return false;
        }
    }

    public async Task<bool?> GetAuthorizationProviderConfiguredStateAsync()
    {
        try
        {
            return (await authorizationClient.GetInstanceAuthorizationProviderConfigurationStatusAsync(
                cancellationToken: CancellationToken.None)).Configured;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check authorization provider configuration status.");
            return null;
        }
    }

    public async Task RefreshAuthSchemesAsync()
    {
        try
        {
            var response = await bffAuthApi.RefreshSchemesAsync(CancellationToken.None);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to refresh auth schemes. Status: {StatusCode}", (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh auth schemes.");
        }
    }

    public async Task<bool> RefreshAuthSessionAsync()
    {
        try
        {
            var response = await bffAuthApi.RefreshSessionInternalAsync(CancellationToken.None);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            logger.LogWarning("Failed to refresh auth session. Status: {StatusCode}", (int)response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh auth session.");
            return false;
        }
    }

    public Task<AnalyticsGovernanceSettingsDto> GetAnalyticsGovernanceSettingsAsync() =>
        GetSettingsAsync(ct => governanceClient.GetInstanceAnalyticsGovernanceSettingsAsync(cancellationToken: ct), () => new());

    public Task<BaseCommandResponseOfGuid> UpdateAnalyticsGovernanceSettingsAsync(AnalyticsGovernanceSettingsDto settings) =>
        SendCommandAsync(ct => governanceClient.UpdateInstanceAnalyticsGovernanceSettingsAsync(ToPatch(settings), cancellationToken: ct));

    public Task<FooterGovernanceSettingsDto> GetFooterGovernanceSettingsAsync() =>
        GetSettingsAsync(ct => governanceClient.GetFooterGovernanceSettingsAsync(cancellationToken: ct), () => new());

    public Task<BaseCommandResponseOfGuid> UpdateFooterGovernanceSettingsAsync(FooterGovernanceSettingsDto settings) =>
        SendCommandAsync(ct => governanceClient.UpdateFooterGovernanceSettingsAsync(ToPatch(settings), cancellationToken: ct));

    private void ApplyKeycloakBootstrapBrowserDefaults(KeycloakBootstrapRequestDto request)
    {
        if (!Uri.TryCreate(navigation.BaseUri, UriKind.Absolute, out var baseUri))
        {
            return;
        }

        var origin = $"{baseUri.Scheme}://{baseUri.Host}{(baseUri.IsDefaultPort ? string.Empty : $":{baseUri.Port}")}";
        request.BlazorRedirectUris = MergeBootstrapValues(request.BlazorRedirectUris, $"{origin.TrimEnd('/')}/*");
        request.BlazorWebOrigins = MergeBootstrapValues(request.BlazorWebOrigins, "+");
    }

    private async Task<T?> GetOptionalAsync<T>(
        Func<CancellationToken, Task<T>> apiCall,
        string description,
        CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            return await apiCall(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch {Description}.", description);
            return null;
        }
    }

    private async Task<T> GetSettingsAsync<T>(Func<CancellationToken, Task<T>> apiCall, Func<T> fallback)
        where T : class
    {
        try
        {
            return await apiCall(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch {Type}.", typeof(T).Name);
            return fallback();
        }
    }

    private async Task<BaseCommandResponseOfGuid> SendCommandAsync(
        Func<CancellationToken, Task<BaseCommandResponseOfGuid>> apiCall)
    {
        try
        {
            return await apiCall(CancellationToken.None);
        }
        catch (ApiException<ValidationProblemDetails> ex)
        {
            // NSwag generates typed ApiException<ValidationProblemDetails> for 400 responses
            // declared as [ProducesResponseType(typeof(ValidationProblemDetails), 400)].
            // The base ApiException.Response string may be empty when the typed result is populated;
            // extract errors from the strongly-typed result instead.
            logger.LogError(ex, "Failed to send command. HTTP Status={StatusCode}", ex.StatusCode);
            return MapValidationProblemDetails(ex.Result);
        }
        catch (ApiException ex)
        {
            logger.LogError(ex, "Failed to send command.");
            return MapCommandApiException(ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send command.");
            return FailedCommandResponse(ex.Message);
        }
    }

    private static BaseCommandResponseOfGuid MapValidationProblemDetails(ValidationProblemDetails? details)
    {
        if (details is null)
        {
            return FailedCommandResponse("Request failed with validation errors.");
        }

        var result = FailedCommandResponse(details.Detail ?? details.Title ?? "Validation failed.");
        result.Message = details.Detail ?? details.Title ?? result.Message;

        if (details.Errors is { Count: > 0 })
        {
            var fieldErrors = details.Errors
                .Values
                .Where(msgs => msgs is { Count: > 0 })
                .SelectMany(msgs => msgs)
                .Where(msg => !string.IsNullOrWhiteSpace(msg))
                .ToArray();

            if (fieldErrors.Length > 0)
            {
                result.Errors = fieldErrors;
            }
        }

        return result;
    }

    private static BaseCommandResponseOfGuid MapCommandApiException(ApiException exception)
    {
        var result = FailedCommandResponse($"Failed with status {exception.StatusCode}.");
        if (string.IsNullOrWhiteSpace(exception.Response))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(exception.Response);
            var root = document.RootElement;
            if (root.TryGetProperty("message", out var message))
            {
                result.Message = message.GetString();
            }

            if (root.TryGetProperty("failureCode", out var failureCode))
            {
                result.FailureCode = failureCode.GetString();
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                result.Errors = errors.EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .ToArray();
            }
            else if (root.TryGetProperty("errors", out errors) && errors.ValueKind == JsonValueKind.Object)
            {
                result.Errors = errors.EnumerateObject()
                    .Where(field => field.Value.ValueKind == JsonValueKind.Array)
                    .SelectMany(field => field.Value.EnumerateArray())
                    .Select(item => item.GetString() ?? string.Empty)
                    .ToArray();
            }
        }
        catch (JsonException)
        {
        }

        return result;
    }

    private static BaseCommandResponseOfGuid FailedCommandResponse(string message) =>
        new() { Success = false, Message = message, Errors = [message] };

    private static PatchModuleSettingsDto ToPatch(ModuleSettingsDto settings) => new()
    {
        EnableIslamicModule = Optional(settings.EnableIslamicModule),
        EnableTechModule = Optional(settings.EnableTechModule)
    };

    private static PatchEventPolicyDto ToPatch(EventPolicyDto settings) => new()
    {
        AllowUserSubmittedEvents = Optional(settings.AllowUserSubmittedEvents),
        AllowOrganizationSubmittedEvents = Optional(settings.AllowOrganizationSubmittedEvents),
        AllowGroupSubmittedEvents = Optional(settings.AllowGroupSubmittedEvents),
        EventCardClickOpensDetailPage = Optional(settings.EventCardClickOpensDetailPage),
        LockTenantEventCardClickBehavior = Optional(settings.LockTenantEventCardClickBehavior)
    };

    private static PatchOrganizationPolicyDto ToPatch(OrganizationPolicyDto settings) => new()
    {
        RequireOrganizationVerification = Optional(settings.RequireOrganizationVerification),
        AllowTenantToOmitVerification = Optional(settings.AllowTenantToOmitVerification),
        AllowOrganizationSelfRegistration = Optional(settings.AllowOrganizationSelfRegistration),
        AllowGroupSelfRegistration = Optional(settings.AllowGroupSelfRegistration)
    };

    private static PatchBrandingSettingsDto ToPatch(BrandingSettingsDto settings) => new()
    {
        DefaultBrandDisplayName = Optional(settings.DefaultBrandDisplayName),
        DefaultBrandLogoUrl = Optional(settings.DefaultBrandLogoUrl),
        DefaultBrandFaviconUrl = Optional(settings.DefaultBrandFaviconUrl),
        DefaultBrandCustomCssUrl = Optional(settings.DefaultBrandCustomCssUrl),
        LockTenantBrandDisplayName = Optional(settings.LockTenantBrandDisplayName),
        LockTenantBrandLogoUrl = Optional(settings.LockTenantBrandLogoUrl),
        LockTenantBrandFaviconUrl = Optional(settings.LockTenantBrandFaviconUrl),
        LockTenantBrandCustomCssUrl = Optional(settings.LockTenantBrandCustomCssUrl)
    };

    private static PatchDomainSettingsDto ToPatch(DomainSettingsDto settings) => new()
    {
        InstanceBaseDomain = Optional(settings.InstanceBaseDomain),
        AdminHost = Optional(settings.AdminHost),
        AllowTenantCustomDomains = Optional(settings.AllowTenantCustomDomains),
        LockTenantSubdomain = Optional(settings.LockTenantSubdomain),
        LockTenantCustomDomain = Optional(settings.LockTenantCustomDomain)
    };

    private static PatchTenantDelegationSettingsDto ToPatch(TenantDelegationSettingsDto settings) => new()
    {
        AllowTenantSelfServiceRegistration = Optional(settings.AllowTenantSelfServiceRegistration),
        AllowTenantWhiteLabeling = Optional(settings.AllowTenantWhiteLabeling),
        DefaultPublicHomePage = Optional(settings.DefaultPublicHomePage),
        LockTenantHomePagePreference = Optional(settings.LockTenantHomePagePreference),
        LockTenantSmtp = Optional(settings.LockTenantSmtp),
        LockTenantStorage = Optional(settings.LockTenantStorage),
        LockTenantAnalytics = Optional(settings.LockTenantAnalytics),
        LockTenantAiAssistant = Optional(settings.LockTenantAiAssistant)
    };

    private static PatchRenderPolicySettingsDto ToPatch(RenderPolicySettingsDto settings) => new()
    {
        RenderPolicyPreset = Optional(settings.RenderPolicyPreset),
        EnableAdvancedRenderPolicyOverrides = Optional(settings.EnableAdvancedRenderPolicyOverrides),
        GlobalRenderMode = Optional(settings.GlobalRenderMode),
        GlobalPrerenderEnabled = Optional(settings.GlobalPrerenderEnabled),
        PublicSeoRenderMode = Optional(settings.PublicSeoRenderMode),
        PublicSeoPrerenderEnabled = Optional(settings.PublicSeoPrerenderEnabled),
        OperationalRenderMode = Optional(settings.OperationalRenderMode),
        OperationalPrerenderEnabled = Optional(settings.OperationalPrerenderEnabled),
        AdminRenderMode = Optional(settings.AdminRenderMode),
        AdminPrerenderEnabled = Optional(settings.AdminPrerenderEnabled),
        OnboardingRenderMode = Optional(settings.OnboardingRenderMode),
        OnboardingPrerenderEnabled = Optional(settings.OnboardingPrerenderEnabled),
        AllowTenantRenderPolicyOverride = Optional(settings.AllowTenantRenderPolicyOverride),
        LockTenantPublicSeoRenderPolicy = Optional(settings.LockTenantPublicSeoRenderPolicy),
        LockTenantOperationalRenderPolicy = Optional(settings.LockTenantOperationalRenderPolicy),
        LockTenantAdminRenderPolicy = Optional(settings.LockTenantAdminRenderPolicy)
    };

    private static PatchMcpGovernanceSettingsDto ToPatch(McpGovernanceSettingsDto settings) => new()
    {
        Enabled = Optional(settings.Enabled),
        EnableLegacySse = Optional(settings.EnableLegacySse),
        LockTenantMcp = Optional(settings.LockTenantMcp),
        LockTenantMcpLegacySse = Optional(settings.LockTenantMcpLegacySse)
    };

    private static PatchAiAssistantGovernanceSettingsDto ToPatch(AiAssistantGovernanceSettingsDto settings) => new()
    {
        Enabled = Optional(settings.Enabled),
        AllowAnonymousAccess = Optional(settings.AllowAnonymousAccess),
        ToolProposalsEnabled = Optional(settings.ToolProposalsEnabled),
        LockTenantAiAssistant = Optional(settings.LockTenantAiAssistant)
    };

    private static PatchAuthProviderConfigurationDto ToPatch(AuthProviderConfigurationDto config) => new()
    {
        Configuration = new OptionalUpdateOfAuthProviderConfigurationWriteDto
        {
            HasValue = true,
            Value = new AuthProviderConfigurationWriteDto
            {
                PrimaryProviderId = config.PrimaryProviderId,
                LockPrimaryProvider = config.LockPrimaryProvider,
                KeycloakAuthority = config.KeycloakAuthority,
                KeycloakClientId = config.KeycloakClientId,
                KeycloakClientSecret = config.KeycloakClientSecret,
                AtprotoLoginEnabled = config.AtprotoLoginEnabled,
                AtprotoPublicUrl = config.AtprotoPublicUrl,
                GoogleSsoEnabled = config.GoogleSsoEnabled,
                GoogleClientId = config.GoogleClientId,
                GoogleClientSecret = config.GoogleClientSecret,
                LockAtprotoLoginEnabled = config.LockAtprotoLoginEnabled,
                LockGoogleSsoEnabled = config.LockGoogleSsoEnabled
            }
        }
    };

    private static PatchAuthorizationProviderConfigurationDto ToPatch(AuthorizationProviderConfigurationDto config) => new()
    {
        Configuration = new OptionalUpdateOfAuthorizationProviderConfigurationWriteDto
        {
            HasValue = true,
            Value = new AuthorizationProviderConfigurationWriteDto
            {
                Provider = config.Provider,
                CerbosGrpcEndpoint = config.CerbosGrpcEndpoint,
                CerbosAdminEndpoint = config.CerbosAdminEndpoint
            }
        }
    };

    private static PatchAnalyticsGovernanceSettingsDto ToPatch(AnalyticsGovernanceSettingsDto settings) => new()
    {
        CookieConsentEnabled = Optional(settings.CookieConsentEnabled),
        DeclineBehavior = Optional(settings.DeclineBehavior),
        ConsentCookieLifetimeDays = Optional(settings.ConsentCookieLifetimeDays),
        GlobalDisableClientTracking = Optional(settings.GlobalDisableClientTracking),
        PosthogCookielessMode = Optional(settings.PosthogCookielessMode),
        PosthogPersonProfiles = Optional(settings.PosthogPersonProfiles),
        PosthogSessionReplay = Optional(settings.PosthogSessionReplay),
        PosthogAutocapture = Optional(settings.PosthogAutocapture),
        PosthogHeatmaps = Optional(settings.PosthogHeatmaps),
        PosthogToolbar = Optional(settings.PosthogToolbar)
    };

    private static PatchFooterGovernanceSettingsDto ToPatch(FooterGovernanceSettingsDto settings) => new()
    {
        LockTenantTemplate = Optional(settings.LockTenantTemplate),
        LockTenantLinkGroups = Optional(settings.LockTenantLinkGroups),
        LockTenantSocialLinks = Optional(settings.LockTenantSocialLinks),
        LockTenantDescription = Optional(settings.LockTenantDescription),
        LockTenantCopyright = Optional(settings.LockTenantCopyright)
    };

    private static OptionalUpdateOfboolean? Optional(bool? value) =>
        value.HasValue ? new() { HasValue = true, Value = value } : null;

    private static OptionalUpdateOfint? Optional(int? value) =>
        value.HasValue ? new() { HasValue = true, Value = value } : null;

    private static OptionalUpdateOfstring? Optional(string? value) =>
        value is not null ? new() { HasValue = true, Value = value } : null;

    private static OptionalUpdateOfDeclineBehavior? Optional(DeclineBehavior? value) =>
        value.HasValue ? new() { HasValue = true, Value = value } : null;

    private static OptionalUpdateOfPosthogCookielessMode? Optional(PosthogCookielessMode? value) =>
        value.HasValue ? new() { HasValue = true, Value = value } : null;

    private static OptionalUpdateOfPosthogPersonProfiles? Optional(PosthogPersonProfiles? value) =>
        value.HasValue ? new() { HasValue = true, Value = value } : null;

    private static ICollection<string> MergeBootstrapValues(ICollection<string>? currentValues, string requiredValue) =>
        (currentValues ?? [])
            .Append(requiredValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static KeycloakRealmDoctorResultDto BlockedDoctor(string message) => new()
    {
        OverallStatus = "blocked",
        Message = message,
        Checks = [new KeycloakRealmDoctorCheckDto
        {
            Code = "keycloak_doctor_failed",
            Name = "Keycloak diagnostics",
            Status = "blocked",
            Message = message
        }]
    };

    private static KeycloakRealmSyncPlanDto BlockedPlan(string message) => new()
    {
        Status = "blocked",
        Message = message,
        Operations = [new KeycloakRealmSyncOperationDto
        {
            OperationId = "keycloak_sync_failed",
            Category = "inspection",
            TargetType = "realm",
            Target = "Keycloak",
            Action = "none",
            Status = "blocked",
            Summary = message,
            Reason = "The sync operation could not be completed safely."
        }]
    };

    private static KeycloakClientSecretRotationResultDto BlockedRotation(string message) => new()
    {
        Status = "blocked",
        Message = message,
        Operations = [new KeycloakRealmSyncOperationDto
        {
            OperationId = "keycloak_client_secret_rotation_failed",
            Category = "client-secret",
            TargetType = "client",
            Action = "update",
            Status = "blocked",
            Summary = message,
            Reason = "The client-secret rotation could not be completed safely."
        }]
    };
}
