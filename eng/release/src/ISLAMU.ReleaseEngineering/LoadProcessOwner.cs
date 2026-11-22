using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// A dedicated Linux x64 process, never an in-process service. Only this owner may
/// wait for its native children. In particular, do not initialize Process here:
/// its child reaper would invalidate the zombie lifetime used to acquire pidfds.
/// </summary>
internal static unsafe partial class LoadProcessOwner
{
    internal const string CommandName = "own-load-process";
    private const int FailureExitCode = 70;
    private const int MaximumLineBytes = 65536;
    private const int CleanupTimeoutMilliseconds = 10000;
    private const int SampleIntervalMilliseconds = 100;
    private const int MaximumCaptureBytes = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    internal static int Run(string[] arguments) => new Owner().Run(arguments);

    private sealed class Owner
    {
        private readonly Dictionary<int, Child> children = [];
        private readonly List<string> cleanupFailures = [];
        private readonly byte[] controlLine = new byte[MaximumLineBytes];
        private readonly byte[] readBuffer = new byte[8192];
        private readonly byte[] capturedOutput = new byte[MaximumCaptureBytes];
        private readonly object signalGate = new();
        private int controlDescriptor = -1;
        private int protocolDescriptor = -1;
        private int savedErrorDescriptor = -1;
        private int signalDescriptor = -1;
        private int stdoutRead = -1;
        private int stdoutWrite = -1;
        private int stderrRead = -1;
        private int stderrWrite = -1;
        private int nullInput = -1;
        private int savedDirectory = -1;
        private bool stdoutEof = true;
        private bool stderrEof = true;
        private bool stdoutReadFailed;
        private bool stderrReadFailed;
        private bool controlEnded;
        private bool protocolWritable = true;
        private bool signalRequested;
        private bool cancelled;
        private bool finalSampleTaken;
        private bool cleanupFailureOverflow;
        private int controlLength;
        private int captureLength;
        private int sampleCount;
        private int? rootPid;
        private int? rootExitCode;
        private long? startedTimestamp;
        private long? exitedTimestamp;
        private long lastSampleTimestamp;
        private ulong lastCpuTicks;
        private long clockTicksPerSecond;
        private double peakCpuPercent;
        private long peakRssBytes;
        private string? primaryFailure;
        private StartRequest? request;
        private Child? rootChild;
        private PosixSignalRegistration? terminateRegistration;
        private PosixSignalRegistration? interruptRegistration;

        internal int Run(string[] arguments)
        {
            bool validArguments = arguments.Length == 1 &&
                string.Equals(arguments[0], CommandName, StringComparison.Ordinal);
            try
            {
                if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                {
                    return FailureExitCode;
                }

                Initialize();
                if (!validArguments)
                {
                    primaryFailure = "invalid_arguments";
                }
                else
                {
                    Execute();
                }
            }
            catch (Exception exception) when (IsOperationalFailure(exception))
            {
                primaryFailure ??= FailureCode(exception);
            }
            finally
            {
                // Every stage runs even if an earlier stage failed. Closing a read
                // descriptor is never evidence that its workload pipe reached EOF.
                CloseOwned(ref stdoutWrite, "stdout_writer_close");
                CloseOwned(ref stderrWrite, "stderr_writer_close");
                CleanupStage("workload_cleanup", CleanupWorkload);
                CloseOwned(ref stdoutRead, "stdout_reader_close");
                CloseOwned(ref stderrRead, "stderr_reader_close");
                CloseOwned(ref nullInput, "stdin_close");
                CloseOwned(ref savedDirectory, "directory_close");
                foreach (Child child in children.Values)
                {
                    CloseDescriptor(child.Descriptor, "pidfd_close");
                }

                children.Clear();
                CleanupStage("sigterm_unregister", () => terminateRegistration?.Dispose());
                CleanupStage("sigint_unregister", () => interruptRegistration?.Dispose());
                lock (signalGate)
                {
                    CloseOwned(ref signalDescriptor, "signal_descriptor_close");
                }

                CloseOwned(ref controlDescriptor, "control_descriptor_close");
                CloseOwned(ref savedErrorDescriptor, "saved_stderr_close");
            }

            bool delivered = WriteMessage(new
            {
                type = "result",
                exitCode = rootExitCode,
                wallMilliseconds = startedTimestamp is long start && exitedTimestamp is long exit
                    ? Stopwatch.GetElapsedTime(start, exit).TotalMilliseconds
                    : 0,
                startedTimestamp,
                exitedTimestamp,
                sampleCount,
                peakCpuPercent,
                peakRssBytes,
                cancelled,
                primaryFailure,
                cleanupFailures,
                output = Encoding.UTF8.GetString(capturedOutput, 0, captureLength)
            }, 1000);

            // The last protocol endpoint cannot report an error closing itself.
            // Its close outcome is still reflected in the owner exit status.
            CloseOwned(ref protocolDescriptor, "protocol_descriptor_close");
            if (!validArguments)
            {
                return Program.UsageError;
            }

            return delivered && primaryFailure is null && !cancelled && rootExitCode == 0 &&
                cleanupFailures.Count == 0
                ? Program.Success
                : FailureExitCode;
        }

