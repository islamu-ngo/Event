using System.Collections.Immutable;
using System.Security.Cryptography;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Responses;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Explore.Persistence.Security;

/// <summary>
/// Retains a real selected regular file inside the original post-migration invocation.
/// A posted parsed manifest, configured administrator or setup ticket is not this source.
/// </summary>
internal sealed class ConfigurationManifestBootstrapOrigin(
    IConfiguration configuration, IHttpContextAccessor requests, IHostApplicationLifetime lifetime)
    : IConfigurationManifestBootstrapSourceAuthority
{
    private Source? active;
    internal IConfiguration Configuration => configuration;

    internal async Task<Source?> CaptureAsync(CancellationToken token)
    {
        var invocation = ConfigurationManifestStartupInvocation.RequireCurrent();
        string? mode = configuration[ConfigurationManifestBootstrapDeployment.ModeVariable];
        string? configuredPath = configuration[ConfigurationManifestBootstrapDeployment.PathVariable];
        if (!ReferenceEquals(invocation.Configuration, configuration) || requests.HttpContext is not null || lifetime.ApplicationStarted.IsCancellationRequested
            || ConfigurationManifestBootstrapDeployment.ParseMode(mode) != ConfigurationManifestMode.Bootstrap)
            throw new UnauthorizedAccessException("Bootstrap requires its original deployment-selected mode and file.");
        bool explicitPath = !string.IsNullOrWhiteSpace(configuredPath);
        string path = explicitPath ? configuredPath! : ConfigurationManifestBootstrapDeployment.ConventionPath;
        if (!Path.IsPathFullyQualified(path))
            throw Ingestion(ConfigurationManifestIngestionFailureCodes.PathInvalid);
        if (Directory.Exists(path))
            throw Ingestion(ConfigurationManifestIngestionFailureCodes.FileNotRegular);
        if (!File.Exists(path))
        {
            if (!explicitPath) return null;
            throw Ingestion(ConfigurationManifestIngestionFailureCodes.FileMissing);
        }
        var identity = ConfigurationManifestBootstrapFileIdentity.ReadPath(path);
        FileStream? stream = null;
        try
        {
            stream = ConfigurationManifestBootstrapFileIdentity.Open(path, identity);
            byte[] content = await ReadBoundedAsync(stream, token);
            identity.RequireSame(path, stream);
            ConfigurationManifestJsonScanner.Validate(content);
            var source = new Source(this, invocation, mode, configuredPath, path, stream,
                new(content.ToImmutableArray(), Convert.ToHexStringLower(SHA256.HashData(content))), identity);
            if (Interlocked.CompareExchange(ref active, source, null) is not null)
                throw new UnauthorizedAccessException("A bootstrap invocation cannot replace its original file.");
            stream = null;
            return source;
        }
        finally
        {
            if (stream is not null) await stream.DisposeAsync();
        }
    }

    public ConfigurationManifestBootstrapFile Claim(IConfigurationManifestBootstrapSource source)
    {
        var original = RequireIssued(source);
        if (Interlocked.Exchange(ref original.Claimed, 1) != 0)
            throw new UnauthorizedAccessException("An original bootstrap file belongs to one physical attempt.");
        return original.File;
    }

    public async Task RequireOriginalAsync(IConfigurationManifestBootstrapSource source, CancellationToken cancellationToken)
    {
        var original = RequireIssued(source);
        original.Identity.RequireSame(original.Path, original.Stream);
        original.Stream.Position = 0;
        byte[] current = await ReadBoundedAsync(original.Stream, cancellationToken);
        if (!current.AsSpan().SequenceEqual(original.File.Content.AsSpan()))
            throw new UnauthorizedAccessException("The original deployment manifest changed.");
        original.Identity.RequireSame(original.Path, original.Stream);
        RequireIssued(source);
    }

    private Source RequireIssued(IConfigurationManifestBootstrapSource source)
    {
        if (source is not Source original || !ReferenceEquals(original.Owner, this)
            || !ReferenceEquals(active, original) || !original.Active
            || !ReferenceEquals(ConfigurationManifestStartupInvocation.RequireCurrent(), original.Invocation)
            || requests.HttpContext is not null || lifetime.ApplicationStarted.IsCancellationRequested
            || configuration[ConfigurationManifestBootstrapDeployment.ModeVariable] != original.Mode
            || configuration[ConfigurationManifestBootstrapDeployment.PathVariable] != original.ConfiguredPath)
            throw new UnauthorizedAccessException("The original active post-migration bootstrap source is required.");
        return original;
    }

    private static async Task<byte[]> ReadBoundedAsync(FileStream stream, CancellationToken token)
    {
        if (stream.Length > ConfigurationManifestBootstrapDeployment.MaximumBytes)
            throw Ingestion(ConfigurationManifestIngestionFailureCodes.TooLarge);
        using var content = new MemoryStream();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int count = await stream.ReadAsync(buffer, token);
            if (count == 0) break;
            if (content.Length + count > ConfigurationManifestBootstrapDeployment.MaximumBytes)
                throw Ingestion(ConfigurationManifestIngestionFailureCodes.TooLarge);
            await content.WriteAsync(buffer.AsMemory(0, count), token);
        }
        if (content.Length == 0) throw Ingestion(ConfigurationManifestIngestionFailureCodes.Empty);
        return content.ToArray();
    }

    private static ConfigurationManifestIngestionException Ingestion(string code) =>
        new(code, "The selected bootstrap manifest is not an admissible regular source.");

    internal sealed class Source(ConfigurationManifestBootstrapOrigin owner, ConfigurationManifestStartupInvocation invocation,
        string? mode, string? configuredPath, string path, FileStream stream, ConfigurationManifestBootstrapFile file,
        ConfigurationManifestBootstrapFileIdentity identity)
        : IConfigurationManifestBootstrapSource, IAsyncDisposable
    {
        internal ConfigurationManifestBootstrapOrigin Owner => owner;
        internal ConfigurationManifestStartupInvocation Invocation => invocation;
        internal string? Mode => mode;
        internal string? ConfiguredPath => configuredPath;
        internal string Path => path;
        internal FileStream Stream => stream;
        internal ConfigurationManifestBootstrapFile File => file;
        internal ConfigurationManifestBootstrapFileIdentity Identity => identity;
        internal int Claimed;
        internal volatile bool Active = true;
        public async ValueTask DisposeAsync()
        {
            Active = false;
            Interlocked.CompareExchange(ref owner.active, null, this);
            await stream.DisposeAsync();
        }
    }
}

internal sealed class ConfigurationManifestBootstrapExecutor(
    ConfigurationManifestBootstrapOrigin origin, IConfigurationManifestBootstrapOwnerFactory owner)
    : IConfigurationManifestBootstrapExecutor
{
    public async Task<BaseCommandResponse<Guid>?> ApplyOriginalStartupAsync(CancellationToken cancellationToken)
    {
        await using var source = await origin.CaptureAsync(cancellationToken);
        if (source is null)
            return null;
        return await owner.ApplyAsync(source, cancellationToken);
    }
}
