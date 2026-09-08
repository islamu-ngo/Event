using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Admissions;
using Explore.Infrastructure.Services.Registration;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Tests.Registration;

public sealed class AdmissionContactEnvelopeBindingTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IncludedDeadlineIsReadableWithoutDecryptingButCannotBeExtendedOrStripped(bool recovery)
    {
        var provider = new EphemeralDataProtectionProvider();
        var admission = new AdmissionDeliveryEnvelopeProtector(provider);
        var recover = new AdmissionRecoveryDeliveryEnvelopeProtector(provider);
        DateTime deadline = new(2026, 9, 8, 14, 0, 0, DateTimeKind.Utc);
        string bearer = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Guid requestId = Guid.CreateVersion7();
        string material;
        int version;
        if (recovery)
        {
            var protectedCopy = recover.Protect(new("holder@example.test", requestId, bearer) { DisclosureUntilUtc = deadline });
            material = protectedCopy.Ciphertext;
            version = protectedCopy.ProtectionVersion;
        }
        else
        {
            var protectedCopy = admission.Protect(new("holder@example.test", bearer) { DisclosureUntilUtc = deadline });
            material = protectedCopy.Ciphertext;
            version = protectedCopy.ProtectionVersion;
        }
        var payload = AdmissionContactDeliveryPayload.Read(material, version);
        await Assert.That(payload.DisclosureUntilUtc).IsEqualTo(deadline);
        await Assert.That(version).IsEqualTo(2);
        await Assert.That(material).DoesNotContain(bearer);
        await Assert.That(material).DoesNotContain("holder@example.test");
        await Assert.That(Unprotect(material, version)).IsEqualTo(bearer);
        await Assert.That(() => Unprotect(JsonSerializer.Serialize(payload with { DisclosureUntilUtc = deadline.AddDays(1) }), version))
            .Throws<InvalidOperationException>();
        await Assert.That(() => Unprotect(JsonSerializer.Serialize(payload with { DisclosureUntilUtc = null }), version))
            .Throws<InvalidOperationException>();
        await Assert.That(() => Unprotect(payload.Ciphertext, 1)).Throws<InvalidOperationException>();
        await Assert.That(() => recovery ? admission.Unprotect(material, version).PlaintextCredential
            : recover.Unprotect(material, version).Capability).Throws<InvalidOperationException>();

        string Unprotect(string ciphertext, int protectionVersion) => recovery
            ? recover.Unprotect(ciphertext, protectionVersion).Capability
            : admission.Unprotect(ciphertext, protectionVersion).PlaintextCredential;
    }

    [Test]
    public async Task AccountContactAuthorityIsCryptographicallyBoundAndUnboundedOrderModeStillWritesVersionOne()
    {
        var protector = new AdmissionDeliveryEnvelopeProtector(new EphemeralDataProtectionProvider());
        string bearer = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var unbounded = protector.Protect(new("holder@example.test", bearer));
        await Assert.That(unbounded.ProtectionVersion).IsEqualTo(1);
        await Assert.That(protector.Unprotect(unbounded.Ciphertext, 1).PlaintextCredential).IsEqualTo(bearer);

        Guid userId = Guid.CreateVersion7();
        var accountCopy = protector.Protect(new("holder@example.test", bearer) { AccountUserId = userId });
        var payload = AdmissionContactDeliveryPayload.Read(accountCopy.Ciphertext, accountCopy.ProtectionVersion);
        await Assert.That(payload.AccountUserId).IsEqualTo(userId);
        await Assert.That(payload.DisclosureUntilUtc).IsNull();
        await Assert.That(protector.Unprotect(accountCopy.Ciphertext, accountCopy.ProtectionVersion).AccountUserId).IsEqualTo(userId);
        await Assert.That(() => protector.Unprotect(JsonSerializer.Serialize(payload with { AccountUserId = Guid.CreateVersion7() }), 2))
            .Throws<InvalidOperationException>();
        await Assert.That(() => protector.Unprotect(payload.Ciphertext, 1)).Throws<InvalidOperationException>();
    }
}
