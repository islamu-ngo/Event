using System.Text.Json;
using Explore.Application.Features.Moderation.Inspection;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Hateoas;

public sealed record ModerationResourceQuarantineStateHeader(Guid ResourceId, int Page, bool HasNext);

/// <summary>Serializes only operational metadata while the actual current-reviewer and erasure fences remain held.</summary>
public sealed class ModerationResourceQuarantineStateHttpResult(
    ModerationResourceQuarantineStateResult pending) : ActionResult
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        var services = context.HttpContext.RequestServices;
        var routes = services.GetRequiredService<LinkGenerator>();
        var options = services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var outputAttempted = false;
        try
        {
            var disclosed = await pending.DiscloseAsync(async (page, current, token) =>
            {
                var items = page.Items.Select(item =>
                {
                    var resource = new HalResource<ModerationResourceQuarantineStateItem>(item);
                    if (item.ReleaseId is null)
                        resource.WithLink("record-safety-review", HalLink.CreateAction(
                            routes.GetPathByName(context.HttpContext, RouteNames.RecordModerationResourceSafetyReview,
                                new { resourceId = page.ResourceId })
                            ?? throw new InvalidOperationException("The native safety review route must be registered."), "POST"));
                    return resource;
                }).ToArray();
                var body = new HalResource<ModerationResourceQuarantineStateHeader>(
                    new(page.ResourceId, page.Page, page.HasNext)).WithEmbedded("quarantineCauses", items);
                body.WithLink(LinkRelations.Self, HalLink.CreateAction(
                    routes.GetPathByName(context.HttpContext, RouteNames.ReadModerationResourceQuarantineState,
                        new { resourceId = page.ResourceId })
                    ?? throw new InvalidOperationException("The native safety state route must be registered."), "POST"));
                var bytes = JsonSerializer.SerializeToUtf8Bytes(body, options);
                if (!await current(token)) return false;
                using var abort = token.Register(static state => ((HttpContext)state!).Abort(), context.HttpContext);
                token.ThrowIfCancellationRequested();
                outputAttempted = true;
                response.StatusCode = StatusCodes.Status200OK;
                response.ContentType = "application/hal+json";
                await response.Body.WriteAsync(bytes, token);
                token.ThrowIfCancellationRequested();
                await response.CompleteAsync();
                token.ThrowIfCancellationRequested();
                return true;
            }, context.HttpContext.RequestAborted);
            if (!disclosed && outputAttempted)
                context.HttpContext.Abort();
            else if (!disclosed)
                await new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound, Title = "Resource safety unavailable",
                    Detail = "The requested resource safety state is unavailable."
                }) { StatusCode = StatusCodes.Status404NotFound }.ExecuteResultAsync(context);
        }
        catch
        {
            context.HttpContext.Abort();
            throw;
        }
    }
}
