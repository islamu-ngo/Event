namespace ISLAMU.Setup.Core.Tests.Portability;

using System.Text;
using System.Text.Json;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class OperatorIdentityManifestTests
{
    [Test]
    public async Task JsonAndYamlRoundTripCanonicalDocumentAndRevision()
    {
        OperatorIdentityManifest manifest = OperatorIdentityManifestJson.Create(Document());
        foreach (OperatorIdentityManifestFormat format in Enum.GetValues<OperatorIdentityManifestFormat>())
        {
            byte[] bytes = OperatorIdentityManifestCodec.Write(manifest, format);
            OperatorIdentityManifest reopened = OperatorIdentityManifestCodec.Read(bytes);
            await Assert.That(reopened.ContentDigest).IsEqualTo(manifest.ContentDigest);
            await Assert.That(reopened.RevisionHash).IsEqualTo(manifest.RevisionHash);
            await Assert.That(reopened.Document.GetProperty("legalName").GetString()).IsEqualTo("Independent ASBL");
            await Assert.That(OperatorIdentityManifestCodec.Write(reopened, format).SequenceEqual(bytes)).IsTrue();
        }
    }

    [Test]
    public async Task CanonicalDigestIgnoresPropertyOrderButDetectsEveryContentChange()
    {
        JsonElement document = Document();
        string reversed = "{" + string.Join(",", document.EnumerateObject().Reverse()
            .Select(property => JsonSerializer.Serialize(property.Name) + ":" + property.Value.GetRawText())) + "}";
        OperatorIdentityManifest original = OperatorIdentityManifestJson.Create(document);
        OperatorIdentityManifest reordered = OperatorIdentityManifestJson.Create(JsonDocument.Parse(reversed).RootElement);
        await Assert.That(reordered.ContentDigest).IsEqualTo(original.ContentDigest);

        string text = Encoding.UTF8.GetString(OperatorIdentityManifestCodec.Write(original));
        _ = await Assert.ThrowsAsync<OperatorIdentityManifestException>(() => Task.FromResult(
            OperatorIdentityManifestCodec.Read(Encoding.UTF8.GetBytes(
                text.Replace("Independent ASBL", "Altered ASBL", StringComparison.Ordinal)))));
        _ = await Assert.ThrowsAsync<OperatorIdentityManifestException>(() => Task.FromResult(
            OperatorIdentityManifestJson.Validate(original with { RevisionHash = new string('0', 64) })));
    }

    [Test]
    public async Task MalformedUnknownDuplicateTaggedAndOversizedInputsFailWithoutReflectingValues()
    {
        OperatorIdentityManifest manifest = OperatorIdentityManifestJson.Create(Document());
        string json = Encoding.UTF8.GetString(OperatorIdentityManifestCodec.Write(manifest));
        string[] invalid =
        [
            "{",
            json.Replace("\"kind\":", "\"unexpected\":\"private-contact@example.test\",\"kind\":", StringComparison.Ordinal),
            json.Replace("\"legalName\":", "\"legalName\":\"private-contact@example.test\",\"legalName\":", StringComparison.Ordinal),
            "document: &identity { legalName: private-contact@example.test }\ncopy: *identity\n",
            "document: !identity private-contact@example.test\n",
            "---\na: b\n---\na: b\n"
        ];
        foreach (string text in invalid)
        {
            OperatorIdentityManifestException? exception = await Assert.ThrowsAsync<OperatorIdentityManifestException>(
                () => Task.FromResult(OperatorIdentityManifestCodec.Read(Encoding.UTF8.GetBytes(text))));
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.ToString().Contains("private-contact", StringComparison.Ordinal)).IsFalse();
        }
        _ = await Assert.ThrowsAsync<OperatorIdentityManifestException>(() => Task.FromResult(
            OperatorIdentityManifestCodec.Read(new byte[OperatorIdentityManifestJson.MaximumBytes + 1])));
    }

    [Test]
    public async Task ManifestOwnsItsDocumentAndDoesNotPrintLegalIdentity()
    {
        using JsonDocument source = JsonDocument.Parse(Document().GetRawText());
        OperatorIdentityManifest manifest = OperatorIdentityManifestJson.Create(source.RootElement);
        source.Dispose();
        await Assert.That(manifest.Document.GetProperty("legalName").GetString()).IsEqualTo("Independent ASBL");
        await Assert.That(manifest.ToString().Contains("Independent", StringComparison.Ordinal)).IsFalse();
    }

    private static JsonElement Document() => JsonDocument.Parse("""
        {
          "operatorId":"0198e2a4-5340-7f89-8abc-b8bdf43e0ea8",
          "revision":"0198e2a4-5340-7f89-8abc-b8bdf43e0ea9",
          "publicName":"Independent Operator",
          "legalName":"Independent ASBL",
          "operatorKindCode":"registered_organization",
          "jurisdictionCountryCode":"BE",
          "registrationIdentifier":"BE 0123.456.789",
          "publicContactEmail":"contact@example.test",
          "websiteUrl":"https://example.test",
          "legalNoticeUrl":"https://example.test/legal",
          "termsUrl":"https://example.test/terms",
          "privacyUrl":"https://example.test/privacy",
          "isOfficialInstance":false,
          "officialOrigin":"https://example.test"
        }
        """).RootElement.Clone();
}
