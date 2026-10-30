using Microsoft.Extensions.Configuration;
using SecretKit.Core;
using SecretKit.Extensions.Configuration;

namespace Explore.ServiceDefaults.Secrets;

public sealed class SecretAuthorityLease : IDisposable
{
    private readonly SecretSnapshotLoader _loader;
    private readonly SecretSnapshotHandle _handle;
    private readonly SecretConfigurationExport _export;
    private readonly IConfigurationRoot _configuration;
    private int _disposed;

    internal SecretAuthorityLease(
        SecretSnapshotLoader loader,
        SecretSnapshotHandle handle,
        SecretConfigurationExport export,
        IConfigurationRoot configuration)
    {
        _loader = loader;
        _handle = handle;
        _export = export;
        _configuration = configuration;
    }

    /// <summary>Contains only the deliberately exported keys from this admitted generation.</summary>
    public IConfiguration Configuration
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _configuration;
        }
    }

    /// <summary>Returns an independent material owner pinned to this local generation.</summary>
    public SecretRead Read(string configurationKey)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _handle.Read(configurationKey);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        ((IDisposable)_configuration).Dispose();
        _export.Dispose();
        _handle.Dispose();
        _loader.Dispose();
    }

    public override string ToString() => "secret_authority_lease";
}
