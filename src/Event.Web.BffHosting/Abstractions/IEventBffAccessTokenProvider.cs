namespace Event.Web.BffHosting.Abstractions;

public interface IEventBffAccessTokenProvider
{
    ValueTask<string?> ResolveAccessTokenAsync(HttpContext httpContext, CancellationToken cancellationToken);
}
