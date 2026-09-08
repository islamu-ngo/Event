namespace Event.Web.BffHosting.Abstractions;

public interface IEventBffSetupSecretProvider
{
    ValueTask<string?> ResolveSetupSecretAsync(HttpContext httpContext, CancellationToken cancellationToken);
}
