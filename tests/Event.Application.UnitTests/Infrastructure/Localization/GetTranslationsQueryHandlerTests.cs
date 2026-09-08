using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Localization.Handlers.Queries;
using Explore.Application.Features.Localization.Requests.Queries;
using FluentValidation;
using NSubstitute;

namespace Event.Application.UnitTests.Infrastructure.Localization;

public class GetTranslationsQueryHandlerTests
{
    [Test]
    public async Task Handle_ReturnsTranslationsAsDictionary()
    {
        var provider = Substitute.For<ITranslationManagementProvider>();
        provider.ExportTranslationsAsync("fr", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TranslationExport>>(new List<TranslationExport>
            {
                new("lookup.tag.FIQH.full_name", "Jurisprudence islamique"),
                new("lookup.madhab.HANAFI.full_name", "Hanafite"),
            }));

        var handler = new GetTranslationsQueryHandler(provider);

        var result = await handler.Handle(
            new GetTranslationsQuery { LanguageCode = "fr" },
            CancellationToken.None);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result["lookup.tag.FIQH.full_name"]).IsEqualTo("Jurisprudence islamique");
        await Assert.That(result["lookup.madhab.HANAFI.full_name"]).IsEqualTo("Hanafite");
    }

    [Test]
    public async Task Handle_WithSupportedLanguages_CallsProviderWithNormalizedCode()
    {
        var provider = Substitute.For<ITranslationManagementProvider>();
        provider.ExportTranslationsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<TranslationExport>>([]));

        var handler = new GetTranslationsQueryHandler(provider);

        await handler.Handle(new GetTranslationsQuery { LanguageCode = " EN " }, CancellationToken.None);
        await handler.Handle(new GetTranslationsQuery { LanguageCode = "fr" }, CancellationToken.None);
        await handler.Handle(new GetTranslationsQuery { LanguageCode = "Ar" }, CancellationToken.None);

        await provider.Received(1).ExportTranslationsAsync("en", Arg.Any<CancellationToken>());
        await provider.Received(1).ExportTranslationsAsync("fr", Arg.Any<CancellationToken>());
        await provider.Received(1).ExportTranslationsAsync("ar", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Handle_WithUnsupportedLanguage_ThrowsValidationExceptionBeforeProviderCall()
    {
        var provider = Substitute.For<ITranslationManagementProvider>();
        var handler = new GetTranslationsQueryHandler(provider);

        await Assert.ThrowsAsync<ValidationException>(async () =>
            await handler.Handle(new GetTranslationsQuery { LanguageCode = "zz" }, CancellationToken.None));

        await provider.DidNotReceiveWithAnyArgs().ExportTranslationsAsync(default!, default);
    }

    [Test]
    public async Task Handle_WithMalformedLanguage_ThrowsValidationExceptionBeforeProviderCall()
    {
        var provider = Substitute.For<ITranslationManagementProvider>();
        var handler = new GetTranslationsQueryHandler(provider);

        await Assert.ThrowsAsync<ValidationException>(async () =>
            await handler.Handle(new GetTranslationsQuery { LanguageCode = "en-US" }, CancellationToken.None));

        await provider.DidNotReceiveWithAnyArgs().ExportTranslationsAsync(default!, default);
    }
}
