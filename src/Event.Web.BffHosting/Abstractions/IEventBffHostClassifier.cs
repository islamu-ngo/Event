namespace Event.Web.BffHosting.Abstractions;

public interface IEventBffHostClassifier
{
    bool IsAdminHost(HttpContext httpContext);

    bool IsAdminHost(string? host);
}
