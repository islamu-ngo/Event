using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

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
    private const string RuntimeSample = """
        # TYPE dotnet_process_cpu_time_seconds_total counter
        # UNIT dotnet_process_cpu_time_seconds_total seconds
        # TYPE dotnet_process_memory_working_set_bytes gauge
        # UNIT dotnet_process_memory_working_set_bytes bytes
        dotnet_process_cpu_time_seconds_total{cpu_mode="user"} 12.5
        dotnet_process_cpu_time_seconds_total{cpu_mode="system"} 3.25
        dotnet_process_memory_working_set_bytes 4096
        """;

    [Test]
    public async Task NumericCandidatesNeverInventAnEmittingProcessOrResetIdentity()
    {
        var observation = K6LoadRunner.ParseRuntimePressure(RuntimeSample,
            K6LoadRunner.ParseRuntimeMetricInventory(RuntimeSample));
        await Assert.That(observation.CpuUserSeconds).IsEqualTo(12.5);
        await Assert.That(observation.CpuSystemSeconds).IsEqualTo(3.25);
        await Assert.That(observation.WorkingSetBytes).IsEqualTo(4096L);
        await Assert.That(observation.Status).IsEqualTo(K6ObservationStatus.RuntimeSeriesUnpinned);
        await Assert.That(observation.ProcessId).IsNull();
        await Assert.That(observation.KernelStart).IsNull();
        await Assert.That(observation.ExportContractQualified).IsFalse();
        await Assert.That(observation.ResetIdentityQualified).IsFalse();
        await Assert.That(observation.MetricDefinitions.Count).IsEqualTo(2);
        await Assert.That(observation.NumericSamples.Count).IsEqualTo(3);
        await Assert.That(observation.NumericSamples[0].CpuMode).IsEqualTo("user");
        await Assert.That(observation.NumericSamples[0].CpuSeconds).IsEqualTo(observation.CpuUserSeconds);
        await Assert.That(observation.NumericSamples[1].CpuMode).IsEqualTo("system");
        await Assert.That(observation.NumericSamples[2].Bytes).IsEqualTo(observation.WorkingSetBytes);
    }

    [Test]
    public async Task RetainedExporterFactsExcludeUnknownLabelsAndHelp()
    {
        string canary = Guid.NewGuid().ToString("N");
        string sample = RuntimeSample + $"\n# HELP dotnet_process_cpu_time_seconds_total {canary}\nprivate_{canary}{{customer=\"{canary}\"}} 7";
        var observation = K6LoadRunner.ParseRuntimePressure(sample, K6LoadRunner.ParseRuntimeMetricInventory(sample)) with
        {
            ScrapeEndpoint = K6LoadRunner.RuntimeScrapeEndpoint(new Uri("http://127.0.0.1:4321/")).AbsoluteUri
        };
        string retained = JsonSerializer.Serialize(observation);
        await Assert.That(retained).DoesNotContain(canary);
        await Assert.That(retained).DoesNotContain("customer");
        await Assert.That(observation.ScrapeEndpoint).IsEqualTo("http://127.0.0.1:4321/metrics");
        await Assert.That(observation.ExportContractQualified).IsFalse();
        await Assert.That(observation.NumericSamples[2].CpuMode).IsNull();
        await Assert.That(observation.NumericSamples[2].CpuSeconds).IsNull();
        await Assert.That(observation.NumericSamples[0].Bytes).IsNull();
    }

    [Test]
    [Arguments("http://user:private@127.0.0.1:4321/")]
    [Arguments("http://127.0.0.1:4321/?private=value")]
    [Arguments("http://127.0.0.1:4321/foreign")]
    [Arguments("http://example.org:4321/")]
    public async Task UntrustedScrapeLeadCannotRetainCredentialsOrOtherTargets(string origin)
    {
        await Assert.That(() => K6LoadRunner.RuntimeScrapeEndpoint(new Uri(origin))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task IntegralMemoryBytesRetainTheirExactWireValue()
    {
        string sample = RuntimeSample.Replace(" 4096", " 9007199254740993", StringComparison.Ordinal);
        var observation = K6LoadRunner.ParseRuntimePressure(sample, K6LoadRunner.ParseRuntimeMetricInventory(sample));
        await Assert.That(observation.WorkingSetBytes).IsEqualTo(9007199254740993L);
        await Assert.That(observation.NumericSamples[2].Bytes).IsEqualTo(9007199254740993L);
    }

    [Test]
    public async Task FinalPressureSnapshotPreservesTheLiveObservedPrefix()
    {
        var directory = Directory.CreateTempSubdirectory("islamu-pressure-snapshot-");
        try
        {
            string path = Path.Combine(directory.FullName, "pressure-Split.json");
            var runtime = K6LoadRunner.ParseRuntimePressure(RuntimeSample, K6LoadRunner.ParseRuntimeMetricInventory(RuntimeSample));
            DateTimeOffset started = DateTimeOffset.UtcNow;
            var rows = new List<K6PressureObservation>
            {
                new(0, 1, started, K6ObservationStatus.Unavailable, null, K6ObservationStatus.Unavailable, null, runtime)
            };
            await K6LoadRunner.WritePressureSnapshotAsync(path, started, rows, final: false, completed: false,
                TestContext.Current!.Execution.CancellationToken);
            using var live = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            await Assert.That(live.RootElement.GetProperty("Final").GetBoolean()).IsFalse();
            await Assert.That(live.RootElement.GetProperty("Completed").GetBoolean()).IsFalse();
            string first = live.RootElement.GetProperty("Observations")[0].GetRawText();
            rows.Add(rows[0] with { StartedOffsetMilliseconds = 2, CompletedOffsetMilliseconds = 3 });
            await K6LoadRunner.WritePressureSnapshotAsync(path, started, rows, final: true, completed: true,
                TestContext.Current!.Execution.CancellationToken);
            using var final = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            await Assert.That(final.RootElement.GetProperty("Final").GetBoolean()).IsTrue();
            await Assert.That(final.RootElement.GetProperty("Observations").GetArrayLength()).IsEqualTo(2);
            await Assert.That(final.RootElement.GetProperty("Observations")[0].GetRawText()).IsEqualTo(first);
            await Assert.That(final.RootElement.GetProperty("CapacityEvidenceEligible").GetBoolean()).IsFalse();
            if (!OperatingSystem.IsWindows())
                await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Test]
    [Arguments(false, true, 1)]
    [Arguments(true, true, 901)]
    public async Task IncompleteOrUnboundedPressureCannotPublishACompletedSnapshot(bool final, bool completed, int count)
    {
        var runtime = new K6RuntimePressure(K6ObservationStatus.RuntimeSeriesUnpinned, null, null, null);
        var row = new K6PressureObservation(0, 1, DateTimeOffset.UtcNow, K6ObservationStatus.Unavailable, null,
            K6ObservationStatus.Unavailable, null, runtime);
        var directory = Directory.CreateTempSubdirectory("islamu-pressure-refusal-");
        try
        {
            string path = Path.Combine(directory.FullName, "pressure-Split.json");
            await Assert.That(() => K6LoadRunner.WritePressureSnapshotAsync(path, row.ObservedAtUtc,
                Enumerable.Repeat(row, count).ToArray(), final, completed, TestContext.Current!.Execution.CancellationToken))
                .Throws<InvalidOperationException>();
            await Assert.That(File.Exists(path)).IsFalse();
        }
        finally { directory.Delete(recursive: true); }
    }

    [Test]
    [Arguments("NaN")]
    [Arguments("+Inf")]
    [Arguments("-1")]
    [Arguments("1 2")]
    public async Task InvalidNumericSamplesCannotBecomeMeasurements(string numeric)
    {
        string sample = RuntimeSample.Replace(" 12.5", " " + numeric, StringComparison.Ordinal);
        await Assert.That(() => K6LoadRunner.ParseRuntimePressure(sample,
            K6LoadRunner.ParseRuntimeMetricInventory(sample))).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("dotnet_process_cpu_time_seconds_total{cpu_mode=\"user\"} 1")]
    [Arguments("dotnet_process_cpu_time_seconds_total{cpu_mode=\"user\",customer=\"private\"} 1")]
    [Arguments("dotnet_process_memory_working_set_bytes 1.5")]
    public async Task DuplicateOrUnqualifiedSeriesCannotEnterNumericEvidence(string extra)
    {
        string sample = RuntimeSample + "\n" + extra;
        await Assert.That(() => K6LoadRunner.ParseRuntimePressure(sample,
            K6LoadRunner.ParseRuntimeMetricInventory(sample))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MissingSeriesRemainUnavailableRatherThanZero()
    {
        var observation = K6LoadRunner.ParseRuntimePressure("", []);
        await Assert.That(observation.CpuUserSeconds).IsNull();
        await Assert.That(observation.WorkingSetBytes).IsNull();
    }

    [Test]
    [Arguments(200)]
    [Arguments(401)]
    [Arguments(403)]
    [Arguments(404)]
    [Arguments(502)]
    public async Task OutageDoesNotExcuseUnexpectedStatusOrAuthorityFailure(int status)
    {
        await Assert.That(() => K6LoadRunner.RequireOutageOutcome(status)).Throws<InvalidOperationException>();
    }

    [Test]
    public void OnlyFiniteOutageOutcomesAreAdmitted()
    {
        K6LoadRunner.RequireOutageOutcome(null);
        K6LoadRunner.RequireOutageOutcome(500);
        K6LoadRunner.RequireOutageOutcome(503);
        K6LoadRunner.RequireDispatchOutcome(true, 1, true);
    }

    [Test]
    [Arguments(false, 1, true)]
    [Arguments(true, 0, true)]
    [Arguments(true, 2, true)]
    [Arguments(true, 1, false)]
    public async Task SettledDispatchMustPreserveOriginalSourceAndExactlyOneAttempt(bool source, int attempts, bool deliveredAt)
    {
        await Assert.That(() => K6LoadRunner.RequireDispatchOutcome(source, attempts, deliveredAt)).Throws<InvalidOperationException>();
    }

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
public sealed class K6RecoveryFinalizationTests
{
    private static readonly K6NativeDataset Dataset = new(Guid.CreateVersion7(), "owned-recovery", Guid.CreateVersion7(),
        "Owned read", Guid.CreateVersion7(), Guid.CreateVersion7(), "Owned control", Guid.CreateVersion7(),
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    private static string ReadBody => JsonSerializer.Serialize(new { id = Dataset.ReadId, title = Dataset.ReadTitle });
    private static string SearchBody => "{\"_embedded\":{\"items\":[{\"event\":" + ReadBody + "}]}}";

    [Test]
    [Arguments(500, false)]
    [Arguments(503, false)]
    [Arguments(500, true)]
    [Arguments(503, true)]
    public async Task RecoveryRetriesAdmittedTransientReadAndSearchResponses(int status, bool duringSearch)
    {
        var responses = new List<(int, string)>();
        if (duringSearch) responses.Add((200, ReadBody));
        responses.Add((status, "{}"));
        responses.Add((200, ReadBody));
        responses.Add((200, SearchBody));
        using var api = RecoveryClient(responses.ToArray());
        await Assert.That(await K6LoadRunner.TryVerifyPublicDatasetRecoveryAsync(api, Dataset, "owned", CancellationToken.None)).IsFalse();
        await Assert.That(await K6LoadRunner.TryVerifyPublicDatasetRecoveryAsync(api, Dataset, "owned", CancellationToken.None)).IsTrue();
    }

    [Test]
    [Arguments(401)]
    [Arguments(403)]
    [Arguments(404)]
    [Arguments(429)]
    [Arguments(502)]
    public async Task RecoveryNeverRetriesAuthorityOrUnadmittedStatusFailures(int status)
    {
        using var api = RecoveryClient((status, "{}"));
        var failure = await Assert.That(() => K6LoadRunner.TryVerifyPublicDatasetRecoveryAsync(api, Dataset, "owned", CancellationToken.None))
            .Throws<HttpRequestException>();
        await Assert.That((int?)failure!.StatusCode).IsEqualTo(status);
    }

    [Test]
    public async Task RecoveryNeverRetriesMalformedSuccessfulResponses()
    {
        using var api = RecoveryClient((200, "{"));
        await Assert.That(() => K6LoadRunner.TryVerifyPublicDatasetRecoveryAsync(api, Dataset, "owned", CancellationToken.None))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RecoveryNeverRetriesChangedReadOrSearchDatasets(bool changedSearch)
    {
        string changed = ReadBody.Replace("Owned read", "Changed read", StringComparison.Ordinal);
        using var api = RecoveryClient((200, changedSearch ? ReadBody : changed),
            (200, changedSearch ? "{\"_embedded\":{\"items\":[]}}" : SearchBody));
        var failure = await Assert.That(() => K6LoadRunner.TryVerifyPublicDatasetRecoveryAsync(api, Dataset, "owned", CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("load_public_dataset_changed");
    }

    [Test]
    public async Task RecoveryCannotHideAChangedReadBehindATransientSearchFailure()
    {
        using var api = RecoveryClient((200, ReadBody.Replace("Owned read", "Changed read", StringComparison.Ordinal)), (503, "{}"));
        var failure = await Assert.That(() => K6LoadRunner.TryVerifyPublicDatasetRecoveryAsync(api, Dataset, "owned", CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("load_public_dataset_changed");
    }

    [Test]
    public async Task RecoveryNeverRetriesAnIncompleteSuccessfulContract()
    {
        using var api = RecoveryClient((200, "{}"), (503, "{}"));
        await Assert.That(() => K6LoadRunner.TryVerifyPublicDatasetRecoveryAsync(api, Dataset, "owned", CancellationToken.None))
            .Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task RecoveryNeverRetriesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var api = RecoveryClient((200, ReadBody), (200, SearchBody));
        await Assert.That(() => K6LoadRunner.TryVerifyPublicDatasetRecoveryAsync(api, Dataset, "owned", cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FinalPressureCompletionRequiresSuccessfulSamplerSettlement(bool samplerFails)
    {
        var directory = Directory.CreateTempSubdirectory("islamu-pressure-finalization-");
        try
        {
            string path = Path.Combine(directory.FullName, "pressure.json");
            var samplerFailure = new InvalidOperationException("sampler-publication-failed");
            Task sampler = samplerFails ? Task.FromException(samplerFailure) : Task.CompletedTask;
            if (samplerFails)
            {
                var failure = await Assert.That(() => K6LoadRunner.FinalizePressureAsync(sampler, path,
                    DateTimeOffset.UnixEpoch, [], true, null, CancellationToken.None)).Throws<InvalidOperationException>();
                await Assert.That(ReferenceEquals(failure, samplerFailure)).IsTrue();
            }
            else
                await K6LoadRunner.FinalizePressureAsync(sampler, path, DateTimeOffset.UnixEpoch, [], true, null, CancellationToken.None);
            using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            await Assert.That(snapshot.RootElement.GetProperty("Final").GetBoolean()).IsTrue();
            await Assert.That(snapshot.RootElement.GetProperty("Completed").GetBoolean()).IsEqualTo(!samplerFails);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Test]
    public async Task PendingSamplerCannotPublishCompletedPressureEvidence()
    {
        var directory = Directory.CreateTempSubdirectory("islamu-pressure-pending-");
        var sampler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? finalization = null;
        try
        {
            string path = Path.Combine(directory.FullName, "pressure.json");
            await K6LoadRunner.WritePressureSnapshotAsync(path, DateTimeOffset.UnixEpoch, [], false, false, CancellationToken.None);
            string live = await File.ReadAllTextAsync(path);
            finalization = K6LoadRunner.FinalizePressureAsync(sampler.Task, path,
                DateTimeOffset.UnixEpoch, [], true, null, CancellationToken.None);

            await Assert.That(finalization.IsCompleted).IsFalse();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(live);

            sampler.SetResult();
            await finalization.WaitAsync(TimeSpan.FromSeconds(5));
            using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            await Assert.That(snapshot.RootElement.GetProperty("Final").GetBoolean()).IsTrue();
            await Assert.That(snapshot.RootElement.GetProperty("Completed").GetBoolean()).IsTrue();
        }
        finally
        {
            sampler.TrySetResult();
            if (finalization is not null) await finalization.WaitAsync(TimeSpan.FromSeconds(5));
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task WorkloadSamplerAndPublicationFailuresRetainTheirIndependentCauses()
    {
        var directory = Directory.CreateTempSubdirectory("islamu-pressure-combined-");
        try
        {
            string path = Path.Combine(directory.FullName, "pressure.json");
            await K6LoadRunner.WritePressureSnapshotAsync(path, DateTimeOffset.UnixEpoch, [], false, false, CancellationToken.None);
            string live = await File.ReadAllTextAsync(path);
            var workloadFailure = new InvalidOperationException("workload-failed");
            var samplerFailure = new IOException("sampler-failed");
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();

            var failure = await Assert.That(() => K6LoadRunner.FinalizePressureAsync(Task.FromException(samplerFailure), path,
                DateTimeOffset.UnixEpoch, [], true, workloadFailure, cancellation.Token)).Throws<AggregateException>();
            var causes = failure!.Flatten().InnerExceptions;
            await Assert.That(causes.Count).IsEqualTo(3);
            await Assert.That(causes.Any(error => ReferenceEquals(error, workloadFailure))).IsTrue();
            await Assert.That(causes.Any(error => ReferenceEquals(error, samplerFailure))).IsTrue();
            await Assert.That(causes.OfType<OperationCanceledException>()
                .Any(error => error.CancellationToken == cancellation.Token)).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(live);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Test]
    public async Task FailedFinalPublicationPreservesIncompleteEvidenceAndTheOriginalFailure()
    {
        var directory = Directory.CreateTempSubdirectory("islamu-pressure-publication-");
        try
        {
            string path = Path.Combine(directory.FullName, "pressure.json");
            await K6LoadRunner.WritePressureSnapshotAsync(path, DateTimeOffset.UnixEpoch, [], false, false, CancellationToken.None);
            string live = await File.ReadAllTextAsync(path);
            var original = new InvalidOperationException("workload-failed");
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            var failure = await Assert.That(() => K6LoadRunner.FinalizePressureAsync(Task.CompletedTask, path,
                DateTimeOffset.UnixEpoch, [], true, original, cancellation.Token)).Throws<AggregateException>();
            await Assert.That(failure!.Flatten().InnerExceptions.Any(error => ReferenceEquals(error, original))).IsTrue();
            await Assert.That(failure.Flatten().InnerExceptions.OfType<OperationCanceledException>()
                .Any(error => error.CancellationToken == cancellation.Token)).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(live);
            using var snapshot = JsonDocument.Parse(live);
            await Assert.That(snapshot.RootElement.GetProperty("Completed").GetBoolean()).IsFalse();
        }
        finally { directory.Delete(recursive: true); }
    }

    [Test]
    public async Task FinalPressurePublicationIoFailureRemainsAFailure()
    {
        var directory = Directory.CreateTempSubdirectory("islamu-pressure-publication-io-");
        try
        {
            string path = Path.Combine(directory.FullName, "pressure.json");
            Directory.CreateDirectory(path);
            await Assert.That(() => K6LoadRunner.FinalizePressureAsync(Task.CompletedTask, path,
                DateTimeOffset.UnixEpoch, [], true, null, CancellationToken.None)).Throws<IOException>();
            await Assert.That(File.Exists(path)).IsFalse();
            await Assert.That(Directory.Exists(path)).IsTrue();
        }
        finally { directory.Delete(recursive: true); }
    }

    private static HttpClient RecoveryClient(params (int Status, string Body)[] responses) =>
        new(new RecoveryResponseHandler(responses)) { BaseAddress = new Uri("http://127.0.0.1:4321/") };

    private sealed class RecoveryResponseHandler((int Status, string Body)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(int Status, string Body)> pending = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = pending.Dequeue();
            return Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)response.Status)
            { Content = new StringContent(response.Body) });
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
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CancellationAndDeadlineKillTheOwnedTreeAndCloseItsOutputPipes(bool useDeadline, bool observeAfterCompletion)
    {
        using var root = new OwnedProcessDirectory();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        var elapsed = Stopwatch.StartNew();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(root.Path, "descendant.ready");
        watcher.Created += (_, _) => ready.TrySetResult();
        watcher.Renamed += (_, _) => ready.TrySetResult();
        watcher.Error += (_, args) => ready.TrySetException(args.GetException());
        watcher.EnableRaisingEvents = true;
        var run = RunChildAsync(root, "tree", useDeadline ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(10), cancellation.Token);
        try
        {
            if (observeAfterCompletion)
            {
                try { await run; }
                catch (OperationCanceledException) { }
            }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellation.Token);
            if (!useDeadline)
            {
                await Assert.That(IsRunning(root.Path, "parent.pid")).IsTrue();
                await Assert.That(IsRunning(root.Path, "descendant.pid")).IsTrue();
                await cancellation.CancelAsync();
            }
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
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExitedParentCannotLeaveAnOwnedDescendantOrOpenOutputPipes(bool escapeSession)
    {
        using var root = new OwnedProcessDirectory();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(root.Path, "descendant.ready");
        watcher.Created += (_, _) => ready.TrySetResult();
        watcher.Renamed += (_, _) => ready.TrySetResult();
        watcher.Error += (_, args) => ready.TrySetException(args.GetException());
        watcher.EnableRaisingEvents = true;
        var run = RunChildAsync(root, escapeSession ? "escaping-parent" : "exiting-parent",
            TimeSpan.FromSeconds(10), cancellation.Token);
        Exception? failure = null;
        bool rejected = false;
        try
        {
            await ready.Task.WaitAsync(cancellation.Token);
            await Assert.That(IsRunning(root.Path, "parent.pid")).IsTrue();
            await Assert.That(IsRunning(root.Path, "descendant.pid")).IsTrue();
            if (escapeSession)
            {
                string pid = File.ReadAllText(System.IO.Path.Combine(root.Path, "descendant.pid")).Split(':')[0];
                string stat = File.ReadAllText("/proc/" + pid + "/stat");
                string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                await Assert.That(fields[3]).IsEqualTo(pid);
            }
            await K6LoadHarnessChild.ReleaseControlAsync(root.Path, parent: true, cancellation.Token);
            var rejection = await Assert.That(async () => await run).Throws<InvalidOperationException>();
            await Assert.That(rejection!.Message).IsEqualTo("load_process_or_summary_failed");
            rejected = true;
            await Assert.That(IsRunning(root.Path, "parent.pid")).IsFalse();
            await Assert.That(IsRunning(root.Path, "descendant.pid")).IsFalse();
            await Assert.That(File.Exists(System.IO.Path.Combine(root.Path, "summary.json"))).IsFalse();
        }
        catch (Exception observationFailure) { failure = observationFailure; }
        finally
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                if (IsRunning(root.Path, "descendant.pid"))
                {
                    int pid = int.Parse(File.ReadAllText(System.IO.Path.Combine(root.Path, "descendant.pid")).Split(':')[0],
                        CultureInfo.InvariantCulture);
                    using var descendant = Process.GetProcessById(pid);
                    await K6LoadHarnessChild.ReleaseControlAsync(root.Path, parent: false, cleanup.Token);
                    await descendant.WaitForExitAsync(cleanup.Token);
                }
                await cancellation.CancelAsync();
            }
            catch (Exception cleanupFailure)
            {
                failure = failure is null ? cleanupFailure : new AggregateException(failure, cleanupFailure);
            }
            try { await run; }
            catch (Exception runFailure)
            {
                if (!rejected)
                    failure = failure is null ? runFailure : new AggregateException(failure, runFailure);
            }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Test]
    public async Task CancellationClosesTheLeaseBeforeOwnerReadiness()
    {
        using var root = new OwnedProcessDirectory();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eof = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(root.Path);
        watcher.Created += (_, args) =>
        {
            if (args.Name == "owner-peer.ready") ready.TrySetResult();
            if (args.Name == "owner-peer.eof") eof.TrySetResult();
        };
        watcher.Error += (_, args) =>
        {
            ready.TrySetException(args.GetException());
            eof.TrySetException(args.GetException());
        };
        watcher.EnableRaisingEvents = true;
        var run = K6LoadRunner.ObserveOwnedProcessAsync(
            K6LoadHarnessChild.ChildStartInfo("success", root.Path), TimeSpan.FromSeconds(1), 0,
            cancellation.Token, K6LoadHarnessChild.ChildStartInfo("owner-withheld-ready", root.Path));
        Exception? failure = null;
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token);
            await cancellation.CancelAsync();
            await eof.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Assert.That(async () => await run).Throws<OperationCanceledException>();
        }
        catch (Exception observationFailure) { failure = observationFailure; }
        finally
        {
            await cancellation.CancelAsync();
            try { await run; }
            catch (OperationCanceledException) { }
            catch (Exception runFailure)
            {
                failure = failure is null ? runFailure : new AggregateException(failure, runFailure);
            }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Test]
    public async Task PreReadyResultPreservesPrimaryAndCleanupFailures()
    {
        using var root = new OwnedProcessDirectory();
        var failure = await Assert.That(async () => await K6LoadRunner.ObserveOwnedProcessAsync(
            K6LoadHarnessChild.ChildStartInfo("success", root.Path), TimeSpan.FromSeconds(1), 0,
            TestContext.Current!.Execution.CancellationToken,
            K6LoadHarnessChild.ChildStartInfo("owner-initialization-failure", root.Path))).Throws<AggregateException>();
        await Assert.That(failure!.Flatten().InnerExceptions.Select(exception => exception.Message).ToArray())
            .IsEquivalentTo(["owner_peer_primary", "owner_peer_cleanup"]);
    }

    [Test]
    public async Task ControllerExitClosesTheLeaseAndSettlesTheOwnedTree()
    {
        using var root = new OwnedProcessDirectory();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(root.Path, "descendant.ready");
        watcher.Created += (_, _) => ready.TrySetResult();
        watcher.Renamed += (_, _) => ready.TrySetResult();
        watcher.Error += (_, args) => ready.TrySetException(args.GetException());
        watcher.EnableRaisingEvents = true;
        using var controller = Process.Start(K6LoadHarnessChild.ChildStartInfo("controller", root.Path))
            ?? throw new InvalidOperationException("load_controller_start_failed");
        Task output = controller.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        Task error = controller.StandardError.BaseStream.CopyToAsync(Stream.Null);
        try
        {
            await ready.Task.WaitAsync(deadline.Token);
            await Assert.That(IsRunning(root.Path, "parent.pid")).IsTrue();
            await Assert.That(IsRunning(root.Path, "descendant.pid")).IsTrue();
            await Assert.That(IsRunning(root.Path, "helper.pid")).IsTrue();
            int helperPid = int.Parse(File.ReadAllText(System.IO.Path.Combine(root.Path, "helper.pid")).Split(':')[0],
                CultureInfo.InvariantCulture);
            using var helper = Process.GetProcessById(helperPid);
            using (var control = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
                await control.ConnectAsync(new UnixDomainSocketEndPoint(
                    System.IO.Path.Combine(root.Path, "controller.control")), deadline.Token);
            await controller.WaitForExitAsync(deadline.Token);
            await helper.WaitForExitAsync(deadline.Token);
            await Task.WhenAll(output, error).WaitAsync(deadline.Token);
            await Assert.That(controller.ExitCode).IsEqualTo(0);
            await Assert.That(IsRunning(root.Path, "parent.pid")).IsFalse();
            await Assert.That(IsRunning(root.Path, "descendant.pid")).IsFalse();
            await Assert.That(IsRunning(root.Path, "helper.pid")).IsFalse();
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            if (!controller.HasExited)
            {
                using var control = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await control.ConnectAsync(new UnixDomainSocketEndPoint(
                    System.IO.Path.Combine(root.Path, "controller.control")), cleanup.Token);
            }
            if (IsRunning(root.Path, "parent.pid"))
                await K6LoadHarnessChild.ReleaseControlAsync(root.Path, parent: true, cleanup.Token);
            if (IsRunning(root.Path, "descendant.pid"))
                await K6LoadHarnessChild.ReleaseControlAsync(root.Path, parent: false, cleanup.Token);
            await controller.WaitForExitAsync(cleanup.Token);
            await Task.WhenAll(output, error).WaitAsync(cleanup.Token);
        }
    }

    [Test]
    public async Task OwnedCleanupPreservesAnUnrelatedProcess()
    {
        using var root = new OwnedProcessDirectory();
        using var foreignRoot = new OwnedProcessDirectory();
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        guard.CancelAfter(TimeSpan.FromSeconds(15));
        using var cancellation = new CancellationTokenSource();
        var foreignReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var foreignWatcher = new FileSystemWatcher(foreignRoot.Path, "descendant.ready");
        foreignWatcher.Created += (_, _) => foreignReady.TrySetResult();
        foreignWatcher.Renamed += (_, _) => foreignReady.TrySetResult();
        foreignWatcher.Error += (_, args) => foreignReady.TrySetException(args.GetException());
        foreignWatcher.EnableRaisingEvents = true;
        using var foreign = Process.Start(K6LoadHarnessChild.ChildStartInfo("held-descendant", foreignRoot.Path))
            ?? throw new InvalidOperationException("load_foreign_canary_start_failed");
        Task<K6ProcessMeasurement>? run = null;
        try
        {
            await foreignReady.Task.WaitAsync(guard.Token);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watcher = new FileSystemWatcher(root.Path, "descendant.ready");
            watcher.Created += (_, _) => ready.TrySetResult();
            watcher.Renamed += (_, _) => ready.TrySetResult();
            watcher.Error += (_, args) => ready.TrySetException(args.GetException());
            watcher.EnableRaisingEvents = true;
            Task<K6ProcessMeasurement> observedRun = RunChildAsync(root, "tree", TimeSpan.FromSeconds(10), cancellation.Token);
            run = observedRun;
            await ready.Task.WaitAsync(guard.Token);
            await cancellation.CancelAsync();
            await Assert.That(async () => await observedRun).Throws<OperationCanceledException>();
            await Assert.That(IsRunning(root.Path, "parent.pid")).IsFalse();
            await Assert.That(IsRunning(root.Path, "descendant.pid")).IsFalse();
            await Assert.That(foreign.HasExited).IsFalse();
            await Assert.That(IsRunning(foreignRoot.Path, "descendant.pid")).IsTrue();
        }
        finally
        {
            await cancellation.CancelAsync();
            try
            {
                if (run is not null)
                {
                    try { await run; }
                    catch (OperationCanceledException) { }
                }
            }
            finally
            {
                if (!foreign.HasExited) foreign.Kill();
                await foreign.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
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

    private static bool IsRunning(string root, string name)
    {
        string file = System.IO.Path.Combine(root, name);
        if (!File.Exists(file))
            return false;
        string[] recorded = File.ReadAllText(file).Split(':');
        int id = int.Parse(recorded[0], CultureInfo.InvariantCulture);
        var current = K6LoadHarnessChild.ReadLifetime(id);
        bool sameLifetime = current is not null && current.Value.Start == long.Parse(recorded[1], CultureInfo.InvariantCulture);
        Console.WriteLine($"{name}: state={current?.State.ToString() ?? "absent"}; sameLifetime={sameLifetime}");
        return current is { } lifetime && sameLifetime && lifetime.State is not ('Z' or 'X');
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
    private static readonly string[] Scenarios =
        ["success", "nonzero", "missing", "oversized", "tree", "descendant", "exiting-parent", "escaping-parent",
            "held-descendant", "controller", "owner-withheld-ready", "owner-initialization-failure", "secrets", "version"];

    internal static async Task ReleaseControlAsync(string root, bool parent, CancellationToken token)
    {
        if (!IsOwnedRoot(root)) throw new InvalidOperationException("load_child_control_owner_invalid");
        using var control = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await control.ConnectAsync(new UnixDomainSocketEndPoint(System.IO.Path.Combine(root,
            parent ? "parent.control" : "descendant.control")), token);
    }

    private static Socket CreateControl(string root, bool parent)
    {
        var control = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            control.Bind(new UnixDomainSocketEndPoint(System.IO.Path.Combine(root,
                parent ? "parent.control" : "descendant.control")));
            control.Listen(1);
            return control;
        }
        catch
        {
            control.Dispose();
            throw;
        }
    }

    private static void WaitForRelease(Socket control)
    {
        if (!control.Poll(15_000_000, SelectMode.SelectRead))
            throw new TimeoutException("load_child_control_deadline");
        using var released = control.Accept();
    }

    internal static (long Start, char State)? ReadLifetime(int pid)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                string stat = File.ReadAllText("/proc/" + pid.ToString(CultureInfo.InvariantCulture) + "/stat");
                string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                return (long.Parse(fields[19], CultureInfo.InvariantCulture), fields[0][0]);
            }
            using var process = Process.GetProcessById(pid);
            return process.HasExited ? null : (process.StartTime.ToUniversalTime().Ticks, 'R');
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (ArgumentException) { return null; }
    }

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
        if (scenario is "owner-withheld-ready" or "owner-initialization-failure")
        {
            bool withheld = scenario == "owner-withheld-ready";
            if (withheld)
            {
                File.WriteAllText(System.IO.Path.Combine(root, "owner-peer.ready"), "ready");
                _ = Console.In.ReadToEnd();
                File.WriteAllText(System.IO.Path.Combine(root, "owner-peer.eof"), "eof");
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                type = "result",
                exitCode = (int?)null,
                wallMilliseconds = 0,
                startedTimestamp = (long?)null,
                exitedTimestamp = (long?)null,
                sampleCount = 0,
                peakCpuPercent = 0,
                peakRssBytes = 0,
                cancelled = withheld,
                primaryFailure = withheld ? "controller_eof" : "owner_peer_primary",
                cleanupFailures = withheld ? Array.Empty<string>() : ["owner_peer_cleanup"],
                output = string.Empty
            }));
            return 70;
        }
        if (scenario == "controller")
        {
            using var control = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            control.Bind(new UnixDomainSocketEndPoint(System.IO.Path.Combine(root, "controller.control")));
            control.Listen(1);
            var ownerStart = K6LoadRunner.StartInfo(System.IO.Path.Combine(
                AppContext.BaseDirectory, "LoadProcessOwner", "ISLAMU.ReleaseEngineering"));
            ownerStart.RedirectStandardInput = true;
            ownerStart.ArgumentList.Add("own-load-process");
            using var owner = Process.Start(ownerStart)
                ?? throw new InvalidOperationException("load_owner_start_failed");
            using var ready = JsonDocument.Parse(owner.StandardOutput.ReadLine()
                ?? throw new InvalidOperationException("load_owner_not_ready"));
            if (ready.RootElement.GetProperty("type").GetString() != "ready")
                throw new InvalidOperationException("load_owner_not_ready");
            var lifetime = ReadLifetime(owner.Id) ?? throw new InvalidOperationException("load_owner_lifetime_missing");
            File.WriteAllText(System.IO.Path.Combine(root, "helper.pid"),
                owner.Id.ToString(CultureInfo.InvariantCulture) + ":" + lifetime.Start.ToString(CultureInfo.InvariantCulture));
            var workload = ChildStartInfo("exiting-parent", root);
            owner.StandardInput.WriteLine(JsonSerializer.Serialize(new
            {
                type = "start",
                executable = workload.FileName,
                arguments = workload.ArgumentList.ToArray(),
                workingDirectory = Environment.CurrentDirectory,
                environment = workload.Environment,
                timeoutMilliseconds = 10000,
                captureLimit = 0
            }));
            owner.StandardInput.Flush();
            using var started = JsonDocument.Parse(owner.StandardOutput.ReadLine()
                ?? throw new InvalidOperationException("load_owner_not_started"));
            if (started.RootElement.GetProperty("type").GetString() != "started")
                throw new InvalidOperationException("load_owner_not_started");
            WaitForRelease(control);
            return 0;
        }
        if (scenario == "version")
        {
            File.WriteAllText(System.IO.Path.Combine(root, "version-launched"), "launched");
            Console.WriteLine("k6 v2.3.0 owned-version-canary");
            return 0;
        }
        if (scenario == "secrets" && Environment.GetEnvironmentVariable(CanaryName) is not null)
            return 73;
        if (scenario is "tree" or "descendant" or "exiting-parent" or "escaping-parent" or "held-descendant")
        {
            bool parent = scenario is "tree" or "exiting-parent" or "escaping-parent";
            string pidName = parent ? "parent.pid" : "descendant.pid";
            var lifetime = ReadLifetime(Environment.ProcessId)
                ?? throw new InvalidOperationException("load_child_lifetime_missing");
            File.WriteAllText(System.IO.Path.Combine(root, pidName), Environment.ProcessId.ToString(CultureInfo.InvariantCulture)
                + ":" + lifetime.Start.ToString(CultureInfo.InvariantCulture));
            if (parent)
            {
                using var control = scenario != "tree" ? CreateControl(root, parent: true) : null;
                var descendantStart = ChildStartInfo(scenario == "tree" ? "descendant" : "held-descendant", root);
                if (scenario == "escaping-parent")
                {
                    string executable = descendantStart.FileName;
                    descendantStart.FileName = "/usr/bin/setsid";
                    descendantStart.ArgumentList.Insert(0, executable);
                }
                descendantStart.RedirectStandardOutput = false;
                descendantStart.RedirectStandardError = false;
                using var descendant = Process.Start(descendantStart)
                    ?? throw new InvalidOperationException("load_child_descendant_start_failed");
                if (control is not null) WaitForRelease(control);
                else if (!descendant.WaitForExit(20_000))
                    throw new TimeoutException("load_child_descendant_deadline");
            }
            else
            {
                using var control = scenario == "held-descendant" ? CreateControl(root, parent: false) : null;
                string pending = System.IO.Path.Combine(root, "descendant.pending");
                File.WriteAllText(pending, "ready");
                File.Move(pending, System.IO.Path.Combine(root, "descendant.ready"));
                if (control is not null) WaitForRelease(control);
                else
                {
                    // Bound synthetic descendants even if a process-cleanup regression leaves one alive.
                    Thread.Sleep(TimeSpan.FromSeconds(15));
                }
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
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
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
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
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
        await using var fixture = await OwnedAppHostFixture.CreateAsync(topology, true, "MultiTenant", null,
            builder =>
            {
                var api = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>()
                    .Single(resource => resource.Name == (topology == "Split" ? "explore-api" : "event-standalone")));
                api.WithEnvironment("EmailDispatchProcessor__Enabled", "true")
                    .WithEnvironment("EmailDispatchProcessor__Mode", "HostedService")
                    .WithEnvironment("EmailDispatchProcessor__PollingIntervalSeconds", "1");
                return Task.CompletedTask;
            }, deadline.Token);
        await fixture.StartAsync(deadline.Token);
        await K6LoadRunner.RunProgrammeAsync(fixture, topology, deadline.Token);
    }
}
