using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class EventReportDecisionExecutionTests
{
    [Test]
    public async Task Execution_ExactReceiptThenCompletion_TransitionsThroughDurableStates()
    {
        DateTime createdAtUtc = new(2026, 7, 20, 8, 0, 0, DateTimeKind.Utc);
        EventReportDecisionExecution execution = EventReportDecisionExecution.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            createdAtUtc);
        Guid enforcementLease = Guid.CreateVersion7();
        Guid moderationReceiptId = Guid.CreateVersion7();
        Guid completionLease = Guid.CreateVersion7();

        execution.ClaimEnforcement(enforcementLease, createdAtUtc, createdAtUtc.AddMinutes(5));
        execution.RecordEnforcementReceipt(
            enforcementLease,
            EventReportDecisionEnforcementReceiptKind.LightModeration,
            moderationReceiptId,
            createdAtUtc.AddMinutes(1));
        execution.ClaimCompletion(completionLease, createdAtUtc.AddMinutes(2), createdAtUtc.AddMinutes(7));
        execution.Complete(completionLease, createdAtUtc.AddMinutes(3));

        await Assert.That(execution.State).IsEqualTo(EventReportDecisionExecutionState.Completed);
        await Assert.That(execution.EnforcementReceiptKind).IsEqualTo(EventReportDecisionEnforcementReceiptKind.LightModeration);
        await Assert.That(execution.EnforcementReceiptId).IsEqualTo(moderationReceiptId);
        await Assert.That(execution.CompletedAtUtc).IsEqualTo(createdAtUtc.AddMinutes(3));
        await Assert.That(execution.ProcessingLeaseToken).IsNull();
    }

    [Test]
    public async Task ClaimEnforcement_AfterExpiredLease_AllowsCrashRecovery()
    {
        DateTime createdAtUtc = new(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc);
        EventReportDecisionExecution execution = EventReportDecisionExecution.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            createdAtUtc);
        execution.ClaimEnforcement(Guid.CreateVersion7(), createdAtUtc, createdAtUtc.AddMinutes(1));
        Guid replacementLease = Guid.CreateVersion7();

        execution.ClaimEnforcement(replacementLease, createdAtUtc.AddMinutes(2), createdAtUtc.AddMinutes(7));

        await Assert.That(execution.State).IsEqualTo(EventReportDecisionExecutionState.InProgress);
        await Assert.That(execution.ProcessingLeaseToken).IsEqualTo(replacementLease);
        await Assert.That(execution.AttemptCount).IsEqualTo(2);
    }

    [Test]
    public async Task RecordEnforcementReceipt_LightWithoutExactRecordId_IsRejected()
    {
        DateTime createdAtUtc = new(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc);
        EventReportDecisionExecution execution = EventReportDecisionExecution.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            createdAtUtc);
        Guid lease = Guid.CreateVersion7();
        execution.ClaimEnforcement(lease, createdAtUtc, createdAtUtc.AddMinutes(5));

        await Assert.ThrowsAsync<ArgumentException>(() => Task.Run(() =>
            execution.RecordEnforcementReceipt(
                lease,
                EventReportDecisionEnforcementReceiptKind.LightModeration,
                receiptId: null,
                createdAtUtc.AddMinutes(1))));

        await Assert.That(execution.State).IsEqualTo(EventReportDecisionExecutionState.InProgress);
    }
}
