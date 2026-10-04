using System.Text;
using ISLAMU.Event.Setup.Core.Environment;

namespace ISLAMU.Setup.Core.EnvironmentTests;

public sealed class IdentityCorrelationEnvironmentTests
{
    private static readonly EnvironmentActivationContext Context =
        new("standalone", ["identity"], ["local"]);

    [Test]
    public async Task EmptyTrustHasNoGeneratedIssuerAndIndexedValuesRoundTrip()
    {
        EnvironmentCatalogue catalogue = PlatformEnvironmentCatalogue.Catalogue;
        DotenvCompositionResult empty = DotenvComposer.ComposeNoSecrets(catalogue, Context, []);
        await Assert.That(empty.Document.Entries.Any(entry =>
            entry.Key.StartsWith("IDENTITYCORRELATION__", StringComparison.Ordinal))).IsFalse();

        DotenvCompositionResult result = DotenvComposer.ComposeNoSecrets(catalogue, Context,
        [
            Entry("0", "https://identity.example.test/realms/one"),
            Entry("12", "https://identity.example.test/realms/two")
        ]);
        await Assert.That(result.Diagnostics).IsEmpty();
        DotenvParseResult parsed = DotenvCodec.Parse(DotenvCodec.Render(result.Document, true).Bytes);
        await Assert.That(parsed.Succeeded).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(DotenvCodec.Render(result.Document, true).Bytes.Span))
            .Contains("IDENTITYCORRELATION__TRUSTEDISSUERS__12=");
        EnvironmentVariableDefinition definition = catalogue.Lookup("IDENTITYCORRELATION__TRUSTEDISSUERS__12")!;
        await Assert.That(definition.Requirement).IsEqualTo(EnvironmentVariableRequirement.Optional);
        await Assert.That(definition.Sensitivity).IsEqualTo(EnvironmentVariableSensitivity.Public);
        await Assert.That(definition.RestartBehavior).IsEqualTo(EnvironmentRestartBehavior.Process);
    }

    [Test]
    [Arguments("not-an-issuer")]
    [Arguments("https://identity.example.test/realm?override=true")]
    [Arguments("https://identity.example.test/realm#fragment")]
    [Arguments("https://user@identity.example.test/realm")]
    [Arguments("https://identity.example.test/*")]
    public async Task InvalidIssuerCannotBeRenderedAsTrusted(string issuer)
    {
        DotenvCompositionResult result = DotenvComposer.ComposeNoSecrets(
            PlatformEnvironmentCatalogue.Catalogue, Context, [Entry("0", issuer)]);
        await Assert.That(result.Diagnostics.Any(item => item.Code == "dotenv-input-value-invalid")).IsTrue();
        await Assert.That(result.Document.Entries.Any(entry =>
            entry.Key.StartsWith("IDENTITYCORRELATION__", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task DuplicateNormalizedAuthoritiesAreRejected()
    {
        DotenvCompositionResult result = DotenvComposer.ComposeNoSecrets(
            PlatformEnvironmentCatalogue.Catalogue, Context,
        [
            Entry("0", "https://IDENTITY.example.test:443/realm/"),
            Entry("1", "https://identity.example.test/realm")
        ]);
        await Assert.That(result.Diagnostics.Any(item => item.Code == "dotenv-identity-issuer-duplicate")).IsTrue();
    }

    [Test]
    [Arguments("-1")]
    [Arguments("01")]
    [Arguments("other")]
    public async Task InvalidArrayIndexIsNotAConfigurationAuthority(string index)
    {
        await Assert.That(PlatformEnvironmentCatalogue.Catalogue.Lookup(
            "IDENTITYCORRELATION__TRUSTEDISSUERS__" + index)).IsNull();
    }

    private static DotenvEntry Entry(string index, string issuer) => new(
        "IDENTITYCORRELATION__TRUSTEDISSUERS__" + index, issuer,
        DotenvEntryKind.LocalHumanValue, false, DotenvProvenance.UserInput);
}
