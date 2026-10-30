using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Explore.ServiceDefaults.Secrets;

namespace Explore.ServiceDefaults.DataProtection;

public sealed record ExternalDataProtectionPartition(string Name);

public static class ExternalDataProtectionRegistration
{
    public static async ValueTask PrepareExternalDataProtectionAsync(
        this IServiceCollection services,
        SecretAuthorityAcquisition owner,
        string partition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(owner);
        _ = EnvironmentPrefix(partition);
        var source = new ExternalDataProtectionSource(owner, new(partition));
        await source.PrepareAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        services.Replace(ServiceDescriptor.Singleton<IExternalDataProtectionSource>(source));
        services.AddExternalDataProtection(partition);
    }

    public static string EnvironmentPrefix(string partition) => partition switch
    {
        "api" => "EVENT_DP_API",
        "bff" => "EVENT_DP_BFF",
        "combined" => "EVENT_DP_COMBINED",
        _ => throw new InvalidOperationException("data_protection_partition_invalid")
    };

    public static string ApplicationName(string partition)
    {
        _ = EnvironmentPrefix(partition);
        // API and Combined own the same durable ciphertext contracts. Preserve their discriminator
        // for operator-coordinated topology moves; the separate BFF must not inherit that authority.
        return partition == "bff" ? "islamu-event:bff:v1" : "islamu-event";
    }

    public static IServiceCollection AddExternalDataProtection(this IServiceCollection services, string partition)
    {
        _ = EnvironmentPrefix(partition);
        bool registered = services.Any(descriptor => descriptor.ServiceType == typeof(ExternalDataProtectionPartition));
        services.Replace(ServiceDescriptor.Singleton(new ExternalDataProtectionPartition(partition)));
        if (registered) return services;
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IExternalDataProtectionSource, ExternalDataProtectionSource>();
        services.TryAddSingleton(provider => ExternalDataProtectionProvider.Create(
            provider.GetRequiredService<IExternalDataProtectionSource>().Read(),
            provider.GetRequiredService<ExternalDataProtectionPartition>().Name,
            provider.GetRequiredService<TimeProvider>()));
        services.AddDataProtection();
        services.Replace(ServiceDescriptor.Singleton<IDataProtectionProvider>(provider =>
            provider.GetRequiredService<ExternalDataProtectionProvider>()));
        services.Replace(ServiceDescriptor.Singleton<IKeyManager, ExternalDataProtectionKeyManager>());
        // Also fence direct IKeyManager callers in the outer host: never permit its defaults to
        // generate/persist keys to a profile directory, database, or Redis.
        services.AddOptions<KeyManagementOptions>().PostConfigure<ExternalDataProtectionProvider>((options, _) =>
        {
            options.AutoGenerateKeys = false;
            options.XmlRepository = new ExternalDataProtectionXmlRepository([]);
            options.XmlEncryptor = null;
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ExternalDataProtectionStartup>());
        services.TryAddSingleton<ExternalDataProtectionReadiness>();
        services.AddHealthChecks().AddCheck<ExternalDataProtectionReadiness>(
            "external-data-protection", failureStatus: HealthStatus.Unhealthy, tags: ["ready", "data-protection"]);
        return services;
    }
}

internal sealed class ExternalDataProtectionStartup(ExternalDataProtectionProvider provider) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        provider.ProveActiveKey();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ExternalDataProtectionReadiness(
    IExternalDataProtectionSource source, ExternalDataProtectionProvider provider, TimeProvider clock) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var current = await source.ReadFreshAsync(cancellationToken).ConfigureAwait(false);
            if (current.AdmissionSha256 != provider.AdmissionSha256
                || ExternalDataProtectionInput.Hash(current.Admission) != provider.AdmissionSha256
                || ExternalDataProtectionInput.Hash(current.Ring) != provider.Admission.RingSha256
                || current.MinimumGeneration != provider.MinimumGeneration
                || current.MinimumGeneration > provider.Admission.Generation)
                throw ExternalDataProtectionRingAdmission.Invalid();
            provider.ProveActiveKey();
            var data = new Dictionary<string, object>
            {
                ["partition"] = provider.Admission.Partition,
                ["generation"] = provider.Admission.Generation,
                ["admissionSha256"] = provider.AdmissionSha256,
                ["keyCount"] = provider.KeyCount,
                ["admittedUntilUtc"] = provider.Admission.NotAfterUtc,
                ["activeExpiresUtc"] = provider.ActiveExpiresUtc
            };
            bool expiring = provider.ActiveExpiresUtc - clock.GetUtcNow() < TimeSpan.FromDays(7)
                || provider.Admission.NotAfterUtc - clock.GetUtcNow() < TimeSpan.FromHours(2);
            return expiring
                ? HealthCheckResult.Degraded("data_protection_renewal_required", data: data)
                : HealthCheckResult.Healthy("data_protection_admitted", data);
        }
        catch
        {
            provider.RejectUntilRestart();
            return HealthCheckResult.Unhealthy("data_protection_restart_required");
        }
    }
}
