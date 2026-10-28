using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Explore.Application.Services.Moderation;

namespace ISLAMU.Moderation.Measurement;

internal sealed record CaseEvidence(string CaseId, string JournalTestId, string ContextId,
    string NodeUid, string SessionId, string SpanTestId, string TraceId, string CaseSpanId,
    string BodySpanId, string MtpTestId, string MtpExecutionId);
internal sealed record BundleEvidence(string Bundle, ChildExecution Process,
    string MtpRunId, ImmutableArray<CaseEvidence> Cases,
    string JournalDigest, string ReportDigest, string TrxDigest, string HtmlDigest);

/// <summary>
/// Evidence is accepted only from paths allocated by the launcher for its just-ended child.
/// Native identities are reconciled, not inferred from counts or a final passing label.
/// The journal is the independent retry/override history that TRX cannot supply.
/// </summary>
internal static class EvidenceReconciliation
{
    internal static async Task<BundleEvidence> ReadAsync(string directory, Guid runId, ModerationMeasurementCatalogueJob job,
        ChildExecution process, string providerCode, string providerVersion, string testConfigurationDigest, CancellationToken token)
    {
        Require.That(process.Bundle == job.Bundle && process.ExitCode == 0, "execution-incomplete");
        var journal = await Artifacts.ReadAsync(Path.Combine(directory, job.Bundle + ".lifecycle.jsonl"),
            OwnedExecution.MaximumReportBytes, token);
        var report = await Artifacts.ReadAsync(Path.Combine(directory, job.Bundle + ".tunit-report.json"),
            OwnedExecution.MaximumReportBytes, token);
        var trx = await Artifacts.ReadAsync(Path.Combine(directory, job.Bundle + ".trx"),
            OwnedExecution.MaximumReportBytes, token);
        var html = await Artifacts.ReadAsync(Path.Combine(directory, job.Bundle + ".html"),
            OwnedExecution.MaximumReportBytes, token);
        var lifecycle = ReadJournal(journal, runId, job, process, providerCode, providerVersion, testConfigurationDigest);
        var cases = ReadReport(report, job, process, lifecycle);
        var terminals = ReadTrx(trx, job, process);
        var retained = cases.Select(item => item with
        {
            MtpTestId = terminals.Cases[item.CaseId].TestId,
            MtpExecutionId = terminals.Cases[item.CaseId].ExecutionId
        }).ToImmutableArray();
        return new(job.Bundle, process, terminals.RunId, retained, Artifacts.Hash(journal), Artifacts.Hash(report),
            Artifacts.Hash(trx), Artifacts.Hash(html));
    }

    private sealed class Lifecycle
    {
        internal int Stage { get; set; }
        internal required string ContextId { get; init; }
        internal string? NodeUid { get; set; }
        internal string? SessionId { get; set; }
        internal string? SpanTestId { get; set; }
        internal ImmutableArray<DateTimeOffset> DatabaseObservedAt { get; set; } = [];
    }

