using Explore.Application.Features.Events.OpenGraph;
using MediatR;

namespace Explore.Application.Features.Events.Requests.Queries;

public sealed record GetPublicEventOpenGraphImageRequest : IRequest<EventOpenGraphImageRenderResult?>
{
    public required string SlugCode { get; init; }
}
