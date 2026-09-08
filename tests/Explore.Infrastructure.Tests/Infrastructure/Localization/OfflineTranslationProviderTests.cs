using Explore.Infrastructure.Localization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Localization;

public class OfflineTranslationProviderTests
{
    private const string EmbeddedAppNameKey = "ui.common.appName";
    private const string EmbeddedLoadingKey = "ui.common.loading";

    private readonly OfflineTranslationProvider _provider;

    public OfflineTranslationProviderTests()
    {
        _provider = new OfflineTranslationProvider(Substitute.For<ILogger<OfflineTranslationProvider>>());
    }

    [Test]
    public async Task TestConnection_AlwaysReturnsTrue()
    {
        var result = await _provider.TestConnectionAsync();

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task GetAvailableLanguages_ReturnsEmbeddedBundleLanguages()
    {
        var languages = await _provider.GetAvailableLanguagesAsync();
        var list = languages.ToList();

        // Starter bundles: en.json, fr.json, ar.json
        await Assert.That(list).IsNotNull();
        await Assert.That(list.Count).IsGreaterThanOrEqualTo(3);
        await Assert.That(list).Contains("en");
        await Assert.That(list).Contains("fr");
        await Assert.That(list).Contains("ar");
    }

    [Test]
    public async Task ExportTranslations_ForExistingLanguage_ReturnsEmbeddedTranslations()
    {
        var result = await _provider.ExportTranslationsAsync("en");
        var translations = result.ToDictionary(item => item.KeyName, item => item.Value);

        var hasAppName = translations.TryGetValue(EmbeddedAppNameKey, out var appName);
        await Assert.That(hasAppName).IsTrue();
        await Assert.That(appName).IsEqualTo("ISLAMU Event");
    }

    [Test]
    public async Task ExportTranslations_WhenWritableBundleExists_MergesWithEmbeddedDefaults()
    {
        var root = CreateTempContentRoot();
        try
        {
            WriteWritableBundle(root, "en", """
                {
                  "ui.common.appName": "Writable Event",
                  "ui.test.localOnly": "Local Only"
                }
                """);

            var provider = CreateProvider(root);
            var translations = (await provider.ExportTranslationsAsync("en"))
                .ToDictionary(item => item.KeyName, item => item.Value);

            await Assert.That(translations[EmbeddedAppNameKey]).IsEqualTo("Writable Event");
            await Assert.That(translations[EmbeddedLoadingKey]).IsEqualTo("Loading…");
            await Assert.That(translations["ui.test.localOnly"]).IsEqualTo("Local Only");
        }
        finally
        {
            DeleteTempContentRoot(root);
        }
    }

    [Test]
    public async Task ExportTranslations_WhenWritableBundleIsMalformed_FallsBackToEmbeddedDefaults()
    {
        var root = CreateTempContentRoot();
        try
        {
            WriteWritableBundle(root, "en", """
                {
                  "bad key": "Broken"
                }
                """);

            var provider = CreateProvider(root);
            var translations = (await provider.ExportTranslationsAsync("en"))
                .ToDictionary(item => item.KeyName, item => item.Value);

            await Assert.That(translations[EmbeddedAppNameKey]).IsEqualTo("ISLAMU Event");
            await Assert.That(translations.ContainsKey("bad key")).IsFalse();
        }
        finally
        {
            DeleteTempContentRoot(root);
        }
    }

    [Test]
    public async Task ExportTranslations_ForNonExistentLanguage_ReturnsEmpty()
    {
        var result = await _provider.ExportTranslationsAsync("zz");
        var list = result.ToList();

        await Assert.That(list).IsNotNull();
        await Assert.That(list.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ImportKeys_IsNoOp()
    {
        await _provider.ImportKeysAsync([]);
    }

    private static OfflineTranslationProvider CreateProvider(string contentRoot)
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(contentRoot);
        return new OfflineTranslationProvider(Substitute.For<ILogger<OfflineTranslationProvider>>(), environment);
    }

    private static string CreateTempContentRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"islamu-localization-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteWritableBundle(string contentRoot, string languageCode, string json)
    {
        var directory = Path.Combine(contentRoot, "App_Data", "Localization", "Bundles");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{languageCode}.json"), json);
    }

    private static void DeleteTempContentRoot(string contentRoot)
    {
        if (Directory.Exists(contentRoot))
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }
}
