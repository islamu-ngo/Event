using System.Collections.Immutable;
using Explore.Application.Responses;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using Explore.Application.Features.ConfigurationManifest.Preflight;
using Explore.Application.Features.ConfigurationManifest.Ingestion;

namespace Explore.Application.Features.ConfigurationManifest.Application;

public static class ConfigurationManifestBootstrapDeployment
{
    public const string ModeVariable = "CONFIGURATION_MANIFEST_MODE";
    public const string PathVariable = "CONFIGURATION_MANIFEST_PATH";
    public const string ConventionPath = "/etc/islamu-event/bootstrap/configuration-manifest.json";
    public const int MaximumBytes = 4_194_304;
    public static ConfigurationManifestMode ParseMode(string? value) => value switch
    {
        null or "" or "Off" => ConfigurationManifestMode.Off,
        "ValidateOnly" => ConfigurationManifestMode.ValidateOnly,
        "Bootstrap" => ConfigurationManifestMode.Bootstrap,
        _ => (ConfigurationManifestMode)(-1)
    };
}

public interface IConfigurationManifestStartupRunner
{
    Task RunAsync(CancellationToken cancellationToken);
}

public interface IConfigurationManifestPostMigrationSequence
{
    Task RunAsync(Func<CancellationToken, Task> migrateAndSeed, CancellationToken cancellationToken);
}

public sealed class ConfigurationManifestStartupException(Guid? operationId, string failureCode)
    : Exception("Configuration manifest startup failed. Inspect the operation id and stable failure code.")
{
    public Guid? OperationId { get; } = operationId;
    public string FailureCode { get; } = failureCode;
}

/// <summary>Opaque original post-migration file custody; public implementations grant nothing.</summary>
public interface IConfigurationManifestBootstrapSource;

public sealed record ConfigurationManifestBootstrapFile(ImmutableArray<byte> Content, string Digest)
{
    public override string ToString() => nameof(ConfigurationManifestBootstrapFile);
}

public interface IConfigurationManifestBootstrapSourceAuthority
{
    ConfigurationManifestBootstrapFile Claim(IConfigurationManifestBootstrapSource source);
    Task RequireOriginalAsync(IConfigurationManifestBootstrapSource source, CancellationToken cancellationToken);
}

/// <summary>Only the original startup adapter selects deployment file authority.</summary>
public interface IConfigurationManifestBootstrapExecutor
{
    Task<BaseCommandResponse<Guid>?> ApplyOriginalStartupAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A distinct nonhuman Global owner. Neither a human import nor an ordinary Settings
/// preparation can implement the actual issuer's source proof.
/// </summary>
public interface IConfigurationManifestBootstrapOwnerFactory
{
    Task<ConfigurationManifestPreflightResult> ValidateAsync(ConfigurationManifestApplyPlan plan,
        CancellationToken cancellationToken);
    Task<BaseCommandResponse<Guid>> ApplyAsync(IConfigurationManifestBootstrapSource source,
        CancellationToken cancellationToken);
}
