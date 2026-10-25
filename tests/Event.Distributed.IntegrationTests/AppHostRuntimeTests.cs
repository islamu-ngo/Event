using Explore.AppHost;
using Aspire.Hosting.ApplicationModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace Event.Distributed.IntegrationTests;

[Category("Distributed")]
public sealed partial class AppHostRuntimeTests
{
    [Test]
    public async Task ManagedResourcePidRequiresExactResourceInstanceAndTypedProperty()
    {
        var selected = new ProjectResource("explore-api");
        var foreign = new ProjectResource("explore-api");
        bool Accepted(IResource resource, string? instance, ImmutableArray<ResourcePropertySnapshot> properties) =>
            OwnedAppHostFixture.ManagedResourceLaunchObservation.TryGetPid([selected], resource, instance, properties, out int pid)
                && pid == 37;
        await Assert.That(Accepted(selected, "owned-instance", [new("executable.pid", 37)])).IsTrue();
        await Assert.That(Accepted(foreign, "owned-instance", [new("executable.pid", 37)])).IsFalse();
        await Assert.That(Accepted(selected, null, [new("executable.pid", 37)])).IsFalse();
        foreach (object value in new object[] { "37", 37L, 0, -1, new ThrowOnShapeFormatting() })
            await Assert.That(Accepted(selected, "owned-instance", [new("executable.pid", value)])).IsFalse();
        await Assert.That(Accepted(selected, "owned-instance", [new("executable.pid", 37) { IsSensitive = true }])).IsFalse();
        await Assert.That(Accepted(selected, "owned-instance", [new("executable.pid", 37), new("executable.pid", 37)])).IsFalse();
        await Assert.That(Accepted(selected, "owned-instance", [new("pid", 37)])).IsFalse();
    }

