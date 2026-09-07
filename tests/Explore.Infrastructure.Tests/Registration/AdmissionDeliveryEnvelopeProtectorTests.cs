using System.Text.Json;
using Explore.Application.Contracts.Admissions;
using Explore.Infrastructure.Services.Registration;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Tests.Registration;

public sealed class AdmissionDeliveryEnvelopeProtectorTests
{
    [Test]
    public async Task ProtectedEnvelopeRestoresAcrossProviderRecreationAndContainsNoBearer()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"admission-dp-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(directory);
        try
        {
            const string bearer = "abcdefghijklmnopqrstuvwxyzABCDEFGH012345678";
            AdmissionProtectedDeliveryMaterial protectedMaterial;
            IDataProtectionProvider first = DataProtectionProvider.Create(
                new DirectoryInfo(directory), configuration => configuration.SetApplicationName("admission-test"));
            protectedMaterial = new AdmissionDeliveryEnvelopeProtector(first).Protect(
                new AdmissionCredentialDeliveryEnvelope("attendee@example.test", bearer));

            IDataProtectionProvider restored = DataProtectionProvider.Create(
                new DirectoryInfo(directory), configuration => configuration.SetApplicationName("admission-test"));
            var protector = new AdmissionDeliveryEnvelopeProtector(restored);

            var intent = new AdmissionDeliveryIntent(
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), protectedMaterial.Ciphertext, protectedMaterial.ProtectionVersion, DateTime.UtcNow);
            string persistedBoundaryJson = JsonSerializer.Serialize(intent);

            await Assert.That(protectedMaterial.Ciphertext).DoesNotContain(bearer);
            await Assert.That(protectedMaterial.ToString()).DoesNotContain(bearer);
            await Assert.That(intent.ToString()).DoesNotContain(bearer);
            await Assert.That(persistedBoundaryJson).DoesNotContain(bearer);
            AdmissionCredentialDeliveryEnvelope restoredEnvelope = protector.Unprotect(
                protectedMaterial.Ciphertext, protectedMaterial.ProtectionVersion);
            await Assert.That(restoredEnvelope.RecipientAddress).IsEqualTo("attendee@example.test");
            await Assert.That(restoredEnvelope.PlaintextCredential).IsEqualTo(bearer);
            await Assert.That(() => protector.Unprotect("not-base64", protectedMaterial.ProtectionVersion))
                .Throws<InvalidOperationException>();
            await Assert.That(() => protector.Unprotect(protectedMaterial.Ciphertext, protectedMaterial.ProtectionVersion + 1))
                .Throws<InvalidOperationException>();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
