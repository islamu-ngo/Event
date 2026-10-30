using Explore.Domain.SetupLive;
using Explore.Domain.Secrets;

namespace Event.Domain.UnitTests.SetupLive;

public sealed class SetupSecretBindingNativeEvidenceTests
{
    [Test]
    public async Task AnAcceptedOperationCannotClaimSuccessWithoutAppliedSourceEvidence()
    {
        var operation = Accepted();
        Action settle = () => operation.Succeed(operation.CreatedAt.AddSeconds(1));
        await Assert.That(settle).Throws<InvalidOperationException>();
        await Assert.That(operation.State).IsEqualTo(SetupSecretBindingOperationState.Accepted);
    }

    [Test]
    public async Task UnobservedEvidenceCannotBeMistakenForProvenNonDispatch()
    {
        var operation = Accepted();
        await Assert.That(operation.MutationEvidence).IsNull();
        Action cancel = () => operation.Cancel(operation.CreatedAt.AddSeconds(1));
        await Assert.That(cancel).Throws<InvalidOperationException>();
        await Assert.That(operation.State).IsEqualTo(SetupSecretBindingOperationState.Accepted);
    }

    [Test]
    [Arguments(SecretMutationEvidence.NotDispatched)]
    [Arguments(SecretMutationEvidence.PossiblyDispatched)]
    [Arguments(SecretMutationEvidence.Pending)]
    [Arguments(SecretMutationEvidence.Rejected)]
    [Arguments(SecretMutationEvidence.Conflict)]
    public async Task NonAppliedEvidenceCannotBecomeReady(SecretMutationEvidence evidence)
    {
        var operation = Accepted();
        operation.RecordMutationEvidence(evidence);
        Action succeed = () => operation.Succeed(operation.CreatedAt.AddSeconds(1));
        await Assert.That(succeed).Throws<InvalidOperationException>();
        await Assert.That(operation.State).IsEqualTo(SetupSecretBindingOperationState.Accepted);
        await Assert.That(operation.MutationEvidence).IsEqualTo(evidence);
    }

    [Test]
    [Arguments(SecretMutationEvidence.PossiblyDispatched)]
    [Arguments(SecretMutationEvidence.Applied)]
    [Arguments(SecretMutationEvidence.Pending)]
    [Arguments(SecretMutationEvidence.Rejected)]
    [Arguments(SecretMutationEvidence.Conflict)]
    public async Task DispatchEvidenceCannotBeRewrittenAsCancellation(SecretMutationEvidence evidence)
    {
        var operation = Accepted();
        operation.RecordMutationEvidence(evidence);
        Action cancel = () => operation.Cancel(operation.CreatedAt.AddSeconds(1));
        await Assert.That(cancel).Throws<InvalidOperationException>();
        await Assert.That(operation.MutationEvidence).IsEqualTo(evidence);
    }

    [Test]
    [Arguments(SecretMutationEvidence.NotDispatched)]
    [Arguments(SecretMutationEvidence.PossiblyDispatched)]
    [Arguments(SecretMutationEvidence.Applied)]
    [Arguments(SecretMutationEvidence.Pending)]
    [Arguments(SecretMutationEvidence.Rejected)]
    [Arguments(SecretMutationEvidence.Conflict)]
    public async Task DeliveryFailureRetainsSourceEvidenceAndCannotAcceptAReplacement(
        SecretMutationEvidence evidence)
    {
        var operation = Accepted();
        operation.RecordMutationEvidence(evidence);
        operation.Fail(SetupSecretBindingOperationOutcome.Unavailable, operation.CreatedAt.AddSeconds(1));
        await Assert.That(operation.MutationEvidence).IsEqualTo(evidence);
        Action replace = () => operation.RecordMutationEvidence(evidence == SecretMutationEvidence.Applied
            ? SecretMutationEvidence.NotDispatched : SecretMutationEvidence.Applied);
        await Assert.That(replace).Throws<InvalidOperationException>();
        await Assert.That(operation.MutationEvidence).IsEqualTo(evidence);
    }

    [Test]
    public async Task AppliedSourceCanSucceedAndProvenNonDispatchCanCancel()
    {
        var applied = Accepted();
        applied.RecordMutationEvidence(SecretMutationEvidence.Applied);
        await Assert.That(applied.Succeed(applied.CreatedAt.AddSeconds(1))).IsTrue();
        var undispatched = Accepted();
        undispatched.RecordMutationEvidence(SecretMutationEvidence.NotDispatched);
        await Assert.That(undispatched.Cancel(undispatched.CreatedAt.AddSeconds(1))).IsTrue();
    }

    private static SetupSecretBindingOperation Accepted() => SetupSecretBindingOperation.CreateAccepted(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        1, Guid.CreateVersion7(), "setup.signing", new string('a', 64), 1, new string('b', 64),
        new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc));
}
