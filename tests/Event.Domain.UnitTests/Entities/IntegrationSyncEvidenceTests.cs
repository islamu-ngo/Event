using Explore.Domain;

namespace Event.Domain.UnitTests.Entities;

public sealed class IntegrationSyncEvidenceTests
{
    [Test]
    [Arguments(IntegrationSyncEvidenceKind.None, IntegrationSyncStatus.Pending)]
    [Arguments(IntegrationSyncEvidenceKind.None, IntegrationSyncStatus.Processing)]
    [Arguments(IntegrationSyncEvidenceKind.None, IntegrationSyncStatus.Completed)]
    [Arguments(IntegrationSyncEvidenceKind.None, IntegrationSyncStatus.RetryScheduled)]
    [Arguments(IntegrationSyncEvidenceKind.None, IntegrationSyncStatus.DeadLettered)]
    [Arguments(IntegrationSyncEvidenceKind.None, IntegrationSyncStatus.Skipped)]
    [Arguments(IntegrationSyncEvidenceKind.ProviderHandoffInDoubt, IntegrationSyncStatus.Processing)]
    [Arguments(IntegrationSyncEvidenceKind.ProviderOutcomeAmbiguous, IntegrationSyncStatus.DeadLettered)]
    [Arguments(IntegrationSyncEvidenceKind.OperatorConfirmedAccepted, IntegrationSyncStatus.Completed)]
    [Arguments(IntegrationSyncEvidenceKind.OperatorRetryDefinitelyNotAccepted, IntegrationSyncStatus.RetryScheduled)]
    [Arguments(IntegrationSyncEvidenceKind.OperatorDeadLettered, IntegrationSyncStatus.DeadLettered)]
    public async Task LifecycleRetainsItsTypedEvidence(
        IntegrationSyncEvidenceKind evidence, IntegrationSyncStatus status)
    {
        var outbox = new IntegrationSyncOutbox { Status = status, EvidenceKind = evidence };

        outbox.RequireValidEvidence();

        await Assert.That(outbox.Status).IsEqualTo(status);
        await Assert.That(outbox.EvidenceKind).IsEqualTo(evidence);
    }

    [Test]
    [Arguments(IntegrationSyncEvidenceKind.ProviderHandoffInDoubt, IntegrationSyncStatus.RetryScheduled)]
    [Arguments(IntegrationSyncEvidenceKind.ProviderHandoffInDoubt, IntegrationSyncStatus.Completed)]
    [Arguments(IntegrationSyncEvidenceKind.ProviderOutcomeAmbiguous, IntegrationSyncStatus.Processing)]
    [Arguments(IntegrationSyncEvidenceKind.ProviderOutcomeAmbiguous, IntegrationSyncStatus.RetryScheduled)]
    [Arguments(IntegrationSyncEvidenceKind.OperatorConfirmedAccepted, IntegrationSyncStatus.DeadLettered)]
    [Arguments(IntegrationSyncEvidenceKind.OperatorRetryDefinitelyNotAccepted, IntegrationSyncStatus.Completed)]
    [Arguments(IntegrationSyncEvidenceKind.OperatorDeadLettered, IntegrationSyncStatus.RetryScheduled)]
    [Arguments((IntegrationSyncEvidenceKind)(-1), IntegrationSyncStatus.DeadLettered)]
    [Arguments((IntegrationSyncEvidenceKind)6, IntegrationSyncStatus.DeadLettered)]
    [Arguments(IntegrationSyncEvidenceKind.None, (IntegrationSyncStatus)0)]
    public async Task IncompatibleOrUnknownEvidenceCannotValidate(
        IntegrationSyncEvidenceKind evidence, IntegrationSyncStatus status)
    {
        var outbox = new IntegrationSyncOutbox { Status = status, EvidenceKind = evidence };

        await Assert.That(() => outbox.RequireValidEvidence()).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("provider_outcome_ambiguous")]
    [Arguments("provider_handoff_in_doubt")]
    [Arguments("operator_confirmed_accepted")]
    public async Task DiagnosticTextDoesNotCreateLifecycleEvidence(string diagnostic)
    {
        var outbox = new IntegrationSyncOutbox
        {
            Status = IntegrationSyncStatus.DeadLettered,
            LastError = diagnostic
        };

        outbox.RequireValidEvidence();

        await Assert.That(outbox.EvidenceKind).IsEqualTo(IntegrationSyncEvidenceKind.None);
        await Assert.That(outbox.LastError).IsEqualTo(diagnostic);
    }
}
