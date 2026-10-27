using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.ConfigurationImport;

public sealed class ConfigurationImportPreviewHttpResult(
    ConfigurationImportPreviewDisclosure disclosure,
    Func<ConfigurationImportPreviewResult, HalResource<ConfigurationImportPreviewResult>> resource,
    IOptions<JsonOptions> json) : ActionResult
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var http = context.HttpContext;
        var response = http.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        bool deliveryAttempted = false;
        CancellationTokenRegistration abort = default;
        await using var owner = disclosure;
        try
        {
            await owner.DiscloseAsync(
                (view, _) => Task.FromResult<ReadOnlyMemory<byte>>(
                    JsonSerializer.SerializeToUtf8Bytes(resource(view), json.Value.JsonSerializerOptions)),
                async (bytes, token) =>
                {
                    abort = token.Register(static state => ((HttpContext)state!).Abort(), http);
                    token.ThrowIfCancellationRequested();
                    deliveryAttempted = true;
                    response.StatusCode = StatusCodes.Status200OK;
                    response.ContentType = "application/hal+json";
                    response.ContentLength = bytes.Length;
                    await response.Body.WriteAsync(bytes, token);
                    token.ThrowIfCancellationRequested();
                }, async token =>
                {
                    token.ThrowIfCancellationRequested();
                    await response.CompleteAsync();
                    token.ThrowIfCancellationRequested();
                }, http.RequestAborted);
        }
        catch
        {
            if (deliveryAttempted || response.HasStarted) http.Abort();
            throw;
        }
        finally { abort.Dispose(); }
    }
}