    private static Dictionary<string, Lifecycle> ReadJournal(
        byte[] bytes, Guid runId, ModerationMeasurementCatalogueJob job, ChildExecution process,
        string providerCode, string providerVersion, string testConfigurationDigest)
    {
        Require.That(bytes[^1] == (byte)'\n', "journal-partial");
        var text = new UTF8Encoding(false, true).GetString(bytes);
        var lines = text.Split('\n');
        Require.That(lines.Length == job.Cases.Length * 4 + 3 && lines[^1].Length == 0, "journal-count");
        var results = new Dictionary<string, Lifecycle>(StringComparer.Ordinal);
        var contexts = new HashSet<string>(StringComparer.Ordinal);
        var previous = process.StartedAt;
        var common = new[] { "schemaVersion", "tunitVersion", "runId", "sequence", "event", "bundle", "utc", "nodeUid", "sessionId" };
        for (var index = 0; index < lines.Length - 1; index++)
        {
            Require.That(lines[index].Length is > 0 and <= 64 * 1024, "journal-record-size");
            using var document = JsonDocument.Parse(lines[index], new JsonDocumentOptions { MaxDepth = 4 });
            var row = document.RootElement;
            EvidenceJson.UniqueProperties(row);
            Require.That(row.GetProperty("schemaVersion").GetInt32() == 2
                && EvidenceJson.Text(row, "tunitVersion") == "1.63.0"
                && EvidenceJson.Text(row, "runId") == runId.ToString("N")
                && row.GetProperty("sequence").GetInt64() == index
                && EvidenceJson.Text(row, "bundle") == job.Bundle, "journal-binding");
            var at = EvidenceJson.Instant(row, "utc");
            Require.That(at >= previous && at <= process.CompletedAt, "journal-time");
            previous = at;
            var kind = EvidenceJson.Text(row, "event");
            if (index == 0)
            {
                EvidenceJson.Shape(row, common);
                Require.That(kind == "header" && row.GetProperty("nodeUid").ValueKind == JsonValueKind.Null
                    && row.GetProperty("sessionId").ValueKind == JsonValueKind.Null, "journal-header");
                continue;
            }
            if (index == lines.Length - 2)
            {
                EvidenceJson.Shape(row, [.. common, "lifecycleCompleted", "sessionHasFailures",
                    "sessionCancelled", "observedTestCount", "failureObserved"]);
                Require.That(kind == "footer" && row.GetProperty("lifecycleCompleted").GetBoolean()
                    && !row.GetProperty("sessionHasFailures").GetBoolean()
                    && !row.GetProperty("sessionCancelled").GetBoolean()
                    && !row.GetProperty("failureObserved").GetBoolean()
                    && row.GetProperty("nodeUid").ValueKind == JsonValueKind.Null
                    && row.GetProperty("sessionId").ValueKind == JsonValueKind.Null
                    && row.GetProperty("observedTestCount").GetInt32() == job.Cases.Length
                    && results.Count == job.Cases.Length && results.Values.All(value => value.Stage == 4),
                    "journal-footer");
                continue;
            }
            Require.That(kind is "test-start" or "test-end-early" or "test-end-late" or "test-terminal",
                "journal-retry-or-unknown-event");
            var testFields = new[] { "testId", "contextId", "spanTestId", "attemptIndex",
                "currentRetryAttempt", "priorAttemptCount", "state", "errorCategory", "originalErrorCategory",
                "originalFailurePresent", "originalExceptionPresent", "resultOverridden", "failureObserved" };
            EvidenceJson.Shape(row, kind == "test-terminal"
                ? [.. common, .. testFields, "databaseWitnesses"] : [.. common, .. testFields]);
            var id = EvidenceJson.Text(row, "testId");
            var expected = job.Cases.SingleOrDefault(test => test.JournalTestId == id)
                ?? throw new MeasurementFailure("journal-extra-case");
            var contextId = EvidenceJson.Text(row, "contextId");
            if (kind == "test-start")
            {
                Require.That(!results.ContainsKey(id) && contexts.Add(contextId), "journal-duplicate-start");
                results.Add(id, new() { ContextId = contextId });
            }
            Require.That(results.TryGetValue(id, out var observation), "journal-start-missing");
            var stage = kind switch { "test-start" => 0, "test-end-early" => 1, "test-end-late" => 2, _ => 3 };
            Require.That(observation!.Stage == stage && observation.ContextId == contextId
                && row.GetProperty("attemptIndex").GetInt32() == 0
                && row.GetProperty("currentRetryAttempt").GetInt32() == 0
                && row.GetProperty("priorAttemptCount").GetInt32() == 0
                && EvidenceJson.Text(row, "errorCategory") == "none"
                && EvidenceJson.Text(row, "originalErrorCategory") == "none"
                && !row.GetProperty("originalFailurePresent").GetBoolean()
                && !row.GetProperty("originalExceptionPresent").GetBoolean()
                && !row.GetProperty("resultOverridden").GetBoolean()
                && !row.GetProperty("failureObserved").GetBoolean(), "journal-lifecycle-or-prior-failure");
            var state = EvidenceJson.Text(row, "state");
            Require.That(stage == 3 ? state == "passed" :
                state is "unavailable" or "not-started" or "waiting-for-dependencies" or "queued" or "running" or "passed",
                "journal-unsuccessful");
            observation.NodeUid = PreserveTag(row, "nodeUid", observation.NodeUid);
            observation.SessionId = PreserveTag(row, "sessionId", observation.SessionId);
            observation.SpanTestId = PreserveTag(row, "spanTestId", observation.SpanTestId);
            Require.That(observation.NodeUid is null || observation.NodeUid == expected.NodeUid, "journal-node-mismatch");
            if (stage == 3)
            {
                var databases = row.GetProperty("databaseWitnesses");
                Require.That(databases.ValueKind == JsonValueKind.Array
                    && (job.Bundle == ModerationMeasurementCatalogue.Bundles[0]
                        ? databases.GetArrayLength() == 0 : databases.GetArrayLength() is > 0 and <= 8),
                    "journal-database-witness-missing-or-extra");
                var unique = new HashSet<(string, string, string)>();
                var times = ImmutableArray.CreateBuilder<DateTimeOffset>();
                foreach (var database in databases.EnumerateArray())
                {
                    EvidenceJson.UniqueProperties(database);
                    EvidenceJson.Shape(database, "providerCode", "providerVersion", "configurationDigest", "observedAt");
                    var provider = EvidenceJson.Text(database, "providerCode");
                    var version = EvidenceJson.Text(database, "providerVersion");
                    var digest = EvidenceJson.Text(database, "configurationDigest");
                    var seenAt = EvidenceJson.Instant(database, "observedAt");
                    Require.That(provider == providerCode && version == providerVersion && digest == testConfigurationDigest
                        && unique.Add((provider, version, digest))
                        && seenAt >= process.StartedAt && seenAt <= at, "journal-database-witness-mismatch");
                    times.Add(seenAt);
                }
                observation.DatabaseObservedAt = times.ToImmutable();
            }
            observation.Stage++;
        }
        return results;
    }

