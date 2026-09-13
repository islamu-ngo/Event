using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Localization.Requests.Queries;

public sealed record ExportLocalizationBundleQuery : IQuery<IReadOnlyDictionary<string, string>>
{
    public required string LanguageCode { get; init; }
}
