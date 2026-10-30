namespace Event.Persistence.IntegrationTests;

public sealed class FreshEfProcessExecutorTests
{
    [Test]
    public async Task CancellationEvidenceBoundsCaptureAndRejectsUntrustedOrIncompletePhases()
    {
        string canary = Guid.NewGuid().ToString("N");
        const string phase = "migration_mixed_boundary phase=baseline_ready";
        using var delivered = new MemoryStream();
        using var bufferedDelivery = new BufferedStream(delivered, 4096);
        FreshEfProcessExecutor.WritePhaseBestEffort(bufferedDelivery, phase);
        FreshEfProcessExecutor.WritePhaseBestEffort(bufferedDelivery, phase + canary);
        await Assert.That(System.Text.Encoding.UTF8.GetString(delivered.ToArray()) == phase + "\n").IsTrue();
        await Assert.That(bufferedDelivery.CanWrite).IsTrue();
        using var completeReader = new StringReader($"{canary}\n{phase}\n{phase}\n{phase} {canary}\n");
        var complete = await FreshEfProcessExecutor.CaptureOutputAsync(completeReader, null, CancellationToken.None);
        using var overflowReader = new StringReader(new string('x', FreshEfProcessExecutor.MaximumRetainedCharacters + 1) + "\n" + phase);
        var overflow = await FreshEfProcessExecutor.CaptureOutputAsync(overflowReader, null, CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var incompleteReader = new StringReader(phase);
        var incomplete = await FreshEfProcessExecutor.CaptureOutputAsync(incompleteReader, null, cancelled.Token);

        await Assert.That(complete.Complete && !complete.Overflowed).IsTrue();
        await Assert.That(overflow.Complete && overflow.Overflowed).IsTrue();
        await Assert.That(overflowReader.Peek()).IsEqualTo(-1);
        await Assert.That(overflow.Text.Length).IsEqualTo(FreshEfProcessExecutor.MaximumRetainedCharacters);
        await Assert.That(incomplete.Complete).IsFalse();
        string[] evidence = FreshEfProcessExecutor.ProjectCancellationEvidence(complete, overflow,
            callerRequested: true, deadlineRequested: false, killAttempted: true, killReturned: true, joinReturned: true);
        await Assert.That(evidence.Count(line => line == phase)).IsEqualTo(1);
        await Assert.That(evidence.Any(line => line.Contains(canary, StringComparison.Ordinal))).IsFalse();
        await Assert.That(evidence[0]).IsEqualTo("fresh_ef_cancellation caller_requested=true owned_deadline_requested=false kill_attempted=true kill_returned=true join_returned=true stdout=complete stderr=overflow phase_semantics=some_attempt_reported");
        string[] unavailable = FreshEfProcessExecutor.ProjectCancellationEvidence(incomplete with { Text = phase }, null,
            callerRequested: false, deadlineRequested: true, killAttempted: true, killReturned: false, joinReturned: false);
        await Assert.That(unavailable.Length).IsEqualTo(1);
        await Assert.That(unavailable[0]).IsEqualTo("fresh_ef_cancellation caller_requested=false owned_deadline_requested=true kill_attempted=true kill_returned=false join_returned=false stdout=incomplete stderr=unavailable phase_semantics=some_attempt_reported");
        string[] failed = FreshEfProcessExecutor.ProjectCancellationEvidence(
            complete with { Failure = new IOException(canary) }, overflow,
            callerRequested: false, deadlineRequested: false, killAttempted: false, killReturned: false, joinReturned: true);
        await Assert.That(failed.Length).IsEqualTo(1);
        await Assert.That(failed[0].Contains(canary, StringComparison.Ordinal)).IsFalse();

        const string inconclusive = "migration_mixed_wait phase=mixed_helper_scope outcome=inconclusive";
        await Assert.That(Migrations.PostgresMigrationPrerequisiteTests.DescribeMixedWait(false, true, false, [], 7) == inconclusive).IsTrue();
        await Assert.That(Migrations.PostgresMigrationPrerequisiteTests.DescribeMixedWait(true, null, false, [], 7) == inconclusive).IsTrue();
        await Assert.That(Migrations.PostgresMigrationPrerequisiteTests.DescribeMixedWait(true, true, null, [], 7) == inconclusive).IsTrue();
        await Assert.That(Migrations.PostgresMigrationPrerequisiteTests.DescribeMixedWait(true, true, false, null, 7) == inconclusive).IsTrue();
        await Assert.That(Migrations.PostgresMigrationPrerequisiteTests.DescribeMixedWait(true, true, true, [0], 7) == inconclusive).IsTrue();
        await Assert.That(Migrations.PostgresMigrationPrerequisiteTests.DescribeMixedWait(true, true, true, [8], 7) == inconclusive).IsTrue();
        await Assert.That(Migrations.PostgresMigrationPrerequisiteTests.DescribeMixedWait(true, true, true, [], 0) == inconclusive).IsTrue();
        string ownedWait = Migrations.PostgresMigrationPrerequisiteTests.DescribeMixedWait(true, true, true, [7, 7], 7);
        await Assert.That(ownedWait == "migration_mixed_wait phase=mixed_helper_scope active=true lock_wait=true owned_blocker=true").IsTrue();
        string noWait = Migrations.PostgresMigrationPrerequisiteTests.DescribeMixedWait(true, false, false, [], 7);
        await Assert.That(noWait == "migration_mixed_wait phase=mixed_helper_scope active=false lock_wait=false owned_blocker=false").IsTrue();
        string independentState = Migrations.PostgresMigrationPrerequisiteTests.DescribeMixedWait(true, false, true, [7], 7);
        await Assert.That(independentState == "migration_mixed_wait phase=mixed_helper_scope active=false lock_wait=true owned_blocker=true").IsTrue();
        using var waitDelivery = new MemoryStream();
        FreshEfProcessExecutor.WritePhaseBestEffort(waitDelivery, ownedWait);
        FreshEfProcessExecutor.WritePhaseBestEffort(waitDelivery, ownedWait + canary);
        FreshEfProcessExecutor.WritePhaseBestEffort(waitDelivery, "migration_mixed_wait phase=mixed_helper_scope active=unknown lock_wait=false owned_blocker=false");
        await Assert.That(System.Text.Encoding.UTF8.GetString(waitDelivery.ToArray()) == ownedWait + "\n").IsTrue();
        string[] waitEvidence = FreshEfProcessExecutor.ProjectCancellationEvidence(
            complete with { Text = ownedWait + "\n" + noWait + "\n" + independentState + "\n" + inconclusive + "\n" + ownedWait + canary }, null,
            callerRequested: false, deadlineRequested: true, killAttempted: true, killReturned: true, joinReturned: true);
        await Assert.That(waitEvidence.Length).IsEqualTo(5);
        await Assert.That(waitEvidence.Any(line => line.Contains(canary, StringComparison.Ordinal))).IsFalse();

        var arrivalClock = System.Diagnostics.Stopwatch.StartNew();
        using var splitReader = new StringReader(new string('x', 4094) + "\n" + phase + "\n" + phase + "\n"
            + phase + " elapsed_ms=1 " + canary + "\n");
        var timed = await FreshEfProcessExecutor.CaptureOutputAsync(splitReader, arrivalClock, CancellationToken.None);
        await Assert.That(timed.Arrivals is [{ Phase: phase }, { Phase: phase }]).IsTrue();
        await Assert.That(timed.Arrivals![0].ElapsedMilliseconds >= 0
            && timed.Arrivals[1].ElapsedMilliseconds >= timed.Arrivals[0].ElapsedMilliseconds).IsTrue();
        string[] arrivalEvidence = FreshEfProcessExecutor.ProjectPhaseArrivals(timed);
        await Assert.That(arrivalEvidence.Length).IsEqualTo(3);
        await Assert.That(arrivalEvidence[0]).IsEqualTo("fresh_ef_timing clock=parent_launch_arrival anchor=child_launch_attempt elapsed_ms=0");
        await Assert.That(arrivalEvidence[1].StartsWith("fresh_ef_timing ordinal=1 elapsed_ms=", StringComparison.Ordinal)
            && arrivalEvidence[2].StartsWith("fresh_ef_timing ordinal=2 elapsed_ms=", StringComparison.Ordinal)
            && arrivalEvidence[1].EndsWith(phase, StringComparison.Ordinal)
            && arrivalEvidence[2].EndsWith(phase, StringComparison.Ordinal)).IsTrue();
        await Assert.That(arrivalEvidence.Any(line => line.Contains(canary, StringComparison.Ordinal))).IsFalse();

        using var hostileReader = new StringReader(new string('x', FreshEfProcessExecutor.MaximumRetainedCharacters + 1) + "\n" + phase + "\n");
        var hostile = await FreshEfProcessExecutor.CaptureOutputAsync(hostileReader, arrivalClock, CancellationToken.None);
        await Assert.That(hostile.Text.Length).IsEqualTo(FreshEfProcessExecutor.MaximumRetainedCharacters);
        await Assert.That(hostileReader.Peek()).IsEqualTo(-1);
        await Assert.That(FreshEfProcessExecutor.ProjectPhaseArrivals(hostile)
            .SequenceEqual(new[] { arrivalEvidence[0], "fresh_ef_timing outcome=overflow" })).IsTrue();
        using var excessReader = new StringReader(string.Concat(Enumerable.Repeat(phase + "\n", FreshEfProcessExecutor.MaximumPhaseArrivals + 1)));
        var excess = await FreshEfProcessExecutor.CaptureOutputAsync(excessReader, arrivalClock, CancellationToken.None);
        await Assert.That(excess.Arrivals!.Length).IsEqualTo(FreshEfProcessExecutor.MaximumPhaseArrivals);
        await Assert.That(FreshEfProcessExecutor.ProjectPhaseArrivals(excess)
            .SequenceEqual(new[] { arrivalEvidence[0], "fresh_ef_timing outcome=inconsistent" })).IsTrue();
        using var incompleteTimedReader = new StringReader(phase + "\n");
        var incompleteTimed = await FreshEfProcessExecutor.CaptureOutputAsync(incompleteTimedReader, arrivalClock, cancelled.Token);
        await Assert.That(FreshEfProcessExecutor.ProjectPhaseArrivals(incompleteTimed)
            .SequenceEqual(new[] { arrivalEvidence[0], "fresh_ef_timing outcome=incomplete" })).IsTrue();
        arrivalClock.Stop();
        using var stoppedReader = new StringReader(phase + "\n");
        var stopped = await FreshEfProcessExecutor.CaptureOutputAsync(stoppedReader, arrivalClock, CancellationToken.None);
        await Assert.That(FreshEfProcessExecutor.ProjectPhaseArrivals(stopped)
            .SequenceEqual(new[] { arrivalEvidence[0], "fresh_ef_timing outcome=inconsistent" })).IsTrue();
        await Assert.That(FreshEfProcessExecutor.ProjectPhaseArrivals(complete) is ["fresh_ef_timing outcome=unavailable"]).IsTrue();

        var finalLineClock = System.Diagnostics.Stopwatch.StartNew();
        using var unterminatedReader = new StringReader(phase);
        var unterminated = await FreshEfProcessExecutor.CaptureOutputAsync(unterminatedReader, finalLineClock, CancellationToken.None);
        await Assert.That(unterminated.Complete && unterminated.Arrivals is []).IsTrue();
        await Assert.That(FreshEfProcessExecutor.ProjectPhaseArrivals(unterminated).SequenceEqual(new[] { arrivalEvidence[0] })).IsTrue();

        foreach (FreshEfProcessExecutor.PhaseArrival[] invalid in new[]
        {
            new[] { new FreshEfProcessExecutor.PhaseArrival(phase, -1) },
            new[] { new FreshEfProcessExecutor.PhaseArrival(phase, FreshEfProcessExecutor.MaximumPhaseElapsedMilliseconds + 1) },
            new[] { new FreshEfProcessExecutor.PhaseArrival(phase, 2), new FreshEfProcessExecutor.PhaseArrival(phase, 1) },
            new[] { new FreshEfProcessExecutor.PhaseArrival(phase + canary, 1) }
        })
        {
            string[] rejected = FreshEfProcessExecutor.ProjectPhaseArrivals(timed with { Arrivals = invalid });
            await Assert.That(rejected.SequenceEqual(new[] { arrivalEvidence[0], "fresh_ef_timing outcome=inconsistent" })).IsTrue();
            await Assert.That(rejected.Any(line => line.Contains(canary, StringComparison.Ordinal))).IsFalse();
        }
        await Assert.That(FreshEfProcessExecutor.ProjectPhaseArrivals(timed with { Failure = new IOException(canary) })
            .SequenceEqual(new[] { arrivalEvidence[0], "fresh_ef_timing outcome=unavailable" })).IsTrue();
    }
}
