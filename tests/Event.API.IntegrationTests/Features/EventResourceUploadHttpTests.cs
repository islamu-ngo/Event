using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventResource;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Models.Storage;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class EventResourceUploadHttpTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task ResourceManagerFinalizesThroughGenericTransportWithoutGenericRolesAndReplayCannotBypassRevocation(
        bool publishFile, bool providerFailure)
    {
        using var capturedLogs = new UploadFailureLogs();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(logCapture: capturedLogs);
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid eventId = Guid.CreateVersion7(), resourceId = Guid.CreateVersion7(), userId, version;
        await using (var db = factory.CreateDatabase())
        {
            var user = await db.Users.SingleAsync(row => row.Pii!.Email == credentials.Identifier);
            userId = user.Id;
            var actor = await db.Actors.SingleAsync(row => row.UserId == userId);
            db.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = "\"local\"", ValueType = SettingValueType.String, Category = "Security"
            });
            db.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                UserId = userId, User = user, ActorId = actor.Id,
                StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = DateTime.UtcNow
            });
            var parent = new Explore.Domain.Event
            {
                Id = eventId, Title = "Resource uploads", TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
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
                    Title = "Private draft file", Kind = EventResourceKindEnum.GeneralDocument,
                    DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
                }, EventResourceDeliveryTypeEnum.StoredFile, EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, resourceId,
                    EventResourceAudienceKindEnum.AuthenticatedTenantMember)], userId, DateTime.UtcNow);
            db.EventResources.Add(resource);
            await db.SaveChangesAsync();
            version = resource.ConcurrencyStamp;
        }
        var written = new Dictionary<string, byte[]>();
        string diagnostic = Guid.CreateVersion7().ToString("N");
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.WriteAsync(Arg.Any<FileStorageWriteInput>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var input = call.Arg<FileStorageWriteInput>()!;
            if (providerFailure)
                throw new HttpRequestException($"S3 write failed at https://private-store.example.test/{input.ObjectKey}?signature={diagnostic}");
            using var buffer = new MemoryStream();
            await input.Content.CopyToAsync(buffer, call.Arg<CancellationToken>());
            byte[] value = buffer.ToArray();
            written.Add(input.ObjectKey!, value);
            return new FileStorageWriteResult(StorageProviders.Local, input.ObjectKey!, value.Length,
                input.ContentType, Convert.ToHexString(SHA256.HashData(value)));
        });
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            byte[] value = written[call.Arg<FileStorageReadInput>()!.ObjectKey];
            return new FileStorageReadResult(new MemoryStream(value, writable: false),
                EventResourceGovernancePolicy.PdfMediaType, value.Length, null);
        });
        var resolver = Substitute.For<IFileStorageProviderResolver>();
        resolver.GetRequired(StorageProviders.Local).Returns(provider);
        using var traces = new UploadFailureTraces();
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            if (providerFailure)
                services.AddOpenTelemetry().WithTracing(tracing =>
                    tracing.AddProcessor(new SimpleActivityExportProcessor(traces)));
            services.RemoveAll<IFileStorageProviderResolver>();
            services.AddSingleton(resolver);
            services.RemoveAll<IStorageProviderBindingService>();
            services.AddScoped<IStorageProviderBindingService>(scope =>
            {
                var database = scope.GetRequiredService<Explore.Persistence.ExploreDbContext>();
                var bindings = Substitute.For<IStorageProviderBindingService>();
                bindings.CaptureAsync(StorageProviders.Local, PlatformDefaults.DefaultTenantId,
                    Arg.Any<CancellationToken>()).Returns(async call =>
                {
                    var binding = StorageProviderBinding.Local(Path.GetFullPath("resource-upload-test-storage"));
                    await database.StorageProviderBindings.AddAsync(binding, call.Arg<CancellationToken>());
                    return binding;
                });
                bindings.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(provider);
                return bindings;
            });
        }));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        using (var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials))
        {
            login.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                body.RootElement.GetProperty("token").GetString());
        }
        using (var generic = await client.PostAsJsonAsync("/api/storageobject/upload-sessions", new CreateStorageUploadSessionDto
        {
            ExpectedSizeBytes = 5, ContentType = "text/plain", OriginalFileName = "ordinary.txt", Extension = "txt",
            Purpose = StorageObjectPurposes.Attachment, Visibility = StorageObjectVisibilities.PrivateOwner,
            IdempotencyKey = Guid.CreateVersion7().ToString("N")
        }))
            await Assert.That(generic.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

        using var management = await client.GetAsync($"/api/eventresource/{resourceId}/management");
        management.EnsureSuccessStatusCode();
        using var managementJson = JsonDocument.Parse(await management.Content.ReadAsStringAsync());
        string uploadHref = managementJson.RootElement.GetProperty("_links").GetProperty("upload-file")
            .GetProperty("href").GetString()!;
        byte[] bytes = "%PDF-1.7\nresource handout\n%%EOF"u8.ToArray();
        var upload = new CreateEventResourceUploadSessionDto
        {
            ExpectedVersion = version, ExpectedSizeBytes = bytes.Length,
            ContentType = EventResourceGovernancePolicy.PdfMediaType, SafeDisplayName = "handout.pdf",
            Extension = "pdf", IdempotencyKey = Guid.CreateVersion7().ToString("N")
        };
        using var reservation = new HttpRequestMessage(HttpMethod.Post, uploadHref)
        {
            Content = JsonContent.Create(upload)
        };
        reservation.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        using var reserved = await client.SendAsync(reservation);
        await Assert.That(reserved.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(reserved.Headers.CacheControl!.NoStore).IsTrue();
        var session = (await reserved.Content.ReadFromJsonAsync<BaseCommandResponse<StorageUploadSessionDto>>())!.Id!;

        string replayKey = Guid.CreateVersion7().ToString("N");
        async Task<HttpResponseMessage> FinalizeAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Put,
                $"/api/storageobject/upload-sessions/{session.Id}/content")
            {
                Content = new ByteArrayContent(bytes)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(EventResourceGovernancePolicy.PdfMediaType);
            request.Headers.Add("Idempotency-Key", replayKey);
            return await client.SendAsync(request);
        }
        if (providerFailure)
        {
            using var unavailable = await FinalizeAsync();
            await Assert.That(unavailable.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
            await Assert.That(await unavailable.Content.ReadAsStringAsync()).DoesNotContain(diagnostic);
            await Assert.That(string.Join('\n', capturedLogs.Entries)).DoesNotContain(diagnostic);
            await traces.Observed.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(string.Join('\n', traces.Entries)).DoesNotContain(diagnostic);
            await Assert.That(string.Join('\n', traces.Entries)).DoesNotContain(session.Id.ToString("D"));
            await using var pending = factory.CreateDatabase();
            pending.EnableTenantFilterBypass("Verify opaque provider failure preserves original cleanup identity.");
            var cleanup = await pending.StorageObjectDeletionTombstones.SingleAsync(item =>
                item.TenantId == PlatformDefaults.DefaultTenantId);
            await Assert.That(cleanup.State).IsEqualTo(StorageObjectDeletionState.AwaitingProducer);
            return;
        }
        using (var first = await FinalizeAsync())
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(first.Headers.CacheControl!.NoStore).IsTrue();
        }
        using (var replay = await FinalizeAsync())
            await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(written.Count).IsEqualTo(1);
        await Assert.That(written.Values.Single()).IsEquivalentTo(bytes);

        if (publishFile)
        {
            using var before = await client.GetAsync($"/api/eventresource/{resourceId}/management");
            before.EnsureSuccessStatusCode();
            using var beforeJson = JsonDocument.Parse(await before.Content.ReadAsStringAsync());
            Guid currentVersion = beforeJson.RootElement.GetProperty("version").GetGuid();
            await Assert.That(beforeJson.RootElement.GetProperty("_links").TryGetProperty("publish", out _)).IsFalse();
            await Assert.That(beforeJson.RootElement.GetProperty("file").GetProperty("safetyState").GetString())
                .IsEqualTo("unscanned");
            using (var denied = new HttpRequestMessage(HttpMethod.Post, $"/api/eventresource/{resourceId}/publish")
            {
                Content = JsonContent.Create(new { expectedVersion = currentVersion })
            })
            {
                denied.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
                using var response = await client.SendAsync(denied);
                await Assert.That(response.IsSuccessStatusCode).IsFalse();
            }
            await using (var policy = factory.CreateDatabase())
            {
                policy.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
                    Value = "true", ValueType = SettingValueType.Boolean, Category = "EventResources"
                });
                await policy.SaveChangesAsync();
            }
            using var ready = await client.GetAsync($"/api/eventresource/{resourceId}/management");
            ready.EnsureSuccessStatusCode();
            using var readyJson = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
            string publishHref = readyJson.RootElement.GetProperty("_links").GetProperty("publish").GetProperty("href").GetString()!;
            using (var publish = new HttpRequestMessage(HttpMethod.Post, publishHref)
            {
                Content = JsonContent.Create(new { expectedVersion = currentVersion })
            })
            {
                publish.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
                using var response = await client.SendAsync(publish);
                await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            }
            using var audience = await client.GetAsync($"/api/eventresource/{resourceId}");
            audience.EnsureSuccessStatusCode();
            using var audienceJson = JsonDocument.Parse(await audience.Content.ReadAsStringAsync());
            string downloadHref = audienceJson.RootElement.GetProperty("_links").GetProperty("download").GetProperty("href").GetString()!;
            using var downloaded = await client.GetAsync(downloadHref);
            await Assert.That(downloaded.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await downloaded.Content.ReadAsByteArrayAsync()).IsEquivalentTo(bytes);
            foreach (string relation in new[] { "unpublish", "publish" })
            {
                using var state = await client.GetAsync($"/api/eventresource/{resourceId}/management");
                state.EnsureSuccessStatusCode();
                using var stateJson = JsonDocument.Parse(await state.Content.ReadAsStringAsync());
                string href = stateJson.RootElement.GetProperty("_links").GetProperty(relation).GetProperty("href").GetString()!;
                using var mutation = new HttpRequestMessage(HttpMethod.Post, href)
                {
                    Content = JsonContent.Create(new { expectedVersion = stateJson.RootElement.GetProperty("version").GetGuid() })
                };
                mutation.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
                using var changed = await client.SendAsync(mutation);
                await Assert.That(changed.StatusCode).IsEqualTo(HttpStatusCode.OK);
                using var content = await client.GetAsync(downloadHref);
                await Assert.That(content.StatusCode).IsEqualTo(relation == "unpublish" ? HttpStatusCode.NotFound : HttpStatusCode.OK);
                if (relation == "publish")
                    await Assert.That(await content.Content.ReadAsByteArrayAsync()).IsEquivalentTo(bytes);
            }
            await using var audit = factory.CreateDatabase();
            audit.EnableTenantFilterBypass("Verify the exact resource's committed republish audit.");
            await Assert.That(await audit.EventResourceAuditEntries.CountAsync(entry =>
                entry.TenantId == PlatformDefaults.DefaultTenantId && entry.EventResourceId == resourceId
                && entry.Action == EventResourceAuditAction.Republish)).IsEqualTo(1);
        }

        await using (var revoke = factory.CreateDatabase())
        {
            revoke.EnableTenantFilterBypass("Upload HTTP test revokes one exact event subject.");
            await revoke.EventRoleAssignments.Where(row => row.TenantId == PlatformDefaults.DefaultTenantId &&
                row.EventId == eventId && row.UserId == userId).ExecuteDeleteAsync();
            await revoke.TenantUsers.Where(row => row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == userId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.StatusId, (int)TenantUserStatusEnum.Suspended));
        }
        using var deniedReplay = await FinalizeAsync();
        await Assert.That(deniedReplay.IsSuccessStatusCode).IsFalse();
        await Assert.That(written.Count).IsEqualTo(1);
    }

    private sealed class UploadFailureLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(this);
        public void Dispose() { }

        private sealed class Capture(UploadFailureLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                owner.Entries.Enqueue(formatter(state, exception));
                if (exception is not null) owner.Entries.Enqueue(exception.ToString());
            }
        }
    }

    private sealed class UploadFailureTraces : BaseExporter<Activity>
    {
        private readonly TaskCompletionSource _observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> Entries { get; } = new();
        public Task Observed => _observed.Task;

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                if (!activity.Source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                    || !activity.TagObjects.Any(tag => tag.Key is "url.path" or "http.route"
                        && tag.Value?.ToString()?.Contains("/upload-sessions/", StringComparison.OrdinalIgnoreCase) == true
                        && tag.Value.ToString()!.EndsWith("/content", StringComparison.OrdinalIgnoreCase)))
                    continue;
                Entries.Enqueue(activity.DisplayName);
                foreach (var tag in activity.TagObjects)
                    Entries.Enqueue($"{tag.Key}={tag.Value}");
                foreach (var activityEvent in activity.Events)
                {
                    Entries.Enqueue(activityEvent.Name);
                    foreach (var tag in activityEvent.Tags)
                        Entries.Enqueue($"{tag.Key}={tag.Value}");
                }
                _observed.TrySetResult();
            }
            return ExportResult.Success;
        }
    }
}
