using Explore.Application.DTOs.EventSessionLanguage;
using MediatR;

namespace Explore.Application.Features.EventSessionLanguages.Requests.Queries;

public sealed record GetEventSessionLanguageDetailsRequest : IRequest<EventSessionLanguageDto>
{
    public int Id { get; init; }
}