        private void Initialize()
        {
            // These copies remain usable even if restoration of standard FDs fails.
            // None may survive workload exec.
            controlDescriptor = RequireDescriptor(Native.Fcntl(0, Native.DuplicateCloseOnExec, 3), "duplicate_stdin");
            protocolDescriptor = RequireDescriptor(Native.Fcntl(1, Native.DuplicateCloseOnExec, 3), "duplicate_stdout");
            savedErrorDescriptor = RequireDescriptor(Native.Fcntl(2, Native.DuplicateCloseOnExec, 3), "duplicate_stderr");
            SetNonblocking(controlDescriptor);
            SetNonblocking(protocolDescriptor);
            if (Native.Signal(Native.SigPipe, 1) == -1)
            {
                throw NativeFailure("ignore_sigpipe");
            }

            signalDescriptor = RequireDescriptor(
                Native.EventFd(0, Native.CloseOnExec | Native.Nonblocking), "eventfd");
            terminateRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);
            interruptRegistration = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);

            // signal(SIG_DFL), unlike SIG_IGN, retains zombies. The glibc signal
            // interface installs BSD flags, without SA_NOCLDWAIT. Install it after
            // signal registration, and never register a managed SIGCHLD consumer.
            if (Native.Signal(Native.SigChild, 0) == -1)
            {
                throw NativeFailure("reset_sigchld");
            }

            RequireZero(Native.Prctl(Native.SetChildSubreaper, 1, 0, 0, 0), "set_subreaper");
            int subreaper = 0;
            RequireZero(Native.Prctl(Native.GetChildSubreaper, (nint)(&subreaper), 0, 0, 0), "get_subreaper");
            if (subreaper != 1 || !HasNoChildren())
            {
                throw new InvalidOperationException("Owner requires exclusive child ownership.");
            }

            DiscoverDirectChildren(terminate: false);

            // Probe kernel behavior, not merely exported libc symbol names.
            int selfDescriptor = OpenPidFd(Environment.ProcessId);
            try
            {
                RequireZero(SendSignal(selfDescriptor, 0), "probe_pidfd_signal");
                int descriptorFlags = RequireDescriptor(
                    Native.Fcntl(selfDescriptor, Native.GetDescriptorFlags, 0), "pidfd_flags");
                if ((descriptorFlags & Native.DescriptorCloseOnExec) == 0 || IsExited(selfDescriptor))
                {
                    throw new InvalidOperationException("Pidfd behavior does not match the owner contract.");
                }
            }
            finally
            {
                CloseDescriptor(selfDescriptor, "probe_pidfd_close");
            }

            RequireZero(MarkDescriptorsCloseOnExec(), "close_range_cloexec");
            clockTicksPerSecond = Native.Sysconf(Native.ClockTicks);
            if (clockTicksPerSecond <= 0)
            {
                throw new InvalidOperationException("Invalid CPU clock tick frequency.");
            }

