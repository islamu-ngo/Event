using System.Collections.Immutable;
using System.Diagnostics;

namespace ISLAMU.DependencySubmission;

public static class RepositoryLocks
{
    public static async Task<ImmutableArray<LockInput>> ReadAsync(string root, CancellationToken cancellationToken = default)
    {
        var tracked = await GitAsync(root, ["ls-files", "--cached", "-z", "--",
            "packages.lock.json", "**/packages.lock.json"], cancellationToken).ConfigureAwait(false);
        var inputs = ImmutableArray.CreateBuilder<LockInput>();
        foreach (var path in tracked.Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal))
        {
            var fullPath = Path.GetFullPath(Path.Combine(root, path));
            if (!fullPath.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) || (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Lock path is not a regular repository file.");
            inputs.Add(new LockInput(path, await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false)));
        }
        return inputs.ToImmutable();
    }

    public static async Task<string> GitAsync(string root, IEnumerable<string> arguments, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(root);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git could not start.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await error.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidDataException("Git repository input failed.");
            return await output.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
