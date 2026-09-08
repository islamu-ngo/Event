namespace Explore.Infrastructure.ConfigurationManifest;

using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Infrastructure.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

public static class ConfigurationManifestStartupServicesRegistration
{
    public static IServiceCollection AddConfigurationManifestStartup(
        this IServiceCollection services,
        IConfiguration configuration,
        ConfigurationManifestEffectDeliveryMode effectDeliveryMode =
            ConfigurationManifestEffectDeliveryMode.Immediate)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDataProtection();
        services.TryAddScoped<IEmailDeliveryDisableTokenService, EmailDeliveryDisableTokenService>();
        services.AddConfigurationManifestApplication(effectDeliveryMode);
        services.AddOptions<ConfigurationManifestOptions>()
            .Configure(options =>
            {
                options.Mode = ConfigurationManifestOptions.ParseMode(
                    configuration[ConfigurationManifestOptions.ModeEnvironmentVariable]);
                options.Path =
                    configuration[ConfigurationManifestOptions.PathEnvironmentVariable];
            })
            .ValidateOnStart();
        services.TryAddSingleton<
            IValidateOptions<ConfigurationManifestOptions>,
            ConfigurationManifestOptionsValidator>();
        services.TryAddSingleton<
            IConfigurationManifestReader,
            ConfigurationManifestReader>();
        services.TryAddScoped<
            IConfigurationManifestStartupRunner,
            ConfigurationManifestStartupRunner>();
        services.TryAddScoped<
            IConfigurationManifestPostMigrationSequence,
            ConfigurationManifestPostMigrationSequence>();

        return services;
    }
}
