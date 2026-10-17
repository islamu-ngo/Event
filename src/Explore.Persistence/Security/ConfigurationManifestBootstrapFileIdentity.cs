using System.Runtime.InteropServices;
using System.Text;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Microsoft.Win32.SafeHandles;

namespace Explore.Persistence.Security;

/// <summary>Linux statx identity for the original deployment file, including no-follow opening.</summary>
internal sealed class ConfigurationManifestBootstrapFileIdentity
{
    private readonly Identity original;
    private ConfigurationManifestBootstrapFileIdentity(Identity original) => this.original = original;

    internal static ConfigurationManifestBootstrapFileIdentity ReadPath(string path)
    {
        if (!OperatingSystem.IsLinux())
            throw new NotSupportedException("configuration_manifest_native_file_identity_not_supported");
        if (Stat(-100, Utf8(path), 0x100, 0x7ff, out var identity) != 0)
            throw Failure();
        RequireRegular(identity);
        return new(identity);
    }

    internal static FileStream Open(string path, ConfigurationManifestBootstrapFileIdentity expected)
    {
        // O_RDONLY | O_CLOEXEC | O_NOFOLLOW | O_NONBLOCK: replacing a regular file
        // with a FIFO must fail the identity check, not block startup in open().
        int descriptor = OpenNative(Utf8(path), 0x80000 | 0x20000 | 0x800);
        if (descriptor < 0) throw Failure();
        var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        try
        {
            var stream = new FileStream(handle, FileAccess.Read, 8192, isAsync: false);
            try { expected.RequireSame(path, stream); return stream; }
            catch { stream.Dispose(); throw; }
        }
        catch { handle.Dispose(); throw; }
    }

    internal void RequireSame(string path, FileStream stream)
    {
        if (Stat(-100, Utf8(path), 0x100, 0x7ff, out var named) != 0
            || StatHandle(stream.SafeFileHandle, [0], 0x1000, 0x7ff, out var opened) != 0)
            throw Failure();
        RequireRegular(named);
        RequireRegular(opened);
        if (!original.Same(named) || !original.Same(opened))
            throw new UnauthorizedAccessException("The selected bootstrap file identity or metadata changed.");
    }

    private static void RequireRegular(Identity identity)
    {
        if ((identity.Mode & 0xf000) != 0x8000 || (identity.Mask & 0x7ff) != 0x7ff)
            throw Failure();
    }

    private static ConfigurationManifestIngestionException Failure() =>
        new(ConfigurationManifestIngestionFailureCodes.FileNotRegular,
            "The original bootstrap source is not a stable regular file.");

    private static byte[] Utf8(string path) => Encoding.UTF8.GetBytes(path + '\0');

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct Identity
    {
        [FieldOffset(0)] internal uint Mask;
        [FieldOffset(16)] internal uint Links;
        [FieldOffset(20)] internal uint User;
        [FieldOffset(24)] internal uint Group;
        [FieldOffset(28)] internal ushort Mode;
        [FieldOffset(32)] internal ulong Inode;
        [FieldOffset(40)] internal ulong Size;
        [FieldOffset(96)] internal long Changed;
        [FieldOffset(104)] internal uint ChangedNanos;
        [FieldOffset(112)] internal long Modified;
        [FieldOffset(120)] internal uint ModifiedNanos;
        [FieldOffset(136)] internal uint DeviceMajor;
        [FieldOffset(140)] internal uint DeviceMinor;
        internal readonly bool Same(Identity other) =>
            Links == other.Links && User == other.User && Group == other.Group && Mode == other.Mode
            && Inode == other.Inode && Size == other.Size && Changed == other.Changed
            && ChangedNanos == other.ChangedNanos && Modified == other.Modified && ModifiedNanos == other.ModifiedNanos
            && DeviceMajor == other.DeviceMajor && DeviceMinor == other.DeviceMinor;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenNative(byte[] path, int flags);
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Stat(int directory, byte[] path, int flags, uint mask, out Identity identity);
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int StatHandle(SafeFileHandle directory, byte[] path, int flags, uint mask, out Identity identity);
}
