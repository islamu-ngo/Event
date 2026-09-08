using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Localization.Requests.Queries;
using Explore.Application.Telemetry;
using Explore.Domain.Common.Localization;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace Explore.Application.Features.Localization.Handlers.Queries;

public class ExportLocalizationBundleQueryHandler : IRequestHandler<ExportLocalizationBundleQuery, IReadOnlyDictionary<string, string>>
{
    private readonly IStaticTranslationBundleReader _bundleReader;
    private readonly TranslationMetrics _metrics;

    public ExportLocalizationBundleQueryHandler(
        IStaticTranslationBundleReader bundleReader,
        TranslationMetrics metrics)
    {
        _bundleReader = bundleReader;
        _metrics = metrics;
    }

    public async Task<IReadOnlyDictionary<string, string>> Handle(
        ExportLocalizationBundleQuery request,
        CancellationToken cancellationToken)
    {
        if (!CultureRegistry.TryGetEntry(request.LanguageCode, out var culture))
            throw new ValidationException([
                new ValidationFailure(nameof(request.LanguageCode), "Language code is not supported."),
            ]);

        var bundle = await _bundleReader.ReadBundleAsync(culture.Code, cancellationToken);
        _metrics.RecordStaticBundleOperation("export_static_bundle", culture.Code, "success");
        return bundle;
    }
}
