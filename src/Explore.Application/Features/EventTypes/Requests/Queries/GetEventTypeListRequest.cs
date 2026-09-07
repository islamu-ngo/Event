using Explore.Application.DTOs.EventType;
using MediatR;

namespace Explore.Application.Features.EventTypes.Requests.Queries;

public sealed record GetEventTypeListRequest : IRequest<List<EventTypeListDto>>
{
    public int Id { get; init; }
    public required string FullName { get; init; }
    public string? Description { get; init; }
}
