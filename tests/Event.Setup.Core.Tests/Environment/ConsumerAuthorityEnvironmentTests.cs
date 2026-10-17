using System.Security.Cryptography;
using System.Text;
using ISLAMU.Event.Setup.Core.Environment;

namespace ISLAMU.Setup.Core.EnvironmentTests;

public sealed class ConsumerAuthorityEnvironmentTests
{
    [Test]
    public async Task PublicTokenPolicyRoundTripsWithoutCreatingAnImplicitClient()
    {
        var context = new EnvironmentActivationContext("split", ["identity"], ["keycloak"]);
        var catalogue = PlatformEnvironmentCatalogue.Catalogue;
        var empty = DotenvComposer.ComposeNoSecrets(catalogue, context, []);
        await Assert.That(empty.Document.Entries.Any(entry =>
            entry.Key.StartsWith("KEYCLOAK__PERMITTEDCLIENTS__", StringComparison.Ordinal))).IsFalse();
        var result = DotenvComposer.ComposeNoSecrets(catalogue, context,
        [
            Entry("KEYCLOAK__AUDIENCE", "event-resource"),
            Entry("KEYCLOAK__PERMITTEDCLIENTS__0", "event-browser"),
            Entry("KEYCLOAK__PERMITTEDCLIENTS__1", "event-worker")
        ]);
        await Assert.That(result.Diagnostics).IsEmpty();
        var rendered = DotenvCodec.Render(result.Document, true);
        var parsed = DotenvCodec.Parse(rendered.Bytes);
        await Assert.That(parsed.Succeeded).IsTrue();
        await Assert.That(parsed.Document!.Entries.Single(entry => entry.Key == "KEYCLOAK__AUDIENCE").Value)
            .IsEqualTo("event-resource");
        await Assert.That(parsed.Document.Entries.Single(entry => entry.Key == "KEYCLOAK__PERMITTEDCLIENTS__1").Value)
            .IsEqualTo("event-worker");
        await Assert.That(catalogue.Lookup("KEYCLOAK__PERMITTEDCLIENTS__1")!.Sensitivity)
            .IsEqualTo(EnvironmentVariableSensitivity.Public);
    }

    [Test]
    [Arguments("")]
    [Arguments("event-browser")]
    public async Task BlankOrDuplicateClientCannotBeRenderedAsValidPolicy(string second)
    {
        var result = DotenvComposer.ComposeNoSecrets(PlatformEnvironmentCatalogue.Catalogue,
            new EnvironmentActivationContext("split", ["identity"], ["keycloak"]),
        [
            Entry("KEYCLOAK__PERMITTEDCLIENTS__0", "event-browser"),
            Entry("KEYCLOAK__PERMITTEDCLIENTS__1", second)
        ]);
        await Assert.That(result.Diagnostics.Any(item => item.Code == "dotenv-input-value-invalid")).IsTrue();
    }

    [Test]
    public async Task BootstrapCredentialsRemainProtectedAndOutsideRuntimeBindings()
    {
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var catalogue = PlatformEnvironmentCatalogue.Catalogue;
        var result = DotenvComposer.ComposeNoSecrets(catalogue,
            new EnvironmentActivationContext("split", ["security"], ["infisical"]),
            [Entry("INFISICAL_API_CLIENT_SECRET", canary)]);
        await Assert.That(Encoding.UTF8.GetString(DotenvCodec.Render(result.Document, true).Bytes.Span))
            .DoesNotContain(canary);
        await Assert.That(result.Diagnostics.Any(item => item.Code == "dotenv-input-key-irrelevant")).IsTrue();
        await Assert.That(PlatformEnvironmentCatalogue.SecretBindingEnvironmentKeys.Contains("KEYCLOAK_DB_PASSWORD")).IsFalse();
        await Assert.That(catalogue.Lookup("INFISICAL_API_CLIENT_SECRET")!.Generation.Surfaces
            .HasFlag(EnvironmentGenerationSurface.Startup)).IsFalse();
    }

    private static DotenvEntry Entry(string key, string value) =>
        new(key, value, DotenvEntryKind.LocalHumanValue, false, DotenvProvenance.UserInput);
}
