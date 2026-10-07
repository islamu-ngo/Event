using Explore.Blazor.Configuration;

namespace Explore.Blazor.IntegrationTests.Configuration;

public sealed class AuthenticationCredentialAcquisitionReceiptTests
{
    [Test]
    public async Task SchemeInstallationCannotManufactureAcquisition()
    {
        var receipts = new AuthenticationCredentialAcquisitionReceipts();
        receipts.SetInstalled(AuthenticationCredentialAcquisitionReceipts.Consumer.GoogleOidc, true);
        await Assert.That(receipts.Snapshot()).IsEmpty();
    }

    [Test]
    public async Task RemovalAndMetadataRefreshPreserveAcquisitionIdentityAndSnapshots()
    {
        var receipts = new AuthenticationCredentialAcquisitionReceipts();
        receipts.RecordAcquired(AuthenticationCredentialAcquisitionReceipts.Consumer.GoogleOidc,
            AuthenticationCredentialAcquisitionReceipts.Authority.Environment);
        var initial = receipts.Snapshot();
        receipts.SetInstalled(AuthenticationCredentialAcquisitionReceipts.Consumer.GoogleOidc, true);
        var installed = receipts.Snapshot();
        receipts.SetInstalled(AuthenticationCredentialAcquisitionReceipts.Consumer.GoogleOidc, false);
        var removed = receipts.Snapshot();
        receipts.SetInstalled(AuthenticationCredentialAcquisitionReceipts.Consumer.GoogleOidc, true);
        await Assert.That(initial.Single().Installed).IsFalse();
        await Assert.That(installed.Single().Installed).IsTrue();
        await Assert.That(removed.Single()).IsEqualTo(initial.Single());
        await Assert.That(receipts.Snapshot().Single()).IsEqualTo(installed.Single());
        await Assert.That(() => receipts.RecordAcquired(
            AuthenticationCredentialAcquisitionReceipts.Consumer.GoogleOidc,
            AuthenticationCredentialAcquisitionReceipts.Authority.Environment))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReplacementProcessHasIndependentBootAndGenerationIdentities()
    {
        Guid approvedProfile = Guid.CreateVersion7();
        var first = new AuthenticationCredentialAcquisitionReceipts(approvedProfile);
        var replacement = new AuthenticationCredentialAcquisitionReceipts(approvedProfile);
        first.RecordAcquired(AuthenticationCredentialAcquisitionReceipts.Consumer.KeycloakOidc,
            AuthenticationCredentialAcquisitionReceipts.Authority.Infisical);
        replacement.RecordAcquired(AuthenticationCredentialAcquisitionReceipts.Consumer.KeycloakOidc,
            AuthenticationCredentialAcquisitionReceipts.Authority.Infisical);
        await Assert.That(replacement.ProcessBootId).IsNotEqualTo(first.ProcessBootId);
        await Assert.That(replacement.Snapshot().Single().Generation)
            .IsNotEqualTo(first.Snapshot().Single().Generation);
        await Assert.That(replacement.Snapshot().Single().Installed).IsFalse();
        await Assert.That(first.Snapshot().Single().AuthorityProfileRevision).IsEqualTo(approvedProfile);
        await Assert.That(replacement.Snapshot().Single().AuthorityProfileRevision).IsEqualTo(approvedProfile);
    }
}
