using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Secrets;
using Explore.Application.Utilities;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

public class AuthorizationProviderConfigurationService : IAuthorizationProviderConfigurationService
{
    private readonly ISystemSettingRepository _systemSettingRepository;
    private readonly IConfiguration _configuration;
    private readonly ISecretResolver _secretResolver;
    private readonly CerbosAdminEndpointValidator _adminEndpointValidator;
    private readonly IAuthorizationProviderModeCacheInvalidator _providerModeCacheInvalidator;
    private readonly ICerbosConfigResolver _cerbosConfigResolver;
    private readonly IPolicyPackageService _policyPackageService;
    private readonly AuthorizationProviderDeploymentOptions _deploymentOptions;
    private readonly AuthorizationProviderBootstrapState _bootstrapState;
    private readonly ILogger<AuthorizationProviderConfigurationService> _logger;

    public AuthorizationProviderConfigurationService(
        ISystemSettingRepository systemSettingRepository,
        IConfiguration configuration,
        ISecretResolver secretResolver,
        CerbosAdminEndpointValidator adminEndpointValidator,
        IAuthorizationProviderModeCacheInvalidator providerModeCacheInvalidator,
        ICerbosConfigResolver cerbosConfigResolver,
        IPolicyPackageService policyPackageService,
        IOptions<AuthorizationProviderDeploymentOptions> deploymentOptions,
        AuthorizationProviderBootstrapState bootstrapState,
        ILogger<AuthorizationProviderConfigurationService> logger)
    {
        _systemSettingRepository = systemSettingRepository;
        _configuration = configuration;
        _secretResolver = secretResolver;
        _adminEndpointValidator = adminEndpointValidator;
        _providerModeCacheInvalidator = providerModeCacheInvalidator;
        _cerbosConfigResolver = cerbosConfigResolver;
        _policyPackageService = policyPackageService;
        _deploymentOptions = deploymentOptions.Value;
        _bootstrapState = bootstrapState;
        _logger = logger;
    }

