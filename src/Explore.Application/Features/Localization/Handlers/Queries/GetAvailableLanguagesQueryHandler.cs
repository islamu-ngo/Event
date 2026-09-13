using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Localization.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Localization.Handlers.Queries;

public class GetAvailableLanguagesQueryHandler : IQueryHandler<GetAvailableLanguagesQuery, List<string>>
{
    private readonly ITranslationManagementProvider _translationProvider;

    public GetAvailableLanguagesQueryHandler(ITranslationManagementProvider translationProvider)
    {
        _translationProvider = translationProvider;
    }

    public async Task<List<string>> QueryAsync(GetAvailableLanguagesQuery request, CancellationToken cancellationToken)
    {
        var languages = await _translationProvider.GetAvailableLanguagesAsync(cancellationToken);
        return languages.ToList();
    }
}
