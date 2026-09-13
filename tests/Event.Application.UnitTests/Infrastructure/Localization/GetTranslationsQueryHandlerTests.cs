using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Localization.Handlers.Queries;
using Explore.Application.Features.Localization.Requests.Queries;
using FluentValidation;
using NSubstitute;

namespace Event.Application.UnitTests.Infrastructure.Localization;

public class GetTranslationsQueryHandlerTests
{
    [Test]
    public async Task QueryAsync_ReturnsTranslationsAsDictionary()
    {
        var provider = Substitute.For<ITranslationManagementProvider>();
        provider.ExportTranslationsAsync("fr", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TranslationExport>>([
                new("lookup.tag.FIQH.full_name", "Jurisprudence islamique"),
                new("lookup.madhab.HANAFI.full_name", "Hanafite"),
            ]));
        IQueryHandler<GetTranslationsQuery, Dictionary<string, string>> handler = new GetTranslationsQueryHandler(provider);

        var result = await handler.QueryAsync(new() { LanguageCode = "fr" }, CancellationToken.None);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result["lookup.tag.FIQH.full_name"]).IsEqualTo("Jurisprudence islamique");
        await Assert.That(result["lookup.madhab.HANAFI.full_name"]).IsEqualTo("Hanafite");
    }

    [Test]
    public async Task QueryAsync_WithSupportedLanguages_ReturnsTheNormalizedLanguageBundle()
    {
        var provider = Substitute.For<ITranslationManagementProvider>();
        provider.ExportTranslationsAsync("en", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TranslationExport>>([new("ui.greeting", "Hello")]));
        provider.ExportTranslationsAsync("fr", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TranslationExport>>([new("ui.greeting", "Bonjour")]));
        provider.ExportTranslationsAsync("ar", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TranslationExport>>([new("ui.greeting", "Salaam")]));
        IQueryHandler<GetTranslationsQuery, Dictionary<string, string>> handler = new GetTranslationsQueryHandler(provider);

        var english = await handler.QueryAsync(new() { LanguageCode = " EN " }, CancellationToken.None);
        var french = await handler.QueryAsync(new() { LanguageCode = "fr" }, CancellationToken.None);
        var arabic = await handler.QueryAsync(new() { LanguageCode = "Ar" }, CancellationToken.None);

        await Assert.That(english["ui.greeting"]).IsEqualTo("Hello");
        await Assert.That(french["ui.greeting"]).IsEqualTo("Bonjour");
        await Assert.That(arabic["ui.greeting"]).IsEqualTo("Salaam");
    }

    [Test]
    public async Task QueryAsync_WithUnsupportedLanguage_ThrowsValidationExceptionBeforeProviderCall()
    {
        var provider = Substitute.For<ITranslationManagementProvider>();
        IQueryHandler<GetTranslationsQuery, Dictionary<string, string>> handler = new GetTranslationsQueryHandler(provider);

        await Assert.ThrowsAsync<ValidationException>(async () =>
            await handler.QueryAsync(new() { LanguageCode = "zz" }, CancellationToken.None));

        await provider.DidNotReceiveWithAnyArgs().ExportTranslationsAsync(default!, default);
    }

    [Test]
    public async Task QueryAsync_WithMalformedLanguage_ThrowsValidationExceptionBeforeProviderCall()
    {
        var provider = Substitute.For<ITranslationManagementProvider>();
        IQueryHandler<GetTranslationsQuery, Dictionary<string, string>> handler = new GetTranslationsQueryHandler(provider);

        await Assert.ThrowsAsync<ValidationException>(async () =>
            await handler.QueryAsync(new() { LanguageCode = "en-US" }, CancellationToken.None));

        await provider.DidNotReceiveWithAnyArgs().ExportTranslationsAsync(default!, default);
    }
}
