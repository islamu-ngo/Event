using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Event.Distributed.IntegrationTests;

internal static class K6LoadRunner
{
    internal const int DurationSeconds = 5;
    internal const int VirtualUsers = 4;
    internal const int OfferedRate = 8;
    private const string AdmittedExecutableDigest = "E001BBF39BA67424A85C9C8516440BA5C73ECF7A6021BE96C76194380C15F260";
    private const string WorkloadId = "owned-language-read-v1";
    private const long MemoryBudgetBytes = 512 * 1024 * 1024;
    private static readonly JsonSerializerOptions EvidenceJsonOptions = new() { WriteIndented = true };
    private static readonly string[] SummaryFields =
    [
        "schema", "mode", "attempts", "responses", "correct", "iterations", "httpRequests",
        "correctnessRate", "httpFailureRate", "dropped", "p95Milliseconds"
    ];

    internal static async Task RunAsync(OwnedAppHostFixture fixture, string topology, CancellationToken cancellationToken)
    {
        if (topology is not ("Split" or "Standalone"))
            throw new InvalidOperationException("load_topology_not_admitted");
        string executable = Environment.GetEnvironmentVariable("ISLAMU_K6_PATH") ?? "";
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new InvalidOperationException("load_k6_absolute_executable_required");
        var versionStartInfo = StartInfo(executable);
        versionStartInfo.ArgumentList.Add("version");
        string version = await ReadVersionAsync(versionStartInfo, cancellationToken);
        string script = Path.Combine(AppContext.BaseDirectory, "Workloads", "owned-runtime.js");
        if (!File.Exists(script))
            throw new InvalidOperationException("load_reviewed_workload_missing");
        string workloadDigest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(script, cancellationToken)));
        string executableDigest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable, cancellationToken)));
        using var client = fixture.CreateHttpClient(topology == "Split" ? "explore-api" : "event-standalone");
        Uri origin = client.BaseAddress ?? throw new InvalidOperationException("load_owned_endpoint_missing");
        // Literal loopback addresses avoid DNS rebinding and make the admitted origin immutable.
        var target = new UriBuilder(origin) { Host = IPAddress.Loopback.ToString(), Path = "/api/language" }.Uri;
        var ownedOrigin = new UriBuilder(origin) { Host = IPAddress.Loopback.ToString(), Path = "/" }.Uri;
        ValidateTarget(ownedOrigin, target);
        using var transport = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var warmupClient = new HttpClient(transport) { Timeout = TimeSpan.FromSeconds(5) };
        var warmup = new List<double>();
        string? expectedDigest = null;
        for (int index = 0; index < 3; index++)
        {
            var timer = Stopwatch.StartNew();
            using var response = await warmupClient.GetAsync(target, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK ||
                response.Content.Headers.ContentType?.MediaType != "application/json")
                throw new InvalidOperationException("load_lookup_preflight_failed");
            ValidateLanguageResponse(body);
            string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            if (expectedDigest is not null && expectedDigest != digest)
                throw new InvalidOperationException("load_lookup_preflight_unstable");
            expectedDigest = digest;
            warmup.Add(timer.Elapsed.TotalMilliseconds);
        }

        string ownedDirectory = Path.Combine(fixture.Root, "k6");
        Directory.CreateDirectory(ownedDirectory);
        string configurationPath = Path.Combine(ownedDirectory, "config.json");
        await File.WriteAllTextAsync(configurationPath, "{}", cancellationToken);
        foreach (string mode in new[] { "closed", "open" })
        {
            string summaryPath = Path.Combine(ownedDirectory, $"{mode}.json");
            var startInfo = StartInfo(executable);
            foreach (string argument in new[] { "run", "--config", configurationPath, "--quiet", "--no-color", "--no-usage-report", "--include-system-env-vars=true", script })
                startInfo.ArgumentList.Add(argument);
            startInfo.WorkingDirectory = ownedDirectory;
            startInfo.Environment["LOAD_ORIGIN"] = ownedOrigin.AbsoluteUri.TrimEnd('/');
            startInfo.Environment["LOAD_TARGET"] = target.AbsoluteUri;
            startInfo.Environment["LOAD_DIGEST"] = expectedDigest!;
            startInfo.Environment["LOAD_MODE"] = mode;
            startInfo.Environment["LOAD_SUMMARY"] = summaryPath;
            var measurement = await RunOwnedProcessAsync(startInfo, summaryPath, mode, TimeSpan.FromSeconds(30), cancellationToken);
            ValidateGenerator(measurement.GeneratorSamples, measurement.GeneratorPeakCpuRatio, measurement.GeneratorPeakRssBytes);
            string evidenceDirectory = Path.Combine(Path.GetTempPath(), "testing-assurance-modernization", "continuation", "load", fixture.RunId);
            Directory.CreateDirectory(evidenceDirectory);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(evidenceDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var evidence = new
            {
                Schema = 1, Candidate = true, WorkloadId, WorkloadDigest = workloadDigest,
                ToolVersion = version, ExecutableDigest = executableDigest, Topology = topology,
                Provider = "PostgreSql", Mode = mode, DurationSeconds,
                VirtualUsers = mode == "closed" ? VirtualUsers : (int?)null,
                OfferedRate = mode == "open" ? OfferedRate : (int?)null,
                MaximumVirtualUsers = VirtualUsers,
                WarmupMilliseconds = warmup, measurement.Summary,
                measurement.WallMilliseconds, measurement.GeneratorSamples,
                measurement.GeneratorPeakCpuRatio, measurement.GeneratorPeakRssBytes,
                GeneratorCpuBudgetRatio = 0.8, GeneratorMemoryBudgetBytes = MemoryBudgetBytes,
                LogicalProcessors = Environment.ProcessorCount, TotalAvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                BuildInformationalVersion = typeof(K6LoadRunner).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                RecordedAtUtc = DateTimeOffset.UtcNow,
                CacheProfile = "three stable warmup requests followed by cached public language lookup reads",
                Scope = "public cached lookup reads; no authenticated writes, settlement, SLO or capacity claim"
            };
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, $"load-{topology}-{mode}.json"),
                JsonSerializer.Serialize(evidence, EvidenceJsonOptions), cancellationToken);
        }
    }

    internal static async Task<K6ProcessMeasurement> RunOwnedProcessAsync(ProcessStartInfo startInfo,
        string summaryPath, string mode, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var wall = Stopwatch.StartNew();
        process.Start();
        Task output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        Task error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        int samples = 0;
        long peakRss = 0;
        double peakCpuRatio = 0;
        double previousSeconds = 0;
        TimeSpan previousCpu = TimeSpan.Zero;
        try
        {
            while (!process.HasExited)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);
                process.Refresh();
                if (process.HasExited)
                    break;
                double seconds = wall.Elapsed.TotalSeconds;
                TimeSpan cpu = process.TotalProcessorTime;
                peakCpuRatio = Math.Max(peakCpuRatio,
                    (cpu - previousCpu).TotalSeconds / (seconds - previousSeconds) / Environment.ProcessorCount);
                peakRss = Math.Max(peakRss, process.WorkingSet64);
                samples++;
                previousCpu = cpu;
                previousSeconds = seconds;
            }
            await process.WaitForExitAsync(deadline.Token);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        wall.Stop();
        if (process.ExitCode != 0 || !File.Exists(summaryPath) || new FileInfo(summaryPath).Length > 4096)
            throw new InvalidOperationException("load_process_or_summary_failed");
        var summary = ParseSummary(await File.ReadAllTextAsync(summaryPath, cancellationToken), mode);
        return new K6ProcessMeasurement(summary, wall.Elapsed.TotalMilliseconds, samples, peakCpuRatio, peakRss);
    }

    internal static Uri ValidateTarget(Uri ownedOrigin, Uri target)
    {
        if (ownedOrigin.Scheme != "http" || ownedOrigin.Host != "127.0.0.1" || ownedOrigin.IsDefaultPort ||
            target.Scheme != ownedOrigin.Scheme || target.Host != ownedOrigin.Host || target.Port != ownedOrigin.Port ||
            target.AbsolutePath != "/api/language" || target.Query.Length != 0 || target.Fragment.Length != 0 || target.UserInfo.Length != 0)
            throw new InvalidOperationException("load_target_not_admitted");
        return target;
    }

    internal static void ValidateLanguageResponse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
                throw new InvalidOperationException("load_lookup_contract_invalid");
            var ids = new HashSet<int>();
            foreach (var row in document.RootElement.EnumerateArray())
            {
                int id = row.GetProperty("id").GetInt32();
                if (id <= 0 || !ids.Add(id) || string.IsNullOrWhiteSpace(row.GetProperty("masterCode").GetString()) ||
                    string.IsNullOrWhiteSpace(row.GetProperty("fullName").GetString()))
                    throw new InvalidOperationException("load_lookup_contract_invalid");
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException("load_lookup_contract_invalid");
        }
    }

    internal static K6Summary ParseSummary(string json, string expectedMode)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("load_summary_invalid");
            string[] names = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Length != SummaryFields.Length || !names.Order().SequenceEqual(SummaryFields.Order()) ||
                root.GetProperty("schema").GetInt32() != 1 || root.GetProperty("mode").GetString() != expectedMode ||
                expectedMode is not ("closed" or "open"))
                throw new InvalidOperationException("load_summary_invalid");
            long attempts = root.GetProperty("attempts").GetInt64();
            double p95 = root.GetProperty("p95Milliseconds").GetDouble();
            if (attempts <= 0 || attempts > 1000 ||
                (expectedMode == "open" && attempts != OfferedRate * DurationSeconds) ||
                new[] { "responses", "correct", "iterations", "httpRequests" }.Any(name => root.GetProperty(name).GetInt64() != attempts) ||
                root.GetProperty("correctnessRate").GetDouble() != 1 || root.GetProperty("httpFailureRate").GetDouble() != 0 ||
                root.GetProperty("dropped").GetInt64() != 0 || !double.IsFinite(p95) || p95 < 0)
                throw new InvalidOperationException("load_measurement_invalid");
            return new K6Summary(expectedMode, attempts, attempts, attempts, attempts, attempts, 1, 0, 0, p95);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new InvalidOperationException("load_summary_invalid");
        }
    }

    internal static void ValidateGenerator(int samples, double peakCpuRatio, long peakRss)
    {
        if (samples == 0 || !double.IsFinite(peakCpuRatio) || peakCpuRatio < 0 || peakCpuRatio > 0.8 || peakRss <= 0 || peakRss > MemoryBudgetBytes)
            throw new InvalidOperationException("load_generator_pressure_invalidates_measurement");
    }

    internal static ProcessStartInfo StartInfo(string executable)
    {
        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        info.Environment.Clear();
        info.Environment["K6_NO_USAGE_REPORT"] = "true";
        info.Environment["NO_PROXY"] = "*";
        info.Environment[OperatingSystem.IsWindows() ? "TEMP" : "TMPDIR"] = Path.GetTempPath();
        return info;
    }

    internal static async Task<string> ReadVersionAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(startInfo.FileName) || !File.Exists(startInfo.FileName) ||
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(startInfo.FileName, cancellationToken))) != AdmittedExecutableDigest)
            throw new InvalidOperationException("load_k6_digest_not_admitted");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            string version = await output;
            if (process.ExitCode != 0 || !version.StartsWith("k6 v2.3.0 ", StringComparison.Ordinal))
                throw new InvalidOperationException("load_k6_version_not_admitted");
            return "2.3.0";
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            await error.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
    }
}

internal sealed record K6Summary(string Mode, long Attempts, long Responses, long Correct,
    long Iterations, long HttpRequests, double CorrectnessRate, double HttpFailureRate,
    long DroppedIterations, double P95Milliseconds);

internal sealed record K6ProcessMeasurement(K6Summary Summary, double WallMilliseconds,
    int GeneratorSamples, double GeneratorPeakCpuRatio, long GeneratorPeakRssBytes);
