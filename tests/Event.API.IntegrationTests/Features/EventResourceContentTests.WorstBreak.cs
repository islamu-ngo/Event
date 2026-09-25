using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.API.IntegrationTests.Features;

public sealed partial class EventResourceContentTests
{
    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task RetainedUploaderIdOldActionAndPermissivePdpCannotBypassRevocationOrTenantSwitch(
        bool switchTenant, bool revokeCoverage)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var seed = await SeedParticipantResourceAsync(factory);
        var providerBarrier = new PermissiveDownloadBarrier();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(PlatformDefaults.DefaultTenantId);
        bool opened = false, disposed = false;
        var disposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storageProvider = NSubstitute.Substitute.For<IFileStorageProvider>();
        storageProvider.Provider.Returns(StorageProviders.Local);
        storageProvider.OpenReadAsync(NSubstitute.Arg.Any<FileStorageReadInput>(), NSubstitute.Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                opened = true;
                return new FileStorageReadResult(new ObservedRead(seed.Bytes, () =>
                {
                    disposed = true;
                    disposal.TrySetResult();
                }), EventResourceGovernancePolicy.PdfMediaType, seed.Bytes.Length, null);
            });
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEventResourceAuthorizationProvider>();
            services.AddSingleton<IEventResourceAuthorizationProvider>(providerBarrier);
            ConfigureStorageProviders(services, seed.BindingId, storageProvider);
            services.RemoveAll<ITenantContext>();
            services.AddSingleton(tenant);
        }));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        await AuthenticateAsync(client, seed.Credentials);

        string retainedAction;
        using (var oldRepresentation = await client.GetAsync($"/api/eventresource/{seed.ResourceId}"))
        {
            await Assert.That(oldRepresentation.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(await oldRepresentation.Content.ReadAsStringAsync());
            string body = await oldRepresentation.Content.ReadAsStringAsync();
            await Assert.That(body).Contains(seed.ProtectedTitle);
            using var json = JsonDocument.Parse(body);
            retainedAction = json.RootElement.GetProperty("_links").GetProperty("download")
                .GetProperty("href").GetString()!;
        }

        providerBarrier.Arm();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<HttpResponseMessage> pending = client.GetAsync(retainedAction, deadline.Token);
        try
        {
            await providerBarrier.ProviderAllowed.WaitAsync(TimeSpan.FromSeconds(10), deadline.Token);
            if (revokeCoverage)
            {
                await using var revoke = factory.CreateDatabase();
                revoke.EnableTenantFilterBypass("Revoke the exact participant's session coverage during resource preparation.");
                await Assert.That(await revoke.EventRegistrations.Where(row =>
                    row.TenantId == PlatformDefaults.DefaultTenantId && row.RegistrationOrderId == seed.OrderId)
                    .ExecuteDeleteAsync(deadline.Token)).IsEqualTo(1);
            }
            if (switchTenant) tenant.TenantId.Returns(seed.OtherTenantId);
        }
        finally
        {
            providerBarrier.Release();
        }

        using HttpResponseMessage response = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        byte[] bodyBytes = await response.Content.ReadAsByteArrayAsync();
        string diagnosticSurface = string.Join('\n', response.Headers.SelectMany(header => header.Value)
            .Concat(response.Content.Headers.SelectMany(header => header.Value))) + '\n'
            + System.Text.Encoding.UTF8.GetString(bodyBytes);
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.NotModified);
        await Assert.That(response.Headers.Location).IsNull();
        await Assert.That(response.Content.Headers.ContentDisposition).IsNull();
        await Assert.That(diagnosticSurface).DoesNotContain("%PDF");
        await Assert.That(diagnosticSurface).DoesNotContain(seed.StorageId.ToString("D"));
        await Assert.That(diagnosticSurface).DoesNotContain(seed.ObjectKey);
        await Assert.That(diagnosticSurface).DoesNotContain(seed.ProtectedTitle);
        if (switchTenant)
            await Assert.That(opened).IsFalse();
        else
        {
            await Assert.That(opened).IsTrue()
                .Because("independent revocation must reach private preparation before the fresh B read denies");
            await disposal.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(disposed).IsTrue();
        }
        foreach (string suffix in new[] { "content", "presigned-url", "public" })
        {
            using var retained = await client.GetAsync($"/api/storageobject/{seed.StorageId}/{suffix}", deadline.Token);
            await Assert.That(retained.IsSuccessStatusCode).IsFalse();
            await Assert.That(retained.Content.Headers.ContentDisposition).IsNull();
            string body = await retained.Content.ReadAsStringAsync(deadline.Token);
            await Assert.That(body).DoesNotContain("%PDF");
            await Assert.That(body).DoesNotContain(seed.ObjectKey);
            await Assert.That(body).DoesNotContain(seed.ProtectedTitle);
        }
    }

    [Test]
    public async Task PermissivePdpCannotGrantARevokedParticipantWithoutOpeningStorage()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var seed = await SeedParticipantResourceAsync(factory);
        await using (var revoke = factory.CreateDatabase())
        {
            revoke.EnableTenantFilterBypass("Revoke the exact participant's session coverage before resource preparation.");
            await Assert.That(await revoke.EventRegistrations.Where(row =>
                row.TenantId == PlatformDefaults.DefaultTenantId && row.RegistrationOrderId == seed.OrderId)
                .ExecuteDeleteAsync()).IsEqualTo(1);
        }
        bool opened = false;
        var storageProvider = NSubstitute.Substitute.For<IFileStorageProvider>();
        storageProvider.Provider.Returns(StorageProviders.Local);
        storageProvider.OpenReadAsync(NSubstitute.Arg.Any<FileStorageReadInput>(), NSubstitute.Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                opened = true;
                return new FileStorageReadResult(new MemoryStream(seed.Bytes),
                    EventResourceGovernancePolicy.PdfMediaType, seed.Bytes.Length, null);
            });
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEventResourceAuthorizationProvider>();
            services.AddSingleton<IEventResourceAuthorizationProvider>(new AlwaysAllowProvider());
            ConfigureStorageProviders(services, seed.BindingId, storageProvider);
        }));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        await AuthenticateAsync(client, seed.Credentials);
        using var response = await client.GetAsync($"/api/eventresource/{seed.ResourceId}/content");
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await Assert.That(body).DoesNotContain(seed.ProtectedTitle);
        await Assert.That(body).DoesNotContain(seed.StorageId.ToString("D"));
        await Assert.That(body).DoesNotContain(seed.ObjectKey);
        await Assert.That(opened).IsFalse();
    }

    private static async Task<ParticipantResourceSeed> SeedParticipantResourceAsync(
        LocalAdmissionWebApplicationFactory factory)
    {
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid eventId = Guid.CreateVersion7(), sessionId = Guid.CreateVersion7(), resourceId = Guid.CreateVersion7();
        Guid storageId = Guid.CreateVersion7(), otherTenantId = Guid.CreateVersion7(), orderId;
        byte[] bytes = "%PDF-1.7\nworst break bytes\n%%EOF"u8.ToArray();
        string objectKey = $"private/{Guid.CreateVersion7():N}/retained.pdf";
        string title = $"protected-participant-title-{Guid.CreateVersion7():N}";
        await using var db = factory.CreateDatabase();
        var user = await db.Users.SingleAsync(row => row.Pii!.Email == credentials.Identifier);
        var actor = await db.Actors.SingleAsync(row => row.UserId == user.Id);
        db.Tenants.Add(new Tenant
        {
            Id = otherTenantId, FullName = "Other resource tenant", Slug = $"other-{otherTenantId:N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!, CreatedAt = DateTime.UtcNow
        });
        db.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
            UserId = user.Id, User = user, ActorId = actor.Id,
            StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = DateTime.UtcNow
        });
        db.SystemSettings.AddRange(
            new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
                Value = "true", ValueType = SettingValueType.Boolean, Category = "EventResources"
            },
            new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                Value = "\"local\"", ValueType = SettingValueType.String, Category = "Security"
            });
        var parent = new Explore.Domain.Event
        {
            Id = eventId, Title = "Participant event", TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
            ActorId = actor.Id, Actor = actor, OrganizerActorId = actor.Id,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!
        };
        parent.Publish(DateTime.UtcNow);
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = sessionId, EventId = eventId, Event = parent, TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!, Title = "Participant session", StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow.AddHours(1), CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        parent.Sessions.Add(session);
        var catalog = EventTicketCatalogVersion.Create(PlatformDefaults.DefaultTenantId, eventId, "USD", 1);
        var order = RegistrationOrder.Create(PlatformDefaults.DefaultTenantId, eventId, user.Id, actor.Id,
            BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null),
            null, null, "USD", DateTime.UtcNow, null);
        orderId = order.Id;
        var participant = RegistrationParticipant.Create(PlatformDefaults.DefaultTenantId, order.Id,
            user.Id, ParticipantTypeEnum.Adult, null);
        order.AddParticipant(participant);
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("USD", 0, 0, 0, 0));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, DateTime.UtcNow);
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, DateTime.UtcNow);
        order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, DateTime.UtcNow);
        var binding = StorageProviderBinding.Local(Path.GetFullPath("resource-content-test-storage"));
        var storage = new StorageObject
        {
            Id = storageId, TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Document, FileType = null!,
            Uri = $"/api/eventresource/{resourceId}/content", ObjectKey = objectKey,
            Provider = StorageProviders.Local, FullName = "retained.pdf", SafeDisplayName = "retained.pdf",
            StorageProviderBindingId = binding.Id,
            Extension = "pdf", ContentType = EventResourceGovernancePolicy.PdfMediaType,
            Size = bytes.Length, Sha256Checksum = Convert.ToHexString(SHA256.HashData(bytes)),
            Purpose = StorageObjectPurposes.EventResource, Visibility = StorageObjectVisibilities.PrivateOwner,
            OwningResourceKind = StorageOwningResourceKinds.EventResource, OwningResourceId = resourceId,
            LifecycleState = StorageObjectLifecycleStates.Active, CreatedBy = user.Id
        };
        storage.RecordEventResourceInspection(storageId, storage.Sha256Checksum);
        var resource = EventResource.CreateDraft(resourceId, PlatformDefaults.DefaultTenantId, eventId, null,
            new EventResourceMetadata
            {
                Title = title, Kind = EventResourceKindEnum.GeneralDocument,
                DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
            }, EventResourceDeliveryTypeEnum.StoredFile, EventResourceAvailability.Create(),
            [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, resourceId,
                EventResourceAudienceKindEnum.SessionRegistrant, sessionId: sessionId)], user.Id, DateTime.UtcNow);
        resource.SetStoredFile(storageId, resource.ConcurrencyStamp, user.Id, DateTime.UtcNow);
        resource.Publish(new(PlatformDefaults.DefaultTenantId, eventId, null, EventStatusEnum.Published,
            false, true, null, false, new(session.StartTime, session.EndTime, null, null)), true,
            resource.ConcurrencyStamp, user.Id, DateTime.UtcNow);
        db.AddRange(parent, catalog, order, binding, storage, resource, new EventRegistration
        {
            Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
            EventId = eventId, Event = parent, EventSessionId = sessionId, EventSession = session,
            LinkedUserId = user.Id, RegistrationOrderId = order.Id,
            RegistrationParticipantId = participant.Id, RegistrationParticipant = participant,
            CoverageEstablishedAt = DateTime.UtcNow, ConcurrencyStamp = Guid.CreateVersion7()
        });
        await db.SaveChangesAsync();
        return new(credentials, resourceId, storageId, binding.Id, orderId, otherTenantId, objectKey, title, bytes);
    }

    private static async Task AuthenticateAsync(HttpClient client,
        Explore.Application.Features.Authentication.Local.Models.LocalAuthRequestDto credentials)
    {
        using var login = await client.PostAsJsonAsync("/api/auth/local/login", credentials);
        login.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            body.RootElement.GetProperty("token").GetString());
    }

    private sealed class PermissiveDownloadBarrier : IEventResourceAuthorizationProvider
    {
        private readonly TaskCompletionSource _allowed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _armed;
        public Task ProviderAllowed => _allowed.Task;
        public void Arm() => Interlocked.Exchange(ref _armed, 1);
        public void Release() => _release.TrySetResult();
        public async Task<EventResourceProviderDecision> CheckAsync(EventResourceProviderInput input,
            CancellationToken cancellationToken)
        {
            if (input.Action != "download" || Volatile.Read(ref _armed) == 0)
                return EventResourceProviderDecision.Allow;
            _allowed.TrySetResult();
            await _release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return EventResourceProviderDecision.Allow;
        }
        public async Task<IReadOnlyList<EventResourceProviderDecision>> CheckBatchAsync(
            IReadOnlyList<EventResourceProviderInput> inputs, CancellationToken cancellationToken)
        {
            var results = new EventResourceProviderDecision[inputs.Count];
            for (int index = 0; index < inputs.Count; index++)
                results[index] = await CheckAsync(inputs[index], cancellationToken);
            return results;
        }
    }

    private sealed class AlwaysAllowProvider : IEventResourceAuthorizationProvider
    {
        public Task<EventResourceProviderDecision> CheckAsync(EventResourceProviderInput input,
            CancellationToken cancellationToken) => Task.FromResult(EventResourceProviderDecision.Allow);
        public Task<IReadOnlyList<EventResourceProviderDecision>> CheckBatchAsync(
            IReadOnlyList<EventResourceProviderInput> inputs, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EventResourceProviderDecision>>(
                Enumerable.Repeat(EventResourceProviderDecision.Allow, inputs.Count).ToArray());
    }

    private static void ConfigureStorageProviders(IServiceCollection services, Guid bindingId, IFileStorageProvider provider)
    {
        var resolver = Substitute.For<IFileStorageProviderResolver>();
        resolver.GetRequired(StorageProviders.Local).Returns(provider);
        var bindings = Substitute.For<IStorageProviderBindingService>();
        bindings.ResolveAsync(bindingId, Arg.Any<CancellationToken>()).Returns(provider);
        services.RemoveAll<IFileStorageProviderResolver>();
        services.AddSingleton(resolver);
        services.RemoveAll<IStorageProviderBindingService>();
        services.AddSingleton(bindings);
    }

    private sealed record ParticipantResourceSeed(
        Explore.Application.Features.Authentication.Local.Models.LocalAuthRequestDto Credentials,
        Guid ResourceId, Guid StorageId, Guid BindingId, Guid OrderId, Guid OtherTenantId, string ObjectKey, string ProtectedTitle, byte[] Bytes);
}
