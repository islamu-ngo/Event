using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Events.OpenGraph;

namespace Explore.Application.Features.Events.Requests.Queries;

public sealed record GetPublicEventOpenGraphImageRequest : IQuery<EventOpenGraphImageRenderResult?>
{
    public required string SlugCode { get; init; }
}
