using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class ReleaseArtifactPolicyTests
{
    [Test]
    [NotInParallel("ReleaseArtifactAmbientState")]
    public async Task JsonAndTextBytesAreStableAcrossPlatformCultureClockAndOrdering()
    {
        const string windowsNfd = "{\r\n  \"z\": [\"beta\", \"alpha\"],\r\n  \"name\": \"Cafe\u0301\",\r\n  \"path\": \"eng\\\\release\\\\notes.md\",\r\n  \"date\": \"2026-08-14\"\r\n}";
        const string linuxNfc = "{\n  \"date\": \"2026-08-14\",\n  \"path\": \"eng/release/notes.md\",\n  \"name\": \"Café\",\n  \"z\": [\"alpha\", \"beta\"]\n}";
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        string? originalTimeZone = Environment.GetEnvironmentVariable("TZ");

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-SA");
            Environment.SetEnvironmentVariable("TZ", "Pacific/Kiritimati");
            TimeZoneInfo.ClearCachedData();
            ArtifactPolicyResult windows = ReleaseArtifactPolicy.NormalizeJson(windowsNfd);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-BE");
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            Environment.SetEnvironmentVariable("TZ", "America/Adak");
            TimeZoneInfo.ClearCachedData();
            ArtifactPolicyResult linux = ReleaseArtifactPolicy.NormalizeJson(linuxNfc);

            await Assert.That(windows.IsValid).IsTrue();
            await Assert.That(windows.Bytes!.SequenceEqual(linux.Bytes!)).IsTrue();
            await Assert.That(windows.Bytes![..3]).IsNotEquivalentTo(Encoding.UTF8.GetPreamble());
            await Assert.That(Encoding.UTF8.GetString(windows.Bytes)).IsEqualTo("{\n  \"date\": \"2026-08-14\",\n  \"name\": \"Café\",\n  \"path\": \"eng/release/notes.md\",\n  \"z\": [\n    \"alpha\",\n    \"beta\"\n  ]\n}\n");
            await Assert.That(Convert.ToHexString(SHA256.HashData(windows.Bytes))).IsEqualTo(Convert.ToHexString(SHA256.HashData(linux.Bytes!)));
            await Assert.That(ReleaseArtifactPolicy.NormalizeJson(windowsNfd).Bytes!.SequenceEqual(windows.Bytes)).IsTrue();

            ArtifactPolicyResult windowsText = ReleaseArtifactPolicy.NormalizeText("Cafe\u0301\r\n\r\n");
            ArtifactPolicyResult linuxText = ReleaseArtifactPolicy.NormalizeText("Café\n");
            await Assert.That(windowsText.Bytes!.SequenceEqual(linuxText.Bytes!)).IsTrue();
            await Assert.That(Encoding.UTF8.GetString(windowsText.Bytes!)).IsEqualTo("Café\n");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            Environment.SetEnvironmentVariable("TZ", originalTimeZone);
            TimeZoneInfo.ClearCachedData();
        }
    }

    [Test]
    public async Task MarkdownEscapesStructureWhilePreservingLegitimateNfcMultilingualText()
    {
        string[] entries = File.ReadAllLines(Path.Combine(RepositoryRoot.Find(), "eng", "release", "tests", "ISLAMU.ReleaseEngineering.Tests", "Fixtures", "untrusted-text-corpus.txt"))
            .Skip(2)
            .Where(line => line.Length != 0)
            .ToArray();

        ArtifactPolicyResult result = ReleaseArtifactPolicy.RenderMarkdown("إطلاق Café", entries);

        await Assert.That(result.IsValid).IsTrue();
        string markdown = Encoding.UTF8.GetString(result.Bytes!);
        await Assert.That(markdown).Contains("# إطلاق Café\n");
        await Assert.That(markdown).DoesNotContain("<script");
        await Assert.That(markdown).DoesNotContain("[forged](https://");
        await Assert.That(markdown).DoesNotContain("![image]");
        await Assert.That(markdown).DoesNotContain("```html");
        await Assert.That(markdown).DoesNotContain("\n## forged");
        await Assert.That(markdown).DoesNotContain("\n---");
        await Assert.That(markdown).DoesNotContain("\n1. forged");
        await Assert.That(markdown).DoesNotContain("\n> forged");
        await Assert.That(markdown).DoesNotContain("| forged | table |");
        await Assert.That(markdown.EndsWith('\n')).IsTrue();
    }

    [Test]
    public async Task UntrustedBoundaryRejectsInvalidUnicodeControlsBidiSecretsIdentityHtmlAndSize()
    {
        Dictionary<string, string> attacks = new(StringComparer.Ordinal)
        {
            ["invalid-unicode"] = "broken \uD800 surrogate",
            ["nul"] = "hidden\0content",
            ["newline"] = "forged\n# heading",
            ["bidi"] = "safe\u202Egnp.exe",
            ["format"] = "zero\u200Bwidth",
            ["secret"] = "api_key=super-secret-value",
            ["identity"] = "maintainer@example.org",
            ["provider"] = "https://github.com/example/repository",
            ["html"] = "<img src=x onerror=alert(1)>",
            ["carriage-return"] = "first\rsecond",
            ["autolink"] = "<https://example.invalid/autolink>",
            ["oversized"] = new('x', ReleaseArtifactPolicy.MaximumFieldUtf8Bytes + 1),
        };

        foreach ((string name, string value) in attacks)
        {
            UntrustedTextResult result = ReleaseArtifactPolicy.EscapeUntrustedMarkdown(value);
            await Assert.That(result.IsValid).IsFalse().Because(name);
            await Assert.That(result.Diagnostic).StartsWith("untrusted_text_").Because(name);
        }

        UntrustedTextResult multilingual = ReleaseArtifactPolicy.EscapeUntrustedMarkdown("يمكن للحاضرين استخدام اعتماد واحد أثناء تسجيل الوصول – Café");
        await Assert.That(multilingual.IsValid).IsTrue();
        await Assert.That(multilingual.Text).IsEqualTo("يمكن للحاضرين استخدام اعتماد واحد أثناء تسجيل الوصول – Café");
    }

    [Test]
    public async Task NormalizedDocumentsRejectClockIdentitySecretHtmlInvalidDateAndUnboundedCollections()
    {
        string[] invalidJson =
        [
            "{\"generatedAtUtc\":\"2026-08-14T12:00:00Z\"}",
            "{\"generated_at_utc\":\"2026-08-14T12:00:00Z\"}",
            "{\"buildTimestamp\":\"2026-08-14T12:00:00Z\"}",
            "{\"current_date\":\"2026-08-14\"}",
            "{\"now_utc\":\"2026-08-14T12:00:00Z\"}",
            "{\"authorEmail\":\"maintainer@example.org\"}",
            "{\"identity\":\"@maintainer\"}",
            "{\"provider\":\"gitlab\"}",
            "{\"provider_url\":\"https://example.invalid\"}",
            "{\"clientSecret\":\"redacted\"}",
            "{\"value\":\"api_key=super-secret-value\"}",
            "{\"value\":\"<script>alert(1)</script>\"}",
            "{\"releaseDate\":\"14/08/2026\"}",
        ];

        foreach (string json in invalidJson)
        {
            await Assert.That(ReleaseArtifactPolicy.NormalizeJson(json).IsValid).IsFalse();
        }

        ArtifactPolicyResult extremeNumber = ReleaseArtifactPolicy.NormalizeJson("{\"value\":1e9999}");
        await Assert.That(extremeNumber.IsValid).IsFalse();
        await Assert.That(extremeNumber.Diagnostics).Contains("normalized_json_invalid_number");

        ArtifactPolicyResult oversizedMarkdown = ReleaseArtifactPolicy.RenderMarkdown(
            "Release",
            Enumerable.Repeat("bounded", ReleaseArtifactPolicy.MaximumCollectionItems + 1));
        await Assert.That(oversizedMarkdown.Diagnostics).Contains("markdown_collection_too_large");
    }

    [Test]
    public async Task NormalizedJsonAllowsObjectFormatAndRejectsEmailAddressesByDesign()
    {
        ArtifactPolicyResult objectFormat = ReleaseArtifactPolicy.NormalizeJson("{\"objectFormat\":\"sha1\"}");
        ArtifactPolicyResult email = ReleaseArtifactPolicy.NormalizeJson("{\"summary\":\"Contact maintainer@example.org for release notes\"}");
        UntrustedTextResult multilingual = ReleaseArtifactPolicy.EscapeUntrustedMarkdown("يمكن للحاضرين استخدام اعتماد واحد أثناء تسجيل الوصول – Café");
        UntrustedTextResult emailText = ReleaseArtifactPolicy.EscapeUntrustedMarkdown("Contact maintainer@example.org for release notes");

        await Assert.That(objectFormat.IsValid).IsTrue();
        await Assert.That(email.IsValid).IsFalse();
        await Assert.That(email.Diagnostics).Contains("normalized_json_identity_or_provider");
        await Assert.That(multilingual.IsValid).IsTrue();
        await Assert.That(emailText.IsValid).IsFalse();
    }

    [Test]
    public async Task MaximumLengthIdentifierDoesNotRescanEverySuffix()
    {
        string identifier = new('a', ReleaseArtifactPolicy.MaximumFieldUtf8Bytes);
        UntrustedTextResult result = ReleaseArtifactPolicy.EscapeUntrustedMarkdown(identifier);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Text).IsEqualTo(identifier);
    }

    [Test]
    public async Task MaximumLengthEmailLocalPartRemainsRejected()
    {
        const string domain = "@example.invalid";
        string email = new string('a', ReleaseArtifactPolicy.MaximumFieldUtf8Bytes - domain.Length) + domain;

        await Assert.That(ReleaseArtifactPolicy.EscapeUntrustedMarkdown(email).IsValid).IsFalse();
    }

    [Test]
    public async Task DeterministicEnvironmentIsExplicitAndDoesNotRepurposeHome()
    {
        IReadOnlyDictionary<string, string> environment = ReleaseArtifactPolicy.CreateDeterministicEnvironment("/isolated/release");

        await Assert.That(environment).ContainsKey("TZ");
        await Assert.That(environment["TZ"]).IsEqualTo("UTC");
        await Assert.That(environment["LC_ALL"]).IsEqualTo("C");
        await Assert.That(environment["LANG"]).IsEqualTo("C");
        await Assert.That(environment["GIT_TERMINAL_PROMPT"]).IsEqualTo("0");
        await Assert.That(environment["GIT_OPTIONAL_LOCKS"]).IsEqualTo("0");
        await Assert.That(environment["GIT_CONFIG_NOSYSTEM"]).IsEqualTo("1");
        await Assert.That(environment["GIT_CONFIG_GLOBAL"]).EndsWith("global.gitconfig");
        await Assert.That(environment).DoesNotContainKey("HOME");
    }

    [Test]
    public async Task JsonNormalizesManifestPathVariantsWithForwardSlashes()
    {
        const string windows = "{\"manifest_path\":\"eng\\\\release\\\\bundle.json\",\"items\":[{\"artifactPath\":\"out\\\\release.md\"}] }";
        const string linux = "{\"items\":[{\"artifactPath\":\"out/release.md\"}],\"manifest_path\":\"eng/release/bundle.json\"}";

        ArtifactPolicyResult left = ReleaseArtifactPolicy.NormalizeJson(windows);
        ArtifactPolicyResult right = ReleaseArtifactPolicy.NormalizeJson(linux);

        await Assert.That(left.IsValid).IsTrue();
        await Assert.That(left.Bytes!.SequenceEqual(right.Bytes!)).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(left.Bytes!)).DoesNotContain('\\');
    }

    [Test]
    public async Task JsonNormalizesPluralPathArraysBeforeSortingAndEmission()
    {
        const string mixed = "{\"paths\":[\"z/item\",\"a\\\\item\",\"a/item\"]}";
        const string normalized = "{\"paths\":[\"a/item\",\"a/item\",\"z/item\"]}";

        ArtifactPolicyResult left = ReleaseArtifactPolicy.NormalizeJson(mixed);
        ArtifactPolicyResult right = ReleaseArtifactPolicy.NormalizeJson(normalized);

        await Assert.That(left.IsValid).IsTrue();
        await Assert.That(left.Bytes!.SequenceEqual(right.Bytes!)).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(left.Bytes!)).IsEqualTo("{\n  \"paths\": [\n    \"a/item\",\n    \"a/item\",\n    \"z/item\"\n  ]\n}\n");
    }

    [Test]
    public async Task JsonNumbersUseOneInvariantRepresentation()
    {
        byte[] integer = ReleaseArtifactPolicy.NormalizeJson("{\"value\":1}").Bytes!;
        byte[] decimalOne = ReleaseArtifactPolicy.NormalizeJson("{\"value\":1.0}").Bytes!;
        byte[] decimalOneWithZeros = ReleaseArtifactPolicy.NormalizeJson("{\"value\":1.00}").Bytes!;
        byte[] exponentOne = ReleaseArtifactPolicy.NormalizeJson("{\"value\":1e0}").Bytes!;

        await Assert.That(decimalOne.SequenceEqual(integer)).IsTrue();
        await Assert.That(decimalOneWithZeros.SequenceEqual(integer)).IsTrue();
        await Assert.That(exponentOne.SequenceEqual(integer)).IsTrue();
        await Assert.That(Encoding.UTF8.GetString(integer)).IsEqualTo("{\n  \"value\": 1\n}\n");
    }
}
