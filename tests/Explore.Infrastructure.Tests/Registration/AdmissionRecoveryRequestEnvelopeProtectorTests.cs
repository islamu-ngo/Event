using Explore.Application.Contracts.Admissions;
using Explore.Infrastructure.Services.Registration;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Tests.Registration;

public sealed class AdmissionRecoveryRequestEnvelopeProtectorTests
{
    private const string Identity = "PERSON@EXAMPLE.TEST";

    [Test]
    public async Task ProtectRoundTripsWithoutIdentityDisclosure()
    {
        var protector = new AdmissionRecoveryRequestEnvelopeProtector(
            new EphemeralDataProtectionProvider());
        var envelope = new AdmissionRecoveryRequestEnvelope(
            Identity,
            AdmissionRecoveryPurpose.TicketRecovery);

        AdmissionRecoveryProtectedDeliveryMaterial protectedMaterial =
            protector.Protect(envelope);
        AdmissionRecoveryRequestEnvelope restored = protector.Unprotect(
            protectedMaterial.Ciphertext,
            protectedMaterial.ProtectionVersion);

        await Assert.That(protectedMaterial.Ciphertext).DoesNotContain(Identity);
        await Assert.That(protectedMaterial.ToString()).DoesNotContain(Identity);
        await Assert.That(envelope.ToString()).DoesNotContain(Identity);
        await Assert.That(restored).IsEqualTo(envelope);
    }

    [Test]
    public async Task UnknownVersionFailsClosed()
    {
        var protector = new AdmissionRecoveryRequestEnvelopeProtector(
            new EphemeralDataProtectionProvider());
        AdmissionRecoveryProtectedDeliveryMaterial protectedMaterial = protector.Protect(
            new AdmissionRecoveryRequestEnvelope(
                Identity,
                AdmissionRecoveryPurpose.TicketRecovery));

        await Assert.That(() => protector.Unprotect(protectedMaterial.Ciphertext, 2))
            .Throws<InvalidOperationException>();
    }
}