    private static string? PreserveTag(JsonElement row, string key, string? prior)
    {
        if (row.GetProperty(key).ValueKind == JsonValueKind.Null)
        {
            Require.That(prior is null, "journal-tag-regression");
            return null;
        }
        var current = EvidenceJson.Text(row, key);
        Require.That(prior is null || prior == current, "journal-tag-changed");
        return current;
    }

    private static ImmutableArray<CaseEvidence> ReadReport(byte[] bytes, ModerationMeasurementCatalogueJob job, ChildExecution process,
        Dictionary<string, Lifecycle> lifecycle)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var report = document.RootElement;
        EvidenceJson.UniqueProperties(report);
        EvidenceJson.Shape(report, "schemaVersion", "assemblyName", "machineName", "timestamp",
            "tunitVersion", "operatingSystem", "runtimeVersion", "totalDurationMs", "summary", "groups", "spans");
        Require.That(report.GetProperty("schemaVersion").GetInt32() == 1
            && EvidenceJson.Text(report, "tunitVersion") == "1.63.0.0"
            && EvidenceJson.Text(report, "assemblyName") == job.Bundle
            && EvidenceJson.Text(report, "runtimeVersion") == ".NET " + Environment.Version,
            "report-version-or-bundle");
        var totalDuration = report.GetProperty("totalDurationMs").GetDouble();
        Require.That(double.IsFinite(totalDuration) && totalDuration >= 0
            && totalDuration <= (process.CompletedAt - process.StartedAt).TotalMilliseconds,
            "report-duration");
        Summary(report.GetProperty("summary"), job.Cases.Length);
        var tests = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var group in report.GetProperty("groups").EnumerateArray())
        {
            var members = group.GetProperty("tests");
            Summary(group.GetProperty("summary"), members.GetArrayLength());
            Require.That(members.GetArrayLength() > 0, "report-empty-group");
            foreach (var test in members.EnumerateArray())
            {
                var id = EvidenceJson.Text(test, "id");
                Require.That(tests.TryAdd(id, test), "report-duplicate-case");
                var expected = job.Cases.SingleOrDefault(item => item.NodeUid == id)
                    ?? throw new MeasurementFailure("report-extra-case");
                Require.That(EvidenceJson.Text(test, "status") == "passed"
                    && EvidenceJson.Text(test, "className") == expected.ClassName
                    && EvidenceJson.Text(group, "className") == expected.ClassName
                    && EvidenceJson.Text(test, "methodName") == expected.MethodName
                    && EvidenceJson.Text(test, "displayName") == expected.DisplayName, "report-case-mismatch");
                Interval(test, "startTime", "endTime", process);
                Require.That(double.IsFinite(test.GetProperty("durationMs").GetDouble())
                    && test.GetProperty("durationMs").GetDouble() >= 0, "report-duration");
            }
        }
        Require.That(tests.Count == job.Cases.Length, "report-missing-case");
        var spans = report.GetProperty("spans").EnumerateArray().ToArray();
        Require.That(spans.Length is > 0 and <= 100_000, "report-span-limit");
        var spanIds = new HashSet<(string, string)>();
        foreach (var span in spans)
        {
            Require.That(spanIds.Add((EvidenceJson.Text(span, "traceId"), EvidenceJson.Text(span, "spanId")))
                && EvidenceJson.Text(span, "status") is "Unset" or "Ok", "report-span-failure");
            SpanInterval(span, process);
            if (span.TryGetProperty("tags", out var tags))
                _ = Tags(tags); // Includes duplicate-key rejection for non-case spans.
        }
        var caseSpans = spans.Where(span => EvidenceJson.Text(span, "spanType") == "test case").ToArray();
        var bodies = spans.Where(span => EvidenceJson.Text(span, "spanType") == "test body").ToArray();
        Require.That(caseSpans.Length == job.Cases.Length && bodies.Length == job.Cases.Length,
            "report-body-or-case-count");
        var sessions = new HashSet<string>(StringComparer.Ordinal);
        var nativeTestIds = new HashSet<string>(StringComparer.Ordinal);
        var usedBodies = new HashSet<(string, string)>();
        var results = ImmutableArray.CreateBuilder<CaseEvidence>();
        foreach (var expected in job.Cases)
        {
            var test = tests[expected.NodeUid];
            var trace = EvidenceJson.Text(test, "traceId");
            var id = EvidenceJson.Text(test, "spanId");
            var span = caseSpans.Single(item => EvidenceJson.Text(item, "traceId") == trace
                && EvidenceJson.Text(item, "spanId") == id);
            var tags = Tags(span.GetProperty("tags"));
            Require.That(tags["tunit.test.node_uid"] == expected.NodeUid
                && tags["tunit.assembly.name"] == job.Bundle
                && tags["tunit.test.method"] == expected.MethodName
                && tags["test.case.result.status"] == "pass", "report-native-identity");
            var session = tags["tunit.session.id"];
            var nativeId = tags["tunit.test.id"];
            Require.That(nativeTestIds.Add(nativeId), "report-duplicate-native-id");
            sessions.Add(session);
            var observed = lifecycle[expected.JournalTestId];
            Require.That((observed.SessionId is null || observed.SessionId == session)
                && (observed.SpanTestId is null || observed.SpanTestId == nativeId)
                && (observed.NodeUid is null || observed.NodeUid == expected.NodeUid), "report-journal-join");
            var body = bodies.Single(item => EvidenceJson.Text(item, "traceId") == trace
                && EvidenceJson.Text(item, "parentSpanId") == id);
            var bodyId = EvidenceJson.Text(body, "spanId");
            Require.That(usedBodies.Add((trace, bodyId))
                && EvidenceJson.Text(span, "source") == "TUnit" && EvidenceJson.Text(body, "source") == "TUnit",
                "report-body-join");
            var start = span.GetProperty("startTimeMs").GetDouble();
            var end = start + span.GetProperty("durationMs").GetDouble();
            var bodyStart = body.GetProperty("startTimeMs").GetDouble();
            var bodyEnd = bodyStart + body.GetProperty("durationMs").GetDouble();
            Require.That(bodyStart >= start && bodyEnd <= end + 0.01, "report-body-window");
            foreach (var databaseObservedAt in observed.DatabaseObservedAt)
            {
                var databaseAt = (databaseObservedAt.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks)
                    / (double)TimeSpan.TicksPerMillisecond;
                Require.That(databaseAt >= bodyStart && databaseAt <= bodyEnd + 0.01,
                    "report-database-witness-outside-body");
            }
            results.Add(new(expected.CaseId, expected.JournalTestId, observed.ContextId,
                expected.NodeUid, session, nativeId, trace, id, bodyId, "", ""));
        }
        Require.That(sessions.Count == 1, "report-multiple-sessions");
        var sessionSpan = spans.Single(span => EvidenceJson.Text(span, "spanType") == "test session");
        var sessionTags = Tags(sessionSpan.GetProperty("tags"));
        var assemblySpan = spans.Single(span => EvidenceJson.Text(span, "spanType") == "test assembly");
        var assemblyTags = Tags(assemblySpan.GetProperty("tags"));
        var count = job.Cases.Length.ToString(CultureInfo.InvariantCulture);
        Require.That(sessionTags["tunit.session.id"] == sessions.Single()
            && sessionTags["tunit.filter"] == job.TreeFilter && sessionTags["tunit.test.count"] == count
            && assemblyTags["tunit.assembly.name"] == job.Bundle && assemblyTags["tunit.test.count"] == count
            && EvidenceJson.Text(assemblySpan, "parentSpanId") == EvidenceJson.Text(sessionSpan, "spanId")
            && EvidenceJson.Text(assemblySpan, "traceId") == EvidenceJson.Text(sessionSpan, "traceId"),
            "report-session-join");
        return results.ToImmutable();
    }

    private static Dictionary<string, string> Tags(JsonElement tags)
    {
        Require.That(tags.ValueKind == JsonValueKind.Array && tags.GetArrayLength() <= 256, "report-tags");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags.EnumerateArray())
            Require.That(result.TryAdd(EvidenceJson.Text(tag, "key"), EvidenceJson.Text(tag, "value")), "report-tag-duplicate");
        return result;
    }

    private static void Summary(JsonElement summary, int count)
    {
        EvidenceJson.Shape(summary, "total", "passed", "failed", "skipped", "cancelled", "timedOut", "flaky");
        foreach (var property in summary.EnumerateObject())
            Require.That(property.Value.GetInt32() == (property.Name is "total" or "passed" ? count : 0),
                "report-summary");
    }

    private static void Interval(JsonElement value, string startKey, string endKey, ChildExecution process)
    {
        var start = EvidenceJson.Instant(value, startKey);
        var end = EvidenceJson.Instant(value, endKey);
        Require.That(start >= process.StartedAt && end >= start && end <= process.CompletedAt, "report-time");
    }

    private static void SpanInterval(JsonElement span, ChildExecution process)
    {
        var start = span.GetProperty("startTimeMs").GetDouble();
        var duration = span.GetProperty("durationMs").GetDouble();
        Require.That(double.IsFinite(start) && double.IsFinite(duration) && duration >= 0
            && start >= process.StartedAt.ToUnixTimeMilliseconds()
            && start + duration < process.CompletedAt.ToUnixTimeMilliseconds() + 1, "report-span-time");
    }

    private sealed record MtpResults(string RunId, Dictionary<string, (string TestId, string ExecutionId)> Cases);

    private static MtpResults ReadTrx(
        byte[] bytes, ModerationMeasurementCatalogueJob job, ChildExecution process)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = OwnedExecution.MaximumReportBytes
        });
        var document = XDocument.Load(reader);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var root = document.Root ?? throw new MeasurementFailure("mtp-missing-root");
        Require.That(root.Name == ns + "TestRun", "mtp-schema");
        var runId = RequiredAttribute(root, "id");
        Require.That(Guid.TryParse(runId, out var nativeRun) && nativeRun != Guid.Empty, "mtp-run-identity");
        var summary = root.Elements(ns + "ResultSummary").Single();
        Require.That((string?)summary.Attribute("outcome") is "Completed" or "Passed", "mtp-unsuccessful");
        var counters = summary.Elements(ns + "Counters").Single();
        foreach (var attribute in counters.Attributes())
        {
            Require.That(int.TryParse(attribute.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number == (attribute.Name.LocalName is "total" or "executed" or "passed" ? job.Cases.Length : 0),
                "mtp-counters");
        }
        foreach (var required in new[] { "total", "executed", "passed", "failed", "notExecuted" })
            Require.That(counters.Attribute(required) is not null, "mtp-counters-missing");
        Require.That(!summary.Descendants(ns + "RunInfo").Any()
            && !root.Descendants(ns + "ErrorInfo").Any(), "mtp-error-info");
        var definitions = root.Elements(ns + "TestDefinitions").Single().Elements().ToArray();
        var terminals = root.Elements(ns + "Results").Single().Elements().ToArray();
        var entries = root.Elements(ns + "TestEntries").Single().Elements().ToArray();
        Require.That(definitions.Length == job.Cases.Length && terminals.Length == job.Cases.Length
            && entries.Length == job.Cases.Length
            && terminals.All(element => element.Name == ns + "UnitTestResult")
            && definitions.All(element => element.Name == ns + "UnitTest")
            && entries.All(element => element.Name == ns + "TestEntry"), "mtp-case-count");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var executions = new HashSet<string>(StringComparer.Ordinal);
        var result = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var expected in job.Cases)
        {
            var definition = definitions.Single(element => (string?)element.Attribute("name") == expected.MtpTestName
                && (string?)element.Element(ns + "TestMethod")?.Attribute("className") == expected.MtpClassName
                && (string?)element.Element(ns + "TestMethod")?.Attribute("name") == expected.MtpMethodName);
            var id = RequiredAttribute(definition, "id");
            var executionId = RequiredAttribute(definition.Elements(ns + "Execution").Single(), "id");
            Require.That(ids.Add(id) && executions.Add(executionId), "mtp-duplicate-identity");
            var terminal = terminals.Single(element => (string?)element.Attribute("testId") == id
                && (string?)element.Attribute("executionId") == executionId);
            Require.That(RequiredAttribute(terminal, "outcome") == "Passed"
                && RequiredAttribute(terminal, "testName") == expected.MtpTestName
                && entries.Count(element => (string?)element.Attribute("testId") == id
                    && (string?)element.Attribute("executionId") == executionId) == 1, "mtp-terminal-mismatch");
            var start = EvidenceJson.Utc(RequiredAttribute(terminal, "startTime"));
            var end = EvidenceJson.Utc(RequiredAttribute(terminal, "endTime"));
            Require.That(start >= process.StartedAt && end >= start && end <= process.CompletedAt, "mtp-time");
            result.Add(expected.CaseId, (id, executionId));
        }
        return new(runId, result);
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        var value = (string?)element.Attribute(name);
        Require.That(value is not null && Require.Identity(value, 4096), "mtp-attribute");
        return value!;
    }
}

internal static class EvidenceJson
{
    internal static void UniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Require.That(names.Add(property.Name), "json-duplicate-field");
                UniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) UniqueProperties(item);
    }

    internal static void Shape(JsonElement element, params string[] names)
    {
        Require.That(element.ValueKind == JsonValueKind.Object
            && element.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(names)
            && element.EnumerateObject().Count() == names.Length, "json-shape");
    }

    internal static string Text(JsonElement element, string name)
    {
        var value = element.GetProperty(name).GetString();
        Require.That(value is not null && Require.Identity(value, 4096), "json-identity");
        return value!;
    }

    internal static DateTimeOffset Instant(JsonElement element, string name) => Utc(Text(element, name));

    internal static DateTimeOffset Utc(string value)
    {
        Require.That(DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            && result.Offset == TimeSpan.Zero && result != DateTimeOffset.MinValue && result != DateTimeOffset.MaxValue
            && (value.EndsWith("+00:00", StringComparison.Ordinal) || value.EndsWith('Z')), "evidence-time-invalid");
        return result;
    }
}
