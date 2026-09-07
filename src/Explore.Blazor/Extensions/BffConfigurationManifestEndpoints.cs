using System.Net.Http.Headers;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.ControlPlane;
using Explore.Blazor.Client.Routing.ControlPlane;
using Explore.Blazor.Services.Preferences;

namespace Explore.Blazor.Extensions;

public static class BffConfigurationManifestEndpoints
{
    private const string MediaType =
        "application/vnd.islamu.configuration-manifest.v1alpha2+json";
    private const string TenantMediaType =
        "application/vnd.islamu.tenant-configuration-package.v1alpha2+json";
    private const string OverridesFileName = "configuration-manifest-overrides.json";
    private const string PortableFileName = "configuration-manifest-portable.json";
    private const int MaximumBytes = 4 * 1024 * 1024;

    public static WebApplication MapConfigurationManifestEndpoints(
        this WebApplication app)
    {
        app.MapGet(
                ConfigurationManifestExportRoutes.BffExport,
                HandleDownloadAsync)
            .RequireAuthorization()
            .ExcludeFromDescription();

        app.MapGet(
                ConfigurationManifestExportRoutes.BffTenantExport,
                HandleTenantDownloadAsync)
            .RequireAuthorization()
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<IResult> HandleTenantDownloadAsync(
        Guid tenantId,
        ConfigurationManifestExportView? view,
        HttpContext context,
        ITenantOnboardingClient tenantOnboardingClient,
        ITenant_ConfigurationClient tenantConfigurationClient,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache";
        context.Response.Headers.Pragma = "no-cache";
        var logger = loggerFactory.CreateLogger("TenantConfigurationPackageExport");

        try
        {
            HalResourceOfTenantOnboardingStatusDto status =
                await tenantOnboardingClient.GetTenantOnboardingStatusAsync(
                    cancellationToken: cancellationToken);
            if (status.TenantId != tenantId
                || !HasGetCapability(
                    status._links,
                    ControlPlaneLinkRelations.ExportTenantConfigurationPackage))
            {
                return Results.Problem(
                    title: "Tenant configuration export unavailable",
                    detail: "The current tenant capabilities do not permit this export.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            using FileResponse response =
                await tenantConfigurationClient.ExportTenantConfigurationPackageAsync(
                    tenantId,
                    view ?? ConfigurationManifestExportView.Overrides,
                    cancellationToken: cancellationToken);
            if (!HasExpectedContentType(response.Headers, TenantMediaType)
                || !TryGetSafeTenantFileName(response.Headers, out string fileName))
            {
                logger.LogWarning(
                    "Rejected a tenant configuration export response with invalid file metadata.");
                return InvalidDownstreamResponse();
            }

            byte[]? content = await ReadBoundedAsync(response.Stream, cancellationToken);
            return content is null
                ? InvalidDownstreamResponse()
                : Results.File(
                    content,
                    TenantMediaType,
                    fileName,
                    enableRangeProcessing: false);
        }
        catch (ApiException exception)
        {
            logger.LogWarning(
                "Tenant configuration export forwarding failed. Status={StatusCode}",
                exception.StatusCode);
            return BffForwardingResults.Problem(
                exception,
                "The tenant configuration package could not be downloaded.",
                "Tenant configuration export failed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Tenant configuration export forwarding failed. FailureType={FailureType}",
                exception.GetType().Name);
            return InvalidDownstreamResponse();
        }
    }

    private static async Task<IResult> HandleDownloadAsync(
        ConfigurationManifestExportView? view,
        HttpContext context,
        IControlPlaneClient controlPlaneClient,
        IControl_Plane_ConfigurationClient controlPlaneConfigurationClient,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store, no-cache";
        context.Response.Headers.Pragma = "no-cache";

        ConfigurationManifestExportView requestedView =
            view ?? ConfigurationManifestExportView.Overrides;
        string relation = RelationFor(requestedView);
        string expectedFileName = FileNameFor(requestedView);
        var logger = loggerFactory.CreateLogger("ConfigurationManifestExport");

        try
        {
            HalResourceOfControlPlaneOverviewDto overview =
                await controlPlaneClient.GetControlPlaneOverviewAsync(
                    cancellationToken: cancellationToken);
            if (!HasGetCapability(overview, relation))
            {
                return Results.Problem(
                    title: "Configuration manifest export unavailable",
                    detail: "The current control-plane capabilities do not permit this export.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            using FileResponse response =
                await controlPlaneConfigurationClient.ExportConfigurationManifestAsync(
                    requestedView,
                    cancellationToken: cancellationToken);

            if (!HasExpectedContentType(response.Headers)
                || !HasExpectedFileName(response.Headers, expectedFileName))
            {
                logger.LogWarning(
                    "Rejected a configuration-manifest export response with invalid file metadata.");
                return InvalidDownstreamResponse();
            }

            byte[]? content = await ReadBoundedAsync(
                response.Stream,
                cancellationToken);
            if (content is null)
            {
                logger.LogWarning(
                    "Rejected a configuration-manifest export response exceeding the BFF byte limit.");
                return InvalidDownstreamResponse();
            }

            return Results.File(
                content,
                MediaType,
                expectedFileName,
                enableRangeProcessing: false);
        }
        catch (ApiException exception)
        {
            logger.LogWarning(
                "Configuration-manifest export forwarding failed. Status={StatusCode}",
                exception.StatusCode);
            return BffForwardingResults.Problem(
                exception,
                "The configuration manifest could not be downloaded.",
                "Configuration manifest export failed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Configuration-manifest export forwarding failed. FailureType={FailureType}",
                exception.GetType().Name);
            return InvalidDownstreamResponse();
        }
    }

    private static bool HasGetCapability(
        HalResourceOfControlPlaneOverviewDto overview,
        string relation) =>
        HasGetCapability(overview._links, relation);

    private static bool HasGetCapability(
        IDictionary<string, HalLink>? links,
        string relation) =>
        links?.TryGetValue(relation, out HalLink? link) == true
        && string.Equals(link.Method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase);

    private static string RelationFor(ConfigurationManifestExportView view) =>
        view switch
        {
            ConfigurationManifestExportView.Overrides =>
                ControlPlaneLinkRelations.ExportConfigurationOverrides,
            ConfigurationManifestExportView.Portable =>
                ControlPlaneLinkRelations.ExportConfigurationPortable,
            _ => throw new ArgumentOutOfRangeException(nameof(view), view, "Unsupported export view.")
        };

    private static string FileNameFor(ConfigurationManifestExportView view) =>
        view switch
        {
            ConfigurationManifestExportView.Overrides => OverridesFileName,
            ConfigurationManifestExportView.Portable => PortableFileName,
            _ => throw new ArgumentOutOfRangeException(nameof(view), view, "Unsupported export view.")
        };

    private static bool HasExpectedContentType(
        IReadOnlyDictionary<string, IEnumerable<string>> headers)
    {
        string? value = HeaderValue(headers, "Content-Type");
        return MediaTypeHeaderValue.TryParse(value, out MediaTypeHeaderValue? parsed)
            && string.Equals(parsed.MediaType, MediaType, StringComparison.Ordinal);
    }

    private static bool HasExpectedContentType(
        IReadOnlyDictionary<string, IEnumerable<string>> headers,
        string mediaType)
    {
        string? value = HeaderValue(headers, "Content-Type");
        return MediaTypeHeaderValue.TryParse(value, out MediaTypeHeaderValue? parsed)
            && string.Equals(parsed.MediaType, mediaType, StringComparison.Ordinal);
    }

    private static bool HasExpectedFileName(
        IReadOnlyDictionary<string, IEnumerable<string>> headers,
        string expectedFileName)
    {
        string? value = HeaderValue(headers, "Content-Disposition");
        if (!ContentDispositionHeaderValue.TryParse(
                value,
                out ContentDispositionHeaderValue? parsed))
        {
            return false;
        }

        string? fileName = parsed.FileNameStar ?? parsed.FileName;
        return string.Equals(
            fileName?.Trim('"'),
            expectedFileName,
            StringComparison.Ordinal);
    }

    private static bool TryGetSafeTenantFileName(
        IReadOnlyDictionary<string, IEnumerable<string>> headers,
        out string fileName)
    {
        fileName = string.Empty;
        string? value = HeaderValue(headers, "Content-Disposition");
        if (!ContentDispositionHeaderValue.TryParse(
                value,
                out ContentDispositionHeaderValue? parsed))
        {
            return false;
        }

        fileName = (parsed.FileNameStar ?? parsed.FileName)?.Trim('"') ?? string.Empty;
        const string prefix = "tenant-configuration-package-";
        const string suffix = ".json";
        string slug = fileName.StartsWith(prefix, StringComparison.Ordinal)
            && fileName.EndsWith(suffix, StringComparison.Ordinal)
            ? fileName[prefix.Length..^suffix.Length]
            : string.Empty;
        return fileName.Length <= 200
            && slug.Length > 0
            && slug.All(character =>
                character is >= 'a' and <= 'z'
                    or >= '0' and <= '9'
                    or '-');
    }

    private static string? HeaderValue(
        IReadOnlyDictionary<string, IEnumerable<string>> headers,
        string name) =>
        headers.FirstOrDefault(pair =>
                string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            .Value?
            .SingleOrDefault();

    private static async Task<byte[]?> ReadBoundedAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        byte[] buffer = GC.AllocateUninitializedArray<byte>(MaximumBytes + 1);
        var total = 0;
        while (total < buffer.Length)
        {
            int read = await source.ReadAsync(
                buffer.AsMemory(total, buffer.Length - total),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total > MaximumBytes ? null : buffer[..total];
    }

    private static IResult InvalidDownstreamResponse() =>
        Results.Problem(
            title: "Configuration manifest export failed",
            detail: "The configuration service returned an invalid download response.",
            statusCode: StatusCodes.Status502BadGateway);
}
