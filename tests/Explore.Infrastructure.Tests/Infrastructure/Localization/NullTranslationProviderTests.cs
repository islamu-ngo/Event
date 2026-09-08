using Explore.Infrastructure.Localization;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Localization;

public class NullTranslationProviderTests
{
    private readonly NullTranslationProvider _provider;

    public NullTranslationProviderTests()
    {
        _provider = new NullTranslationProvider(Substitute.For<ILogger<NullTranslationProvider>>());
    }

    [Test]
    public async Task TestConnection_ReturnsFalse()
    {
        var result = await _provider.TestConnectionAsync();

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task GetAvailableLanguages_ReturnsEmpty()
    {
        var languages = await _provider.GetAvailableLanguagesAsync();
        var list = languages.ToList();

        await Assert.That(list.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExportTranslations_ReturnsEmpty()
    {
        var result = await _provider.ExportTranslationsAsync("en");
        var list = result.ToList();

        await Assert.That(list.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ImportKeys_DoesNotThrow()
    {
        await _provider.ImportKeysAsync([]);
    }
}
