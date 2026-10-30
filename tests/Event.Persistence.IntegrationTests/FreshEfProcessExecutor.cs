using System.Diagnostics;
using System.Text;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests;

public sealed class FreshEfProcessExecutor : ITestExecutor
{
    internal const string ChildTestIdVariable = "EVENT_PERSISTENCE_EF_CHILD_TEST_ID";
    internal const string ParentProcessIdVariable = "EVENT_PERSISTENCE_EF_PARENT_PROCESS_ID";
    internal const int MaximumRetainedCharacters = 65_536;
    internal const int MaximumPhaseArrivals = 128;
    internal const long MaximumPhaseElapsedMilliseconds = 900_000;
    private static readonly HashSet<string> AllowedPhaseLines = CreateAllowedPhaseLines();
    private static readonly int MaximumPhaseLineLength = AllowedPhaseLines.Max(line => line.Length);

    /// <summary>Retains the standard stream for the child process lifetime to keep its reporting route open.</summary>
    private static readonly Lazy<Stream?> ProcessPhaseOutput = new(() =>
    {
        try { return Console.OpenStandardOutput(); }
        catch { return null; }
    });

    public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action)
    {
        string testId = context.Metadata.TestDetails.Identity.TestId;
        string? childTestId = Environment.GetEnvironmentVariable(ChildTestIdVariable);
        if (childTestId is not null)
        {
            if (!StringComparer.Ordinal.Equals(childTestId, testId))
            {
                throw new InvalidOperationException($"Isolated EF process selected unexpected test {testId}.");
            }

            await action();
            return;
        }

        await RunAsync(testId, context.Execution.CancellationToken);
    }

    internal static async Task RunAsync(string testId, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        start.ArgumentList.Add(typeof(FreshEfProcessExecutor).Assembly.Location);
        string resultsDirectory = Path.Combine(Path.GetTempPath(), "event-persistence-ef", Guid.NewGuid().ToString("N"));
        start.ArgumentList.Add("--results-directory");
        start.ArgumentList.Add(resultsDirectory);
        start.ArgumentList.Add("--filter-uid");
        start.ArgumentList.Add(testId);
        start.ArgumentList.Add("--minimum-expected-tests");
        start.ArgumentList.Add("1");
        start.ArgumentList.Add("--zero-tests-policy");
        start.ArgumentList.Add("strict");
        start.ArgumentList.Add("--no-ansi");
        start.ArgumentList.Add("--progress");
        start.ArgumentList.Add("off");
        start.ArgumentList.Add("--exit-on-process-exit");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.Environment[ChildTestIdVariable] = testId;
        start.Environment[ParentProcessIdVariable] = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using var process = new Process { StartInfo = start };
        var arrivalClock = Stopwatch.StartNew();
        process.Start();
        using var stopReading = new CancellationTokenSource();
        Task<OutputCapture> stdout = CaptureOutputAsync(process.StandardOutput, arrivalClock, stopReading.Token);
        Task<OutputCapture> stderr = CaptureOutputAsync(process.StandardError, null, stopReading.Token);
        using var ownedDeadline = new CancellationTokenSource();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ownedDeadline.Token);
        ownedDeadline.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException primary)
        {
            bool callerRequested = cancellationToken.IsCancellationRequested;
            bool deadlineRequested = ownedDeadline.IsCancellationRequested;
            bool killAttempted = false, killReturned = false, joinReturned = false;
            var cleanupFailures = new List<Exception>();
            void RetainFailure(Exception failure)
            {
                if (!cleanupFailures.Any(existing => ReferenceEquals(existing, failure)))
                    cleanupFailures.Add(failure);
            }
            try
            {
                if (!process.HasExited)
                {
                    killAttempted = true;
                    process.Kill(entireProcessTree: true);
                    killReturned = true;
                }
            }
            catch (Exception failure) { RetainFailure(failure); }
            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
                joinReturned = true;
            }
            catch (Exception failure) { RetainFailure(failure); }
            try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None); }
            catch (TimeoutException) { }
            catch (Exception failure) { RetainFailure(failure); }
            try { await stopReading.CancelAsync(); }
            catch (Exception failure) { RetainFailure(failure); }
            OutputCapture? Snapshot(Task<OutputCapture> capture)
            {
                if (capture.IsCompletedSuccessfully)
                {
                    OutputCapture value = capture.Result;
                    if (value.Failure is { } capturedFailure) RetainFailure(capturedFailure);
                    return value;
                }
                if (capture.Exception is { } failures)
                    foreach (Exception taskFailure in failures.InnerExceptions) RetainFailure(taskFailure);
                return null;
            }
            OutputCapture? outputSnapshot = Snapshot(stdout);
            OutputCapture? errorSnapshot = Snapshot(stderr);
            try
            {
                foreach (string line in ProjectCancellationEvidence(
                    outputSnapshot, errorSnapshot, callerRequested, deadlineRequested,
                    killAttempted, killReturned, joinReturned))
                    Console.WriteLine(line);
                foreach (string line in ProjectPhaseArrivals(outputSnapshot))
                    Console.WriteLine(line);
            }
            catch { }
            if (cleanupFailures.Count != 0)
                throw new AggregateException("fresh_ef_cancellation_cleanup_failed", new[] { primary }.Concat(cleanupFailures));
            throw;
        }

        OutputCapture standardOutput = await stdout;
        OutputCapture standardError = await stderr;
        if (standardOutput.Failure is { } outputFailure) throw outputFailure;
        if (standardError.Failure is { } errorFailure) throw errorFailure;
        string output = standardOutput.Text + standardError.Text;
        if (process.ExitCode != 0)
        {
            throw new IsolatedEfTestException(process.ExitCode, testId, output);
        }
        if (Directory.Exists(resultsDirectory))
        {
            Directory.Delete(resultsDirectory, recursive: true);
        }
    }

    internal sealed record PhaseArrival(string Phase, long ElapsedMilliseconds);

    internal sealed record OutputCapture(string Text, bool Complete, bool Overflowed, Exception? Failure = null,
        PhaseArrival[]? Arrivals = null, bool TimingInvalid = false);

    internal static void WritePhaseBestEffort(string phase)
    {
        try
        {
            if (AllowedPhaseLines.Contains(phase) && ProcessPhaseOutput.Value is { } output)
                WritePhaseBestEffort(output, phase);
        }
        catch { }
    }

    internal static void WritePhaseBestEffort(Stream output, string phase)
    {
        try
        {
            if (!AllowedPhaseLines.Contains(phase)) return;
            byte[] line = Encoding.UTF8.GetBytes(phase + "\n");
            lock (output)
            {
                output.Write(line);
                output.Flush();
            }
        }
        catch { }
    }

    internal static async Task<OutputCapture> CaptureOutputAsync(
        TextReader reader, Stopwatch? arrivalClock, CancellationToken cancellationToken)
    {
        var retained = new StringBuilder(MaximumRetainedCharacters, MaximumRetainedCharacters);
        char[] buffer = new char[4096];
        bool overflowed = false;
        List<PhaseArrival>? arrivals = arrivalClock is null ? null : [];
        StringBuilder? pendingLine = arrivals is null ? null : new(MaximumPhaseLineLength + 1, MaximumPhaseLineLength + 1);
        bool discardLine = false, timingInvalid = false;
        OutputCapture Snapshot(bool complete, Exception? failure = null)
        {
            if (arrivalClock is not null && (!arrivalClock.IsRunning
                || arrivalClock.ElapsedMilliseconds < 0 || arrivalClock.ElapsedMilliseconds > MaximumPhaseElapsedMilliseconds))
                timingInvalid = true;
            return new(retained.ToString(), complete, overflowed, failure, arrivals?.ToArray(), timingInvalid);
        }
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (arrivalClock is not null && (!arrivalClock.IsRunning
                    || arrivalClock.ElapsedMilliseconds > MaximumPhaseElapsedMilliseconds)) timingInvalid = true;
                int count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (count == 0) return Snapshot(complete: true);
                int available = MaximumRetainedCharacters - retained.Length;
                retained.Append(buffer, 0, Math.Min(available, count));
                overflowed |= count > available;
                if (arrivals is null || timingInvalid) continue;
                for (int index = 0; index < count; index++)
                {
                    char character = buffer[index];
                    if (character == '\n')
                    {
                        if (!discardLine)
                        {
                            string line = pendingLine!.ToString();
                            if (line.EndsWith('\r')) line = line[..^1];
                            if (AllowedPhaseLines.Contains(line))
                            {
                                long elapsed = arrivalClock!.ElapsedMilliseconds;
                                if (!arrivalClock.IsRunning || elapsed < 0 || elapsed > MaximumPhaseElapsedMilliseconds
                                    || arrivals.Count == MaximumPhaseArrivals
                                    || (arrivals.Count != 0 && elapsed < arrivals[^1].ElapsedMilliseconds))
                                    timingInvalid = true;
                                else arrivals.Add(new(line, elapsed));
                            }
                        }
                        pendingLine!.Clear();
                        discardLine = false;
                        if (timingInvalid) break;
                    }
                    else if (!discardLine)
                    {
                        if (pendingLine!.Length < MaximumPhaseLineLength + 1) pendingLine.Append(character);
                        else
                        {
                            pendingLine.Clear();
                            discardLine = true;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Snapshot(complete: false);
        }
        catch (Exception failure)
        {
            return Snapshot(complete: false, failure);
        }
    }

    internal static string[] ProjectPhaseArrivals(OutputCapture? capture)
    {
        const string anchor = "fresh_ef_timing clock=parent_launch_arrival anchor=child_launch_attempt elapsed_ms=0";
        if (capture?.Arrivals is not { } arrivals) return ["fresh_ef_timing outcome=unavailable"];
        if (capture.Overflowed || capture.Text.Length > MaximumRetainedCharacters) return [anchor, "fresh_ef_timing outcome=overflow"];
        if (capture.Failure is not null) return [anchor, "fresh_ef_timing outcome=unavailable"];
        if (!capture.Complete) return [anchor, "fresh_ef_timing outcome=incomplete"];
        if (capture.TimingInvalid || arrivals.Length > MaximumPhaseArrivals) return [anchor, "fresh_ef_timing outcome=inconsistent"];
        long previous = 0;
        foreach (PhaseArrival arrival in arrivals)
        {
            if (arrival is null || !AllowedPhaseLines.Contains(arrival.Phase)
                || arrival.ElapsedMilliseconds < previous || arrival.ElapsedMilliseconds > MaximumPhaseElapsedMilliseconds)
                return [anchor, "fresh_ef_timing outcome=inconsistent"];
            previous = arrival.ElapsedMilliseconds;
        }
        // This clock measures parent line recognition, including startup, transport and scheduling delay.
        var lines = new string[arrivals.Length + 1];
        lines[0] = anchor;
        for (int index = 0; index < arrivals.Length; index++)
            lines[index + 1] = FormattableString.Invariant(
                $"fresh_ef_timing ordinal={index + 1} elapsed_ms={arrivals[index].ElapsedMilliseconds} {arrivals[index].Phase}");
        return lines;
    }

    internal static string[] ProjectCancellationEvidence(OutputCapture? stdout, OutputCapture? stderr,
        bool callerRequested, bool deadlineRequested, bool killAttempted, bool killReturned, bool joinReturned)
    {
        static string Status(OutputCapture? capture) => capture switch
        {
            null => "unavailable",
            { Overflowed: true } => "overflow",
            { Text.Length: > MaximumRetainedCharacters } => "overflow",
            { Failure: not null } => "unavailable",
            { Complete: false } => "incomplete",
            _ => "complete"
        };
        var phases = new SortedSet<string>(StringComparer.Ordinal);
        foreach (OutputCapture? capture in new[] { stdout, stderr })
        {
            if (capture is not { Complete: true, Overflowed: false, Failure: null }
                || capture.Text.Length > MaximumRetainedCharacters) continue;
            using var lines = new StringReader(capture.Text);
            while (lines.ReadLine() is { } line)
                if (AllowedPhaseLines.Contains(line)) phases.Add(line);
        }
        return new[]
        {
            $"fresh_ef_cancellation caller_requested={(callerRequested ? "true" : "false")} owned_deadline_requested={(deadlineRequested ? "true" : "false")} kill_attempted={(killAttempted ? "true" : "false")} kill_returned={(killReturned ? "true" : "false")} join_returned={(joinReturned ? "true" : "false")} stdout={Status(stdout)} stderr={Status(stderr)} phase_semantics=some_attempt_reported"
        }.Concat(phases).ToArray();
    }

    private static HashSet<string> CreateAllowedPhaseLines()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "fresh_ef_control phase=refusal_entered",
            "migration_public phase=first_returned",
            "migration_public phase=repeat_returned",
            "migration_auth_boundary phase=entry_readiness_start",
            "migration_auth_boundary phase=entry_readiness_complete",
            "migration_mixed_boundary phase=entry",
            "migration_mixed_boundary phase=baseline_ready",
            "migration_mixed_boundary phase=missing_source_retained",
            "migration_mixed_boundary phase=missing_source_restored",
            "migration_mixed_boundary phase=complete",
            "migration_mixed_wait phase=mixed_helper_scope outcome=inconclusive",
            "migration_mixed_wait phase=mixed_helper_scope outcome=not_observed"
        };
        foreach (string active in new[] { "true", "false" })
            foreach (string lockWait in new[] { "true", "false" })
                foreach (string ownedBlocker in new[] { "true", "false" })
                    allowed.Add($"migration_mixed_wait phase=mixed_helper_scope active={active} lock_wait={lockWait} owned_blocker={ownedBlocker}");
        foreach (string change in new[] { "api_name_update", "scanner_label_update", "api_key_id_select", "scanner_digest_select" })
            foreach (string phase in new[] { "preimage_start", "preimage_complete", "tamper_applied", "restore_returned", "restored_acl_complete", "restored_readiness_complete" })
                allowed.Add($"migration_auth_boundary case={change} phase={phase}");
        foreach (string change in new[] { "extra", "missing", "public", "grant_option", "wrong_issuer" })
            allowed.Add($"migration_mixed_boundary case={change} phase=restored");
        return allowed;
    }
}

internal sealed class IsolatedEfTestException(int exitCode, string testId, string output)
    : Exception($"Isolated EF test {testId} exited with code {exitCode}.\n{output}")
{
    public int ExitCode { get; } = exitCode;
}
