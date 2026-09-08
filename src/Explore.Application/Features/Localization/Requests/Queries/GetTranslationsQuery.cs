using MediatR;

namespace Explore.Application.Features.Localization.Requests.Queries;

public sealed record GetTranslationsQuery : IRequest<Dictionary<string, string>>
{
    public required string LanguageCode { get; init; }
}
