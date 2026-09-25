using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Explore.Infrastructure.Services;

/// <summary>Reads only fresh repository entities and non-secret deployment options; never resolves Admin configuration.</summary>
public sealed class EventResourceProviderSnapshotReader(
    ISystemSettingRepository systemSettings,
    ITenantSettingRepository tenantSettings,
    IEventResourceProviderActivationRepository activations,
    IOptions<AuthorizationProviderDeploymentOptions> deploymentOptions,
    IOptions<CerbosSettings> cerbosOptions,
    IConfiguration configuration,
    ILogger<EventResourceProviderSnapshotReader> logger) : IEventResourceProviderSnapshotReader
{
    private static readonly string[] RouteKeys =
    [
        GovernanceSettingKeys.Security.AuthorizationProvider,
        GovernanceSettingKeys.Cerbos.GrpcEndpoint,
        GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled,
        GovernanceSettingKeys.Cerbos.Mode,
        GovernanceSettingKeys.Cerbos.CustomEndpoint,
        EventResourceProviderBindingDocument.SettingKey
    ];

    public async Task<EventResourceProviderSnapshot?> ReadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var system = new Dictionary<string, SystemSetting>(StringComparer.Ordinal);
        foreach (var key in RouteKeys)
        {
            var setting = await systemSettings.GetByKey(key, cancellationToken);
            if (setting is not null)
                system.Add(key, setting);
        }
        var tenant = (await tenantSettings.GetByTenantAndKeys(tenantId, RouteKeys, cancellationToken))
            .ToDictionary(setting => setting.SettingKey, StringComparer.Ordinal);
        try
        {
            string Value(string key) => HierarchicalSettingMerge.Resolve(key, system, tenant)?.Value ?? "";
            var customization = JsonSerializer.Deserialize<bool>(Value(GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled));
            var mode = SettingValueSerializer.DeserializeString(Value(GovernanceSettingKeys.Cerbos.Mode));
            var custom = customization && mode == "custom_endpoint";
            // Unknown customization modes are not permission to fall back to local authorization.
            if (customization && mode is not ("custom_endpoint" or "shared" or "instance"))
                return null;
            var deploymentProvider = deploymentOptions.Value.GetProvider();
            var provider = deploymentProvider ?? SettingValueSerializer.DeserializeString(
                Value(GovernanceSettingKeys.Security.AuthorizationProvider)).Trim().ToLowerInvariant();
            if (!custom && provider == "local")
                return new(EventResourceProviderMode.Local, tenantId.ToString(), "default",
                    ParentEventPolicy: new(cerbosOptions.Value.UsePolicyScope ? tenantId.ToString("D") : ""));
            if (!custom && provider != "cerbos")
                return null;

            var managedKeys = configuration.GetSection("Secrets:Ownership:DeploymentManagedKeys").Get<string[]>() ?? [];
            var endpointManaged = deploymentProvider == AuthorizationProviderDeploymentOptions.CerbosProvider
                || managedKeys.Any(key => key == "*" || key.Equals(GovernanceSettingKeys.Cerbos.GrpcEndpoint, StringComparison.OrdinalIgnoreCase)
                    || key.Equals(Explore.Domain.Secrets.SecretDefinitionRegistry.Keys.Cerbos.GrpcEndpoint, StringComparison.OrdinalIgnoreCase));
            var endpoint = custom
                ? SettingValueSerializer.DeserializeString(Value(GovernanceSettingKeys.Cerbos.CustomEndpoint))
                : endpointManaged
                    ? cerbosOptions.Value.GrpcEndpoint
                    : system.TryGetValue(GovernanceSettingKeys.Cerbos.GrpcEndpoint, out var grpc)
                        ? SettingValueSerializer.DeserializeString(grpc.Value)
                        : cerbosOptions.Value.GrpcEndpoint;
            endpoint = EventResourceProviderBindingDocument.NormalizeEndpoint(endpoint);
            var document = EventResourceProviderBindingDocument.Parse(system.GetValueOrDefault(EventResourceProviderBindingDocument.SettingKey)?.Value);
            var binding = document.Deployments.SingleOrDefault(binding => binding.Endpoints.Contains(endpoint, StringComparer.Ordinal));
            if (binding is null)
                return null;
            var activation = await activations.GetAsync(binding.DeploymentId, cancellationToken);
            return new(EventResourceProviderMode.Remote, binding.Scope, binding.PolicyVersion, endpoint,
                binding.DeploymentId, activation?.Epoch ?? 0, activation?.State,
                new(cerbosOptions.Value.UsePolicyScope ? tenantId.ToString("D") : ""));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            // Do not log malformed endpoint/document contents: those are untrusted configuration.
            logger.LogWarning("Resource provider configuration is unusable. FailureType={FailureType}", exception.GetType().Name);
            return null;
        }
    }
}
