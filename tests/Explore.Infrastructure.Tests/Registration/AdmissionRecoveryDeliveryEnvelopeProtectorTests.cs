using Explore.Application.Contracts.Admissions;
using Explore.Infrastructure.Services.Registration;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Tests.Registration;

public sealed class AdmissionRecoveryDeliveryEnvelopeProtectorTests
{
    private static readonly Guid RequestId =
        Guid.Parse("018e4e5c-7f00-7000-8000-000000000442");
    private const string Recipient = "verified@example.test";
    private const string Capability = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Test]
    public async Task ProtectRoundTripsWithoutPlaintextOrDiagnosticDisclosure()
    {
        IAdmissionRecoveryDeliveryEnvelopeProtector protector = CreateProtector();
        var envelope = new AdmissionRecoveryDeliveryEnvelope(
            Recipient,
            RequestId,
            Capability);

        AdmissionRecoveryProtectedDeliveryMaterial protectedMaterial =
            protector.Protect(envelope);
        AdmissionRecoveryDeliveryEnvelope restored = protector.Unprotect(
            protectedMaterial.Ciphertext,
            protectedMaterial.ProtectionVersion);

        await Assert.That(protectedMaterial.Ciphertext).DoesNotContain(Recipient);
        await Assert.That(protectedMaterial.Ciphertext).DoesNotContain(Capability);
        await Assert.That(protectedMaterial.ToString()).DoesNotContain(Capability);
        await Assert.That(envelope.ToString()).DoesNotContain(Capability);
        await Assert.That(restored).IsEqualTo(envelope);
    }

    [Test]
    public async Task UnknownProtectionVersionFailsClosed()
    {
        IAdmissionRecoveryDeliveryEnvelopeProtector protector = CreateProtector();
        AdmissionRecoveryProtectedDeliveryMaterial protectedMaterial = protector.Protect(
            new AdmissionRecoveryDeliveryEnvelope(Recipient, RequestId, Capability));

        await Assert.That(() => protector.Unprotect(protectedMaterial.Ciphertext, 2))
            .Throws<InvalidOperationException>();
    }

    private static IAdmissionRecoveryDeliveryEnvelopeProtector CreateProtector()
    {
        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        return new AdmissionRecoveryDeliveryEnvelopeProtector(provider);
    }
}
