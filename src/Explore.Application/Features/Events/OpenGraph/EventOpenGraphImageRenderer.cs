using Explore.Application.Features.Events.Requests.Queries;

namespace Explore.Application.Features.Events.OpenGraph;

public interface IEventOpenGraphImageRenderer
{
    Task<EventOpenGraphImageRenderResult> RenderAsync(
        EventOpenGraphImageRenderRequest request,
        CancellationToken cancellationToken);
}

public sealed record EventOpenGraphImageRenderResult
{
    public EventOpenGraphImageRenderResult(ReadOnlyMemory<byte> PngBytes, string ETag)
    {
        this.PngBytes = PngBytes.ToArray();
        this.ETag = ETag;
    }

    public ReadOnlyMemory<byte> PngBytes { get; }
    public string ETag { get; }
}
