using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class EventResourceAccessDiagnosticsTests
{
    [Test]
    public async Task ProtectedDestinationAppearsOnlyInAuthorizedRedirectNotInDiagnostics()
    {
        string sentinel = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string destination = $"https://resource.example.org/visit/{sentinel}?ticket={sentinel}";
        Guid eventId = Guid.CreateVersion7(), resourceId = Guid.CreateVersion7();
        using var logs = new RequestLogs();
        using var spans = new RequestSpans();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(logCapture: logs);
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid userId, version;
        await using (var db = factory.CreateDatabase())
        {
            var user = await db.Users.SingleAsync(row => row.Pii!.Email == credentials.Identifier);
            userId = user.Id;
            var actor = await db.Actors.SingleAsync(row => row.UserId == userId);
            db.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                UserId = userId, User = user, ActorId = actor.Id,
                StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = DateTime.UtcNow
            });
            db.SystemSettings.AddRange(
                new SystemSetting
                {
                    SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                    Value = "\"local\"", ValueType = SettingValueType.String, Category = "Security"
                },
                new SystemSetting
                {
                    SettingKey = GovernanceSettingKeys.EventResources.ExternalOrigins,
                    Value = "[\"https://resource.example.org\"]", ValueType = SettingValueType.Json,
                    Category = "EventResources"
                });
            var parent = new Explore.Domain.Event
            {
                Id = eventId, Title = "Resource diagnostics", TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!
            };
            parent.Publish(DateTime.UtcNow);
            db.Events.Add(parent);
            db.EventRoleAssignments.Add(EventRoleAssignment.Create(PlatformDefaults.DefaultTenantId, eventId, userId,
                (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active, DateTime.UtcNow.AddMinutes(-1), null, userId));
            var resource = EventResource.CreateDraft(resourceId, PlatformDefaults.DefaultTenantId, eventId, null,
                new EventResourceMetadata
                {
                    Title = "Public link", PublicTitle = "Visit", Kind = EventResourceKindEnum.GeneralDocument,
                    DisclosureMode = EventResourceDisclosureModeEnum.Public
                }, EventResourceDeliveryTypeEnum.ExternalLink, EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, resourceId,
                    EventResourceAudienceKindEnum.Public)], userId, DateTime.UtcNow);
            db.EventResources.Add(resource);
            await db.SaveChangesAsync();
            version = resource.ConcurrencyStamp;
        }

        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddOpenTelemetry().WithTracing(tracing =>
                tracing.AddProcessor(new SimpleActivityExportProcessor(spans)))));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials))
        {
            login.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                json.RootElement.GetProperty("token").GetString());
        }

        const string destinationRoute = "/api/eventresource/{id:guid}/destination";
        const string accessRoute = "/api/eventresource/{id:guid}/access";
        // Observe exact completed request exports; do not rely on the response arriving after telemetry.
        var badLog = logs.Expect("PUT", destinationRoute, 400);
        var badSpan = spans.Expect("PUT", destinationRoute, 400);
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"bad-destination-{resourceId:N}");
        using (var bad = await client.PutAsJsonAsync($"/api/eventresource/{resourceId}/destination",
                   new { expectedVersion = version, destination = $"https://reader:{sentinel}@resource.example.org/visit" }))
        {
            await Assert.That(bad.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            string problem = await bad.Content.ReadAsStringAsync();
            await Assert.That(problem.Length).IsLessThan(4096);
            await Assert.That(problem).DoesNotContain(sentinel);
            await Assert.That(bad.Headers.Location).IsNull();
        }
        await Task.WhenAll(badLog, badSpan).WaitAsync(TimeSpan.FromSeconds(10));
        client.DefaultRequestHeaders.Remove("Idempotency-Key");

        var putLog = logs.Expect("PUT", destinationRoute, 200);
        var putSpan = spans.Expect("PUT", destinationRoute, 200);
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"destination-{resourceId:N}");
        using (var configured = await client.PutAsJsonAsync($"/api/eventresource/{resourceId}/destination",
                   new { expectedVersion = version, destination }))
        {
            await Assert.That(configured.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(await configured.Content.ReadAsStringAsync());
            await Assert.That(await configured.Content.ReadAsStringAsync()).DoesNotContain(sentinel);
        }
        await Task.WhenAll(putLog, putSpan).WaitAsync(TimeSpan.FromSeconds(10));
        await using (var db = factory.CreateDatabase())
        {
            db.EnableTenantFilterBypass("Read updated version for publication.");
            version = (await db.EventResources.SingleAsync(row => row.Id == resourceId)).ConcurrencyStamp;
        }
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"publish-{resourceId:N}");
        using (var published = await client.PostAsJsonAsync($"/api/eventresource/{resourceId}/publish",
                   new { expectedVersion = version }))
            await Assert.That(published.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(await published.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Authorization = null;

        using (var metadata = await client.GetAsync($"/api/eventresource/{resourceId}"))
        {
            await Assert.That(metadata.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await metadata.Content.ReadAsStringAsync()).DoesNotContain(sentinel);
            await Assert.That(metadata.Headers.Location).IsNull();
        }
        var getLog = logs.Expect("GET", accessRoute, 302);
        var getSpan = spans.Expect("GET", accessRoute, 302);
        using (var response = await client.GetAsync($"/api/eventresource/{resourceId}/access"))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(response.Headers.Location!.AbsoluteUri).IsEqualTo(destination);
            await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain(sentinel);
            await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        }
        await Task.WhenAll(getLog, getSpan).WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(string.Join('\n', logs.Values.Concat(spans.Values))).DoesNotContain(sentinel);
    }

    private sealed class RequestLogs : ILoggerProvider
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _expected = new();
        private readonly ConcurrentQueue<string> _values = new();
        public IEnumerable<string> Values => _values.ToArray();
        public Task Expect(string method, string route, int status) =>
            _expected.GetOrAdd($"HTTP {method} {route} responded {status}",
                _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);
        public void Dispose() { }

        private sealed class Capture(RequestLogs owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                string message = formatter(state, exception);
                owner._values.Enqueue($"{category}|{message}|{state}|{exception}");
                if (category == "Explore.API.Middleware.RequestLoggingMiddleware")
                    foreach (var expected in owner._expected)
                        if (message.Replace("\"", "", StringComparison.Ordinal)
                            .Contains(expected.Key, StringComparison.OrdinalIgnoreCase))
                            expected.Value.TrySetResult();
            }
        }
    }

    private sealed class RequestSpans : BaseExporter<Activity>
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _expected = new();
        private readonly ConcurrentQueue<string> _values = new();
        public IEnumerable<string> Values => _values.ToArray();
        public Task Expect(string method, string route, int status) =>
            _expected.GetOrAdd($"{method}|{route.TrimStart('/')}|{status}",
                _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                _values.Enqueue($"{activity.Source.Name}|{activity.DisplayName}");
                foreach (var tag in activity.TagObjects)
                    _values.Enqueue($"{tag.Key}={tag.Value}");
                foreach (var baggage in activity.Baggage)
                    _values.Enqueue($"baggage:{baggage.Key}={baggage.Value}");
                foreach (var activityEvent in activity.Events)
                {
                    _values.Enqueue($"event:{activityEvent.Name}");
                    foreach (var tag in activityEvent.Tags)
                        _values.Enqueue($"event:{tag.Key}={tag.Value}");
                }
                if (!activity.Source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
                    continue;
                string method = activity.GetTagItem("http.request.method")?.ToString()
                    ?? activity.GetTagItem("http.method")?.ToString() ?? "";
                string route = activity.GetTagItem("http.route")?.ToString() ?? "";
                string status = activity.GetTagItem("http.response.status_code")?.ToString()
                    ?? activity.GetTagItem("http.status_code")?.ToString() ?? "";
                if (_expected.TryGetValue($"{method}|{route.TrimStart('/')}|{status}", out var observed))
                    observed.TrySetResult();
            }
            return ExportResult.Success;
        }
    }
}
