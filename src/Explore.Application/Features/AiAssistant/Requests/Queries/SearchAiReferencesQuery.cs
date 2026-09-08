using Explore.Application.DTOs.Ai;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Requests.Queries;

public sealed record SearchAiReferencesQuery : IRequest<IReadOnlyList<AiReferenceSearchResultDto>>
{
    public string SearchTerm { get; init; } = string.Empty;
    public int Limit { get; init; } = 10;
}