            if (cleanupFailures.Count != 0)
            {
                throw new InvalidOperationException("Owner initialization cleanup failed.");
            }
        }

        private void Execute()
        {
            if (!WriteMessage(new
            {
                type = "ready",
                protocolVersion = 1,
                stopwatchFrequency = Stopwatch.Frequency,
                processorCount = Environment.ProcessorCount,
                maximumLineBytes = MaximumLineBytes,
                cleanupTimeoutMilliseconds = CleanupTimeoutMilliseconds
            }, 1000))
            {
                primaryFailure = "protocol_write_failed";
                return;
            }

            long readyTimestamp = Stopwatch.GetTimestamp();
            while (request is null && primaryFailure is null)
            {
                ReadControl();
                ObserveSignal();
                if (request is not null || primaryFailure is not null)
                {
                    break;
                }

                if (Stopwatch.GetElapsedTime(readyTimestamp).TotalMilliseconds >= 10000)
                {
                    primaryFailure = "start_not_received";
                    return;
                }

                WaitForActivity(SampleIntervalMilliseconds);
            }

            if (request is null || primaryFailure is not null)
            {
                return;
            }

            Spawn(request);
            if (rootPid is not int pid)
            {
                return;
            }

            if (!WriteMessage(new { type = "started", rootPid = pid, startedTimestamp }, 0))
            {
                primaryFailure ??= "protocol_write_failed";
                return;
            }

            while (primaryFailure is null && cleanupFailures.Count == 0)
            {
                ReadControl();
                ObserveSignal();
                if (primaryFailure is not null)
                {
                    break;
                }

                long now = Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(startedTimestamp!.Value, now).TotalMilliseconds >= request.TimeoutMilliseconds)
                {
                    primaryFailure = "deadline_exceeded";
                    break;
                }

                if (children.TryGetValue(pid, out Child? root) && IsExited(root.Descriptor))
                {
                    ObserveRootExit();
                    break;
                }

                if (sampleCount == 0 ||
                    Stopwatch.GetElapsedTime(lastSampleTimestamp, now).TotalMilliseconds >= SampleIntervalMilliseconds)
                {
                    SampleRoot();
                }

                if (primaryFailure is not null)
                {
                    break;
                }

                DrainOutput(ref stdoutRead, ref stdoutEof, ref stdoutReadFailed, "stdout");
                DrainOutput(ref stderrRead, ref stderrEof, ref stderrReadFailed, "stderr");
                if (primaryFailure is not null)
                {
                    break;
                }

                double remaining = request.TimeoutMilliseconds -
                    Stopwatch.GetElapsedTime(startedTimestamp.Value).TotalMilliseconds;
                WaitForActivity((int)Math.Clamp(Math.Ceiling(remaining), 0, SampleIntervalMilliseconds));
            }
        }

        private void Spawn(StartRequest start)
        {
            using var argv = new NativeStrings([start.Executable, .. start.Arguments]);
            using var environment = new NativeStrings(
                start.Environment.Select(pair => $"{pair.Key}={pair.Value}").ToArray());
            try
            {
                CreatePipe(ref stdoutRead, ref stdoutWrite, ref stdoutEof);
                CreatePipe(ref stderrRead, ref stderrWrite, ref stderrEof);
                nullInput = RequireDescriptor(Native.Open("/dev/null", Native.CloseOnExec), "open_dev_null");
                savedDirectory = RequireDescriptor(
                    Native.Open(".", Native.Directory | Native.CloseOnExec), "open_working_directory");

                // Check the lease again after setup, before changing process-global
                // descriptors. All protocol I/O is synchronous on this thread.
                ReadControl();
                ObserveSignal();
                if (primaryFailure is not null)
                {
                    return;
                }

                RequireZero(Native.ChangeDirectory(start.WorkingDirectory), "change_working_directory");
                RequireDescriptor(Native.DuplicateTo(nullInput, 0), "redirect_stdin");
                RequireDescriptor(Native.DuplicateTo(stdoutWrite, 1), "redirect_stdout");
                RequireDescriptor(Native.DuplicateTo(stderrWrite, 2), "redirect_stderr");
                RequireZero(MarkDescriptorsCloseOnExec(), "close_range_cloexec");

                // No opaque posix_spawn objects, managed callback in the child,
                // shell, async protocol I/O, or concurrent Console access.
                long beforeSpawn = Stopwatch.GetTimestamp();
                int error = Native.Spawn(out int pid, start.Executable, 0, 0, argv.Pointer, environment.Pointer);
                if (error != 0)
                {
                    primaryFailure = $"spawn_failed:errno={error.ToString(CultureInfo.InvariantCulture)}";
                    return;
                }

                rootPid = pid;
                startedTimestamp = beforeSpawn;
                lastSampleTimestamp = beforeSpawn;
                // No wait has happened. Even an immediately exited child remains
                // an unreaped zombie, so its PID cannot have been reused here.
                rootChild = new Child(OpenPidFd(pid));
                children.Add(pid, rootChild);
            }
            finally
            {
                RestoreStandardDescriptor(controlDescriptor, 0, "restore_stdin");
                RestoreStandardDescriptor(protocolDescriptor, 1, "restore_stdout");
                RestoreStandardDescriptor(savedErrorDescriptor, 2, "restore_stderr");
                if (savedDirectory >= 0)
                {
                    CleanupStage("restore_directory",
                        () => RequireZero(Native.RestoreDirectory(savedDirectory), "fchdir"));
                }

                CloseOwned(ref stdoutWrite, "stdout_writer_close");
                CloseOwned(ref stderrWrite, "stderr_writer_close");
                CloseOwned(ref nullInput, "stdin_close");
                CloseOwned(ref savedDirectory, "directory_close");
            }
        }

        private void CleanupWorkload()
        {
            if (rootPid is null && stdoutRead < 0 && stderrRead < 0)
            {
                return;
            }

            long cleanupStarted = Stopwatch.GetTimestamp();
            bool noChildren = false;
            while (true)
            {
                CleanupStage("control_read", ReadControl);
                ObserveSignal();
                // Stop known producers before discovering newly adopted children;
                // discovery itself must not leave a running root free to fork.
                foreach (Child child in children.Values)
                {
                    RequestTermination(child);
                }

                CleanupStage("discover_children", () => DiscoverDirectChildren(terminate: true));
                bool reaped = false;
                CleanupStage("reap_children", () => reaped = ReapChildren());
                CleanupStage("verify_no_children", () => noChildren = HasNoChildren());
                CleanupStage("stdout_drain",
                    () => DrainOutput(ref stdoutRead, ref stdoutEof, ref stdoutReadFailed, "stdout"));
                CleanupStage("stderr_drain",
                    () => DrainOutput(ref stderrRead, ref stderrEof, ref stderrReadFailed, "stderr"));
                if (noChildren && stdoutEof && stderrEof)
                {
                    if (rootPid is not null && rootExitCode is null)
                    {
                        AddCleanupFailure("root_exit_status_missing");
                    }

                    return;
                }

                double remaining = CleanupTimeoutMilliseconds -
                    Stopwatch.GetElapsedTime(cleanupStarted).TotalMilliseconds;
                if (remaining <= 0)
                {
                    if (!noChildren) AddCleanupFailure("children_not_reaped_before_deadline");
                    if (!stdoutEof) AddCleanupFailure("stdout_eof_not_observed");
                    if (!stderrEof) AddCleanupFailure("stderr_eof_not_observed");
                    return;
                }

                // Reaping an intermediate exposes adopted descendants immediately,
                // including new-session leaders and nested subreapers.
                if (!reaped)
                {
                    CleanupStage("cleanup_poll", () =>
                        WaitForActivity((int)Math.Min(SampleIntervalMilliseconds, Math.Ceiling(remaining))));
                }
            }
        }

        private void RequestTermination(Child child)
        {
            if (child.TerminationRequested)
            {
                return;
            }

            int sent = SendSignal(child.Descriptor, Native.SigKill);
            int error = Marshal.GetLastPInvokeError();
            if (sent == 0 || error == Native.NoSuchProcess)
            {
                child.TerminationRequested = true;
            }
            else
            {
                AddCleanupFailure($"pidfd_signal_failed:errno={error.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        private void DiscoverDirectChildren(bool terminate)
        {
            // Never enumerate arbitrary PIDs or infer ancestry from parent-PID
            // snapshots. Only the kernel's direct-child lists establish custody.
            bool observedChildList = false;
            foreach (string taskDirectory in System.IO.Directory.EnumerateDirectories("/proc/self/task"))
            {
                string childList;
                try
                {
                    childList = File.ReadAllText(Path.Join(taskDirectory, "children"));
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                {
                    // A CLR thread can exit while its task directory is enumerated.
                    continue;
                }

                observedChildList = true;
                foreach (string value in childList.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    int pid = int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
                    if (children.ContainsKey(pid))
                    {
                        continue;
                    }

                    try
                    {
                        var child = new Child(OpenPidFd(pid));
                        children.Add(pid, child);
                        // If the initial pidfd acquisition failed, the root is
                        // still unreaped. Retain its object identity after reap so
                        // a later descendant reusing that PID is never the root.
                        if (pid == rootPid && rootChild is null)
                        {
                            rootChild = child;
                        }

                        if (terminate)
                        {
                            RequestTermination(child);
                        }
                    }
                    catch (Win32Exception exception)
                    {
                        // Keep the child unreaped. Other acquired children can
                        // still be killed/reaped, freeing descriptors for this one.
                        AddCleanupFailure(FailureCode(exception));
                    }
                }
            }

            if (!observedChildList)
            {
                throw new InvalidOperationException("Direct-child discovery is unavailable.");
            }
        }

        private bool ReapChildren()
        {
            bool reaped = false;
            foreach ((int pid, Child child) in children.ToArray())
            {
                if (!IsExited(child.Descriptor))
                {
                    continue;
                }

                if (ReferenceEquals(child, rootChild))
                {
                    ObserveRootExit();
                }

                int waited = Native.WaitPid(pid, out int status, Native.WaitNoHang | Native.WaitAllChildren);
                int error = Marshal.GetLastPInvokeError();
                if (waited == 0 || waited < 0 && error == Native.Interrupted)
                {
                    continue;
                }

                if (waited < 0)
                {
                    AddCleanupFailure($"child_wait_failed:errno={error.ToString(CultureInfo.InvariantCulture)}");
                    if (error != Native.NoChildren)
                    {
                        continue;
                    }
                }
                else if ((status & 0x7f) == 0x7f)
                {
                    // A ptrace stop is not a terminal wait, even without WUNTRACED.
                    continue;
                }
                else if (ReferenceEquals(child, rootChild))
                {
                    rootExitCode = (status & 0x7f) == 0 ? (status >> 8) & 0xff : 128 + (status & 0x7f);
                    if (rootExitCode != 0)
                    {
                        primaryFailure ??= "load_process_or_summary_failed";
                    }
                }

                // After wait, never use this numeric PID again. Close the pidfd
                // only after removing that lifetime from the ownership map.
                children.Remove(pid);
                CloseDescriptor(child.Descriptor, "pidfd_close");
                reaped = true;
            }

            return reaped;
        }

        private void ObserveRootExit()
        {
            exitedTimestamp ??= Stopwatch.GetTimestamp();
            if (!finalSampleTaken)
            {
                finalSampleTaken = true;
                SampleRoot();
            }
        }

        private void SampleRoot()
        {
            if (rootPid is not int pid)
            {
                return;
            }

            try
            {
                // Custody retains the root lifetime until after this read, including
                // its terminal CPU counters. RSS is instantaneous, not VmHWM.
                string stat = File.ReadAllText($"/proc/{pid.ToString(CultureInfo.InvariantCulture)}/stat");
                int commEnd = stat.LastIndexOf(')');
                if (commEnd < 0)
                {
                    throw new FormatException();
                }

                string[] fields = stat[(commEnd + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 22)
                {
                    throw new FormatException();
                }

                ulong ticks = checked(
                    ulong.Parse(fields[11], NumberStyles.None, CultureInfo.InvariantCulture) +
                    ulong.Parse(fields[12], NumberStyles.None, CultureInfo.InvariantCulture));
                long rss = checked(long.Parse(fields[21], CultureInfo.InvariantCulture) * Environment.SystemPageSize);
                long now = Stopwatch.GetTimestamp();
                double elapsed = Stopwatch.GetElapsedTime(lastSampleTimestamp, now).TotalSeconds;
                if (ticks < lastCpuTicks || rss < 0)
                {
                    throw new FormatException();
                }

                if (elapsed > 0)
                {
                    double cpu = (ticks - lastCpuTicks) * 100.0 /
                        clockTicksPerSecond / elapsed / Environment.ProcessorCount;
                    peakCpuPercent = Math.Max(peakCpuPercent, cpu);
                }

                peakRssBytes = Math.Max(peakRssBytes, rss);
                lastCpuTicks = ticks;
                lastSampleTimestamp = now;
                sampleCount++;
                if (rss > 512L * 1024 * 1024)
                {
                    primaryFailure ??= "load_generator_pressure_invalidates_measurement";
                }
            }
            catch (Exception exception) when (IsOperationalFailure(exception))
            {
                primaryFailure ??= $"measurement_failed:{FailureCode(exception)}";
            }
        }

        private void ReadControl()
        {
            if (controlEnded || controlDescriptor < 0)
            {
                return;
            }

            // A continuously writing controller cannot starve deadlines or cleanup.
            for (int batch = 0; batch < 8; batch++)
            {
                int count = ReadAvailable(controlDescriptor, readBuffer);
                if (count < 0)
                {
                    return;
                }

                if (count == 0)
                {
                    controlEnded = true;
                    cancelled = true;
                    primaryFailure ??= controlLength == 0 ? "controller_eof" : "control_line_incomplete";
                    return;
                }

                for (int index = 0; index < count; index++)
                {
                    byte value = readBuffer[index];
                    if (value == (byte)'\n')
                    {
                        ParseControlLine(controlLine.AsSpan(0, controlLength));
                        controlLength = 0;
                        if (controlEnded)
                        {
                            return;
                        }
                    }
                    else if (controlLength == controlLine.Length)
                    {
                        controlEnded = true;
                        primaryFailure ??= "control_line_too_long";
                        return;
                    }
                    else
                    {
                        controlLine[controlLength++] = value;
                    }
                }
            }
        }

        private void ParseControlLine(ReadOnlySpan<byte> line)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
                JsonElement message = document.RootElement;
                RejectDuplicateProperties(message);
                string? type = message.GetProperty("type").GetString();
                if (type == "cancel" && message.EnumerateObject().Count() == 1)
                {
                    cancelled = true;
                    primaryFailure ??= "cancelled";
                    controlEnded = true;
                    return;
                }

                if (type != "start" || request is not null)
                {
                    throw new JsonException();
                }

                StartRequest parsed = message.Deserialize<StartRequest>(JsonOptions) ?? throw new JsonException();
                ValidateStart(parsed);
                request = parsed;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or
                KeyNotFoundException or ArgumentException)
            {
                controlEnded = true;
                primaryFailure ??= "invalid_control_message";
            }
        }

        private static void RejectDuplicateProperties(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException();
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException();
                }

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    RejectDuplicateProperties(property.Value);
                }
            }
        }

        private static void ValidateStart(StartRequest start)
        {
            if (!ValidString(start.Executable) || !Path.IsPathFullyQualified(start.Executable) ||
                !ValidString(start.WorkingDirectory) || !Path.IsPathFullyQualified(start.WorkingDirectory) ||
                start.Arguments is null || start.Arguments.Length > 256 ||
                start.Arguments.Any(value => value is null || value.Contains('\0', StringComparison.Ordinal)) ||
                start.Environment is null || start.Environment.Count > 256 ||
                start.Environment.Any(pair => !ValidString(pair.Key) || pair.Key.Contains('=', StringComparison.Ordinal) ||
                    pair.Value is null || pair.Value.Contains('\0', StringComparison.Ordinal)) ||
                start.TimeoutMilliseconds <= 0 || start.CaptureLimit is not (0 or MaximumCaptureBytes))
            {
                throw new JsonException();
            }
        }

        private static bool ValidString(string? value) =>
            !string.IsNullOrEmpty(value) && !value.Contains('\0', StringComparison.Ordinal);

        private void DrainOutput(ref int descriptor, ref bool eof, ref bool failed, string name)
        {
            if (descriptor < 0 || eof || failed)
            {
                return;
            }

            try
            {
                for (int batch = 0; batch < 8; batch++)
                {
                    int count = ReadAvailable(descriptor, readBuffer);
                    if (count < 0)
                    {
                        return;
                    }

                    if (count == 0)
                    {
                        eof = true;
                        return;
                    }

                    if (request?.CaptureLimit == MaximumCaptureBytes)
                    {
                        int retained = Math.Min(count, capturedOutput.Length - captureLength);
                        readBuffer.AsSpan(0, retained).CopyTo(capturedOutput.AsSpan(captureLength));
                        captureLength += retained;
                        if (retained != count)
                        {
                            primaryFailure ??= "output_limit_exceeded";
                        }
                    }
                }
            }
            catch (Win32Exception exception)
            {
                failed = true;
                primaryFailure ??= $"{name}_read_failed:{FailureCode(exception)}";
            }
        }

        private void WaitForActivity(int timeoutMilliseconds)
        {
            var descriptors = new PollDescriptor[children.Count + 4];
            descriptors[0] = new(controlEnded ? -1 : controlDescriptor, Native.PollIn);
            descriptors[1] = new(stdoutEof || stdoutReadFailed ? -1 : stdoutRead, Native.PollIn);
            descriptors[2] = new(stderrEof || stderrReadFailed ? -1 : stderrRead, Native.PollIn);
            descriptors[3] = new(signalDescriptor, Native.PollIn);
            int index = 4;
            foreach (Child child in children.Values)
            {
                descriptors[index++] = new(child.Descriptor, Native.PollIn);
            }

            fixed (PollDescriptor* pointer = descriptors)
            {
                int result = Native.Poll(pointer, (nuint)descriptors.Length, timeoutMilliseconds);
                if (result < 0 && Marshal.GetLastPInvokeError() != Native.Interrupted)
                {
                    throw NativeFailure("poll");
                }
            }

            if (descriptors.Any(item => (item.ReturnedEvents & Native.PollInvalid) != 0))
            {
                throw new InvalidOperationException("An owned descriptor became invalid.");
            }
        }

        private void OnSignal(PosixSignalContext context)
        {
            context.Cancel = true;
            lock (signalGate)
            {
                signalRequested = true;
                if (signalDescriptor >= 0)
                {
                    ulong wake = 1;
                    // EAGAIN means the event is already readable. The callback
                    // performs no protocol I/O and never waits for the owner.
                    _ = Native.Write(signalDescriptor, (byte*)(&wake), sizeof(ulong));
                }
            }
        }

        private void ObserveSignal()
        {
            lock (signalGate)
            {
                if (!signalRequested)
                {
                    return;
                }

                signalRequested = false;
                cancelled = true;
                primaryFailure ??= "signal_received";
                ulong wake;
                if (signalDescriptor >= 0)
                {
                    _ = Native.Read(signalDescriptor, (byte*)(&wake), sizeof(ulong));
                }
            }
        }

        private bool WriteMessage(object message, int timeoutMilliseconds)
        {
            if (!protocolWritable || protocolDescriptor < 0)
            {
                return false;
            }

            try
            {
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
                if (payload.Length > MaximumLineBytes)
                {
                    protocolWritable = false;
                    return false;
                }

                byte[] line = new byte[payload.Length + 1];
                payload.CopyTo(line, 0);
                line[^1] = (byte)'\n';
                int written = 0;
                long began = Stopwatch.GetTimestamp();
                fixed (byte* pointer = line)
                {
                    while (written < line.Length)
                    {
                        if (written > 0 && Stopwatch.GetElapsedTime(began).TotalMilliseconds >= timeoutMilliseconds)
                        {
                            protocolWritable = false;
                            return false;
                        }

                        nint count = Native.Write(protocolDescriptor, pointer + written, (nuint)(line.Length - written));
                        int error = Marshal.GetLastPInvokeError();
                        if (count > 0)
                        {
                            written += (int)count;
                            continue;
                        }

                        double remaining = timeoutMilliseconds - Stopwatch.GetElapsedTime(began).TotalMilliseconds;
                        if (count == 0 || error is not (Native.Interrupted or Native.WouldBlock) || remaining <= 0)
                        {
                            protocolWritable = false;
                            return false;
                        }

                        var descriptor = new PollDescriptor(protocolDescriptor, Native.PollOut);
                        int polled = Native.Poll(&descriptor, 1, (int)Math.Ceiling(remaining));
                        if (polled < 0 && Marshal.GetLastPInvokeError() != Native.Interrupted)
                        {
                            protocolWritable = false;
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception exception) when (IsOperationalFailure(exception))
            {
                protocolWritable = false;
                return false;
            }
        }

        private void RestoreStandardDescriptor(int saved, int destination, string stage)
        {
            if (saved < 0)
            {
                return;
            }

            if (Native.DuplicateTo(saved, destination) < 0)
            {
                AddCleanupFailure($"{stage}:errno={Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture)}");
                // Do not retain a workload writer or write protocol into it.
                CloseDescriptor(destination, $"{stage}_close");
            }
        }

        private void CleanupStage(string stage, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception) when (IsOperationalFailure(exception))
            {
                AddCleanupFailure($"{stage}:{FailureCode(exception)}");
            }
        }

        private void CloseOwned(ref int descriptor, string stage)
        {
            int owned = descriptor;
            descriptor = -1;
            CloseDescriptor(owned, stage);
        }

        private void CloseDescriptor(int descriptor, string stage)
        {
            // Linux releases the descriptor even on EINTR; retrying close could
            // close an unrelated descriptor that reused its number.
            if (descriptor >= 0 && Native.Close(descriptor) < 0)
            {
                AddCleanupFailure($"{stage}:errno={Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture)}");
            }
        }

        private void AddCleanupFailure(string failure)
        {
            if (cleanupFailures.Contains(failure, StringComparer.Ordinal))
            {
                return;
            }

            if (cleanupFailures.Count < 31)
            {
                cleanupFailures.Add(failure);
            }
            else if (!cleanupFailureOverflow)
            {
                cleanupFailureOverflow = true;
                cleanupFailures.Add("additional_cleanup_failures");
            }
        }
    }

    private sealed record StartRequest
    {
        public required string Type { get; init; }
        public required string Executable { get; init; }
        public required string[] Arguments { get; init; }
        public required string WorkingDirectory { get; init; }
        public required Dictionary<string, string> Environment { get; init; }
        public required int TimeoutMilliseconds { get; init; }
        public required int CaptureLimit { get; init; }
    }

    private sealed class Child(int descriptor)
    {
        internal int Descriptor { get; } = descriptor;
        internal bool TerminationRequested { get; set; }
    }

    private sealed class NativeStrings : IDisposable
    {
        private readonly nint[] strings;
        internal nint Pointer { get; private set; }

        internal NativeStrings(string[] values)
        {
            strings = new nint[values.Length];
            try
            {
                Pointer = Marshal.AllocHGlobal(checked((values.Length + 1) * IntPtr.Size));
                for (int index = 0; index < values.Length; index++)
                {
                    strings[index] = Marshal.StringToCoTaskMemUTF8(values[index]);
                    Marshal.WriteIntPtr(Pointer, index * IntPtr.Size, strings[index]);
                }

                Marshal.WriteIntPtr(Pointer, values.Length * IntPtr.Size, 0);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            for (int index = 0; index < strings.Length; index++)
            {
                Marshal.FreeCoTaskMem(strings[index]);
                strings[index] = 0;
            }

            Marshal.FreeHGlobal(Pointer);
            Pointer = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollDescriptor(int descriptor, short events)
    {
        internal int Descriptor = descriptor;
        internal short Events = events;
        internal short ReturnedEvents = 0;
    }

    private static bool IsOperationalFailure(Exception exception) => exception is
        Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or
        ArgumentException or FormatException or OverflowException or JsonException or
        NotSupportedException or EntryPointNotFoundException or DllNotFoundException;

    private static string FailureCode(Exception exception) => exception is Win32Exception native
        ? $"{native.Message}:errno={native.NativeErrorCode.ToString(CultureInfo.InvariantCulture)}"
        : exception.GetType().Name;

    private static Win32Exception NativeFailure(string operation) =>
        new(Marshal.GetLastPInvokeError(), operation);

    private static int RequireDescriptor(int result, string operation) =>
        result >= 0 ? result : throw NativeFailure(operation);

    private static void RequireZero(int result, string operation)
    {
        if (result != 0)
        {
            throw NativeFailure(operation);
        }
    }

    private static int OpenPidFd(int pid) => RequireDescriptor(
        (int)Native.Syscall(Native.PidFdOpenNumber, pid, 0, 0, 0, 0, 0), "pidfd_open_failed");

    private static int SendSignal(int descriptor, int signal) =>
        (int)Native.Syscall(Native.PidFdSignalNumber, descriptor, signal, 0, 0, 0, 0);

    private static int MarkDescriptorsCloseOnExec() =>
        (int)Native.Syscall(Native.CloseRangeNumber, 3, unchecked((nint)uint.MaxValue), Native.CloseRangeCloseOnExec, 0, 0, 0);

    private static bool HasNoChildren()
    {
        // Linux deliberately accepts a null siginfo pointer. WNOWAIT preserves
        // every unseen lifetime; __WALL also includes non-SIGCHLD clone children.
        // No opaque siginfo_t buffer and no speculative waitpid(-1) reaping.
        int result = Native.WaitId(0, 0, 0,
            Native.WaitExited | Native.WaitNoHang | Native.WaitNoWait | Native.WaitAllChildren);
        if (result >= 0)
        {
            return false;
        }

        int error = Marshal.GetLastPInvokeError();
        if (error == Native.NoChildren)
        {
            return true;
        }

        if (error == Native.Interrupted)
        {
            return false;
        }

        throw NativeFailure("waitid");
    }

    private static bool IsExited(int descriptor)
    {
        var poll = new PollDescriptor(descriptor, Native.PollIn);
        int result = Native.Poll(&poll, 1, 0);
        if (result < 0)
        {
            if (Marshal.GetLastPInvokeError() == Native.Interrupted) return false;
            throw NativeFailure("pidfd_poll");
        }

        if ((poll.ReturnedEvents & (Native.PollInvalid | Native.PollError)) != 0)
        {
            throw new InvalidOperationException("Invalid pidfd readiness.");
        }

        return (poll.ReturnedEvents & (Native.PollIn | Native.PollHangup)) != 0;
    }

    private static void SetNonblocking(int descriptor)
    {
        int flags = RequireDescriptor(Native.Fcntl(descriptor, Native.GetStatusFlags, 0), "get_descriptor_flags");
        RequireZero(Native.Fcntl(descriptor, Native.SetStatusFlags, flags | Native.Nonblocking), "set_nonblocking");
    }

    private static void CreatePipe(ref int reader, ref int writer, ref bool eof)
    {
        int* descriptors = stackalloc int[2];
        RequireZero(Native.Pipe(descriptors, Native.CloseOnExec), "pipe2");
        reader = descriptors[0];
        writer = descriptors[1];
        eof = false;
        SetNonblocking(reader);
    }

    private static int ReadAvailable(int descriptor, byte[] buffer)
    {
        fixed (byte* pointer = buffer)
        {
            nint count = Native.Read(descriptor, pointer, (nuint)buffer.Length);
            if (count >= 0)
            {
                return (int)count;
            }

            int error = Marshal.GetLastPInvokeError();
            if (error is Native.WouldBlock or Native.Interrupted)
            {
                return -1;
            }

            throw NativeFailure("read");
        }
    }

    private static partial class Native
    {
        // Linux x86-64 UAPI constants, not libc opaque-structure layouts.
        internal const int PidFdSignalNumber = 424;
        internal const int PidFdOpenNumber = 434;
        internal const int CloseRangeNumber = 436;
        internal const int CloseRangeCloseOnExec = 4;
        internal const int SetChildSubreaper = 36;
        internal const int GetChildSubreaper = 37;
        internal const int ClockTicks = 2;
        internal const int SigKill = 9;
        internal const int SigPipe = 13;
        internal const int SigChild = 17;
        internal const int CloseOnExec = 0x80000;
        internal const int Directory = 0x10000;
        internal const int Nonblocking = 0x800;
        internal const int DuplicateCloseOnExec = 1030;
        internal const int GetDescriptorFlags = 1;
        internal const int DescriptorCloseOnExec = 1;
        internal const int GetStatusFlags = 3;
        internal const int SetStatusFlags = 4;
        internal const int WaitNoHang = 1;
        internal const int WaitExited = 4;
        internal const int WaitNoWait = 0x01000000;
        internal const int WaitAllChildren = 0x40000000;
        internal const int NoSuchProcess = 3;
        internal const int Interrupted = 4;
        internal const int NoChildren = 10;
        internal const int WouldBlock = 11;
        internal const short PollIn = 1;
        internal const short PollOut = 4;
        internal const short PollError = 8;
        internal const short PollHangup = 16;
        internal const short PollInvalid = 32;

        [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
        internal static partial nint Syscall(nint number, nint first, nint second, nint third, nint fourth, nint fifth, nint sixth);

        [LibraryImport("libc", EntryPoint = "posix_spawn", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int Spawn(out int pid, string path, nint actions, nint attributes, nint argv, nint environment);

        [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
        internal static partial int Prctl(int option, nint second, nint third, nint fourth, nint fifth);

        [LibraryImport("libc", EntryPoint = "signal", SetLastError = true)]
        internal static partial nint Signal(int signal, nint handler);

        [LibraryImport("libc", EntryPoint = "sysconf", SetLastError = true)]
        internal static partial long Sysconf(int name);

        [LibraryImport("libc", EntryPoint = "eventfd", SetLastError = true)]
        internal static partial int EventFd(uint initialValue, int flags);

        [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
        internal static partial int Fcntl(int descriptor, int command, int argument);

        [LibraryImport("libc", EntryPoint = "pipe2", SetLastError = true)]
        internal static partial int Pipe(int* descriptors, int flags);

        [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int Open(string path, int flags);

        [LibraryImport("libc", EntryPoint = "chdir", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int ChangeDirectory(string path);

        [LibraryImport("libc", EntryPoint = "fchdir", SetLastError = true)]
        internal static partial int RestoreDirectory(int descriptor);

        [LibraryImport("libc", EntryPoint = "dup2", SetLastError = true)]
        internal static partial int DuplicateTo(int source, int destination);

        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        internal static partial int Close(int descriptor);

        [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
        internal static partial nint Read(int descriptor, byte* buffer, nuint count);

        [LibraryImport("libc", EntryPoint = "write", SetLastError = true)]
        internal static partial nint Write(int descriptor, byte* buffer, nuint count);

        [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
        internal static partial int Poll(PollDescriptor* descriptors, nuint count, int timeout);

        [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
        internal static partial int WaitPid(int pid, out int status, int options);

        [LibraryImport("libc", EntryPoint = "waitid", SetLastError = true)]
        internal static partial int WaitId(int type, uint id, nint information, int options);
    }
}
