using System.Globalization;
using System.Xml.Linq;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Seo.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("sitemap.xml")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public sealed class SitemapController(IMediator mediator, IPublicUrlBuilder publicUrlBuilder) : ControllerBase
{
    private static readonly IReadOnlyList<StaticSitemapEntry> StaticRoutes =
    [
        new("/", "daily", "1.0"),
        new("/events", "hourly", "0.9"),
        new("/about", "monthly", "0.5"),
        new("/contact", "monthly", "0.5"),
        new("/privacy", "yearly", "0.3"),
        new("/terms", "yearly", "0.3"),
        new("/community-guidelines", "yearly", "0.3")
    ];

    [HttpGet(Name = RouteNames.GetSitemap)]
    [AllowAnonymous]
    [OutputCache(PolicyName = "SitemapData")]
    [EndpointSummary("Get XML sitemap")]
    [EndpointDescription("Returns tenant-aware sitemap.xml with static public routes and published public events.")]
    [Produces("application/xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ContentResult> Get(CancellationToken cancellationToken = default)
    {
        var baseUrl = publicUrlBuilder.GetBaseUrl().TrimEnd('/');
        var eventEntries = await mediator.Send(new GetSitemapEventsQuery(), cancellationToken);

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "urlset",
                StaticRoutes.Select(entry => CreateUrlElement(ns, BuildAbsoluteUrl(baseUrl, entry.Path), null, entry.ChangeFrequency, entry.Priority))
                    .Concat(eventEntries.Select(entry => CreateUrlElement(
                        ns,
                        publicUrlBuilder.GetEventUrl(entry.EventId),
                        entry.LastModifiedAt,
                        "weekly",
                        "0.8")))));

        return Content(document.ToString(SaveOptions.DisableFormatting), "application/xml; charset=utf-8");
    }

    private static XElement CreateUrlElement(
        XNamespace ns,
        string location,
        DateTime? lastModifiedAt,
        string changeFrequency,
        string priority)
    {
        var element = new XElement(ns + "url",
            new XElement(ns + "loc", location),
            new XElement(ns + "changefreq", changeFrequency),
            new XElement(ns + "priority", priority));

        if (lastModifiedAt.HasValue)
        {
            element.Add(new XElement(
                ns + "lastmod",
                lastModifiedAt.Value.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        return element;
    }

    private static string BuildAbsoluteUrl(string baseUrl, string path)
    {
        return string.Concat(baseUrl, path.StartsWith('/') ? path : string.Concat('/', path));
    }

    private sealed record StaticSitemapEntry(string Path, string ChangeFrequency, string Priority);
}
