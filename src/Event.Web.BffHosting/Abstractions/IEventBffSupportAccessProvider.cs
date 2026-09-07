namespace Event.Web.BffHosting.Abstractions;

public interface IEventBffSupportAccessProvider
{
    ValueTask<string?> ResolveSupportAccessSessionIdAsync(HttpContext httpContext, CancellationToken cancellationToken);
}
