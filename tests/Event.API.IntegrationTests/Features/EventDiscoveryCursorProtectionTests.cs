using System.Security.Cryptography;
using System.Text.Json;
using Explore.API.Services;
using Explore.Application.Features.Events.Discovery;
using Microsoft.AspNetCore.DataProtection;

namespace Event.API.IntegrationTests.Features;

public sealed class EventDiscoveryCursorProtectionTests
{
    [Test]
    public async Task SharedAuthorityPreservesEveryContinuationBindingAcrossServiceInstances()
    {
        var authority = new EphemeralDataProtectionProvider();
        var issuer = new EventDiscoveryCursorProtector(authority);
        var replica = new EventDiscoveryCursorProtector(authority);
        EventDiscoveryContinuation continuation = CreateContinuation();

        string cursor = issuer.Protect(continuation);

        await Assert.That(replica.Unprotect(cursor)).IsEqualTo(continuation);
    }

    [Test]
    public async Task ChangedCiphertextCannotSelectAnotherTenantSnapshotOrOrdinal()
    {
        var protector = new EventDiscoveryCursorProtector(new EphemeralDataProtectionProvider());
        string cursor = protector.Protect(CreateContinuation());
        int index = cursor.Length / 2;
        string tampered = cursor[..index] + (cursor[index] == 'A' ? 'B' : 'A') + cursor[(index + 1)..];

        await Assert.That(() => protector.Unprotect(tampered)).Throws<CryptographicException>();
    }

    [Test]
    public async Task MissingSharedAuthorityDoesNotFallBackToPlaintextOrAnotherKeyRing()
    {
        var issuer = new EventDiscoveryCursorProtector(new EphemeralDataProtectionProvider());
        var isolated = new EventDiscoveryCursorProtector(new EphemeralDataProtectionProvider());
        string cursor = issuer.Protect(CreateContinuation());

        await Assert.That(() => isolated.Unprotect(cursor)).Throws<CryptographicException>();
        await Assert.That(() => isolated.Unprotect(JsonSerializer.Serialize(CreateContinuation())))
            .Throws<CryptographicException>();
    }

    [Test]
    public async Task AnotherPurposeCannotIssueAnAuthenticatedDiscoveryContinuation()
    {
        var authority = new EphemeralDataProtectionProvider();
        var discovery = new EventDiscoveryCursorProtector(authority);
        string anotherPurpose = authority.CreateProtector("Explore.RegistrationProviderCallbackReceipt", "v1")
            .Protect(JsonSerializer.Serialize(CreateContinuation()));

        await Assert.That(() => discovery.Unprotect(anotherPurpose)).Throws<CryptographicException>();
    }

    private static EventDiscoveryContinuation CreateContinuation() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        new string('a', 64),
        100,
        new DateTimeOffset(2026, 10, 3, 12, 15, 0, TimeSpan.Zero),
        7,
        11);
}