    [Test]
    public async Task ManagedEntryUsesOnlyClosedObservedArgumentSlots()
    {
        string root = Path.GetFullPath(Path.GetTempPath());
        string entry = Path.Combine(root, "managed-entry.dll");
        string executable = Path.Combine(root, "dotnet");
        await Assert.That(OwnedAppHostFixture.ManagedResourceLaunchObservation.ParseManagedEntry(
            $"dotnet\0{entry}\0application-argument.dll\0", root, executable) == entry).IsTrue();
        await Assert.That(OwnedAppHostFixture.ManagedResourceLaunchObservation.ParseManagedEntry(
            "dotnet\0exec\0managed-entry.dll\0", root, executable) == entry).IsTrue();
        foreach (string command in new[]
        {
            "dotnet\0--depsfile\0other.dll\0managed-entry.dll\0",
            "dotnet\0exec\0--runtimeconfig\0other.json\0managed-entry.dll\0",
            "dotnet\0run\0--project\0managed-entry.dll\0", "apphost\0managed-entry.dll\0",
            "dotnet\0\0managed-entry.dll\0", "dotnet\0exec\0\0managed-entry.dll\0",
            "dotnet\0managed-entry.dll", "dotnet\0exec\0"
        })
            await Assert.That(() => OwnedAppHostFixture.ManagedResourceLaunchObservation.ParseManagedEntry(command, root, executable))
                .Throws<InvalidOperationException>();
        await Assert.That(() => OwnedAppHostFixture.ManagedResourceLaunchObservation.ParseManagedEntry(
            "dotnet\0managed-entry.dll\0", root, Path.Combine(root, "foreign-host"))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ManagedProcessJoinCalibratesLiveParentAndRejectsIdentityReplacement()
    {
        await Assert.That(OperatingSystem.IsLinux()).IsTrue();
        if (!OperatingSystem.IsLinux()) return;
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        OwnedAppHostFixture.ManagedProcessInputs observed;
        string stage = "identity";
        try
        {
            var current = await OwnedAppHostFixture.ManagedResourceLaunchObservation.ReadIdentityAsync(Environment.ProcessId, token);
            stage = "inputs";
            observed = await OwnedAppHostFixture.ManagedResourceLaunchObservation.ReadProcessInputsAsync(Environment.ProcessId, current.Parent, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            string reason = OwnedAppHostFixture.ManagedResourceLaunchObservation.FailureReason(exception);
            throw new InvalidOperationException($"test_managed_process_calibration_failed:{stage}:{reason}");
        }
        await Assert.That(observed.Ancestry.Length == 2 && observed.Ancestry[0].Pid == Environment.ProcessId
            && observed.Entry == System.Reflection.Assembly.GetEntryAssembly()?.Location && observed.Files.Length == 4).IsTrue();
        var identity = observed.Ancestry[0];
        await Assert.That(OwnedAppHostFixture.ManagedResourceLaunchObservation.SameAncestry(
            [identity with { State = 'R' }], [identity with { State = 'S' }])).IsTrue();
        foreach (var replacement in new[]
        {
            identity with { Start = identity.Start + 1 }, identity with { Parent = identity.Parent + 1 },
            identity with { User = identity.User + 1 }, identity with { Session = identity.Session + 1 },
            identity with { Group = identity.Group + 1 }, identity with { State = 'Z' }
        })
            await Assert.That(OwnedAppHostFixture.ManagedResourceLaunchObservation.SameAncestry([identity], [replacement])).IsFalse();
        foreach (var replacement in new[]
        {
            observed with { Executable = observed.Executable + "-replaced" },
            observed with { WorkingDirectory = observed.WorkingDirectory + "-replaced" },
            observed with { Entry = observed.Entry + "-replaced" },
            observed with { Files = [observed.Files[0] with { LastWriteUtcTicks = observed.Files[0].LastWriteUtcTicks + 1 }, .. observed.Files.Skip(1)] }
        })
            await Assert.That(OwnedAppHostFixture.ManagedResourceLaunchObservation.SameProcessInputs(observed, replacement)).IsFalse();
        await Assert.That(async () => await OwnedAppHostFixture.ManagedResourceLaunchObservation.ReadProcessInputsAsync(
            Environment.ProcessId, Environment.ProcessId, token)).Throws<InvalidOperationException>();
        await Assert.That(async () => await OwnedAppHostFixture.ManagedResourceLaunchObservation.ReadProcessInputsAsync(
            observed.Ancestry[0].Parent, Environment.ProcessId, token)).Throws<InvalidOperationException>();
        await Assert.That(async () => await OwnedAppHostFixture.ManagedResourceLaunchObservation.ReadProcessInputsAsync(
            int.MaxValue, Environment.ProcessId, token)).Throws<IOException>();
    }

    [Test]
    public async Task ManagedFileBracketRejectsSameBytesWithChangedModificationStamp()
    {
        await using var owner = new OwnedAppHostFixture("Split");
        string path = Path.Combine(owner.Root, "managed-input.bin");
        await File.WriteAllBytesAsync(path, RandomNumberGenerator.GetBytes(64));
        var before = await OwnedAppHostFixture.ManagedResourceLaunchObservation.HashInputAsync(path, "entry", "before", CancellationToken.None);
        File.SetLastWriteTimeUtc(path, new DateTime(before.LastWriteUtcTicks, DateTimeKind.Utc).AddSeconds(10));
        var after = await OwnedAppHostFixture.ManagedResourceLaunchObservation.HashInputAsync(path, "entry", "after", CancellationToken.None);
        await Assert.That(before.Bytes == after.Bytes && before.Sha256 == after.Sha256 && before != after).IsTrue();
        var missing = await Assert.That(async () => await OwnedAppHostFixture.ManagedResourceLaunchObservation.HashInputAsync(
            Path.Combine(owner.Root, "missing.bin"), "deps", "before", CancellationToken.None)).Throws<InvalidOperationException>();
        await Assert.That(missing!.Message == "test_managed_input_invalid:deps:before:missing:file:0").IsTrue();
        string fileLink = Path.Combine(owner.Root, "linked-input.bin");
        File.CreateSymbolicLink(fileLink, path);
        var linked = await Assert.That(async () => await OwnedAppHostFixture.ManagedResourceLaunchObservation.HashInputAsync(
            fileLink, "entry", "after", CancellationToken.None)).Throws<InvalidOperationException>();
        await Assert.That(linked!.Message == "test_managed_input_invalid:entry:after:link:file:0").IsTrue();
        string directoryLink = Path.Combine(owner.Root, "linked-directory");
        Directory.CreateSymbolicLink(directoryLink, owner.Root);
        var ancestor = await Assert.That(async () => await OwnedAppHostFixture.ManagedResourceLaunchObservation.HashInputAsync(
            Path.Combine(directoryLink, "managed-input.bin"), "runtimeconfig", "before", CancellationToken.None)).Throws<InvalidOperationException>();
        await Assert.That(ancestor!.Message == "test_managed_input_invalid:runtimeconfig:before:link:ancestor:1").IsTrue();
    }

    [Test]
    public async Task ManagedObserverArmsBeforeSynchronousStartFailureAndJoinsShutdown()
    {
        using var startup = new CancellationTokenSource();
        var primary = new InvalidOperationException("test_managed_start_failed");
        bool armed = false, joined = false, stopped = false, firstFailureObserved = false;
        async Task Observe(CancellationToken token)
        {
            armed = true;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { joined = true; }
        }
        var failure = await Assert.That(async () => await OwnedAppHostFixture.RunObservedStartupAsync(Observe,
            _ => { if (!armed) throw new InvalidOperationException("test_observer_not_armed"); throw primary; },
            _ => Task.CompletedTask, () => stopped = true,
            (phase, reason, requested) => firstFailureObserved = phase == "application_start_call"
                && reason == "guard_unclassified" && !requested && !stopped && !startup.IsCancellationRequested,
            startup)).Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(failure, primary) && joined && stopped && firstFailureObserved
            && startup.IsCancellationRequested).IsTrue();
        foreach (bool cancelCaller in new[] { false, true })
        {
            using var caller = new CancellationTokenSource();
            using var independent = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller.Token);
            var cancellation = new OperationCanceledException(cancelCaller ? linked.Token : independent.Token);
            var releaseObserver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool sampledBeforeStop = false, cancellationStopped = false;
            var canceled = await Assert.That(async () => await OwnedAppHostFixture.RunObservedStartupAsync(
                async token =>
                {
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    { await releaseObserver.Task; throw; }
                }, _ => Task.CompletedTask,
                async _ =>
                {
                    if (cancelCaller) await caller.CancelAsync();
                    else await independent.CancelAsync();
                    throw cancellation;
                }, () => { cancellationStopped = true; releaseObserver.TrySetResult(); },
                (phase, reason, requested) => sampledBeforeStop = phase == "readiness_wait"
                    && reason == (cancelCaller ? "canceled_linked" : "canceled_other")
                    && requested == cancelCaller && caller.IsCancellationRequested == cancelCaller
                    && !cancellationStopped,
                linked)).Throws<OperationCanceledException>();
            await Assert.That(ReferenceEquals(canceled, cancellation) && sampledBeforeStop
                && cancellationStopped && linked.IsCancellationRequested).IsTrue();
        }
    }

    [Test]
    public async Task ManagedObserverRetainsPrimaryIndependentFaultAndCancellationCallbackFailure()
    {
        using var startup = new CancellationTokenSource();
        var primary = new InvalidOperationException("test_managed_start_failed");
        var observerFailure = new InvalidOperationException("test_managed_observer_failed");
        var cancellationFailure = new InvalidOperationException("test_managed_cancellation_failed");
        var diagnosticFailure = new InvalidOperationException("test_managed_diagnostic_failed");
        var proofFailure = new InvalidOperationException("test_resource_cleanup_proof_failed");
        using var registration = startup.Token.Register(() => throw cancellationFailure);
        bool joined = false, stopped = false, proofPrecededStop = false;
        async Task Observe(CancellationToken token)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw observerFailure; }
            finally { joined = true; }
        }
        var failure = await Assert.That(async () => await OwnedAppHostFixture.RunObservedStartupAsync(Observe,
            _ => throw primary, _ => Task.CompletedTask, () => stopped = true,
            (_, _, _) => throw diagnosticFailure, startup, beforeStop: () =>
            {
                proofPrecededStop = !stopped;
                return Task.FromException(proofFailure);
            })).Throws<AggregateException>();
        var causes = failure!.Flatten().InnerExceptions;
        await Assert.That(joined && stopped && proofPrecededStop && causes.Any(item => ReferenceEquals(item, primary))
            && causes.Any(item => ReferenceEquals(item, observerFailure))
            && causes.Any(item => ReferenceEquals(item, cancellationFailure))
            && causes.Any(item => ReferenceEquals(item, diagnosticFailure))
            && causes.Any(item => ReferenceEquals(item, proofFailure))).IsTrue();
        string sentinel = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var knownDenial = new InvalidOperationException("test_resource_container_identity_unknown:postgres:candidate-unproved");
        var unknownDenial = new InvalidOperationException(sentinel);
        var original = new AggregateException(knownDenial, unknownDenial);
        var denials = new[]
        {
            new OwnedAppHostFixture.CleanupProofDenial("postgres", "completed-proof", knownDenial, true),
            new OwnedAppHostFixture.CleanupProofDenial(sentinel, sentinel, unknownDenial, false)
        };
        var closed = OwnedAppHostFixture.CreateCleanupProofFailure(new HashSet<string> { "postgres" }, denials,
            true, TaskStatus.Faulted, original);
        await Assert.That(ReferenceEquals(closed.InnerException, original)
            && original.InnerExceptions.Any(item => ReferenceEquals(item, knownDenial))
            && original.InnerExceptions.Any(item => ReferenceEquals(item, unknownDenial))
            && closed.Message.StartsWith("test_resource_cleanup_proof_incomplete:budget=1:observer=faulted:", StringComparison.Ordinal)
            && closed.Message.Contains("postgres/completed-proof/identity-candidate-unproved/model-equal=1", StringComparison.Ordinal)
            && closed.Message.Contains("unknown/unknown/guard-unknown/model-equal=0", StringComparison.Ordinal)
            && !closed.Message.Contains(sentinel, StringComparison.Ordinal)).IsTrue();
        var bounded = OwnedAppHostFixture.CreateCleanupProofFailure(new HashSet<string> { "postgres" },
            Enumerable.Repeat(denials[0], 17).ToArray(), false, TaskStatus.WaitingForActivation, original);
        await Assert.That(bounded.Message.EndsWith(":omitted=1", StringComparison.Ordinal)
            && bounded.Message.Split("postgres/completed-proof/identity-candidate-unproved/model-equal=1", StringSplitOptions.None).Length == 17
            && bounded.Message.Length < 4096 && ReferenceEquals(bounded.InnerException, original)).IsTrue();

