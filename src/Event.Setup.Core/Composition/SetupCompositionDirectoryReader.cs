namespace ISLAMU.Event.Setup.Core.Composition;

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static class SetupCompositionDirectoryReader
{
    internal static async ValueTask<CompositionMap> ReadAsync(
        SetupCompositionDirectorySource source, SetupCompositionLimits limits,
        ISetupCompositionPublicationCommitBarrier barrier, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            throw new SetupCompositionException(SetupCompositionFailureCode.UnsupportedFilesystem);

        string root = Path.GetFullPath(source.RootDirectory);
        if (!Path.IsPathFullyQualified(root))
            throw new SetupCompositionException(SetupCompositionFailureCode.UnsafePath);

        DirectorySnapshot first = await CaptureAsync(root, limits, cancellationToken).ConfigureAwait(false);
        var budget = new CompositionBudget(limits);
        var fragments = new List<CompositionMap>(first.Files.Count);
        foreach (SnapshotFile file in first.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompositionMap fragment = file.Format == FragmentFormat.Json
                ? SetupCompositionNormalizer.ParseJson(file.Bytes, limits, cancellationToken, budget)
                : SetupCompositionYamlParser.Parse(file.Bytes, limits, cancellationToken, budget);
            if (budget.Nodes > limits.AggregateDirectoryNodes)
                throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
            fragments.Add(fragment);
        }

        CompositionMap merged = SetupCompositionNormalizer.Merge(fragments, limits, cancellationToken);
        await barrier.AwaitPublicationCommitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        DirectorySnapshot second;
        try
        {
            second = await CaptureAsync(root, limits, cancellationToken).ConfigureAwait(false);
        }
        catch (SetupCompositionException)
        {
            throw new SetupCompositionException(SetupCompositionFailureCode.SourceChanged);
        }
        if (!first.IdentityEquals(second))
            throw new SetupCompositionException(SetupCompositionFailureCode.SourceChanged);
        return merged;
    }

    private static async ValueTask<DirectorySnapshot> CaptureAsync(
        string root, SetupCompositionLimits limits, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NativeIdentity rootIdentity = NativeIdentity.ReadPath(root);
        if (!rootIdentity.IsDirectory || rootIdentity.IsLink || rootIdentity.IsSpecial)
            throw new SetupCompositionException(SetupCompositionFailureCode.UnsafeEntry);

        using SafeFileHandle rootHandle = NativeMethods.OpenRoot(root);
        NativeIdentity openedRoot = NativeIdentity.ReadHandle(rootHandle);
        if (!rootIdentity.StableEquals(openedRoot))
            throw new SetupCompositionException(SetupCompositionFailureCode.SourceChanged);

        var files = new List<SnapshotFile>();
        var directories = new List<SnapshotDirectory>();
        var pathIdentities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        pending.Enqueue(string.Empty);
        int aggregateBytes = 0;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativeDirectory = pending.Dequeue();
            string absoluteDirectory = relativeDirectory.Length == 0
                ? root : Path.Combine(root, relativeDirectory);
            string[] entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(absoluteDirectory)
                    .Order(StringComparer.Ordinal).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new SetupCompositionException(SetupCompositionFailureCode.UnsafeEntry);
            }

            if (entries.Length > limits.EntriesPerDirectory)
                throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
            if (checked(directories.Count + 1) > limits.Directories)
                throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);

            NativeIdentity directoryIdentity = relativeDirectory.Length == 0
                ? openedRoot : NativeMethods.OpenIdentity(rootHandle, relativeDirectory, directory: true);
            directories.Add(new SnapshotDirectory(relativeDirectory, directoryIdentity));

            foreach (string entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(root, entry);
                ValidateRelativePath(relative, limits, pathIdentities);
                string name = Path.GetFileName(relative);
                if (IsRejectedName(name))
                    throw new SetupCompositionException(SetupCompositionFailureCode.UnsafePath);

                NativeIdentity pathIdentity = NativeIdentity.ReadPath(entry);
                if (pathIdentity.IsLink || pathIdentity.IsSpecial)
                    throw new SetupCompositionException(SetupCompositionFailureCode.UnsafeEntry);
                if (pathIdentity.IsDirectory)
                {
                    NativeIdentity opened = NativeMethods.OpenIdentity(rootHandle, relative, directory: true);
                    if (!pathIdentity.StableEquals(opened))
                        throw new SetupCompositionException(SetupCompositionFailureCode.SourceChanged);
                    pending.Enqueue(relative);
                    continue;
                }
                if (!pathIdentity.IsRegularFile || pathIdentity.LinkCount != 1)
                    throw new SetupCompositionException(SetupCompositionFailureCode.UnsafeEntry);
                if (checked(files.Count + 1) > limits.Files)
                    throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);

                FragmentFormat format = Extension(relative);
                SnapshotFile file = await ReadFileAsync(
                    rootHandle, relative, format, pathIdentity, limits, cancellationToken).ConfigureAwait(false);
                try { aggregateBytes = checked(aggregateBytes + file.Bytes.Length); }
                catch (OverflowException) { throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded); }
                if (aggregateBytes > limits.AggregateDirectoryBytes
                    || aggregateBytes > limits.AggregateSourceBytes)
                    throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
                files.Add(file);
            }
        }

        NativeIdentity finalRoot = NativeIdentity.ReadPath(root);
        if (!rootIdentity.StableEquals(finalRoot))
            throw new SetupCompositionException(SetupCompositionFailureCode.SourceChanged);
        return new DirectorySnapshot(
            rootIdentity,
            directories.OrderBy(static item => item.RelativePath, StringComparer.Ordinal).ToArray(),
            files.OrderBy(static item => item.RelativePath, StringComparer.Ordinal).ToArray());
    }

    private static async ValueTask<SnapshotFile> ReadFileAsync(
        SafeFileHandle rootHandle, string relative, FragmentFormat format,
        NativeIdentity discovered, SetupCompositionLimits limits, CancellationToken cancellationToken)
    {
        using SafeFileHandle handle = NativeMethods.OpenRelative(rootHandle, relative, directory: false);
        NativeIdentity before = NativeIdentity.ReadHandle(handle);
        if (!discovered.Equals(before) || !before.IsRegularFile || before.LinkCount != 1)
            throw new SetupCompositionException(SetupCompositionFailureCode.SourceChanged);
        if (before.Size < 0 || before.Size > limits.PerFileBytes || before.Size > int.MaxValue)
            throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);

        byte[] bytes = new byte[(int)before.Size];
        NativeIdentity after;
        using (var stream = new FileStream(handle, FileAccess.Read, bufferSize: 16_384, isAsync: false))
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (stream.ReadByte() != -1)
                throw new SetupCompositionException(SetupCompositionFailureCode.SourceChanged);
            after = NativeIdentity.ReadHandle(handle);
        }

        if (!before.StableEquals(after))
            throw new SetupCompositionException(SetupCompositionFailureCode.SourceChanged);
        byte[] digest = SHA256.HashData(bytes);
        return new SnapshotFile(relative, format, before, bytes, digest);
    }

    private static void ValidateRelativePath(
        string relative, SetupCompositionLimits limits, Dictionary<string, string> identities)
    {
        if (relative.Length == 0 || relative.Length > limits.RelativePathCharacters
            || Path.IsPathFullyQualified(relative) || Path.IsPathRooted(relative)
            || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal)
            || relative.Contains("/../", StringComparison.Ordinal))
            throw new SetupCompositionException(SetupCompositionFailureCode.UnsafePath);
        int depth = relative.Count(static character => character == Path.DirectorySeparatorChar) + 1;
        if (depth > limits.PathDepth)
            throw new SetupCompositionException(SetupCompositionFailureCode.LimitExceeded);
        string normalized = relative.Normalize(NormalizationForm.FormC);
        if (identities.TryGetValue(normalized, out _))
            throw new SetupCompositionException(SetupCompositionFailureCode.PathCollision);
        identities.Add(normalized, relative);
    }

    private static bool IsRejectedName(string name)
    {
        if (name.Length == 0 || name[0] == '.' || name.EndsWith('~'))
            return true;
        string folded = name.ToLowerInvariant();
        if (folded.EndsWith(".tmp", StringComparison.Ordinal)
            || folded.EndsWith(".temp", StringComparison.Ordinal)
            || folded.EndsWith(".bak", StringComparison.Ordinal)
            || folded.EndsWith(".backup", StringComparison.Ordinal))
            return true;
        string stem = Path.GetFileNameWithoutExtension(name);
        return stem.Equals("con", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("prn", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("aux", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("nul", StringComparison.OrdinalIgnoreCase)
            || stem.Length == 4 && (stem.StartsWith("com", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("lpt", StringComparison.OrdinalIgnoreCase))
                && stem[3] is >= '1' and <= '9';
    }

    private static FragmentFormat Extension(string relative) => Path.GetExtension(relative) switch
    {
        ".json" => FragmentFormat.Json,
        ".yaml" or ".yml" => FragmentFormat.Yaml,
        _ => throw new SetupCompositionException(SetupCompositionFailureCode.UnsafePath)
    };

    private enum FragmentFormat { Json, Yaml }

    private sealed record SnapshotDirectory(string RelativePath, NativeIdentity Identity);
    private sealed record SnapshotFile(
        string RelativePath, FragmentFormat Format, NativeIdentity Identity, byte[] Bytes, byte[] Digest);

    private sealed record DirectorySnapshot(
        NativeIdentity Root, IReadOnlyList<SnapshotDirectory> Directories, IReadOnlyList<SnapshotFile> Files)
    {
        internal bool IdentityEquals(DirectorySnapshot other)
        {
            if (!Root.StableEquals(other.Root)
                || Directories.Count != other.Directories.Count || Files.Count != other.Files.Count)
                return false;
            for (int index = 0; index < Directories.Count; index++)
            {
                if (Directories[index].RelativePath != other.Directories[index].RelativePath
                    || !Directories[index].Identity.StableEquals(other.Directories[index].Identity))
                    return false;
            }
            for (int index = 0; index < Files.Count; index++)
            {
                SnapshotFile left = Files[index];
                SnapshotFile right = other.Files[index];
                if (left.RelativePath != right.RelativePath || left.Format != right.Format
                    || !left.Identity.StableEquals(right.Identity)
                    || !left.Digest.AsSpan().SequenceEqual(right.Digest))
                    return false;
            }
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeIdentity(
        ulong Device, ulong Inode, ulong LinkCount, uint Mode, uint UserId, uint GroupId,
        int Padding, ulong DeviceType, long Size, long BlockSize, long Blocks,
        long AccessSeconds, long AccessNanoseconds, long ModifySeconds, long ModifyNanoseconds,
        long ChangeSeconds, long ChangeNanoseconds, long Reserved0, long Reserved1, long Reserved2)
    {
        private const uint TypeMask = 0xF000;
        private const uint Regular = 0x8000;
        private const uint Directory = 0x4000;
        private const uint SymbolicLink = 0xA000;

        internal bool IsRegularFile => (Mode & TypeMask) == Regular;
        internal bool IsDirectory => (Mode & TypeMask) == Directory;
        internal bool IsLink => (Mode & TypeMask) == SymbolicLink;
        internal bool IsSpecial => !IsRegularFile && !IsDirectory && !IsLink;

        internal bool StableEquals(NativeIdentity other) =>
            Device == other.Device && Inode == other.Inode && LinkCount == other.LinkCount
            && Mode == other.Mode && UserId == other.UserId && GroupId == other.GroupId
            && DeviceType == other.DeviceType && Size == other.Size && Blocks == other.Blocks
            && ModifySeconds == other.ModifySeconds && ModifyNanoseconds == other.ModifyNanoseconds
            && ChangeSeconds == other.ChangeSeconds && ChangeNanoseconds == other.ChangeNanoseconds;

        internal static NativeIdentity ReadPath(string path)
        {
            if (NativeMethods.LStat(path, out NativeIdentity identity) != 0)
                throw new SetupCompositionException(SetupCompositionFailureCode.UnsafeEntry);
            return identity;
        }

        internal static NativeIdentity ReadHandle(SafeFileHandle handle)
        {
            if (NativeMethods.FStat(handle, out NativeIdentity identity) != 0)
                throw new SetupCompositionException(SetupCompositionFailureCode.UnsupportedFilesystem);
            return identity;
        }
    }

    private static class NativeMethods
    {
        private const int ReadOnly = 0;
        private const int CloseOnExec = 0x80000;
        private const int NoFollow = 0x20000;
        private const int Directory = 0x10000;
        private const long OpenAt2SystemCall = 437;
        private const ulong ResolveNoCrossDevice = 0x01;
        private const ulong ResolveNoMagicLinks = 0x02;
        private const ulong ResolveNoSymbolicLinks = 0x04;
        private const ulong ResolveBeneath = 0x08;

        [StructLayout(LayoutKind.Sequential)]
        private struct OpenHow
        {
            internal ulong Flags;
            internal ulong Mode;
            internal ulong Resolve;
        }

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int Open(byte[] path, int flags);

        [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
        private static extern long SystemCall(long number, int directoryFileDescriptor,
            byte[] path, ref OpenHow how, nuint size);

        [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
        private static extern int LStatNative(byte[] path, out NativeIdentity identity);

        internal static int LStat(string path, out NativeIdentity identity) =>
            LStatNative(Utf8Path(path), out identity);

        [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
        internal static extern int FStat(SafeFileHandle handle, out NativeIdentity identity);

        internal static SafeFileHandle OpenRoot(string root)
        {
            int descriptor = Open(Utf8Path(root), ReadOnly | CloseOnExec | NoFollow | Directory);
            if (descriptor < 0)
                throw new SetupCompositionException(SetupCompositionFailureCode.UnsupportedFilesystem);
            return new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        }

        internal static SafeFileHandle OpenRelative(SafeFileHandle root, string relative, bool directory)
        {
            var how = new OpenHow
            {
                Flags = (ulong)(ReadOnly | CloseOnExec | NoFollow | (directory ? Directory : 0)),
                Resolve = ResolveNoCrossDevice | ResolveNoMagicLinks | ResolveNoSymbolicLinks | ResolveBeneath
            };
            long descriptor = SystemCall(OpenAt2SystemCall, root.DangerousGetHandle().ToInt32(),
                Utf8Path(relative), ref how, (nuint)Marshal.SizeOf<OpenHow>());
            if (descriptor < 0)
                throw new SetupCompositionException(SetupCompositionFailureCode.UnsafeEntry);
            return new SafeFileHandle(checked((IntPtr)descriptor), ownsHandle: true);
        }

        internal static NativeIdentity OpenIdentity(SafeFileHandle root, string relative, bool directory)
        {
            using SafeFileHandle handle = OpenRelative(root, relative, directory);
            return NativeIdentity.ReadHandle(handle);
        }

        private static byte[] Utf8Path(string path)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(path);
            Array.Resize(ref bytes, checked(bytes.Length + 1));
            return bytes;
        }
    }
}
