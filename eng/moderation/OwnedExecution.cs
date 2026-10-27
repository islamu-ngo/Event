using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Explore.Application.Services.Moderation;

namespace ISLAMU.Moderation.Measurement;

internal sealed record ChildExecution(string Bundle, int ProcessId, DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt, int ExitCode, string StdoutDigest, string StderrDigest);

/// <summary>
/// Owns exactly one invocation per pinned bundle. Raw output is private evidence, never
/// console telemetry. Cancellation is registered before start; exit and both pipes are
/// drained, including failures, before the process object and ownership marker close.
/// </summary>
internal static partial class OwnedExecution
{
    internal const int MaximumReportBytes = 32 * 1024 * 1024;
    private const long MaximumDirectoryBytes = 512L * 1024 * 1024;
    internal const string SessionLauncher = "/usr/bin/setsid";

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int SignalProcessGroup(int processId, int signal);

    internal static string CreateDirectory(string output, Guid runId)
    {
        if (!OperatingSystem.IsLinux()) throw new MeasurementFailure("host-platform-unsupported");
        Require.That(Path.IsPathFullyQualified(output) && Directory.Exists(output), "output-invalid");
        Artifacts.RegularPath(output);
        var path = Path.Combine(output, runId.ToString("N"));
        Require.That(!Directory.Exists(path) && !File.Exists(path), "run-collision");
        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Artifacts.RegularPath(path);
        Require.That(File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute),
            "run-permissions");
        return path;
    }

    internal static async Task<ChildExecution> RunAsync(string root, string directory, Guid runId,
        ModerationMeasurementCatalogueJob job, MeasurementConfiguration configuration, DateTimeOffset deadline, CancellationToken cancellation)
    {
        var executable = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", "dotnet"));
        Artifacts.RegularPath(executable);
        Artifacts.RegularPath(SessionLauncher);
        var assembly = Path.Combine(PinnedCatalogue.BundleDirectory(root, job.Bundle), job.Bundle + ".dll");
        var start = new ProcessStartInfo(SessionLauncher)
        {
            UseShellExecute = false, WorkingDirectory = directory,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            CreateNoWindow = true
        };
        start.Environment.Clear();
        foreach (var pair in configuration.ChildEnvironment(directory, runId))
            start.Environment.Add(pair.Key, pair.Value);
        foreach (var argument in new[]
        {
            "--", executable, assembly, "--treenode-filter", job.TreeFilter,
            "--minimum-expected-tests", job.Cases.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--progress", "off", "--report-html", "--report-html-filename", Path.Combine(directory, job.Bundle + ".html"),
            "--report-trx", "--report-trx-filename", job.Bundle + ".trx", "--results-directory", directory
        }) start.ArgumentList.Add(argument);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var remaining = deadline - DateTimeOffset.UtcNow;
        Require.That(remaining > TimeSpan.Zero, "source-expired");
        timeout.CancelAfter(remaining < TimeSpan.FromMinutes(15) ? remaining : TimeSpan.FromMinutes(15));
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var gate = new object();
        var started = false;
        var acceptingSignals = true;
        string? failure = null;
        bool KillGroup()
        {
            // setsid execs the fixed dotnet host in a separate session/process group.
            // Negative PID addresses surviving descendants even after the leader exits.
            if (SignalProcessGroup(-process.Id, 9) == 0) return true;
            Require.That(Marshal.GetLastPInvokeError() == 3, "process-group-close-failed"); // ESRCH: absent.
            return false;
        }
        void CloseProcess()
        {
            if (!started) return;
            _ = KillGroup();
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* Exit raced cancellation. */ }
            }
        }
        void Stop(string category)
        {
            lock (gate)
            {
                if (!acceptingSignals) return;
                Interlocked.CompareExchange(ref failure, category, null);
                timeout.Cancel();
            }
        }
        using var registration = timeout.Token.Register(() =>
        {
            lock (gate)
            {
                try
                {
                    CloseProcess();
                }
                catch (Exception)
                {
                    Interlocked.CompareExchange(ref failure, "process-group-close-failed", null);
                }
            }
        });
        using var watcher = new FileSystemWatcher(directory) { IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName };
        void CheckBounds(object? sender, FileSystemEventArgs args)
        {
            try
            {
                var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Take(4097).ToArray();
                if (files.Length > 4096 || files.Sum(file => new FileInfo(file).Length) > MaximumDirectoryBytes)
                    Stop("execution-artifact-limit");
            }
            catch (IOException) { Stop("execution-artifact-race"); }
            catch (UnauthorizedAccessException) { Stop("execution-artifact-access"); }
        }
        watcher.Created += CheckBounds;
        watcher.Changed += CheckBounds;
        watcher.Error += (_, _) => Stop("execution-artifact-watch");
        watcher.EnableRaisingEvents = true;
        var stdout = Path.Combine(directory, job.Bundle + ".stdout");
        var stderr = Path.Combine(directory, job.Bundle + ".stderr");
        var owner = Path.Combine(directory, job.Bundle + ".owner");
        var began = DateTimeOffset.UtcNow;
        Task? output = null;
        Task? error = null;
        try
        {
            await WritePrivateAsync(owner, JsonSerializer.SerializeToUtf8Bytes(new { runId, job.Bundle, state = "starting" }), cancellation);
            lock (gate)
            {
                timeout.Token.ThrowIfCancellationRequested();
                Require.That(process.Start(), "process-start-failed");
                started = true;
            }
            process.StandardInput.Close();
            output = DrainAsync(process.StandardOutput.BaseStream, stdout, Stop);
            error = DrainAsync(process.StandardError.BaseStream, stderr, Stop);
            await process.WaitForExitAsync(timeout.Token);
            if (KillGroup()) Stop("process-descendants-remained");
            await Task.WhenAll(output, error).WaitAsync(timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            Require.That(failure is null, failure ?? "execution-failed");
            CheckBounds(null, new FileSystemEventArgs(WatcherChangeTypes.Changed, directory, job.Bundle));
            timeout.Token.ThrowIfCancellationRequested();
            foreach (var path in new[] { stdout, stderr })
            {
                var text = await File.ReadAllTextAsync(path, timeout.Token);
                Require.That(!text.Contains("Unknown option", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("Unrecognized option", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("Unrecognised option", StringComparison.OrdinalIgnoreCase),
                    "process-option-error");
            }
            var result = new ChildExecution(job.Bundle, process.Id, began, DateTimeOffset.UtcNow, process.ExitCode,
                await Artifacts.FileDigestAsync(stdout, cancellation), await Artifacts.FileDigestAsync(stderr, cancellation));
            await WritePrivateAsync(Path.Combine(directory, job.Bundle + ".exit.json"),
                JsonSerializer.SerializeToUtf8Bytes(result), cancellation);
            Require.That(process.ExitCode == 0, "process-nonzero-exit");
            return result;
        }
        catch (OperationCanceledException)
        {
            throw new MeasurementFailure(failure ?? (cancellation.IsCancellationRequested ? "cancelled" : "execution-timeout"));
        }
        finally
        {
            watcher.EnableRaisingEvents = false;
            lock (gate)
            {
                // FileSystemWatcher disposal does not join already-dispatched handlers.
                // Close their signal gate before disposing the cancellation source.
                acceptingSignals = false;
                CloseProcess();
            }
            if (started)
            {
                // Bounded cleanup is distinct from a retry. A drain failure forbids signing.
                await Task.WhenAll(process.WaitForExitAsync(CancellationToken.None), output ?? Task.CompletedTask, error ?? Task.CompletedTask)
                    .WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
                await WritePrivateAsync(Path.Combine(directory, job.Bundle + ".closed.json"),
                    JsonSerializer.SerializeToUtf8Bytes(new { runId, job.Bundle, processId = process.Id, exitCode = process.ExitCode,
                        utc = DateTimeOffset.UtcNow, state = "closed" }), CancellationToken.None);
            }
            Require.That(failure is null, failure ?? "execution-failed");
        }
    }

    private static async Task DrainAsync(Stream input, string path, Action<string> stop)
    {
        if (!OperatingSystem.IsLinux()) throw new MeasurementFailure("host-platform-unsupported");
        try
        {
            await using var file = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });
            var buffer = new byte[16 * 1024];
            long count = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) != 0)
            {
                count += read;
                if (count > MaximumReportBytes)
                {
                    stop("process-output-limit");
                    continue; // Drain after killing; never retain a deceptively complete output.
                }
                await file.WriteAsync(buffer.AsMemory(0, read));
            }
            file.Flush(flushToDisk: true);
        }
        catch
        {
            stop("process-output-failed");
            throw;
        }
    }

    internal static async Task WritePrivateAsync(string path, byte[] bytes, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux()) throw new MeasurementFailure("host-platform-unsupported");
        await using var file = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        });
        await file.WriteAsync(bytes, token);
        file.Flush(flushToDisk: true);
    }
}
