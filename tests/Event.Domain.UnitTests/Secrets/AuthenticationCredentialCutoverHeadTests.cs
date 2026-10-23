using Explore.Domain.Secrets;

namespace Event.Domain.UnitTests.Secrets;

public sealed class AuthenticationCredentialCutoverHeadTests
{
    [Test]
    public async Task HeadCapturesPreparedInstanceAndCurrentOperation()
    {
        var operation = CreateOperation(Guid.CreateVersion7());
        var head = AuthenticationCredentialCutoverHead.Create(operation);

        await Assert.That(head.InstanceId).IsEqualTo(operation.InstanceId);
        await Assert.That(head.CurrentOperationId).IsEqualTo(operation.Id);
        await Assert.That(head.ConcurrencyStamp).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task HeadCannotStartFromAnAlreadyActiveReceipt()
    {
        var operation = CreateOperation(Guid.CreateVersion7());
        operation.StartCollecting(operation.ConcurrencyStamp, operation.CreatedAt.AddSeconds(1));

        await Assert.That(() => AuthenticationCredentialCutoverHead.Create(operation))
            .Throws<InvalidOperationException>();
        await Assert.That(operation.State).IsEqualTo(AuthenticationCredentialCutover.CutoverState.Collecting);
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task GuardRejectsStalePointerOrStampWithoutMutation(bool wrongOperation, bool wrongStamp)
    {
        var operation = CreateOperation(Guid.CreateVersion7());
        var head = AuthenticationCredentialCutoverHead.Create(operation);
        Guid stamp = head.ConcurrencyStamp;

        await Assert.That(() => head.Guard(
            wrongOperation ? Guid.CreateVersion7() : operation.Id,
            wrongStamp ? Guid.CreateVersion7() : stamp))
            .Throws<InvalidOperationException>();

        await Assert.That(head.CurrentOperationId).IsEqualTo(operation.Id);
        await Assert.That(head.ConcurrencyStamp).IsEqualTo(stamp);
    }

    [Test]
    public async Task GuardAdvancesStampWithoutChangingCurrentOperation()
    {
        var operation = CreateOperation(Guid.CreateVersion7());
        var head = AuthenticationCredentialCutoverHead.Create(operation);
        Guid stamp = head.ConcurrencyStamp;

        head.Guard(operation.Id, stamp);

        await Assert.That(head.CurrentOperationId).IsEqualTo(operation.Id);
        await Assert.That(head.ConcurrencyStamp).IsNotEqualTo(stamp);
    }

    [Test]
    [Arguments("different-instance")]
    [Arguments("same-operation")]
    [Arguments("already-active")]
    public async Task ReplacementRejectsInvalidTargetWithoutMovingHead(string target)
    {
        var operation = CreateOperation(Guid.CreateVersion7());
        var head = AuthenticationCredentialCutoverHead.Create(operation);
        Guid stamp = head.ConcurrencyStamp;
        var replacement = target == "same-operation"
            ? operation
            : CreateOperation(target == "different-instance" ? Guid.CreateVersion7() : operation.InstanceId);
        if (target == "already-active")
            replacement.StartCollecting(replacement.ConcurrencyStamp, replacement.CreatedAt.AddSeconds(1));

        await Assert.That(() => head.Replace(replacement, operation.Id, stamp))
            .Throws<InvalidOperationException>();

        await Assert.That(head.CurrentOperationId).IsEqualTo(operation.Id);
        await Assert.That(head.ConcurrencyStamp).IsEqualTo(stamp);
    }

    [Test]
    public async Task ReplacementMovesOnlyTheGuardedInstancePointer()
    {
        var operation = CreateOperation(Guid.CreateVersion7());
        var head = AuthenticationCredentialCutoverHead.Create(operation);
        var replacement = CreateOperation(operation.InstanceId);
        Guid stamp = head.ConcurrencyStamp;

        head.Replace(replacement, operation.Id, stamp);

        await Assert.That(head.InstanceId).IsEqualTo(operation.InstanceId);
        await Assert.That(head.CurrentOperationId).IsEqualTo(replacement.Id);
        await Assert.That(head.ConcurrencyStamp).IsNotEqualTo(stamp);
        await Assert.That(operation.State).IsEqualTo(AuthenticationCredentialCutover.CutoverState.Prepared);
    }

    private static AuthenticationCredentialCutover CreateOperation(Guid instanceId)
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        return AuthenticationCredentialCutover.Create(instanceId, Guid.CreateVersion7(),
            Guid.CreateVersion7(), now, now.AddMinutes(10), Guid.CreateVersion7(),
            AuthenticationCredentialCutover.CredentialAuthority.Infisical, Guid.CreateVersion7(),
            [new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                AuthenticationCredentialCutover.CredentialConsumer.GoogleOidc, Guid.CreateVersion7())]);
    }
}
