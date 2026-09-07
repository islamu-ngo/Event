namespace Explore.Secrets.Extensions;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.SetupLive;
using Explore.Secrets.HealthChecks;
using Explore.Secrets.Infrastructure;
using Explore.Secrets.Observability;
using Explore.Secrets.Services;
using Explore.Secrets.Sources;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// Registers the secret resolution pipeline.
/// </summary>
public static class SecretResolutionServiceCollectionExtensions
{
    /// <summary>
    /// Name used for the secret-resolver health check registration.
    /// </summary>
    public const string HealthCheckName = "secret-resolver";

    /// <summary>
    /// Wires the full Phase 3 pipeline: sources, resolver,
    /// metrics meter, and health check. Safe to call multiple times (uses TryAdd).
    /// </summary>
    public static IServiceCollection AddSecretResolution(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ---- Infrastructure ---------------------------------------------------
        services.AddMemoryCache();
        services.TryAddSingleton<IInfisicalClientFactory, InfisicalClientFactory>();

        // ---- Metrics ---------------------------------------------------------
        // IMeterFactory is registered by AddMetrics() (part of the default host).
        services.AddMetrics();
        services.TryAddSingleton<SecretResolverMetrics>();

        // ---- Sources (one per SourceType) ------------------------------------
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretSource, EnvironmentSecretSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretSource, InfisicalSecretSource>());

        // ---- Resolver --------------------------------------------------------
        services.TryAddScoped<ISecretResolver, SecretResolver>();
        services.TryAddScoped<ISetupSecretBindingWriter, SetupSecretBindingWriter>();
        services.TryAddScoped<ISetupSecretBindingReadinessReader>(provider =>
            (ISetupSecretBindingReadinessReader)provider.GetRequiredService<
                ISetupSecretBindingWriter>());
        services.TryAddScoped<ISetupSecretBindingCommitmentAuthority,
            SetupSecretBindingCommitmentAuthority>();
        services.TryAddSingleton<ISetupSecretBindingCommitBarrier,
            ImmediateSetupSecretBindingCommitBarrier>();

        // ---- Health check ----------------------------------------------------
        services.AddHealthChecks()
            .AddCheck<SecretResolverHealthCheck>(
                name: HealthCheckName,
                failureStatus: HealthStatus.Degraded,
                tags: ["ready", "secrets", "infrastructure"]);

        return services;
    }
}