        var resource = new ContainerResource("postgres");
        var absent = OwnedAppHostFixture.ClassifyContainerInspection(resource, "observed-instance", 1, "no such container");
        var available = OwnedAppHostFixture.ClassifyContainerInspection(resource, "observed-instance", 0, "no such container");
        await Assert.That(absent.Outcome == OwnedAppHostFixture.ContainerInspectionOutcome.Absent
            && available.Outcome == OwnedAppHostFixture.ContainerInspectionOutcome.Available).IsTrue();
        var inspectionFailure = await Assert.That(() =>
        {
            _ = OwnedAppHostFixture.ClassifyContainerInspection(resource, "observed-instance", 1, "permission denied");
        }).Throws<InvalidOperationException>();
        await Assert.That(inspectionFailure!.Message).IsEqualTo("test_resource_container_identity_unknown:postgres:native-inspection");

        OwnedAppHostFixture.RequireVolumeConsumerInspectionSucceeded(0, "no such container: permission denied");
        (string Error, string Category)[] inspectionErrors =
        [
            ("", "empty"),
            (" \t\n", "empty"),
            ("no such container", "no-such-marker"),
            ("PeRmIsSiOn DeNiEd", "permission-marker"),
            ("access denied; permission denied", "permission-marker"),
            ("cannot connect", "connection-marker"),
            ("connection refused; connection reset", "connection-marker"),
            ("no such container; permission denied", "multiple-markers"),
            (sentinel, "other"),
            (new string('x', 8193) + " permission denied " + sentinel, "oversize")
        ];
        InvalidOperationException? bulkInspectionFailure = null;
        foreach (var observation in inspectionErrors)
        {
            var classified = await Assert.That(() =>
                OwnedAppHostFixture.RequireVolumeConsumerInspectionSucceeded(125, observation.Error)).Throws<InvalidOperationException>();
            await Assert.That(classified!.Message == "test_resource_volume_consumers_unknown:stage=bulk-inspect:exit=125:stderr=" + observation.Category
                && !classified.Message.Contains(sentinel, StringComparison.Ordinal)).IsTrue();
            if (observation.Category == "permission-marker") bulkInspectionFailure ??= classified;
        }
        var boundedExit = await Assert.That(() =>
            OwnedAppHostFixture.RequireVolumeConsumerInspectionSucceeded(int.MinValue, sentinel)).Throws<InvalidOperationException>();
        await Assert.That(boundedExit!.Message)
            .IsEqualTo("test_resource_volume_consumers_unknown:stage=bulk-inspect:exit=-2147483648:stderr=other");
        var consumerReadFailures = await VerifyCompleteConsumerReobservationAsync(sentinel);

