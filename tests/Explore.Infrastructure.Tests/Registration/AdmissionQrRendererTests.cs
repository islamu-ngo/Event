using ISLAMU.Wire.Contracts.Admissions;
using Explore.Application.Contracts.Admissions;
using Explore.Infrastructure;
using Explore.Infrastructure.Services.Registration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;

namespace Explore.Infrastructure.Tests.Registration;

public sealed class AdmissionQrRendererTests
{
    private const string Bearer = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";

    [Test]
    public async Task RenderProducesDeterministicBoundedSecretFreeSvgGeometry()
    {
        AdmissionQrPayloadCodec.TryDecode("islamu-admission:v1:" + Bearer, out AdmissionQrPayload? payload);
        var renderer = new AdmissionQrSvgRenderer();

        AdmissionQrSvg first = renderer.Render(payload!);
        AdmissionQrSvg second = renderer.Render(payload!);

        await Assert.That(first.Content).IsEqualTo(second.Content);
        await Assert.That(first.ModuleCount).IsEqualTo(41);
        await Assert.That(first.QuietZoneModules).IsEqualTo(4);
        await Assert.That(first.ViewBoxSize).IsEqualTo(first.ModuleCount + 8);
        await Assert.That(first.Content.Length).IsLessThanOrEqualTo(262_144);
        await Assert.That(first.Content).Contains("fill=\"#fff\"");
        await Assert.That(first.Content).Contains("fill=\"#000\"");
        await Assert.That(first.Content).DoesNotContain(Bearer);
        await Assert.That(first.Content).DoesNotContain("islamu-admission");
        await Assert.That(first.Content).DoesNotContain("<!--");
        await Assert.That(first.Content).DoesNotContain("<title");
        await Assert.That(first.Content).DoesNotContain("<desc");
        await Assert.That(first.Content).DoesNotContain(" id=");
        await Assert.That(first.Content).DoesNotContain("data-");
        await Assert.That(first.ToString()).DoesNotContain(first.Content);

        string geometryDigest = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(first.Content)));
        await Assert.That(geometryDigest).IsEqualTo(
            "EAE76C0D58A4B56AAF7A4D99383832E4683D6FC9CB1ECFCF41ED323CEDF21D0A");

        AdmissionCredentialBearer differentBearer =
            AdmissionCredentialBearer.FromBytes(new byte[AdmissionCredentialBearer.ByteLength]);
        AdmissionQrPayloadCodec.TryDecode(
            AdmissionQrPayloadCodec.Encode(differentBearer),
            out AdmissionQrPayload? differentPayload);
        AdmissionQrSvg different = renderer.Render(differentPayload!);
        await Assert.That(different.Content).IsNotEqualTo(first.Content);
    }

    [Test]
    public async Task RendererRejectsMissingPayloadWithoutIncludingCandidateMaterial()
    {
        var renderer = new AdmissionQrSvgRenderer();

        var exception = Assert.Throws<ArgumentException>(() => renderer.Render(null!));

        await Assert.That(exception.Message).DoesNotContain(Bearer);
    }

    [Test]
    public async Task ProductionRegistrationResolvesRealRenderer()
    {
        var services = new ServiceCollection();
        services.ConfigureInfrastructureServices(new ConfigurationBuilder().Build());
        using ServiceProvider provider = services.BuildServiceProvider();

        IAdmissionQrRenderer renderer = provider.GetRequiredService<IAdmissionQrRenderer>();

        await Assert.That(renderer).IsTypeOf<AdmissionQrSvgRenderer>();
    }
}
