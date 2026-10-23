using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Event.Distributed.IntegrationTests;

[Category("Harness")]
public sealed class K6LoadHarnessTests
{
    internal const string CompleteSummary = """
        {"schema":1,"mode":"open","attempts":40,"responses":40,"correct":40,"iterations":40,"httpRequests":40,"correctnessRate":1,"httpFailureRate":0,"dropped":0,"p95Milliseconds":12.5}
        """;

    [Test]
    [Arguments("https://example.org:4321/api/language")]
    [Arguments("http://localhost:4321/api/language")]
    [Arguments("http://127.0.0.1:4321/api/actor")]
    [Arguments("http://127.0.0.1:4321/api/language?target=external")]
    [Arguments("http://unadmitted@127.0.0.1:4321/api/language")]
    [Arguments("http://127.0.0.1:4321/api/language#fragment")]
    [Arguments("http://127.0.0.1:4322/api/language")]
    public async Task UnadmittedTargetsAreRejected(string target)
    {
        await Assert.That(() => K6LoadRunner.ValidateTarget(
            new Uri("http://127.0.0.1:4321/"), new Uri(target))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task OnlyTheOwnedLoopbackLookupIsAdmitted()
    {
        var owned = new Uri("http://127.0.0.1:4321/");
        var admitted = K6LoadRunner.ValidateTarget(owned, new Uri(owned, "/api/language"));
        await Assert.That(admitted.AbsolutePath).IsEqualTo("/api/language");
    }

    [Test]
    [Arguments("{}")]
    [Arguments("{\"schema\":2}")]
    [Arguments("{\"schema\":1,\"schema\":1}")]
    [Arguments("[]")]
    public async Task MissingOrUnrecognizedSummariesFailClosed(string summary)
    {
        await Assert.That(() => K6LoadRunner.ParseSummary(summary, "open"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("responses", "39")]
    [Arguments("correct", "39")]
    [Arguments("iterations", "39")]
    [Arguments("httpRequests", "41")]
    [Arguments("correctnessRate", "0.99")]
    [Arguments("httpFailureRate", "0.01")]
    [Arguments("dropped", "1")]
    [Arguments("attempts", "0")]
    [Arguments("p95Milliseconds", "-1")]
    public async Task IncompleteOrIncorrectRunsCannotProduceCapacityEvidence(string field, string value)
    {
        using var document = JsonDocument.Parse(CompleteSummary);
        var changed = document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name, property => property.Value.Clone());
        using var replacement = JsonDocument.Parse(value);
        changed[field] = replacement.RootElement.Clone();
        string summary = JsonSerializer.Serialize(changed);
        await Assert.That(() => K6LoadRunner.ParseSummary(summary, "open"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ACompleteOpenRunMustMeetItsDeclaredOfferedCount()
    {
        var summary = K6LoadRunner.ParseSummary(CompleteSummary, "open");
        await Assert.That(summary.Attempts).IsEqualTo(40L);
        await Assert.That(summary.P95Milliseconds).IsEqualTo(12.5);
        await Assert.That(() => K6LoadRunner.ParseSummary(CompleteSummary, "closed"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ACompleteClosedRunHasNoFixedOfferedCount()
    {
        var summary = K6LoadRunner.ParseSummary(CompleteSummary.Replace("\"open\"", "\"closed\"", StringComparison.Ordinal), "closed");
        await Assert.That(summary.Attempts).IsEqualTo(40L);
        await Assert.That(summary.Mode).IsEqualTo("closed");
    }

    [Test]
    public async Task AnUnexpectedSummaryFieldCannotCarryUnreviewedArtifacts()
    {
        string summary = CompleteSummary.TrimEnd().TrimEnd('}') + ",\"responseBody\":\"unreviewed\"}";
        await Assert.That(() => K6LoadRunner.ParseSummary(summary, "open"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("[]")]
    [Arguments("{\"items\":[]}")]
    [Arguments("[{\"id\":1,\"masterCode\":\"en\",\"fullName\":\"English\"},{\"id\":1,\"masterCode\":\"fr\",\"fullName\":\"French\"}]")]
    [Arguments("[{\"id\":0,\"masterCode\":\"en\",\"fullName\":\"English\"}]")]
    [Arguments("[{\"id\":1,\"masterCode\":\"\",\"fullName\":\"English\"}]")]
    public async Task AHealthyStatusCannotReplaceTheLookupContract(string body)
    {
        await Assert.That(() => K6LoadRunner.ValidateLanguageResponse(body))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task GeneratorPressureAndMissingSamplesInvalidateMeasurements()
    {
        await Assert.That(() => K6LoadRunner.ValidateGenerator(0, 0.1, 10_000_000))
            .Throws<InvalidOperationException>();
        await Assert.That(() => K6LoadRunner.ValidateGenerator(3, 0.81, 10_000_000))
            .Throws<InvalidOperationException>();
        await Assert.That(() => K6LoadRunner.ValidateGenerator(3, 0.1, 600_000_000))
            .Throws<InvalidOperationException>();
    }
}

[Category("Harness")]
[NotInParallel]
public sealed class K6LoadProcessHarnessTests
{
    [Test]
    public async Task CompletedChildDrainsBothFullPipesAndReturnsItsValidatedSummary()
    {
        using var root = new OwnedProcessDirectory();
        var result = await RunChildAsync(root, "success", TimeSpan.FromSeconds(10));
        await Assert.That(result.Summary.Attempts).IsEqualTo(40L);
        await Assert.That(result.WallMilliseconds).IsGreaterThan(0);
        await Assert.That(Directory.GetFiles(root.Path).Select(System.IO.Path.GetFileName).Order())
            .IsEquivalentTo(new[] { ".load-process-owner", "summary.json" });
    }

    [Test]
    [Arguments("nonzero")]
    [Arguments("missing")]
    [Arguments("oversized")]
    public async Task FailedChildCannotSupplyAValidMeasurement(string scenario)
    {
        using var root = new OwnedProcessDirectory();
        await Assert.That(async () => await RunChildAsync(root, scenario, TimeSpan.FromSeconds(10)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationAndDeadlineKillTheOwnedTreeAndCloseItsOutputPipes(bool useDeadline)
    {
        using var root = new OwnedProcessDirectory();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        var run = RunChildAsync(root, "tree", useDeadline ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(10), cancellation.Token);
        try
        {
            await WaitForAsync(() => File.Exists(System.IO.Path.Combine(root.Path, "descendant.ready")), TimeSpan.FromSeconds(3));
            if (!useDeadline)
                await cancellation.CancelAsync();
            await Assert.That(async () => await run).Throws<OperationCanceledException>();
            await Assert.That(IsRunning(root.Path, "parent.pid")).IsFalse();
            await Assert.That(IsRunning(root.Path, "descendant.pid")).IsFalse();
            await Assert.That(File.Exists(System.IO.Path.Combine(root.Path, "summary.json"))).IsFalse();
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await run; }
            catch (OperationCanceledException) { }
            finally
            {
                KillRecordedChild(root.Path, "descendant.pid");
                KillRecordedChild(root.Path, "parent.pid");
            }
        }
    }

    [Test]
    public async Task InheritedSecretCanaryCannotReachChildArgumentsOrRetainedDiagnostics()
    {
        using var root = new OwnedProcessDirectory();
        string? previous = Environment.GetEnvironmentVariable(K6LoadHarnessChild.CanaryName);
        string canary = Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(K6LoadHarnessChild.CanaryName, canary);
        try
        {
            var startInfo = K6LoadHarnessChild.ChildStartInfo("secrets", root.Path);
            await Assert.That(startInfo.Environment.ContainsKey(K6LoadHarnessChild.CanaryName)).IsFalse();
            await Assert.That(string.Join(' ', startInfo.ArgumentList)).DoesNotContain(canary);
            var measurement = await K6LoadRunner.RunOwnedProcessAsync(startInfo,
                System.IO.Path.Combine(root.Path, "summary.json"), "open", TimeSpan.FromSeconds(10), TestContext.Current!.Execution.CancellationToken);
            await Assert.That(JsonSerializer.Serialize(measurement)).DoesNotContain(canary);
            foreach (string file in Directory.GetFiles(root.Path))
                await Assert.That(await File.ReadAllTextAsync(file)).DoesNotContain(canary);
        }
        finally
        {
            Environment.SetEnvironmentVariable(K6LoadHarnessChild.CanaryName, previous);
        }
    }

    private static Task<K6ProcessMeasurement> RunChildAsync(OwnedProcessDirectory root, string scenario, TimeSpan timeout,
        CancellationToken cancellationToken = default) => K6LoadRunner.RunOwnedProcessAsync(
            K6LoadHarnessChild.ChildStartInfo(scenario, root.Path), System.IO.Path.Combine(root.Path, "summary.json"), "open", timeout,
            cancellationToken == default ? TestContext.Current!.Execution.CancellationToken : cancellationToken);

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(timeout);
        while (!condition())
            await Task.Delay(20, deadline.Token);
    }

    private static bool IsRunning(string root, string name)
    {
        string file = System.IO.Path.Combine(root, name);
        if (!File.Exists(file))
            return false;
        int id = int.Parse(File.ReadAllText(file), CultureInfo.InvariantCulture);
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }

    private static void KillRecordedChild(string root, string name)
    {
        if (!IsRunning(root, name))
            return;
        int id = int.Parse(File.ReadAllText(System.IO.Path.Combine(root, name)), CultureInfo.InvariantCulture);
        using var process = Process.GetProcessById(id);
        process.Kill(entireProcessTree: true);
        process.WaitForExit(5000);
    }

    private sealed class OwnedProcessDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "islamu-load-process-" + Guid.NewGuid().ToString("N"));

        internal OwnedProcessDirectory()
        {
            Directory.CreateDirectory(Path);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(System.IO.Path.Combine(Path, ".load-process-owner"), System.IO.Path.GetFileName(Path)["islamu-load-process-".Length..]);
        }

        public void Dispose()
        {
            if (!K6LoadHarnessChild.IsOwnedRoot(Path))
                throw new InvalidOperationException("load_child_cleanup_owner_invalid");
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal static class K6LoadHarnessChild
{
    internal const string CanaryName = "ISLAMU_LOAD_HARNESS_SECRET_CANARY";
    private const string ChildFlag = "--assurance-process-child";
    private static readonly string[] Scenarios = ["success", "nonzero", "missing", "oversized", "tree", "descendant", "secrets"];

    [ModuleInitializer]
    internal static void DispatchChild()
    {
        // A strict test-only child mode preserves TUnit's generated Main and leaves ordinary discovery inert.
        string[] arguments = Environment.GetCommandLineArgs();
        if (arguments.Length < 2 || arguments[1] != ChildFlag)
            return;
        int exitCode = 64;
        try
        {
            if (arguments.Length == 4 && Scenarios.Contains(arguments[2], StringComparer.Ordinal) && IsOwnedRoot(arguments[3]))
                exitCode = RunChildAsync(arguments[2], arguments[3]).GetAwaiter().GetResult();
        }
        catch (Exception) { exitCode = 70; }
        Environment.Exit(exitCode);
    }

    internal static bool IsOwnedRoot(string root)
    {
        if (!System.IO.Path.IsPathFullyQualified(root))
            return false;
        string full = System.IO.Path.GetFullPath(root);
        string name = System.IO.Path.GetFileName(full);
        const string prefix = "islamu-load-process-";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..], "N", out _) ||
            System.IO.Path.GetDirectoryName(full) != System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar) ||
            new DirectoryInfo(full).LinkTarget is not null)
            return false;
        string marker = System.IO.Path.Combine(full, ".load-process-owner");
        return File.Exists(marker) && new FileInfo(marker).LinkTarget is null && File.ReadAllText(marker) == name[prefix.Length..];
    }

    internal static ProcessStartInfo ChildStartInfo(string scenario, string root)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("load_child_process_path_missing");
        var info = K6LoadRunner.StartInfo(executable);
        if (System.IO.Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(K6LoadHarnessChild).Assembly.Location);
        foreach (string argument in new[] { ChildFlag, scenario, root })
            info.ArgumentList.Add(argument);
        return info;
    }

    private static async Task<int> RunChildAsync(string scenario, string root)
    {
        string summary = System.IO.Path.Combine(root, "summary.json");
        if (scenario == "secrets" && Environment.GetEnvironmentVariable(CanaryName) is not null)
            return 73;
        if (scenario is "tree" or "descendant")
        {
            string pidName = scenario == "tree" ? "parent.pid" : "descendant.pid";
            await File.WriteAllTextAsync(System.IO.Path.Combine(root, pidName), Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            if (scenario == "tree")
            {
                var descendantStart = ChildStartInfo("descendant", root);
                descendantStart.RedirectStandardOutput = false;
                descendantStart.RedirectStandardError = false;
                using var descendant = Process.Start(descendantStart)
                    ?? throw new InvalidOperationException("load_child_descendant_start_failed");
                await descendant.WaitForExitAsync();
            }
            else
            {
                await File.WriteAllTextAsync(System.IO.Path.Combine(root, "descendant.ready"), "ready");
                await Task.Delay(Timeout.InfiniteTimeSpan);
            }
            return 74;
        }
        byte[] noise = new byte[65536];
        Array.Fill(noise, (byte)'x');
        for (int block = 0; block < 16; block++)
        {
            await Console.OpenStandardOutput().WriteAsync(noise);
            await Console.OpenStandardError().WriteAsync(noise);
        }
        if (scenario != "missing")
            await File.WriteAllTextAsync(summary, scenario == "oversized" ? new string('x', 4097) : K6LoadHarnessTests.CompleteSummary);
        return scenario == "nonzero" ? 71 : 0;
    }
}

[Category("Load")]
[NotInParallel]
public sealed class K6LoadTests
{
    [Test]
    [Arguments("Split")]
    [Arguments("Standalone")]
    public async Task OwnedPublicLookupRemainsCorrectUnderClosedAndOpenLoad(string topology)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        await using var fixture = await OwnedAppHostFixture.CreateAsync(topology, deadline.Token);
        await fixture.StartAsync(deadline.Token);
        await K6LoadRunner.RunAsync(fixture, topology, deadline.Token);
    }
}
