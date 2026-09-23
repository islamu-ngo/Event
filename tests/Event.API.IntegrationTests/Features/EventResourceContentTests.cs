using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Models;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
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
    [Arguments("expire")]
    [Arguments("expire-before-headers")]
    [Arguments("cancel-result")]
    [Arguments("owner-after-hal")]
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
            db.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
                Value = change == "owner-after-hal" ? "false" : "true",
                ValueType = SettingValueType.Boolean, Category = "EventResources"
            });
            if (change == "owner-after-hal")
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
                EventResourceAvailability.Create(absoluteEndUtc: clock.GetUtcNow().AddMinutes(1)),
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
        var disposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await Assert.That(call.Arg<FileStorageReadInput>()!.ObjectKey).IsEqualTo(objectKey);
            opened = true;
            if (change == "expire")
                clock.Advance(TimeSpan.FromMinutes(2));
            if (change is "withdraw" or "tighten")
            {
                await using var mutate = factory.CreateDatabase();
                mutate.EnableTenantFilterBypass("Resource delivery test mutates one exact tenant/resource.");
                if (change == "withdraw")
                {
                    var resource = await mutate.EventResources.SingleAsync(row =>
                        row.TenantId == PlatformDefaults.DefaultTenantId && row.Id == resourceId);
                    resource.Withdraw(resource.ConcurrencyStamp, userId, clock.GetUtcNow().UtcDateTime);
                }
                else
                {
                    var policy = await mutate.SystemSettings.SingleAsync(row =>
                        row.SettingKey == GovernanceSettingKeys.EventResources.AllowUnscannedDocuments);
                    policy.Value = "false";
                }
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
            await Assert.That(metadata.StatusCode).IsEqualTo(HttpStatusCode.OK);
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
