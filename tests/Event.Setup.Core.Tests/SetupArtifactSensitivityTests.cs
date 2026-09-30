using System.Security.Cryptography;
using System.Text;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Event.Setup.Core.Environment;

namespace ISLAMU.Setup.Core.Tests;

public sealed class SetupArtifactSensitivityTests
{
    [Test]
    public async Task GeneratedEnvironmentAndUnknownKindsHaveNoPublicProjection()
    {
        string value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var context = new EnvironmentActivationContext("standalone", ["platform"], ["environment", "local", "sqlite"]);
        DotenvCompositionResult composition = DotenvComposer.ComposeWithSecrets(
            PlatformEnvironmentCatalogue.Catalogue, context,
            [new DotenvEntry("SETUP_SECRET", value, DotenvEntryKind.LocalHumanValue, true, DotenvProvenance.UserInput)]);
        DotenvRenderResult rendered = DotenvCodec.Render(composition.Document, true);
        await Assert.That(rendered.Succeeded).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(rendered.Bytes.Span)).Contains(value);

        foreach (SetupArtifactKind kind in new[]
        {
            SetupArtifactKind.Environment, SetupArtifactKind.Configuration,
            SetupArtifactKind.OperatorIdentity, SetupArtifactKind.Unknown, (SetupArtifactKind)int.MaxValue
        })
        {
            await Assert.That(SetupArtifactPolicy.Classify(kind)).IsEqualTo(SetupArtifactSensitivity.Restricted);
            await Assert.That(SetupArtifactPolicy.TryProjectPublic(kind, rendered.Bytes, out var projection)).IsFalse();
            await Assert.That(projection.IsEmpty).IsTrue();
        }
    }

    [Test]
    public async Task OnlyClosedPublicKindsCanProjectBytes()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        foreach (SetupArtifactKind kind in new[] { SetupArtifactKind.PublicCatalogue, SetupArtifactKind.PublicTemplate })
        {
            await Assert.That(SetupArtifactPolicy.TryProjectPublic(kind, bytes, out var projection)).IsTrue();
            await Assert.That(projection.ToArray()).IsEquivalentTo(bytes);
        }
    }
}
