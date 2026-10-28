using System.Security.Cryptography;
using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Identity;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class LocalCredentialBindingTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task Received_privacy_request_blocks_Local_binding_without_changing_credentials(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalIdentityUser before = await fixture.ReadIdentityUserAsync();
        await using (var context = fixture.Lifecycle.Native.CreateDatabase())
        {
            DateTimeOffset now = fixture.Lifecycle.Clock.GetUtcNow();
            context.Set<ModerationRecognitionPending>().Add(ModerationRecognitionPending.Receive(
                Guid.CreateVersion7(), fixture.Receipt.ApplicationUserId, 1, PrivacyErasureReasonCode.AccountDeletion,
                Guid.CreateVersion7(), Guid.CreateVersion7(), now, now.AddHours(2), now.AddHours(1),
                RandomNumberGenerator.GetBytes(32), now.AddHours(3)));
            await context.SaveChangesAsync(CancellationToken);
        }

        if (topology == IdentityDatabaseTopology.External)
        {
            await AssertUnsupportedOwnerHasNoEffectsAsync(fixture);
            return;
        }

        await Assert.That(() => fixture.ReconcileAsync()).Throws<InvalidOperationException>();
        await fixture.AssertNoGraphAsync();
        LocalIdentityUser after = await fixture.ReadIdentityUserAsync();
        await Assert.That(after.PasswordHash).IsEqualTo(before.PasswordHash);
        await Assert.That(after.SecurityStamp).IsEqualTo(before.SecurityStamp);
        await fixture.AssertStateAsync(LocalCredentialState.ProvisioningPending);
    }
}
