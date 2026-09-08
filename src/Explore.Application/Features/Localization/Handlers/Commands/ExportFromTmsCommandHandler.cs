using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Localization.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Localization.Handlers.Commands;

public class ExportFromTmsCommandHandler : IRequestHandler<ExportFromTmsCommand, BaseCommandResponse<Guid>>
{
    private readonly IAdminContext _adminContext;
    private readonly ITranslationManagementProvider _translationProvider;
    private readonly ITranslationResolver _translationResolver;
    private readonly IBundleFileWriter _bundleFileWriter;
    private readonly TranslationMetrics _metrics;
    private readonly ILogger<ExportFromTmsCommandHandler> _logger;

    public ExportFromTmsCommandHandler(
        IAdminContext adminContext,
        ITranslationManagementProvider translationProvider,
        ITranslationResolver translationResolver,
        IBundleFileWriter bundleFileWriter,
        TranslationMetrics metrics,
        ILogger<ExportFromTmsCommandHandler> logger)
    {
        _adminContext = adminContext;
        _translationProvider = translationProvider;
        _translationResolver = translationResolver;
        _bundleFileWriter = bundleFileWriter;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(ExportFromTmsCommand request, CancellationToken cancellationToken)
    {
        var actor = await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (!actor.HasValue || !await _adminContext.IsInstanceAdminAsync(actor.Value, cancellationToken))
        {
            const string message = "Instance administrator authority is required to export localization bundles from TMS.";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        IEnumerable<TranslationExport> exports;
        try
        {
            exports = await _translationProvider.ExportTranslationsAsync(request.LanguageCode, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LOCALIZATION] Export from TMS failed for {Language}", request.LanguageCode);
            _metrics.RecordStaticBundleOperation("export_from_tms", request.LanguageCode, "provider_error");
            var message = $"Export from TMS failed for language '{request.LanguageCode}': {ex.Message}";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        var translations = exports
            .Where(e => !string.IsNullOrEmpty(e.KeyName))
            .GroupBy(e => e.KeyName)
            .ToDictionary(g => g.Key, g => g.First().Value ?? string.Empty);

        if (translations.Count == 0)
        {
            var message =
                $"No translations found for language '{request.LanguageCode}'. Verify the TMS has translations for this language.";
            _metrics.RecordStaticBundleOperation("export_from_tms", request.LanguageCode, "empty");
            return BaseCommandResponse.Validation<Guid>([message], message);
        }

        try
        {
            var path = await _bundleFileWriter.WriteBundleAsync(request.LanguageCode, translations, cancellationToken);
            await _translationResolver.InvalidateLanguageAsync(request.LanguageCode, cancellationToken);

            var message =
                $"Exported {translations.Count} translations for language '{request.LanguageCode}' → {path}";
            _metrics.RecordStaticBundleOperation("export_from_tms", request.LanguageCode, "success");
            return BaseCommandResponse.Success(Guid.CreateVersion7(), message);
        }
        catch (BundleWriteException ex)
        {
            _logger.LogError(ex, "[LOCALIZATION] Bundle persistence failed for {Language}", request.LanguageCode);
            _metrics.RecordStaticBundleOperation("export_from_tms", request.LanguageCode, "write_error");
            var message = $"Failed to persist bundle for '{request.LanguageCode}': {ex.Message}";
            return BaseCommandResponse.Validation<Guid>([message], message);
        }
    }
}
