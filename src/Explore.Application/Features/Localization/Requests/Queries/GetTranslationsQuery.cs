using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Localization.Requests.Queries;

public sealed record GetTranslationsQuery : IQuery<Dictionary<string, string>>
{
    public required string LanguageCode { get; init; }
}
