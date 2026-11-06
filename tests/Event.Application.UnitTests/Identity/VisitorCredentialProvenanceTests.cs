using Explore.Application.Contracts.Identity;

namespace Event.Application.UnitTests.Identity;

public sealed class VisitorCredentialProvenanceTests
{
    private static readonly DateTime Created = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task PendingVisitorHasEnrollmentAuthorityWithoutAdministratorVerification()
    {
        var receipt = Visitor();
        var status = new LocalCredentialOperationStatus(receipt, Guid.CreateVersion7(),
            null, null, null, true, LocalCredentialState.RegistrationPending, null);

        await Assert.That(status.Receipt.VisitorEnrollmentOperationId).IsNotNull();
        await Assert.That(status.Receipt.InitiatingApplicationUserId).IsNull();
        await Assert.That(status.VerifiedByApplicationUserId).IsNull();
        await Assert.That(status.VerifiedAt).IsNull();
        await Assert.That(status.CredentialState).IsEqualTo(LocalCredentialState.RegistrationPending);
    }

    [Test]
    public async Task VisitorCannotClaimAdministratorVerification()
    {
        var receipt = Visitor(LocalCredentialOperationStage.Replaced);
        await Assert.That(() => new LocalCredentialOperationStatus(receipt, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Created, Created, true, LocalCredentialState.Ready, null))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task VisitorCannotEnterAdministratorReplacementChallenge()
    {
        await Assert.That(() => Visitor(LocalCredentialOperationStage.ChangeRequired))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PendingVisitorCannotBePresentedAsReady()
    {
        await Assert.That(() => new LocalCredentialOperationStatus(Visitor(), Guid.CreateVersion7(),
            null, null, null, true, LocalCredentialState.Ready, null))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task VisitorCreationCannotBorrowAdministratorInitiation()
    {
        await Assert.That(() => new LocalCredentialOperationReceipt(
            Guid.CreateVersion7(), LocalCredentialOperationKind.VisitorCreate,
            LocalCredentialOperationStage.ProvisioningPending, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Created,
            Guid.CreateVersion7())).Throws<ArgumentException>();
    }

    [Test]
    public async Task AdministratorCreationCannotBorrowVisitorEnrollment()
    {
        await Assert.That(() => new LocalCredentialOperationReceipt(
            Guid.CreateVersion7(), LocalCredentialOperationKind.Create,
            LocalCredentialOperationStage.ProvisioningPending, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Created,
            Guid.CreateVersion7())).Throws<ArgumentException>();
    }

    [Test]
    public async Task VisitorOriginResetRetainsResetAdministratorAndOriginalEnrollment()
    {
        Guid administrator = Guid.CreateVersion7();
        Guid enrollment = Guid.CreateVersion7();
        var operation = new LocalCredentialOperationReceipt(Guid.CreateVersion7(),
            LocalCredentialOperationKind.Reset, LocalCredentialOperationStage.ChangeRequired,
            administrator, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Created, enrollment);
        var audit = new LocalCredentialResetReceipt(operation, Guid.CreateVersion7(),
            Guid.CreateVersion7(), "Replace a lost credential");

        var status = new LocalCredentialOperationStatus(operation, Guid.CreateVersion7(),
            null, null, null, true, LocalCredentialState.ChangeRequired, audit);

        await Assert.That(status.Receipt.InitiatingApplicationUserId).IsEqualTo(administrator);
        await Assert.That(status.Receipt.VisitorEnrollmentOperationId).IsEqualTo(enrollment);
        await Assert.That(status.VerifiedByApplicationUserId).IsNull();
        await Assert.That(status.VerifiedAt).IsNull();
        await Assert.That(status.ResetAudit).IsEqualTo(audit);
    }

    [Test]
    public async Task VisitorOriginResetCannotReattributeVerificationToResettingAdministrator()
    {
        Guid administrator = Guid.CreateVersion7();
        var operation = new LocalCredentialOperationReceipt(Guid.CreateVersion7(),
            LocalCredentialOperationKind.Reset, LocalCredentialOperationStage.ChangeRequired,
            administrator, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Created, Guid.CreateVersion7());
        var audit = new LocalCredentialResetReceipt(operation, Guid.CreateVersion7(),
            Guid.CreateVersion7(), "Replace a lost credential");

        await Assert.That(() => new LocalCredentialOperationStatus(operation, Guid.CreateVersion7(),
            administrator, Created, null, true, LocalCredentialState.ChangeRequired, audit))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AdministratorOriginResetCannotDiscardVerification()
    {
        var operation = new LocalCredentialOperationReceipt(Guid.CreateVersion7(),
            LocalCredentialOperationKind.Reset, LocalCredentialOperationStage.ChangeRequired,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Created, null);
        var audit = new LocalCredentialResetReceipt(operation, Guid.CreateVersion7(),
            Guid.CreateVersion7(), "Replace a lost credential");

        await Assert.That(() => new LocalCredentialOperationStatus(operation, Guid.CreateVersion7(),
            null, null, null, true, LocalCredentialState.ChangeRequired, audit))
            .Throws<ArgumentException>();
    }

    private static LocalCredentialOperationReceipt Visitor(
        LocalCredentialOperationStage stage = LocalCredentialOperationStage.ProvisioningPending) =>
        new(Guid.CreateVersion7(), LocalCredentialOperationKind.VisitorCreate, stage, null,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Created,
            Guid.CreateVersion7());
}
