using System.Security.Cryptography;
using System.Text.Json;
using Explore.Infrastructure.Services;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class CerbosPublicationReceiptTests
{
    [Test]
    public async Task SignedPublicationBindsEveryDeploymentInput()
    {
        using var signer = RSA.Create(3072);
        var now = DateTimeOffset.UtcNow;
        var receipt = new CerbosPublicationReceipt(
            "cerbos-publication.v1", new string('a', 40), new string('b', 64),
            "production", "https://pdp.example:3593", now, now.AddHours(1), true);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt);
        var signature = signer.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var verifier = new CerbosPublicationReceiptVerifier();
        var key = signer.ExportSubjectPublicKeyInfoPem();

        await Assert.That(verifier.Verify(bytes, signature, key, receipt.Revision,
            receipt.ContentHash, receipt.Environment, receipt.PdpEndpoint, now)).IsTrue();
        await Assert.That(verifier.Verify(bytes, signature, key, new string('c', 40),
            receipt.ContentHash, receipt.Environment, receipt.PdpEndpoint, now)).IsFalse();
        await Assert.That(verifier.Verify(bytes, signature, key, receipt.Revision,
            new string('c', 64), receipt.Environment, receipt.PdpEndpoint, now)).IsFalse();
        await Assert.That(verifier.Verify(bytes, signature, key, receipt.Revision,
            receipt.ContentHash, "staging", receipt.PdpEndpoint, now)).IsFalse();
        await Assert.That(verifier.Verify(bytes, signature, key, receipt.Revision,
            receipt.ContentHash, receipt.Environment, "https://other.example:3593", now)).IsFalse();
        await Assert.That(verifier.Verify(bytes, signature, key, receipt.Revision,
            receipt.ContentHash, receipt.Environment, receipt.PdpEndpoint, now.AddHours(2))).IsFalse();
        await Assert.That(verifier.Verify(bytes, signature, key, receipt.Revision,
            receipt.ContentHash, receipt.Environment, receipt.PdpEndpoint, now.AddSeconds(-1))).IsFalse();

        using var otherSigner = RSA.Create(3072);
        await Assert.That(verifier.Verify(bytes, signature, otherSigner.ExportSubjectPublicKeyInfoPem(),
            receipt.Revision, receipt.ContentHash, receipt.Environment, receipt.PdpEndpoint, now)).IsFalse();
        await Assert.That(verifier.Verify([], [], key, receipt.Revision,
            receipt.ContentHash, receipt.Environment, receipt.PdpEndpoint, now)).IsFalse();
        await Assert.That(verifier.Verify(bytes, signature, signer.ExportPkcs8PrivateKeyPem(),
            receipt.Revision, receipt.ContentHash, receipt.Environment, receipt.PdpEndpoint, now)).IsFalse();
        bytes[0] ^= 1;
        await Assert.That(verifier.Verify(bytes, signature, key, receipt.Revision,
            receipt.ContentHash, receipt.Environment, receipt.PdpEndpoint, now)).IsFalse();
    }

    [Test]
    public async Task AuthenticFailureOrExcessiveLifetimeNeverGrantsReadiness()
    {
        using var signer = RSA.Create(3072);
        var now = DateTimeOffset.UtcNow;
        foreach (var receipt in new[]
        {
            new CerbosPublicationReceipt("cerbos-publication.v1", new string('a', 40),
                new string('b', 64), "production", "https://pdp.example:3593",
                now, now.AddHours(1), false),
            new CerbosPublicationReceipt("cerbos-publication.v1", new string('a', 40),
                new string('b', 64), "production", "https://pdp.example:3593",
                now, now.AddDays(2), true)
        })
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt);
            var signature = signer.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            await Assert.That(new CerbosPublicationReceiptVerifier().Verify(bytes, signature,
                signer.ExportSubjectPublicKeyInfoPem(), receipt.Revision, receipt.ContentHash,
                receipt.Environment, receipt.PdpEndpoint, now)).IsFalse();
        }
    }
}
