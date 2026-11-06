using Explore.Domain;
using Explore.Domain.Interfaces;

namespace Event.Domain.UnitTests.Identity;

public sealed class VisitorEnrollmentOperationTests
{
    private static readonly DateTime Start = new(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);
    private const string Intent = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public async Task CleanupAcknowledgementRetainsCanceledEvidenceAndDoesNotRenewOnReplay()
    {
        var row = Reserve();
        row.Cancel(1, row.CredentialOperationId, Start.AddMinutes(15));
        var original = (row.Id, row.TenantId, row.LocalSubjectId, row.PersonalActorId,
            row.ExternalLoginId, row.CredentialOperationId, row.IntentDigest, row.ExpiresAt);
        row.AcknowledgeCancellationCleanup(2, row.CredentialOperationId, Start.AddMinutes(16));
        await Assert.That(row.Stage).IsEqualTo(VisitorEnrollmentStage.Canceled);
        await Assert.That(row.Generation).IsEqualTo(3L);
        await Assert.That(row.CleanupCompletedAt).IsEqualTo(Start.AddMinutes(16));
        row.AcknowledgeCancellationCleanup(3, row.CredentialOperationId, Start.AddHours(1));
        await Assert.That(row.Generation).IsEqualTo(3L);
        await Assert.That(row.UpdatedAt).IsEqualTo(Start.AddMinutes(16));
        await Assert.That(row.CleanupCompletedAt).IsEqualTo(Start.AddMinutes(16));
        await Assert.That((row.Id, row.TenantId, row.LocalSubjectId, row.PersonalActorId,
            row.ExternalLoginId, row.CredentialOperationId, row.IntentDigest, row.ExpiresAt)).IsEqualTo(original);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task UncanceledEnrollmentCannotAcknowledgeCleanup(int stage)
    {
        var row = Reserve();
        if (stage >= 2) row.RecordIdentityPrepared(1, row.CredentialOperationId, 1, Start.AddSeconds(1));
        if (stage >= 3)
            row.RecordPrimaryAdmission(2, row.CredentialOperationId, Guid.CreateVersion7(), Start.AddSeconds(2));
        if (stage == 4) row.Activate(3, row.CredentialOperationId, Start.AddSeconds(3));
        long generation = row.Generation;
        await Assert.That(() => row.AcknowledgeCancellationCleanup(
            generation, row.CredentialOperationId, Start.AddMinutes(16))).Throws<InvalidOperationException>();
        await Assert.That(row.Generation).IsEqualTo(generation);
        await Assert.That(row.CleanupCompletedAt).IsNull();
    }

    [Test]
    public async Task CleanupAcknowledgementRequiresOriginalGenerationOperationAndTimeline()
    {
        var row = Reserve();
        row.Cancel(1, row.CredentialOperationId, Start.AddMinutes(15));
        await Assert.That(() => row.AcknowledgeCancellationCleanup(1, row.CredentialOperationId, Start.AddMinutes(16)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => row.AcknowledgeCancellationCleanup(2, Guid.CreateVersion7(), Start.AddMinutes(16)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => row.AcknowledgeCancellationCleanup(2, row.CredentialOperationId, Start.AddMinutes(14)))
            .Throws<InvalidOperationException>();
        await Assert.That(row.Generation).IsEqualTo(2L);
        await Assert.That(row.CleanupCompletedAt).IsNull();
    }

    [Test]
    public async Task ReservationRetainsOnlyBoundIdentifiersAndIntentDigest()
    {
        var row = Reserve();
        await Assert.That(row.Stage).IsEqualTo(VisitorEnrollmentStage.Reserved);
        await Assert.That(row.Generation).IsEqualTo(1L);
        await Assert.That(row.CreatedAt).IsEqualTo(Start);
        await Assert.That(row.ExpiresAt).IsEqualTo(Start.AddMinutes(15));
        await Assert.That(row.UpdatedAt).IsNull();
        await Assert.That(row.TenantUserId).IsNull();
        await Assert.That(row.CredentialGeneration).IsNull();
        await Assert.That(row.IntentDigest).IsEqualTo(Intent);
        Guid tenant = row.TenantId;
        ((ITenantEntity)row).TenantId = tenant;
        await Assert.That(() => { ((ITenantEntity)row).TenantId = Guid.CreateVersion7(); })
            .Throws<InvalidOperationException>();
        await Assert.That(row.TenantId).IsEqualTo(tenant);
    }

    [Test]
    public async Task OnlyOrderedSettlementActivatesTheOriginalPreparedIdentity()
    {
        var row = Reserve();
        var binding = (row.Id, row.TenantId, row.LocalSubjectId, row.PersonalActorId,
            row.ExternalLoginId, row.CredentialOperationId, row.IntentDigest, row.ExpiresAt);
        Guid membership = Guid.CreateVersion7();
        row.RecordIdentityPrepared(1, row.CredentialOperationId, 4, Start.AddSeconds(1));
        await Assert.That(row.Stage).IsEqualTo(VisitorEnrollmentStage.IdentityPrepared);
        await Assert.That(row.CredentialGeneration).IsEqualTo(4L);
        row.RecordPrimaryAdmission(2, row.CredentialOperationId, membership, Start.AddSeconds(2));
        await Assert.That(row.Stage).IsEqualTo(VisitorEnrollmentStage.PrimaryAdmitted);
        await Assert.That(row.TenantUserId).IsEqualTo(membership);
        row.Activate(3, row.CredentialOperationId, Start.AddSeconds(3));
        await Assert.That(row.Stage).IsEqualTo(VisitorEnrollmentStage.Active);
        await Assert.That(row.Generation).IsEqualTo(4L);
        await Assert.That((row.Id, row.TenantId, row.LocalSubjectId, row.PersonalActorId,
            row.ExternalLoginId, row.CredentialOperationId, row.IntentDigest, row.ExpiresAt)).IsEqualTo(binding);
    }

    [Test]
    public async Task ReservationCannotSkipIdentityOrPrimaryAdmission()
    {
        var row = Reserve();
        await Assert.That(() =>
            row.RecordPrimaryAdmission(1, row.CredentialOperationId, Guid.CreateVersion7(), Start.AddSeconds(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => row.Activate(1, row.CredentialOperationId, Start.AddSeconds(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(row.Generation).IsEqualTo(1L);
        await Assert.That(row.Stage).IsEqualTo(VisitorEnrollmentStage.Reserved);
    }

    [Test]
    public async Task PendingIdentityCannotActivateBeforePrimaryCommit()
    {
        var row = Reserve();
        row.RecordIdentityPrepared(1, row.CredentialOperationId, 1, Start.AddSeconds(1));
        await Assert.That(() => row.Activate(2, row.CredentialOperationId, Start.AddSeconds(2)))
            .Throws<InvalidOperationException>();
        await Assert.That(row.Stage).IsEqualTo(VisitorEnrollmentStage.IdentityPrepared);
        await Assert.That(row.Generation).IsEqualTo(2L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationCannotOvertakePrimaryAdmission(bool active)
    {
        var row = Reserve();
        row.RecordIdentityPrepared(1, row.CredentialOperationId, 1, Start.AddSeconds(1));
        row.RecordPrimaryAdmission(2, row.CredentialOperationId, Guid.CreateVersion7(), Start.AddSeconds(2));
        if (active) row.Activate(3, row.CredentialOperationId, Start.AddSeconds(3));
        long generation = row.Generation;
        await Assert.That(() => row.Cancel(generation, row.CredentialOperationId, Start.AddMinutes(16)))
            .Throws<InvalidOperationException>();
        await Assert.That(row.Generation).IsEqualTo(generation);
        await Assert.That(row.Stage).IsEqualTo(active ? VisitorEnrollmentStage.Active : VisitorEnrollmentStage.PrimaryAdmitted);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CanceledOperationCannotPrepareOrCommitMembership(bool prepared)
    {
        var row = Reserve();
        if (prepared) row.RecordIdentityPrepared(1, row.CredentialOperationId, 1, Start.AddSeconds(1));
        row.Cancel(row.Generation, row.CredentialOperationId, Start.AddMinutes(15));
        long generation = row.Generation;
        await Assert.That(() =>
            row.RecordIdentityPrepared(generation, row.CredentialOperationId, 1, Start.AddMinutes(16)))
            .Throws<InvalidOperationException>();
        await Assert.That(() =>
            row.RecordPrimaryAdmission(generation, row.CredentialOperationId, Guid.CreateVersion7(), Start.AddMinutes(16)))
            .Throws<InvalidOperationException>();
        await Assert.That(row.Stage).IsEqualTo(VisitorEnrollmentStage.Canceled);
        await Assert.That(row.Generation).IsEqualTo(generation);
        await Assert.That(row.TenantUserId).IsNull();
    }

    [Test]
    [Arguments(15)]
    [Arguments(16)]
    public async Task ExpiryPreventsUnadmittedProgressButNotCancellation(int minutes)
    {
        var row = Reserve();
        await Assert.That(() => row.RecordIdentityPrepared(1, row.CredentialOperationId, 1, Start.AddMinutes(minutes)))
            .Throws<InvalidOperationException>();
        row.RecordIdentityPrepared(1, row.CredentialOperationId, 1, Start.AddSeconds(1));
        await Assert.That(() =>
            row.RecordPrimaryAdmission(2, row.CredentialOperationId, Guid.CreateVersion7(), Start.AddMinutes(minutes)))
            .Throws<InvalidOperationException>();
        row.Cancel(2, row.CredentialOperationId, Start.AddMinutes(minutes));
        await Assert.That(row.Stage).IsEqualTo(VisitorEnrollmentStage.Canceled);
        await Assert.That(row.TenantUserId).IsNull();
    }

    [Test]
    public async Task CommittedAdmissionMayRecoverActivationAfterTheReservationExpires()
    {
        var row = Reserve();
        row.RecordIdentityPrepared(1, row.CredentialOperationId, 1, Start.AddSeconds(1));
        row.RecordPrimaryAdmission(2, row.CredentialOperationId, Guid.CreateVersion7(), Start.AddSeconds(2));
        row.Activate(3, row.CredentialOperationId, Start.AddHours(1));
        await Assert.That(row.Stage).IsEqualTo(VisitorEnrollmentStage.Active);
    }

    [Test]
    public async Task StaleGenerationOrDifferentNativeOperationCannotPrepareTheReceipt()
    {
        var row = Reserve();
        await Assert.That(() => row.RecordIdentityPrepared(2, row.CredentialOperationId, 1, Start.AddSeconds(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => row.RecordIdentityPrepared(1, Guid.CreateVersion7(), 1, Start.AddSeconds(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(row.Generation).IsEqualTo(1L);
        await Assert.That(row.Stage).IsEqualTo(VisitorEnrollmentStage.Reserved);
    }

    [Test]
    public async Task InvalidTimeOrCredentialGenerationCannotAdvanceTheReceipt()
    {
        var row = Reserve();
        await Assert.That(() =>
            row.RecordIdentityPrepared(1, row.CredentialOperationId, 1, DateTime.SpecifyKind(Start, DateTimeKind.Unspecified)))
            .Throws<ArgumentException>();
        await Assert.That(() => row.RecordIdentityPrepared(1, row.CredentialOperationId, 1, Start.AddTicks(-1)))
            .Throws<InvalidOperationException>();
        await Assert.That(() => row.RecordIdentityPrepared(1, row.CredentialOperationId, 0, Start.AddSeconds(1)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(row.Generation).IsEqualTo(1L);
    }

    [Test]
    [Arguments("")]
    [Arguments("unprotected-browser-intent")]
    [Arguments("gggggggggggggggg" + "gggggggggggggggg" + "gggggggggggggggg" + "gggggggggggggggg")]
    public async Task MalformedIntentDigestCannotReserveEnrollment(string digest)
    {
        await Assert.That(() => VisitorEnrollmentOperation.Reserve(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), digest, Start)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StaleOrForeignRecoveryCannotActivateCommittedAdmission(bool foreignOperation)
    {
        var row = Reserve();
        Guid membership = Guid.CreateVersion7();
        row.RecordIdentityPrepared(1, row.CredentialOperationId, 4, Start.AddSeconds(1));
        row.RecordPrimaryAdmission(2, row.CredentialOperationId, membership, Start.AddSeconds(2));

        await Assert.That(() => row.Activate(foreignOperation ? 3 : 2,
            foreignOperation ? Guid.CreateVersion7() : row.CredentialOperationId, Start.AddHours(1)))
            .Throws<InvalidOperationException>();

        await Assert.That(row.Stage).IsEqualTo(VisitorEnrollmentStage.PrimaryAdmitted);
        await Assert.That(row.Generation).IsEqualTo(3L);
        await Assert.That(row.TenantUserId).IsEqualTo(membership);
        await Assert.That(row.CredentialGeneration).IsEqualTo(4L);
    }

    [Test]
    public async Task ReplayedAdmissionCannotReplaceCommittedMembership()
    {
        var row = Reserve();
        Guid membership = Guid.CreateVersion7();
        row.RecordIdentityPrepared(1, row.CredentialOperationId, 4, Start.AddSeconds(1));
        row.RecordPrimaryAdmission(2, row.CredentialOperationId, membership, Start.AddSeconds(2));

        await Assert.That(() => row.RecordPrimaryAdmission(
            2, row.CredentialOperationId, Guid.CreateVersion7(), Start.AddSeconds(3)))
            .Throws<InvalidOperationException>();

        await Assert.That(row.Stage).IsEqualTo(VisitorEnrollmentStage.PrimaryAdmitted);
        await Assert.That(row.Generation).IsEqualTo(3L);
        await Assert.That(row.TenantUserId).IsEqualTo(membership);
    }

    private static VisitorEnrollmentOperation Reserve() => VisitorEnrollmentOperation.Reserve(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        Guid.CreateVersion7(), Guid.CreateVersion7(), Intent, Start);
}
