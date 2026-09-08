using Asp.Versioning;
using Asp.Versioning.ApiExplorer;

namespace Explore.API.Extensions;

/// <summary>
/// Configures API versioning with three non-URL version readers combined:
///   1. Media-type parameter — <c>Accept: application/json;v=0.1</c> or <c>application/hal+json;v=0.1</c>
///   2. Query-string parameter — <c>?api-version=0.1</c>
///   3. Custom header — <c>X-Api-Version: 0.1</c>
///
/// When no version is specified, defaults to the current version (<see cref="ApiVersioningOptions.DefaultApiVersion"/>).
///
/// URL-segment versioning (e.g. <c>/api/v0.1/actor</c>) is intentionally disallowed. Every endpoint has
/// exactly one canonical path (<c>/api/actor</c>), enforced by <c>NoUrlSegmentVersioning</c> in the
/// architecture test suite. This keeps <c>operationId</c>, <c>RouteNames</c>, and HAL link generation stable.
/// </summary>
public static class ApiVersioningExtensions
{
    public static IServiceCollection AddApiMediaTypeVersioning(this IServiceCollection services)
    {
        var mediaTypeReader = new MediaTypeApiVersionReaderBuilder()
            .Parameter("v")
            .Include("application/json")
            .Include("application/hal+json")
            .Build();

        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(0, 1);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = ApiVersionReader.Combine(
                mediaTypeReader,
                new QueryStringApiVersionReader("api-version"),
                new HeaderApiVersionReader("X-Api-Version"));
        })
        .AddMvc()
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });

        return services;
    }
}
