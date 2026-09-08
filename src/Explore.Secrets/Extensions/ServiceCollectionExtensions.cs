// Provides AddSecretProvider and AddSecretManagement extension methods.
// Includes observability setup with metrics, health checks, and audit logging.

using Explore.Secrets.Abstractions;
using Explore.Application.Contracts.Secrets;
using Explore.Secrets.Configuration;
using Explore.Secrets.Observability;
using Explore.Secrets.Providers;
using Explore.Secrets.Services;
using Explore.Secrets.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.Extensions;

/// <summary>
/// Extension methods for configuring secret management services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds secret provider services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSecretProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddSecretProvider(configuration, _ => { });
    }

    /// <summary>
    /// Adds secret provider services with custom configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="configure">Action to configure options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSecretProvider(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<SecretProviderOptions> configure)
    {
        // Bind options from configuration
        services.AddOptions<SecretProviderOptions>()
            .Bind(configuration.GetSection(SecretProviderOptions.SectionName))
            .Configure(configure)
            .ValidateOnStart();

        // Register validator
        services.TryAddSingleton<IValidateOptions<SecretProviderOptions>, SecretProviderOptionsValidator>();
        services.TryAddSingleton<UserSecretsAuthority>();

        // Register factory
        services.TryAddSingleton<SecretProviderFactory>();

        // Register ISecretProvider as singleton (created via factory)
        services.TryAddSingleton<ISecretProvider>(sp =>
        {
            var factory = sp.GetRequiredService<SecretProviderFactory>();
            var provider = factory.Create();

            // Initialize synchronously for startup
            // In production, consider async initialization pattern
            provider.InitializeAsync().GetAwaiter().GetResult();

            return provider;
        });
        services.TryAddSingleton<ISecretAuthorityStatusReader, SecretAuthorityStatusReader>();

        return services;
    }

    /// <summary>
    /// Adds complete secret management including refresh service and observability.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSecretManagement(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddSecretManagement(configuration, enableAuditing: true);
    }

    /// <summary>
    /// Adds complete secret management with configurable auditing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="enableAuditing">Whether to enable audit logging decorator.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSecretManagement(
        this IServiceCollection services,
        IConfiguration configuration,
        bool enableAuditing)
    {
        return services.AddSecretManagement(
            configuration,
            enableAuditing,
            enableRefreshService: true);
    }

    /// <summary>
    /// Adds complete secret management with configurable auditing, background refresh, and resolver registration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="enableAuditing">Whether to enable audit logging decorator.</param>
    /// <param name="enableRefreshService">Whether to register the background refresh service.</param>
    /// <param name="enableSecretResolution">Whether to register repository-backed secret resolution services.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSecretManagement(
        this IServiceCollection services,
        IConfiguration configuration,
        bool enableAuditing,
        bool enableRefreshService,
        bool enableSecretResolution = true)
    {
        // Add core secret provider
        services.AddSecretProvider(configuration);
        if (enableSecretResolution)
        {
            services.AddSecretResolution();
        }

        // Bind refresh options
        services.AddOptions<SecretRefreshOptions>()
            .Bind(configuration.GetSection(SecretRefreshOptions.SectionName));

        // Add observability
        services.AddSecretObservability(enableAuditing);

        // Add background refresh service
        if (enableRefreshService)
        {
            services.AddSecretRefreshService();
        }

        return services;
    }

    /// <summary>
    /// Adds secret management with explicit options configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="configureProvider">Action to configure provider options.</param>
    /// <param name="configureRefresh">Action to configure refresh options.</param>
    /// <param name="enableSecretResolution">Whether to register repository-backed secret resolution services.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSecretManagement(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<SecretProviderOptions>? configureProvider = null,
        Action<SecretRefreshOptions>? configureRefresh = null,
        bool enableSecretResolution = true)
    {
        // Add core secret provider with custom configuration
        services.AddSecretProvider(configuration, configureProvider ?? (_ => { }));
        if (enableSecretResolution)
        {
            services.AddSecretResolution();
        }

        // Bind refresh options with custom configuration
        var refreshOptionsBuilder = services.AddOptions<SecretRefreshOptions>()
            .Bind(configuration.GetSection(SecretRefreshOptions.SectionName));

        if (configureRefresh is not null)
        {
            refreshOptionsBuilder.Configure(configureRefresh);
        }

        return services;
    }

    /// <summary>
    /// Adds secret provider observability: metrics, health checks, and audit logging.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="enableAuditing">Whether to wrap provider with auditing decorator.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSecretObservability(
        this IServiceCollection services,
        bool enableAuditing = true)
    {
        // Register metrics as singleton (uses IMeterFactory if available)
        services.TryAddSingleton<SecretRefreshMetrics>();

        // Register audit logger
        services.TryAddSingleton<ISecretAuditLogger, StructuredSecretAuditLogger>();

        // Register health check
        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                name: SecretProviderHealthCheck.Name,
                factory: sp => new SecretProviderHealthCheck(
                    sp.GetRequiredService<ISecretProvider>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SecretProviderHealthCheck>>(),
                    sp.GetService<SecretRefreshMetrics>()),
                failureStatus: HealthStatus.Degraded,
                tags: ["ready", SecretProviderHealthCheck.Tag]));

        // Wrap provider with auditing decorator if enabled
        if (enableAuditing)
        {
            // Use decorator pattern by replacing the ISecretProvider registration
            services.DecorateSecretProvider();
        }

        return services;
    }

    /// <summary>
    /// Decorates the ISecretProvider with auditing functionality.
    /// </summary>
    private static void DecorateSecretProvider(this IServiceCollection services)
    {
        // Find and replace the existing ISecretProvider registration
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISecretProvider));
        if (descriptor is null)
        {
            throw new InvalidOperationException(
                "ISecretProvider must be registered before adding auditing decorator. " +
                "Call AddSecretProvider() before AddSecretObservability().");
        }

        services.Remove(descriptor);

        // Re-register with decorator wrapper
        services.AddSingleton<ISecretProvider>(sp =>
        {
            // Create the inner provider using the original factory
            ISecretProvider inner;
            if (descriptor.ImplementationInstance is not null)
            {
                inner = (ISecretProvider)descriptor.ImplementationInstance;
            }
            else if (descriptor.ImplementationFactory is not null)
            {
                inner = (ISecretProvider)descriptor.ImplementationFactory(sp);
            }
            else if (descriptor.ImplementationType is not null)
            {
                inner = (ISecretProvider)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);
            }
            else
            {
                throw new InvalidOperationException("Cannot resolve inner ISecretProvider for decoration.");
            }

            // Create the decorator
            var auditLogger = sp.GetRequiredService<ISecretAuditLogger>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingSecretProviderDecorator>>();
            var httpContextAccessor = sp.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();

            return new AuditingSecretProviderDecorator(inner, auditLogger, logger, httpContextAccessor);
        });
    }

    /// <summary>
    /// Adds only secret metrics without health checks or auditing.
    /// Useful when you want to register metrics separately from full observability.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSecretMetrics(this IServiceCollection services)
    {
        services.TryAddSingleton<SecretRefreshMetrics>();
        return services;
    }

    /// <summary>
    /// Adds secret provider health check only.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSecretHealthCheck(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .Add(new HealthCheckRegistration(
                name: SecretProviderHealthCheck.Name,
                factory: sp => new SecretProviderHealthCheck(
                    sp.GetRequiredService<ISecretProvider>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SecretProviderHealthCheck>>(),
                    sp.GetService<SecretRefreshMetrics>()),
                failureStatus: HealthStatus.Degraded,
                tags: [SecretProviderHealthCheck.Tag]));

        return services;
    }

    /// <summary>
    /// Adds the secret refresh background service.
    /// Only runs if the provider supports refresh and refresh is enabled.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSecretRefreshService(this IServiceCollection services)
    {
        services.AddHostedService<SecretRefreshService>();
        return services;
    }

    /// <summary>
    /// Adds rotation-aware HTTP client factory that supports credential rotation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRotationAwareHttpClientFactory(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind rotation options
        services.AddOptions<RotationOptions>()
            .Bind(configuration.GetSection(RotationOptions.SectionName));

        // Bind HTTP client credential options
        services.AddOptions<HttpClientCredentialOptions>()
            .Bind(configuration.GetSection(HttpClientCredentialOptions.SectionName));

        // Register the rotation-aware factory as singleton
        services.TryAddSingleton<RotationAwareHttpClientFactory>();

        // Also register as IHttpClientFactory for standard usage
        services.TryAddSingleton<IHttpClientFactory>(sp =>
            sp.GetRequiredService<RotationAwareHttpClientFactory>());

        return services;
    }

    /// <summary>
    /// Adds rotation-aware HTTP client factory with custom configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="configureRotation">Action to configure rotation options.</param>
    /// <param name="configureCredentials">Action to configure HTTP client credentials.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRotationAwareHttpClientFactory(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<RotationOptions>? configureRotation = null,
        Action<HttpClientCredentialOptions>? configureCredentials = null)
    {
        // Bind and configure rotation options
        var rotationBuilder = services.AddOptions<RotationOptions>()
            .Bind(configuration.GetSection(RotationOptions.SectionName));
        if (configureRotation is not null)
        {
            rotationBuilder.Configure(configureRotation);
        }

        // Bind and configure HTTP client credentials
        var credentialsBuilder = services.AddOptions<HttpClientCredentialOptions>()
            .Bind(configuration.GetSection(HttpClientCredentialOptions.SectionName));
        if (configureCredentials is not null)
        {
            credentialsBuilder.Configure(configureCredentials);
        }

        // Register the rotation-aware factory
        services.TryAddSingleton<RotationAwareHttpClientFactory>();
        services.TryAddSingleton<IHttpClientFactory>(sp =>
            sp.GetRequiredService<RotationAwareHttpClientFactory>());

        return services;
    }

    /// <summary>
    /// Adds rotation-aware DbContext factory that supports connection string rotation.
    /// </summary>
    /// <typeparam name="TContext">The type of DbContext to create.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="contextFactory">Factory function to create DbContext instances from options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRotationAwareDbContextFactory<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<DbContextOptions<TContext>, TContext> contextFactory)
        where TContext : DbContext
    {
        // Bind rotation options
        services.AddOptions<RotationOptions>()
            .Bind(configuration.GetSection(RotationOptions.SectionName));

        // Bind database connection options
        services.AddOptions<DatabaseConnectionOptions>()
            .Bind(configuration.GetSection(DatabaseConnectionOptions.SectionName));

        // Register the rotation-aware factory
        services.TryAddSingleton<IDbContextFactory<TContext>>(sp =>
        {
            var connectionOptions = sp.GetRequiredService<IOptionsMonitor<DatabaseConnectionOptions>>();
            var rotationOptions = sp.GetRequiredService<IOptionsMonitor<RotationOptions>>();
            var logger = sp.GetRequiredService<ILogger<RotationAwareDbContextFactory<TContext>>>();

            return new RotationAwareDbContextFactory<TContext>(
                contextFactory,
                connectionOptions,
                rotationOptions,
                logger);
        });

        return services;
    }

    /// <summary>
    /// Adds rotation-aware DbContext factory with explicit options configuration.
    /// </summary>
    /// <typeparam name="TContext">The type of DbContext to create.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="contextFactory">Factory function to create DbContext instances.</param>
    /// <param name="configureRotation">Action to configure rotation options.</param>
    /// <param name="configureConnection">Action to configure database connection options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRotationAwareDbContextFactory<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<DbContextOptions<TContext>, TContext> contextFactory,
        Action<RotationOptions>? configureRotation = null,
        Action<DatabaseConnectionOptions>? configureConnection = null)
        where TContext : DbContext
    {
        // Bind and configure rotation options
        var rotationBuilder = services.AddOptions<RotationOptions>()
            .Bind(configuration.GetSection(RotationOptions.SectionName));
        if (configureRotation is not null)
        {
            rotationBuilder.Configure(configureRotation);
        }

        // Bind and configure database connection options
        var connectionBuilder = services.AddOptions<DatabaseConnectionOptions>()
            .Bind(configuration.GetSection(DatabaseConnectionOptions.SectionName));
        if (configureConnection is not null)
        {
            connectionBuilder.Configure(configureConnection);
        }

        // Register the rotation-aware factory
        services.TryAddSingleton<IDbContextFactory<TContext>>(sp =>
        {
            var connectionOptions = sp.GetRequiredService<IOptionsMonitor<DatabaseConnectionOptions>>();
            var rotationOptions = sp.GetRequiredService<IOptionsMonitor<RotationOptions>>();
            var logger = sp.GetRequiredService<ILogger<RotationAwareDbContextFactory<TContext>>>();

            return new RotationAwareDbContextFactory<TContext>(
                contextFactory,
                connectionOptions,
                rotationOptions,
                logger);
        });

        return services;
    }

    /// <summary>
    /// Adds complete connection rotation support for both HTTP clients and DbContext.
    /// </summary>
    /// <typeparam name="TContext">The type of DbContext to create.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <param name="contextFactory">Factory function to create DbContext instances.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddConnectionRotation<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<DbContextOptions<TContext>, TContext> contextFactory)
        where TContext : DbContext
    {
        services.AddRotationAwareHttpClientFactory(configuration);
        services.AddRotationAwareDbContextFactory(configuration, contextFactory);
        return services;
    }
}
