using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Secrets;
using Explore.Application.Utilities;
using Explore.Domain.Constants;
using Explore.Domain;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly CerbosAdminEndpointValidator _adminEndpointValidator;
    private readonly IAuthorizationProviderModeCacheInvalidator _providerModeCacheInvalidator;
    private readonly ICerbosConfigResolver _cerbosConfigResolver;
    private readonly IPolicyPackageService _policyPackageService;
    private readonly AuthorizationProviderDeploymentOptions _deploymentOptions;
    private readonly AuthorizationProviderBootstrapState _bootstrapState;
    private readonly ILogger<AuthorizationProviderConfigurationService> _logger;
    private readonly IPolicyMutationExecutionScopeFactory _scopeFactory;

    public AuthorizationProviderConfigurationService(
        ISystemSettingRepository systemSettingRepository,
        IConfiguration configuration,
        CerbosAdminEndpointValidator adminEndpointValidator,
        IAuthorizationProviderModeCacheInvalidator providerModeCacheInvalidator,
        ICerbosConfigResolver cerbosConfigResolver,
        IPolicyPackageService policyPackageService,
        IOptions<AuthorizationProviderDeploymentOptions> deploymentOptions,
        AuthorizationProviderBootstrapState bootstrapState,
        ILogger<AuthorizationProviderConfigurationService> logger,
        IPolicyMutationExecutionScopeFactory scopeFactory)
    {
        _systemSettingRepository = systemSettingRepository;
        _configuration = configuration;
        _adminEndpointValidator = adminEndpointValidator;
        _providerModeCacheInvalidator = providerModeCacheInvalidator;
        _cerbosConfigResolver = cerbosConfigResolver;
        _policyPackageService = policyPackageService;
        _deploymentOptions = deploymentOptions.Value;
        _bootstrapState = bootstrapState;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public Task<AuthorizationProviderConfigurationDto> ReadConfigurationAsync() =>
        ReadConfigurationCoreAsync(null, null);

    private async Task<AuthorizationProviderConfigurationDto> ReadConfigurationCoreAsync(
        ProviderConfigurationState? original, IConfiguration? deployment, bool? originalPublication = null)
    {
        Task<SystemSetting?> Read(string key) => original is null
            ? _systemSettingRepository.GetByKey(key) : Task.FromResult(original.System[key]?.ToEntity());
        var configuration = deployment ?? _configuration;
        var providerSetting = await Read(GovernanceSettingKeys.Security.AuthorizationProvider);
        var grpcEndpointSetting = await Read(GovernanceSettingKeys.Cerbos.GrpcEndpoint);
        var adminEndpointSetting = await Read(GovernanceSettingKeys.Cerbos.CustomAdminEndpoint);

        // Preserve operator's raw bootstrap value end-to-end. Deployment values prefill setup/admin
        // screens, but application-managed saved settings take precedence after an explicit save.
        var deploymentProvider = _deploymentOptions.GetProvider();
        var rawEnvEndpoint = configuration["Cerbos:GrpcEndpoint"]?.Trim() ?? string.Empty;
        var detectedFromEnv = !string.IsNullOrWhiteSpace(rawEnvEndpoint);
        var storedProviderConfigured = providerSetting is not null && !string.IsNullOrWhiteSpace(providerSetting.Value);
        var provider = deploymentProvider ?? DeserializeString(providerSetting?.Value, "local");
        var bootstrap = ResolveBootstrapSnapshot(deploymentProvider);
        var providerConfigured = deploymentProvider switch
        {
            AuthorizationProviderDeploymentOptions.LocalProvider => true,
            AuthorizationProviderDeploymentOptions.CerbosProvider => bootstrap.Status == AuthorizationProviderBootstrapState.Ready
                && (originalPublication ?? await _policyPackageService.IsPublicationReadyAsync()),
            _ => storedProviderConfigured
        };
        var storedGrpcEndpoint = DeserializeString(grpcEndpointSetting?.Value, string.Empty);
        var endpointDeploymentManaged = deploymentProvider == AuthorizationProviderDeploymentOptions.CerbosProvider
                                        || IsDeploymentManaged(GovernanceSettingKeys.Cerbos.GrpcEndpoint, configuration)
                                        || IsDeploymentManaged(Explore.Domain.Secrets.SecretDefinitionRegistry.Keys.Cerbos.GrpcEndpoint, configuration);

        var grpcEndpoint = endpointDeploymentManaged
            ? rawEnvEndpoint
            : storedGrpcEndpoint;

        if (string.IsNullOrWhiteSpace(grpcEndpoint) && detectedFromEnv)
        {
            grpcEndpoint = rawEnvEndpoint;
        }

        return new AuthorizationProviderConfigurationDto
        {
            Provider = provider,
            CerbosGrpcEndpoint = grpcEndpoint,
            CerbosAdminEndpoint = DeserializeString(adminEndpointSetting?.Value, string.Empty),
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
        };
    }

    public async Task ApplyConfigurationAsync(UpdateAuthorizationProviderConfigurationCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var observation = await ObserveConfigurationAsync(command, cancellationToken);
        await ApplyConfigurationAsync(observation, cancellationToken);
    }

    public async Task ApplySetupConfigurationAsync(UpdateAuthorizationProviderConfigurationDuringSetupCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var observation = await ObserveConfigurationAsync(command, cancellationToken);
        await ApplyConfigurationAsync(observation, cancellationToken);
    }

    public Task<ProviderConfigurationMutation> ObserveConfigurationAsync(UpdateAuthorizationProviderConfigurationCommand command,
        CancellationToken token) => ObserveConfigurationCoreAsync(command, token);

    public Task<ProviderConfigurationMutation> ObserveConfigurationAsync(UpdateAuthorizationProviderConfigurationDuringSetupCommand command,
        CancellationToken token) => ObserveConfigurationCoreAsync(command, token);

    public async Task ApplyConfigurationAsync(ProviderConfigurationMutation observation, CancellationToken token)
    {
        await observation.ExecuteAsync(this, token);
        _cerbosConfigResolver.InvalidateCache();
        _providerModeCacheInvalidator.InvalidateInstanceMode();
    }

    private async Task<ProviderConfigurationMutation> ObserveConfigurationCoreAsync(object command, CancellationToken token)
    {
        var observation = await ProviderConfigurationMutation.ObserveAsync(command, this, _scopeFactory, token);
        try
        {
            var core = observation.Services.GetRequiredService<IAuthorizationProviderConfigurationService>()
                as AuthorizationProviderConfigurationService
                ?? throw new UnauthorizedAccessException("The original provider compiler is unavailable.");
            var materials = await observation.Source.ObserveMaterialsAsync(observation.Before, token);
            var original = await core.ReadConfigurationCoreAsync(observation.Before, observation.Source.Deployment, materials.PublicationReady);
            var configuration = observation.AuthorizationPatch ?? throw new ArgumentException("A complete provider group is required.");
            string? deploymentProvider = core._deploymentOptions.GetProvider();
            bool cerbos = configuration.Provider.Equals("cerbos", StringComparison.OrdinalIgnoreCase);
            var rows = new List<SystemSetting>();
            void Add(string key, string value, int order, string description) => rows.Add(new()
            {
                SettingKey = key,
                Value = JsonSerializer.Serialize(value),
                ValueType = SettingValueType.String,
                IsLocked = true,
                Category = "Security",
                DisplayOrder = order,
                Description = description,
                CreatedAt = observation.OccurredAt,
                UpdatedAt = observation.OccurredAt
            });
            if (deploymentProvider is null)
            {
                Add(GovernanceSettingKeys.Security.AuthorizationProvider, configuration.Provider.ToLowerInvariant(), 1,
                    "Authorization provider: 'cerbos' for external PDP, 'local' for built-in RBAC");
                if (!core.IsDeploymentManaged(GovernanceSettingKeys.Cerbos.GrpcEndpoint, observation.Source.Deployment)
                    && !core.IsDeploymentManaged(Explore.Domain.Secrets.SecretDefinitionRegistry.Keys.Cerbos.GrpcEndpoint, observation.Source.Deployment))
                    Add(GovernanceSettingKeys.Cerbos.GrpcEndpoint,
                        cerbos ? GrpcEndpointNormalizer.Normalize(configuration.CerbosGrpcEndpoint) : string.Empty, 2,
                        "Cerbos PDP gRPC endpoint for authorization requests");
                string rawAdmin = configuration.CerbosAdminEndpoint?.Trim() ?? "";
                if (cerbos && rawAdmin.Length != 0
                    && core._adminEndpointValidator.TryNormalize(rawAdmin, isByo: true, out _, out _))
                {
                    string endpoint = rawAdmin.TrimEnd('/');
                    if (!endpoint.Contains("://", StringComparison.Ordinal)) endpoint = $"https://{endpoint}";
                    Add(GovernanceSettingKeys.Cerbos.CustomAdminEndpoint,
                        new Uri(endpoint).GetLeftPart(UriPartial.Path).TrimEnd('/'), 3,
                        "Cerbos Admin API endpoint for policy package publishing");
                }
            }
            await observation.CompileAuthorizationAsync(original, rows, core, token);
            await observation.Services.GetRequiredService<IProviderConfigurationSettingsWriter>()
                .RequireUnchangedAsync(observation.Before, token);
            return observation;
        }
        catch { await observation.DisposeAsync(); throw; }
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
            return ResolveBootstrapSnapshot(deploymentProvider).Status == AuthorizationProviderBootstrapState.Ready
                && await _policyPackageService.IsPublicationReadyAsync();
        }

        var providerSetting = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider);
        return providerSetting is not null && !string.IsNullOrWhiteSpace(providerSetting.Value);
    }

    public async Task<AuthorizationProviderReconciliationResult> ReconcileDeploymentProviderAsync(
        CancellationToken cancellationToken = default)
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
            token => ReconcileCerbosDeploymentProviderAsync(provider, token),
            cancellationToken);
    }

    private async Task<AuthorizationProviderReconciliationResult> ReconcileCerbosDeploymentProviderAsync(
        string provider,
        CancellationToken cancellationToken)
    {
        var endpointVerified = false;
        try
        {
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
                if (!await _policyPackageService.IsPublicationReadyAsync(cancellationToken))
                {
                    return MarkReconciliationFailed(
                        provider,
                        endpointVerified: true,
                        "A current authenticated operator publication receipt for this revision, package and environment is required.");
                }

                const string message = "Cerbos PDP health and the operator publication declaration were verified. Live-store equality is unverified; resource activation remains separate.";
                _bootstrapState.MarkReady(provider, endpointVerified: true, policiesSynchronized: false, message);
                _cerbosConfigResolver.InvalidateCache();
                _providerModeCacheInvalidator.InvalidateInstanceMode();
                return new(true, true, true, false, message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    "Deployment-managed Cerbos reconciliation failed. FailureType={FailureType}",
                    ex.GetType().Name);
                return MarkReconciliationFailed(
                    provider,
                    endpointVerified: true,
                    "The Cerbos publication declaration could not be verified.");
            }
        }
        catch (OperationCanceledException)
        {
            MarkReconciliationFailed(
                provider,
                endpointVerified,
                endpointVerified
                    ? "Cerbos readiness was canceled before publication verification completed."
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

    private bool IsDeploymentManaged(string key, IConfiguration? deployment = null)
    {
        var configuredKeys = (deployment ?? _configuration).GetSection("Secrets:Ownership:DeploymentManagedKeys").Get<string[]>()
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
