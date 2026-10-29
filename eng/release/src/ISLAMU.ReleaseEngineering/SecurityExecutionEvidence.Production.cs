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
    internal sealed record ApiBenchmarkMeasurement(string Scenario, int Samples, double MeanNanoseconds,
        long ActualOperations, double ActualNanoseconds, string FullName);

    internal static bool BenchmarkWorkersCoverResults(IReadOnlyList<string> identities, IReadOnlyList<ApiBenchmarkMeasurement> results) =>
        results.Count == 9 && results.Select(item => item.FullName).Distinct(StringComparer.Ordinal).Count() == 9
        && identities.Count is >= 9 and <= 128 && identities.All(identity => !string.IsNullOrWhiteSpace(identity))
        && identities.ToHashSet(StringComparer.Ordinal).SetEquals(results.Select(item => item.FullName));

    internal static bool TryReadApiBenchmarkMeasurements(byte[] bytes, out IReadOnlyList<ApiBenchmarkMeasurement>? measurements,
        bool queryConstruction = false)
    {
        measurements = null;
        try
        {
            if (bytes.Length is < 1 or > MaximumReportBytes) return false;
            using JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions);
            JsonElement root = document.RootElement;
            if (!UniqueProperties(root) || !root.TryGetProperty("Benchmarks", out JsonElement rows)
                || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != (queryConstruction ? 3 : 6)) return false;
            var expected = queryConstruction ? new HashSet<string>(StringComparer.Ordinal)
            { "QueryConstruction_WithTracking", "QueryConstruction_WithNoTracking", "CompiledQuery_Invocation" }
            : new HashSet<string>(StringComparer.Ordinal)
            {
                "event-list-minimal", "event-list-hal", "lookup-category-types", "system-onboarding-status",
                "my-events-minimal", "event-creation-context"
            };
            var parsed = new List<ApiBenchmarkMeasurement>();
            var fullNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (Text(row, "Namespace") != "Event.Benchmarks.Benchmarks"
                    || Text(row, "Type") != (queryConstruction ? "EfCoreQueryBenchmarks" : "ApiEndpointPostgreSqlBenchmarks")) return false;
                string parameter = Text(row, "Parameters");
                string fullName = Text(row, "FullName");
                if (fullName.Length is < 1 or > 4096 || fullName.Any(char.IsControl)
                    || !fullName.StartsWith("Event.Benchmarks.Benchmarks." + Text(row, "Type") + "." + Text(row, "Method"), StringComparison.Ordinal)
                    || !fullNames.Add(fullName)) return false;
                const string prefix = "Scenario=";
                string identity;
                if (queryConstruction)
                {
                    identity = Text(row, "Method");
                    if (!string.IsNullOrEmpty(parameter) || !expected.Remove(identity)) return false;
                }
                else
                {
                    if (Text(row, "Method") != "Get_Endpoint_PostgreSql" || !parameter.StartsWith(prefix, StringComparison.Ordinal)
                        || !expected.Remove(parameter[prefix.Length..])) return false;
                    identity = parameter[prefix.Length..];
                }
                JsonElement statistics = row.GetProperty("Statistics"), samples = row.GetProperty("Measurements");
                if (statistics.ValueKind != JsonValueKind.Object || samples.ValueKind != JsonValueKind.Array
                    || samples.GetArrayLength() is < 1 or > 65_536
                    || !statistics.GetProperty("N").TryGetInt32(out int count) || count is < 1 or > 1_000_000) return false;
                double mean = statistics.GetProperty("Mean").GetDouble(), nanoseconds = 0;
                long operations = 0;
                if (!double.IsFinite(mean) || mean <= 0) return false;
                foreach (JsonElement sample in samples.EnumerateArray())
                {
                    if (Text(sample, "IterationMode") != "Workload" || Text(sample, "IterationStage") != "Actual") continue;
                    if (!sample.GetProperty("Operations").TryGetInt64(out long executed) || executed <= 0) return false;
                    double elapsed = sample.GetProperty("Nanoseconds").GetDouble();
                    if (!double.IsFinite(elapsed) || elapsed <= 0) return false;
                    operations = checked(operations + executed);
                    nanoseconds += elapsed;
                }
                if (operations <= 0 || !double.IsFinite(nanoseconds) || nanoseconds <= 0) return false;
                parsed.Add(new(identity, count, mean, operations, nanoseconds, fullName));
            }
            if (expected.Count != 0) return false;
            measurements = parsed.OrderBy(item => item.Scenario, StringComparer.Ordinal).ToArray();
            return true;
        }
        catch (Exception failure) when (failure is JsonException or InvalidDataException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException or KeyNotFoundException or NotSupportedException)
        { return false; }
    }

    internal static ProcessStartInfo ApiBenchmarkStartInfo(string assembly, string temporary, string artifacts, string mode)
    {
        if (!HintAbsolutePath(assembly) || !HintAbsolutePath(temporary) || !HintAbsolutePath(artifacts)
            || !InputWithin(artifacts, temporary) || mode is not ("smoke" or "baseline")) throw new InvalidDataException("benchmark_paths_invalid");
        var command = new ProcessStartInfo(ReleaseToolPaths.Dotnet)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = temporary
        };
        foreach (string argument in new[] { assembly, "--filter", "*ApiEndpointPostgreSqlBenchmarks*" }) command.ArgumentList.Add(argument);
        if (mode == "baseline") command.ArgumentList.Add("*EfCoreQueryBenchmarks*");
        else foreach (string argument in new[] { "--job", "Dry", "--inProcess" }) command.ArgumentList.Add(argument);
        foreach (string argument in new[] { "--stopOnFirstError", "--artifacts", artifacts }) command.ArgumentList.Add(argument);
        return command;
    }

    private static IReadOnlyList<string> PopulateBenchmarkEnvironment(ProcessStartInfo command, string temporary,
        string owner, string root, string dockerHost)
    {
        command.Environment["TMPDIR"] = temporary;
        command.Environment["ISLAMU_TEST_RUN_ID"] = owner;
        command.Environment["ISLAMU_TEST_ROOT"] = root;
        command.Environment["DOCKER_HOST"] = dockerHost;
        command.Environment["DOCKER_CONFIG"] = CreateOwnedDockerConfiguration(temporary);
        command.Environment["TESTCONTAINERS_RYUK_DISABLED"] = "true";
        command.Environment["SECRET_PROVIDER"] = "Environment";
        command.Environment["AUTHENTICATION_PROVIDER"] = "Local";
        command.Environment["AUTHORIZATION_PROVIDER"] = "Local";
        command.Environment["DEPLOYMENT_MODE"] = "MultiTenant";
        command.Environment["IDENTITY_DATABASE_TOPOLOGY"] = "colocated";
        command.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        command.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        var canaries = new List<string>();
        foreach (string purpose in new[] { "RUNTIME", "MIGRATOR", "GLOBAL", "INSTANCE_KEY_READ", "INSTANCE_KEY_WRITE",
            "INSTANCE_KEY_ISSUANCE", "PLATFORM_KEY_REPORT", "EMAIL_POLICY_INSTANCE_MUTATION", "EMAIL_POLICY_TENANT_MUTATION",
            "LOCAL_IDENTITY_WRITER" })
        {
            string username = "bench_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
            string password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            command.Environment["DATABASE_" + purpose + "_USERNAME"] = username;
            command.Environment["DATABASE_" + purpose + "_PASSWORD"] = password;
            canaries.Add(username);
            canaries.Add(password);
        }
        foreach (string key in new[] { "INSTANCE_BOOTSTRAP_LOCAL_PASSWORD", "ISLAMU_TEST_REPLACEMENT_PASSWORD", "AUTHENTICATION_LOCAL_JWT_KEY" })
        {
            string value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            command.Environment[key] = value;
            canaries.Add(value);
        }
        foreach (string key in new[] { "DOCKER_CONTEXT", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH" })
            command.Environment.Remove(key);
        return canaries.AsReadOnly();
    }

    public static int ExecuteApiBenchmarks(string[] args, TextWriter output, string repositoryRoot)
    {
        if (args.Length != 7 || args[0] != "execute-api-benchmarks" || args[2].Length is not (40 or 64) || !LowerHex(args[2])
            || !ulong.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out ulong run) || run == 0
            || !int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int attempt) || attempt <= 0
            || args[5] is not ("push" or "pull_request" or "merge_group" or "workflow_dispatch" or "schedule") || args[6] is not ("smoke" or "baseline"))
        {
            output.WriteLine("invalid_arguments: execute-api-benchmarks requires <new-output-directory> <candidate-oid> <run-id> <run-attempt> <event-name> <smoke|baseline>");
            return Program.UsageError;
        }
        if (!OperatingSystem.IsLinux())
        {
            output.WriteLine("api_benchmarks_rejected: unsupported_execution_platform");
            return Program.ToolchainRejected;
        }
        using var cancellation = new CancellationTokenSource();
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => { context.Cancel = true; cancellation.Cancel(); });
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; cancellation.Cancel(); });
        try
        {
            var request = new OwnedExecutionRequest(Path.GetFullPath(repositoryRoot), Path.GetFullPath(args[1], repositoryRoot),
                args[2], args[3], attempt, args[5], Environment.GetEnvironmentVariable("TMPDIR") ?? string.Empty,
                Environment.GetEnvironmentVariable("DOCKER_HOST") ?? string.Empty,
                Environment.GetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED") ?? string.Empty);
            ExecuteApiBenchmarksAsync(request, args[6], cancellation.Token).GetAwaiter().GetResult();
            output.WriteLine("api_benchmarks_admitted");
            return Program.Success;
        }
        catch (Exception failure)
        {
            output.WriteLine("api_benchmarks_rejected: " + (failure is OwnedExecutionFailure owned ? owned.Code : "owned_benchmark_execution_failed"));
            return Program.ToolchainRejected;
        }
    }

    private static async Task ExecuteApiBenchmarksAsync(OwnedExecutionRequest request, string mode, CancellationToken token)
    {
        RequireOwnedLaunchEnvironment(Environment.GetEnvironmentVariable);
        if (!OperatingSystem.IsLinux() || !Path.IsPathFullyQualified(request.Temporary)
            || InputDirectory(request.Temporary) != Path.TrimEndingDirectorySeparator(Path.GetTempPath())
            || request.Temporary == request.Repository || InputWithin(request.Temporary, request.Repository)
            || !string.Equals(request.RyukDisabled, "true", StringComparison.OrdinalIgnoreCase)
            || !PrivateDirectory(Path.GetDirectoryName(request.Output) ?? string.Empty)
            || request.Output == request.Repository || InputWithin(request.Output, request.Repository)
            || File.Exists(request.Output) || Directory.Exists(request.Output))
            throw new OwnedExecutionFailure("benchmark_prerequisites_not_admitted");
        string socket = SelectedUnixSocket(request.DockerHost);
        const string project = "tests/Event.Benchmarks/Event.Benchmarks.csproj";
        string directory = InputDirectory(Path.Combine(request.Repository, "tests", "Event.Benchmarks", "bin", "Release", "net10.0"));
        string assembly = Path.Combine(directory, "Event.Benchmarks.dll");
        var entry = new OwnedEntry(project, directory, assembly);
        OwnedInputs before = CaptureOwnedInputs(request, [entry]);
        string temporary = FreshPrivateTemporary("islamu-benchmark-", request.Temporary);
        string owner = Guid.CreateVersion7().ToString("N");
        string root = PrivateChildDirectory(PrivateChildDirectory(temporary, "islamu-assurance"), owner);
        WritePrivateBytes(Path.Combine(root, ".islamu-test-owner"), Encoding.ASCII.GetBytes(owner));
        string artifacts = PrivateChildDirectory(temporary, "bdn");
        ProcessStartInfo command = ApiBenchmarkStartInfo(assembly, temporary, artifacts, mode);
        IReadOnlyList<string> canaries = PopulateBenchmarkEnvironment(command, temporary, owner, root, request.DockerHost);
        OwnedNativeProcessResult? process = null;
        bool resourceCleanup = false;
        Exception? primaryFailure = null;
        byte[]? summary = null;
        SelectedDaemon? daemon = null;
        try
        {
            daemon = new SelectedDaemon(socket);
            var observed = await daemon.RunBenchmarksAsync(owner, root, command, request.DockerHost, assembly, token).ConfigureAwait(false);
            process = observed.Process;
            resourceCleanup = observed.ResourceCleanupComplete;
            await ValidateOwnedDockerConfigurationAsync(command.Environment["DOCKER_CONFIG"]!, token).ConfigureAwait(false);
            if (!process.Succeeded || !observed.Admitted || !resourceCleanup)
                throw new OwnedExecutionFailure("native_benchmark_execution_not_admitted");
            string report = Path.Combine(artifacts, "results", "Event.Benchmarks.Benchmarks.ApiEndpointPostgreSqlBenchmarks-report-full.json");
            if (!NoInputLinks(report) || !await AreRegularCandidateInputsAsync([report]).ConfigureAwait(false))
                throw new OwnedExecutionFailure("benchmark_report_missing");
            byte[] bytes = ReadBoundedFile(report, MaximumReportBytes);
            if (!TryReadApiBenchmarkMeasurements(bytes, out var nativeResults)) throw new OwnedExecutionFailure("benchmark_six_outcomes_missing");
            var results = new List<ApiBenchmarkMeasurement>(nativeResults!);
            string? queryReportSha256 = null;
            byte[]? queryBytes = null;
            if (mode == "baseline")
            {
                string queryReport = Path.Combine(artifacts, "results", "Event.Benchmarks.Benchmarks.EfCoreQueryBenchmarks-report-full.json");
                if (!NoInputLinks(queryReport) || !await AreRegularCandidateInputsAsync([queryReport]).ConfigureAwait(false))
                    throw new OwnedExecutionFailure("benchmark_query_report_missing");
                queryBytes = ReadBoundedFile(queryReport, MaximumReportBytes);
                if (!TryReadApiBenchmarkMeasurements(queryBytes, out var queryResults, queryConstruction: true))
                    throw new OwnedExecutionFailure("benchmark_query_outcomes_missing");
                queryReportSha256 = DigestBytes(queryBytes);
                results.AddRange(queryResults!);
                if (!BenchmarkWorkersCoverResults(observed.WorkerIdentities, results))
                    throw new OwnedExecutionFailure("benchmark_worker_case_join_incomplete");
            }
            if (ContainsBenchmarkCanary(process.StandardOutput, canaries) || ContainsBenchmarkCanary(process.StandardError, canaries)
                || !BenchmarkArtifactsCanaryFree(temporary, canaries)) throw new OwnedExecutionFailure("benchmark_secret_canary_detected");
            OwnedInputs after = CaptureOwnedInputs(request, [entry]);
            if (before.Source != after.Source || before.Fingerprint != after.Fingerprint)
                throw new OwnedExecutionFailure("benchmark_inputs_changed");
            string privateRoot = FreshPrivateTemporary("islamu-benchmark-evidence-", request.Temporary);
            RetainApiBenchmarkEvidence(privateRoot, before.Manifest, after.Manifest, bytes, queryBytes, process, observed.Evidence);
            summary = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                candidateOid = request.Candidate,
                treeOid = before.Source.TreeOid,
                runId = request.RunId,
                runAttempt = request.Attempt,
                eventName = request.Event,
                operation = "api-postgresql-benchmarks",
                mode,
                job = mode == "smoke" ? "Dry" : "configured-default",
                expectedOutcomes = mode == "smoke" ? 6 : 9,
                completedOutcomes = results.Count,
                results,
                inputsBeforeSha256 = before.Fingerprint,
                inputsAfterSha256 = after.Fingerprint,
                privateReportSha256 = DigestBytes(bytes),
                queryReportSha256,
                resourceEvidenceSha256 = DigestBytes(observed.Evidence),
                nativeExitCode = process.ExitCode,
                observerSettled = process.ObserverSettled,
                nativeGroupCleanupComplete = process.CleanupSucceeded,
                resourceCleanupComplete = resourceCleanup,
                secretCanaryScanPassed = true,
                statisticalCapacityQualified = false
            });
            WritePrivateBytes(Path.Combine(privateRoot, "benchmark-summary.json"), summary);
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
        }
        finally
        {
            try { daemon?.Dispose(); }
            catch (Exception disposalFailure)
            {
                primaryFailure = primaryFailure is null ? disposalFailure : new AggregateException(primaryFailure, disposalFailure);
            }
        }
        Exception? cleanupFailure = null;
        try
        {
            bool safe = BenchmarkArtifactsCanaryFree(temporary, canaries);
            if (!safe) RemoveBenchmarkCanaryFiles(temporary, canaries);
            bool removed = TryRemoveOwnedChildTemporary(temporary, process, resourceCleanup && safe && primaryFailure is null);
            if (primaryFailure is null && !removed) cleanupFailure = new OwnedExecutionFailure("benchmark_private_cleanup_incomplete");
        }
        catch (Exception failure) { cleanupFailure = failure; }
        if (primaryFailure is not null && cleanupFailure is not null) throw new AggregateException(primaryFailure, cleanupFailure);
        if (primaryFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        if (cleanupFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        if (summary is null) throw new OwnedExecutionFailure("benchmark_summary_missing");
        string stage = FreshPrivateTemporary("islamu-benchmark-public-", request.Temporary);
        try
        {
            WritePrivateBytes(Path.Combine(stage, "benchmark-summary.json"), summary);
            PublishFreshPublicDirectory(stage, request.Output, destination =>
                Directory.EnumerateFileSystemEntries(destination).ToHashSet(StringComparer.Ordinal)
                    .SetEquals([Path.Combine(destination, "benchmark-summary.json")])
                && ReadBoundedFile(Path.Combine(destination, "benchmark-summary.json"), MaximumReportBytes).AsSpan().SequenceEqual(summary));
        }
        catch (Exception failure)
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
            catch (Exception cleanup) { throw new AggregateException(failure, cleanup); }
            throw;
        }
    }

    internal static void RetainApiBenchmarkEvidence(string directory, byte[] inputsBefore, byte[] inputsAfter,
        byte[] report, byte[]? queryReport, OwnedNativeProcessResult process, byte[] resourceEvidence)
    {
        if (!PrivateDirectory(directory) || Directory.EnumerateFileSystemEntries(directory).Any())
            throw new OwnedExecutionFailure("private_benchmark_evidence_destination_invalid");
        WritePrivateBytes(Path.Combine(directory, "inputs-before.json"), inputsBefore);
        WritePrivateBytes(Path.Combine(directory, "inputs-after.json"), inputsAfter);
        WritePrivateBytes(Path.Combine(directory, "api-report-full.json"), report);
        if (queryReport is not null) WritePrivateBytes(Path.Combine(directory, "query-report-full.json"), queryReport);
        WritePrivateBytes(Path.Combine(directory, "stdout.bin"), process.StandardOutput);
        WritePrivateBytes(Path.Combine(directory, "stderr.bin"), process.StandardError);
        WritePrivateBytes(Path.Combine(directory, "resource-observation.json"), resourceEvidence);
    }

    internal static void RequireOwnedLaunchEnvironment(Func<string, string?> read)
    {
        foreach (string name in new[]
        {
            "LD_PRELOAD", "LD_AUDIT", "LD_LIBRARY_PATH", "DOTNET_STARTUP_HOOKS", "DOTNET_ADDITIONAL_DEPS",
            "DOTNET_SHARED_STORE", "CORECLR_PROFILER", "CORECLR_PROFILER_PATH", "CORECLR_PROFILER_PATH_32", "CORECLR_PROFILER_PATH_64"
        })
            if (!string.IsNullOrEmpty(read(name))) throw new OwnedExecutionFailure("native_launch_environment_not_admitted");
    }

    internal static bool LoadPurposeObservationsMatch(SecurityControlRegister.LoadPurpose? purpose,
        bool workloadObserved, bool programmeObserved, bool pressureQualified) => purpose switch
        {
            null => true,
            SecurityControlRegister.LoadPurpose.Foundation => workloadObserved && !programmeObserved && !pressureQualified,
            SecurityControlRegister.LoadPurpose.Programme => workloadObserved && programmeObserved && pressureQualified,
            _ => false
        };

    internal static string CreateOwnedDockerConfiguration(string temporary)
    {
        string directory = PrivateChildDirectory(temporary, "docker-config");
        WritePrivateBytes(Path.Combine(directory, "config.json"), "{}\n"u8.ToArray());
        return directory;
    }

    internal static async Task ValidateOwnedDockerConfigurationAsync(string directory, CancellationToken token)
    {
        string path = Path.Combine(directory, "config.json");
        ManagedProcessIdentity? parent = ReadManagedProcessIdentity(Environment.ProcessId);
        if (parent is null || !NoInputLinks(directory) || !NoInputLinks(path) || !PrivateDirectory(directory)
            || !Directory.EnumerateFileSystemEntries(directory).Take(2).ToHashSet(StringComparer.Ordinal).SetEquals([path]))
            throw new OwnedExecutionFailure("docker_cli_configuration_changed");
        LiveFileIdentity before = await ReadLiveMetadataAsync(path, false, token).ConfigureAwait(false);
        if (!PrivateLiveFile(before, parent.User, 3) || !ReadBoundedFile(path, 3).AsSpan().SequenceEqual("{}\n"u8)
            || await ReadLiveMetadataAsync(path, false, token).ConfigureAwait(false) != before)
            throw new OwnedExecutionFailure("docker_cli_configuration_changed");
    }

    private static bool ContainsBenchmarkCanary(byte[] bytes, IReadOnlyList<string> canaries) =>
        canaries.Any(value => bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(value)) >= 0);

    private static IEnumerable<string> BenchmarkArtifactFiles(string root)
    {
        var directories = new Stack<(string Path, int Depth)>();
        directories.Push((root, 0));
        int entries = 0;
        while (directories.TryPop(out var directory))
        {
            if (directory.Depth > 32 || !NoInputLinks(directory.Path)) throw new OwnedExecutionFailure("benchmark_artifact_inventory_invalid");
            foreach (string path in Directory.EnumerateFileSystemEntries(directory.Path))
            {
                if (++entries > 16_384 || !NoInputLinks(path)) throw new OwnedExecutionFailure("benchmark_artifact_inventory_invalid");
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) directories.Push((path, directory.Depth + 1));
                else yield return path;
            }
        }
    }

    private static bool BenchmarkArtifactsCanaryFree(string root, IReadOnlyList<string> canaries)
    {
        long bytes = 0;
        foreach (string path in BenchmarkArtifactFiles(root))
        {
            var info = new FileInfo(path);
            if (info.Length > MaximumReportBytes || (bytes = checked(bytes + info.Length)) > 67_108_864)
                throw new OwnedExecutionFailure("benchmark_artifact_inventory_limit");
            if (ContainsBenchmarkCanary(ReadBoundedFile(path, MaximumReportBytes), canaries)) return false;
        }
        return true;
    }

    private static void RemoveBenchmarkCanaryFiles(string root, IReadOnlyList<string> canaries)
    {
        foreach (string path in BenchmarkArtifactFiles(root))
            if (ContainsBenchmarkCanary(ReadBoundedFile(path, MaximumReportBytes), canaries)) File.Delete(path);
    }

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
    private sealed record LoadToolInputs(string Executable, string ExecutableSha256, string Script, string ScriptSha256);
    private sealed record OwnedInputs(CandidateSourceIdentity Source, string Fingerprint, byte[] Manifest,
        IReadOnlyDictionary<string, OwningManagedTarget> Targets, LoadToolInputs? LoadTool);
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
            RequireOwnedLaunchEnvironment(Environment.GetEnvironmentVariable);
            using (Stream embedded = typeof(SecurityControlRegister).Assembly.GetManifestResourceStream(
                "ISLAMU.ReleaseEngineering.SecurityControlRegister.json") ?? throw new InvalidDataException())
            {
                if (!SHA256.HashData(embedded).AsSpan().SequenceEqual(SHA256.HashData(ReadBoundedFile(register, 1_048_576))))
                    throw new OwnedExecutionFailure("compiled_register_mismatch");
            }
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
                        null, before, cancellationToken).ConfigureAwait(false);
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
                        SecurityControlRegister.RuntimeLifecycle? lifecycle = null;
                        SecurityControlRegister.LoadPurpose? loadPurpose = null;
                        if (lane.Id == "owned-runtime")
                        {
                            if (selected.Length != 1 || !lane.Requirement.Lifecycles.TryGetValue(
                                discovered.Single(node => node.Uid == selected[0]).Identity, out lifecycle))
                                throw new OwnedExecutionFailure("native_runtime_lifecycle_missing");
                        }
                        if (lane.Id == "owned-load-correctness")
                        {
                            if (selected.Length != 1 || !lane.Requirement.LoadPurposes.TryGetValue(
                                discovered.Single(node => node.Uid == selected[0]).Identity, out var expectedPurpose))
                                throw new OwnedExecutionFailure("native_load_purpose_missing");
                            loadPurpose = expectedPurpose;
                        }
                        OwnedChild child = await InvokeOwnedChildAsync(request, entry, lane.Id, lane.Requirement, daemon,
                            selected, before, cancellationToken, lifecycle, loadPurpose).ConfigureAwait(false);
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
                        schemaVersion = 1,
                        synthetic = false,
                        candidateOid = request.Candidate,
                        treeOid = before.Source.TreeOid,
                        runId = request.RunId,
                        runAttempt = request.Attempt,
                        lane = lane.Id,
                        invocation = index,
                        inputsBeforeSha256 = before.Fingerprint,
                        inputsAfterSha256 = after.Fingerprint,
                        selectedUids = invocation.Uids,
                        discovery = ChildFacts(lane.Discovery!),
                        execution = ChildFacts(invocation.Child),
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
                                postObservation = "captured",
                                unchanged = inputsBefore is not null &&
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
            schemaVersion = 1,
            outcome,
            candidateOid = request.Candidate,
            lanes = lanes.Select(lane => new
            {
                id = lane.Id,
                outcome = lane.Outcome,
                discovery = lane.Discovery is null ? null : ChildFacts(lane.Discovery),
                invocations = lane.Invocations.Select(item => new { selectedUids = item.Uids, execution = ChildFacts(item.Child) })
            })
        });

    private static object ChildFacts(OwnedChild child) => new
    {
        owner = child.Owner,
        child.Process.ExitCode,
        child.Process.TimedOut,
        child.Process.Cancelled,
        child.Process.ResidualOwnedChildren,
        child.Process.OutputLimitExceeded,
        child.Process.CleanupSucceeded,
        child.Process.ObserverSettled,
        observerFailureCount = child.Process.ObserverFailures.Count,
        child.ResourcesAdmitted,
        child.TemporaryRemoved,
        reportCaptured = child.Report.Length != 0,
        reportBytes = child.Report.Length,
        stdoutSha256 = DigestBytes(child.Process.StandardOutput),
        stderrSha256 = DigestBytes(child.Process.StandardError),
        resourceEvidence = JsonSerializer.Deserialize<JsonElement>(child.ResourceEvidence)
    };

    internal static (ProcessStartInfo Command, TimeSpan Deadline) NativeExecutionStartInfo(
        string assemblyPath, IReadOnlyList<string> selectedUids, string resultsDirectory,
        string temporaryDirectory, string reportFileName, string hostProfile, string laneId,
        SecurityControlRegister.RuntimeLifecycle? lifecycle = null)
    {
        if (string.IsNullOrEmpty(laneId) ||
            !SecurityControlRegister.ExecutionRequirements.TryGetValue(laneId, out var requirement) ||
            requirement.HostProfile != hostProfile || selectedUids.Count is < 1 or > 1024 ||
            (laneId is "owned-runtime" or "owned-load-control" or "owned-load-correctness" && selectedUids.Count != 1))
            throw new InvalidDataException("native_batch_invalid");

        // Native --timeout covers the serial batch. These are hang allowances, not reserved case runtimes.
        bool healthyRuntime = laneId == "owned-runtime" && lifecycle is
        {
            Stage: SecurityControlRegister.RuntimeStage.Hosted,
            Completion: SecurityControlRegister.RuntimeCompletion.Success or SecurityControlRegister.RuntimeCompletion.StopFault
        };
        int timeoutSeconds = laneId switch
        {
            "security-integration" or "cerbos-policy-contract" => 600,
            "owned-runtime" => healthyRuntime ? 1200 : 720,
            "owned-browser-journeys" => Math.Min(checked(1200 * selectedUids.Count), 19_800),
            "owned-browser-security" => Math.Min(checked(240 * selectedUids.Count + 1020), 19_800),
            "owned-load-control" or "owned-load-correctness" => 1500,
            _ => throw new InvalidDataException("native_batch_invalid")
        };
        return (NativeTestStartInfo(assemblyPath, selectedUids, resultsDirectory, temporaryDirectory,
            reportFileName, timeoutSeconds), TimeSpan.FromSeconds(timeoutSeconds + (healthyRuntime ? 60 : 15)));
    }

    private static async Task<OwnedChild> InvokeOwnedChildAsync(OwnedExecutionRequest request, OwnedEntry entry, string laneId,
        SecurityControlRegister.ExecutionRequirement requirement, SelectedDaemon daemon, string[]? uids,
        OwnedInputs inputs, CancellationToken cancellationToken,
        SecurityControlRegister.RuntimeLifecycle? lifecycle = null, SecurityControlRegister.LoadPurpose? loadPurpose = null)
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
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = temporary
                };
                foreach (string argument in new[] { entry.Assembly, "--list-tests", "json", "--treenode-filter", requirement.Selector, "--no-ansi" })
                    command.ArgumentList.Add(argument);
            }
            else (command, deadline) = NativeExecutionStartInfo(entry.Assembly, uids, results, temporary,
                "execution.trx", requirement.HostProfile, laneId, lifecycle);
            command.Environment["TMPDIR"] = temporary;
            command.Environment["ISLAMU_TEST_RUN_ID"] = owner;
            command.Environment["ISLAMU_TEST_ROOT"] = context;
            command.Environment["DOCKER_HOST"] = request.DockerHost;
            command.Environment["DOCKER_CONFIG"] = CreateOwnedDockerConfiguration(temporary);
            command.Environment["TESTCONTAINERS_RYUK_DISABLED"] = request.RyukDisabled;
            command.Environment["DOTNET_ENVIRONMENT"] = requirement.Environment;
            command.Environment["ASPNETCORE_ENVIRONMENT"] = requirement.Environment;
            foreach (string name in new[] { "DOCKER_CONTEXT", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH" })
                command.Environment.Remove(name);
            var observed = uids is not null && requirement.HostProfile != "dedicated-api-test-host"
                ? await daemon.RunManagedAsync(owner, temporary, context, command, deadline, request.Repository,
                    request.Candidate, inputs.Targets, inputs.LoadTool, loadPurpose, lifecycle, cancellationToken).ConfigureAwait(false)
                : await daemon.RunAsync(owner, temporary, context, command, uids is not null,
                    deadline, cancellationToken).ConfigureAwait(false);
            process = observed.Process;
            admitted = observed.Admitted;
            evidence = observed.Evidence;
            resourceCleanupComplete = observed.ResourceCleanupComplete;
            try { await ValidateOwnedDockerConfigurationAsync(command.Environment["DOCKER_CONFIG"]!, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception failure)
            {
                process = process with { ObserverFailures = process.ObserverFailures.Append(failure).ToArray() };
                admitted = false;
                resourceCleanupComplete = false;
            }
            if (uids is not null && process.CleanupSucceeded && process.ObserverSettled)
                _ = TryReadOwnedChildReport(results, "execution.trx", out report);
        }
        finally
        {
            removed = TryRemoveOwnedChildTemporary(temporary, process, resourceCleanupComplete);
        }
        return new(process ?? throw new OwnedExecutionFailure("native_process_missing"), report, evidence, owner, admitted, removed);
    }

    internal static bool TryRemoveOwnedChildTemporary(string temporary, OwnedNativeProcessResult? process,
        bool resourceCleanupComplete)
    {
        try
        {
            if (process is not { CleanupSucceeded: true, ObserverSettled: true } || !resourceCleanupComplete || !NoInputLinks(temporary))
                return false;
            Directory.Delete(temporary, recursive: true);
            return !Directory.Exists(temporary);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        { return false; }
    }

    internal static void RequireSameApiDaemonIdentity(byte[] before, byte[] after)
    {
        if (!before.AsSpan().SequenceEqual(after)) throw new OwnedExecutionFailure("api_daemon_identity_changed");
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
        OwnedEntry[] selectedEntries = entries.ToArray();
        if (!TryReadCandidateSource(request.Repository, request.Candidate, out var source, out _))
            throw new OwnedExecutionFailure("candidate_source_invalid");
        var managed = new List<object>();
        string engine = typeof(SecurityExecutionEvidence).Assembly.Location;
        if (!TryCaptureProjectManagedInputs(Path.GetDirectoryName(engine)!, request.Candidate, [Path.GetFileName(engine)],
                out var own, out _)) throw new OwnedExecutionFailure("producer_inputs_invalid");
        managed.Add(new { project = "producer", digest = own!.PrivateManifestSha256, manifest = JsonSerializer.Deserialize<JsonElement>(own.PrivateManifest) });
        foreach (OwnedEntry entry in selectedEntries.OrderBy(item => item.Project, StringComparer.Ordinal))
        {
            if (!TryCaptureProjectManagedInputs(entry.Directory, request.Candidate, [Path.GetFileName(entry.Assembly)],
                    out var captured, out _))
                throw new OwnedExecutionFailure("selected_test_inputs_invalid");
            managed.Add(new
            {
                project = entry.Project,
                digest = captured!.PrivateManifestSha256,
                manifest = JsonSerializer.Deserialize<JsonElement>(captured.PrivateManifest)
            });
            if (SecurityControlRegister.ExecutionRequirements.Values.Any(requirement =>
                requirement.Project == entry.Project && requirement.HostProfile == "dedicated-api-test-host"))
            {
                if (!TryCaptureApiPrerequisiteInputs(request.Repository, request.Candidate, entry.Directory, out var content, out _))
                    throw new OwnedExecutionFailure("selected_test_inputs_invalid");
                managed.Add(new
                {
                    project = entry.Project + ":content",
                    digest = content!.PrivateManifestSha256,
                    manifest = JsonSerializer.Deserialize<JsonElement>(content.PrivateManifest)
                });
            }
        }
        Dictionary<string, OwningManagedTarget> targets = CaptureOwningManagedTargets(request.Repository,
            request.Candidate, selectedEntries.Select(entry => entry.Project));
        LoadToolInputs? load = null;
        foreach (OwnedEntry entry in selectedEntries.Where(entry => SecurityControlRegister.ExecutionRequirements.Any(item =>
            item.Key is "owned-load-control" or "owned-load-correctness" && item.Value.Project == entry.Project)))
        {
            string executable = Environment.GetEnvironmentVariable("ISLAMU_K6_PATH") ?? string.Empty;
            const string relative = "eng/performance/k6/owned-runtime.js";
            string script = Path.Combine(entry.Directory, "Workloads", "owned-runtime.js");
            Dictionary<string, string> tracked = ReadTrackedInputBlobs(request.Repository, request.Candidate);
            if (load is not null || !HintAbsolutePath(executable) || !NoInputLinks(executable) || !NoInputLinks(script)
                || !tracked.TryGetValue(relative, out string? blob)
                || !AreRegularCandidateInputsAsync([executable, script]).GetAwaiter().GetResult())
                throw new OwnedExecutionFailure("load_tool_inputs_invalid");
            string[] hashes = ReadCandidateGitAsync(request.Repository,
                ["hash-object", "--no-filters", "--", Path.Combine(request.Repository, relative), script]).GetAwaiter().GetResult().Split('\n');
            if (hashes.Length != 3 || hashes[0] != blob || hashes[1] != blob || hashes[2].Length != 0)
                throw new OwnedExecutionFailure("load_script_candidate_mismatch");
            long executableBytes = new FileInfo(executable).Length;
            if (executableBytes is < 1 or > 1_073_741_824) throw new OwnedExecutionFailure("load_tool_inputs_invalid");
            load = new(executable, HashLiveExecutable(executable, executableBytes), script,
                DigestBytes(ReadBoundedFile(script, 1_048_576)));
        }
        byte[] tools = CaptureExecutionTools();
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            candidateOid = source!.CandidateOid,
            treeOid = source.TreeOid,
            managed,
            owningTargets = targets.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => new
            {
                item.Value.Build,
                fileListPath = Path.GetRelativePath(request.Repository, item.Value.FileListPath),
                item.Value.FileListSha256,
                compiledInputsSha256 = item.Value.Inputs.PrivateManifestSha256,
                compiledInputs = JsonSerializer.Deserialize<JsonElement>(item.Value.Inputs.PrivateManifest),
                item.Value.RestoreInputsSha256,
                item.Value.GeneratedRoots,
                item.Value.LockedPackageRoots,
                servedInputsSha256 = item.Value.ServedInputs?.PrivateManifestSha256,
                servedInputs = item.Value.ServedInputs is null ? (JsonElement?)null
                    : JsonSerializer.Deserialize<JsonElement>(item.Value.ServedInputs.PrivateManifest)
            }),
            tools = JsonSerializer.Deserialize<JsonElement>(tools),
            loadTool = load
        });
        if (manifest.Length > MaximumReportBytes) throw new OwnedExecutionFailure("execution_inputs_too_large");
        return new(source, DigestBytes(manifest), manifest, targets, load);
    }

    internal static byte[] CaptureExecutionTools()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal)
            { ReleaseToolPaths.Dotnet, ReleaseToolPaths.Git, "/usr/bin/setsid", "/usr/bin/kill", "/usr/bin/stat" };
        // The current host's default binding inputs remain stable across lazy loads.
        // This is an eligible binding closure; actual child execution is observed separately.
        foreach (string property in new[] { "TRUSTED_PLATFORM_ASSEMBLIES", "APP_CONTEXT_DEPS_FILES" })
        {
            if (AppContext.GetData(property) is not string value || value.Length is < 1 or > 4_194_304)
                throw new OwnedExecutionFailure("native_runtime_binding_inputs_missing");
            string[] configured = value.Split(property == "APP_CONTEXT_DEPS_FILES" ? ';' : Path.PathSeparator);
            if (configured.Length > 65_536 || configured.Any(path => !HintAbsolutePath(path)))
                throw new OwnedExecutionFailure("native_runtime_binding_inputs_invalid");
            paths.UnionWith(configured);
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
                artifacts.Add(new
                {
                    kind = "native-proof",
                    path = relative,
                    sha256 = DigestBytes(proof),
                    invocation = index,
                    privateInvocationSha256 = completed.InvocationDigests[lane.Id][index]
                });
            }
            var requirement = lane.Requirement;
            lanes.Add(new
            {
                id = lane.Id,
                project = requirement.Project,
                selector = requirement.Selector,
                command = ExecutionCommandName,
                runId = completed.Request.RunId,
                runAttempt = completed.Request.Attempt,
                outcome = "passed",
                exitCode = 0,
                counts = new { selected, executed = selected, passed = selected, failed = 0, skipped = 0 },
                executionContext = new
                {
                    kind = "test-host",
                    environment = requirement.Environment,
                    hostProfile = requirement.HostProfile,
                    databaseProvider = "not-attested",
                    databaseRole = "not-attested",
                    authenticationScheme = "not-attested",
                    authenticationAuthority = "not-attested"
                },
                artifacts
            });
        }
        WritePrivateBytes(Path.Combine(stage, PacketFileName), JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = "security-execution.v1",
            candidateOid = completed.Request.Candidate,
            workflowPath = ".github/workflows/security-tests.yml",
            runId = completed.Request.RunId,
            runAttempt = completed.Request.Attempt,
            eventName = completed.Request.Event,
            selection = "executed",
            lanes
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
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            Options = FileOptions.WriteThrough
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
            _handler = new SocketsHttpHandler
            {
                UseProxy = false,
                UseCookies = false,
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(3)
            };
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
                _client = new HttpClient(_handler, disposeHandler: false)
                {
                    BaseAddress = new Uri("http://localhost"),
                    Timeout = Timeout.InfiniteTimeSpan
                };
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
        private sealed record BenchmarkStage(string Fixture, string InstanceRun, string Name, string Stage,
            int Sequence, string? ContainerId, int WriterPid, ApiCreationFile File);
        private sealed record BenchmarkWriter(int Pid, ulong Start, string InputsSha256, LiveNativeTool Executable, string? BenchmarkIdentity);

        private static int ReadBenchmarkWriterPid(string path)
        {
            using JsonDocument document = JsonDocument.Parse(ReadBoundedFile(path, 16_384), DocumentOptions);
            return document.RootElement.GetProperty("writerPid").GetInt32();
        }

        private static async Task<BenchmarkWriter?> CaptureBenchmarkWriterAsync(OwnedNativeProcessContext invocation,
            int pid, string benchmarkAssembly, ProcessStartInfo command, CancellationToken token)
        {
            try
            {
                ManagedProcessIdentity? process = ReadManagedProcessIdentity(pid);
                LiveNativeTool? executable = await CaptureLiveNativeToolAsync(invocation, pid, token).ConfigureAwait(false);
                if (process is null || executable is null || executable.Start != process.Start) return null;
                string prefix = "/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/";
                byte[] arguments = ReadLiveProcBytes(prefix + "cmdline", 65_536);
                if (arguments.Length < 2 || arguments[^1] != 0) return null;
                string[] values = new UTF8Encoding(false, true).GetString(arguments).Split('\0');
                string[] identities = values.Where(value => value.StartsWith("Event.Benchmarks.Benchmarks.ApiEndpointPostgreSqlBenchmarks.", StringComparison.Ordinal)
                    || value.StartsWith("Event.Benchmarks.Benchmarks.EfCoreQueryBenchmarks.", StringComparison.Ordinal)).ToArray();
                if (identities.Length > 1 || identities.Any(identity => identity.Length > 4096 || identity.Any(char.IsControl))
                    || pid != invocation.Pid && identities.Length != 1) return null;
                string? benchmarkIdentity = identities.SingleOrDefault();
                string directory = new DirectoryInfo(prefix + "cwd").ResolveLinkTarget(true)?.FullName
                    ?? throw new InvalidDataException("benchmark_worker_directory_missing");
                string dotnet = new FileInfo(ReleaseToolPaths.Dotnet).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(ReleaseToolPaths.Dotnet);
                string entry;
                if (executable.Executable == dotnet)
                {
                    int slot = values.Length > 2 && values[1] == "exec" ? 2 : 1;
                    if (slot >= values.Length - 1 || !values[slot].EndsWith(".dll", StringComparison.Ordinal)) return null;
                    entry = Path.GetFullPath(values[slot], directory);
                }
                else entry = Path.ChangeExtension(executable.Executable, ".dll");
                string candidateDirectory = Path.GetDirectoryName(benchmarkAssembly)!;
                if (entry != benchmarkAssembly && !InputWithin(entry, candidateDirectory)
                    && !InputWithin(entry, command.WorkingDirectory)) return null;
                string workerDirectory = Path.GetDirectoryName(entry)!;
                string dependencies = Path.ChangeExtension(entry, ".deps.json");
                var paths = new HashSet<string>(StringComparer.Ordinal)
            { entry, dependencies, Path.ChangeExtension(entry, ".runtimeconfig.json") };
                var projectCopies = new List<(string Actual, string Expected)>();
                using (JsonDocument deps = JsonDocument.Parse(ReadBoundedFile(dependencies, MaximumReportBytes), DocumentOptions))
                {
                    JsonElement root = deps.RootElement;
                    if (!UniqueProperties(root)) return null;
                    JsonElement target = root.GetProperty("targets").GetProperty(Text(root.GetProperty("runtimeTarget"), "name"));
                    bool benchmarkFound = false;
                    foreach (JsonProperty library in root.GetProperty("libraries").EnumerateObject())
                    {
                        if (Text(library.Value, "type") != "project") continue;
                        if (!target.GetProperty(library.Name).TryGetProperty("runtime", out var runtime)) continue;
                        foreach (JsonProperty asset in runtime.EnumerateObject())
                        {
                            if (!asset.Name.EndsWith(".dll", StringComparison.Ordinal) || Path.GetFileName(asset.Name) != asset.Name) return null;
                            string actual = Path.Combine(workerDirectory, asset.Name);
                            if (actual == entry) continue;
                            string expected = Path.Combine(candidateDirectory, asset.Name);
                            if (!NoInputLinks(actual) || !NoInputLinks(expected)) return null;
                            byte[] expectedBytes = ReadBoundedFile(expected, 134_217_728), actualBytes = ReadBoundedFile(actual, 134_217_728);
                            if (!actualBytes.AsSpan().SequenceEqual(expectedBytes)) return null;
                            paths.Add(actual); paths.Add(expected);
                            projectCopies.Add((actual, expected));
                            if (projectCopies.Count > 128 || paths.Count > 256) return null;
                            if (expected == benchmarkAssembly) benchmarkFound = true;
                        }
                    }
                    if (entry == benchmarkAssembly) benchmarkFound = true;
                    if (!benchmarkFound || paths.Count > 256) return null;
                }
                var captured = new List<(string Path, LiveFileIdentity Identity, string Hash)>();
                foreach (string path in paths.Order(StringComparer.Ordinal))
                {
                    if (!NoInputLinks(path)) return null;
                    LiveFileIdentity identity = await ReadLiveMetadataAsync(path, false, token).ConfigureAwait(false);
                    if ((identity.Bits & 0xf000) != 0x8000 || identity.Bytes is < 1 or > 134_217_728) return null;
                    captured.Add((path, identity, HashLiveExecutable(path, identity.Bytes)));
                }
                string benchmarkCopy = Path.Combine(workerDirectory, Path.GetFileName(benchmarkAssembly));
                await using var candidateHandle = new FileStream(benchmarkCopy, FileMode.Open, FileAccess.Read, FileShare.Read);
                string heldPath = "/proc/" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "/fd/"
                    + candidateHandle.SafeFileHandle.DangerousGetHandle().ToInt64().ToString(CultureInfo.InvariantCulture);
                LiveFileIdentity held = await ReadLiveMetadataAsync(heldPath, true, token).ConfigureAwait(false);
                var copyIdentity = captured.Single(item => item.Path == benchmarkCopy);
                if (held != copyIdentity.Identity || HashLiveExecutable(heldPath, held.Bytes) != copyIdentity.Hash
                    || !TryMatchMappedCandidate(ReadLiveProcBytes(prefix + "maps", 4_194_304), benchmarkCopy,
                        held.DeviceMajor, held.DeviceMinor, held.Inode, held.Bytes, out string? mappingSha256)) return null;
                byte[] environment = ReadLiveProcBytes(prefix + "environ", 262_144);
                var actualEnvironment = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string row in new UTF8Encoding(false, true).GetString(environment).Split('\0', StringSplitOptions.RemoveEmptyEntries))
                {
                    int equals = row.IndexOf('=', StringComparison.Ordinal);
                    if (equals <= 0 || !actualEnvironment.TryAdd(row[..equals], row[(equals + 1)..])) return null;
                }
                foreach (var expected in command.Environment.Where(item => item.Key.StartsWith("DATABASE_", StringComparison.Ordinal)
                    || item.Key.StartsWith("ISLAMU_TEST_", StringComparison.Ordinal) || item.Key is "TMPDIR" or "DOCKER_HOST" or "DOCKER_CONFIG"
                    or "SECRET_PROVIDER" or "AUTHENTICATION_PROVIDER" or "AUTHORIZATION_PROVIDER" or "DEPLOYMENT_MODE"
                    or "IDENTITY_DATABASE_TOPOLOGY" or "INSTANCE_BOOTSTRAP_LOCAL_PASSWORD" or "AUTHENTICATION_LOCAL_JWT_KEY"))
                    if (!actualEnvironment.TryGetValue(expected.Key, out string? actual) || actual != expected.Value) return null;
                RequireOwnedLaunchEnvironment(name => actualEnvironment.GetValueOrDefault(name));
                if (actualEnvironment.ContainsKey("DOCKER_CONTEXT")) return null;
                foreach (var item in captured)
                    if (await ReadLiveMetadataAsync(item.Path, false, token).ConfigureAwait(false) != item.Identity
                        || HashLiveExecutable(item.Path, item.Identity.Bytes) != item.Hash) return null;
                foreach (var copy in projectCopies)
                    if (!ReadBoundedFile(copy.Actual, 134_217_728).AsSpan().SequenceEqual(ReadBoundedFile(copy.Expected, 134_217_728))) return null;
                LiveNativeTool? after = await CaptureLiveNativeToolAsync(invocation, pid, token).ConfigureAwait(false);
                if (after is null || after.Start != executable.Start || after.Sha256 != executable.Sha256
                    || after.Device != executable.Device || after.Inode != executable.Inode
                    || !SameLiveProcess(ReadManagedProcessIdentity(pid), process)
                    || !ReadLiveProcBytes(prefix + "cmdline", 65_536).AsSpan().SequenceEqual(arguments)
                    || !ReadLiveProcBytes(prefix + "environ", 262_144).AsSpan().SequenceEqual(environment)
                    || await ReadLiveMetadataAsync(heldPath, true, token).ConfigureAwait(false) != held
                    || HashLiveExecutable(heldPath, held.Bytes) != copyIdentity.Hash
                    || !TryMatchMappedCandidate(ReadLiveProcBytes(prefix + "maps", 4_194_304), benchmarkCopy,
                        held.DeviceMajor, held.DeviceMinor, held.Inode, held.Bytes, out string? afterMapping)
                    || afterMapping != mappingSha256) return null;
                return new(pid, process.Start, DigestBytes(JsonSerializer.SerializeToUtf8Bytes(new
                { files = captured.Select(item => new { item.Path, item.Hash }), mappingSha256, benchmarkIdentity })), executable, benchmarkIdentity);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or InvalidDataException
                or ArgumentException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException or NotSupportedException)
            { return null; }
        }

        private static BenchmarkStage? ReadBenchmarkStage(string path, string owner, int writer, string endpointHash,
            LiveFileIdentity identity)
        {
            using JsonDocument document = JsonDocument.Parse(ReadBoundedFile(path, 16_384), DocumentOptions);
            JsonElement value = document.RootElement;
            if (!UniqueProperties(value) || !ExactFields(value, "schemaVersion", "owner", "fixtureNonce", "writerPid",
                    "constructedAtUtc", "constructorReturnedAtUtc", "recordedAtUtc", "sequence", "stage", "injectFailure",
                    "resource", "intendedContainerName", "imageReference", "imagePullPolicy", "dockerEndpointSha256",
                    "intendedLabels", "containerId") || value.GetProperty("schemaVersion").GetInt32() != 1
                || Text(value, "owner") != owner || value.GetProperty("writerPid").GetInt32() != writer
                || value.GetProperty("injectFailure").ValueKind != JsonValueKind.Null || Text(value, "resource") != "postgres"
                || Text(value, "imageReference") != "postgres:18-alpine" || Text(value, "imagePullPolicy") != "never"
                || Text(value, "dockerEndpointSha256") != endpointHash) return null;
            string nonce = Text(value, "fixtureNonce"), name = Text(value, "intendedContainerName"), stage = Text(value, "stage");
            int sequence = value.GetProperty("sequence").GetInt32();
            JsonElement labels = value.GetProperty("intendedLabels"), identifier = value.GetProperty("containerId");
            if (nonce.Length != 32 || !LowerHex(nonce) || nonce[12] != '7' || nonce[16] is not ('8' or '9' or 'a' or 'b')
                || name != "islamu-assurance-postgres-" + owner + "-" + nonce || sequence is < 1 or > 1024
                || stage is not ("ConstructorReturned" or "InitializationEntered" or "CreationEntered" or "ContainerStartReturned"
                    or "PurposeLoginsReturned" or "MigrationsReturned" or "DisposalEntered" or "ContainerDisposalReturned")
                || Path.GetFileName(path) != "postgres-" + nonce + "-" + sequence.ToString("D3", CultureInfo.InvariantCulture) + "-" + stage + ".json"
                || !ExactFields(labels, "islamu.assurance.owner", "islamu.assurance.resource", "islamu.assurance.fixture",
                    "islamu.assurance.kind", "islamu.assurance.run")
                || Text(labels, "islamu.assurance.owner") != owner || Text(labels, "islamu.assurance.resource") != "postgres"
                || Text(labels, "islamu.assurance.fixture") != nonce || Text(labels, "islamu.assurance.kind") != "benchmark") return null;
            string instance = Text(labels, "islamu.assurance.run");
            if (instance.Length != 32 || !LowerHex(instance) || instance[12] != '7'
                || instance[16] is not ('8' or '9' or 'a' or 'b') || instance == nonce || instance == owner) return null;
            string? id = identifier.ValueKind == JsonValueKind.Null ? null : identifier.GetString();
            if (id is not null && (id.Length != 64 || !LowerHex(id))
                || stage is "ContainerStartReturned" or "PurposeLoginsReturned" or "MigrationsReturned" or "ContainerDisposalReturned" && id is null)
                return null;
            foreach (string key in new[] { "constructedAtUtc", "constructorReturnedAtUtc", "recordedAtUtc" })
                if (!DateTimeOffset.TryParse(Text(value, key), CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return null;
            return new(nonce, instance, name, stage, sequence, id, writer,
                new(path, identity, DigestBytes(ReadBoundedFile(path, 16_384))));
        }

        internal async Task<(OwnedNativeProcessResult Process, bool Admitted, byte[] Evidence, bool ResourceCleanupComplete,
            IReadOnlyList<string> WorkerIdentities)> RunBenchmarksAsync(
            string owner, string root, ProcessStartInfo command, string dockerHost, string benchmarkAssembly, CancellationToken token)
        {
            byte[] before = await IdentityAsync(token).ConfigureAwait(false);
            string image = await LocalImageIdAsync("postgres:18-alpine", token).ConfigureAwait(false);
            HashSet<string> initialImages = await CachedImageIdsAsync(token).ConfigureAwait(false);
            Dictionary<string, string> initialReferences = await CachedImageReferencesAsync(token).ConfigureAwait(false);
            if (!initialImages.Contains(image) || !initialReferences.TryGetValue("postgres:18-alpine", out string? initialImage)
                || initialImage != image) throw new OwnedExecutionFailure("benchmark_cached_image_missing");
            HashSet<string> initialContainers = await InitialContainersAsync(owner, token).ConfigureAwait(false);
            Dictionary<string, string> initialVolumes = await VolumeMountpointsAsync(token).ConfigureAwait(false);
            IReadOnlySet<string> inheritedTargets = await ImageVolumeTargetsAsync(image, token).ConfigureAwait(false);
            var creations = new Dictionary<string, BenchmarkStage>(StringComparer.Ordinal);
            var seenFiles = new HashSet<string>(StringComparer.Ordinal);
            var seenOwned = new HashSet<string>(StringComparer.Ordinal);
            var observed = new Dictionary<string, IReadOnlyList<ObservedContainerMount>>(StringComparer.Ordinal);
            var volumes = new Dictionary<string, string>(StringComparer.Ordinal);
            var tools = new Dictionary<(int, ulong), LiveNativeTool>();
            var writers = new Dictionary<int, BenchmarkWriter>();
            var failures = new List<Exception>();
            string endpointHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dockerHost)));
            string dotnetSha256 = HashLiveExecutable(ReleaseToolPaths.Dotnet, new FileInfo(ReleaseToolPaths.Dotnet).Length);
            uint? nativeUser = null;
            OwnedNativeProcessContext? nativeIdentity = null;
            bool cleanup = false;
            byte[] after = [];

            async Task RetainInventoryAsync(JsonElement inventory, CancellationToken captureToken)
            {
                _ = ReadAllContainerIds(inventory, owner, requireOwnerAbsent: false);
                Dictionary<string, string>? currentVolumes = null;
                foreach (JsonElement row in inventory.EnumerateArray())
                {
                    string id = Text(row, "Id");
                    if (row.GetProperty("Labels").ValueKind == JsonValueKind.Object
                        && row.GetProperty("Labels").TryGetProperty("islamu.assurance.owner", out var selected) && selected.GetString() == owner)
                    {
                        if (seenOwned.Add(id) && seenOwned.Count > 65_536) throw new OwnedExecutionFailure("benchmark_inventory_limit");
                    }
                    if (!creations.TryGetValue(id, out BenchmarkStage? creation)) continue;
                    if (!BenchmarkContainerMatches(row, creation, owner, image, inheritedTargets, out var mounts))
                        throw new OwnedExecutionFailure("benchmark_container_join_invalid");
                    currentVolumes ??= await VolumeMountpointsAsync(captureToken).ConfigureAwait(false);
                    foreach (ObservedContainerMount mount in mounts!)
                    {
                        if (initialVolumes.ContainsKey(mount.Name) || !currentVolumes.TryGetValue(mount.Name, out string? actual)
                            || actual != mount.Source || volumes.TryGetValue(mount.Name, out string? previous) && previous != actual)
                            throw new OwnedExecutionFailure("benchmark_volume_join_invalid");
                        volumes[mount.Name] = actual;
                    }
                    observed[id] = mounts;
                }
            }

            async Task ObserveAsync(OwnedNativeProcessContext context, CancellationToken stop)
            {
                if (!nativeUser.HasValue) throw new OwnedExecutionFailure("benchmark_native_identity_missing");
                await ValidateApiRootAsync(root, owner, nativeUser.Value, CancellationToken.None).ConfigureAwait(false);
                string[] paths = Directory.EnumerateFiles(root, "postgres-*.json").Take(1025).ToArray();
                if (paths.Length > 1024) throw new OwnedExecutionFailure("benchmark_stage_inventory_limit");
                foreach (string path in paths)
                {
                    seenFiles.Add(path);
                    if (seenFiles.Count > 1024) throw new OwnedExecutionFailure("benchmark_stage_inventory_limit");
                    if (stop.IsCancellationRequested) return;
                    if (!path.EndsWith("-ContainerStartReturned.json", StringComparison.Ordinal)
                        || creations.Values.Any(item => item.File.Path == path)) continue;
                    try
                    {
                        LiveFileIdentity identity = await ReadLiveMetadataAsync(path, false, CancellationToken.None).ConfigureAwait(false);
                        if (!PrivateLiveFile(identity, nativeUser.Value, identity.Bytes) || identity.Bytes is < 1 or > 16_384) continue;
                        int writerPid = ReadBenchmarkWriterPid(path);
                        BenchmarkWriter? writer = await CaptureBenchmarkWriterAsync(context, writerPid, benchmarkAssembly, command, CancellationToken.None).ConfigureAwait(false);
                        if (writer is null || !await ProcessHoldsLiveFileAsync(writerPid, path, identity, CancellationToken.None).ConfigureAwait(false)) continue;
                        BenchmarkStage? stage = ReadBenchmarkStage(path, owner, writerPid, endpointHash, identity);
                        if (stage?.ContainerId is null) continue;
                        if (await ReadLiveMetadataAsync(path, false, CancellationToken.None).ConfigureAwait(false) != identity
                            || DigestBytes(ReadBoundedFile(path, 16_384)) != stage.File.Sha256
                            || !await ProcessHoldsLiveFileAsync(writerPid, path, identity, CancellationToken.None).ConfigureAwait(false)
                            || await CaptureBenchmarkWriterAsync(context, writerPid, benchmarkAssembly, command, CancellationToken.None).ConfigureAwait(false) is not { } currentWriter
                            || currentWriter.Start != writer.Start || currentWriter.InputsSha256 != writer.InputsSha256) continue;
                        if (writers.TryGetValue(writerPid, out var priorWriter) && (priorWriter.Start != writer.Start
                            || priorWriter.InputsSha256 != writer.InputsSha256)) throw new OwnedExecutionFailure("benchmark_writer_changed");
                        if (!writers.ContainsKey(writerPid) && writers.Count >= 128) throw new OwnedExecutionFailure("benchmark_writer_inventory_limit");
                        writers[writerPid] = writer;
                        if (creations.Count >= 64 || initialContainers.Contains(stage.ContainerId) || !creations.TryAdd(stage.ContainerId, stage))
                            throw new OwnedExecutionFailure("benchmark_creation_conflict");
                    }
                    catch (Exception partial) when (partial is JsonException or FileNotFoundException or DirectoryNotFoundException)
                    { /* A newly created stage file may not have completed its bounded write yet. */ }
                }
                foreach (int pid in ReadOwnedChildCandidates(context))
                {
                    if (stop.IsCancellationRequested) return;
                    ManagedProcessIdentity? identity = ReadManagedProcessIdentity(pid);
                    if (identity is null) continue;
                    if (!tools.ContainsKey((pid, identity.Start)))
                    {
                        LiveNativeTool? tool = await CaptureLiveNativeToolAsync(context, pid, CancellationToken.None).ConfigureAwait(false);
                        if (tool is not null) tools[(pid, tool.Start)] = tool;
                    }
                    if (tools.Count > 1024) throw new OwnedExecutionFailure("benchmark_tool_inventory_limit");
                    if (writers.TryGetValue(pid, out var knownWriter))
                    {
                        if (knownWriter.Start != identity.Start) throw new OwnedExecutionFailure("benchmark_writer_changed");
                        continue;
                    }
                    BenchmarkWriter? writer = await CaptureBenchmarkWriterAsync(context, pid, benchmarkAssembly, command, CancellationToken.None).ConfigureAwait(false);
                    if (writer is not null)
                    {
                        if (writers.TryGetValue(pid, out var previousWriter) && (previousWriter.Start != writer.Start
                            || previousWriter.InputsSha256 != writer.InputsSha256)) throw new OwnedExecutionFailure("benchmark_writer_changed");
                        if (!writers.ContainsKey(pid) && writers.Count >= 128) throw new OwnedExecutionFailure("benchmark_writer_inventory_limit");
                        writers[pid] = writer;
                    }
                }
                using JsonDocument document = await GetAsync("/v1.40/containers/json?all=true", CancellationToken.None).ConfigureAwait(false);
                await RetainInventoryAsync(document.RootElement, CancellationToken.None).ConfigureAwait(false);
            }

            OwnedNativeProcessResult process = await RunOwnedNativeProcessAsync(command, TimeSpan.FromMinutes(30), token,
                async (context, stop) =>
                {
                    ManagedProcessIdentity? identity = ReadManagedProcessIdentity(context.Pid);
                    if (identity is null || identity.Start != context.Start || identity.Group != context.Group || identity.Session != context.Session)
                        throw new OwnedExecutionFailure("benchmark_native_identity_missing");
                    nativeUser = identity.User;
                    nativeIdentity = context;
                    var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var registration = stop.Register(() => ended.TrySetResult());
                    while (!stop.IsCancellationRequested)
                    {
                        await ObserveAsync(context, stop).ConfigureAwait(false);
                        if (!stop.IsCancellationRequested) _ = await Task.WhenAny(ended.Task, Task.Delay(50, CancellationToken.None)).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            if (!process.ObserverSettled) return (process, false, [], false, []);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            bool valid = process.Succeeded;
            try
            {
                if (nativeIdentity is null || !nativeUser.HasValue) throw new OwnedExecutionFailure("benchmark_native_identity_missing");
                string[] terminalFiles = Directory.EnumerateFiles(root, "postgres-*.json").Take(1025).ToArray();
                if (terminalFiles.Length > 1024) throw new OwnedExecutionFailure("benchmark_stage_inventory_limit");
                seenFiles.UnionWith(terminalFiles);
                var terminalStages = new List<BenchmarkStage>();
                foreach (string path in terminalFiles)
                {
                    LiveFileIdentity metadata = await ReadLiveMetadataAsync(path, false, deadline.Token).ConfigureAwait(false);
                    if (!NoInputLinks(path) || !PrivateLiveFile(metadata, nativeUser.Value, metadata.Bytes)
                        || metadata.Bytes is < 1 or > 16_384) throw new OwnedExecutionFailure("benchmark_terminal_stage_invalid");
                    int writerPid = ReadBenchmarkWriterPid(path);
                    BenchmarkStage? stage = ReadBenchmarkStage(path, owner, writerPid, endpointHash, metadata);
                    if (stage is null || await ReadLiveMetadataAsync(path, false, deadline.Token).ConfigureAwait(false) != metadata
                        || DigestBytes(ReadBoundedFile(path, 16_384)) != stage.File.Sha256 || !writers.ContainsKey(writerPid))
                        throw new OwnedExecutionFailure("benchmark_terminal_stage_invalid");
                    terminalStages.Add(stage);
                }
                foreach (BenchmarkStage creation in creations.Values)
                {
                    await ValidateApiCreationFileAsync(creation.File, deadline.Token).ConfigureAwait(false);
                    BenchmarkStage[] matching = terminalStages.Where(stage => stage.Fixture == creation.Fixture
                        && stage.InstanceRun == creation.InstanceRun && stage.Name == creation.Name
                        && stage.ContainerId == creation.ContainerId && stage.WriterPid == creation.WriterPid && stage.Sequence > creation.Sequence).ToArray();
                    bool migrated = matching.Any(stage => stage.Stage == "MigrationsReturned");
                    bool disposed = matching.Any(stage => stage.Stage == "ContainerDisposalReturned"
                        && matching.Any(prior => prior.Stage == "MigrationsReturned" && prior.Sequence < stage.Sequence));
                    if (!disposed || !migrated) throw new OwnedExecutionFailure("benchmark_terminal_stage_missing");
                }
                if (terminalStages.GroupBy(stage => stage.Fixture, StringComparer.Ordinal).Any(group =>
                    group.Select(stage => stage.InstanceRun).Distinct(StringComparer.Ordinal).Count() != 1
                    || group.Select(stage => stage.Name).Distinct(StringComparer.Ordinal).Count() != 1
                    || !group.Any(stage => stage.Stage == "ContainerStartReturned" && stage.ContainerId is not null
                        && creations.ContainsKey(stage.ContainerId))))
                    throw new OwnedExecutionFailure("benchmark_terminal_creation_unqualified");
                using (JsonDocument terminalInventory = await GetAsync("/v1.40/containers/json?all=true", deadline.Token).ConfigureAwait(false))
                    await RetainInventoryAsync(terminalInventory.RootElement, deadline.Token).ConfigureAwait(false);
                if (creations.Count == 0 || !creations.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(observed.Keys)
                    || !seenOwned.IsSubsetOf(creations.Keys)
                    || seenFiles.Where(path => path.EndsWith("-ContainerStartReturned.json", StringComparison.Ordinal))
                        .Any(path => !creations.Values.Any(creation => creation.File.Path == path)) || tools.Count == 0
                    || command.ArgumentList.Contains("*EfCoreQueryBenchmarks*") && !writers.Keys.Any(pid => pid != nativeIdentity.Pid))
                    throw new OwnedExecutionFailure("benchmark_observation_incomplete");
                if (tools.Values.Any(tool => tool.Sha256 == dotnetSha256
                    && (!writers.TryGetValue(tool.Pid, out var writer) || writer.Start != tool.Start)))
                    throw new OwnedExecutionFailure("benchmark_managed_tool_inputs_unqualified");
            }
            catch (Exception failure) { failures.Add(failure); valid = false; }
            foreach (var item in creations)
            {
                try
                {
                    using JsonDocument current = await GetAsync("/v1.40/containers/json?all=true", deadline.Token).ConfigureAwait(false);
                    _ = ReadAllContainerIds(current.RootElement, owner, requireOwnerAbsent: false);
                    JsonElement? row = current.RootElement.EnumerateArray().Cast<JsonElement?>().SingleOrDefault(value => Text(value!.Value, "Id") == item.Key);
                    if (row is null) continue;
                    if (!observed.ContainsKey(item.Key) || !BenchmarkContainerMatches(row.Value, item.Value, owner, image, inheritedTargets, out _))
                        throw new OwnedExecutionFailure("benchmark_cleanup_not_owned");
                    await DeleteAsync(item.Key, deadline.Token).ConfigureAwait(false);
                }
                catch (Exception failure) { failures.Add(failure); valid = false; }
            }
            try
            {
                using JsonDocument current = await GetAsync("/v1.40/containers/json?all=true", deadline.Token).ConfigureAwait(false);
                HashSet<string> remaining = ReadAllContainerIds(current.RootElement, owner, requireOwnerAbsent: true);
                if (remaining.Overlaps(creations.Keys) || remaining.Overlaps(seenOwned)
                    || !VolumeConsumersAbsent(current.RootElement, volumes)) throw new OwnedExecutionFailure("benchmark_cleanup_incomplete");
                Dictionary<string, string> currentVolumes = await VolumeMountpointsAsync(deadline.Token).ConfigureAwait(false);
                foreach (var volume in volumes)
                {
                    if (!currentVolumes.TryGetValue(volume.Key, out string? mountpoint)) continue;
                    if (initialVolumes.ContainsKey(volume.Key) || mountpoint != volume.Value)
                        throw new OwnedExecutionFailure("benchmark_volume_cleanup_not_owned");
                    await DeleteVolumeAsync(volume.Key, deadline.Token).ConfigureAwait(false);
                }
                if ((await VolumeMountpointsAsync(deadline.Token).ConfigureAwait(false)).Keys.Intersect(volumes.Keys, StringComparer.Ordinal).Any())
                    throw new OwnedExecutionFailure("benchmark_volume_cleanup_incomplete");
                after = await IdentityAsync(deadline.Token).ConfigureAwait(false);
                RequireSameApiDaemonIdentity(before, after);
                if (await LocalImageIdAsync("postgres:18-alpine", deadline.Token).ConfigureAwait(false) != image)
                    throw new OwnedExecutionFailure("benchmark_image_changed");
                cleanup = failures.Count == 0;
            }
            catch (Exception failure) { failures.Add(failure); valid = false; cleanup = false; }
            process = process with { ObserverFailures = process.ObserverFailures.Concat(failures).ToArray() };
            byte[] evidence = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                creatorAuthentication = "not-attested",
                observationCoverage = "point-in-time-only",
                backendBefore = JsonSerializer.Deserialize<JsonElement>(before),
                backendAfter = after.Length == 0 ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(after),
                initialContainerCount = initialContainers.Count,
                initialVolumeCount = initialVolumes.Count,
                imageReference = "postgres:18-alpine",
                imageId = image,
                creators = creations.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => new
                { containerId = item.Key, item.Value.Name, item.Value.Fixture, item.Value.InstanceRun, item.Value.WriterPid, item.Value.File.Sha256 }),
                writers = writers.Values.OrderBy(item => item.Pid),
                observedContainerIds = observed.Keys.Order(StringComparer.Ordinal),
                tools = tools.Values.OrderBy(item => item.Pid),
                cleanup,
                observationFailureCount = process.ObserverFailures.Count
            });
            return (process, valid && process.Succeeded && cleanup, evidence, cleanup,
                writers.Values.Select(item => item.BenchmarkIdentity).OfType<string>().ToArray());
        }

        private static Dictionary<string, string[]> ReadApiCreationCandidates(string root, string kind = "api")
        {
            if (kind is not ("api" or "direct")) throw new InvalidDataException();
            string prefix = kind + "-container-";
            string[] paths = Directory.EnumerateFiles(root, prefix + "*.json").Take(129).Order(StringComparer.Ordinal).ToArray();
            if (paths.Length > 128) throw new OwnedExecutionFailure("api_creation_inventory_limit");
            var candidates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                string name = Path.GetFileName(path);
                const string declaration = "-declaration.json", created = "-created-";
                int end = name.EndsWith(declaration, StringComparison.Ordinal)
                    ? name.Length - declaration.Length : name.LastIndexOf(created, StringComparison.Ordinal);
                if (end < prefix.Length + 34 || name[end - 33] != '-')
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
            string temporary, string root, string owner, string creationId, CancellationToken token, string kind = "api",
            ISet<string>? pendingHints = null)
        {
            NativeContainerCreationSnapshot? capture = await CaptureLiveNativeContainerCreationsAsync(
                context, temporary, owner, kind, token, creationId).ConfigureAwait(false);
            if (capture is null) return null;
            if (capture.Created.Count == 0)
            {
                if (pendingHints is not null)
                    UpdatePendingDirectHints(pendingHints, creationId, capture.PendingCreationIds.SetEquals([creationId]), completedQualified: false);
                return null;
            }
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
            string stem = Path.Combine(root, kind + "-container-" + pair.Resource + "-" + pair.CreationId);
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
            foreach (ApiCreationFile file in proof.Files) await ValidateApiCreationFileAsync(file, token).ConfigureAwait(false);
        }

        private static async Task ValidateApiCreationFileAsync(ApiCreationFile file, CancellationToken token)
        {
            if (!NoInputLinks(file.Path) || await ReadLiveMetadataAsync(file.Path, false, token).ConfigureAwait(false) != file.Identity
                || DigestBytes(ReadBoundedFile(file.Path, 16384)) != file.Sha256)
                throw new OwnedExecutionFailure("api_creation_records_changed");
        }

        private static bool BenchmarkContainerMatches(JsonElement row, BenchmarkStage creation, string owner, string image,
            IReadOnlySet<string> inherited, out IReadOnlyList<ObservedContainerMount>? mounts)
        {
            mounts = null;
            if (Text(row, "Id") != creation.ContainerId || Text(row, "ImageID") != image || Text(row, "Image") != "postgres:18-alpine"
                || !row.TryGetProperty("Names", out JsonElement names) || names.ValueKind != JsonValueKind.Array
                || !names.EnumerateArray().Any(name => name.ValueKind == JsonValueKind.String && name.GetString() == "/" + creation.Name)
                || !row.TryGetProperty("Labels", out JsonElement labels) || labels.ValueKind != JsonValueKind.Object
                || Text(labels, "islamu.assurance.owner") != owner || Text(labels, "islamu.assurance.resource") != "postgres"
                || Text(labels, "islamu.assurance.fixture") != creation.Fixture || Text(labels, "islamu.assurance.kind") != "benchmark"
                || Text(labels, "islamu.assurance.run") != creation.InstanceRun || !TryReadRuntimeMounts(row, out var parsed)
                || !parsed.Select(mount => mount.Destination).ToHashSet(StringComparer.Ordinal).SetEquals(inherited)
                || parsed.Any(mount => mount.Type != "volume" || !mount.ReadWrite)) return false;
            mounts = parsed;
            return true;
        }

        private static bool VolumeConsumersAbsent(JsonElement inventory, IReadOnlyDictionary<string, string> volumes)
        {
            if (volumes.Count == 0) return true;
            foreach (JsonElement row in inventory.EnumerateArray())
            {
                if (!TryReadRuntimeMounts(row, out var mounts)) return false;
                foreach (ObservedContainerMount mount in mounts)
                    if (mount.Type == "volume" && volumes.ContainsKey(mount.Name)
                        || mount.Type == "bind" && volumes.Values.Any(path => ObservedMountPathsOverlap(path, mount.Source))) return false;
            }
            return true;
        }

        private async Task<IReadOnlySet<string>> ImageVolumeTargetsAsync(string image, CancellationToken token)
        {
            using JsonDocument document = await GetAsync("/v1.40/images/" + Uri.EscapeDataString(image) + "/json", token).ConfigureAwait(false);
            if (Text(document.RootElement, "Id") != image || !document.RootElement.TryGetProperty("Config", out JsonElement config)
                || config.ValueKind != JsonValueKind.Object) throw new OwnedExecutionFailure("image_volume_metadata_invalid");
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (!config.TryGetProperty("Volumes", out JsonElement volumes) || volumes.ValueKind == JsonValueKind.Null) return result;
            if (volumes.ValueKind != JsonValueKind.Object) throw new OwnedExecutionFailure("image_volume_metadata_invalid");
            foreach (JsonProperty volume in volumes.EnumerateObject())
                if (result.Count >= 128 || !CanonicalObservedMountPath(volume.Name) || volume.Value.ValueKind != JsonValueKind.Object
                    || volume.Value.EnumerateObject().Any() || !result.Add(volume.Name))
                    throw new OwnedExecutionFailure("image_volume_metadata_invalid");
            return result;
        }

        private async Task<Dictionary<string, string>> VolumeMountpointsAsync(CancellationToken token)
        {
            using JsonDocument document = await GetAsync("/v1.40/volumes", token).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("Warnings", out JsonElement warnings) && warnings.ValueKind != JsonValueKind.Null
                && (warnings.ValueKind != JsonValueKind.Array || warnings.GetArrayLength() != 0))
                throw new OwnedExecutionFailure("volume_inventory_incomplete");
            JsonElement volumes = root.GetProperty("Volumes");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (volumes.ValueKind == JsonValueKind.Null) return result;
            if (volumes.ValueKind != JsonValueKind.Array || volumes.GetArrayLength() > 65_536)
                throw new OwnedExecutionFailure("volume_inventory_invalid");
            foreach (JsonElement volume in volumes.EnumerateArray())
            {
                string name = Text(volume, "Name"), mountpoint = Text(volume, "Mountpoint");
                if (!RuntimeVolumeName(name) || !CanonicalObservedMountPath(mountpoint)
                    || !BoundedIdentity(Text(volume, "Driver"), 128) || !result.TryAdd(name, mountpoint))
                    throw new OwnedExecutionFailure("volume_inventory_invalid");
            }
            return result;
        }

        private async Task DeleteVolumeAsync(string name, CancellationToken token)
        {
            if (!RuntimeVolumeName(name)) throw new OwnedExecutionFailure("volume_cleanup_invalid");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Delete, "/v1.40/volumes/" + Uri.EscapeDataString(name) + "?force=false");
            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                deadline.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.NoContent) throw new OwnedExecutionFailure("volume_cleanup_failed");
        }

        private sealed class ManagedFixtureObservation(NativeFixtureModelSnapshot model)
        {
            internal NativeFixtureModelSnapshot Model { get; } = model;
            internal Dictionary<string, LivePrivateObservation> Records { get; } = new(StringComparer.Ordinal);
            internal Dictionary<string, NativeContainerAssociation> Containers { get; } = new(StringComparer.Ordinal);
            internal Dictionary<string, NativeManagedLaunchHint> Leads { get; } = new(StringComparer.Ordinal);
            internal Dictionary<string, CompiledInputSnapshot> Hosts { get; } = new(StringComparer.Ordinal);
            internal Dictionary<string, QualifiedManagedInterval> Intervals { get; } = new(StringComparer.Ordinal);
            internal List<ParentPressureSample> RuntimeSamples { get; } = [];
            internal List<ParentPressureDelta> RuntimeDeltas { get; } = [];
            internal Dictionary<string, PressureSchedule> Pressure { get; } = new(StringComparer.Ordinal);
            internal bool WorkloadObserved { get; set; }
            internal bool ProgrammeObserved { get; set; }
            internal Dictionary<string, ConfiguredOwnedResource> Resources { get; } = new(StringComparer.Ordinal);
            internal Dictionary<string, string> Volumes { get; } = new(StringComparer.Ordinal);
            internal HashSet<string> SeenIds { get; } = new(StringComparer.Ordinal);
        }

        private static string ManagedLeadKey(NativeManagedLaunchHint hint) =>
            hint.Resource + ":" + hint.InstanceSha256 + ":" + hint.Pid.ToString(CultureInfo.InvariantCulture)
            + ":" + hint.Start.ToString(CultureInfo.InvariantCulture);

        private static void RetainConfiguredResources(ManagedFixtureObservation fixture,
            IReadOnlyList<ConfiguredOwnedResource> resources, IReadOnlyDictionary<string, string> mountpoints)
        {
            foreach (ConfiguredOwnedResource resource in resources)
            {
                if (!fixture.Containers.Values.Any(association => association.Present
                    && association.ContainerId == resource.Container.ContainerId && association.Resource == resource.Container.Resource
                    && association.ImageId == resource.Container.ImageId)) throw new OwnedExecutionFailure("sdk_container_association_missing");
                if (fixture.Resources.TryGetValue(resource.Container.ContainerId, out var old)
                    && (old.Container.ImageId != resource.Container.ImageId || old.Container.Resource != resource.Container.Resource
                        || !old.Mounts.SequenceEqual(resource.Mounts))) throw new OwnedExecutionFailure("sdk_container_identity_changed");
                fixture.Resources[resource.Container.ContainerId] = resource;
                foreach (ObservedContainerMount mount in resource.Mounts.Where(mount => mount.Type == "volume"))
                    if (!mountpoints.TryGetValue(mount.Name, out string? path)
                        || fixture.Volumes.TryGetValue(mount.Name, out string? previous) && previous != path)
                        throw new OwnedExecutionFailure("sdk_volume_identity_changed");
                    else fixture.Volumes[mount.Name] = path;
            }
        }

        internal async Task<(OwnedNativeProcessResult Process, bool Admitted, byte[] Evidence, bool ResourceCleanupComplete)> RunManagedAsync(
            string owner, string temporary, string root, ProcessStartInfo command, TimeSpan timeout, string repository,
            string candidate, IReadOnlyDictionary<string, OwningManagedTarget> targets, LoadToolInputs? loadTool, SecurityControlRegister.LoadPurpose? loadPurpose,
            SecurityControlRegister.RuntimeLifecycle? lifecycle, CancellationToken token)
        {
            byte[] before = await IdentityAsync(token).ConfigureAwait(false);
            HashSet<string> initialIds = await InitialContainersAsync(owner, token).ConfigureAwait(false);
            HashSet<string> initialImages = await CachedImageIdsAsync(token).ConfigureAwait(false);
            Dictionary<string, string> initialReferences = await CachedImageReferencesAsync(token).ConfigureAwait(false);
            Dictionary<string, string> initialVolumes = await VolumeMountpointsAsync(token).ConfigureAwait(false);
            var initial = new InitialNativeResources(initialIds, initialVolumes.Keys.ToHashSet(StringComparer.Ordinal), initialImages);
            var fixtures = new Dictionary<string, ManagedFixtureObservation>(StringComparer.Ordinal);
            var images = new Dictionary<string, string>(StringComparer.Ordinal);
            var imageTargets = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
            var tools = new Dictionary<(int Pid, ulong Start), LiveNativeTool>();
            var requiredDocker = new HashSet<(int Pid, ulong Start)>();
            var browsers = new HashSet<int>();
            var direct = new Dictionary<string, ApiCreationProof>(StringComparer.Ordinal);
            var directSeen = new HashSet<string>(StringComparer.Ordinal);
            var pendingDirectHints = new HashSet<string>(StringComparer.Ordinal);
            var directResources = new Dictionary<string, ConfiguredOwnedResource>(StringComparer.Ordinal);
            var directVolumes = new Dictionary<string, string>(StringComparer.Ordinal);
            var newIds = new HashSet<string>(StringComparer.Ordinal);
            var failures = new List<Exception>();
            ManagedProcessIdentity? native = null;

            async Task ResolveImageAsync(string reference)
            {
                string id = await LocalImageIdAsync(reference, CancellationToken.None).ConfigureAwait(false);
                if (!initialImages.Contains(id) || !initialReferences.TryGetValue(reference, out string? initialImage)
                    || initialImage != id || images.TryGetValue(reference, out string? previous) && previous != id)
                    throw new OwnedExecutionFailure("declared_image_cache_changed");
                images[reference] = id;
                if (!imageTargets.ContainsKey(id)) imageTargets[id] = await ImageVolumeTargetsAsync(id, CancellationToken.None).ConfigureAwait(false);
            }

            async Task ObserveToolsAsync(OwnedNativeProcessContext context, string observedRoot, string runId,
                IReadOnlyList<string> paths, Dictionary<string, LivePrivateObservation>? retained)
            {
                foreach (string path in paths.Where(path => Path.GetFileName(path).StartsWith("managed-launch-tool-", StringComparison.Ordinal)))
                {
                    LivePrivateObservation? record = await CaptureLivePrivateObservationAsync(context, observedRoot, runId,
                        path, CancellationToken.None).ConfigureAwait(false);
                    if (record is null) continue;
                    if (!TryReadNativeObservationLead(record.Bytes, runId, out _, out NativeToolLead? lead)
                        || lead is null || lead.CreatorPid != context.Pid) throw new OwnedExecutionFailure("native_tool_lead_invalid");
                    if (retained is not null) retained[path] = record;
                    if (lead.Role == "docker")
                    {
                        LiveNativeTool? actual = await CaptureLiveNativeToolAsync(context, lead.Pid!.Value, CancellationToken.None).ConfigureAwait(false);
                        if (actual is null || actual.Start != lead.Start) continue;
                        tools[(actual.Pid, actual.Start)] = actual;
                        requiredDocker.Add((actual.Pid, actual.Start));
                    }
                    else browsers.Add(lead.CreatorPid);
                }
                foreach (int pid in ReadOwnedChildCandidates(context))
                {
                    LiveNativeTool? actual = await CaptureLiveNativeToolAsync(context, pid, CancellationToken.None).ConfigureAwait(false);
                    if (actual is not null) tools[(actual.Pid, actual.Start)] = actual;
                }
                if (tools.Count > 1024) throw new OwnedExecutionFailure("native_tool_inventory_limit");
            }

            bool WorkloadMatches(LiveNativeTool tool, ManagedFixtureObservation fixture) => loadTool is not null
                && tool.WorkloadRun && tool.Sha256 == loadTool.ExecutableSha256 && tool.Script == loadTool.Script
                && tool.ScriptSha256 == loadTool.ScriptSha256 && tool.ConfigPath is not null && tool.ConfigSha256 is not null
                && (InputWithin(tool.ConfigPath, Path.Combine(fixture.Model.Root, "k6"))
                    || InputWithin(tool.ConfigPath, Path.Combine(fixture.Model.Root, "k6-events")))
                && ReadManagedProcessIdentity(tool.Pid) is { State: not ('Z' or 'X') } current && current.Start == tool.Start;

            async Task ObservePressureAsync(OwnedNativeProcessContext context, ManagedFixtureObservation fixture)
            {
                LiveNativeTool[] active = tools.Values.Where(tool => WorkloadMatches(tool, fixture)).ToArray();
                fixture.WorkloadObserved |= active.Length != 0;
                fixture.ProgrammeObserved |= active.Any(tool => InputWithin(tool.ConfigPath!, Path.Combine(fixture.Model.Root, "k6-events")));
                string directory = Path.Combine(temporary, "testing-assurance-modernization", "continuation", "load", fixture.Model.RunId);
                string[] paths = new[] { Path.Combine(directory, "pressure-Split.json"), Path.Combine(directory, "pressure-Standalone.json") }
                    .Where(File.Exists).ToArray();
                if (paths.Length > 1) throw new OwnedExecutionFailure("pressure_topology_ambiguous");
                if (paths.Length == 0) return;
                string path = paths[0];
                if (!NoInputLinks(path)) throw new OwnedExecutionFailure("pressure_schedule_invalid");
                LiveFileIdentity identity = await ReadLiveMetadataAsync(path, false, CancellationToken.None).ConfigureAwait(false);
                if (!PrivateLiveFile(identity, native!.User, identity.Bytes) || identity.Bytes is < 1 or > 4_194_304)
                    throw new OwnedExecutionFailure("pressure_schedule_invalid");
                byte[] bytes = ReadBoundedFile(path, 4_194_304);
                if (await ReadLiveMetadataAsync(path, false, CancellationToken.None).ConfigureAwait(false) != identity) return;
                if (!TryReadPressureSchedule(bytes, out var schedule)) throw new OwnedExecutionFailure("pressure_schedule_invalid");
                fixture.Pressure.TryGetValue(path, out var previous);
                if (previous is not null && (schedule!.RowDigests.Count < previous.RowDigests.Count
                    || !previous.RowDigests.SequenceEqual(schedule.RowDigests.Take(previous.RowDigests.Count))
                    || previous.Final && (!schedule.Final || schedule.Completed != previous.Completed
                        || !previous.RowDigests.SequenceEqual(schedule.RowDigests))))
                    throw new OwnedExecutionFailure("pressure_schedule_prefix_changed");
                fixture.Pressure[path] = schedule!;
                if (previous is not null && previous.RowDigests.Count == schedule!.RowDigests.Count) return;
                if (active.Length == 0) return;
                if (active.Length != 1) throw new OwnedExecutionFailure("pressure_generator_ambiguous");
                var hints = fixture.Leads.Values.Where(hint => hint.Resource is "explore-api" or "event-standalone")
                    .Where(hint => fixture.Hosts.ContainsKey(ManagedLeadKey(hint)))
                    .Where(hint =>
                    {
                        using JsonDocument document = JsonDocument.Parse(fixture.Hosts[ManagedLeadKey(hint)].PrivateManifest, DocumentOptions);
                        JsonElement process = document.RootElement.GetProperty("process");
                        ManagedProcessIdentity? current = ReadManagedProcessIdentity(process.GetProperty("pid").GetInt32());
                        return current is { State: not ('Z' or 'X') } && current.Start == process.GetProperty("start").GetUInt64();
                    }).ToArray();
                if (hints.Length == 0) return;
                if (hints.Length != 1) throw new OwnedExecutionFailure("pressure_api_ambiguous");
                NativeManagedLaunchHint hint = hints[0];
                CompiledInputSnapshot? hostBefore = await CaptureQualifiedManagedResourceAsync(context, temporary, fixture.Model.Root,
                    hint, repository, candidate, targets, CancellationToken.None).ConfigureAwait(false);
                if (hostBefore is null) return;
                using JsonDocument hostDocument = JsonDocument.Parse(hostBefore.PrivateManifest, DocumentOptions);
                JsonElement hostProcess = hostDocument.RootElement.GetProperty("process");
                JsonElement portValue = hostDocument.RootElement.GetProperty("hostRoot").GetProperty("operationsTargetPort");
                JsonElement httpPortValue = hostDocument.RootElement.GetProperty("hostRoot").GetProperty("httpTargetPort");
                if (portValue.ValueKind != JsonValueKind.Number || httpPortValue.ValueKind != JsonValueKind.Number)
                    throw new OwnedExecutionFailure("pressure_target_port_unknown");
                int apiPid = hostProcess.GetProperty("pid").GetInt32(), port = portValue.GetInt32(), httpPort = httpPortValue.GetInt32();
                ulong apiStart = hostProcess.GetProperty("start").GetUInt64();
                OwnedApiListener? listener = await CaptureOwnedApiListenerAsync(context, apiPid, apiStart, port, CancellationToken.None).ConfigureAwait(false);
                OwnedApiListener? httpListener = await CaptureOwnedApiListenerAsync(context, apiPid, apiStart, httpPort, CancellationToken.None).ConfigureAwait(false);
                LiveNativeTool? generator = await CaptureLiveNativeToolAsync(context, active[0].Pid, CancellationToken.None).ConfigureAwait(false);
                if (listener is null || httpListener is null || generator is null || !WorkloadMatches(generator, fixture)) return;
                if (generator.LoadOrigin != new Uri(httpListener.Endpoint).GetLeftPart(UriPartial.Authority))
                    throw new OwnedExecutionFailure("pressure_load_target_not_joined");
                long started = Stopwatch.GetTimestamp();
                RuntimeExporterValues? values = await ReadOwnedRuntimeExporterAsync(listener.Endpoint, CancellationToken.None).ConfigureAwait(false);
                long completed = Stopwatch.GetTimestamp();
                if (values is null) throw new OwnedExecutionFailure("pressure_exporter_unqualified");
                OwnedApiListener? listenerAfter = await CaptureOwnedApiListenerAsync(context, apiPid, apiStart, port, CancellationToken.None).ConfigureAwait(false);
                OwnedApiListener? httpListenerAfter = await CaptureOwnedApiListenerAsync(context, apiPid, apiStart, httpPort, CancellationToken.None).ConfigureAwait(false);
                LiveNativeTool? generatorAfter = await CaptureLiveNativeToolAsync(context, generator.Pid, CancellationToken.None).ConfigureAwait(false);
                CompiledInputSnapshot? hostAfter = await CaptureQualifiedManagedResourceAsync(context, temporary, fixture.Model.Root,
                    hint, repository, candidate, targets, CancellationToken.None).ConfigureAwait(false);
                if (listenerAfter != listener || httpListenerAfter != httpListener || hostAfter?.PrivateManifestSha256 != hostBefore.PrivateManifestSha256
                    || generatorAfter is null || generatorAfter.Start != generator.Start || !WorkloadMatches(generatorAfter, fixture)
                    || generatorAfter.Sha256 != generator.Sha256 || generatorAfter.ConfigSha256 != generator.ConfigSha256
                    || generatorAfter.LoadOrigin != generator.LoadOrigin) return;
                var measured = new ParentPressureSample(new(apiPid, apiStart, Text(hostProcess, "executableSha256"), started, completed,
                    values.CpuUserSeconds, values.CpuSystemSeconds, values.WorkingSetBytes), listener,
                    generator.Pid, generator.Start, generator.Sha256, Stopwatch.Frequency)
                { HttpListener = httpListener with { Endpoint = generator.LoadOrigin! } };
                if (fixture.RuntimeSamples.Count >= 900) throw new OwnedExecutionFailure("pressure_parent_sample_limit");
                if (fixture.RuntimeSamples.Count != 0)
                {
                    RuntimePressureIdentity prior = fixture.RuntimeSamples[^1].Api;
                    if (prior.Pid == measured.Api.Pid && prior.Start == measured.Api.Start
                        && (measured.Api.CpuUserSeconds < prior.CpuUserSeconds || measured.Api.CpuSystemSeconds < prior.CpuSystemSeconds))
                        throw new OwnedExecutionFailure("pressure_process_counter_reset");
                }
                if (fixture.RuntimeSamples.Count != 0 && TryCreateParentPressureDelta(fixture.RuntimeSamples[^1], measured, out var delta))
                    fixture.RuntimeDeltas.Add(delta!);
                fixture.RuntimeSamples.Add(measured);
            }

            async Task ObserveAsync(OwnedNativeProcessContext context)
            {
                string parent = Path.Combine(temporary, "islamu-assurance");
                string[] roots = Directory.EnumerateDirectories(parent).Take(65).Order(StringComparer.Ordinal).ToArray();
                if (roots.Length > 64) throw new OwnedExecutionFailure("fixture_inventory_limit");
                using JsonDocument inventory = await GetAsync("/v1.40/containers/json?all=true", CancellationToken.None).ConfigureAwait(false);
                _ = ReadAllContainerIds(inventory.RootElement, owner, false);
                foreach (JsonElement item in inventory.RootElement.EnumerateArray())
                    if (!initialIds.Contains(Text(item, "Id"))) newIds.Add(Text(item, "Id"));
                if (newIds.Count > 65_536) throw new OwnedExecutionFailure("native_resource_inventory_limit");
                Dictionary<string, string> mountpoints = await VolumeMountpointsAsync(CancellationToken.None).ConfigureAwait(false);
                Dictionary<string, string[]> candidates = ReadApiCreationCandidates(root, "direct");
                foreach (string id in candidates.Keys)
                {
                    directSeen.Add(id);
                    if (directSeen.Count > 64) throw new OwnedExecutionFailure("direct_creation_inventory_limit");
                    if (direct.TryGetValue(id, out var prior)) { await ValidateApiCreationAsync(prior, candidates[id], CancellationToken.None).ConfigureAwait(false); continue; }
                    ApiCreationProof? proof = await CaptureApiCreationAsync(context, temporary, root, owner, id,
                        CancellationToken.None, "direct", pendingDirectHints).ConfigureAwait(false);
                    if (proof is not null)
                    {
                        await ResolveImageAsync(proof.Creation.ImageReference).ConfigureAwait(false);
                        direct.Add(id, proof);
                        UpdatePendingDirectHints(pendingDirectHints, id, heldPendingObserved: false, completedQualified: true);
                    }
                }
                foreach (string fixtureRoot in roots)
                {
                    string runId = Path.GetFileName(fixtureRoot);
                    if (runId == owner) continue;
                    NativeFixtureModelSnapshot? model = await CaptureLiveFixtureModelAsync(context, temporary, fixtureRoot,
                        runId, CancellationToken.None).ConfigureAwait(false);
                    if (model is null) continue;
                    if (!fixtures.TryGetValue(runId, out ManagedFixtureObservation? fixture))
                    {
                        if (inventory.RootElement.EnumerateArray().Any(row => initialIds.Contains(Text(row, "Id")) && ContainerOwner(row) == runId))
                            throw new OwnedExecutionFailure("fixture_initial_resource_present");
                        fixture = new(model); fixtures.Add(runId, fixture);
                    }
                    if (model.ModelSha256 != fixture.Model.ModelSha256) throw new OwnedExecutionFailure("fixture_model_changed");
                    foreach (ConfiguredContainerDeclaration declaration in model.Containers) await ResolveImageAsync(declaration.Image).ConfigureAwait(false);
                    string[] paths = Directory.EnumerateFiles(fixtureRoot, "managed-launch-*.json").Take(1025).Order(StringComparer.Ordinal).ToArray();
                    if (paths.Length > 1024) throw new OwnedExecutionFailure("fixture_observation_inventory_limit");
                    foreach (string path in paths.Where(path => !Path.GetFileName(path).StartsWith("managed-launch-tool-", StringComparison.Ordinal)))
                    {
                        LivePrivateObservation? record = await CaptureLivePrivateObservationAsync(context, fixtureRoot, runId,
                            path, CancellationToken.None).ConfigureAwait(false);
                        if (record is null) continue;
                        if (fixture.Records.TryGetValue(path, out var previous) && previous.Sha256 != record.Sha256)
                            throw new OwnedExecutionFailure("fixture_observation_changed");
                        fixture.Records[path] = record;
                        if (TryReadNativeObservationLead(record.Bytes, runId, out var association, out _)
                            && association is not null)
                        {
                            if (!model.Containers.Any(item => item.Name == association.Resource))
                                throw new OwnedExecutionFailure("sdk_container_resource_unknown");
                            string key = association.Resource + ":" + association.InstanceSha256 + ":" + association.ContainerId;
                            if (!fixture.Containers.TryGetValue(key, out var prior) || association.Present || !prior.Present)
                                fixture.Containers[key] = association;
                            continue;
                        }
                        if (!TryReadManagedLaunchHint(record.Bytes, runId, out var hint, out _) || hint is null
                            || !model.Projects.Any(project => project.Name == hint.Resource))
                            throw new OwnedExecutionFailure("sdk_managed_resource_unknown");
                        ManagedProcessIdentity? lead = ReadManagedProcessIdentity(hint.Pid);
                        if (lead is null || lead.Start != hint.Start || lead.User != native!.User || lead.Start < context.Start) continue;
                        string hintKey = ManagedLeadKey(hint);
                        fixture.Leads[hintKey] = hint;
                        long captureStarted = Stopwatch.GetTimestamp();
                        CompiledInputSnapshot? host = await CaptureQualifiedManagedResourceAsync(context, temporary, fixtureRoot,
                            hint, repository, candidate, targets, CancellationToken.None).ConfigureAwait(false);
                        if (host is not null)
                        {
                            long captureCompleted = Stopwatch.GetTimestamp();
                            using JsonDocument hostManifest = JsonDocument.Parse(host.PrivateManifest, DocumentOptions);
                            JsonElement hosted = hostManifest.RootElement.GetProperty("process");
                            var interval = new QualifiedManagedInterval(runId, hint.Resource, hosted.GetProperty("pid").GetInt32(),
                                hosted.GetProperty("start").GetUInt64(), Text(hosted, "executableSha256"), captureStarted,
                                captureCompleted, Stopwatch.Frequency);
                            if (fixture.Intervals.TryGetValue(hintKey, out var previousInterval))
                            {
                                if (previousInterval.Pid != interval.Pid || previousInterval.Start != interval.Start
                                    || previousInterval.ExecutableSha256 != interval.ExecutableSha256)
                                    throw new OwnedExecutionFailure("managed_interval_identity_changed");
                                interval = interval with { FirstTimestamp = previousInterval.FirstTimestamp };
                            }
                            fixture.Intervals[hintKey] = interval;
                            fixture.Hosts[hintKey] = host;
                        }
                    }
                    await ObserveToolsAsync(context, fixtureRoot, runId, paths, fixture.Records).ConfigureAwait(false);
                    await ObservePressureAsync(context, fixture).ConfigureAwait(false);
                    var eligible = new List<JsonElement>();
                    foreach (JsonElement row in inventory.RootElement.EnumerateArray())
                    {
                        string id = Text(row, "Id");
                        if (ContainerOwner(row) != runId)
                        {
                            if (IncludeForeignRowInSdkInventory(id, ContainerOwner(row), initialIds,
                                direct.Values.Select(proof => proof.Creation.ContainerId).ToHashSet(StringComparer.Ordinal), pendingDirectHints.Count != 0))
                                eligible.Add(row);
                            continue;
                        }
                        fixture.SeenIds.Add(id);
                        if (fixture.Containers.Values.Any(association => association.ContainerId == id && association.Present))
                        {
                            NativeContainerAssociation association = fixture.Containers.Values.First(item => item.ContainerId == id && item.Present);
                            if (!ContainerAssociationMatches(row, association)) throw new OwnedExecutionFailure("sdk_container_instance_changed");
                            eligible.Add(row);
                        }
                    }
                    var selectedMounts = new Dictionary<string, string>(fixture.Volumes, StringComparer.Ordinal);
                    foreach (string name in model.Containers.SelectMany(item => item.Mounts).Where(mount => mount.Type == "Volume").Select(mount => mount.Source))
                        if (mountpoints.TryGetValue(name, out string? path)) selectedMounts[name] = path;
                    foreach (JsonElement row in eligible.Where(row => ContainerOwner(row) == runId))
                        if (TryReadRuntimeMounts(row, out var mounts))
                            foreach (ObservedContainerMount mount in mounts.Where(mount => mount.Type == "volume"))
                                if (mountpoints.TryGetValue(mount.Name, out string? path)) selectedMounts[mount.Name] = path;
                    var inputs = new ConfiguredResourceInputs(runId, model.Containers, images, initial, imageTargets, selectedMounts);
                    if (!TryReadConfiguredOwnedResources(JsonSerializer.SerializeToUtf8Bytes(eligible), inputs, out var observed))
                        throw new OwnedExecutionFailure("sdk_resource_model_join_failed");
                    RetainConfiguredResources(fixture, observed!, mountpoints);
                    if (fixture.SeenIds.Count > 1024 || fixture.Leads.Count > 320 || fixture.Containers.Count > 64)
                        throw new OwnedExecutionFailure("fixture_incarnation_inventory_limit");
                }
                string[] nativePaths = Directory.EnumerateFiles(root, "managed-launch-tool-*.json").Take(129).ToArray();
                if (nativePaths.Length > 128) throw new OwnedExecutionFailure("native_tool_lead_inventory_limit");
                await ObserveToolsAsync(context, root, owner, nativePaths, null).ConfigureAwait(false);
                foreach (ManagedFixtureObservation fixture in fixtures.Values)
                    foreach (ConfiguredMountDeclaration mount in fixture.Model.Containers.SelectMany(item => item.Mounts)
                        .Where(mount => mount.Type == "Volume"))
                        if (!initial.VolumeNames.Contains(mount.Source) && mountpoints.TryGetValue(mount.Source, out string? selectedPath)
                            && !initialVolumes.Values.Any(path => ObservedMountPathsOverlap(path, selectedPath)))
                        {
                            if (directVolumes.TryGetValue(mount.Source, out string? previousPath) && previousPath != selectedPath)
                                throw new OwnedExecutionFailure("direct_volume_identity_changed");
                            directVolumes[mount.Source] = selectedPath;
                        }
                foreach (JsonElement row in inventory.RootElement.EnumerateArray())
                {
                    ApiCreationProof? proof = direct.Values.SingleOrDefault(item => item.Creation.ContainerId == Text(row, "Id"));
                    if (proof is null) continue;
                    string selectedImage = images[proof.Creation.ImageReference];
                    if (!TrySelectDirectImageVolumeMountpoints(row, proof.Creation, selectedImage, imageTargets[selectedImage],
                        mountpoints, initial, out var imageVolumes)) throw new OwnedExecutionFailure("direct_image_volume_invalid");
                    foreach (var volume in imageVolumes!)
                    {
                        if (initialVolumes.Values.Any(path => ObservedMountPathsOverlap(path, volume.Value))
                            || directVolumes.TryGetValue(volume.Key, out string? previousPath) && previousPath != volume.Value)
                            throw new OwnedExecutionFailure("direct_volume_identity_changed");
                        directVolumes[volume.Key] = volume.Value;
                    }
                }
                if (!TryReadDirectCreatedResources(JsonSerializer.SerializeToUtf8Bytes(inventory.RootElement), owner,
                    new(direct.Values.Select(proof => proof.Creation).ToArray(), new HashSet<string>(StringComparer.Ordinal), []),
                    images, initial, imageTargets, directVolumes, out var directObserved)) throw new OwnedExecutionFailure("direct_resource_join_failed");
                foreach (ConfiguredOwnedResource item in directObserved!) directResources[item.Container.ContainerId] = item;
            }

            OwnedNativeProcessResult process = await RunOwnedNativeProcessAsync(command, timeout, token, async (context, stop) =>
            {
                native = ReadManagedProcessIdentity(context.Pid);
                if (native is null || native.Start != context.Start || native.Group != context.Group || native.Session != context.Session)
                    throw new OwnedExecutionFailure("native_creator_identity_missing");
                while (!stop.IsCancellationRequested)
                {
                    await ObserveAsync(context).ConfigureAwait(false);
                    try { await Task.Delay(50, stop).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                }
            }).ConfigureAwait(false);
            if (!process.ObserverSettled)
                return (process, false, JsonSerializer.SerializeToUtf8Bytes(new
                {
                    observed = false,
                    cleanup = false,
                    reason = "managed_observer_unsettled"
                }), false);
            bool cleanup = false, admitted = false;
            byte[] after = [];
            try
            {
                if (!process.ObserverSettled || native is null) throw new OwnedExecutionFailure("managed_observer_unsettled");
                foreach (ManagedFixtureObservation fixture in fixtures.Values)
                {
                    ValidateManagedTerminal(temporary, fixture, native.User, lifecycle);
                    if (fixture.ProgrammeObserved && fixture.Pressure.Count == 0)
                        throw new OwnedExecutionFailure("programme_pressure_observation_missing");
                    string pressureDirectory = Path.Combine(temporary, "testing-assurance-modernization", "continuation", "load", fixture.Model.RunId);
                    foreach (string topology in new[] { "Split", "Standalone" })
                        if (File.Exists(Path.Combine(pressureDirectory, "pressure-" + topology + ".json"))
                            && !fixture.Pressure.ContainsKey(Path.Combine(pressureDirectory, "pressure-" + topology + ".json")))
                            throw new OwnedExecutionFailure("pressure_live_observation_missing");
                    foreach (var observed in fixture.Pressure)
                    {
                        string pressure = observed.Key;
                        LiveFileIdentity identity = await ReadLiveMetadataAsync(pressure, false, CancellationToken.None).ConfigureAwait(false);
                        if (!NoInputLinks(pressure) || !PrivateLiveFile(identity, native.User, identity.Bytes)
                            || identity.Bytes is < 1 or > 4_194_304)
                            throw new OwnedExecutionFailure("runtime_pressure_artifact_invalid");
                        byte[] samples = ReadBoundedFile(pressure, 4_194_304);
                        if (!TryReadPressureSchedule(samples, out var terminal) || !terminal!.Final || !terminal.Completed
                            || terminal.RowDigests.Count < observed.Value.RowDigests.Count
                            || !observed.Value.RowDigests.SequenceEqual(terminal.RowDigests.Take(observed.Value.RowDigests.Count))
                            || await ReadLiveMetadataAsync(pressure, false, CancellationToken.None).ConfigureAwait(false) != identity
                            || !ReadBoundedFile(pressure, 4_194_304).AsSpan().SequenceEqual(samples)
                            || fixture.RuntimeSamples.Count < 2 || fixture.RuntimeDeltas.Count == 0)
                            throw new OwnedExecutionFailure("runtime_pressure_identity_unqualified");
                    }
                }
                if (!LoadPurposeObservationsMatch(loadPurpose, fixtures.Values.Any(fixture => fixture.WorkloadObserved),
                    fixtures.Values.Any(fixture => fixture.ProgrammeObserved), fixtures.Values.Any(fixture =>
                        fixture.Pressure.Count != 0 && fixture.RuntimeSamples.Count >= 2 && fixture.RuntimeDeltas.Count != 0)))
                    throw new OwnedExecutionFailure("load_purpose_observation_incomplete");
                if (!DirectCreationObservationComplete(directSeen, direct.ToDictionary(item => item.Key, item => item.Value.Creation, StringComparer.Ordinal),
                    directResources.Keys.ToHashSet(StringComparer.Ordinal)))
                    throw new OwnedExecutionFailure("direct_creation_unqualified");
                if (lifecycle?.DirectContainers == true && (direct.Count == 0 || requiredDocker.Count == 0))
                    throw new OwnedExecutionFailure("direct_creation_required");
                if ((lifecycle is null || lifecycle.Stage is SecurityControlRegister.RuntimeStage.Build or SecurityControlRegister.RuntimeStage.Startup
                        or SecurityControlRegister.RuntimeStage.Hosted) && fixtures.Count == 0
                    && lifecycle?.Completion != SecurityControlRegister.RuntimeCompletion.BuilderFault)
                    throw new OwnedExecutionFailure("fixture_observation_missing");
                foreach (ManagedFixtureObservation fixture in fixtures.Values)
                    if (!fixture.SeenIds.IsSubsetOf(fixture.Resources.Keys.ToHashSet(StringComparer.Ordinal)))
                        throw new OwnedExecutionFailure("sdk_seen_resource_unqualified");
                if (!newIds.IsSubsetOf(fixtures.Values.SelectMany(fixture => fixture.Resources.Keys)
                    .Concat(directResources.Keys).ToHashSet(StringComparer.Ordinal)))
                    throw new OwnedExecutionFailure("native_new_resource_unqualified");
                foreach (int creator in browsers)
                {
                    LiveNativeTool[] drivers = tools.Values.Where(tool => tool.Script is not null && tool.Ancestors.Contains(creator)).ToArray();
                    if (drivers.Length != 1 || tools.Values.Count(tool => tool.Script is null
                        && tool.Ancestors.Skip(1).FirstOrDefault() == drivers[0].Pid) != 1)
                        throw new OwnedExecutionFailure("browser_tool_relation_unknown");
                }
                admitted = process.Succeeded;
            }
            catch (Exception failure) { failures.Add(failure); }
            try
            {
                using JsonDocument final = await GetAsync("/v1.40/containers/json?all=true", CancellationToken.None).ConfigureAwait(false);
                HashSet<string> finalIds = ReadAllContainerIds(final.RootElement, owner, true);
                var knownIds = fixtures.Values.SelectMany(fixture => fixture.SeenIds).Concat(directResources.Keys).ToHashSet(StringComparer.Ordinal);
                var volumes = fixtures.Values.SelectMany(fixture => fixture.Volumes).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
                foreach (ConfiguredOwnedResource resource in directResources.Values)
                    foreach (ObservedContainerMount mount in resource.Mounts.Where(mount => mount.Type == "volume")) volumes[mount.Name] = mount.Source;
                Dictionary<string, string> finalVolumes = await VolumeMountpointsAsync(CancellationToken.None).ConfigureAwait(false);
                cleanup = !finalIds.Overlaps(knownIds) && !finalVolumes.Keys.Intersect(volumes.Keys, StringComparer.Ordinal).Any()
                    && !finalIds.Except(initialIds).Any() && VolumeConsumersAbsent(final.RootElement, volumes)
                    && newIds.IsSubsetOf(knownIds);
                after = await IdentityAsync(CancellationToken.None).ConfigureAwait(false);
                RequireSameApiDaemonIdentity(before, after);
                foreach (var image in images)
                    if (await LocalImageIdAsync(image.Key, CancellationToken.None).ConfigureAwait(false) != image.Value)
                        throw new OwnedExecutionFailure("declared_image_cache_changed");
            }
            catch (Exception failure) { failures.Add(failure); cleanup = false; }
            process = process with { ObserverFailures = process.ObserverFailures.Concat(failures).ToArray() };
            byte[] evidence = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                nativeUidBoundByInvocation = true,
                creatorAuthentication = "not-attested",
                selectedBackendBefore = JsonSerializer.Deserialize<JsonElement>(before),
                selectedBackendAfter = after.Length == 0 ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(after),
                fixtures = fixtures.Values.Select(fixture => new
                {
                    fixture.Model.RunId,
                    fixture.Model.ModelSha256,
                    containerAssociations = fixture.Containers.Values,
                    resources = fixture.Resources.Values,
                    runtimeSamples = fixture.RuntimeSamples,
                    runtimeDeltas = fixture.RuntimeDeltas,
                    exporterContractQualified = fixture.RuntimeSamples.Count != 0,
                    resetIdentityQualified = fixture.RuntimeDeltas.Count != 0,
                    measurementSource = "parent-owned-target-scrape",
                    capacityEvidenceEligible = false,
                    managed = fixture.Hosts.Select(host => new
                    {
                        incarnation = host.Key,
                        host.Value.PrivateManifestSha256,
                        manifest = JsonSerializer.Deserialize<JsonElement>(host.Value.PrivateManifest)
                    })
                }),
                directCreations = direct.Values.Select(proof => proof.Creation),
                actualTools = tools.Values,
                knownResourceCleanupComplete = cleanup,
                observationCoverage = "point-in-time-only"
            });
            if (evidence.Length > MaximumReportBytes) { evidence = JsonSerializer.SerializeToUtf8Bytes(new { admitted = false }); admitted = false; }
            return (process, admitted && cleanup && failures.Count == 0, evidence, cleanup);
        }

        private static string? ContainerOwner(JsonElement row)
        {
            return row.TryGetProperty("Labels", out JsonElement labels) && labels.ValueKind == JsonValueKind.Object
                && labels.TryGetProperty("islamu.assurance.owner", out JsonElement owner) ? owner.GetString() : null;
        }

        private static bool ContainerAssociationMatches(JsonElement row, NativeContainerAssociation association)
        {
            if (Text(row, "Id") != association.ContainerId || Text(row, "ImageID") != association.ImageId) return false;
            var references = new HashSet<string>(StringComparer.Ordinal) { association.ContainerId };
            if (!row.TryGetProperty("Names", out JsonElement names) || names.ValueKind != JsonValueKind.Array || names.GetArrayLength() > 32) return false;
            foreach (JsonElement nameValue in names.EnumerateArray())
            {
                if (nameValue.ValueKind != JsonValueKind.String) return false;
                string name = nameValue.GetString()!;
                if (!BoundedIdentity(name, 256)) return false;
                references.Add(name);
                if (name.StartsWith('/') && name.Length > 1 && !name[1..].Contains('/', StringComparison.Ordinal)) references.Add(name[1..]);
            }
            return references.Any(reference => DigestBytes(Encoding.UTF8.GetBytes(reference)) == association.InstanceSha256);
        }

        private static void ValidateManagedTerminal(string temporary, ManagedFixtureObservation fixture, uint user,
            SecurityControlRegister.RuntimeLifecycle? lifecycle)
        {
            string directory = Path.Combine(temporary, "islamu-assurance-managed-launches-" + fixture.Model.RunId);
            string path = Path.Combine(directory, "managed-launches.json");
            if (!NoInputLinks(directory) || !NoInputLinks(path) || !PrivateDirectory(directory))
                throw new OwnedExecutionFailure("managed_terminal_missing");
            LiveFileIdentity identity = ReadLiveMetadataAsync(path, false, CancellationToken.None).GetAwaiter().GetResult();
            if (!PrivateLiveFile(identity, user, identity.Bytes) || identity.Bytes is < 1 or > MaximumReportBytes)
                throw new OwnedExecutionFailure("managed_terminal_invalid");
            byte[] bytes = ReadBoundedFile(path, MaximumReportBytes);
            using JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions);
            JsonElement value = document.RootElement;
            if (!UniqueProperties(value) || !ExactFields(value, "Schema", "RunId", "LaunchInputs", "HostContentRoot", "ExpectedResources",
                "StartupFailureDiagnostic", "Rows", "ContainerRows") || value.GetProperty("Schema").GetInt32() != 1
                || Text(value, "RunId") != fixture.Model.RunId || Text(value, "LaunchInputs") != "not-attested"
                || Text(value, "HostContentRoot") != "unknown") throw new OwnedExecutionFailure("managed_terminal_invalid");
            JsonElement rows = value.GetProperty("Rows"), containers = value.GetProperty("ContainerRows");
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 320
                || containers.ValueKind != JsonValueKind.Array || containers.GetArrayLength() > 64)
                throw new OwnedExecutionFailure("managed_terminal_inventory_invalid");
            var expected = value.GetProperty("ExpectedResources").EnumerateArray().Select(item => item.GetString()!).ToArray();
            if (expected.Length != fixture.Model.Projects.Count || !expected.ToHashSet(StringComparer.Ordinal)
                .SetEquals(fixture.Model.Projects.Select(project => project.Name))) throw new OwnedExecutionFailure("managed_terminal_project_changed");
            var retainedLeads = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement row in rows.EnumerateArray())
            {
                byte[] hintBytes = JsonSerializer.SerializeToUtf8Bytes(new { Schema = 1, RunId = fixture.Model.RunId, Hint = row });
                if (!TryReadManagedLaunchHint(hintBytes, fixture.Model.RunId, out var hint, out _) || hint is null
                    || !retainedLeads.Add(ManagedLeadKey(hint)) || !fixture.Leads.ContainsKey(ManagedLeadKey(hint)))
                    throw new OwnedExecutionFailure("managed_terminal_unknown_incarnation");
                bool mayBePartial = lifecycle?.Stage is SecurityControlRegister.RuntimeStage.None or SecurityControlRegister.RuntimeStage.Build
                    or SecurityControlRegister.RuntimeStage.Startup;
                if (!fixture.Hosts.ContainsKey(ManagedLeadKey(hint)) && (!mayBePartial
                    || lifecycle?.Completion == SecurityControlRegister.RuntimeCompletion.Refused && hint.Resource != lifecycle.Resource))
                    throw new OwnedExecutionFailure("managed_host_inputs_unqualified");
            }
            if (!retainedLeads.SetEquals(fixture.Leads.Keys)) throw new OwnedExecutionFailure("managed_terminal_incarnation_missing");
            var retainedContainers = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement row in containers.EnumerateArray())
            {
                byte[] associationBytes = JsonSerializer.SerializeToUtf8Bytes(new { Schema = 1, RunId = fixture.Model.RunId, Container = row });
                if (!TryReadNativeObservationLead(associationBytes, fixture.Model.RunId, out var association, out _) || association is null
                    || !retainedContainers.Add(association.Resource + ":" + association.InstanceSha256 + ":" + association.ContainerId)
                    || !fixture.Containers.Values.Any(prior => prior.Resource == association.Resource
                        && prior.InstanceSha256 == association.InstanceSha256 && prior.ContainerId == association.ContainerId
                        && prior.ImageId == association.ImageId)
                    || !fixture.Resources.ContainsKey(association.ContainerId)) throw new OwnedExecutionFailure("managed_terminal_unknown_container");
            }
            if (!retainedContainers.SetEquals(fixture.Containers.Keys)) throw new OwnedExecutionFailure("managed_terminal_container_missing");
            string diagnostic = Text(value, "StartupFailureDiagnostic");
            if (lifecycle?.Completion == SecurityControlRegister.RuntimeCompletion.Cancelled
                && (!diagnostic.Contains(":canceled_linked:linked=1:caller=1:fixture=0", StringComparison.Ordinal)))
                throw new OwnedExecutionFailure("managed_expected_cancellation_unobserved");
            if (lifecycle?.Completion == SecurityControlRegister.RuntimeCompletion.Refused
                && !diagnostic.Contains(":resource_start_failed:", StringComparison.Ordinal))
                throw new OwnedExecutionFailure("managed_expected_refusal_unobserved");
            if ((lifecycle is null || lifecycle.Stage == SecurityControlRegister.RuntimeStage.Hosted)
                && fixture.Model.Projects.Any(project => !fixture.Hosts.Keys.Any(key => key.StartsWith(project.Name + ":", StringComparison.Ordinal))))
                throw new OwnedExecutionFailure("managed_expected_host_missing");
            if (ReadLiveMetadataAsync(path, false, CancellationToken.None).GetAwaiter().GetResult() != identity
                || !ReadBoundedFile(path, MaximumReportBytes).AsSpan().SequenceEqual(bytes))
                throw new OwnedExecutionFailure("managed_terminal_changed");
        }

        internal async Task<(OwnedNativeProcessResult Process, bool Admitted, byte[] Evidence, bool ResourceCleanupComplete)> RunAsync(
            string owner, string temporary, string root, ProcessStartInfo command, bool requireRunningPrerequisites, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            byte[] before = await IdentityAsync(CancellationToken.None).ConfigureAwait(false);
            HashSet<string> cachedImageIds = await CachedImageIdsAsync(CancellationToken.None).ConfigureAwait(false);
            Dictionary<string, string> initialReferences = await CachedImageReferencesAsync(CancellationToken.None).ConfigureAwait(false);
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
                    if (!cachedImageIds.Contains(image) || !initialReferences.TryGetValue(proof.Creation.ImageReference, out string? initialImage)
                        || initialImage != image || images.TryGetValue(proof.Creation.ImageReference, out string? previousImage)
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
                RequireSameApiDaemonIdentity(before, after);
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
                schemaVersion = 1,
                selectedBackendBefore = JsonSerializer.Deserialize<JsonElement>(before),
                selectedBackendAfter = after.Length == 0 ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(after),
                daemonContinuity = "unknown",
                creatorAuthentication = "not-attested",
                initialContainerCount = initial.Count,
                initialContainerInventorySha256 = DigestBytes(JsonSerializer.SerializeToUtf8Bytes(initial.Order(StringComparer.Ordinal))),
                resources = known.Values.OrderBy(item => item.ContainerId, StringComparer.Ordinal)
                    .Select(item => new { item.ContainerId, item.ImageId, item.Resource, runningObserved = item.Running }),
                creatorObservations = qualified.Values.OrderBy(proof => proof.Creation.CreationId, StringComparer.Ordinal)
                    .Select(proof => new
                    {
                        proof.Creation,
                        witnessSha256 = DigestBytes(proof.PrivateManifest),
                        witness = JsonSerializer.Deserialize<JsonElement>(proof.PrivateManifest)
                    }),
                seenCreationIds = seenCreations.Order(StringComparer.Ordinal),
                seenOwnedContainerIds = seenOwnedIds.Order(StringComparer.Ordinal),
                runningPrerequisites = running.Order(StringComparer.Ordinal),
                cachedImages = images.OrderBy(image => image.Key, StringComparer.Ordinal)
                    .Select(image => new { reference = image.Key, imageId = image.Value }),
                initialImageCount = cachedImageIds.Count,
                initialImageInventorySha256 = DigestBytes(JsonSerializer.SerializeToUtf8Bytes(cachedImageIds.Order(StringComparer.Ordinal))),
                boundedObservationsAdmitted = valid,
                observationCoverage = "point-in-time-only",
                knownContainerCleanupComplete = cleanup,
                observationFailureCount = process.ObserverFailures.Count
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
            return JsonSerializer.SerializeToUtf8Bytes(new
            {
                requestedProtocol = "v1.40",
                reportedVersion = backend,
                reportedApiVersion = api,
                reportedMinimumApiVersion = minimum,
                os,
                architecture,
                id
            });
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

        private async Task<Dictionary<string, string>> CachedImageReferencesAsync(CancellationToken token)
        {
            using JsonDocument document = await GetAsync("/v1.40/images/json?all=true", token).ConfigureAwait(false);
            JsonElement inventory = document.RootElement;
            if (inventory.ValueKind != JsonValueKind.Array || inventory.GetArrayLength() > 65_536) throw new InvalidDataException();
            var references = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonElement item in inventory.EnumerateArray())
            {
                string id = Text(item, "Id");
                if (!FullLocalImageId(id)) throw new InvalidDataException();
                references[id] = id;
                foreach (string field in new[] { "RepoTags", "RepoDigests" })
                {
                    if (!item.TryGetProperty(field, out JsonElement values) || values.ValueKind == JsonValueKind.Null) continue;
                    if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 1024) throw new InvalidDataException();
                    foreach (JsonElement value in values.EnumerateArray())
                    {
                        if (value.ValueKind != JsonValueKind.String || !BoundedIdentity(value.GetString()!, 2048)) throw new InvalidDataException();
                        string reference = value.GetString()!;
                        if (references.TryGetValue(reference, out string? previous) && previous != id) throw new InvalidDataException();
                        references[reference] = id;
                        if (references.Count > 131_072) throw new InvalidDataException();
                    }
                }
            }
            return references;
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