        string initialId = new('a', 64), extraId = new('b', 64);
        var baseline = (Containers: (IReadOnlySet<string>)new HashSet<string> { initialId },
            Volumes: (IReadOnlySet<string>)new HashSet<string> { "existing-volume" });
        var equalInventory = new OwnedAppHostFixture.CleanupInventory(0, initialId, 0, "existing-volume");
        var extraContainer = equalInventory with { Containers = initialId + "\n" + extraId };
        var originalGuard = new InvalidOperationException("test_resource_container_identity_unknown:postgres:owner-label");
        (IResource Resource, string Instance)[] exactJoin = [(resource, "observed-instance")];
        (IResource Resource, string Instance)[] wrongInstance = [(resource, "other-instance")];
        (IResource Resource, string Instance)[] wrongReference = [(new ContainerResource("postgres"), "observed-instance")];
        (string Name, OwnedAppHostFixture.CleanupInventory First, OwnedAppHostFixture.CleanupInventory Second,
            bool Nonterminal, Exception? Original, OwnedAppHostFixture.ContainerInspectionObservation? Inspection,
            (IResource Resource, string Instance)[] Joined)[] scenarios =
        [
            ("equal", equalInventory, equalInventory, false, null, absent, []),
            ("container-status", equalInventory with { ContainerExitCode = 1 }, equalInventory, false, null, absent, []),
            ("volume-status", equalInventory with { VolumeExitCode = 1 }, equalInventory, false, null, absent, []),
            ("container-format", equalInventory with { Containers = "invalid" }, equalInventory, false, null, absent, []),
            ("volume-format", equalInventory with { Volumes = "invalid/name" }, equalInventory, false, null, absent, []),
            ("container-duplicate", equalInventory with { Containers = initialId + "\n" + initialId }, equalInventory, false, null, absent, []),
            ("volume-duplicate", equalInventory with { Volumes = "existing-volume\nexisting-volume" }, equalInventory, false, null, absent, []),
            ("container-extra", extraContainer, equalInventory, false, null, absent, []),
            ("container-missing", equalInventory with { Containers = "" }, equalInventory, false, null, absent, []),
            ("volume-extra", equalInventory with { Volumes = "existing-volume\nnew-volume" }, equalInventory, false, null, absent, []),
            ("volume-missing", equalInventory with { Volumes = "" }, equalInventory, false, null, absent, []),
            ("repeat-drift", equalInventory, extraContainer, false, null, absent, []),
            ("nonterminal", equalInventory, equalInventory, true, null, absent, []),
            ("original-guard", equalInventory, equalInventory, false, originalGuard, absent, []),
            ("bulk-inspection-guard", equalInventory, equalInventory, false,
                bulkInspectionFailure ?? throw new InvalidOperationException("test_bulk_inspection_guard_missing"), absent, []),
            ("earlier-capture-guard", equalInventory, equalInventory, false, null, absent, []),
            ("available-unproved", equalInventory, equalInventory, false, null, available, []),
            ("unattempted", equalInventory, equalInventory, false, null, null, []),
            ("wrong-cached-instance", extraContainer, extraContainer, false, null, absent, wrongInstance),
            ("wrong-cached-reference", extraContainer, extraContainer, false, null, absent, wrongReference),
            ("later-join-neighbour", extraContainer, extraContainer, false, null, absent, exactJoin),
            ("proven-neighbour", extraContainer, extraContainer, false, null, available, exactJoin)
        ];
        foreach (var scenario in scenarios.Concat(consumerReadFailures.Select((failure, index) =>
            (Name: "consumer-read-" + index, First: equalInventory, Second: equalInventory,
                Nonterminal: false, Original: (Exception?)failure,
                Inspection: (OwnedAppHostFixture.ContainerInspectionObservation?)absent,
                Joined: Array.Empty<(IResource Resource, string Instance)>()))))
        {
            string runId = Guid.NewGuid().ToString("N");
            string root = Path.Combine(Path.GetTempPath(), "islamu-assurance", runId);
            Directory.CreateDirectory(root);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var marker = new FileStream(Path.Combine(root, TestingResourceIsolation.MarkerName), options))
                marker.Write(System.Text.Encoding.UTF8.GetBytes(runId));
            var unfinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var retainedProofFailures = new OwnedAppHostFixture.CleanupProofFailures();
            Exception? earlierCaptureFailure = null;
            if (scenario.Name == "earlier-capture-guard")
            {
                try { retainedProofFailures.ClassifyInspection(resource, "observed-instance", 1, "permission denied"); }
                catch (InvalidOperationException exception) { earlierCaptureFailure = exception; }
                var later = retainedProofFailures.ClassifyInspection(resource, "observed-instance", 1, "no such container");
                await Assert.That(earlierCaptureFailure is not null
                    && later.Outcome == OwnedAppHostFixture.ContainerInspectionOutcome.Absent).IsTrue();
            }
            int reads = 0;
            Task<OwnedAppHostFixture.CleanupInventory> ReadInventory(CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(++reads == 1 ? scenario.First : scenario.Second);
            }
            try
            {
                Exception? releaseFailure = null;
                try
                {
                    await OwnedAppHostFixture.ReleaseCleanupRootAsync(runId, root,
                        [scenario.Nonterminal ? unfinished.Task : Task.CompletedTask],
                        scenario.Original is { } preserved ? [preserved] : [], retainedProofFailures,
                        [(resource, "observed-instance")], scenario.Inspection is { } attempted ? [attempted] : [],
                        scenario.Joined, baseline, ReadInventory, CancellationToken.None);
                }
                catch (Exception exception) { releaseFailure = exception; }
                bool rejected = scenario.Name is not ("equal" or "later-join-neighbour" or "proven-neighbour");
                if (rejected)
                {
                    if (releaseFailure is not AggregateException retained || !Directory.Exists(root))
                        throw new InvalidOperationException("test_cleanup_absence_refusal_missing:" + scenario.Name, releaseFailure);
                    await Assert.That(scenario.Original is null
                        || retained.InnerExceptions.Any(item => ReferenceEquals(item, scenario.Original))).IsTrue();
                    await Assert.That(earlierCaptureFailure is null
                        || retained.Flatten().InnerExceptions.Any(item => ReferenceEquals(item, earlierCaptureFailure))).IsTrue();
                }
                else
                {
                    if (releaseFailure is not null || Directory.Exists(root))
                        throw new InvalidOperationException("test_cleanup_absence_release_missing:" + scenario.Name, releaseFailure);
                    await Assert.That(scenario.Name == "equal" ? reads == 2 : reads == 0).IsTrue();
                }
            }
            finally
            {
                unfinished.TrySetResult();
                if (Directory.Exists(root))
                {
                    new TestingResourceIsolation(runId, root).VerifyOwnership();
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }

    private static async Task<Exception[]> VerifyCompleteConsumerReobservationAsync(string sentinel)
    {
        string own = new('a', 64), neighbour = new('b', 64), added = new('c', 64);
        const string volume = "probe-volume", mountpoint = "/native/owned-volume";
        var failures = new List<Exception>();
        object named = new { Type = "volume", Name = volume };
        object unrelated = new { Type = "volume", Name = "other-volume" };
        string Row(string id, params object[] mounts) => JsonSerializer.Serialize(new object[] { id, mounts });
        (string Kind, string[] Ids, int ExitCode, string Output, string Error, Exception? Failure) Inventory(params string[] ids) =>
            ("ps", ids, 0, string.Join('\n', ids), "", null);
        (string Kind, string[] Ids, int ExitCode, string Output, string Error, Exception? Failure) Inspect(
            string[] ids, string output, int exitCode = 0, string error = "") => ("inspect", ids, exitCode, output, error, null);

        async Task<Exception?> ExerciseAsync(string name, string[]? expectedConsumers, bool foreign,
            params (string Kind, string[] Ids, int ExitCode, string Output, string Error, Exception? Failure)[] observations)
        {
            var pending = new Queue<(string Kind, string[] Ids, int ExitCode, string Output, string Error, Exception? Failure)>(observations);
            bool invalidRequest = false;
            Task<(int ExitCode, string Output, string Error)> ExecuteAsync(string[] arguments, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                if (!pending.TryDequeue(out var observation))
                {
                    invalidRequest = true;
                    throw new InvalidOperationException("test_consumer_control_unexpected_request");
                }
                bool valid = observation.Kind == "ps"
                    ? arguments.SequenceEqual(new[] { "ps", "--no-trunc", "-aq" })
                    : arguments.Take(5).SequenceEqual(new[] { "inspect", "--type", "container", "--format", "[{{json .ID}},{{json .Mounts}}]" })
                        && arguments.Length == 5 + observation.Ids.Length
                        && arguments.Skip(5).ToHashSet(StringComparer.Ordinal).SetEquals(observation.Ids);
                if (!valid)
                {
                    invalidRequest = true;
                    throw new InvalidOperationException("test_consumer_control_membership_request_invalid");
                }
                return observation.Failure is { } original
                    ? Task.FromException<(int, string, string)>(original)
                    : Task.FromResult((observation.ExitCode, observation.Output, observation.Error));
            }
            string[]? consumers = null;
            Exception? failure = null;
            try { consumers = await OwnedAppHostFixture.ReadVolumeConsumersAsync(volume, mountpoint, ExecuteAsync, CancellationToken.None); }
            catch (Exception exception) { failure = exception; }
            if (invalidRequest) throw new InvalidOperationException("test_consumer_control_protocol_invalid:" + name, failure);
            if (expectedConsumers is null)
            {
                if (failure is null) throw new InvalidOperationException("test_consumer_control_refusal_missing:" + name);
                failures.Add(failure);
                return failure;
            }
            if (failure is not null) throw new InvalidOperationException("test_consumer_control_proof_missing:" + name, failure);
            var complete = consumers ?? throw new InvalidOperationException("test_consumer_control_result_missing:" + name);
            await Assert.That(complete.ToHashSet(StringComparer.Ordinal).SetEquals(expectedConsumers)).IsTrue();
            if (foreign)
            {
                var refused = await Assert.That(() => OwnedAppHostFixture.RequireExclusiveVolumeConsumer(own, complete))
                    .Throws<InvalidOperationException>();
                await Assert.That(refused!.Message).IsEqualTo("test_resource_foreign_container");
                failures.Add(refused);
            }
            return null;
        }

        await ExerciseAsync("stable", [own], false, Inventory(own), Inspect([own], Row(own, named)), Inventory(own));
        await ExerciseAsync("empty", [], false, Inventory(), Inventory());
        await ExerciseAsync("failed-partial-discarded", [], false,
            Inventory(own, neighbour), Inspect([own, neighbour], Row(neighbour, named) + sentinel, 1, "no such container"),
            Inventory(neighbour), Inventory(neighbour), Inspect([neighbour], Row(neighbour, unrelated)), Inventory(neighbour));
        foreach (object mount in new object[] { named,
            new { Type = "bind", Source = mountpoint }, new { Type = "bind", Source = "/native" },
            new { Type = "bind", Source = mountpoint + "/child" } })
        {
            string rows = Row(own, unrelated) + "\n" + Row(added, mount);
            await ExerciseAsync("missing-result-new-consumer", [added], true,
                Inventory(own), Inspect([own], "", 1, "no such container"), Inventory(own, added),
                Inventory(own, added), Inspect([own, added], rows), Inventory(own, added));
            await ExerciseAsync("successful-result-new-consumer", [added], true,
                Inventory(own), Inspect([own], Row(own, unrelated)), Inventory(own, added),
                Inventory(own, added), Inspect([own, added], rows), Inventory(own, added));
        }
        await ExerciseAsync("same-membership-contradiction", null, false,
            Inventory(own), Inspect([own], "", 1, "no such container"), Inventory(own));
        (int Exit, string Error)[] rejectedInspections = [(1, "permission denied"), (125, "no such container"),
            (1, "no such container; cannot connect"), (1, sentinel)];
        foreach (var rejected in rejectedInspections)
            await ExerciseAsync("unrecognized-error", null, false,
                Inventory(own), Inspect([own], "", rejected.Exit, rejected.Error));
        await ExerciseAsync("second-attempt-missing", null, false,
            Inventory(own), Inspect([own], "", 1, "no such container"), Inventory(neighbour),
            Inventory(neighbour), Inspect([neighbour], "", 1, "no such container"));
        await ExerciseAsync("repeated-drift", null, false,
            Inventory(own), Inspect([own], Row(own, unrelated)), Inventory(neighbour),
            Inventory(neighbour), Inspect([neighbour], Row(neighbour, unrelated)), Inventory(added));
        foreach (string malformed in new[] { "{}", Row(own), Row(own) + "\n" + Row(own), Row(added) })
            await ExerciseAsync("incomplete-inspection", null, false, Inventory(own, neighbour), Inspect([own, neighbour], malformed));
        await ExerciseAsync("duplicate-complete-coverage", null, false,
            Inventory(own), Inspect([own], Row(own) + "\n" + Row(own)));
        foreach (object malformed in new object[] { 42, new { Type = (string?)null }, new { Type = "unknown" },
            new { Type = "npipe", Source = "/native" }, new { Type = "volume" }, new { Type = "volume", Name = (string?)null },
            new { Type = "volume", Name = "" }, new { Type = "volume", Name = "invalid/name" }, new { Type = "bind" },
            new { Type = "bind", Source = (string?)null }, new { Type = "bind", Source = "relative" },
            new { Type = "tmpfs" }, new { Type = "tmpfs", Source = mountpoint },
            new { Type = "tmpfs", Source = "", Name = volume } })
            await ExerciseAsync("malformed-mount", null, false, Inventory(own), Inspect([own], Row(own, malformed)));
        await ExerciseAsync("valid-nonconsumers", [], false, Inventory(own), Inspect([own], Row(own, unrelated,
            new { Type = "bind", Source = "/native/owned-volumes" }, new { Type = "tmpfs", Source = "" })), Inventory(own));
        foreach (var invalid in new[] { Inventory("invalid"), Inventory(own, own) })
            await ExerciseAsync("inventory-invalid", null, false, invalid);
        await ExerciseAsync("inventory-failed", null, false, ("ps", Array.Empty<string>(), 1, "", "", null));
        var fatal = new InvalidOperationException("test_consumer_original_failure");
        var preserved = await ExerciseAsync("fatal-after-transient", null, false,
            Inventory(own), Inspect([own], "", 1, "no such container"), Inventory(neighbour),
            ("ps", Array.Empty<string>(), 0, "", "", fatal));
        await Assert.That(preserved is AggregateException aggregate
            && aggregate.Flatten().InnerExceptions.Any(item => ReferenceEquals(item, fatal))
            && aggregate.Flatten().InnerExceptions.Any(item => item.Message ==
                "test_resource_volume_consumers_unknown:stage=bulk-inspect:exit=1:stderr=no-such-marker")).IsTrue();
        var canceled = new OperationCanceledException("test_consumer_budget_canceled", new CancellationToken(true));
        var cancellation = await ExerciseAsync("canceled-read", null, false,
            ("ps", Array.Empty<string>(), 0, "", "", canceled));
        await Assert.That(ReferenceEquals(cancellation, canceled)).IsTrue();
        return [.. failures];
    }

    [Test]
    public async Task ManagedObserverStopsAfterIndependentFinalizationFailures()
    {
        foreach (bool callbackFault in new[] { false, true })
        {
            using var startup = new CancellationTokenSource();
            using var independent = new CancellationTokenSource();
            await independent.CancelAsync();
            var callbackFailure = new InvalidOperationException("test_managed_final_callback_failure");
            var stopFailure = new InvalidOperationException("test_managed_final_stop_failure");
            using var registration = callbackFault ? startup.Token.Register(() => throw callbackFailure) : default;
            bool stopped = false, ready = false, finalizationObserved = false;
            async Task Observe(CancellationToken token)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested && !callbackFault)
                { throw new OperationCanceledException("test_managed_independent_cancellation", independent.Token); }
            }
            var failure = await Assert.That(async () => await OwnedAppHostFixture.RunObservedStartupAsync(
                Observe,
                _ => Task.CompletedTask, _ => { ready = true; return Task.CompletedTask; },
                () =>
                {
                    if (stopped) throw new InvalidOperationException("test_managed_duplicate_stop");
                    stopped = true;
                    throw stopFailure;
                },
                (phase, reason, requested) => finalizationObserved = requested && ready && !stopped
                    && (callbackFault ? phase == "cancellation_callbacks" : phase == "observer_join"
                        && reason == "canceled_other"),
                startup)).Throws<AggregateException>();
            var causes = failure!.Flatten().InnerExceptions;
            await Assert.That(ready && stopped && finalizationObserved
                && causes.Any(item => ReferenceEquals(item, stopFailure))
                && (callbackFault ? causes.Any(item => ReferenceEquals(item, callbackFailure))
                    : causes.OfType<OperationCanceledException>().Any(item => item.CancellationToken == independent.Token))
                && causes.All(item => item.Message != "test_managed_duplicate_stop")).IsTrue();
        }
    }

    [Test]
    public async Task ManagedLaunchHintsRemainPrivateExclusiveAndUnattested()
    {
        await using var owner = new OwnedAppHostFixture("Split");
        var selected = new ProjectResource("explore-api");
        var observation = new OwnedAppHostFixture.ManagedResourceLaunchObservation([selected], owner.RunId, owner.Root);
        string retained = Path.Combine(Path.GetTempPath(), $"islamu-assurance-managed-launches-{owner.RunId}");
        try
        {
            await observation.CaptureAsync(selected, "owned-instance", [new("executable.pid", Environment.ProcessId)], CancellationToken.None);
            string[] hints = Directory.GetFiles(owner.Root, "managed-launch-*.json");
            await Assert.That(hints.Length == 1 && observation.Rows.Single().Inputs is null
                && observation.Rows.Single().Diagnostic == "managed_launch_inputs_unavailable:process_not_owned").IsTrue();
            string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            string unknownReason = OwnedAppHostFixture.ManagedResourceLaunchObservation.FailureReason(
                new InvalidOperationException(canary));
            observation.RecordStartupFailure("readiness_wait", unknownReason, false, true, false);
            if (OperatingSystem.IsLinux())
                await Assert.That(File.GetUnixFileMode(hints[0]) == (UnixFileMode.UserRead | UnixFileMode.UserWrite)).IsTrue();
            await observation.RetainAsync(CancellationToken.None);
            string summary = Path.Combine(retained, "managed-launches.json");
            byte[] original = await File.ReadAllBytesAsync(summary);
            var primary = new InvalidOperationException("test_managed_health_failure");
            var failure = await Assert.That(async () => await OwnedAppHostFixture.RetainManagedLaunchEvidenceAsync(
                observation, primary, CancellationToken.None)).Throws<AggregateException>();
            await Assert.That(failure!.InnerExceptions.Any(exception => ReferenceEquals(exception, primary))
                && failure.InnerExceptions.Any(exception => exception.Message == "test_managed_launch_destination_exists")).IsTrue();
            byte[] retainedBytes = await File.ReadAllBytesAsync(summary);
            await Assert.That(original.SequenceEqual(retainedBytes)).IsTrue();
            using var document = JsonDocument.Parse(original);
            await Assert.That(document.RootElement.GetProperty("LaunchInputs").GetString() == "not-attested"
                && document.RootElement.GetProperty("HostContentRoot").GetString() == "unknown"
                && document.RootElement.GetProperty("StartupFailureDiagnostic").GetString()
                    == "readiness_wait:guard_unclassified:linked=0:caller=1:fixture=0"
                && !System.Text.Encoding.UTF8.GetString(original).Contains(canary, StringComparison.Ordinal)).IsTrue();
        }
        finally { if (Directory.Exists(retained)) Directory.Delete(retained, recursive: true); }
    }

    [Test]
    public async Task TemporaryResourcePropertyShapesRejectForeignObjectsAndNeverDiscloseValues()
    {
        var selected = new ProjectResource("explore-api");
        var foreign = new ProjectResource("explore-api");
        var observation = new OwnedAppHostFixture.ResourcePropertyShapeObservation([selected]);
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        ImmutableArray<ResourcePropertySnapshot> properties =
        [
            new("pid", canary), new("process-id", new ThrowOnShapeFormatting()) { IsSensitive = true },
            new("executable", new ThrowOnShapeFormatting()), new("args", canary),
            new("environment", canary), new("password", canary), new("pid1", canary),
            new("/pid", canary), new(new string('p', 65), canary)
        ];
        observation.Capture(foreign, "foreign-instance", properties);
        observation.Capture(selected, null, properties);
        observation.Capture(selected, "owned-instance", default);
        await Assert.That(observation.Rows.Count).IsEqualTo(0);
        observation.Capture(selected, "owned-instance", properties);
        await Assert.That(observation.Rows.Count).IsEqualTo(2);
        await Assert.That(observation.Rows.Any(row => row.Name == "process-id"
            && row.PrimitiveType == "not-inspected" && row.IsSensitive)).IsTrue();
        await Assert.That(JsonSerializer.Serialize(observation.Rows).Contains(canary, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task TemporaryResourcePropertyShapesRemainBoundedAndUnknown()
    {
        var selected = new ProjectResource("explore-api");
        var observation = new OwnedAppHostFixture.ResourcePropertyShapeObservation([selected]);
        var properties = Enumerable.Range(0, 33).Select(variant => new ResourcePropertySnapshot(
            new string("processstarttime".Select((character, index) => ((variant >> index) & 1) != 0
                ? char.ToUpperInvariant(character) : character).ToArray()), variant)).ToImmutableArray();
        observation.Capture(selected, "owned-instance-0", properties);
        await Assert.That(observation.Rows.Count).IsEqualTo(32);
        for (int index = 1; index < 6; index++) observation.Capture(selected, $"owned-instance-{index}", properties);
        await Assert.That(observation.Rows.Count).IsEqualTo(128);
        await Assert.That(observation.HasOmittedPropertyShapes).IsTrue();
        await Assert.That(observation.LaunchInputs).IsEqualTo("not-attested");
    }

    [Test]
    public async Task TemporaryResourcePropertyEvidenceUsesExclusivePrivateFiles()
    {
        string runId = Guid.NewGuid().ToString("N");
        string directory = Path.Combine(Path.GetTempPath(), $"islamu-assurance-resource-shapes-{runId}");
        var observation = new OwnedAppHostFixture.ResourcePropertyShapeObservation([]);
        try
        {
            await OwnedAppHostFixture.WritePropertyShapeEvidenceAsync(runId,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), observation, CancellationToken.None);
            string file = Path.Combine(directory, "shapes.json");
            await Assert.That(OperatingSystem.IsLinux()).IsTrue();
            if (OperatingSystem.IsLinux())
            {
                await Assert.That(File.GetUnixFileMode(directory) == (UnixFileMode.UserRead
                    | UnixFileMode.UserWrite | UnixFileMode.UserExecute)).IsTrue();
                await Assert.That(File.GetUnixFileMode(file) == (UnixFileMode.UserRead | UnixFileMode.UserWrite)).IsTrue();
            }
            byte[] original = await File.ReadAllBytesAsync(file);
            var refusal = await Assert.That(async () => await OwnedAppHostFixture.WritePropertyShapeEvidenceAsync(runId,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), observation, CancellationToken.None))
                .Throws<InvalidOperationException>();
            await Assert.That(refusal!.Message).IsEqualTo("test_resource_shape_destination_exists");
            byte[] retained = await File.ReadAllBytesAsync(file);
            await Assert.That(original.SequenceEqual(retained)).IsTrue();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task TemporaryResourcePropertyEvidenceRefusesLinkedDestinationsAndHonoursCancellation()
    {
        await Assert.That(OperatingSystem.IsLinux()).IsTrue();
        if (!OperatingSystem.IsLinux()) return;
        string runId = Guid.NewGuid().ToString("N");
        string directory = Path.Combine(Path.GetTempPath(), $"islamu-assurance-resource-shapes-{runId}");
        string foreign = Path.Combine(Path.GetTempPath(), $"islamu-assurance-foreign-shape-{runId}");
        Directory.CreateDirectory(foreign, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string sentinel = Path.Combine(foreign, "sentinel");
        await File.WriteAllTextAsync(sentinel, canary);
        try
        {
            Directory.CreateSymbolicLink(directory, foreign);
            var refusal = await Assert.That(async () => await OwnedAppHostFixture.WritePropertyShapeEvidenceAsync(runId,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), new([]), CancellationToken.None))
                .Throws<InvalidOperationException>();
            await Assert.That(refusal!.Message).IsEqualTo("test_resource_shape_destination_exists");
            await Assert.That(await File.ReadAllTextAsync(sentinel) == canary).IsTrue();
            await Assert.That(Directory.GetFiles(foreign).Length).IsEqualTo(1);
            Directory.Delete(directory);
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            await Assert.That(async () => await OwnedAppHostFixture.WritePropertyShapeEvidenceAsync(runId,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), new([]), cancelled.Token))
                .Throws<OperationCanceledException>();
            await Assert.That(Directory.Exists(directory)).IsFalse();
        }
        finally
        {
            if (new DirectoryInfo(directory).LinkTarget is not null) Directory.Delete(directory);
            Directory.Delete(foreign, recursive: true);
        }
    }

    [Test]
    public async Task TemporaryResourcePropertyEvidenceRetainsOriginalFailureAndOwnedCleanup()
    {
        await using var owner = new OwnedAppHostFixture("Split");
        var original = new InvalidOperationException("test_shape_original_failure");
        var failure = await Assert.That(async () => await owner.CompleteResourcePropertyShapeObservationAsync(
            original, CancellationToken.None)).Throws<AggregateException>();
        await Assert.That(failure!.InnerExceptions.Any(exception => ReferenceEquals(exception, original))).IsTrue();
        await Assert.That(failure.InnerExceptions.Any(exception => exception.Message == "test_resource_shape_write_invalid_state")).IsTrue();
        await owner.DisposeAsync();
        await Assert.That(Directory.Exists(owner.Root)).IsFalse();
    }

    private sealed class ThrowOnShapeFormatting
    {
        public override string ToString() => throw new InvalidOperationException("test_shape_value_was_formatted");
    }

    [Test]
    public async Task CancelledDockerWaitReapsItsChildAndPreservesTheContainer()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var creation = await OwnedAppHostFixture.RunDockerAsync(["run", "--pull=never", "--detach", "--entrypoint", "sleep",
            "postgres:18-alpine", "600"], deadline.Token);
        await Assert.That(creation.ExitCode).IsEqualTo(0);
        string container = creation.Output.Trim();
        try
        {
            using var cancellation = new CancellationTokenSource();
            Process? child = null;
            try
            {
                Task waiting = OwnedAppHostFixture.RunDockerAsync(["wait", container], cancellation.Token,
                    id => child = Process.GetProcessById(id));
                cancellation.Cancel();
                await Assert.That(async () => await waiting).Throws<OperationCanceledException>();
                await Assert.That(child!.WaitForExit(0)).IsTrue();
            }
            finally { child?.Dispose(); }
            var retained = await OwnedAppHostFixture.RunDockerAsync(["inspect", "--format", "{{.State.Running}}", container], deadline.Token);
            await Assert.That(retained.ExitCode).IsEqualTo(0);
            await Assert.That(retained.Output.Trim()).IsEqualTo("true");
        }
        finally
        {
            var removal = await OwnedAppHostFixture.RunDockerAsync(["rm", "--force", "--volumes", container], CancellationToken.None);
            await Assert.That(removal.ExitCode).IsEqualTo(0);
        }
    }

    [Test]
    public async Task BuilderFailureRetainsTheCauseAndRemovesItsOwnedRoot()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        string? root = null;
        var failure = await Assert.That(async () => await OwnedAppHostFixture.CreateAsync("Split",
            false, "SingleTenant", null, builder =>
            {
                root = builder.Configuration["ISLAMU_TEST_ROOT"];
                throw new InvalidOperationException("assurance_builder_fault");
            }, deadline.Token)).Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).IsEqualTo("assurance_builder_fault");
        await Assert.That(root).IsNotNull();
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    [Test]
    public async Task ForeignVolumeConsumerSurvivesOwnerCleanup()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await Assert.That(OwnedAppHostFixture.NativePathsOverlap("/native/data", "/native/data")).IsTrue();
        await Assert.That(OwnedAppHostFixture.NativePathsOverlap("/native", "/native/data")).IsTrue();
        await Assert.That(OwnedAppHostFixture.NativePathsOverlap("/native/data/child", "/native/data")).IsTrue();
        await Assert.That(OwnedAppHostFixture.NativePathsOverlap("/native/database", "/native/data")).IsFalse();
        foreach (bool bindMount in new[] { false, true })
        {
            var owner = await OwnedAppHostFixture.CreateAsync("Split", deadline.Token);
            string volume = $"islamu-test-{owner.RunId}-islamu-event-postgres-data";
            string? sentinel = null;
            try
            {
                var creation = await OwnedAppHostFixture.RunDockerAsync(["volume", "create", volume], deadline.Token);
                await Assert.That(creation.ExitCode).IsEqualTo(0);
                var inspected = await OwnedAppHostFixture.RunDockerAsync(
                    ["volume", "inspect", "--format", "{{.Mountpoint}}", volume], deadline.Token);
                await Assert.That(inspected.ExitCode).IsEqualTo(0);
                string mount = bindMount
                    ? $"type=bind,source={inspected.Output.Trim()},target=/sentinel"
                    : $"type=volume,source={volume},target=/sentinel";
                var container = await OwnedAppHostFixture.RunDockerAsync(["create", "--pull=never", "--label",
                    "islamu.assurance.owner=foreign", "--mount", mount,
                    "postgres:18-alpine"], deadline.Token);
                await Assert.That(container.ExitCode).IsEqualTo(0);
                sentinel = container.Output.Trim();

                var failure = await Assert.That(async () => await owner.DisposeAsync()).Throws<AggregateException>();
                await Assert.That(failure!.Flatten().InnerExceptions.Any(error =>
                    error.Message == "test_resource_foreign_container")).IsTrue();
                var retained = await OwnedAppHostFixture.RunDockerAsync(["inspect", "--format",
                    "{{index .Config.Labels \"islamu.assurance.owner\"}}", sentinel], deadline.Token);
                await Assert.That(retained.ExitCode).IsEqualTo(0);
                await Assert.That(retained.Output.Trim()).IsEqualTo("foreign");
                await Assert.That(Directory.Exists(owner.Root)).IsTrue();
            }
            finally
            {
                try { await using (owner) { } }
                catch (AggregateException) { }
                if (sentinel is not null)
                {
                    var removal = await OwnedAppHostFixture.RunDockerAsync(["rm", "--volumes", sentinel], CancellationToken.None);
                    await Assert.That(removal.ExitCode).IsEqualTo(0);
                }
                var volumeRemoval = await OwnedAppHostFixture.RunDockerAsync(["volume", "rm", volume], CancellationToken.None);
                await Assert.That(volumeRemoval.ExitCode).IsEqualTo(0);
                if (Directory.Exists(owner.Root))
                {
                    new TestingResourceIsolation(owner.RunId, owner.Root).VerifyOwnership();
                    Directory.Delete(owner.Root, recursive: true);
                }
            }
        }
    }

    [Test]
    [Arguments("Split")]
    [Arguments("Standalone")]
    public async Task RealTopologyCompletesMigrationAndBecomesHealthy(string topology)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await using var owner = await OwnedAppHostFixture.CreateAsync(topology, deadline.Token);
        if (topology == "Split") await owner.BeginResourcePropertyShapeObservationAsync(deadline.Token);
        Exception? failure = null;
        try
        {
            await owner.StartAsync(deadline.Token);
            using var client = owner.CreateHttpClient(topology == "Split" ? "explore-api" : "event-standalone", "operations");
            using var response = await client.GetAsync("/health", deadline.Token);
            await Assert.That(response.IsSuccessStatusCode).IsTrue();
        }
        catch (Exception exception) { failure = exception; }
        if (topology == "Split")
        {
            using var evidenceDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await owner.CompleteResourcePropertyShapeObservationAsync(failure, evidenceDeadline.Token);
        }
        else
        {
            using var evidenceDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await owner.CompleteManagedLaunchObservationAsync(failure, evidenceDeadline.Token);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancelledStartupDisposesOnlyItsOwnedRoot(bool cancelWhenPostgresStarting)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var owner = await OwnedAppHostFixture.CreateAsync("Split", deadline.Token);
        string root = owner.Root;
        string[] volumes = owner.OwnedVolumeNames;
        var observed = new List<OwnedAppHostFixture.ContainerCleanupIdentity>();
        try
        {
            using var cancellation = new CancellationTokenSource();
            Task postgresState = cancelWhenPostgresStarting
                ? owner.Application.ResourceNotifications.WaitForResourceAsync(
                    "postgres", KnownResourceStates.Starting, deadline.Token)
                : owner.Application.ResourceNotifications.WaitForResourceHealthyAsync("postgres", deadline.Token);
            Task startup = owner.StartAsync(cancellation.Token);
            await postgresState;
            var postgres = await owner.WaitForOwnedContainerAsync("postgres", false, deadline.Token);
            observed.Add(postgres);
            if (!cancelWhenPostgresStarting)
            {
                var bootstrap = await owner.WaitForOwnedContainerAsync("minio-bootstrap", true, deadline.Token);
                await Assert.That(bootstrap.MountCount).IsEqualTo(0);
                observed.Add(bootstrap);
                var cerbos = await owner.WaitForOwnedContainerAsync("cerbos", false, deadline.Token);
                var implicitVolumes = cerbos.Volumes.Where(volume => volume.Implicit).ToArray();
                await Assert.That(implicitVolumes.Length).IsEqualTo(2);
                observed.Add(cerbos);
                var changed = System.Text.Json.Nodes.JsonNode.Parse(cerbos.Inspection)!.AsArray();
                var mount = changed[4]!.AsArray().Single(item =>
                    item!["Destination"]!.GetValue<string>() == implicitVolumes[0].Target)!;
                mount["Destination"] = "/undeclared-cleanup-target";
                await Assert.That(OwnedAppHostFixture.TryReadContainerIdentity(
                    [cerbos.Resource], cerbos.Resource, cerbos.Instance, owner.RunId, cerbos.Image,
                    cerbos.ImageVolumeTargets, new HashSet<string>(), new HashSet<string>(),
                    changed.ToJsonString(), out _, out string targetReason)).IsFalse();
                await Assert.That(targetReason).IsEqualTo("mount-target");
                await Assert.That(OwnedAppHostFixture.TryReadContainerIdentity(
                    [cerbos.Resource], cerbos.Resource, cerbos.Instance, owner.RunId, cerbos.Image,
                    cerbos.ImageVolumeTargets, new HashSet<string>(), new HashSet<string> { implicitVolumes[0].Name },
                    cerbos.Inspection, out _, out string originReason)).IsFalse();
                await Assert.That(originReason).IsEqualTo("preexisting-volume");
                mount["Destination"] = implicitVolumes[0].Target;
                mount["Name"] = $"changed-{owner.RunId}";
                await Assert.That(OwnedAppHostFixture.TryReadContainerIdentity(
                    [cerbos.Resource], cerbos.Resource, cerbos.Instance, owner.RunId, cerbos.Image,
                    cerbos.ImageVolumeTargets, new HashSet<string>(), new HashSet<string>(),
                    changed.ToJsonString(), out _, out string bindingReason, cerbos)).IsFalse();
                await Assert.That(bindingReason).IsEqualTo("volume-binding-changed");
            }
            await Assert.That(OwnedAppHostFixture.TryReadContainerIdentity(
                [postgres.Resource], new ContainerResource(postgres.Resource.Name), postgres.Instance,
                owner.RunId, postgres.Image, postgres.ImageVolumeTargets, new HashSet<string>(),
                new HashSet<string>(), postgres.Inspection, out _, out _)).IsFalse();
            await Assert.That(OwnedAppHostFixture.TryReadContainerIdentity(
                [postgres.Resource], postgres.Resource, "unmatched-sdk-instance", owner.RunId,
                postgres.Image, postgres.ImageVolumeTargets, new HashSet<string>(), new HashSet<string>(),
                postgres.Inspection, out _, out _)).IsFalse();
            await Assert.That(OwnedAppHostFixture.TryReadContainerIdentity(
                [postgres.Resource], postgres.Resource, postgres.Instance, owner.RunId,
                postgres.Image, postgres.ImageVolumeTargets, new HashSet<string> { postgres.Id },
                new HashSet<string>(), postgres.Inspection, out _, out _)).IsFalse();
            await Assert.That(OwnedAppHostFixture.TryReadContainerIdentity(
                [postgres.Resource], postgres.Resource, postgres.Instance, owner.RunId,
                postgres.Image, postgres.ImageVolumeTargets, new HashSet<string>(), new HashSet<string>(),
                "{}", out _, out _)).IsFalse();
            cancellation.Cancel();
            Exception? startupCancellation = null;
            try { await startup; }
            catch (Exception exception) { startupCancellation = exception; }
            if (startupCancellation is null)
                throw new InvalidOperationException("test_resource_startup_cancellation_missing");
            Exception[] causes = startupCancellation is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions.ToArray() : [startupCancellation];
            if (!cancellation.IsCancellationRequested || causes.Length == 0
                || causes.Any(cause => cause is not OperationCanceledException))
            {
                string requested = cancellation.IsCancellationRequested ? "requested" : "not-requested";
                string kinds = string.Join(",", causes.Select(cause => cause switch
                {
                    OperationCanceledException => "canceled",
                    TimeoutException => "timeout",
                    InvalidOperationException failure when failure.Message.StartsWith("test_resource_", StringComparison.Ordinal) => "guard",
                    _ => "fault"
                }));
                throw new InvalidOperationException($"test_resource_startup_cancellation_invalid:{requested}:{kinds}",
                    startupCancellation);
            }
        }
        finally
        {
            await Task.WhenAll(owner.DisposeAsync().AsTask(), owner.DisposeAsync().AsTask());
        }
        await Assert.That(owner.ExecutionSettled).IsTrue();
        await Assert.That(Directory.Exists(root)).IsFalse();
        observed.AddRange(owner.CompletedCleanupIdentities);
        foreach (var identity in observed.DistinctBy(identity => identity.Id))
        {
            var retained = await OwnedAppHostFixture.RunDockerAsync(
                ["ps", "--no-trunc", "-aq", "--filter", $"id={identity.Id}"], deadline.Token);
            await Assert.That(retained.ExitCode).IsEqualTo(0);
            await Assert.That(retained.Output.Trim()).IsEqualTo(string.Empty);
        }
        var remaining = await OwnedAppHostFixture.RunDockerAsync(
            ["ps", "--no-trunc", "-aq", "--filter", $"label=islamu.assurance.owner={owner.RunId}"], deadline.Token);
        await Assert.That(remaining.ExitCode).IsEqualTo(0);
        await Assert.That(remaining.Output.Trim()).IsEqualTo(string.Empty);
        foreach (string volume in volumes.Concat(observed.SelectMany(identity => identity.Volumes.Select(volume => volume.Name)))
            .Distinct(StringComparer.Ordinal))
        {
            var retained = await OwnedAppHostFixture.RunDockerAsync(
                ["volume", "inspect", "--format", "{{.Name}}", volume], deadline.Token);
            await Assert.That(retained.ExitCode).IsNotEqualTo(0);
            await Assert.That(retained.Error.Contains("no such volume", StringComparison.OrdinalIgnoreCase)).IsTrue();
        }
    }

    [Test]
    public async Task ConcurrentOwnersRemainHealthyWhenTheirNeighbourStops()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using var first = await OwnedAppHostFixture.CreateAsync("Split", deadline.Token, configuredAdministrator: true);
        await using var second = await OwnedAppHostFixture.CreateAsync("Standalone", deadline.Token, configuredAdministrator: true);
        await Task.WhenAll(first.StartAsync(deadline.Token), second.StartAsync(deadline.Token));
        await Assert.That(first.Root).IsNotEqualTo(second.Root);
        using var firstApi = first.CreateHttpClient("explore-api");
        using var secondApi = second.CreateHttpClient("event-standalone");
        Guid firstUserId = await first.AuthenticateBootstrapAsync(firstApi, deadline.Token);
        Guid secondUserId = await second.AuthenticateBootstrapAsync(secondApi, deadline.Token);
        var firstAuthorization = firstApi.DefaultRequestHeaders.Authorization;
        var secondAuthorization = secondApi.DefaultRequestHeaders.Authorization;
        firstApi.DefaultRequestHeaders.Authorization = secondAuthorization;
        using (var foreign = await firstApi.GetAsync("/api/user", deadline.Token))
            await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        firstApi.DefaultRequestHeaders.Authorization = firstAuthorization;
        secondApi.DefaultRequestHeaders.Authorization = firstAuthorization;
        using (var foreign = await secondApi.GetAsync("/api/user", deadline.Token))
            await Assert.That(foreign.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        secondApi.DefaultRequestHeaders.Authorization = secondAuthorization;
        using (var own = await firstApi.GetAsync("/api/user", deadline.Token))
        {
            own.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await own.Content.ReadAsStringAsync(deadline.Token));
            await Assert.That(body.RootElement.GetProperty("id").GetGuid()).IsEqualTo(firstUserId);
        }
        await first.DisposeAsync();
        using (var own = await secondApi.GetAsync("/api/user", deadline.Token))
        {
            own.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await own.Content.ReadAsStringAsync(deadline.Token));
            await Assert.That(body.RootElement.GetProperty("id").GetGuid()).IsEqualTo(secondUserId);
        }
        using var client = second.CreateHttpClient("event-standalone", "operations");
        using var response = await client.GetAsync("/health", deadline.Token);
        await Assert.That(response.IsSuccessStatusCode).IsTrue();
        await Assert.That(Directory.Exists(second.Root)).IsTrue();
    }

}
