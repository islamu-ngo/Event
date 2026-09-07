using Explore.Application.Contracts.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Explore.API.HealthChecks;

public sealed class StorageReadinessHealthCheck(
    IStoragePolicyResolver storagePolicyResolver,
    IFileStorageProviderResolver providerResolver) : IHealthCheck
{
    private const string PolicyFailureCode = "storage_policy_resolution_failed";
    private const string ProviderFailureCode = "storage_provider_resolution_failed";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var policy = await storagePolicyResolver.ResolveAsync(null, cancellationToken);
            var provider = providerResolver.GetRequired(policy.Provider);
            var status = await provider.TestAsync(cancellationToken);
            var data = new Dictionary<string, object>
            {
                ["provider"] = status.Provider,
                ["selectedProvider"] = policy.Provider,
                ["available"] = status.IsAvailable,
                ["supportsServerSideStreaming"] = status.SupportsServerSideStreaming,
                ["supportsBrowserDirectUpload"] = status.SupportsBrowserDirectUpload,
                ["tenantOverridesAllowed"] = policy.TenantOverridesAllowed,
                ["tenantStorageLocked"] = policy.TenantStorageLocked
            };

            if (!string.IsNullOrWhiteSpace(status.FailureCode))
            {
                data["failureCode"] = status.FailureCode;
            }

            return status.IsAvailable
                ? HealthCheckResult.Healthy(status.Message ?? "Selected storage provider is available.", data)
                : HealthCheckResult.Unhealthy(status.Message ?? "Selected storage provider is unavailable.", data: data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex)
        {
            return HealthCheckResult.Unhealthy(
                "Selected storage provider could not be resolved.",
                data: CreateFailureData(ProviderFailureCode, ex.GetType().Name));
        }
        catch (Exception ex) when (ex is ArgumentException or SystemException)
        {
            return HealthCheckResult.Unhealthy(
                "Storage policy could not be resolved.",
                data: CreateFailureData(PolicyFailureCode, ex.GetType().Name));
        }
    }

    private static Dictionary<string, object> CreateFailureData(string failureCode, string message)
        => new()
        {
            ["available"] = false,
            ["failureCode"] = failureCode,
            ["reason"] = message
        };
}
