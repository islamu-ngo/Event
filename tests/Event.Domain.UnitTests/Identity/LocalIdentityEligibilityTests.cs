using Explore.Domain;

namespace Event.Domain.UnitTests.Identity;

public sealed class LocalIdentityEligibilityTests
{
    private static readonly DateTime Start = new(2026, 10, 20, 7, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task PendingCreationCannotIssueCurrentEligibility()
    {
        var row = Begin();
        await Assert.That(row.Id.Version).IsEqualTo(7);
        await Assert.That(row.Generation).IsEqualTo(1L);
        await Assert.That(row.State).IsEqualTo(LocalIdentityEligibilityState.ProvisioningPending);
        await Assert.That(row.IsEligible).IsFalse();
        await Assert.That(Current(row)).IsFalse();
        await Assert.That(row.CreatedAt).IsEqualTo(Start);
        await Assert.That(row.UpdatedAt).IsNull();
    }

    [Test]
    public async Task ActivationMakesOnlyTheReservedCurrentBindingEligible()
    {
        var row = Begin();
        var reserved = Image(row);
        row.Activate(1, row.CurrentOperationId, Start.AddSeconds(1));
        await Assert.That(row.Generation).IsEqualTo(2L);
        await Assert.That(row.State).IsEqualTo(LocalIdentityEligibilityState.ChangeRequired);
        await Assert.That(row.IsEligible).IsTrue();
        await Assert.That(Current(row)).IsTrue();
        await Assert.That((row.Id, row.LocalSubjectId, row.CurrentOperationId, row.PersonalActorId, row.ExternalLoginId))
            .IsEqualTo((reserved.Id, reserved.Subject, reserved.Operation, reserved.Actor, reserved.Login));
    }

    [Test]
    public async Task ReplacementAdvancesIndependentGenerationWithoutInventingAnotherOperation()
    {
        var row = Begin();
        row.Activate(1, row.CurrentOperationId, Start.AddSeconds(1));
        var previous = Image(row);
        row.ReplaceCredential(2, row.CurrentOperationId, Start.AddSeconds(2));
        await Assert.That(row.State).IsEqualTo(LocalIdentityEligibilityState.Ready);
        await Assert.That(row.Generation).IsEqualTo(3L);
        await Assert.That(row.CurrentOperationId).IsEqualTo(previous.Operation);
        await Assert.That(Matches(row, previous)).IsFalse();
        await Assert.That(Current(row)).IsTrue();
    }

    [Test]
    public async Task ResetReplacesTheCurrentOperationAndPermanentlyInvalidatesItsOldReadyImage()
    {
        var row = Ready();
        var previous = Image(row);
        Guid nextOperation = Guid.CreateVersion7();
        row.Reset(3, previous.Operation, nextOperation, Start.AddSeconds(3));
        await Assert.That(row.CurrentOperationId).IsEqualTo(nextOperation);
        await Assert.That(row.State).IsEqualTo(LocalIdentityEligibilityState.ChangeRequired);
        await Assert.That(row.Generation).IsEqualTo(4L);
        await Assert.That(Matches(row, previous)).IsFalse();
        await Assert.That(Current(row)).IsTrue();
        await Assert.That((row.LocalSubjectId, row.PersonalActorId, row.ExternalLoginId))
            .IsEqualTo((previous.Subject, previous.Actor, previous.Login));
    }

    [Test]
    public async Task BindingReplacementInvalidatesTheOldActorAndLoginDespiteAnUnchangedOperation()
    {
        var row = Ready();
        var previous = Image(row);
        Guid actor = Guid.CreateVersion7(), login = Guid.CreateVersion7();
        row.Rebind(3, previous.Operation, actor, login, Start.AddSeconds(3));
        await Assert.That(row.PersonalActorId).IsEqualTo(actor);
        await Assert.That(row.ExternalLoginId).IsEqualTo(login);
        await Assert.That(row.CurrentOperationId).IsEqualTo(previous.Operation);
        await Assert.That(row.Generation).IsEqualTo(4L);
        await Assert.That(Matches(row, previous)).IsFalse();
        await Assert.That(Current(row)).IsTrue();
    }

    [Test]
    public async Task CredentialOrRecoveryRefreshInvalidatesOldEvidenceEvenWhenItsStateAndPointersDoNotChange()
    {
        var row = Ready();
        var previous = Image(row);
        row.Refresh(3, previous.Operation, Start.AddSeconds(3));
        await Assert.That(row.Generation).IsEqualTo(4L);
        await Assert.That((row.State, row.CurrentOperationId, row.PersonalActorId, row.ExternalLoginId))
            .IsEqualTo((previous.State, previous.Operation, previous.Actor, previous.Login));
        await Assert.That(Matches(row, previous)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task CurrentEvidenceRequiresEveryExactPointerStateAndGeneration(int changed)
    {
        var row = Ready();
        var before = Image(row);
        bool current = row.IsCurrent(changed == 0 ? Guid.CreateVersion7() : row.LocalSubjectId,
            changed == 1 ? Guid.CreateVersion7() : row.CurrentOperationId,
            changed == 2 ? Guid.CreateVersion7() : row.PersonalActorId,
            changed == 3 ? Guid.CreateVersion7() : row.ExternalLoginId,
            changed == 4 ? LocalIdentityEligibilityState.ChangeRequired : row.State,
            changed == 5 ? row.Generation + 1 : row.Generation);
        await Assert.That(current).IsFalse();
        await Assert.That(Image(row)).IsEqualTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StaleGenerationOrOperationCannotMutateTheCurrentImage(bool staleOperation)
    {
        var row = Ready();
        var before = Image(row);
        await Assert.That(() => row.Reset(staleOperation ? row.Generation : row.Generation - 1,
                staleOperation ? Guid.CreateVersion7() : row.CurrentOperationId, Guid.CreateVersion7(), Start.AddSeconds(3)))
            .Throws<InvalidOperationException>();
        await Assert.That(Image(row)).IsEqualTo(before);
    }

    [Test]
    public async Task ResetCannotReuseItsCurrentOperation()
    {
        var row = Ready();
        var before = Image(row);
        await Assert.That(() => row.Reset(row.Generation, row.CurrentOperationId, row.CurrentOperationId, Start.AddSeconds(3)))
            .Throws<ArgumentException>();
        await Assert.That(Image(row)).IsEqualTo(before);
    }

    [Test]
    public async Task RevocationAdvancesGenerationAndCannotBeSilentlyRefreshedBackToEligibility()
    {
        var row = Ready();
        var previous = Image(row);
        row.Revoke(3, row.CurrentOperationId, Start.AddSeconds(3));
        var revoked = Image(row);
        await Assert.That(row.State).IsEqualTo(LocalIdentityEligibilityState.Revoked);
        await Assert.That(row.Generation).IsEqualTo(4L);
        await Assert.That(row.IsEligible).IsFalse();
        await Assert.That(Current(row)).IsFalse();
        await Assert.That(Matches(row, previous)).IsFalse();
        await Assert.That(() => row.Refresh(4, row.CurrentOperationId, Start.AddSeconds(4))).Throws<InvalidOperationException>();
        await Assert.That(Image(row)).IsEqualTo(revoked);
    }

    [Test]
    public async Task DeletionCannotBeResurrectedByResetReplacementOrBindingChanges()
    {
        var row = Ready();
        row.Delete(3, row.CurrentOperationId, Start.AddSeconds(3));
        var deleted = Image(row);
        await Assert.That(row.State).IsEqualTo(LocalIdentityEligibilityState.Deleted);
        await Assert.That(row.Generation).IsEqualTo(4L);
        await Assert.That(Current(row)).IsFalse();
        foreach (Action mutation in new Action[]
        {
            () => row.Activate(4, row.CurrentOperationId, Start.AddSeconds(4)),
            () => row.ReplaceCredential(4, row.CurrentOperationId, Start.AddSeconds(4)),
            () => row.Reset(4, row.CurrentOperationId, Guid.CreateVersion7(), Start.AddSeconds(4)),
            () => row.Rebind(4, row.CurrentOperationId, Guid.CreateVersion7(), Guid.CreateVersion7(), Start.AddSeconds(4)),
            () => row.Refresh(4, row.CurrentOperationId, Start.AddSeconds(4))
        })
        {
            await Assert.That(mutation).Throws<InvalidOperationException>();
            await Assert.That(Image(row)).IsEqualTo(deleted);
        }
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(0, true)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    [Arguments(3, true)]
    public async Task CurrentPointerIdentifiersMustBeNonemptyUuidVersionSeven(int field, bool wrongVersion)
    {
        Guid[] ids = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];
        ids[field] = wrongVersion ? Guid.NewGuid() : Guid.Empty;
        await Assert.That(() => LocalIdentityEligibility.Begin(ids[0], ids[1], ids[2], ids[3], Start))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(DateTimeKind.Local)]
    [Arguments(DateTimeKind.Unspecified)]
    public async Task CreationRequiresActualUtcAuditTime(DateTimeKind kind)
    {
        await Assert.That(() => LocalIdentityEligibility.Begin(Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), DateTime.SpecifyKind(Start, kind)))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(DateTimeKind.Local)]
    [Arguments(DateTimeKind.Unspecified)]
    public async Task InvalidAuditTimeCannotPartiallyReplaceAValidCurrentOperation(DateTimeKind kind)
    {
        var row = Ready();
        var before = Image(row);
        await Assert.That(() => row.Reset(3, row.CurrentOperationId, Guid.CreateVersion7(),
                DateTime.SpecifyKind(Start.AddSeconds(3), kind))).Throws<ArgumentException>();
        await Assert.That(Image(row)).IsEqualTo(before);
    }

    [Test]
    public async Task EarlierAuditTimeCannotRegressTheCurrentImage()
    {
        var row = Ready();
        var before = Image(row);
        await Assert.That(() => row.Refresh(3, row.CurrentOperationId, Start.AddSeconds(1))).Throws<ArgumentException>();
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

    private static bool Current(LocalIdentityEligibility row) => row.IsCurrent(row.LocalSubjectId,
        row.CurrentOperationId, row.PersonalActorId, row.ExternalLoginId, row.State, row.Generation);

    private static bool Matches(LocalIdentityEligibility row, ScalarImage image) => row.IsCurrent(image.Subject,
        image.Operation, image.Actor, image.Login, image.State, image.Generation);

    private static ScalarImage Image(LocalIdentityEligibility row) => new(row.Id, row.LocalSubjectId,
        row.CurrentOperationId, row.PersonalActorId, row.ExternalLoginId, row.State, row.Generation, row.CreatedAt, row.UpdatedAt);

    private sealed record ScalarImage(Guid Id, Guid Subject, Guid Operation, Guid Actor, Guid Login,
        LocalIdentityEligibilityState State, long Generation, DateTime CreatedAt, DateTime? UpdatedAt);
}
