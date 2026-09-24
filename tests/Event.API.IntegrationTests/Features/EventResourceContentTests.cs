using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Models;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Settings;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed partial class EventResourceContentTests
{
    [Test]
    [Arguments("allow")]
    [Arguments("withdraw")]
    [Arguments("tighten")]
    [Arguments("tighten-audience")]
    [Arguments("tighten-filetype")]
    [Arguments("tighten-size")]
    [Arguments("expire")]
    [Arguments("expire-before-headers")]
    [Arguments("cancel-result")]
    [Arguments("owner-after-hal")]
    [Arguments("window-boundaries")]
    public async Task PreparedBytesStayPrivateUntilFinalAuthorityAndClockChecks(string change)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        var clock = new ResourceClock(DateTimeOffset.UtcNow);
        Guid eventId = Guid.CreateVersion7(), resourceId = Guid.CreateVersion7(),
            storageId = Guid.CreateVersion7(), userId;
        byte[] bytes = "%PDF-1.7\nresource test document\n%%EOF"u8.ToArray();
        string objectKey = $"tenants/{PlatformDefaults.DefaultTenantId:N}/{Guid.CreateVersion7():N}.pdf";
        string checksum = Convert.ToHexString(SHA256.HashData(bytes));
        var binding = StorageProviderBinding.Local(Path.GetFullPath("resource-content-test-storage"));
        await using (var db = factory.CreateDatabase())
        {
            var user = await db.Users.SingleAsync(row => row.Pii!.Email == credentials.Identifier);
            userId = user.Id;
            var actor = await db.Actors.SingleAsync(row => row.UserId == userId);
            db.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                UserId = userId, User = user, ActorId = actor.Id,
                StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = clock.GetUtcNow().UtcDateTime
            });
            if (change == "tighten-audience")
                db.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(), UserId = userId, User = user,
                    RoleId = (int)RoleEnum.Admin, Role = null!,
                    GrantedAt = clock.GetUtcNow().UtcDateTime, GrantedBy = userId
                });
            db.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
                Value = change == "owner-after-hal" ? "false" : "true",
                ValueType = SettingValueType.Boolean, Category = "EventResources"
            });
            if (change is "owner-after-hal" or "tighten-audience")
                db.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                    Value = "\"local\"", ValueType = SettingValueType.String, Category = "Security"
                });
            var parent = new Explore.Domain.Event
            {
                Id = eventId, Title = "Resource file delivery", TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!
            };
            parent.Publish(clock.GetUtcNow().UtcDateTime);
            db.Events.Add(parent);
            var storage = new StorageObject
            {
                Id = storageId, TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                FileTypeId = (int)FileTypeEnum.Document, FileType = null!,
                Uri = $"/api/eventresource/{resourceId}/content",
                ObjectKey = objectKey, Provider = StorageProviders.Local,
                StorageProviderBindingId = binding.Id,
                FullName = "handout.pdf", SafeDisplayName = "handout.pdf", Extension = "pdf",
                ContentType = EventResourceGovernancePolicy.PdfMediaType, Size = bytes.Length, Sha256Checksum = checksum,
                Purpose = StorageObjectPurposes.EventResource, Visibility = StorageObjectVisibilities.PrivateOwner,
                OwningResourceKind = StorageOwningResourceKinds.EventResource, OwningResourceId = resourceId,
                LifecycleState = StorageObjectLifecycleStates.Active, CreatedBy = userId
            };
            storage.RecordEventResourceInspection(storageId, checksum);
            db.AddRange(binding, storage);
            var resource = EventResource.CreateDraft(resourceId, PlatformDefaults.DefaultTenantId, eventId, null,
                new EventResourceMetadata
                {
                    Title = "Public audience, private provider",
                    PublicTitle = "Public resource handout",
                    Kind = EventResourceKindEnum.GeneralDocument,
                    DisclosureMode = EventResourceDisclosureModeEnum.Public
                }, EventResourceDeliveryTypeEnum.StoredFile,
                change == "window-boundaries"
                    ? EventResourceAvailability.Create(
                        absoluteStartUtc: clock.GetUtcNow().AddMinutes(1),
                        absoluteEndUtc: clock.GetUtcNow().AddMinutes(2))
                    : EventResourceAvailability.Create(absoluteEndUtc: clock.GetUtcNow().AddMinutes(1)),
                [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, resourceId,
                    EventResourceAudienceKindEnum.Public)], userId, clock.GetUtcNow().UtcDateTime);
            resource.SetStoredFile(storageId, resource.ConcurrencyStamp, userId, clock.GetUtcNow().UtcDateTime);
            resource.Publish(new(PlatformDefaults.DefaultTenantId, eventId, null, EventStatusEnum.Published,
                false, true, null, false, new(null, null, null, null)), true,
                resource.ConcurrencyStamp, userId, clock.GetUtcNow().UtcDateTime);
            db.EventResources.Add(resource);
            await db.SaveChangesAsync();
        }

        bool opened = false, disposed = false;
        HttpClient? administrator = null;
        var disposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await Assert.That(call.Arg<FileStorageReadInput>()!.ObjectKey).IsEqualTo(objectKey);
            opened = true;
            if (change == "expire")
                clock.Advance(TimeSpan.FromMinutes(2));
            if (change == "tighten-audience")
            {
                using var tightened = await administrator!.PutAsJsonAsync(
                    "/api/settings/instance/event-resources", new UpdateSettingBatchDto
                    {
                        Values = new Dictionary<string, string>
                        {
                            [GovernanceSettingKeys.EventResources.EnabledAudiences] = "[\"Organizer\"]"
                        }
                    });
                await Assert.That(tightened.StatusCode).IsEqualTo(HttpStatusCode.OK)
                    .Because(await tightened.Content.ReadAsStringAsync());
            }
            if (change is "withdraw" or "tighten" or "tighten-filetype" or "tighten-size")
            {
                await using var mutate = factory.CreateDatabase();
                mutate.EnableTenantFilterBypass("Resource delivery test mutates one exact tenant/resource.");
                if (change == "withdraw")
                {
                    var resource = await mutate.EventResources.SingleAsync(row =>
                        row.TenantId == PlatformDefaults.DefaultTenantId && row.Id == resourceId);
                    resource.Withdraw(resource.ConcurrencyStamp, userId, clock.GetUtcNow().UtcDateTime);
                }
                else if (change == "tighten")
                {
                    var policy = await mutate.SystemSettings.SingleAsync(row =>
                        row.SettingKey == GovernanceSettingKeys.EventResources.AllowUnscannedDocuments);
                    policy.Value = "false";
                }
                else
                    mutate.SystemSettings.Add(new SystemSetting
                    {
                        SettingKey = change == "tighten-filetype"
                            ? GovernanceSettingKeys.EventResources.PermittedFileTypes
                            : GovernanceSettingKeys.EventResources.MaxUploadBytes,
                        Value = change switch
                        {
                            "tighten-filetype" => "[\"application/vnd.openxmlformats-officedocument.wordprocessingml.document\"]",
                            _ => "1"
                        },
                        ValueType = change == "tighten-size" ? SettingValueType.Long : SettingValueType.Json,
                        Category = "EventResources"
                    });
                await mutate.SaveChangesAsync();
            }
            return new FileStorageReadResult(new ObservedRead(bytes, () =>
                {
                    disposed = true;
                    disposal.TrySetResult();
                }),
                EventResourceGovernancePolicy.PdfMediaType, bytes.Length, null);
        });
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            ConfigureStorageProviders(services, binding.Id, provider);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.Configure<MvcOptions>(options =>
                options.Filters.Add(new ResultBoundary(change, clock)));
            if (change == "owner-after-hal")
            {
                services.RemoveAll<IEventResourceAuthorizationProvider>();
                services.AddScoped<IEventResourceAuthorizationProvider>(serviceProvider =>
                    new OwnershipBoundary(
                        ActivatorUtilities.CreateInstance<Explore.Infrastructure.Services.EventResourceAuthorizationProvider>(serviceProvider),
                        async () =>
                        {
                            await using var mutate = factory.CreateDatabase();
                            mutate.EnableTenantFilterBypass("File descriptor test changes one exact storage owner without changing its stamp.");
                            await mutate.StorageObjects.Where(row => row.TenantId == PlatformDefaults.DefaultTenantId && row.Id == storageId)
                                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.OwningResourceId, Guid.CreateVersion7()));
                        }));
            }
        }));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        if (change == "tighten-audience")
        {
            administrator = client;
            using var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials);
            login.EnsureSuccessStatusCode();
            using var loginJson = System.Text.Json.JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                loginJson.RootElement.GetProperty("token").GetString());
        }
        if (change == "owner-after-hal")
        {
            using var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials);
            login.EnsureSuccessStatusCode();
            using var loginJson = System.Text.Json.JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                loginJson.RootElement.GetProperty("token").GetString());
        }
        using (var metadata = await client.GetAsync($"/api/eventresource/{resourceId}"))
        {
            if (change == "owner-after-hal")
            {
                await Assert.That(metadata.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
                await Assert.That(await metadata.Content.ReadAsStringAsync()).DoesNotContain("handout.pdf");
                await Assert.That(opened).IsFalse();
                return;
            }
            await Assert.That(metadata.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(await metadata.Content.ReadAsStringAsync());
            if (change == "allow")
            {
                string json = await metadata.Content.ReadAsStringAsync();
                using var document = System.Text.Json.JsonDocument.Parse(json);
                var file = document.RootElement.GetProperty("file");
                await Assert.That(file.GetProperty("fileName").GetString()).IsEqualTo("handout.pdf");
                await Assert.That(file.GetProperty("contentType").GetString()).IsEqualTo("application/pdf");
                await Assert.That(file.GetProperty("sizeBytes").GetInt64()).IsEqualTo((long)bytes.Length);
                await Assert.That(file.GetProperty("safetyState").GetString()).IsEqualTo("unscanned");
                await Assert.That(json).DoesNotContain(storageId.ToString());
                await Assert.That(json).DoesNotContain(objectKey);
                await Assert.That(json).DoesNotContain(checksum);
            }
        }
        if (change == "window-boundaries")
        {
            using (var before = await client.GetAsync($"/api/eventresource/{resourceId}/content"))
            {
                await Assert.That(before.IsSuccessStatusCode).IsFalse();
                await Assert.That(before.Content.Headers.ContentDisposition).IsNull();
            }
            await Assert.That(opened).IsFalse();
            clock.Advance(TimeSpan.FromMinutes(1));
            using (var start = await client.GetAsync($"/api/eventresource/{resourceId}/content"))
            {
                await Assert.That(start.StatusCode).IsEqualTo(HttpStatusCode.OK);
                await Assert.That(await start.Content.ReadAsByteArrayAsync()).IsEquivalentTo(bytes);
            }
            clock.Advance(TimeSpan.FromMinutes(1));
            using (var end = await client.GetAsync($"/api/eventresource/{resourceId}/content"))
            {
                await Assert.That(end.IsSuccessStatusCode).IsFalse();
                await Assert.That(end.Content.Headers.ContentDisposition).IsNull();
                await Assert.That((await end.Content.ReadAsStringAsync()).Contains("%PDF", StringComparison.Ordinal))
                    .IsFalse();
            }
            await provider.Received(1).OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>());
            await disposal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(disposed).IsTrue();
            await AssertNoBrowseAuditAsync();
            return;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/eventresource/{resourceId}/content");
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-4");
        using var response = await client.SendAsync(request);
        byte[] body = await response.Content.ReadAsByteArrayAsync();
        if (change == "allow")
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(body).IsEquivalentTo(bytes);
            await Assert.That(response.Content.Headers.ContentDisposition!.DispositionType).IsEqualTo("attachment");
            await Assert.That(response.Content.Headers.ContentDisposition.FileNameStar).IsEqualTo("handout.pdf");
            await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo(EventResourceGovernancePolicy.PdfMediaType);
            await Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single()).IsEqualTo("nosniff");
            await Assert.That(response.Headers.ETag).IsNull();
            await Assert.That(response.Content.Headers.LastModified).IsNull();
        }
        else
        {
            await Assert.That(response.IsSuccessStatusCode).IsFalse();
            await Assert.That(response.StatusCode == HttpStatusCode.NotModified).IsFalse();
            await Assert.That(response.Content.Headers.ContentDisposition).IsNull();
            await Assert.That(System.Text.Encoding.UTF8.GetString(body)).DoesNotContain("%PDF");
        }
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(response.Headers.CacheControl.Private).IsTrue();
        await Assert.That(opened).IsTrue();
        await disposal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(disposed).IsTrue();
        await Assert.That(System.Text.Encoding.UTF8.GetString(body)).DoesNotContain(objectKey);
        if (change == "tighten")
        {
            using var detail = await client.GetAsync($"/api/eventresource/{resourceId}");
            await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(await detail.Content.ReadAsStringAsync());
            using var projection = System.Text.Json.JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
            await Assert.That(projection.RootElement.GetProperty("file").GetProperty("safetyState").GetString())
                .IsEqualTo("unscanned");
            await Assert.That(projection.RootElement.GetProperty("_links").TryGetProperty("download", out _))
                .IsFalse();
        }
        if (change == "allow") await AssertNoBrowseAuditAsync();

        async Task AssertNoBrowseAuditAsync()
        {
            await using var audit = factory.CreateDatabase();
            audit.EnableTenantFilterBypass("Attendee file reads do not write management audit history.");
            await Assert.That(await audit.EventResourceAuditEntries.AnyAsync(row => row.EventResourceId == resourceId))
                .IsFalse();
        }
    }

    private sealed class ResultBoundary(string change, ResourceClock clock) : IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is EventResourceFileResult)
            {
                if (change == "expire-before-headers")
                    clock.Advance(TimeSpan.FromMinutes(2));
                if (change == "cancel-result")
                {
                    context.HttpContext.Response.StatusCode = 503;
                    context.Cancel = true;
                    return;
                }
            }
            await next();
        }
    }

    private sealed class OwnershipBoundary(IEventResourceAuthorizationProvider inner, Func<Task> mutate)
        : IEventResourceAuthorizationProvider
    {
        private bool _changed;
        public async Task<EventResourceProviderDecision> CheckAsync(EventResourceProviderInput input, CancellationToken token) =>
            (await CheckBatchAsync([input], token))[0];
        public async Task<IReadOnlyList<EventResourceProviderDecision>> CheckBatchAsync(
            IReadOnlyList<EventResourceProviderInput> inputs, CancellationToken token)
        {
            var decisions = await inner.CheckBatchAsync(inputs, token);
            if (!_changed && inputs.Any(input => input.Action == "view-management"))
            {
                _changed = true;
                await mutate();
            }
            return decisions;
        }
    }

    private sealed class ResourceClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private sealed class ObservedRead(byte[] bytes, Action disposed) : MemoryStream(bytes, writable: false)
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing) disposed();
            base.Dispose(disposing);
        }
    }
}
