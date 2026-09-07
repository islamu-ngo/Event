using MediatR;

namespace Explore.Application.Features.Localization.Requests.Queries;

public sealed record ExportLocalizationBundleQuery : IRequest<IReadOnlyDictionary<string, string>>
{
    public required string LanguageCode { get; init; }
}