    public async Task<AuthorizationProviderConfigurationDto> ReadConfigurationAsync()
    {
        var providerSetting = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider);
        var grpcEndpointSetting = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Cerbos.GrpcEndpoint);
        var adminEndpointSetting = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Cerbos.CustomAdminEndpoint);

        // Preserve operator's raw bootstrap value end-to-end. Deployment values prefill setup/admin
        // screens, but application-managed saved settings take precedence after an explicit save.
        var deploymentProvider = _deploymentOptions.GetProvider();
        var rawEnvEndpoint = _configuration["Cerbos:GrpcEndpoint"]?.Trim() ?? string.Empty;
        var detectedFromEnv = !string.IsNullOrWhiteSpace(rawEnvEndpoint);
        var storedProviderConfigured = providerSetting is not null && !string.IsNullOrWhiteSpace(providerSetting.Value);
        var provider = deploymentProvider ?? DeserializeString(providerSetting?.Value, "local");
        var bootstrap = ResolveBootstrapSnapshot(deploymentProvider);
        var providerConfigured = deploymentProvider switch
        {
            AuthorizationProviderDeploymentOptions.LocalProvider => true,
            AuthorizationProviderDeploymentOptions.CerbosProvider => bootstrap.Status == AuthorizationProviderBootstrapState.Ready,
            _ => storedProviderConfigured
        };
        var storedGrpcEndpoint = DeserializeString(grpcEndpointSetting?.Value, string.Empty);
        var endpointDeploymentManaged = deploymentProvider == AuthorizationProviderDeploymentOptions.CerbosProvider
                                        || IsDeploymentManaged(GovernanceSettingKeys.Cerbos.GrpcEndpoint)
                                        || IsDeploymentManaged(Explore.Domain.Secrets.SecretDefinitionRegistry.Keys.Cerbos.GrpcEndpoint);

        var grpcEndpoint = endpointDeploymentManaged
            ? rawEnvEndpoint
            : storedGrpcEndpoint;

        if (string.IsNullOrWhiteSpace(grpcEndpoint) && detectedFromEnv)
        {
            grpcEndpoint = rawEnvEndpoint;
        }

        var configuredAdminUsernameConfigured = (await _secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Cerbos.CustomAdminUsername,
            null)).IsResolved;
        var configuredAdminPasswordConfigured = (await _secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Cerbos.CustomAdminPassword,
            null)).IsResolved;
        var adminCredentialsConfigured = configuredAdminUsernameConfigured && configuredAdminPasswordConfigured;

        return new AuthorizationProviderConfigurationDto
        {
            Provider = provider,
            CerbosGrpcEndpoint = grpcEndpoint,
            CerbosAdminEndpoint = DeserializeString(adminEndpointSetting?.Value, string.Empty),
            CerbosAdminUsernameConfigured = configuredAdminUsernameConfigured,
            CerbosAdminPasswordConfigured = configuredAdminPasswordConfigured,
            CerbosDetectedFromEnvironment = detectedFromEnv,
            CerbosEndpointVerified = bootstrap.EndpointVerified,
            CerbosPoliciesSynchronized = bootstrap.PoliciesSynchronized,
            AuthorizationProviderConfigured = providerConfigured,
            AuthorizationProviderManagedByDeployment = deploymentProvider is not null,
            AuthorizationProviderBootstrapStatus = bootstrap.Status,
            AuthorizationProviderBootstrapMessage = bootstrap.Message,
            CerbosEndpointOwnership = CreateOwnershipMetadata(
                endpointDeploymentManaged,
                configured: !string.IsNullOrWhiteSpace(grpcEndpoint),
                bootstrapAvailable: detectedFromEnv && !endpointDeploymentManaged && string.IsNullOrWhiteSpace(storedGrpcEndpoint),
                applicationManagedDescription: "Saved Cerbos PDP endpoint settings take precedence after onboarding/admin save. Environment values are only bootstrap prefills unless deployment-managed mode is configured.",
                deploymentManagedDescription: "Cerbos PDP endpoint is managed by deployment configuration. Change it in the environment, secret provider, or appsettings and restart."),
            CerbosAdminCredentialsOwnership = CreateOwnershipMetadata(
                deploymentManaged: adminCredentialsConfigured,
                configured: adminCredentialsConfigured,
                bootstrapAvailable: false,
                applicationManagedDescription: "No deployment credentials are configured. One-time credentials can be supplied for an explicit policy sync and are not saved.",
                deploymentManagedDescription: "Cerbos Admin API credentials are provided by deployment configuration. One-time credentials can override them for a single explicit policy sync."),
        };
    }

    public async Task ApplyConfigurationAsync(AuthorizationProviderConfigurationDto configuration)
    {
        var deploymentProvider = _deploymentOptions.GetProvider();
        if (deploymentProvider is not null)
        {
            if (!configuration.Provider.Equals(deploymentProvider, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The authorization provider is managed by deployment configuration and cannot be changed in the application.");
            }

            _cerbosConfigResolver.InvalidateCache();
            _providerModeCacheInvalidator.InvalidateInstanceMode();
            return;
        }

        var isCerbosProvider = configuration.Provider.Equals("cerbos", StringComparison.OrdinalIgnoreCase);
        var normalizedGrpcEndpoint = isCerbosProvider
            ? GrpcEndpointNormalizer.Normalize(configuration.CerbosGrpcEndpoint)
            : string.Empty;
        var rawAdminEndpoint = configuration.CerbosAdminEndpoint?.Trim() ?? string.Empty;
        var endpointDeploymentManaged = IsDeploymentManaged(GovernanceSettingKeys.Cerbos.GrpcEndpoint)
                                        || IsDeploymentManaged(Explore.Domain.Secrets.SecretDefinitionRegistry.Keys.Cerbos.GrpcEndpoint);
        Uri? normalizedAdminEndpoint = null;

        if (isCerbosProvider && !string.IsNullOrWhiteSpace(rawAdminEndpoint))
        {
            if (!_adminEndpointValidator.TryNormalize(rawAdminEndpoint, isByo: true, out normalizedAdminEndpoint, out var warning))
                throw new InvalidOperationException(warning);
        }

        await UpsertSettingAsync(
            GovernanceSettingKeys.Security.AuthorizationProvider,
            JsonSerializer.Serialize(configuration.Provider.ToLowerInvariant()),
            SettingValueType.String,
            true,
            "Security",
            1,
            "Authorization provider: 'cerbos' for external PDP, 'local' for built-in RBAC");

        if (!endpointDeploymentManaged)
        {
            await UpsertSettingAsync(
                GovernanceSettingKeys.Cerbos.GrpcEndpoint,
                JsonSerializer.Serialize(normalizedGrpcEndpoint),
                SettingValueType.String,
                true,
                "Security",
                2,
                "Cerbos PDP gRPC endpoint for authorization requests");
        }

        if (!isCerbosProvider)
        {
            _cerbosConfigResolver.InvalidateCache();
            _providerModeCacheInvalidator.InvalidateInstanceMode();
            return;
        }

        if (normalizedAdminEndpoint is not null)
        {
            await UpsertSettingAsync(
                GovernanceSettingKeys.Cerbos.CustomAdminEndpoint,
                JsonSerializer.Serialize(normalizedAdminEndpoint.GetLeftPart(UriPartial.Path).TrimEnd('/')),
                SettingValueType.String,
                true,
                "Security",
                3,
                "Cerbos Admin API endpoint for policy package publishing");
        }

        _cerbosConfigResolver.InvalidateCache();
        _providerModeCacheInvalidator.InvalidateInstanceMode();
    }

    public async Task<bool> IsConfiguredAsync()
    {
        var deploymentProvider = _deploymentOptions.GetProvider();
        if (deploymentProvider == AuthorizationProviderDeploymentOptions.LocalProvider)
        {
            return true;
        }

        if (deploymentProvider == AuthorizationProviderDeploymentOptions.CerbosProvider)
        {
            return ResolveBootstrapSnapshot(deploymentProvider).Status == AuthorizationProviderBootstrapState.Ready;
        }

        var providerSetting = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider);
        return providerSetting is not null && !string.IsNullOrWhiteSpace(providerSetting.Value);
    }

    public async Task<AuthorizationProviderReconciliationResult> ReconcileDeploymentProviderAsync(
        CancellationToken cancellationToken = default,
        PolicyPackageAdminCredentials? oneTimeCredentials = null)
    {
        var provider = _deploymentOptions.GetProvider();
        if (provider is null)
        {
            return new(false, false, false, false, "No deployment-managed authorization provider is configured.");
        }

        if (provider == AuthorizationProviderDeploymentOptions.LocalProvider)
        {
            const string message = "Local authorization is ready from deployment configuration.";
            _bootstrapState.MarkReady(provider, endpointVerified: false, policiesSynchronized: false, message);
            _providerModeCacheInvalidator.InvalidateInstanceMode();
            return new(true, true, false, false, message);
        }

        return await _bootstrapState.RunSingleFlightAsync(
            token => ReconcileCerbosDeploymentProviderAsync(provider, oneTimeCredentials, token),
            cancellationToken);
    }

    private async Task<AuthorizationProviderReconciliationResult> ReconcileCerbosDeploymentProviderAsync(
        string provider,
        PolicyPackageAdminCredentials? oneTimeCredentials,
        CancellationToken cancellationToken)
    {
        var endpointVerified = false;
        try
        {
            var current = _bootstrapState.Read();
            if (current.Provider == provider && current.Status == AuthorizationProviderBootstrapState.Ready)
            {
                return new(
                    Attempted: true,
                    Succeeded: true,
                    EndpointVerified: current.EndpointVerified,
                    PoliciesSynchronized: current.PoliciesSynchronized,
                    Message: current.Message ?? "Cerbos authorization is ready.");
            }

            _bootstrapState.MarkPending(provider);
            cancellationToken.ThrowIfCancellationRequested();

            var endpoint = _configuration["Cerbos:GrpcEndpoint"];
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return MarkReconciliationFailed(
                    provider,
                    endpointVerified: false,
                    "Cerbos is selected by deployment configuration, but no PDP endpoint is configured.");
            }

            endpointVerified = await VerifyCerbosEndpointAsync(endpoint, cancellationToken);
            if (!endpointVerified)
            {
                return MarkReconciliationFailed(
                    provider,
                    endpointVerified: false,
                    "The deployment-managed Cerbos PDP endpoint could not be reached.");
            }

            try
            {
                var publishResult = await _policyPackageService.PublishInstanceAsync(
                    cancellationToken,
                    oneTimeCredentials);
                if (!publishResult.Succeeded)
                {
                    return MarkReconciliationFailed(
                        provider,
                        endpointVerified: true,
                        string.IsNullOrWhiteSpace(publishResult.Message)
                            ? "The Cerbos policy package could not be synchronized."
                            : publishResult.Message);
                }

                const string message = "Cerbos endpoint verification and policy synchronization completed.";
                _bootstrapState.MarkReady(provider, endpointVerified: true, policiesSynchronized: true, message);
                _cerbosConfigResolver.InvalidateCache();
                _providerModeCacheInvalidator.InvalidateInstanceMode();
                return new(true, true, true, true, message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    "Deployment-managed Cerbos reconciliation failed. FailureType={FailureType}",
                    ex.GetType().Name);
                return MarkReconciliationFailed(
                    provider,
                    endpointVerified: true,
                    "The Cerbos policy package could not be synchronized.");
            }
        }
        catch (OperationCanceledException)
        {
            MarkReconciliationFailed(
                provider,
                endpointVerified,
                endpointVerified
                    ? "Automatic Cerbos setup was canceled before policy synchronization completed."
                    : "Automatic Cerbos setup was canceled before completion.");
            throw;
        }
    }

    public async Task<bool> VerifyCerbosEndpointAsync(string grpcEndpoint, CancellationToken cancellationToken = default)
    {
        var normalizedEndpoint = GrpcEndpointNormalizer.Normalize(grpcEndpoint);
        if (!GrpcEndpointNormalizer.IsValid(normalizedEndpoint))
        {
            return false;
        }

        try
        {
            using var channel = GrpcChannel.ForAddress(
                normalizedEndpoint,
                CerbosGrpcChannelOptionsFactory.Create());

            var client = new Health.HealthClient(channel);
            var response = await client.CheckAsync(
                new HealthCheckRequest(),
                deadline: DateTime.UtcNow.AddSeconds(5),
                cancellationToken: cancellationToken);

            var isHealthy = response.Status == HealthCheckResponse.Types.ServingStatus.Serving;
            _logger.LogInformation("Cerbos gRPC health check completed with status {Status}", response.Status);
            return isHealthy;
        }
        catch (RpcException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (RpcException rpcEx)
        {
            _logger.LogWarning(
                "Cerbos gRPC health check failed. GrpcStatusCode={GrpcStatusCode}",
                rpcEx.StatusCode);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Cerbos gRPC health check failed. FailureType={FailureType}",
                ex.GetType().Name);
            return false;
        }
    }

    public Task<bool> VerifyCerbosAdminEndpointAsync(string adminEndpoint, CancellationToken cancellationToken = default)
    {
        var isSafe = _adminEndpointValidator.TryNormalize(adminEndpoint, isByo: true, out _, out var warning);
        if (!isSafe)
        {
            _logger.LogWarning("Cerbos Admin API endpoint validation failed: {Reason}", warning);
        }

        return Task.FromResult(isSafe);
    }

    private bool IsDeploymentManaged(string key)
    {
        var configuredKeys = _configuration.GetSection("Secrets:Ownership:DeploymentManagedKeys").Get<string[]>()
                             ?? Array.Empty<string>();
        return configuredKeys.Any(candidate =>
            candidate.Equals("*", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    private AuthorizationProviderBootstrapSnapshot ResolveBootstrapSnapshot(string? deploymentProvider)
    {
        if (deploymentProvider is null)
        {
            return new(null, AuthorizationProviderBootstrapState.NotApplicable, false, false, null);
        }

        if (deploymentProvider == AuthorizationProviderDeploymentOptions.LocalProvider)
        {
            return new(
                deploymentProvider,
                AuthorizationProviderBootstrapState.Ready,
                false,
                false,
                "Local authorization is ready from deployment configuration.");
        }

        var snapshot = _bootstrapState.Read();
        return snapshot.Provider == deploymentProvider
            ? snapshot
            : new(deploymentProvider, AuthorizationProviderBootstrapState.Pending, false, false, null);
    }

    private AuthorizationProviderReconciliationResult MarkReconciliationFailed(
        string provider,
        bool endpointVerified,
        string message)
    {
        _bootstrapState.MarkFailed(provider, endpointVerified, message);
        _providerModeCacheInvalidator.InvalidateInstanceMode();
        return new(true, false, endpointVerified, false, message);
    }

    private static SecretOwnershipDto CreateOwnershipMetadata(
        bool deploymentManaged,
        bool configured,
        bool bootstrapAvailable,
        string applicationManagedDescription,
        string deploymentManagedDescription)
    {
        if (deploymentManaged)
        {
            return new SecretOwnershipDto
            {
                Mode = "deployment-managed",
                Source = "deployment",
                Badge = "Managed by Deployment",
                Description = deploymentManagedDescription,
                Editable = false,
                Configured = configured,
                BootstrapAvailable = false
            };
        }

        return new SecretOwnershipDto
        {
            Mode = "application-managed",
            Source = bootstrapAvailable ? "deployment-bootstrap" : "application",
            Badge = bootstrapAvailable ? "Bootstrap from Deployment" : "Managed by Application",
            Description = bootstrapAvailable
                ? "These values were detected from environment variables. If you modify them, saved application settings will be used from now on."
                : applicationManagedDescription,
            Editable = true,
            Configured = configured,
            BootstrapAvailable = bootstrapAvailable
        };
    }

    private async Task UpsertSettingAsync(
        string settingKey,
        string value,
        SettingValueType valueType,
        bool isLocked,
        string category,
        int displayOrder,
        string description)
    {
        await _systemSettingRepository.UpsertAsync(new SystemSetting
        {
            SettingKey = settingKey,
            Value = value,
            ValueType = valueType,
            IsLocked = isLocked,
            Description = description,
            Category = category,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    private static string DeserializeString(string? rawValue, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return defaultValue;

        try
        {
            var deserialized = JsonSerializer.Deserialize<string>(rawValue);
            return string.IsNullOrWhiteSpace(deserialized) ? defaultValue : deserialized;
        }
        catch
        {
            return rawValue.Trim('"');
        }
    }
}
