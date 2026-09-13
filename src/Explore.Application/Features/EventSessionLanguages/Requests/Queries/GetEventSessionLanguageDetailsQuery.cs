using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionLanguages.Requests.Queries;

public sealed record GetEventSessionLanguageDetailsQuery : IQuery<EventSessionLanguageDto?>
{
    public int Id { get; init; }
}
