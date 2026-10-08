using Explore.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Models;

/// <summary>Owns DTO serialization after result filters and the final native disclosure check.</summary>
public sealed class EventResourceProjectionResult<T>(
    EventResourceReadProjection<T> projection,
    Func<T, CancellationToken, Task<ActionResult>> prepare,
    Func<EventResourceAuthorityOutcome, ActionResult> failure) : ActionResult where T : class
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var outcome = await projection.DiscloseAsync(prepare,
            (representation, _) => representation.ExecuteResultAsync(context),
            context.HttpContext.RequestAborted);
        if (outcome != EventResourceAuthorityOutcome.Allowed)
            await failure(outcome).ExecuteResultAsync(context);
    }
}
