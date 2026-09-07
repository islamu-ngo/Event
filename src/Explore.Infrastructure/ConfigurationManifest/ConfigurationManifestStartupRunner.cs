namespace Explore.Infrastructure.ConfigurationManifest;

using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Preflight;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public interface IConfigurationManifestStartupRunner
{
    Task RunAsync(CancellationToken cancellationToken);
}

public sealed class ConfigurationManifestStartupRunner(
    IOptions<ConfigurationManifestOptions> options,
    IConfigurationManifestReader reader,
    IConfigurationManifestApplier applier,
    ILogger<ConfigurationManifestStartupRunner> logger)
    : IConfigurationManifestStartupRunner
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ConfigurationManifestOptions startup = options.Value;
        ConfigurationManifestReadResult? source;
        try
        {
            source = await reader.ReadAsync(
                new ConfigurationManifestReadOptions(
                    startup.Mode,
                    startup.Path),
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

public sealed class ConfigurationManifestStartupException(
    Guid? operationId,
    string failureCode)
    : Exception(
        "Configuration manifest startup failed. Inspect the operation id and stable failure code.")
{
    public Guid? OperationId { get; } = operationId;

    public string FailureCode { get; } = failureCode;
}

public interface IConfigurationManifestPostMigrationSequence
{
    Task RunAsync(
        Func<CancellationToken, Task> migrateAndSeed,
        CancellationToken cancellationToken);
}

public sealed class ConfigurationManifestPostMigrationSequence(
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
