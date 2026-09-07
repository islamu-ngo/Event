using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Localization.Requests.Queries;
using Explore.Domain.Common.Localization;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace Explore.Application.Features.Localization.Handlers.Queries;

public class GetTranslationsQueryHandler : IRequestHandler<GetTranslationsQuery, Dictionary<string, string>>
{
    private readonly ITranslationManagementProvider _translationProvider;

    public GetTranslationsQueryHandler(ITranslationManagementProvider translationProvider)
    {
        _translationProvider = translationProvider;
    }

    public async Task<Dictionary<string, string>> Handle(GetTranslationsQuery request, CancellationToken cancellationToken)
    {
        if (!CultureRegistry.TryGetEntry(request.LanguageCode, out var culture))
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure(nameof(request.LanguageCode), "Language code is not supported.")
            });
        }

        var exports = await _translationProvider.ExportTranslationsAsync(culture.Code, cancellationToken);
        return exports.ToDictionary(e => e.KeyName, e => e.Value);
    }
}
