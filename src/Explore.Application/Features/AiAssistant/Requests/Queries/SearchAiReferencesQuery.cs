using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Ai;

namespace Explore.Application.Features.AiAssistant.Requests.Queries;

public sealed record SearchAiReferencesQuery : IQuery<IReadOnlyList<AiReferenceSearchResultDto>>
{
    public string SearchTerm { get; init; } = string.Empty;
    public int Limit { get; init; } = 10;
}
