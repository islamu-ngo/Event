using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Localization.Requests.Queries;

public sealed record GetAvailableLanguagesQuery : IQuery<List<string>>
{
}
