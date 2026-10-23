using System.Text.Json;
using Explore.Application.Features.Moderation.Inspection;
using Explore.Application.Hateoas;
using Explore.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Hateoas;

/// <summary>Native guards, not browser roles, select review actions. Metadata is serialized under terminal fences.</summary>
public sealed class ModerationResourceQuarantineReviewStateHttpResult(
    ModerationResourceQuarantineReviewStateResult pending) : ActionResult
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
            var disclosed = await pending.DiscloseAsync(async (state, current, token) =>
            {
                var body = new HalResource<ModerationResourceQuarantineReviewState>(state);
                foreach (var step in state.AllowedSteps)
                {
                    var relation = step switch
                    {
                        ResourceSafetyReviewStep.AdoptPurpose => "adopt-purpose",
                        ResourceSafetyReviewStep.AttestMaterialSafety => "attest-material-safety",
                        ResourceSafetyReviewStep.ApproveSafety => "approve-safety",
                        ResourceSafetyReviewStep.WithdrawPurpose => "withdraw-purpose",
                        ResourceSafetyReviewStep.ApproveContinuedQuarantine => "approve-continued-quarantine",
                        _ => throw new InvalidOperationException("A named existing review chain cannot propose a new purpose.")
                    };
                    Add(relation, RouteNames.RecordModerationResourceSafetyReview);
                }
                if (state.CanInspect) Add("inspect-material", RouteNames.InspectModerationResourceQuarantine);
                if (state.CanRelease) Add("release-quarantine", RouteNames.ReleaseModerationResourceQuarantine);
                Add(LinkRelations.Self, RouteNames.ReadModerationResourceQuarantineReviewState);
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

                void Add(string relation, string route)
                {
                    object values = route == RouteNames.RecordModerationResourceSafetyReview
                        ? new { resourceId = state.ResourceId }
                        : new { resourceId = state.ResourceId, quarantineId = state.QuarantineId };
                    body.WithLink(relation, HalLink.CreateAction(
                        routes.GetPathByName(context.HttpContext, route, values)
                        ?? throw new InvalidOperationException("The native safety route must be registered."), "POST"));
                }
            }, context.HttpContext.RequestAborted);
            if (!disclosed && outputAttempted)
                context.HttpContext.Abort();
            else if (!disclosed)
                await new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Review state unavailable",
                    Detail = "The requested resource review state is unavailable."
                })
                { StatusCode = StatusCodes.Status404NotFound }.ExecuteResultAsync(context);
        }
        catch
        {
            context.HttpContext.Abort();
            throw;
        }
    }
}
