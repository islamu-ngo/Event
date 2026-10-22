using Explore.Domain;

namespace Event.Domain.UnitTests.Identity;

public sealed class LocalIdentityEligibilityLockoutTests
{
    private static readonly DateTime Start = new(2026, 10, 20, 7, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task TemporaryLockoutDeniesCurrentReadyBindingUntilItsExactDeadline()
    {
        var row = Ready();
        Guid operation = row.CurrentOperationId;
        row.SetLockout(3, operation, Start.AddMinutes(5), Start.AddSeconds(3));
        await Assert.That(row.State).IsEqualTo(LocalIdentityEligibilityState.Ready);
        await Assert.That(row.CurrentOperationId).IsEqualTo(operation);
        await Assert.That(row.Generation).IsEqualTo(4L);
        await Assert.That(row.LockoutUntilUtc).IsEqualTo(Start.AddMinutes(5));
        await Assert.That(row.IsEligibleAt(Start.AddMinutes(5).AddTicks(-1))).IsFalse();
        await Assert.That(row.IsEligibleAt(Start.AddMinutes(5))).IsTrue();
        await Assert.That(row.IsEligibleAt(Start.AddMinutes(6))).IsTrue();
    }

    [Test]
    public async Task ClearingLockoutRequiresCurrentGenerationAndDoesNotInventAnotherCredential()
    {
        var row = Ready();
        row.SetLockout(3, row.CurrentOperationId, Start.AddMinutes(5), Start.AddSeconds(3));
        var locked = Image(row);
        row.SetLockout(4, row.CurrentOperationId, null, Start.AddSeconds(4));
        await Assert.That(row.LockoutUntilUtc).IsNull();
        await Assert.That(row.Generation).IsEqualTo(5L);
        await Assert.That(row.IsEligibleAt(Start.AddSeconds(4))).IsTrue();
        await Assert.That((row.Id, row.LocalSubjectId, row.CurrentOperationId, row.PersonalActorId, row.ExternalLoginId, row.State))
            .IsEqualTo((locked.Id, locked.Subject, locked.Operation, locked.Actor, locked.Login, locked.State));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StalePointerCannotClearALockout(bool staleOperation)
    {
        var row = Ready();
        row.SetLockout(3, row.CurrentOperationId, Start.AddMinutes(5), Start.AddSeconds(3));
        var before = Image(row);
        await Assert.That(() => row.SetLockout(staleOperation ? 4 : 3,
            staleOperation ? Guid.CreateVersion7() : row.CurrentOperationId, null, Start.AddSeconds(4)))
            .Throws<InvalidOperationException>();
        await Assert.That(Image(row)).IsEqualTo(before);
    }

    [Test]
    [Arguments(DateTimeKind.Local)]
    [Arguments(DateTimeKind.Unspecified)]
    public async Task InvalidDeadlineCannotMutateTheCurrentImage(DateTimeKind kind)
    {
        var row = Ready();
        var before = Image(row);
        await Assert.That(() => row.SetLockout(3, row.CurrentOperationId,
            DateTime.SpecifyKind(Start.AddMinutes(5), kind), Start.AddSeconds(3))).Throws<ArgumentException>();
        await Assert.That(Image(row)).IsEqualTo(before);
    }

    [Test]
    [Arguments(DateTimeKind.Local)]
    [Arguments(DateTimeKind.Unspecified)]
    public async Task LockoutObservationRequiresActualUtc(DateTimeKind kind)
    {
        var row = Ready();
        await Assert.That(() => row.IsEligibleAt(DateTime.SpecifyKind(Start.AddMinutes(5), kind)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task TemporaryLockoutExpiryCannotResurrectRevokedOrDeletedCredentials()
    {
        foreach (bool deleted in new[] { false, true })
        {
            var row = Ready();
            row.SetLockout(3, row.CurrentOperationId, Start.AddMinutes(5), Start.AddSeconds(3));
            if (deleted) row.Delete(4, row.CurrentOperationId, Start.AddSeconds(4));
            else row.Revoke(4, row.CurrentOperationId, Start.AddSeconds(4));
            var before = Image(row);
            await Assert.That(row.IsEligibleAt(Start.AddMinutes(6))).IsFalse();
            await Assert.That(() => row.SetLockout(5, row.CurrentOperationId, null, Start.AddMinutes(6)))
                .Throws<InvalidOperationException>();
            await Assert.That(Image(row)).IsEqualTo(before);
        }
    }

    [Test]
    public async Task DeadlineExpiryNeverActivatesAnUnacknowledgedPendingBinding()
    {
        var row = Begin();
        row.SetLockout(1, row.CurrentOperationId, Start.AddMinutes(5), Start.AddSeconds(1));
        await Assert.That(row.State).IsEqualTo(LocalIdentityEligibilityState.ProvisioningPending);
        await Assert.That(row.IsEligibleAt(Start.AddMinutes(6))).IsFalse();
    }

    [Test]
    public async Task InvalidAuditTimeCannotPartiallyChangeAPreviousLockout()
    {
        var row = Ready();
        row.SetLockout(3, row.CurrentOperationId, Start.AddMinutes(5), Start.AddSeconds(3));
        var before = Image(row);
        await Assert.That(() => row.SetLockout(4, row.CurrentOperationId, null, Start.AddSeconds(2)))
            .Throws<ArgumentException>();
        await Assert.That(Image(row)).IsEqualTo(before);
    }

    private static LocalIdentityEligibility Begin() => LocalIdentityEligibility.Begin(Guid.CreateVersion7(),
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Start);

    private static LocalIdentityEligibility Ready()
    {
        var row = Begin();
        row.Activate(1, row.CurrentOperationId, Start.AddSeconds(1));
        row.ReplaceCredential(2, row.CurrentOperationId, Start.AddSeconds(2));
        return row;
    }

    private static ScalarImage Image(LocalIdentityEligibility row) => new(row.Id, row.LocalSubjectId,
        row.CurrentOperationId, row.PersonalActorId, row.ExternalLoginId, row.State, row.Generation,
        row.CreatedAt, row.UpdatedAt, row.LockoutUntilUtc);

    private sealed record ScalarImage(Guid Id, Guid Subject, Guid Operation, Guid Actor, Guid Login,
        LocalIdentityEligibilityState State, long Generation, DateTime CreatedAt, DateTime? UpdatedAt, DateTime? Deadline);
}
