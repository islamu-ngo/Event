using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace ISLAMU.Event.Setup.Artifacts;

/// <summary>
/// Linux-only descriptor-relative, create-only publication. Private staging is
/// anonymous: no replaceable temporary pathname ever identifies its bytes.
/// </summary>
[SupportedOSPlatform("linux")]
internal static partial class UnixProtectedArtifactWriter
{
    private const int CurrentDirectory = -100;
    private const int DirectoryNoFollow = 0x10000 | 0x20000 | 0x80000;
    private const int AnonymousReadWrite = 0x410000 | 0x80000 | 2;
    private const int EmptyPath = 0x1000;
    private const int NoFollow = 0x100;
    private const int FollowDescriptorLink = 0x400;
    private const uint BasicStats = 0x7ff;
    private const uint OwnerOnly = 0x180;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Staging stream ownership is transferred to the returned ProtectedArtifactPreparation which disposes it.")]
    internal static async Task<ProtectedArtifactPreparation> PrepareAsync(
        string targetPath, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        SafeFileHandle? parent = null;
        FileStream? staged = null;
        try
        {
            string target = Path.GetFullPath(targetPath);
            string directory = Path.GetDirectoryName(target) ?? string.Empty;
            string name = Path.GetFileName(target);
            if (name.Length == 0)
                return ProtectedArtifactPreparation.Rejected(ProtectedArtifactStatus.UnsafeTarget);
            parent = OpenDirectoryChain(directory);
            if (parent is null)
                return ProtectedArtifactPreparation.Rejected(ProtectedArtifactStatus.UnsafeTarget);
            FileIdentity initialParent = Inspect(parent);
            ProtectedArtifactStatus? existing = ExistingTarget(parent, name);
            if (existing.HasValue)
                return ProtectedArtifactPreparation.Rejected(existing.Value);

            int descriptor = OpenAt(parent, ".", AnonymousReadWrite, OwnerOnly);
            if (descriptor < 0)
                return ProtectedArtifactPreparation.Rejected(ProtectedArtifactStatus.IoFailure);

            SafeFileHandle? handle = new((IntPtr)descriptor, ownsHandle: true);
            try
            {
                if (ChangeMode(handle, OwnerOnly) != 0 || !IsPrivateFile(Inspect(handle), 0))
                    return ProtectedArtifactPreparation.Rejected(ProtectedArtifactStatus.PermissionDenied);

                staged = new FileStream(handle, FileAccess.ReadWrite);
                handle = null;
            }
            finally
            {
                handle?.Dispose();
            }

            await staged.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await staged.FlushAsync(cancellationToken).ConfigureAwait(false);
            staged.Flush(flushToDisk: true);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsPrivateFile(Inspect(staged.SafeFileHandle), bytes.Length))
                return ProtectedArtifactPreparation.Rejected(ProtectedArtifactStatus.PermissionDenied);

            SafeFileHandle ownedParent = parent;
            FileStream ownedStaging = staged;
            int length = bytes.Length;
            var preparation = new ProtectedArtifactPreparation(token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(Commit(directory, name, ownedParent, initialParent, ownedStaging, length));
            }, () =>
            {
                ownedStaging.Dispose();
                ownedParent.Dispose();
            });
            parent = null;
            staged = null;
            return preparation;
        }
        catch (UnauthorizedAccessException)
        {
            return ProtectedArtifactPreparation.Rejected(ProtectedArtifactStatus.PermissionDenied);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            return ProtectedArtifactPreparation.Rejected(ProtectedArtifactStatus.IoFailure);
        }
        catch (ArgumentException)
        {
            return ProtectedArtifactPreparation.Rejected(ProtectedArtifactStatus.InvalidRequest);
        }
        finally
        {
            if (staged is not null)
                await staged.DisposeAsync().ConfigureAwait(false);
            parent?.Dispose();
        }
    }

    private static ProtectedArtifactStatus Commit(
        string directory, string name, SafeFileHandle parent, FileIdentity initialParent,
        FileStream staged, int length)
    {
        try
        {
            using SafeFileHandle? current = OpenDirectoryChain(directory);
            if (current is null)
                return ProtectedArtifactStatus.UnsafeTarget;
            if (!Inspect(current).SameFile(initialParent))
                return ProtectedArtifactStatus.TargetChanged;
            if (!IsTrustedDirectory(Inspect(parent), final: true))
                return ProtectedArtifactStatus.UnsafeTarget;
            FileIdentity prepared = Inspect(staged.SafeFileHandle);
            if (!IsPrivateFile(prepared, length))
                return ProtectedArtifactStatus.PermissionDenied;

            // linkat follows only our procfs descriptor, not an operator pathname.
            // The kernel installs that inode iff the destination is still absent.
            int linkResult;
            int linkError = 0;
            bool addedRef = false;
            staged.SafeFileHandle.DangerousAddRef(ref addedRef);
            try
            {
                string descriptorPath = "/proc/self/fd/" + staged.SafeFileHandle.DangerousGetHandle()
                    .ToInt64().ToString(CultureInfo.InvariantCulture);
                linkResult = LinkAt(CurrentDirectory, descriptorPath, parent, name, FollowDescriptorLink);
                if (linkResult != 0)
                    linkError = Marshal.GetLastPInvokeError();
            }
            finally
            {
                if (addedRef)
                    staged.SafeFileHandle.DangerousRelease();
            }

            if (linkResult != 0)
                return linkError == 17
                    ? ProtectedArtifactStatus.TargetChanged : ProtectedArtifactStatus.IoFailure;

            if (StatAt(parent, name, NoFollow, BasicStats, out FileIdentity installed) != 0
                || !installed.SameFile(prepared) || !IsPrivateFile(installed, length))
                return ProtectedArtifactStatus.TargetChanged;
            return ProtectedArtifactStatus.Written;
        }
        catch (IOException)
        {
            return ProtectedArtifactStatus.IoFailure;
        }
    }

    private static SafeFileHandle? OpenDirectoryChain(string directory)
    {
        if (!Path.IsPathFullyQualified(directory))
            return null;
        int descriptor = OpenRoot("/", DirectoryNoFollow, 0);
        if (descriptor < 0)
            return null;
        SafeFileHandle? current = new((IntPtr)descriptor, ownsHandle: true);
        try
        {
            if (!IsTrustedDirectory(Inspect(current), final: directory == "/"))
                return null;
            string[] segments = directory.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < segments.Length; index++)
            {
                int next = OpenAt(current, segments[index], DirectoryNoFollow, 0);
                if (next < 0)
                    return null;
                SafeFileHandle toDispose = current;
                current = new SafeFileHandle((IntPtr)next, ownsHandle: true);
                toDispose.Dispose();
                if (!IsTrustedDirectory(Inspect(current), final: index == segments.Length - 1))
                    return null;
            }
            SafeFileHandle result = current;
            current = null;
            return result;
        }
        finally
        {
            current?.Dispose();
        }
    }

    private static readonly uint CurrentEffectiveUserId = GetEffectiveUserId();

    private static uint GetEffectiveUserId() => EffectiveUserId();

    private static bool IsTrustedDirectory(FileIdentity identity, bool final) =>
        (identity.Mode & 0xf000) == 0x4000
        && (identity.UserId == 0 || identity.UserId == CurrentEffectiveUserId)
        && ((identity.Mode & 0x12) == 0
            || (!final && identity.UserId == 0 && (identity.Mode & 0x200) != 0));

    private static bool IsPrivateFile(FileIdentity identity, int length) =>
        identity.UserId == CurrentEffectiveUserId
        && identity.Mode == (0x8000 | OwnerOnly)
        && identity.Size == (ulong)length;

    private static FileIdentity Inspect(SafeFileHandle handle)
    {
        if (StatAt(handle, string.Empty, EmptyPath, BasicStats, out FileIdentity identity) != 0
            || (identity.Mask & 0x31b) != 0x31b)
            throw new IOException("protected-output-stat-failed");
        return identity;
    }

    private static ProtectedArtifactStatus? ExistingTarget(SafeFileHandle parent, string name)
    {
        if (StatAt(parent, name, NoFollow, BasicStats, out FileIdentity target) == 0)
            return (target.Mode & 0xf000) == 0x8000
                ? ProtectedArtifactStatus.TargetExists : ProtectedArtifactStatus.UnsafeTarget;
        return Marshal.GetLastPInvokeError() == 2 ? null : ProtectedArtifactStatus.UnsafeTarget;
    }

    // statx has a fixed, architecture-independent Linux ABI.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct FileIdentity
    {
        [FieldOffset(0)] internal uint Mask;
        [FieldOffset(20)] internal uint UserId;
        [FieldOffset(28)] internal ushort Mode;
        [FieldOffset(32)] internal ulong Inode;
        [FieldOffset(40)] internal ulong Size;
        [FieldOffset(136)] internal uint DeviceMajor;
        [FieldOffset(140)] internal uint DeviceMinor;

        internal readonly bool SameFile(FileIdentity other) =>
            Inode == other.Inode && DeviceMajor == other.DeviceMajor && DeviceMinor == other.DeviceMinor;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenRoot(string path, int flags, uint mode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenAt(SafeFileHandle directory, string path, int flags, uint mode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int StatAt(SafeFileHandle directory, string path, int flags, uint mask, out FileIdentity identity);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "linkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinkAt(int sourceDirectory, string source, SafeFileHandle directory, string target, int flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "fchmod", SetLastError = true)]
    private static partial int ChangeMode(SafeFileHandle file, uint mode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint EffectiveUserId();
}
