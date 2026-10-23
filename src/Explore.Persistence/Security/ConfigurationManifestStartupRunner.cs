namespace Explore.Persistence.Security;

using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Preflight;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

internal sealed class ConfigurationManifestStartupRunner(
    IConfiguration configuration,
    IConfigurationManifestReader reader,
    IConfigurationManifestApplier applier,
    ILogger<ConfigurationManifestStartupRunner> logger)
    : IConfigurationManifestStartupRunner
{
    internal IConfiguration Configuration => configuration;
    public Task RunAsync(CancellationToken cancellationToken) =>
        ConfigurationManifestStartupInvocation.RunAsync(this, RunCoreAsync, cancellationToken);

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        var startup = new ConfigurationManifestReadOptions(
            ConfigurationManifestBootstrapDeployment.ParseMode(configuration[ConfigurationManifestBootstrapDeployment.ModeVariable]),
            configuration[ConfigurationManifestBootstrapDeployment.PathVariable]);
        if (startup.Mode == ConfigurationManifestMode.Bootstrap)
        {
            var original = await applier.ApplyOriginalStartupAsync(cancellationToken);
            if (original is null)
            {
                logger.LogInformation("No convention bootstrap manifest was discovered.");
                return;
            }
            if (!original.IsSuccess)
                throw new ConfigurationManifestStartupException(original.Id,
                    original.FailureCode ?? ConfigurationManifestApplicationFailureCodes.ApplyFailed);
            logger.LogInformation("Original configuration bootstrap operation {OperationId} completed.", original.Id);
            return;
        }
        ConfigurationManifestReadResult? source;
        try
        {
            source = await reader.ReadAsync(
                new ConfigurationManifestReadOptions(
                    startup.Mode,
                    startup.ConfiguredPath),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ConfigurationManifestIngestionException exception)
        {
            logger.LogCritical(
                "Configuration manifest startup discovery failed in mode {Mode} with code {FailureCode}.",
                startup.Mode,
                exception.FailureCode);
            throw;
        }

        if (source is null)
        {
            logger.LogInformation(
                "Configuration manifest startup completed without a discovered manifest in mode {Mode}.",
                startup.Mode);
            return;
        }

        string digestPrefix = source.Sha256Digest[
            ..Math.Min(source.Sha256Digest.Length, 12)];
        logger.LogInformation(
            "Configuration manifest startup discovered apiVersion {ApiVersion} in mode {Mode} with digest prefix {DigestPrefix}, {ByteLength} bytes, and {TenantCount} tenants.",
            source.Manifest.ApiVersion,
            source.Mode,
            digestPrefix,
            source.ByteLength,
            source.Manifest.Spec.Tenants.Count);

        var result = await applier.ApplyAsync(source, cancellationToken);
        if (!result.IsSuccess)
        {
            string failureCode = string.IsNullOrWhiteSpace(result.FailureCode)
                ? ConfigurationManifestApplicationFailureCodes.ApplyFailed
                : result.FailureCode;
            logger.LogCritical(
                "Configuration manifest startup operation {OperationId} failed with code {FailureCode}.",
                result.Id,
                failureCode);
            throw new ConfigurationManifestStartupException(
                result.Id,
                failureCode);
        }

        logger.LogInformation(
            "Configuration manifest startup operation {OperationId} completed in mode {Mode}.",
            result.Id,
            source.Mode);
    }
}

internal sealed class ConfigurationManifestPostMigrationSequence(
    IConfigurationManifestStartupRunner startupRunner)
    : IConfigurationManifestPostMigrationSequence
{
    public async Task RunAsync(
        Func<CancellationToken, Task> migrateAndSeed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(migrateAndSeed);
        await migrateAndSeed(cancellationToken);
        await startupRunner.RunAsync(cancellationToken);
    }
}
