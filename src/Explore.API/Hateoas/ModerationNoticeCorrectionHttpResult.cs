using Explore.Application.Features.Moderation.Notices;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Hateoas;

/// <summary>Completes the response inside a fresh native disclosure, never through a deferred formatter or ETag buffer.</summary>
public sealed class ModerationNoticeCorrectionHttpResult(ModerationNoticeCorrectionReadResult pending) : ActionResult
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var http = context.HttpContext;
        var response = http.Response;
        response.Headers.CacheControl = "private, no-store";
        response.Headers.Pragma = "no-cache";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.XContentTypeOptions = "nosniff";
        http.RequestAborted.ThrowIfCancellationRequested();
        if (!pending.IsValid)
        {
            var problem = pending.AuthenticationRequired
                ? new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Authentication required" }
                : new ValidationProblemDetails(new Dictionary<string, string[]>
                {
                    ["request"] = pending.ValidationErrors.ToArray()
                })
                { Status = StatusCodes.Status400BadRequest, Title = "Invalid correction request" };
            await new ObjectResult(problem) { StatusCode = problem.Status }.ExecuteResultAsync(context);
            return;
        }
        bool outputAttempted = false;
        try
        {
            bool disclosed = await pending.DiscloseAsync(async (bytes, token) =>
            {
                using var abort = token.Register(static state => ((HttpContext)state!).Abort(), http);
                token.ThrowIfCancellationRequested();
                outputAttempted = true;
                response.StatusCode = StatusCodes.Status200OK;
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength = bytes.Length;
                await response.Body.WriteAsync(bytes, token);
                token.ThrowIfCancellationRequested();
                await response.CompleteAsync();
                token.ThrowIfCancellationRequested();
            }, http.RequestAborted);
            if (disclosed) return;
            if (outputAttempted || response.HasStarted)
                throw new InvalidOperationException("Correction output cannot be replaced after disclosure starts.");
            await new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Correction unavailable",
                Detail = "The requested correction is unavailable."
            })
            { StatusCode = StatusCodes.Status404NotFound }.ExecuteResultAsync(context);
        }
        catch
        {
            if (outputAttempted || response.HasStarted) http.Abort();
            throw;
        }
    }
}
