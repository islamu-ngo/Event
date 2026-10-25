using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering;

public static partial class SecurityExecutionEvidence
{
    /// <summary>Owns native execution; supplied reports and serialized records are never execution authority.</summary>
    public static int Execute(string[] args, TextWriter output, string repositoryRoot)
    {
        if (args.Length != 6 || args[0] != ExecutionCommandName ||
            args[2].Length is not (40 or 64) || !LowerHex(args[2]) ||
            !ulong.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out ulong run) || run == 0 ||
            !int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int attempt) || attempt < 1 ||
            args[5] is not ("push" or "pull_request" or "merge_group" or "workflow_dispatch" or "schedule"))
        {
            output.WriteLine("invalid_arguments: execute-security-execution requires <new-output-directory> <candidate-oid> <run-id> <run-attempt> <event-name>");
            return Program.UsageError;
        }
        if (!OperatingSystem.IsLinux())
        {
            output.WriteLine("security_execution_rejected: unsupported_execution_platform");
            return Program.ToolchainRejected;
        }
        using var cancellation = new CancellationTokenSource();
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            context.Cancel = true;
            cancellation.Cancel();
        });
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cancellation.Cancel();
        });
        try
        {
            string temporary = Environment.GetEnvironmentVariable("TMPDIR") ?? string.Empty;
            string host = Environment.GetEnvironmentVariable("DOCKER_HOST") ?? string.Empty;
            string ryuk = Environment.GetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED") ?? string.Empty;
            var request = new OwnedExecutionRequest(Path.GetFullPath(repositoryRoot),
                Path.GetFullPath(args[1], repositoryRoot), args[2], args[3], attempt, args[5],
                temporary, host, ryuk);
            ExecuteOwnedAsync(request, cancellation.Token).GetAwaiter().GetResult();
            output.WriteLine("security_execution_admitted");
            return Program.Success;
        }
        catch (Exception exception)
        {
            string code = exception is OwnedExecutionFailure owned ? owned.Code : "owned_execution_failed";
            output.WriteLine("security_execution_rejected: " + code);
            return Program.ToolchainRejected;
        }
    }

    private sealed record OwnedExecutionRequest(string Repository, string Output, string Candidate, string RunId,
        int Attempt, string Event, string Temporary, string DockerHost, string RyukDisabled);
    private sealed class OwnedExecutionFailure(string code) : Exception
    {
        internal string Code { get; } = code;
    }
    private sealed record OwnedEntry(string Project, string Directory, string Assembly);
    private sealed record OwnedInputs(CandidateSourceIdentity Source, string Fingerprint, byte[] Manifest);
    private sealed record OwnedChild(OwnedNativeProcessResult Process, byte[] Report, byte[] ResourceEvidence,
        string Owner, bool ResourcesAdmitted, bool TemporaryRemoved)
    {
        internal bool Succeeded => Process.Succeeded && ResourcesAdmitted && TemporaryRemoved;
    }
    private sealed class OwnedLane(string id, SecurityControlRegister.ExecutionRequirement requirement)
    {
        internal string Id { get; } = id;
        internal SecurityControlRegister.ExecutionRequirement Requirement { get; } = requirement;
        internal string Outcome { get; set; } = "not-run";
        internal OwnedChild? Discovery { get; set; }
        internal List<(string[] Uids, OwnedChild Child)> Invocations { get; } = [];
    }
    private sealed record CompletedOwnedRun(OwnedExecutionRequest Request,
        IReadOnlyList<OwnedLane> Lanes, IReadOnlyDictionary<string, IReadOnlyList<byte[]>> Proofs,
        IReadOnlyDictionary<string, IReadOnlyList<string>> InvocationDigests);

    private static async Task ExecuteOwnedAsync(OwnedExecutionRequest request, CancellationToken cancellationToken)
    {
        string? privateRoot = null;
        var lanes = new List<OwnedLane>();
        OwnedEntry[]? inputEntries = null;
        OwnedInputs? inputsBefore = null;
        string outcome = "failed";
        Exception? primaryFailure = null;
        Exception? auditFailureWithoutPrimary = null;
        try
        {
            if (!OperatingSystem.IsLinux() || !Path.IsPathFullyQualified(request.Temporary) ||
                InputDirectory(request.Temporary) != Path.TrimEndingDirectorySeparator(Path.GetTempPath()) ||
                InputWithin(request.Temporary, request.Repository) || request.Temporary == request.Repository)
                throw new OwnedExecutionFailure("native_prerequisites_not_admitted");
            privateRoot = FreshPrivateTemporary("islamu-execution-", request.Temporary);
            string register = Path.Combine(request.Repository, "eng", "security", "security-control-register.json");
            var requirements = SecurityControlRegister.ReadExecutionRequirements(register);
            lanes.AddRange(requirements.Select(item => new OwnedLane(item.Key, item.Value)));
            if (!string.Equals(request.RyukDisabled, "true", StringComparison.OrdinalIgnoreCase))
                throw new OwnedExecutionFailure("resource_reaper_disable_not_admitted");
            string socket = SelectedUnixSocket(request.DockerHost);
            string outputParent = Path.GetDirectoryName(request.Output) ?? string.Empty;
            if (request.Output.Any(char.IsControl) || request.Output != request.Output.TrimEnd(' ') ||
                File.Exists(request.Output) || Directory.Exists(request.Output) ||
                !PrivateDirectory(outputParent) || InputWithin(request.Output, request.Repository) ||
                request.Output == request.Repository)
                throw new OwnedExecutionFailure("output_destination_not_fresh");
            foreach (string name in new[]
            {
                "LD_PRELOAD", "LD_AUDIT", "LD_LIBRARY_PATH", "DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS",
                "DOTNET_SHARED_STORE", "CORECLR_PROFILER", "CORECLR_PROFILER_PATH", "CORECLR_PROFILER_PATH_32", "CORECLR_PROFILER_PATH_64"
            })
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
                    throw new OwnedExecutionFailure("native_launch_environment_not_admitted");
            using (Stream embedded = typeof(SecurityControlRegister).Assembly.GetManifestResourceStream(
                "ISLAMU.ReleaseEngineering.SecurityControlRegister.json") ?? throw new InvalidDataException())
            {
                if (!SHA256.HashData(embedded).AsSpan().SequenceEqual(SHA256.HashData(ReadBoundedFile(register, 1_048_576))))
                    throw new OwnedExecutionFailure("compiled_register_mismatch");
            }
            // A configured project path is not a witness for a DCP-managed child launch.
            // Those profiles remain ineligible until their live managed-entry/served-input witness is available.
            if (requirements.Values.Any(requirement => requirement.HostProfile != "dedicated-api-test-host"))
                throw new OwnedExecutionFailure("managed_launch_inputs_not_attested");
            var entries = requirements.Values.Select(requirement => requirement.Project).Distinct(StringComparer.Ordinal)
                .ToDictionary(project => project, project =>
                {
                    string directory = InputDirectory(Path.Combine(request.Repository, Path.GetDirectoryName(project)!, "bin", "Release", "net10.0"));
                    return new OwnedEntry(project, directory, Path.Combine(directory, Path.GetFileNameWithoutExtension(project) + ".dll"));
                }, StringComparer.Ordinal);
            inputEntries = entries.Values.ToArray();
            OwnedInputs before = CaptureOwnedInputs(request, inputEntries);
            inputsBefore = before;
            WritePrivateBytes(Path.Combine(privateRoot, "inputs-before.json"), before.Manifest);
            using (var daemon = new SelectedDaemon(socket))
            {
                foreach (OwnedLane lane in lanes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lane.Outcome = "failed";
                    OwnedEntry entry = entries[lane.Requirement.Project];
                    lane.Discovery = await InvokeOwnedChildAsync(request, entry, lane.Id, lane.Requirement, daemon,
                        null, cancellationToken).ConfigureAwait(false);
                    if (!lane.Discovery.Succeeded || lane.Discovery.Process.StandardError.Length != 0 ||
                        !TryReadNativeDiscovery(lane.Discovery.Process.StandardOutput, out var discovered))
                        throw new OwnedExecutionFailure("native_discovery_failed");
                    var identities = discovered.Select(node => node.Identity).ToHashSet();
                    bool owned = lane.Id.StartsWith("owned-", StringComparison.Ordinal);
                    if (owned ? !lane.Requirement.Tests.SetEquals(identities) : !lane.Requirement.Tests.IsSubsetOf(identities))
                        throw new OwnedExecutionFailure("native_discovery_requirements_mismatch");
                    string[][] selections = lane.Id is "owned-runtime" or "owned-load-control" or "owned-load-correctness"
                        ? discovered.Select(node => new[] { node.Uid }).ToArray()
                        : [discovered.Select(node => node.Uid).ToArray()];
                    foreach (string[] selected in selections)
                    {
                        OwnedChild child = await InvokeOwnedChildAsync(request, entry, lane.Id, lane.Requirement, daemon,
                            selected, cancellationToken).ConfigureAwait(false);
                        lane.Invocations.Add((selected, child));
                        if (!child.Succeeded || child.Report.Length == 0)
                        {
                            lane.Outcome = child.Process.Cancelled ? "cancelled" : child.Process.TimedOut ? "timed-out" : "failed";
                            throw new OwnedExecutionFailure("native_execution_failed");
                        }
                    }
                    lane.Outcome = "passed";
                }
            }
            OwnedInputs after = CaptureOwnedInputs(request, entries.Values);
            WritePrivateBytes(Path.Combine(privateRoot, "inputs-after.json"), after.Manifest);
            if (before.Fingerprint != after.Fingerprint || before.Source != after.Source)
                throw new OwnedExecutionFailure("execution_inputs_changed");
            var native = lanes.ToDictionary(lane => lane.Id,
                lane => new NativeLaneInput(lane.Discovery!.Process.StandardOutput,
                    lane.Invocations.Select(item => new NativeInvocationInput(item.Uids, item.Child.Report)).ToArray()),
                StringComparer.Ordinal);
            if (!TryCreateNativeRunProofs(native, register, out var proofs, out _))
                throw new OwnedExecutionFailure("native_run_join_failed");
            var digests = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (OwnedLane lane in lanes)
            {
                var laneDigests = new List<string>();
                for (int index = 0; index < lane.Invocations.Count; index++)
                {
                    var invocation = lane.Invocations[index];
                    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        schemaVersion = 1, synthetic = false, candidateOid = request.Candidate, treeOid = before.Source.TreeOid,
                        runId = request.RunId, runAttempt = request.Attempt, lane = lane.Id, invocation = index,
                        inputsBeforeSha256 = before.Fingerprint, inputsAfterSha256 = after.Fingerprint,
                        selectedUids = invocation.Uids,
                        discovery = ChildFacts(lane.Discovery!), execution = ChildFacts(invocation.Child),
                        privateDiscoverySha256 = DigestBytes(lane.Discovery!.Process.StandardOutput),
                        privateTrxSha256 = DigestBytes(invocation.Child.Report)
                    });
                    string directory = PrivateChildDirectory(privateRoot, lane.Id + "-" + index.ToString(CultureInfo.InvariantCulture));
                    WritePrivateBytes(Path.Combine(directory, "discovery.json"), lane.Discovery!.Process.StandardOutput);
                    WritePrivateBytes(Path.Combine(directory, "execution.trx"), invocation.Child.Report);
                    WritePrivateBytes(Path.Combine(directory, "stdout.bin"), invocation.Child.Process.StandardOutput);
                    WritePrivateBytes(Path.Combine(directory, "stderr.bin"), invocation.Child.Process.StandardError);
                    string path = Path.Combine(directory, "invocation.json");
                    WritePrivateBytes(path, bytes);
                    if (!ReadBoundedFile(path, MaximumReportBytes).AsSpan().SequenceEqual(bytes))
                        throw new OwnedExecutionFailure("private_record_write_failed");
                    laneDigests.Add(DigestBytes(bytes));
                }
                digests.Add(lane.Id, laneDigests.AsReadOnly());
            }
            cancellationToken.ThrowIfCancellationRequested();
            WritePrivateBytes(Path.Combine(privateRoot, "attempt.json"), AttemptSummary("native-passed", request, lanes));
            PublishOwnedRun(new CompletedOwnedRun(request, lanes.AsReadOnly(), proofs!, digests), privateRoot, register);
            outcome = "passed";
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            if (privateRoot is not null && outcome != "passed")
            {
                try
                {
                    if (inputEntries is not null)
                    {
                        try
                        {
                            OwnedInputs failedAfter = CaptureOwnedInputs(request, inputEntries);
                            WritePrivateBytes(Path.Combine(privateRoot, "inputs-after-failure.json"), failedAfter.Manifest);
                            WritePrivateBytes(Path.Combine(privateRoot, "failed-input-status.json"), JsonSerializer.SerializeToUtf8Bytes(new
                            {
                                postObservation = "captured", unchanged = inputsBefore is not null &&
                                    inputsBefore.Fingerprint == failedAfter.Fingerprint && inputsBefore.Source == failedAfter.Source
                            }));
                        }
                        catch (Exception inputFailure)
                        {
                            if (primaryFailure is not null) primaryFailure.Data["post_input_failure"] = inputFailure;
                            WritePrivateBytes(Path.Combine(privateRoot, "failed-input-status.json"),
                                JsonSerializer.SerializeToUtf8Bytes(new { postObservation = "unavailable", unchanged = false }));
                        }
                    }
                    WritePrivateBytes(Path.Combine(privateRoot, "failure.json"), AttemptSummary(outcome, request, lanes));
                    foreach (OwnedLane lane in lanes)
                    {
                        IEnumerable<OwnedChild> children = lane.Discovery is null ? lane.Invocations.Select(item => item.Child)
                            : new[] { lane.Discovery! }.Concat(lane.Invocations.Select(item => item.Child));
                        int index = 0;
                        foreach (OwnedChild child in children)
                        {
                            string directory = PrivateChildDirectory(privateRoot, "failed-" + lane.Id + "-" + (index++).ToString(CultureInfo.InvariantCulture));
                            WritePrivateBytes(Path.Combine(directory, "stdout.bin"), child.Process.StandardOutput);
                            WritePrivateBytes(Path.Combine(directory, "stderr.bin"), child.Process.StandardError);
                            if (child.Report.Length != 0) WritePrivateBytes(Path.Combine(directory, "execution.trx"), child.Report);
                        }
                    }
                }
                catch (Exception auditFailure)
                {
                    if (primaryFailure is not null) primaryFailure.Data["private_audit_failure"] = auditFailure;
                    else auditFailureWithoutPrimary = auditFailure;
                }
            }
        }
        if (auditFailureWithoutPrimary is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(auditFailureWithoutPrimary).Throw();
    }

    private static byte[] AttemptSummary(string outcome, OwnedExecutionRequest request, IReadOnlyList<OwnedLane> lanes) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, outcome, candidateOid = request.Candidate,
            lanes = lanes.Select(lane => new { id = lane.Id, outcome = lane.Outcome,
                discovery = lane.Discovery is null ? null : ChildFacts(lane.Discovery),
                invocations = lane.Invocations.Select(item => new { selectedUids = item.Uids, execution = ChildFacts(item.Child) }) })
        });

    private static object ChildFacts(OwnedChild child) => new
    {
        owner = child.Owner, child.Process.ExitCode, child.Process.TimedOut, child.Process.Cancelled,
        child.Process.ResidualOwnedChildren, child.Process.OutputLimitExceeded, child.Process.CleanupSucceeded,
        child.Process.ObserverSettled, observerFailureCount = child.Process.ObserverFailures.Count,
        child.ResourcesAdmitted, child.TemporaryRemoved,
        reportCaptured = child.Report.Length != 0, reportBytes = child.Report.Length,
        stdoutSha256 = DigestBytes(child.Process.StandardOutput), stderrSha256 = DigestBytes(child.Process.StandardError),
        resourceEvidence = JsonSerializer.Deserialize<JsonElement>(child.ResourceEvidence)
    };

    internal static (ProcessStartInfo Command, TimeSpan Deadline) NativeExecutionStartInfo(
        string assemblyPath, IReadOnlyList<string> selectedUids, string resultsDirectory,
        string temporaryDirectory, string reportFileName, string hostProfile, string laneId)
    {
        if (string.IsNullOrEmpty(laneId) ||
            !SecurityControlRegister.ExecutionRequirements.TryGetValue(laneId, out var requirement) ||
            requirement.HostProfile != hostProfile || selectedUids.Count is < 1 or > 1024 ||
            (laneId is "owned-runtime" or "owned-load-control" or "owned-load-correctness" && selectedUids.Count != 1))
            throw new InvalidDataException("native_batch_invalid");

        // Native --timeout covers the serial batch. These are hang allowances, not reserved case runtimes.
        int timeoutSeconds = laneId switch
        {
            "security-integration" or "cerbos-policy-contract" => 600,
            "owned-runtime" => 720,
            "owned-browser-journeys" => Math.Min(checked(1200 * selectedUids.Count), 19_800),
            "owned-browser-security" => Math.Min(checked(240 * selectedUids.Count + 1020), 19_800),
            "owned-load-control" or "owned-load-correctness" => 1500,
            _ => throw new InvalidDataException("native_batch_invalid")
        };
        return (NativeTestStartInfo(assemblyPath, selectedUids, resultsDirectory, temporaryDirectory,
            reportFileName, timeoutSeconds), TimeSpan.FromSeconds(timeoutSeconds + 15));
    }

    private static async Task<OwnedChild> InvokeOwnedChildAsync(OwnedExecutionRequest request, OwnedEntry entry, string laneId,
        SecurityControlRegister.ExecutionRequirement requirement, SelectedDaemon daemon, string[]? uids,
        CancellationToken cancellationToken)
    {
        string temporary = FreshPrivateTemporary("islamu-native-", request.Temporary);
        string owner = Guid.NewGuid().ToString("N");
        OwnedNativeProcessResult? process = null;
        byte[] report = [];
        byte[] evidence = JsonSerializer.SerializeToUtf8Bytes(new { observed = false, cleanup = false });
        bool admitted = false;
        bool removed = false;
        bool resourceCleanupComplete = false;
        try
        {
            string context = PrivateChildDirectory(PrivateChildDirectory(temporary, "islamu-assurance"), owner);
            WritePrivateBytes(Path.Combine(context, ".islamu-test-owner"), Encoding.ASCII.GetBytes(owner));
            string results = PrivateChildDirectory(temporary, "results");
            ProcessStartInfo command;
            TimeSpan deadline = TimeSpan.FromSeconds(60);
            if (uids is null)
            {
                command = new ProcessStartInfo(ReleaseToolPaths.Dotnet)
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    CreateNoWindow = true, WorkingDirectory = temporary
                };
                foreach (string argument in new[] { entry.Assembly, "--list-tests", "json", "--treenode-filter", requirement.Selector, "--no-ansi" })
                    command.ArgumentList.Add(argument);
            }
            else (command, deadline) = NativeExecutionStartInfo(entry.Assembly, uids, results, temporary,
                "execution.trx", requirement.HostProfile, laneId);
            command.Environment["TMPDIR"] = temporary;
            command.Environment["ISLAMU_TEST_RUN_ID"] = owner;
            command.Environment["ISLAMU_TEST_ROOT"] = context;
            command.Environment["DOCKER_HOST"] = request.DockerHost;
            command.Environment["TESTCONTAINERS_RYUK_DISABLED"] = request.RyukDisabled;
            command.Environment["DOTNET_ENVIRONMENT"] = requirement.Environment;
            command.Environment["ASPNETCORE_ENVIRONMENT"] = requirement.Environment;
            var observed = await daemon.RunAsync(owner, temporary, context, command, uids is not null,
                deadline, cancellationToken).ConfigureAwait(false);
            process = observed.Process;
            admitted = observed.Admitted;
            evidence = observed.Evidence;
            resourceCleanupComplete = observed.ResourceCleanupComplete;
            if (uids is not null && process.CleanupSucceeded && process.ObserverSettled)
                _ = TryReadOwnedChildReport(results, "execution.trx", out report);
        }
        finally
        {
            try
            {
                if (process is { CleanupSucceeded: true, ObserverSettled: true } && resourceCleanupComplete && NoInputLinks(temporary))
                {
                    Directory.Delete(temporary, recursive: true);
                    removed = !Directory.Exists(temporary);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            { removed = false; }
        }
        return new(process ?? throw new OwnedExecutionFailure("native_process_missing"), report, evidence, owner, admitted, removed);
    }

    internal static bool TryReadOwnedChildReport(string results, string filename, out byte[] report)
    {
        report = [];
        try
        {
            string path = Path.Combine(results, filename);
            if (!File.Exists(path) || !Directory.EnumerateFiles(results, "*.trx", SearchOption.TopDirectoryOnly)
                    .ToHashSet(StringComparer.Ordinal).SetEquals([path]) || !NoInputLinks(path) ||
                !AreRegularCandidateInputsAsync([path]).GetAwaiter().GetResult()) return false;
            report = ReadBoundedFile(path, MaximumReportBytes);
            return report.Length != 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or NotSupportedException or OverflowException or
            System.ComponentModel.Win32Exception or TimeoutException or OperationCanceledException)
        {
            report = [];
            return false;
        }
    }

    private static OwnedInputs CaptureOwnedInputs(OwnedExecutionRequest request, IEnumerable<OwnedEntry> entries)
    {
        if (!TryReadCandidateSource(request.Repository, request.Candidate, out var source, out _))
            throw new OwnedExecutionFailure("candidate_source_invalid");
        var managed = new List<object>();
        string engine = typeof(SecurityExecutionEvidence).Assembly.Location;
        if (!TryCaptureProjectManagedInputs(Path.GetDirectoryName(engine)!, request.Candidate, [Path.GetFileName(engine)],
                out var own, out _)) throw new OwnedExecutionFailure("producer_inputs_invalid");
        managed.Add(new { project = "producer", digest = own!.PrivateManifestSha256, manifest = JsonSerializer.Deserialize<JsonElement>(own.PrivateManifest) });
        foreach (OwnedEntry entry in entries.OrderBy(item => item.Project, StringComparer.Ordinal))
        {
            if (!TryCaptureProjectManagedInputs(entry.Directory, request.Candidate, [Path.GetFileName(entry.Assembly)],
                    out var captured, out _))
                throw new OwnedExecutionFailure("selected_test_inputs_invalid");
            managed.Add(new { project = entry.Project, digest = captured!.PrivateManifestSha256,
                manifest = JsonSerializer.Deserialize<JsonElement>(captured.PrivateManifest) });
            if (SecurityControlRegister.ExecutionRequirements.Values.Any(requirement =>
                requirement.Project == entry.Project && requirement.HostProfile == "dedicated-api-test-host"))
            {
                if (!TryCaptureApiPrerequisiteInputs(request.Repository, request.Candidate, entry.Directory, out var content, out _))
                    throw new OwnedExecutionFailure("selected_test_inputs_invalid");
                managed.Add(new { project = entry.Project + ":content", digest = content!.PrivateManifestSha256,
                    manifest = JsonSerializer.Deserialize<JsonElement>(content.PrivateManifest) });
            }
        }
        byte[] tools = CaptureExecutionTools();
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, candidateOid = source!.CandidateOid, treeOid = source.TreeOid, managed,
            tools = JsonSerializer.Deserialize<JsonElement>(tools)
        });
        if (manifest.Length > MaximumReportBytes) throw new OwnedExecutionFailure("execution_inputs_too_large");
        return new(source, DigestBytes(manifest), manifest);
    }

    private static byte[] CaptureExecutionTools()
    {
        string shared = InputDirectory(Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..")));
        var paths = new HashSet<string>(StringComparer.Ordinal)
            { ReleaseToolPaths.Dotnet, ReleaseToolPaths.Git, "/usr/bin/setsid", "/usr/bin/kill", "/usr/bin/stat" };
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((shared, 0));
        int entries = 0;
        while (pending.TryPop(out var item))
        {
            if (++entries > 100_000 || item.Depth > 64 || !NoInputLinks(item.Path))
                throw new OwnedExecutionFailure("native_tool_inputs_invalid");
            if ((File.GetAttributes(item.Path) & FileAttributes.Directory) != 0)
                foreach (string child in Directory.EnumerateFileSystemEntries(item.Path)) pending.Push((child, item.Depth + 1));
            else if (!paths.Add(item.Path)) throw new OwnedExecutionFailure("native_tool_inputs_invalid");
        }
        string[] files = paths.Order(StringComparer.Ordinal).ToArray();
        if (files.Length > 65_536 || files.Any(path => !NoInputLinks(path)) ||
            !AreRegularCandidateInputsAsync(files).GetAwaiter().GetResult())
            throw new OwnedExecutionFailure("native_tool_inputs_invalid");
        var captured = new List<object>();
        long total = 0;
        foreach (string path in files)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long size = stream.Length;
            if (size > 1_073_741_824 || (total = checked(total + size)) > 17_179_869_184)
                throw new OwnedExecutionFailure("native_tool_inputs_invalid");
            string digest = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (stream.Length != size || !NoInputLinks(path)) throw new OwnedExecutionFailure("native_tool_inputs_invalid");
            captured.Add(new { path, bytes = size, sha256 = digest });
        }
        return JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, files = captured });
    }

    private static void PublishOwnedRun(CompletedOwnedRun completed, string privateRoot, string register)
    {
        string stage = PrivateChildDirectory(privateRoot, "public");
        var lanes = new List<object>();
        var expected = new HashSet<string>(StringComparer.Ordinal) { PacketFileName };
        foreach (OwnedLane lane in completed.Lanes)
        {
            var artifacts = new List<object>();
            int selected = 0;
            for (int index = 0; index < completed.Proofs[lane.Id].Count; index++)
            {
                byte[] proof = completed.Proofs[lane.Id][index];
                using JsonDocument document = JsonDocument.Parse(proof, DocumentOptions);
                selected = checked(selected + JsonCounts(document.RootElement.GetProperty("counts")).Selected);
                string relative = "proofs/" + lane.Id + "/" + index.ToString(CultureInfo.InvariantCulture) + ".json";
                string directory = Path.GetDirectoryName(Path.Combine(stage, relative))!;
                Directory.CreateDirectory(directory);
                WritePrivateBytes(Path.Combine(stage, relative), proof);
                expected.Add(relative);
                artifacts.Add(new { kind = "native-proof", path = relative, sha256 = DigestBytes(proof), invocation = index,
                    privateInvocationSha256 = completed.InvocationDigests[lane.Id][index] });
            }
            var requirement = lane.Requirement;
            lanes.Add(new
            {
                id = lane.Id, project = requirement.Project, selector = requirement.Selector, command = ExecutionCommandName,
                runId = completed.Request.RunId, runAttempt = completed.Request.Attempt, outcome = "passed", exitCode = 0,
                counts = new { selected, executed = selected, passed = selected, failed = 0, skipped = 0 },
                executionContext = new { kind = "test-host", environment = requirement.Environment, hostProfile = requirement.HostProfile,
                    databaseProvider = "not-attested", databaseRole = "not-attested", authenticationScheme = "not-attested",
                    authenticationAuthority = "not-attested" }, artifacts
            });
        }
        WritePrivateBytes(Path.Combine(stage, PacketFileName), JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = "security-execution.v1", candidateOid = completed.Request.Candidate,
            workflowPath = ".github/workflows/security-tests.yml", runId = completed.Request.RunId,
            runAttempt = completed.Request.Attempt, eventName = completed.Request.Event, selection = "executed", lanes
        }));
        if (!SafePublishedInventory(stage, expected) ||
            !TryValidate(stage, completed.Request.Candidate, register, out string? before, out _) ||
            !PrivateDirectory(Path.GetDirectoryName(completed.Request.Output)!))
            throw new OwnedExecutionFailure("safe_packet_validation_failed");
        PublishFreshPublicDirectory(stage, completed.Request.Output, directory =>
            SafePublishedInventory(directory, expected) &&
            TryValidate(directory, completed.Request.Candidate, register, out string? after, out _) && before == after);
    }

    internal static void PublishFreshPublicDirectory(string stage, string destination, Func<string, bool> readback)
    {
        if (!PrivateDirectory(Path.GetDirectoryName(destination)!))
            throw new OwnedExecutionFailure("safe_packet_publication_failed");
        bool moved = false;
        try
        {
            Directory.Move(stage, destination);
            moved = true;
            if (!readback(destination)) throw new OwnedExecutionFailure("safe_packet_publication_failed");
        }
        catch (Exception failure)
        {
            if (moved)
            {
                try { Directory.Move(destination, stage); }
                catch (Exception rollbackFailure)
                {
                    failure.Data["public_rollback_failure"] = rollbackFailure;
                    try
                    {
                        if (!NoInputLinks(destination)) throw new OwnedExecutionFailure("public_invalidation_path_invalid");
                        File.Delete(ResolveArtifact(destination, PacketFileName));
                    }
                    catch (Exception invalidationFailure) { failure.Data["public_invalidation_failure"] = invalidationFailure; }
                }
            }
            throw;
        }
    }

    private static bool SafePublishedInventory(string root, IReadOnlySet<string> expected)
    {
        if (!NoInputLinks(root)) return false;
        var actual = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(root);
        int entries = 0;
        while (pending.TryPop(out string? directory))
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > 16_384 || !NoInputLinks(path)) return false;
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) pending.Push(path);
                else if (!actual.Add(Path.GetRelativePath(root, path))) return false;
            }
        }
        return actual.SetEquals(expected) && AreRegularCandidateInputsAsync(actual.Select(path => Path.Combine(root, path)).ToArray())
            .GetAwaiter().GetResult();
    }

    private static string SelectedUnixSocket(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != "unix" ||
            endpoint.Host.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            !Path.IsPathFullyQualified(endpoint.LocalPath) || value != "unix://" + Path.GetFullPath(endpoint.LocalPath) ||
            !NoInputLinks(endpoint.LocalPath)) throw new OwnedExecutionFailure("explicit_unix_daemon_required");
        return endpoint.LocalPath;
    }

    private static string FreshPrivateTemporary(string prefix, string parent)
    {
        string path = Directory.CreateTempSubdirectory(prefix).FullName;
        if (Path.GetDirectoryName(path) != Path.TrimEndingDirectorySeparator(parent) || !PrivateDirectory(path) ||
            Directory.EnumerateFileSystemEntries(path).Any()) throw new OwnedExecutionFailure("private_run_root_invalid");
        return path;
    }

    private static bool PrivateDirectory(string path) => OperatingSystem.IsLinux() && Directory.Exists(path) &&
        NoInputLinks(path) && File.GetUnixFileMode(path) ==
        (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

    private static string PrivateChildDirectory(string parent, string name)
    {
        if (!OperatingSystem.IsLinux() || !PrivateDirectory(parent)) throw new OwnedExecutionFailure("private_run_root_invalid");
        string path = Path.Combine(parent, name);
        if (Directory.Exists(path) || File.Exists(path)) throw new OwnedExecutionFailure("private_run_root_not_fresh");
        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (!PrivateDirectory(path)) throw new OwnedExecutionFailure("private_run_root_invalid");
        return path;
    }

    private static void WritePrivateBytes(string path, byte[] bytes)
    {
        if (!OperatingSystem.IsLinux() || !NoInputLinks(Path.GetDirectoryName(path)!))
            throw new OwnedExecutionFailure("private_artifact_path_invalid");
        using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite, Options = FileOptions.WriteThrough
        });
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static string DigestBytes(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class SelectedDaemon : IDisposable
    {
        private readonly SocketsHttpHandler _handler;
        private readonly HttpClient _client;

        internal SelectedDaemon(string socketPath)
        {
            _handler = new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(3) };
            try
            {
                _handler.ConnectCallback = async (_, token) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                };
                _client = new HttpClient(_handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost"),
                    Timeout = Timeout.InfiniteTimeSpan };
            }
            catch (Exception constructionFailure)
            {
                try { _handler.Dispose(); }
                catch (Exception cleanupFailure) { throw new AggregateException(constructionFailure, cleanupFailure); }
                throw;
            }
        }

        private sealed record ApiCreationFile(string Path, LiveFileIdentity Identity, string Sha256);
        private sealed record ApiCreationProof(NativeCreatedContainer Creation, byte[] PrivateManifest,
            IReadOnlyList<ApiCreationFile> Files);

        private static Dictionary<string, string[]> ReadApiCreationCandidates(string root)
        {
            string[] paths = Directory.EnumerateFiles(root, "api-container-*.json").Take(129).Order(StringComparer.Ordinal).ToArray();
            if (paths.Length > 128) throw new OwnedExecutionFailure("api_creation_inventory_limit");
            var candidates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                string name = Path.GetFileName(path);
                const string declaration = "-declaration.json", created = "-created-";
                int end = name.EndsWith(declaration, StringComparison.Ordinal)
                    ? name.Length - declaration.Length : name.LastIndexOf(created, StringComparison.Ordinal);
                if (end < "api-container-".Length + 34 || name[end - 33] != '-')
                    throw new OwnedExecutionFailure("api_creation_filename_invalid");
                string id = name.Substring(end - 32, 32);
                if (!LowerHex(id) || id[12] != '7' || id[16] is not ('8' or '9' or 'a' or 'b'))
                    throw new OwnedExecutionFailure("api_creation_filename_invalid");
                if (!candidates.TryGetValue(id, out List<string>? files))
                {
                    if (candidates.Count >= 64) throw new OwnedExecutionFailure("api_creation_inventory_limit");
                    files = [];
                    candidates.Add(id, files);
                }
                files.Add(path);
            }
            return candidates.ToDictionary(item => item.Key, item => item.Value.ToArray(), StringComparer.Ordinal);
        }

        private static async Task ValidateApiRootAsync(string root, string owner, uint user, CancellationToken token)
        {
            string marker = Path.Combine(root, ".islamu-test-owner");
            LiveFileIdentity directory = await ReadLiveMetadataAsync(root, false, token).ConfigureAwait(false);
            LiveFileIdentity identity = await ReadLiveMetadataAsync(marker, false, token).ConfigureAwait(false);
            if (!NoInputLinks(root) || !NoInputLinks(marker) || (directory.Bits & 0xf000) != 0x4000
                || (directory.Bits & 0xfff) != 0x1c0 || directory.User != user || !PrivateLiveFile(identity, user, 32)
                || Encoding.ASCII.GetString(ReadBoundedFile(marker, 32)) != owner)
                throw new OwnedExecutionFailure("api_creation_root_invalid");
        }

        private static async Task<ApiCreationProof?> CaptureApiCreationAsync(OwnedNativeProcessContext context,
            string temporary, string root, string owner, string creationId, CancellationToken token)
        {
            NativeContainerCreationSnapshot? capture = await CaptureLiveNativeContainerCreationsAsync(
                context, temporary, owner, "api", token, creationId).ConfigureAwait(false);
            if (capture is null || capture.Created.Count == 0) return null;
            if (capture.Created.Count != 1 || capture.PendingCreationIds.Count != 0)
                throw new OwnedExecutionFailure("api_creation_capture_invalid");
            NativeCreatedContainer pair = capture.Created[0];
            if (pair.RunId != owner || pair.CreatorPid != context.Pid || pair.CreationId != creationId)
                throw new OwnedExecutionFailure("api_creation_capture_invalid");
            using JsonDocument manifest = JsonDocument.Parse(capture.PrivateManifest, DocumentOptions);
            uint user = manifest.RootElement.GetProperty("creator").GetProperty("User").GetUInt32();
            JsonElement records = manifest.RootElement.GetProperty("records");
            if (records.ValueKind != JsonValueKind.Array || records.GetArrayLength() != 2)
                throw new OwnedExecutionFailure("api_creation_capture_invalid");
            string stem = Path.Combine(root, "api-container-" + pair.Resource + "-" + pair.CreationId);
            var expected = new HashSet<string>(StringComparer.Ordinal)
                { stem + "-declaration.json", stem + "-created-" + pair.ContainerId + ".json" };
            var files = new List<ApiCreationFile>();
            foreach (JsonElement record in records.EnumerateArray())
            {
                string path = Text(record, "path"), digest = Text(record, "sha256");
                if (!expected.Remove(path) || !NoInputLinks(path))
                    throw new OwnedExecutionFailure("api_creation_capture_changed");
                LiveFileIdentity identity = await ReadLiveMetadataAsync(path, false, token).ConfigureAwait(false);
                if (!PrivateLiveFile(identity, user, identity.Bytes)
                    || identity.Bytes is < 1 or > 16384 || digest.Length != 64 || !LowerHex(digest)
                    || identity.Device != record.GetProperty("Device").GetUInt64()
                    || identity.Inode != record.GetProperty("Inode").GetUInt64()
                    || DigestBytes(ReadBoundedFile(path, 16384)) != digest)
                    throw new OwnedExecutionFailure("api_creation_capture_changed");
                files.Add(new(path, identity, digest));
            }
            if (expected.Count != 0) throw new OwnedExecutionFailure("api_creation_capture_invalid");
            return new(pair, capture.PrivateManifest, files.AsReadOnly());
        }

        private static async Task ValidateApiCreationAsync(ApiCreationProof proof, IReadOnlyList<string> paths,
            CancellationToken token)
        {
            if (!paths.ToHashSet(StringComparer.Ordinal).SetEquals(proof.Files.Select(file => file.Path)))
                throw new OwnedExecutionFailure("api_creation_records_changed");
            foreach (ApiCreationFile file in proof.Files)
                if (!NoInputLinks(file.Path) || await ReadLiveMetadataAsync(file.Path, false, token).ConfigureAwait(false) != file.Identity
                    || DigestBytes(ReadBoundedFile(file.Path, 16384)) != file.Sha256)
                    throw new OwnedExecutionFailure("api_creation_records_changed");
        }

        internal async Task<(OwnedNativeProcessResult Process, bool Admitted, byte[] Evidence, bool ResourceCleanupComplete)> RunAsync(
            string owner, string temporary, string root, ProcessStartInfo command, bool requireRunningPrerequisites, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            byte[] before = await IdentityAsync(CancellationToken.None).ConfigureAwait(false);
            HashSet<string> cachedImageIds = await CachedImageIdsAsync(CancellationToken.None).ConfigureAwait(false);
            if (requireRunningPrerequisites && cachedImageIds.Count == 0)
                throw new OwnedExecutionFailure("cached_image_inputs_missing");
            HashSet<string> initial = await InitialContainersAsync(owner, CancellationToken.None).ConfigureAwait(false);
            var initialResources = new InitialNativeResources(initial, new HashSet<string>(StringComparer.Ordinal), cachedImageIds);
            var qualified = new Dictionary<string, ApiCreationProof>(StringComparer.Ordinal);
            var seenCreations = new HashSet<string>(StringComparer.Ordinal);
            var seenOwnedIds = new HashSet<string>(StringComparer.Ordinal);
            var images = new Dictionary<string, string>(StringComparer.Ordinal);
            var known = new Dictionary<string, ObservedOwnedResource>(StringComparer.Ordinal);
            var running = new HashSet<string>(StringComparer.Ordinal);
            bool cleanup = false;
            byte[] after = [];
            var failures = new List<Exception>();
            uint? nativeUser = null;

            void RetainCandidates(Dictionary<string, string[]> candidates)
            {
                foreach (string id in candidates.Keys)
                    if (seenCreations.Add(id) && seenCreations.Count > 64)
                        throw new OwnedExecutionFailure("api_creation_inventory_limit");
                foreach (ApiCreationProof proof in qualified.Values)
                    if (!candidates.ContainsKey(proof.Creation.CreationId))
                        throw new OwnedExecutionFailure("api_creation_records_changed");
            }

            NativeContainerCreationSnapshot QualifiedSnapshot() => new(
                qualified.Values.Select(proof => proof.Creation).ToArray(), new HashSet<string>(StringComparer.Ordinal), []);

            void RetainResources(IReadOnlyList<ObservedOwnedResource> observed)
            {
                foreach (ObservedOwnedResource item in observed)
                {
                    if (known.TryGetValue(item.ContainerId, out ObservedOwnedResource? previous)
                        && (previous.ImageId != item.ImageId || previous.Resource != item.Resource))
                        throw new OwnedExecutionFailure("owned_resource_identity_changed");
                    known[item.ContainerId] = item with { Running = item.Running || (previous?.Running ?? false) };
                    if (item.Running) running.Add(item.Resource);
                }
            }

            byte[] EligibleInventory(JsonElement inventory)
            {
                _ = ReadAllContainerIds(inventory, owner, requireOwnerAbsent: false);
                var eligible = new List<JsonElement>();
                var pairedIds = qualified.Values.Select(proof => proof.Creation.ContainerId).ToHashSet(StringComparer.Ordinal);
                foreach (JsonElement item in inventory.EnumerateArray())
                {
                    string id = Text(item, "Id");
                    bool selected = item.TryGetProperty("Labels", out JsonElement labels) && labels.ValueKind == JsonValueKind.Object
                        && labels.TryGetProperty("islamu.assurance.owner", out JsonElement selectedOwner) && selectedOwner.GetString() == owner;
                    if (selected && seenOwnedIds.Add(id) && seenOwnedIds.Count > 65_536)
                        throw new OwnedExecutionFailure("api_resource_inventory_limit");
                    if (!selected || pairedIds.Contains(id)) eligible.Add(item);
                }
                return JsonSerializer.SerializeToUtf8Bytes(eligible);
            }

            async Task ObserveAsync(OwnedNativeProcessContext context, CancellationToken stop)
            {
                if (!nativeUser.HasValue) throw new OwnedExecutionFailure("api_native_creator_identity_missing");
                await ValidateApiRootAsync(root, owner, nativeUser.Value, CancellationToken.None).ConfigureAwait(false);
                Dictionary<string, string[]> candidates = ReadApiCreationCandidates(root);
                RetainCandidates(candidates);
                foreach (ApiCreationProof proof in qualified.Values)
                {
                    if (stop.IsCancellationRequested) return;
                    await ValidateApiCreationAsync(proof, candidates[proof.Creation.CreationId], CancellationToken.None).ConfigureAwait(false);
                }
                foreach (string id in candidates.Keys)
                {
                    if (stop.IsCancellationRequested) return;
                    if (qualified.ContainsKey(id)) continue;
                    ApiCreationProof? proof = await CaptureApiCreationAsync(context, temporary, root, owner, id,
                        CancellationToken.None).ConfigureAwait(false);
                    if (proof is null) continue;
                    if (qualified.Values.Any(previous => previous.Creation.ContainerId == proof.Creation.ContainerId)
                        || qualified.Values.Sum(previous => (long)previous.PrivateManifest.Length) + proof.PrivateManifest.Length > MaximumReportBytes / 2)
                        throw new OwnedExecutionFailure("api_creation_capture_conflict");
                    if (stop.IsCancellationRequested) return;
                    string image = await LocalImageIdAsync(proof.Creation.ImageReference, CancellationToken.None).ConfigureAwait(false);
                    if (!cachedImageIds.Contains(image) || images.TryGetValue(proof.Creation.ImageReference, out string? previousImage)
                        && previousImage != image) throw new OwnedExecutionFailure("api_image_reference_changed");
                    images[proof.Creation.ImageReference] = image;
                    qualified.Add(id, proof);
                }
                if (stop.IsCancellationRequested) return;
                using JsonDocument document = await GetAsync("/v1.40/containers/json?all=true", CancellationToken.None).ConfigureAwait(false);
                if (!TryReadApiCreatedResources(EligibleInventory(document.RootElement), owner, QualifiedSnapshot(), images,
                        initialResources, out var observed)) throw new OwnedExecutionFailure("api_created_resource_observation_invalid");
                RetainResources(observed!);
            }

            OwnedNativeProcessResult process = await RunOwnedNativeProcessAsync(command, timeout, cancellationToken, async (context, stop) =>
            {
                ManagedProcessIdentity? identity = ReadManagedProcessIdentity(context.Pid);
                if (identity is null || identity.Start != context.Start || identity.Group != context.Group
                    || identity.Session != context.Session || identity.State is 'Z' or 'X')
                    throw new OwnedExecutionFailure("api_native_creator_identity_missing");
                nativeUser = identity.User;
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using CancellationTokenRegistration registration = stop.Register(() => stopped.TrySetResult());
                while (!stop.IsCancellationRequested)
                {
                    await ObserveAsync(context, stop).ConfigureAwait(false);
                    if (!stop.IsCancellationRequested)
                        _ = await Task.WhenAny(stopped.Task, Task.Delay(50, CancellationToken.None)).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
            if (!process.ObserverSettled)
            {
                byte[] unsettled = JsonSerializer.SerializeToUtf8Bytes(new
                    { observed = false, cleanup = false, reason = "api_observer_unsettled", creatorAuthentication = "not-attested" });
                return (process, false, unsettled, false);
            }
            bool valid = process.Succeeded && nativeUser.HasValue;
            using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                if (!nativeUser.HasValue) throw new OwnedExecutionFailure("api_native_creator_identity_missing");
                await ValidateApiRootAsync(root, owner, nativeUser.Value, cleanupDeadline.Token).ConfigureAwait(false);
                Dictionary<string, string[]> finalCandidates = ReadApiCreationCandidates(root);
                RetainCandidates(finalCandidates);
                foreach (ApiCreationProof proof in qualified.Values)
                    await ValidateApiCreationAsync(proof, finalCandidates[proof.Creation.CreationId], cleanupDeadline.Token).ConfigureAwait(false);
                using (JsonDocument terminal = await GetAsync("/v1.40/containers/json?all=true", cleanupDeadline.Token).ConfigureAwait(false))
                {
                    if (!TryReadApiCreatedResources(EligibleInventory(terminal.RootElement), owner, QualifiedSnapshot(), images,
                            initialResources, out var terminalQualified))
                        throw new OwnedExecutionFailure("api_resource_terminal_unqualified");
                    RetainResources(terminalQualified!);
                    if (!TryReadApiCreatedResources(JsonSerializer.SerializeToUtf8Bytes(terminal.RootElement), owner,
                            QualifiedSnapshot(), images, initialResources, out _))
                        throw new OwnedExecutionFailure("api_resource_terminal_unqualified");
                }
                var qualifiedIds = qualified.Values.Select(proof => proof.Creation.ContainerId).ToHashSet(StringComparer.Ordinal);
                if (!seenCreations.SetEquals(qualified.Keys) || !qualifiedIds.SetEquals(known.Keys)
                    || !seenOwnedIds.IsSubsetOf(qualifiedIds))
                    throw new OwnedExecutionFailure("api_resource_terminal_unqualified");
            }
            catch (Exception failure) { failures.Add(failure); valid = false; }
            foreach (ObservedOwnedResource item in known.Values)
            {
                try
                {
                    if (initial.Contains(item.ContainerId)) throw new OwnedExecutionFailure("owned_container_cleanup_failed");
                    string filter = Uri.EscapeDataString(JsonSerializer.Serialize(new Dictionary<string, string[]>
                        { ["id"] = [item.ContainerId] }));
                    using JsonDocument current = await GetAsync("/v1.40/containers/json?all=true&filters=" + filter,
                        cleanupDeadline.Token).ConfigureAwait(false);
                    JsonElement inventory = current.RootElement;
                    if (inventory.ValueKind != JsonValueKind.Array) throw new OwnedExecutionFailure("owned_container_cleanup_failed");
                    if (inventory.GetArrayLength() == 0) continue;
                    if (inventory.GetArrayLength() != 1 || Text(inventory[0], "Id") != item.ContainerId
                        || !TryReadApiCreatedResources(JsonSerializer.SerializeToUtf8Bytes(inventory), owner, QualifiedSnapshot(), images,
                            initialResources, out var currentResources) || currentResources!.Count != 1
                        || currentResources[0].ImageId != item.ImageId || currentResources[0].Resource != item.Resource)
                        throw new OwnedExecutionFailure("owned_container_cleanup_failed");
                    await DeleteAsync(item.ContainerId, cleanupDeadline.Token).ConfigureAwait(false);
                }
                catch (Exception failure) { failures.Add(failure); valid = false; }
            }
            try
            {
                using JsonDocument final = await GetAsync("/v1.40/containers/json?all=true", cleanupDeadline.Token).ConfigureAwait(false);
                HashSet<string> finalIds = ReadAllContainerIds(final.RootElement, owner, requireOwnerAbsent: true);
                cleanup = failures.Count == 0 && !finalIds.Overlaps(known.Keys) && !finalIds.Overlaps(seenOwnedIds);
                after = await IdentityAsync(cleanupDeadline.Token).ConfigureAwait(false);
                HashSet<string> finalImages = await CachedImageIdsAsync(cleanupDeadline.Token).ConfigureAwait(false);
                foreach (var image in images)
                    if (!finalImages.Contains(image.Value)
                        || await LocalImageIdAsync(image.Key, cleanupDeadline.Token).ConfigureAwait(false) != image.Value)
                        throw new OwnedExecutionFailure("api_image_reference_changed");
            }
            catch (Exception failure) { failures.Add(failure); valid = false; cleanup = false; }
            process = process with { ObserverFailures = process.ObserverFailures.Concat(failures).ToArray() };
            bool admitted = valid && process.Succeeded && cleanup && before.AsSpan().SequenceEqual(after) &&
                (!requireRunningPrerequisites || running.SetEquals(["keycloak", "cerbos"]));
            byte[] evidence = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1, selectedBackendBefore = JsonSerializer.Deserialize<JsonElement>(before),
                selectedBackendAfter = after.Length == 0 ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(after),
                daemonContinuity = "unknown", creatorAuthentication = "not-attested", initialContainerCount = initial.Count,
                initialContainerInventorySha256 = DigestBytes(JsonSerializer.SerializeToUtf8Bytes(initial.Order(StringComparer.Ordinal))),
                resources = known.Values.OrderBy(item => item.ContainerId, StringComparer.Ordinal)
                    .Select(item => new { item.ContainerId, item.ImageId, item.Resource, runningObserved = item.Running }),
                creatorObservations = qualified.Values.OrderBy(proof => proof.Creation.CreationId, StringComparer.Ordinal)
                    .Select(proof => new { proof.Creation, witnessSha256 = DigestBytes(proof.PrivateManifest),
                        witness = JsonSerializer.Deserialize<JsonElement>(proof.PrivateManifest) }),
                seenCreationIds = seenCreations.Order(StringComparer.Ordinal),
                seenOwnedContainerIds = seenOwnedIds.Order(StringComparer.Ordinal),
                runningPrerequisites = running.Order(StringComparer.Ordinal),
                cachedImages = images.OrderBy(image => image.Key, StringComparer.Ordinal)
                    .Select(image => new { reference = image.Key, imageId = image.Value }),
                initialImageCount = cachedImageIds.Count,
                initialImageInventorySha256 = DigestBytes(JsonSerializer.SerializeToUtf8Bytes(cachedImageIds.Order(StringComparer.Ordinal))),
                boundedObservationsAdmitted = valid, observationCoverage = "point-in-time-only",
                knownContainerCleanupComplete = cleanup, observationFailureCount = process.ObserverFailures.Count
            });
            if (evidence.Length > MaximumReportBytes)
            {
                process = process with { ObserverFailures = process.ObserverFailures.Append(new OwnedExecutionFailure("api_creation_evidence_limit")).ToArray() };
                evidence = JsonSerializer.SerializeToUtf8Bytes(new { observed = false, cleanup, reason = "api_creation_evidence_limit" });
                admitted = false;
            }
            return (process, admitted, evidence, cleanup);
        }

        private async Task<byte[]> IdentityAsync(CancellationToken token)
        {
            using JsonDocument version = await GetAsync("/v1.40/version", token).ConfigureAwait(false);
            using JsonDocument info = await GetAsync("/v1.40/info", token).ConfigureAwait(false);
            JsonElement value = version.RootElement;
            string backend = Text(value, "Version");
            string api = Text(value, "ApiVersion");
            string minimum = Text(value, "MinAPIVersion");
            string os = Text(value, "Os");
            string architecture = Text(value, "Arch");
            string id = Text(info.RootElement, "ID");
            if (!BoundedIdentity(backend, 128) || !BoundedIdentity(api, 32) || !BoundedIdentity(minimum, 32) ||
                os != "linux" || !BoundedIdentity(architecture, 64) || !BoundedIdentity(id, 1024))
                throw new OwnedExecutionFailure("daemon_metadata_unavailable");
            return JsonSerializer.SerializeToUtf8Bytes(new { requestedProtocol = "v1.40", reportedVersion = backend,
                reportedApiVersion = api, reportedMinimumApiVersion = minimum, os, architecture, id });
        }

        private async Task<HashSet<string>> CachedImageIdsAsync(CancellationToken token)
        {
            using JsonDocument document = await GetAsync("/v1.40/images/json?all=true", token).ConfigureAwait(false);
            JsonElement inventory = document.RootElement;
            if (inventory.ValueKind != JsonValueKind.Array || inventory.GetArrayLength() > 65_536) throw new InvalidDataException();
            var images = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement item in inventory.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("Id", out var id) ||
                    id.ValueKind != JsonValueKind.String || !FullLocalImageId(id.GetString()!))
                    throw new OwnedExecutionFailure("cached_image_inventory_invalid");
                images.Add(id.GetString()!);
            }
            return images;
        }

        private async Task<string> LocalImageIdAsync(string reference, CancellationToken token)
        {
            if (!BoundedIdentity(reference, 2048)) throw new OwnedExecutionFailure("api_image_reference_invalid");
            using JsonDocument document = await GetAsync("/v1.40/images/" + Uri.EscapeDataString(reference) + "/json", token).ConfigureAwait(false);
            string id = Text(document.RootElement, "Id");
            if (!FullLocalImageId(id)) throw new OwnedExecutionFailure("api_image_reference_invalid");
            return id;
        }

        private async Task<HashSet<string>> InitialContainersAsync(string owner, CancellationToken token)
        {
            using JsonDocument document = await GetAsync("/v1.40/containers/json?all=true", token).ConfigureAwait(false);
            return ReadAllContainerIds(document.RootElement, owner, requireOwnerAbsent: true);
        }

        private static HashSet<string> ReadAllContainerIds(JsonElement inventory, string owner, bool requireOwnerAbsent)
        {
            if (inventory.ValueKind != JsonValueKind.Array || inventory.GetArrayLength() > 65_536) throw new InvalidDataException();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement item in inventory.EnumerateArray())
            {
                string id = Text(item, "Id");
                if (id.Length != 64 || !LowerHex(id) || !ids.Add(id)) throw new InvalidDataException();
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("Labels", out JsonElement labels) ||
                    labels.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) throw new InvalidDataException();
                if (labels.ValueKind == JsonValueKind.Object && labels.TryGetProperty("islamu.assurance.owner", out JsonElement selected))
                {
                    if (selected.ValueKind != JsonValueKind.String) throw new InvalidDataException();
                    if (requireOwnerAbsent && selected.GetString() == owner)
                        throw new OwnedExecutionFailure("selected_owner_container_present");
                }
            }
            return ids;
        }

        private async Task<JsonDocument> GetAsync(string path, CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                deadline.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) throw new OwnedExecutionFailure("daemon_observation_failed");
            await using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            byte[] buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (bytes.Length + read > MaximumReportBytes) throw new InvalidDataException();
                bytes.Write(buffer, 0, read);
            }
            JsonDocument document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            if (!UniqueProperties(document.RootElement))
            {
                document.Dispose();
                throw new InvalidDataException();
            }
            return document;
        }

        private async Task DeleteAsync(string id, CancellationToken token)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Delete, "/v1.40/containers/" + id + "?force=true&v=false");
            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                deadline.Token).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.NoContent or HttpStatusCode.NotFound))
                throw new OwnedExecutionFailure("owned_container_cleanup_failed");
        }

        public void Dispose()
        {
            Exception? failure = null;
            try { _client.Dispose(); }
            catch (Exception exception) { failure = exception; }
            try { _handler.Dispose(); }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException(failure, exception);
            }
            if (failure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    /// Rejects detached report harvesting and invalidates its designated packet.
    /// Only an owning execution can supply inputs for a new producer attestation.
    /// </summary>
    public static int Run(string[] args, TextWriter output, string repositoryRoot) =>
        RunCore(args, output, repositoryRoot);

    private static int RunCore(string[] args, TextWriter output, string repositoryRoot)
    {
        if (args.Length != 6)
        {
            output.WriteLine("invalid_arguments: produce-security-execution requires <output-directory> <candidate-oid> <run-id> <run-attempt> <event-name>");
            return Program.UsageError;
        }

        try
        {
            string directory = Path.GetFullPath(args[1], repositoryRoot);
            if (Directory.Exists(directory))
            {
                if (!NoInputLinks(directory)) throw new InvalidDataException();
                File.Delete(ResolveArtifact(directory, PacketFileName));
            }
            output.WriteLine("security_execution_rejected: unowned_execution_reports");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            output.WriteLine("security_execution_rejected: packet_cleanup_failed");
        }
        return Program.ToolchainRejected;
    }
}
