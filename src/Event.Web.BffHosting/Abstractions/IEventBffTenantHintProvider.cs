namespace Event.Web.BffHosting.Abstractions;

public interface IEventBffTenantHintProvider
{
    string? ResolveTenantSlug(HttpContext httpContext);
}
