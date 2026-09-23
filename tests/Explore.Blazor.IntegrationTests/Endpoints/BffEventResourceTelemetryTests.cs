using System.Collections.Concurrent;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Explore.Blazor.IntegrationTests.Endpoints;

[NotInParallel]
public sealed class BffEventResourceTelemetryTests
{
    [Test]
    public async Task SplitIngressAndRealOutboundSpansExcludeResourceAndSessionIdentities()
    {
        Guid resourceId = Guid.CreateVersion7(), sessionId = Guid.CreateVersion7();
        byte[] bytes = "%PDF-1.7\ntrace transport\n%%EOF"u8.ToArray();
        string title = $"private-title-{Guid.CreateVersion7():N}";
        string credential = Guid.CreateVersion7().ToString("N");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = builder.Build();
        upstream.MapPost("/api/eventresource/{id:guid}/upload-sessions", () => Results.Json(new
        {
            success = true,
            id = new
            {
                id = sessionId, tenantId = Guid.CreateVersion7(), provider = "local",
                expectedSizeBytes = bytes.Length, reservedBytes = bytes.Length,
                contentType = "application/pdf", safeDisplayName = "handout.pdf",
                purpose = "event_resource", visibility = "private_owner", status = "reserved",
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(10), maxUploadBytes = 10485760,
                tenantQuotaBytes = 1073741824, usedBytes = 0, totalReservedBytes = bytes.Length
            }
        }));
        upstream.MapPut("/api/storageobject/upload-sessions/{id:guid}/content", async (HttpContext context) =>
        {
            await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
            return Results.Json(new { success = true, id = new
                { id = sessionId, storageObjectId = Guid.CreateVersion7(), status = "finalized" } });
        });
        upstream.MapGet("/api/eventresource/{id:guid}/content", () => Results.NotFound());
        await upstream.StartAsync();
        string address = upstream.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        var exporter = new ResourceSpans();
        var antiforgery = Substitute.For<IAntiforgery>();
        antiforgery.ValidateRequestAsync(Arg.Any<HttpContext>()).Returns(Task.CompletedTask);
        antiforgery.GetAndStoreTokens(Arg.Any<HttpContext>()).Returns(new AntiforgeryTokenSet(
            "validated-by-test-antiforgery", Guid.CreateVersion7().ToString("N"),
            "__RequestVerificationToken", "X-CSRF-TOKEN"));
        await using var baseFactory = new BlazorBffWebApplicationFactory();
        await using var factory = baseFactory.WithWebHostBuilder(host =>
        {
            host.UseSetting("ExploreApi:BaseUrl", address);
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAntiforgery>();
                services.AddSingleton(antiforgery);
                services.AddOpenTelemetry().WithTracing(tracing =>
                    tracing.AddProcessor(new SimpleActivityExportProcessor(exporter)));
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, HandleCookies = false
        });
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(
            Guid.CreateVersion7(), "Transport reader", ("test:access_token",
                new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(expires: DateTime.UtcNow.AddMinutes(10))))));
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "validated-by-test-antiforgery");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var reserved = await client.PostAsJsonAsync($"/bff/event-resources/{resourceId:D}/upload-session",
            new { expectedVersion = Guid.CreateVersion7(), fileName = "handout.pdf", contentType = "application/pdf",
                expectedSizeBytes = bytes.Length }, deadline.Token);
        reserved.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await reserved.Content.ReadAsStringAsync(deadline.Token));
        string opaque = body.RootElement.GetProperty("uploadSessionId").GetString()!;
        using var form = new MultipartFormDataContent();
        using var session = new StringContent(opaque);
        using var type = new StringContent("application/pdf");
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(session, "uploadSessionId");
        form.Add(type, "contentType");
        form.Add(file, "file", "handout.pdf");
        using var uploaded = await client.PostAsync("/bff/storage/upload-proxy", form, deadline.Token);
        uploaded.EnsureSuccessStatusCode();
        using var denied = await client.GetAsync(
            $"/api/eventresource/{resourceId:D}/content?title={title}&token={credential}", deadline.Token);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Task.WhenAll(exporter.Ingress.Task, exporter.Reservation.Task, exporter.Finalization.Task,
            exporter.Denial.Task).WaitAsync(deadline.Token);
        string emitted = string.Join('\n', exporter.Values);
        foreach (string secret in new[] { resourceId.ToString("D"), sessionId.ToString("D"), opaque, title, credential })
            await Assert.That(emitted).DoesNotContain(secret);
    }

    private sealed class ResourceSpans : BaseExporter<Activity>
    {
        public ConcurrentQueue<string> Values { get; } = new();
        public TaskCompletionSource Ingress { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Reservation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finalization { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Denial { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (Activity activity in batch)
            {
                string tags = string.Join('\n', activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"));
                if (!tags.Contains("/eventresource/", StringComparison.Ordinal)
                    && !tags.Contains("/event-resources/", StringComparison.Ordinal)
                    && !tags.Contains("/storageobject/upload-sessions/", StringComparison.Ordinal))
                    continue;
                Values.Enqueue(activity.DisplayName + "\n" + tags);
                foreach (var baggage in activity.Baggage) Values.Enqueue($"{baggage.Key}={baggage.Value}");
                foreach (var item in activity.Events)
                    Values.Enqueue(item.Name + "\n" + string.Join('\n', item.Tags.Select(tag => $"{tag.Key}={tag.Value}")));
                if (activity.Kind == ActivityKind.Server && tags.Contains("/bff/event-resources/", StringComparison.Ordinal))
                    Ingress.TrySetResult();
                if (activity.Kind == ActivityKind.Client && tags.Contains("/upload-sessions", StringComparison.Ordinal))
                {
                    if (tags.Contains("/storageobject/", StringComparison.Ordinal)) Finalization.TrySetResult();
                    else Reservation.TrySetResult();
                }
                if (activity.Kind == ActivityKind.Client && activity.GetTagItem("http.response.status_code")?.ToString() == "404")
                    Denial.TrySetResult();
            }
            return ExportResult.Success;
        }
    }
}
