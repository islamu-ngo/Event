using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Event.Distributed.IntegrationTests;

[Category("Harness")]
public sealed class K6LoadHarnessTests
{
    internal const string CompleteSummary = """
        {"schema":2,"journey":"public-language","profile":"open-lookup","mode":"open","attempts":40,"responses":40,"correct":40,"iterations":40,"httpRequests":40,"correctnessRate":1,"httpFailureRate":0,"dropped":0,"p50Milliseconds":6,"p95Milliseconds":12.5,"p99Milliseconds":20}
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
    [Arguments("{\"schema\":3}")]
    [Arguments("{\"schema\":2,\"schema\":2}")]
    [Arguments("[]")]
    public async Task MissingOrUnrecognizedSummariesFailClosed(string summary)
    {
        await Assert.That(() => K6LoadRunner.ParseSummary(summary, K6LoadProfile.OpenLookup))
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
    [Arguments("p50Milliseconds", "99")]
    [Arguments("p99Milliseconds", "1")]
    [Arguments("p50Milliseconds", "-1")]
    [Arguments("p99Milliseconds", "-1")]
    [Arguments("profile", "\"steady\"")]
    public async Task IncompleteOrIncorrectRunsCannotProduceCapacityEvidence(string field, string value)
    {
        using var document = JsonDocument.Parse(CompleteSummary);
        var changed = document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name, property => property.Value.Clone());
        using var replacement = JsonDocument.Parse(value);
        changed[field] = replacement.RootElement.Clone();
        string summary = JsonSerializer.Serialize(changed);
        await Assert.That(() => K6LoadRunner.ParseSummary(summary, K6LoadProfile.OpenLookup))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ACompleteOpenRunMustMeetItsDeclaredOfferedCount()
    {
        var summary = K6LoadRunner.ParseSummary(CompleteSummary, K6LoadProfile.OpenLookup);
        await Assert.That(summary.Attempts).IsEqualTo(40L);
        await Assert.That(summary.P95Milliseconds).IsEqualTo(12.5);
        await Assert.That(() => K6LoadRunner.ParseSummary(CompleteSummary, K6LoadProfile.ClosedLookup))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ACompleteClosedRunHasNoFixedOfferedCount()
    {
        string json = CompleteSummary.Replace("\"open\"", "\"closed\"", StringComparison.Ordinal)
            .Replace("\"open-lookup\"", "\"closed-lookup\"", StringComparison.Ordinal);
        var summary = K6LoadRunner.ParseSummary(json, K6LoadProfile.ClosedLookup);
        await Assert.That(summary.Attempts).IsEqualTo(40L);
        await Assert.That(summary.Mode).IsEqualTo("closed");
    }

    [Test]
    public async Task AnUnexpectedSummaryFieldCannotCarryUnreviewedArtifacts()
    {
        string summary = CompleteSummary.TrimEnd().TrimEnd('}') + ",\"responseBody\":\"unreviewed\"}";
        await Assert.That(() => K6LoadRunner.ParseSummary(summary, K6LoadProfile.OpenLookup))
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
public sealed class K6LoadProfileHarnessTests
{
    [Test]
    public async Task RejectedDroppedWorkRemainsVisibleWithoutBecomingAccepted()
    {
        var root = Directory.CreateTempSubdirectory("islamu-load-rejected-");
        try
        {
            const string summary = """
                {"schema":2,"journey":"public-language","profile":"open-lookup","mode":"open","attempts":38,"responses":38,"correct":38,"iterations":38,"httpRequests":38,"correctnessRate":1,"httpFailureRate":0,"dropped":2,"p50Milliseconds":6,"p95Milliseconds":12.5,"p99Milliseconds":20}
                """;
            string input = System.IO.Path.Combine(root.FullName, "summary.json");
            string output = System.IO.Path.Combine(root.FullName, "rejected.json");
            await File.WriteAllTextAsync(input, summary);
            await K6LoadRunner.RetainRejectedSummaryAsync(input, output, K6LoadProfile.OpenLookup, K6Journey.LanguageLookup,
                TestContext.Current!.Execution.CancellationToken);
            using var retained = JsonDocument.Parse(await File.ReadAllTextAsync(output));
            await Assert.That(retained.RootElement.GetProperty("Admission").GetString()).IsEqualTo("Rejected");
            await Assert.That(retained.RootElement.GetProperty("CapacityEvidenceEligible").GetBoolean()).IsFalse();
            await Assert.That(retained.RootElement.GetProperty("Summary").GetProperty("DroppedIterations").GetInt64()).IsEqualTo(2L);
            await Assert.That(retained.RootElement.GetProperty("Summary").GetProperty("Attempts").GetInt64()).IsEqualTo(38L);
            await Assert.That(() => K6LoadRunner.ParseSummary(summary, K6LoadProfile.OpenLookup)).Throws<InvalidOperationException>();
        }
        finally { root.Delete(recursive: true); }
    }

    [Test]
    public async Task RejectedSummaryCannotRetainAnUnreviewedStringField()
    {
        var root = Directory.CreateTempSubdirectory("islamu-load-rejected-");
        try
        {
            string canary = Guid.NewGuid().ToString("N");
            string summary = K6LoadHarnessTests.CompleteSummary.TrimEnd().TrimEnd('}') + ",\"responseBody\":\"" + canary + "\"}";
            string input = System.IO.Path.Combine(root.FullName, "summary.json");
            string output = System.IO.Path.Combine(root.FullName, "rejected.json");
            await File.WriteAllTextAsync(input, summary);
            await K6LoadRunner.RetainRejectedSummaryAsync(input, output, K6LoadProfile.OpenLookup, K6Journey.LanguageLookup,
                TestContext.Current!.Execution.CancellationToken);
            string retained = await File.ReadAllTextAsync(output);
            await Assert.That(retained).DoesNotContain(canary);
            await Assert.That(retained).DoesNotContain("responseBody");
            using var artifact = JsonDocument.Parse(retained);
            await Assert.That(artifact.RootElement.GetProperty("SummaryStatus").GetString()).IsEqualTo("Invalid");
            await Assert.That(artifact.RootElement.GetProperty("Summary").ValueKind).IsEqualTo(JsonValueKind.Null);
        }
        finally { root.Delete(recursive: true); }
    }

    private const string CompleteWriteSummary = """
        {"schema":2,"journey":"event-write","profile":"constrained-write","mode":"open","attempts":20,"responses":60,"correct":20,"iterations":20,"httpRequests":60,"correctnessRate":1,"httpFailureRate":0,"dropped":0,"p50Milliseconds":6,"p95Milliseconds":12.5,"p99Milliseconds":20}
        """;

    [Test]
    public async Task SerialWriteEvidenceNeedsTwentyCompleteThreeRequestJourneys()
    {
        var summary = K6LoadRunner.ParseSummary(CompleteWriteSummary, K6LoadProfile.ConstrainedWrite, K6Journey.EventWrite);
        await Assert.That(summary.Attempts).IsEqualTo(20L);
        await Assert.That(summary.HttpRequests).IsEqualTo(60L);
        await Assert.That(() => K6LoadRunner.ParseSummary(CompleteWriteSummary, K6LoadProfile.OpenLookup))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("\"responses\":60", "\"responses\":59")]
    [Arguments("\"httpRequests\":60", "\"httpRequests\":59")]
    [Arguments("\"iterations\":20", "\"iterations\":19")]
    [Arguments("\"attempts\":20", "\"attempts\":21")]
    public async Task PartialOrExcessWriteWorkCannotBeReportedAsComplete(string original, string invalid)
    {
        await Assert.That(() => K6LoadRunner.ParseSummary(CompleteWriteSummary.Replace(original, invalid, StringComparison.Ordinal),
            K6LoadProfile.ConstrainedWrite, K6Journey.EventWrite)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(1, "/api/Event/01999999-9999-7999-8999-999999999991")]
    [Arguments(2, "/api/Event?pageSize=20&searchTerm=owned-search")]
    [Arguments(3, "/api/Event/01999999-9999-7999-8999-999999999992")]
    public async Task OnlyTheIndependentlyVerifiedEventJourneyCanSelectItsTarget(int journey, string path)
    {
        var origin = new Uri("http://127.0.0.1:4321/");
        var target = new Uri(origin, path);
        await Assert.That(K6LoadRunner.ValidateEventTarget(origin, target,
            Guid.Parse("01999999-9999-7999-8999-999999999991"), Guid.Parse("01999999-9999-7999-8999-999999999992"),
            "owned-search", (K6Journey)journey)).IsEqualTo(target);
    }

    [Test]
    [Arguments("http://127.0.0.1:4322/api/Event/01999999-9999-7999-8999-999999999991", 1)]
    [Arguments("http://127.0.0.1:4321/api/Event/01999999-9999-7999-8999-999999999992", 1)]
    [Arguments("http://127.0.0.1:4321/api/Event?pageSize=20&searchTerm=other", 2)]
    [Arguments("http://127.0.0.1:4321/api/Event/01999999-9999-7999-8999-999999999991", 3)]
    public async Task AForeignOriginOrDifferentEventJourneyCannotReceiveLoad(string target, int journey)
    {
        await Assert.That(() => K6LoadRunner.ValidateEventTarget(new Uri("http://127.0.0.1:4321/"), new Uri(target),
            Guid.Parse("01999999-9999-7999-8999-999999999991"), Guid.Parse("01999999-9999-7999-8999-999999999992"),
            "owned-search", (K6Journey)journey)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AWriteJourneyCannotBorrowAReadBudgetOrReadTarget()
    {
        K6LoadRunner.ValidateJourneyBudget(K6Journey.EventWrite, K6LoadProfile.ConstrainedWrite);
        await Assert.That(() => K6LoadRunner.ValidateJourneyBudget(K6Journey.EventWrite, K6LoadProfile.Steady))
            .Throws<InvalidOperationException>();
        await Assert.That(() => K6LoadRunner.ValidateJourneyBudget(K6Journey.EventRead, K6LoadProfile.ConstrainedWrite))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(K6LoadProfile.LowRate, 10, 20)]
    [Arguments(K6LoadProfile.Steady, 120, 40)]
    [Arguments(K6LoadProfile.Spike, 40, 15)]
    [Arguments(K6LoadProfile.Recovery, 10, 20)]
    [Arguments(K6LoadProfile.Soak, 240, 130)]
    [Arguments(K6LoadProfile.ConstrainedWrite, 20, 30)]
    public async Task FixedProfilesHaveBoundedOfferedCountsAndDeadlines(K6LoadProfile profile, int attempts, int deadlineSeconds)
    {
        var budget = K6LoadRunner.BudgetFor(profile);
        await Assert.That(budget.ExpectedAttempts).IsEqualTo(attempts);
        await Assert.That(budget.ProcessDeadline.TotalSeconds).IsEqualTo((double)deadlineSeconds);
        await Assert.That(budget.MaximumVirtualUsers).IsLessThanOrEqualTo(4);
        await Assert.That(budget.OfferedRate).IsLessThanOrEqualTo(8);
    }

    [Test]
    public async Task UnknownProfilesCannotSelectAnUnboundedWorkload()
    {
        await Assert.That(() => K6LoadRunner.BudgetFor((K6LoadProfile)int.MaxValue)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ConstrainedWritesAreSerialAndCannotExceedTwentyAttempts()
    {
        var budget = K6LoadRunner.BudgetFor(K6LoadProfile.ConstrainedWrite);
        await Assert.That(budget.MaximumVirtualUsers).IsEqualTo(1);
        await Assert.That(budget.OfferedRate).IsEqualTo(1);
        await Assert.That(budget.ExpectedAttempts).IsEqualTo(20);
    }

    [Test]
    public async Task AllObservedPercentilesAreRetained()
    {
        var summary = K6LoadRunner.ParseSummary(K6LoadHarnessTests.CompleteSummary, K6LoadProfile.OpenLookup);
        await Assert.That(summary.P50Milliseconds).IsEqualTo(6d);
        await Assert.That(summary.P95Milliseconds).IsEqualTo(12.5);
        await Assert.That(summary.P99Milliseconds).IsEqualTo(20d);
    }

    [Test]
    public async Task RetainedRuntimeMetricMetadataExcludesLabelsHelpAndUnpinnedValues()
    {
        var root = Directory.CreateTempSubdirectory("islamu-load-metric-");
        try
        {
            string canary = Guid.NewGuid().ToString("N");
            string exposition = $$"""
                # HELP dotnet_process_cpu_time_seconds_total {{canary}}
                # TYPE dotnet_process_cpu_time_seconds_total counter
                # UNIT dotnet_process_cpu_time_seconds_total seconds
                dotnet_process_cpu_time_seconds_total{cpu_mode="user",customer="{{canary}}"} 123456789
                # TYPE dotnet_process_memory_working_set_bytes gauge
                dotnet_process_memory_working_set_bytes{customer="{{canary}}"} 987654321
                # TYPE private_{{canary}} gauge
                # TYPE dotnet_private_{{canary}} gauge
                """;
            var definitions = K6LoadRunner.ParseRuntimeMetricInventory(exposition);
            string path = System.IO.Path.Combine(root.FullName, "metrics.json");
            await K6LoadRunner.WriteRuntimeMetricInventoryAsync(path, K6MetricInventoryStatus.MetadataOnly,
                definitions, TestContext.Current!.Execution.CancellationToken);
            string retained = await File.ReadAllTextAsync(path);
            await Assert.That(retained).DoesNotContain(canary);
            await Assert.That(retained).DoesNotContain("123456789");
            await Assert.That(retained).DoesNotContain("987654321");
            using var artifact = JsonDocument.Parse(retained);
            await Assert.That(artifact.RootElement.GetProperty("CapacityEvidenceEligible").GetBoolean()).IsFalse();
            var metrics = artifact.RootElement.GetProperty("MetricDefinitions");
            await Assert.That(metrics.GetArrayLength()).IsEqualTo(2);
            await Assert.That(metrics[0].GetProperty("Unit").GetString()).IsEqualTo("seconds");
            await Assert.That(metrics[1].GetProperty("Unit").ValueKind).IsEqualTo(JsonValueKind.Null);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    [Arguments("# TYPE dotnet_process_cpu_time_seconds_total counter\n# TYPE dotnet_process_cpu_time_seconds_total gauge")]
    [Arguments("# UNIT dotnet_process_cpu_time_seconds_total seconds\n# UNIT dotnet_process_cpu_time_seconds_total bytes")]
    public async Task AmbiguousMetricMetadataCannotSupplyAnObservation(string exposition)
    {
        await Assert.That(() => K6LoadRunner.ParseRuntimeMetricInventory(exposition)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MetricMetadataInventoryIsBounded()
    {
        string exposition = new('x', 4 * 1024 * 1024 + 1);
        await Assert.That(() => K6LoadRunner.ParseRuntimeMetricInventory(exposition)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UnadmittedMetricUnitsCannotEnterRetainedMetadata()
    {
        var root = Directory.CreateTempSubdirectory("islamu-load-metric-");
        try
        {
            string canary = Guid.NewGuid().ToString("N");
            var definitions = K6LoadRunner.ParseRuntimeMetricInventory(
                "# TYPE dotnet_process_cpu_time_seconds_total counter\n# UNIT dotnet_process_cpu_time_seconds_total " + canary);
            string path = System.IO.Path.Combine(root.FullName, "metrics.json");
            await K6LoadRunner.WriteRuntimeMetricInventoryAsync(path, K6MetricInventoryStatus.MetadataOnly,
                definitions, TestContext.Current!.Execution.CancellationToken);
            string retained = await File.ReadAllTextAsync(path);
            await Assert.That(retained).DoesNotContain(canary);
            using var artifact = JsonDocument.Parse(retained);
            await Assert.That(artifact.RootElement.GetProperty("MetricDefinitions")[0].GetProperty("Unit").ValueKind)
                .IsEqualTo(JsonValueKind.Null);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}

[Category("Harness")]
[NotInParallel]
public sealed class K6LoadProcessHarnessTests
{
    [Test]
    public async Task AnUnboundedDeadlineCannotLaunchTheOwnedChild()
    {
        using var root = new OwnedProcessDirectory();
        var failure = await Assert.That(async () => await K6LoadRunner.RunOwnedProcessAsync(
            K6LoadHarnessChild.ChildStartInfo("version", root.Path), System.IO.Path.Combine(root.Path, "summary.json"),
            K6LoadProfile.OpenLookup, TimeSpan.FromSeconds(16), TestContext.Current!.Execution.CancellationToken))
            .Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("load_deadline_not_admitted");
        await Assert.That(File.Exists(System.IO.Path.Combine(root.Path, "version-launched"))).IsFalse();
    }

    [Test]
    public async Task AVersionCompatibleUnapprovedExecutableIsRejectedBeforeItStarts()
    {
        using var root = new OwnedProcessDirectory();
        string sentinel = System.IO.Path.Combine(root.Path, "version-launched");
        using (var canary = Process.Start(K6LoadHarnessChild.ChildStartInfo("version", root.Path))
            ?? throw new InvalidOperationException("load_version_canary_start_failed"))
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var output = canary.StandardOutput.ReadToEndAsync(deadline.Token);
                await canary.WaitForExitAsync(deadline.Token);
                await Assert.That(canary.ExitCode).IsEqualTo(0);
                await Assert.That(await output).StartsWith("k6 v2.3.0 ");
            }
            finally
            {
                if (!canary.HasExited)
                    canary.Kill(entireProcessTree: true);
                await canary.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        await Assert.That(File.Exists(sentinel)).IsTrue();
        File.Delete(sentinel);

        var rejection = await Assert.That(async () => await K6LoadRunner.ReadVersionAsync(
            K6LoadHarnessChild.ChildStartInfo("version", root.Path), TestContext.Current!.Execution.CancellationToken))
            .Throws<InvalidOperationException>();
        await Assert.That(rejection!.Message).IsEqualTo("load_k6_digest_not_admitted");
        await Assert.That(File.Exists(sentinel)).IsFalse();
    }

    [Test]
    public async Task CompletedChildDrainsBothFullPipesAndReturnsItsValidatedSummary()
    {
        using var root = new OwnedProcessDirectory();
        var result = await RunChildAsync(root, "success", TimeSpan.FromSeconds(10));
        await Assert.That(result.Summary.Attempts).IsEqualTo(40L);
        await Assert.That(result.WallMilliseconds).IsGreaterThan(0);
        await Assert.That(Directory.GetFiles(root.Path).Select(path => System.IO.Path.GetFileName(path)!).Order())
            .IsEquivalentTo(new[] { ".load-process-owner", "summary.json" });
    }

    [Test]
    [Arguments("nonzero", "Available")]
    [Arguments("missing", "Missing")]
    [Arguments("oversized", "Oversized")]
    public async Task FailedChildCannotSupplyAValidMeasurement(string scenario, string expectedSummaryStatus)
    {
        using var root = new OwnedProcessDirectory();
        string evidence = System.IO.Path.Combine(root.Path, "rejected.json");
        await Assert.That(async () => await K6LoadRunner.RunWithRejectedEvidenceAsync(
            K6LoadHarnessChild.ChildStartInfo(scenario, root.Path), System.IO.Path.Combine(root.Path, "summary.json"), evidence,
            K6LoadProfile.OpenLookup, K6Journey.LanguageLookup, TimeSpan.FromSeconds(10), TestContext.Current!.Execution.CancellationToken))
            .Throws<InvalidOperationException>();
        using var retained = JsonDocument.Parse(await File.ReadAllTextAsync(evidence));
        await Assert.That(retained.RootElement.GetProperty("Admission").GetString()).IsEqualTo("Rejected");
        await Assert.That(retained.RootElement.GetProperty("CapacityEvidenceEligible").GetBoolean()).IsFalse();
        await Assert.That(retained.RootElement.GetProperty("SummaryStatus").GetString()).IsEqualTo(expectedSummaryStatus);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationAndDeadlineKillTheOwnedTreeAndCloseItsOutputPipes(bool useDeadline)
    {
        using var root = new OwnedProcessDirectory();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        var elapsed = Stopwatch.StartNew();
        var run = RunChildAsync(root, "tree", useDeadline ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(10), cancellation.Token);
        try
        {
            await WaitForAsync(() => File.Exists(System.IO.Path.Combine(root.Path, "descendant.ready")), TimeSpan.FromSeconds(3));
            if (!useDeadline)
                await cancellation.CancelAsync();
            await Assert.That(async () => await run).Throws<OperationCanceledException>();
            await Assert.That(elapsed.Elapsed.TotalSeconds).IsLessThan(10);
            await Assert.That(IsRunning(root.Path, "parent.pid")).IsFalse();
            await Assert.That(IsRunning(root.Path, "descendant.pid")).IsFalse();
            await Assert.That(File.Exists(System.IO.Path.Combine(root.Path, "summary.json"))).IsFalse();
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await run; }
            catch (OperationCanceledException) { }
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
                System.IO.Path.Combine(root.Path, "summary.json"), K6LoadProfile.OpenLookup, TimeSpan.FromSeconds(10), TestContext.Current!.Execution.CancellationToken);
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
            K6LoadHarnessChild.ChildStartInfo(scenario, root.Path), System.IO.Path.Combine(root.Path, "summary.json"), K6LoadProfile.OpenLookup, timeout,
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
    private static readonly string[] Scenarios = ["success", "nonzero", "missing", "oversized", "tree", "descendant", "secrets", "version"];

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
                exitCode = RunChild(arguments[2], arguments[3]);
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

    // Async continuations can deadlock behind the CLR's module-initialization lock.
    private static int RunChild(string scenario, string root)
    {
        string summary = System.IO.Path.Combine(root, "summary.json");
        if (scenario == "version")
        {
            File.WriteAllText(System.IO.Path.Combine(root, "version-launched"), "launched");
            Console.WriteLine("k6 v2.3.0 owned-version-canary");
            return 0;
        }
        if (scenario == "secrets" && Environment.GetEnvironmentVariable(CanaryName) is not null)
            return 73;
        if (scenario is "tree" or "descendant")
        {
            string pidName = scenario == "tree" ? "parent.pid" : "descendant.pid";
            File.WriteAllText(System.IO.Path.Combine(root, pidName), Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            if (scenario == "tree")
            {
                var descendantStart = ChildStartInfo("descendant", root);
                descendantStart.RedirectStandardOutput = false;
                descendantStart.RedirectStandardError = false;
                using var descendant = Process.Start(descendantStart)
                    ?? throw new InvalidOperationException("load_child_descendant_start_failed");
                if (!descendant.WaitForExit(20_000))
                    throw new TimeoutException("load_child_descendant_deadline");
            }
            else
            {
                File.WriteAllText(System.IO.Path.Combine(root, "descendant.ready"), "ready");
                // Bound synthetic descendants even if a process-cleanup regression leaves one alive.
                Thread.Sleep(TimeSpan.FromSeconds(15));
            }
            return 74;
        }
        byte[] noise = new byte[65536];
        Array.Fill(noise, (byte)'x');
        using Stream output = Console.OpenStandardOutput();
        using Stream error = Console.OpenStandardError();
        for (int block = 0; block < 16; block++)
        {
            output.Write(noise);
            error.Write(noise);
        }
        if (scenario != "missing")
            File.WriteAllText(summary, scenario == "oversized" ? new string('x', 4097) : K6LoadHarnessTests.CompleteSummary);
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

[Category("LoadControl")]
[NotInParallel]
public sealed class K6NativeControlTests
{
    [Test]
    [Arguments("Split")]
    [Arguments("Standalone")]
    public async Task OwnedPublicEventSearchAndGuardedWritesHaveIndependentSettledState(string topology)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(8));
        await using var fixture = await OwnedAppHostFixture.CreateAsync(topology, deadline.Token,
            configuredAdministrator: true, deploymentMode: "MultiTenant");
        await fixture.StartAsync(deadline.Token);
        await K6LoadRunner.RunNativeControlAsync(fixture, topology, deadline.Token);
    }
}

[Category("Load")]
[NotInParallel]
public sealed class K6EventProgrammeTests
{
    [Test]
    [Arguments("Split")]
    [Arguments("Standalone")]
    public async Task OwnedEventWorkloadsPreserveSettledStateAcrossLoadAndRecovery(string topology)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(20));
        await using var fixture = await OwnedAppHostFixture.CreateAsync(topology, deadline.Token,
            configuredAdministrator: true, deploymentMode: "MultiTenant");
        await fixture.StartAsync(deadline.Token);
        await K6LoadRunner.RunProgrammeAsync(fixture, topology, deadline.Token);
    }
}
