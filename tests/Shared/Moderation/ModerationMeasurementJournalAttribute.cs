using System.Text.Json;
using TUnit.Core;
using TUnit.Core.Enums;
using TUnit.Core.Interfaces;

[assembly: Event.Tests.Shared.Moderation.ModerationMeasurementJournal(EventReceiverStage.Early)]
[assembly: Event.Tests.Shared.Moderation.ModerationMeasurementJournal(EventReceiverStage.Late)]

namespace Event.Tests.Shared.Moderation;

/// <summary>
/// Invocation-owned evidence, not a verdict or signing interface. The parent must create a
/// fresh directory named with its RunId (Guid N format), inject these three non-secret
/// variables into each child, and retain that invocation's native spans and MTP results.
/// An absent mode variable is inert. No signing material is read by this receiver.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ModerationMeasurementJournalAttribute(EventReceiverStage stage) : Attribute,
    ITestStartEventReceiver, ITestEndEventReceiver, ITestSkippedEventReceiver, ITestRetryEventReceiver
{
    public const string ModeEnvironmentVariable = "ISLAMU_MODERATION_MEASUREMENT_JOURNAL";
    public const string RunIdEnvironmentVariable = "ISLAMU_MODERATION_MEASUREMENT_RUN_ID";
    public const string RunDirectoryEnvironmentVariable = "ISLAMU_MODERATION_MEASUREMENT_RUN_DIRECTORY";
    public const int SchemaVersion = 2;
    public const string SupportedTUnitVersion = "1.63.0";

    private const int MaximumTests = 100_000;
    private const int MaximumAttempts = 1_000;
    private const int MaximumIdentityLength = 4_096;
    private const long MaximumRecords = 2_000_000;
    private const int MaximumDatabaseWitnesses = 8;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Observation> Observations = new(StringComparer.Ordinal);
    private static FileStream? journal;
    private static string? runId;
    private static long sequence;
    private static bool enabled;
    private static bool initialized;
    private static bool completed;
    private static bool faulted;

    public int Order => stage == EventReceiverStage.Early ? int.MinValue : int.MaxValue;
    public EventReceiverStage Stage => stage;

    /// <summary>
    /// Retains the database facts observed by this test's native database owner after executing
    /// against that connection. The launcher independently requires the observation to lie inside
    /// the actual test-body span and to match the selected database's public configuration.
    /// </summary>
    public static void RecordDatabaseObservation(
        TestContext context, string providerCode, string providerVersion, string configurationDigest)
    {
        lock (Gate)
        {
            if (!enabled) return;
            EnsureWritable();
            try
            {
                if (providerCode is not ("PostgreSql" or "Sqlite" or "SqlServer" or "MariaDb" or "MySql")
                    || string.IsNullOrWhiteSpace(providerVersion) || providerVersion.Length > 128
                    || providerVersion.Any(value => value is < ' ' or > '~')
                    || configurationDigest.Length != 64
                    || configurationDigest.Any(value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                    || !Observations.TryGetValue(context.Metadata.TestDetails.Identity.TestId, out var observation)
                    || observation.Context.Id != context.Id)
                    throw new InvalidOperationException("Measurement database observation binding is invalid.");
                if (observation.Databases.Any(value => value.ProviderCode == providerCode
                    && value.ProviderVersion == providerVersion && value.ConfigurationDigest == configurationDigest))
                    return;
                if (observation.Databases.Count >= MaximumDatabaseWitnesses)
                    throw new InvalidOperationException("Measurement database observation bound exceeded.");
                observation.Databases.Add(new(providerCode, providerVersion, configurationDigest, DateTimeOffset.UtcNow));
            }
            catch
            {
                faulted = true;
                throw;
            }
        }
    }

    public ValueTask OnTestStart(TestContext context) => stage == EventReceiverStage.Early
        ? Observe("test-start", context) : ValueTask.CompletedTask;

    public ValueTask OnTestEnd(TestContext context) => Observe(
        stage == EventReceiverStage.Early ? "test-end-early" : "test-end-late", context);

    public ValueTask OnTestSkipped(TestContext context) => stage == EventReceiverStage.Early
        ? Observe("test-skipped", context) : ValueTask.CompletedTask;

    public ValueTask OnTestRetry(TestContext context, int retryAttempt) => stage == EventReceiverStage.Early
        ? Observe("test-retry", context, retryAttempt) : ValueTask.CompletedTask;

    /// <summary>Creates this child's journal before test execution, including zero-test sessions.</summary>
    [Before(HookType.TestSession, Order = int.MinValue)]
    public static void BeginSession()
    {
        lock (Gate)
        {
            var mode = Environment.GetEnvironmentVariable(ModeEnvironmentVariable);
            if (mode is null)
            {
                return;
            }

            if (initialized)
            {
                throw new InvalidOperationException("Measurement journal session was already initialized.");
            }

            initialized = true;
            enabled = true;
            try
            {
                if (mode != "1")
                {
                    throw new InvalidOperationException("Unsupported measurement journal mode.");
                }

                var suppliedRunId = Environment.GetEnvironmentVariable(RunIdEnvironmentVariable);
                var directory = Environment.GetEnvironmentVariable(RunDirectoryEnvironmentVariable);
                if (!Guid.TryParseExact(suppliedRunId, "N", out var parsedRunId)
                    || parsedRunId == Guid.Empty || suppliedRunId != parsedRunId.ToString("N")
                    || string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)
                    || !Directory.Exists(directory)
                    || Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)) != suppliedRunId)
                {
                    throw new InvalidOperationException("Measurement journal invocation binding is invalid.");
                }

                // The package version is pinned; the installed assembly uses a four-part version.
                if (typeof(TestContext).Assembly.GetName().Version != new Version(1, 63, 0, 0))
                {
                    throw new InvalidOperationException("Unsupported measurement journal TUnit version.");
                }

                runId = suppliedRunId;
                var bundle = typeof(ModerationMeasurementJournalAttribute).Assembly.GetName().Name
                    ?? throw new InvalidOperationException("Measurement bundle identity is missing.");
                journal = new FileStream(Path.Combine(directory, bundle + ".lifecycle.jsonl"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                Write("header", null, null, null, null);
            }
            catch
            {
                faulted = true;
                journal?.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// A completion marker means this journal's session hook completed, not that tests passed.
    /// The launcher must also require successful process/MTP termination and exact span coverage.
    /// </summary>
    [After(HookType.TestSession, Order = int.MaxValue)]
    public static void CompleteSession(TestSessionContext session)
    {
        lock (Gate)
        {
            if (!enabled)
            {
                return;
            }

            EnsureWritable();
            try
            {
                foreach (var observation in Observations.Values)
                {
                    Capture("test-terminal", observation.Context, observation, null);
                }

                Write("footer", null, null, null, session);
                journal!.Flush(flushToDisk: true);
                completed = true;
            }
            catch
            {
                faulted = true;
                throw;
            }
            finally
            {
                journal!.Dispose();
            }
        }
    }

    private static ValueTask Observe(string kind, TestContext context, int? retryAttempt = null)
    {
        lock (Gate)
        {
            if (!enabled)
            {
                return ValueTask.CompletedTask;
            }

            EnsureWritable();
            try
            {
                var testId = context.Metadata.TestDetails.Identity.TestId;
                ValidateIdentity(testId);
                ValidateIdentity(context.Id);
                if (!Observations.TryGetValue(testId, out var observation))
                {
                    if (Observations.Count >= MaximumTests)
                    {
                        throw new InvalidOperationException("Measurement journal test bound exceeded.");
                    }

                    observation = new Observation(context);
                    Observations.Add(testId, observation);
                }
                else if (observation.Context.Id != context.Id)
                {
                    throw new InvalidOperationException("Measurement journal duplicate test identity.");
                }

                Capture(kind, context, observation, retryAttempt);
            }
            catch
            {
                faulted = true;
                throw;
            }
        }

        return ValueTask.CompletedTask;
    }

    private static void Capture(string kind, TestContext context, Observation observation, int? retryAttempt)
    {
        var execution = context.Execution;
        if (execution.CurrentRetryAttempt < 0 || execution.CurrentRetryAttempt >= MaximumAttempts
            || execution.RetryAttempts.Count >= MaximumAttempts
            || retryAttempt is < 1 or >= MaximumAttempts)
        {
            throw new InvalidOperationException("Measurement journal attempt bound exceeded.");
        }

        // Native Activity tags are optional. Never substitute Context.Id for node_uid or
        // synthesize a session ID. Preserve the first available tag and reject conflicts.
        observation.NodeUid = CaptureTag(context, "tunit.test.node_uid", observation.NodeUid);
        observation.SessionId = CaptureTag(context, "tunit.session.id", observation.SessionId);
        observation.SpanTestId = CaptureTag(context, "tunit.test.id", observation.SpanTestId);

        // Each snapshot is appended, never replacing earlier observations. Retain every
        // prior attempt even when a later hook has overridden the surviving result.
        for (var index = 0; index < execution.RetryAttempts.Count; index++)
        {
            var prior = execution.RetryAttempts[index];
            observation.FailureObserved |= HasFailure(prior);
            Write("prior-attempt", observation, prior, index, null);
        }

        observation.FailureObserved |= retryAttempt.HasValue || HasFailure(execution.Result);
        Write(kind, observation, execution.Result, execution.CurrentRetryAttempt, null, retryAttempt);
    }

    private static bool HasFailure(TestResult? result) => result is not null
        && (result.State is TestState.Failed or TestState.Timeout or TestState.Cancelled
            || result.Exception is not null || result.OriginalException is not null);

    private static void EnsureWritable()
    {
        if (!initialized || journal is null || completed || faulted)
        {
            throw new InvalidOperationException("Measurement journal lifecycle is incomplete or closed.");
        }
    }

    private static void ValidateIdentity(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity) || identity.Length > MaximumIdentityLength)
        {
            throw new InvalidOperationException("Measurement journal native identity is invalid.");
        }
    }

    private static string? CaptureTag(TestContext context, string name, string? previous)
    {
        var value = context.Activity?.GetTagItem(name);
        if (value is null)
        {
            return previous;
        }

        if (value is not string identity)
        {
            throw new InvalidOperationException("Unsupported measurement journal native tag type.");
        }

        ValidateIdentity(identity);
        if (previous is not null && previous != identity)
        {
            throw new InvalidOperationException("Measurement journal native tag identity changed.");
        }

        return identity;
    }

    private static string ErrorCategory(Exception? exception) => exception switch
    {
        null => "none",
        OperationCanceledException => "cancellation",
        TimeoutException => "timeout",
        AggregateException => "aggregate",
        _ => "other"
    };

    private static string State(TestResult? result) => result?.State switch
    {
        null => "unavailable",
        TestState.NotStarted => "not-started",
        TestState.WaitingForDependencies => "waiting-for-dependencies",
        TestState.Queued => "queued",
        TestState.Running => "running",
        TestState.Passed => "passed",
        TestState.Failed => "failed",
        TestState.Skipped => "skipped",
        TestState.Timeout => "timeout",
        TestState.Cancelled => "cancelled",
        _ => throw new InvalidOperationException("Unsupported measurement journal test state.")
    };

    private static void Write(string kind, Observation? observation, TestResult? result,
        int? attemptIndex, TestSessionContext? session, int? retryAttempt = null)
    {
        if (sequence >= MaximumRecords)
        {
            throw new InvalidOperationException("Measurement journal record bound exceeded.");
        }

        // Write a whole bounded record under Gate and flush before returning from the hook.
        // A torn record or any write failure leaves a partial journal; it never yields a footer.
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("schemaVersion", SchemaVersion);
            json.WriteString("tunitVersion", SupportedTUnitVersion);
            json.WriteString("runId", runId);
            json.WriteNumber("sequence", sequence);
            json.WriteString("event", kind);
            json.WriteString("bundle", typeof(ModerationMeasurementJournalAttribute).Assembly.GetName().Name);
            json.WriteString("utc", DateTimeOffset.UtcNow);
            // Null native tags require a verified join with same-invocation test-case spans.
            // No equality between contextId, testId and spanTestId is assumed here.
            json.WriteString("nodeUid", observation?.NodeUid);
            json.WriteString("sessionId", observation?.SessionId);
            if (observation is not null)
            {
                json.WriteString("testId", observation.Context.Metadata.TestDetails.Identity.TestId);
                json.WriteString("contextId", observation.Context.Id);
                json.WriteString("spanTestId", observation.SpanTestId);
                json.WriteNumber("attemptIndex", attemptIndex!.Value);
                json.WriteNumber("currentRetryAttempt", observation.Context.Execution.CurrentRetryAttempt);
                json.WriteNumber("priorAttemptCount", observation.Context.Execution.RetryAttempts.Count);
                if (retryAttempt.HasValue)
                {
                    json.WriteNumber("retryAttempt", retryAttempt.Value);
                    json.WriteNumber("triggeringAttemptIndex", retryAttempt.Value - 1);
                }

                json.WriteString("state", State(result));
                json.WriteString("errorCategory", ErrorCategory(result?.Exception));
                json.WriteString("originalErrorCategory", ErrorCategory(result?.OriginalException));
                json.WriteBoolean("originalFailurePresent", HasFailure(result));
                json.WriteBoolean("originalExceptionPresent", result?.OriginalException is not null);
                json.WriteBoolean("resultOverridden", result?.IsOverridden == true);
                json.WriteBoolean("failureObserved", observation.FailureObserved);
                if (kind == "test-terminal")
                {
                    json.WriteStartArray("databaseWitnesses");
                    foreach (var database in observation.Databases)
                    {
                        json.WriteStartObject();
                        json.WriteString("providerCode", database.ProviderCode);
                        json.WriteString("providerVersion", database.ProviderVersion);
                        json.WriteString("configurationDigest", database.ConfigurationDigest);
                        json.WriteString("observedAt", database.ObservedAt);
                        json.WriteEndObject();
                    }
                    json.WriteEndArray();
                }
            }

            if (session is not null)
            {
                json.WriteBoolean("lifecycleCompleted", true);
                json.WriteBoolean("sessionHasFailures", session.AllTests.Any(test => HasFailure(test.Execution.Result)));
                json.WriteBoolean("sessionCancelled", session.SessionCancellationToken.IsCancellationRequested);
                json.WriteNumber("observedTestCount", Observations.Count);
                json.WriteBoolean("failureObserved", Observations.Values.Any(value => value.FailureObserved));
            }

            json.WriteEndObject();
        }

        journal!.Write(buffer.GetBuffer(), 0, checked((int)buffer.Length));
        journal.WriteByte((byte)'\n');
        journal.Flush(flushToDisk: true);
        sequence++;
    }

    private sealed class Observation(TestContext context)
    {
        public TestContext Context { get; } = context;
        public bool FailureObserved { get; set; }
        public string? NodeUid { get; set; }
        public string? SessionId { get; set; }
        public string? SpanTestId { get; set; }
        public List<DatabaseObservation> Databases { get; } = [];
    }

    private sealed record DatabaseObservation(
        string ProviderCode, string ProviderVersion, string ConfigurationDigest, DateTimeOffset ObservedAt);
}
