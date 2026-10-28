using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Events.Handlers.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Explore.API.Services.Calendar;

/// <summary>
/// Builds ICS privately and holds the original anonymous native lease through the
/// actual body write and response completion. Deadline cancellation aborts the transport; a started
/// calendar response is never replaced with another payload after authority loss.
/// </summary>
public sealed class PublicEventCalendarHttpResult(
    PublicEventCalendarProjection pending, IEventCalendarFileBuilder builder, IPublicUrlBuilder urls,
    Func<ModerationPublicEventCalendarOutcome, ActionResult> failure) : ActionResult
{
    public override async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        await using var projection = pending;
        var outputAttempted = false;
        var outcome = await projection.DiscloseAsync((export, _) =>
        {
            var publicUrl = new Uri(urls.GetEventUrl(export.EventId));
            var content = builder.Build(export, publicUrl);
            var sanitized = string.Concat((export.Slug ?? export.Title).Trim()
                .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
            sanitized = string.Join('-', sanitized.Split('-',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var name = (string.IsNullOrWhiteSpace(sanitized) ? "event" : sanitized.ToLowerInvariant()) + ".ics";
            return Task.FromResult((Bytes: Encoding.UTF8.GetBytes(content), Name: name));
        }, async (file, token) =>
        {
            outputAttempted = true;
            using var abort = token.Register(static state => ((HttpContext)state!).Abort(), context.HttpContext);
            token.ThrowIfCancellationRequested();
            response.ContentType = "text/calendar; charset=utf-8";
            response.ContentLength = file.Bytes.Length;
            response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileNameStar = file.Name
            }.ToString();
            response.StatusCode = StatusCodes.Status200OK;
            await response.Body.WriteAsync(file.Bytes, token);
            token.ThrowIfCancellationRequested();
            await response.CompleteAsync();
        }, context.HttpContext.RequestAborted);
        if (outcome == ModerationPublicEventCalendarOutcome.Allowed) return;
        if (outputAttempted || response.HasStarted)
        {
            context.HttpContext.Abort();
            return;
        }
        await failure(outcome).ExecuteResultAsync(context);
    }
}
