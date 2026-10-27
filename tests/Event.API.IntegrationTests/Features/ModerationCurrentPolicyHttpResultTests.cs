using System.Text.Json;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Queries;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Actual terminal rejection output. Native success and revocation witnesses live with the
/// original Persistence Database fixture; no substituted repository supplies success metadata.
/// </summary>
public sealed class ModerationCurrentPolicyHttpResultTests
{
    [Test]
    public async Task UnqualifiedDeclarationCannotAdvertisePublicationOrPreparation()
    {
        var scope = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var view = new ModerationCurrentPolicyView(Guid.CreateVersion7(), "instance", "uninitialized", scope,
            Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
            ModerationPolicyPublicationProjection.WriterGeneration, 0, null, 1, 1, new string('a', 64),
            ModerationPolicyPublicationProjection.AuthoringBounds(scope), null);
        var links = new ModerationCurrentPolicyLinkPolicy().GetLinks(view, null).ToArray();
        await Assert.That(links.Any(link => link.Method == HttpMethods.Post)).IsFalse();
        await Assert.That(links.Select(link => link.Rel).ToHashSet()
            .SetEquals(["self", "refresh", "reference-catalogue"])).IsTrue();
        var collections = new ModerationCurrentPolicyCollectionLinkPolicy();
        await Assert.That(collections.GetCollectionLinks(null).Any()).IsFalse();
        await Assert.That(collections.GetItemLinks(view, null).Any()).IsFalse();
    }

    [Test]
    [Arguments(ModerationCurrentPolicyReadOutcome.Unauthenticated, StatusCodes.Status401Unauthorized)]
    [Arguments(ModerationCurrentPolicyReadOutcome.Denied, StatusCodes.Status403Forbidden)]
    [Arguments(ModerationCurrentPolicyReadOutcome.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    [Arguments(ModerationCurrentPolicyReadOutcome.Invalid, StatusCodes.Status400BadRequest)]
    public async Task RejectedPendingReadWritesOnlyProblemBytesWithPrivacyHeaders(
        ModerationCurrentPolicyReadOutcome outcome, int status)
    {
        var http = new DefaultHttpContext();
        await using var body = new MemoryStream();
        http.Response.Body = body;
        var context = new ActionContext(http, new RouteData(), new ActionDescriptor());
        // The assembler is deliberately absent: a rejected pending result must never use it.
        var result = new ModerationCurrentPolicyHttpResult(new(outcome), null!,
            Options.Create(new JsonOptions()), _ => new TerminalProblem(status));
        await result.ExecuteResultAsync(context);
        await Assert.That(http.Response.StatusCode).IsEqualTo(status);
        await Assert.That(http.Response.Headers.CacheControl.ToString()).IsEqualTo("no-store");
        await Assert.That(http.Response.Headers["Referrer-Policy"].ToString()).IsEqualTo("no-referrer");
        using var json = JsonDocument.Parse(body.ToArray());
        await Assert.That(json.RootElement.GetProperty("status").GetInt32()).IsEqualTo(status);
        await Assert.That(json.RootElement.TryGetProperty("authoring", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("_links", out _)).IsFalse();
    }

    private sealed class TerminalProblem(int status) : ActionResult
    {
        public override async Task ExecuteResultAsync(ActionContext context)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new ProblemDetails { Status = status });
            context.HttpContext.Response.StatusCode = status;
            context.HttpContext.Response.ContentType = "application/problem+json";
            await context.HttpContext.Response.Body.WriteAsync(bytes, context.HttpContext.RequestAborted);
        }
    }
}
