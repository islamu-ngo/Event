using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Localization.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain.Common.Localization;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Localization.Handlers.Commands;

public class ImportLocalizationBundleCommandHandler : IRequestHandler<ImportLocalizationBundleCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly IBundleFileWriter _bundleFileWriter;
    private readonly ITranslationResolver _translationResolver;
    private readonly TranslationMetrics _metrics;
    private readonly ILogger<ImportLocalizationBundleCommandHandler> _logger;

    public ImportLocalizationBundleCommandHandler(
        IAdminContext adminContext,
        IBundleFileWriter bundleFileWriter,
        ITranslationResolver translationResolver,
        TranslationMetrics metrics,
        ILogger<ImportLocalizationBundleCommandHandler> logger)
    {
        _adminContext = adminContext;
        _bundleFileWriter = bundleFileWriter;
        _translationResolver = translationResolver;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(
        ImportLocalizationBundleCommand request,
        CancellationToken cancellationToken)
    {
        var actor = await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (!actor.HasValue || !await _adminContext.IsInstanceAdminAsync(actor.Value, cancellationToken))
        {
            const string message = "Instance administrator authority is required to import localization bundles.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        if (!CultureRegistry.TryGetEntry(request.Dto.LanguageCode, out var culture))
            throw new ValidationException([
                new ValidationFailure(nameof(request.Dto.LanguageCode), "Language code is not supported."),
            ]);

        if (request.Dto.Translations.Count == 0)
        {
            const string message = "Bundle import requires at least one translation.";
            _metrics.RecordStaticBundleOperation("import_static_bundle", culture.Code, "validation_failure");
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        try
        {
            var path = await _bundleFileWriter.WriteBundleAsync(
                culture.Code,
                request.Dto.Translations,
                cancellationToken);

            await _translationResolver.InvalidateLanguageAsync(culture.Code, cancellationToken);

            var message = $"Imported {request.Dto.Translations.Count} translations for language '{culture.Code}' → {path}";
            _metrics.RecordStaticBundleOperation("import_static_bundle", culture.Code, "success");
            return BaseCommandResponse.Success(Guid.CreateVersion7(), message);
        }
        catch (BundleWriteException ex)
        {
            _logger.LogError(ex, "[LOCALIZATION] Static bundle import failed for {Language}", culture.Code);
            var message = $"Failed to import bundle for '{culture.Code}': {ex.Message}";
            _metrics.RecordStaticBundleOperation("import_static_bundle", culture.Code, "write_error");
            return BaseCommandResponse.Validation<Guid>([message], message);
        }
    }
}
