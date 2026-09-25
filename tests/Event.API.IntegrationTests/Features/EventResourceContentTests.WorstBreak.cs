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
using Explore.Domain.Services.Scheduling;
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
    public async Task ConfirmedPurchaserCannotUseAnotherParticipantsSessionResource()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var seed = await SeedParticipantResourceAsync(factory, separatePurchaser: true);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => new FileStorageReadResult(new MemoryStream(seed.Bytes),
                EventResourceGovernancePolicy.PdfMediaType, seed.Bytes.Length, null));
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            ConfigureStorageProviders(services, seed.BindingId, provider)));
        using var participant = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await AuthenticateAsync(participant, seed.Credentials);
        using (var allowed = await participant.GetAsync($"/api/eventresource/{seed.ResourceId}/content"))
        {
            await Assert.That(allowed.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(await allowed.Content.ReadAsStringAsync());
            await Assert.That(await allowed.Content.ReadAsByteArrayAsync()).IsEquivalentTo(seed.Bytes);
        }

        using var purchaser = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await AuthenticateAsync(purchaser, seed.PurchaserCredentials!);
        using var denied = await purchaser.GetAsync($"/api/eventresource/{seed.ResourceId}/content");
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(denied.Content.Headers.ContentDisposition).IsNull();
        string body = await denied.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain(seed.ProtectedTitle);
        await Assert.That(body).DoesNotContain("%PDF");
        Guid participantUserId;
        await using (var db = factory.CreateDatabase())
            participantUserId = await db.Users.Where(user => user.Pii!.Email == seed.Credentials.Identifier)
                .Select(user => user.Id).SingleAsync();
        foreach (var (header, claimedValue) in new (string Header, string Value)[]
                 {
                     ("X-Tenant-Id", PlatformDefaults.DefaultTenantId.ToString("D")),
                     ("X-Subject-Id", participantUserId.ToString("D")),
                     ("X-Guest-Id", Guid.CreateVersion7().ToString("D")),
                     ("X-Dependent-Id", participantUserId.ToString("D")),
                     ("X-Role-Claim", "event-owner")
                 })
        {
            using var forged = new HttpRequestMessage(HttpMethod.Get, $"/api/eventresource/{seed.ResourceId}/content");
            forged.Headers.TryAddWithoutValidation(header, claimedValue);
            using var rejected = await purchaser.SendAsync(forged);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(rejected.Content.Headers.ContentDisposition).IsNull();
            string rejectedBody = await rejected.Content.ReadAsStringAsync();
            await Assert.That(rejectedBody).DoesNotContain(seed.ProtectedTitle);
            await Assert.That(rejectedBody).DoesNotContain("%PDF");
        }
        await using (var db = factory.CreateDatabase())
        {
            var purchaserUser = await db.Users.SingleAsync(user =>
                user.Pii!.Email == seed.PurchaserCredentials!.Identifier);
            db.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(), UserId = purchaserUser.Id, User = purchaserUser,
                RoleId = (int)RoleEnum.Admin, Role = null!,
                GrantedAt = DateTime.UtcNow, GrantedBy = purchaserUser.Id
            });
            await db.SaveChangesAsync();
        }
        await AuthenticateAsync(purchaser, seed.PurchaserCredentials!);
        using (var broadRole = await purchaser.GetAsync($"/api/eventresource/{seed.ResourceId}/content"))
        {
            await Assert.That(broadRole.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(broadRole.Content.Headers.ContentDisposition).IsNull();
            await Assert.That(await broadRole.Content.ReadAsStringAsync()).DoesNotContain(seed.ProtectedTitle);
        }
        await provider.Received(1).OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TransferredRegistrationStopsServingThePreviousParticipant()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var seed = await SeedParticipantResourceAsync(factory, separatePurchaser: true);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => new FileStorageReadResult(new MemoryStream(seed.Bytes),
                EventResourceGovernancePolicy.PdfMediaType, seed.Bytes.Length, null));
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            ConfigureStorageProviders(services, seed.BindingId, provider)));
        using var previous = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await AuthenticateAsync(previous, seed.Credentials);
        using (var before = await previous.GetAsync($"/api/eventresource/{seed.ResourceId}/content"))
        {
            await Assert.That(before.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await before.Content.ReadAsByteArrayAsync()).IsEquivalentTo(seed.Bytes);
        }
        await using (var writer = factory.CreateDatabase())
        {
            writer.EnableTenantFilterBypass("Commit a transferred registration and its participant subject.");
            Guid successor = await writer.Users.Where(user => user.Pii!.Email == seed.PurchaserCredentials!.Identifier)
                .Select(user => user.Id).SingleAsync();
            await writer.RegistrationParticipants.Where(row => row.RegistrationOrderId == seed.OrderId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LinkedUserId, successor));
            await writer.EventRegistrations.Where(row => row.EventId == seed.EventId &&
                row.EventSessionId == seed.SessionId && row.RegistrationOrderId == seed.OrderId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LinkedUserId, successor));
        }
        using (var denied = await previous.GetAsync($"/api/eventresource/{seed.ResourceId}/content"))
        {
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(denied.Content.Headers.ContentDisposition).IsNull();
            await Assert.That(await denied.Content.ReadAsStringAsync()).DoesNotContain(seed.ProtectedTitle);
        }
        using var successorClient = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await AuthenticateAsync(successorClient, seed.PurchaserCredentials!);
        using var after = await successorClient.GetAsync($"/api/eventresource/{seed.ResourceId}/content");
        await Assert.That(after.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await after.Content.ReadAsByteArrayAsync()).IsEquivalentTo(seed.Bytes);
    }

    [Test]
    public async Task RemovedSpeakerCannotReuseSessionResourceDownload()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var seed = await SeedParticipantResourceAsync(factory, speakerAudience: true);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => new FileStorageReadResult(new MemoryStream(seed.Bytes),
                EventResourceGovernancePolicy.PdfMediaType, seed.Bytes.Length, null));
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            ConfigureStorageProviders(services, seed.BindingId, provider)));
        using var speaker = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await AuthenticateAsync(speaker, seed.Credentials);
        using (var before = await speaker.GetAsync($"/api/eventresource/{seed.ResourceId}/content"))
        {
            await Assert.That(before.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await before.Content.ReadAsByteArrayAsync()).IsEquivalentTo(seed.Bytes);
        }
        await using (var writer = factory.CreateDatabase())
        {
            writer.EnableTenantFilterBypass("Commit removal of the session speaker's exact assignment.");
            await writer.EventSessionSpeakers.Where(row => row.EventSessionId == seed.SessionId)
                .ExecuteDeleteAsync();
        }
        using var denied = await speaker.GetAsync($"/api/eventresource/{seed.ResourceId}/content");
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(denied.Content.Headers.ContentDisposition).IsNull();
        await Assert.That(await denied.Content.ReadAsStringAsync()).DoesNotContain(seed.ProtectedTitle);
    }

    [Test]
    public async Task ApprovalAndCompletionReversalsImmediatelyDenyNewParticipantDownloads()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var seed = await SeedParticipantResourceAsync(factory, approvalGated: true);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => new FileStorageReadResult(new MemoryStream(seed.Bytes),
                EventResourceGovernancePolicy.PdfMediaType, seed.Bytes.Length, null));
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            ConfigureStorageProviders(services, seed.BindingId, provider)));
        using var participant = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await AuthenticateAsync(participant, seed.Credentials);
        using (var approved = await participant.GetAsync($"/api/eventresource/{seed.ResourceId}/content"))
        {
            await Assert.That(approved.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await approved.Content.ReadAsByteArrayAsync()).IsEquivalentTo(seed.Bytes);
        }
        Guid approverActorId;
        await using (var writer = factory.CreateDatabase())
        {
            writer.EnableTenantFilterBypass("Withdraw the participant's exact admission approval.");
            approverActorId = (await writer.ParticipantAdmissionEligibilities.SingleAsync(value =>
                value.RegistrationOrderId == seed.OrderId)).ApprovedByActorId
                ?? throw new InvalidOperationException("The initial admission must have an approver.");
            await writer.ParticipantAdmissionEligibilities.Where(value => value.RegistrationOrderId == seed.OrderId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ApprovedAt, (DateTime?)null)
                    .SetProperty(value => value.ApprovedByActorId, (Guid?)null));
        }
        using (var denied = await participant.GetAsync($"/api/eventresource/{seed.ResourceId}/content"))
        {
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(denied.Content.Headers.ContentDisposition).IsNull();
        }
        await using (var writer = factory.CreateDatabase())
        {
            writer.EnableTenantFilterBypass("Restore approval but reverse the participant's completion.");
            await writer.ParticipantAdmissionEligibilities.Where(value => value.RegistrationOrderId == seed.OrderId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ApprovedAt, (DateTime?)DateTime.UtcNow)
                    .SetProperty(value => value.ApprovedByActorId, (Guid?)approverActorId)
                    .SetProperty(value => value.RequirementsCompletedAt, (DateTime?)null));
        }
        using var completionDenied = await participant.GetAsync($"/api/eventresource/{seed.ResourceId}/content");
        await Assert.That(completionDenied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(completionDenied.Content.Headers.ContentDisposition).IsNull();
        await Assert.That(await completionDenied.Content.ReadAsStringAsync()).DoesNotContain(seed.ProtectedTitle);
    }

    [Test]
    public async Task ApprovalAndCompletionOnDifferentParticipantsCannotCombineAcrossAuthenticatedPeople()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var seed = await SeedParticipantResourceAsync(factory, crossPersonFacts: true);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => new FileStorageReadResult(new MemoryStream(seed.Bytes),
                EventResourceGovernancePolicy.PdfMediaType, seed.Bytes.Length, null));
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            ConfigureStorageProviders(services, seed.BindingId, provider)));
        using var completed = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var approved = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await AuthenticateAsync(completed, seed.Credentials);
        await AuthenticateAsync(approved, seed.PurchaserCredentials!);
        foreach (var person in new[] { completed, approved })
        {
            using var hidden = await person.GetAsync($"/api/eventresource/{seed.ResourceId}");
            await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(await hidden.Content.ReadAsStringAsync()).DoesNotContain(seed.ProtectedTitle);
            using var denied = await person.GetAsync($"/api/eventresource/{seed.ResourceId}/content");
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            await Assert.That(denied.Content.Headers.ContentDisposition).IsNull();
        }
        await using (var writer = factory.CreateDatabase())
        {
            writer.EnableTenantFilterBypass("Join approval and completion only for the first assigned participant.");
            var completedUserId = await writer.Users.Where(value => value.Pii!.Email == seed.Credentials.Identifier)
                .Select(value => value.Id).SingleAsync();
            var participantId = await writer.EventRegistrations.Where(value =>
                    value.RegistrationOrderId == seed.OrderId && value.LinkedUserId == completedUserId)
                .Select(value => value.RegistrationParticipantId).SingleAsync();
            var eligibility = await writer.ParticipantAdmissionEligibilities.SingleAsync(value =>
                value.ParticipantId == participantId);
            var actorId = await writer.Actors.Where(value => value.UserId == completedUserId)
                .Select(value => value.Id).SingleAsync();
            eligibility.Approve(actorId, DateTime.UtcNow, Guid.CreateVersion7());
            await writer.SaveChangesAsync();
        }
        using (var joined = await completed.GetAsync($"/api/eventresource/{seed.ResourceId}/content"))
        {
            await Assert.That(joined.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(await joined.Content.ReadAsStringAsync());
            await Assert.That(await joined.Content.ReadAsByteArrayAsync()).IsEquivalentTo(seed.Bytes);
        }
        using var otherStillDenied = await approved.GetAsync($"/api/eventresource/{seed.ResourceId}/content");
        await Assert.That(otherStillDenied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(otherStillDenied.Content.Headers.ContentDisposition).IsNull();
    }

    [Test]
    public async Task ReversedCheckInImmediatelyDeniesNewParticipantDownload()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var seed = await SeedParticipantResourceAsync(factory, checkedInAudience: true);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => new FileStorageReadResult(new MemoryStream(seed.Bytes),
                EventResourceGovernancePolicy.PdfMediaType, seed.Bytes.Length, null));
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            ConfigureStorageProviders(services, seed.BindingId, provider)));
        using var participant = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await AuthenticateAsync(participant, seed.Credentials);
        using (var checkedIn = await participant.GetAsync($"/api/eventresource/{seed.ResourceId}/content"))
        {
            await Assert.That(checkedIn.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(await checkedIn.Content.ReadAsStringAsync());
            await Assert.That(await checkedIn.Content.ReadAsByteArrayAsync()).IsEquivalentTo(seed.Bytes);
        }
        await using (var writer = factory.CreateDatabase())
        {
            writer.EnableTenantFilterBypass("Reverse the active check-in for this resource's exact event target.");
            await writer.AdmissionCheckInStates.Where(value => value.AdmissionTargetId == seed.AdmissionTargetId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ActiveCheckInEventId, (Guid?)null));
        }
        using var denied = await participant.GetAsync($"/api/eventresource/{seed.ResourceId}/content");
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(denied.Content.Headers.ContentDisposition).IsNull();
        string body = await denied.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain(seed.ProtectedTitle);
        await Assert.That(body).DoesNotContain("%PDF");
    }

    [Test]
    public async Task CompletedSessionRecordingUsesTheLatestScheduleBeforeDeliveringBytes()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var clock = new ResourceClock(DateTimeOffset.UtcNow);
        var seed = await SeedParticipantResourceAsync(factory, completedRecording: true,
            sessionStart: clock.GetUtcNow());
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>())
            .Returns(new FileStorageReadResult(new MemoryStream(seed.Bytes),
                EventResourceGovernancePolicy.PdfMediaType, seed.Bytes.Length, null));
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            ConfigureStorageProviders(services, seed.BindingId, provider);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await AuthenticateAsync(client, seed.Credentials);
        using (var before = await client.GetAsync($"/api/eventresource/{seed.ResourceId}/content"))
            await Assert.That(before.IsSuccessStatusCode).IsFalse();

        await using (var reschedule = factory.CreateDatabase())
        {
            reschedule.EnableTenantFilterBypass("Reschedule the linked session after resource publication.");
            var session = await reschedule.EventSessions.SingleAsync(row => row.Id == seed.SessionId);
            session.Reschedule(UtcInstantRange.Create(clock.GetUtcNow().AddHours(3),
                clock.GetUtcNow().AddHours(4)), "UTC", new EventScheduleProjectionCalculator());
            await reschedule.SaveChangesAsync();
        }
        clock.Advance(TimeSpan.FromHours(2));
        using (var stale = await client.GetAsync($"/api/eventresource/{seed.ResourceId}/content"))
        {
            await Assert.That(stale.IsSuccessStatusCode).IsFalse();
            await Assert.That((await stale.Content.ReadAsStringAsync()).Contains("%PDF", StringComparison.Ordinal)).IsFalse();
        }
        clock.Advance(TimeSpan.FromHours(3));
        await using (var complete = factory.CreateDatabase())
        {
            complete.EnableTenantFilterBypass("Complete the linked session after its rescheduled end.");
            var session = await complete.EventSessions.SingleAsync(row => row.Id == seed.SessionId);
            await Assert.That(session.Complete(EventStatusEnum.Published, clock.GetUtcNow().UtcDateTime)).IsTrue();
            await complete.SaveChangesAsync();
        }
        using var released = await client.GetAsync($"/api/eventresource/{seed.ResourceId}/content");
        await Assert.That(released.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because(await released.Content.ReadAsStringAsync());
        await Assert.That(await released.Content.ReadAsByteArrayAsync()).IsEquivalentTo(seed.Bytes);
        await provider.Received(1).OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CommittedParentCancellationBeforeFinalFileReadReleasesPreparedBytes()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var seed = await SeedParticipantResourceAsync(factory);
        bool opened = false, disposed = false;
        var disposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                opened = true;
                await using var db = factory.CreateDatabase();
                db.EnableTenantFilterBypass("Cancel the committed parent after private file preparation begins, before B.");
                var parent = await db.Events.SingleAsync(row => row.Id == seed.EventId);
                await Assert.That(parent.Cancel(DateTime.UtcNow)).IsTrue();
                await db.SaveChangesAsync(call.Arg<CancellationToken>());
                return new FileStorageReadResult(new ObservedRead(seed.Bytes, () =>
                {
                    disposed = true;
                    disposal.TrySetResult();
                }), EventResourceGovernancePolicy.PdfMediaType, seed.Bytes.Length, null);
            });
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            ConfigureStorageProviders(services, seed.BindingId, provider)));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await AuthenticateAsync(client, seed.Credentials);
        using var response = await client.GetAsync($"/api/eventresource/{seed.ResourceId}/content");
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(opened).IsTrue();
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await Assert.That(response.Content.Headers.ContentDisposition).IsNull();
        await Assert.That(body).DoesNotContain("%PDF");
        await Assert.That(body).DoesNotContain(seed.ProtectedTitle);
        await disposal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(disposed).IsTrue();
    }

    [Test]
    public async Task StaffAssignmentExpiringDuringPrivateFilePreparationDeniesFinalRead()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var clock = new ResourceClock(DateTimeOffset.UtcNow);
        var seed = await SeedParticipantResourceAsync(factory, staffExpiry: clock.GetUtcNow().AddSeconds(5));
        bool opened = false, disposed = false;
        var disposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                opened = true;
                clock.Advance(TimeSpan.FromSeconds(10));
                return new FileStorageReadResult(new ObservedRead(seed.Bytes, () =>
                {
                    disposed = true;
                    disposal.TrySetResult();
                }), EventResourceGovernancePolicy.PdfMediaType, seed.Bytes.Length, null);
            });
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            ConfigureStorageProviders(services, seed.BindingId, provider);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await AuthenticateAsync(client, seed.Credentials);
        using (var metadata = await client.GetAsync($"/api/eventresource/{seed.ResourceId}"))
        {
            await Assert.That(metadata.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(await metadata.Content.ReadAsStringAsync());
            using var document = JsonDocument.Parse(await metadata.Content.ReadAsStringAsync());
            await Assert.That(document.RootElement.GetProperty("_links").TryGetProperty("download", out _)).IsTrue()
                .Because("the authenticated staff member must be allowed to download before the grant expires");
        }
        using var response = await client.GetAsync($"/api/eventresource/{seed.ResourceId}/content");
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(opened).IsTrue().Because("the assignment must be effective at A");
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await Assert.That(response.Content.Headers.ContentDisposition).IsNull();
        await Assert.That(body).DoesNotContain("%PDF");
        await Assert.That(body).DoesNotContain(seed.ProtectedTitle);
        await disposal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(disposed).IsTrue();
    }

    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task RetainedUploaderIdOldActionAndPermissivePdpCannotBypassRevocationOrTenantSwitch(
        bool switchTenant, bool revokeCoverage)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var seed = await SeedParticipantResourceAsync(factory);
        var providerBarrier = new PermissiveAccessBarrier();
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
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task RetainedUploaderIdOldExternalActionAndPermissivePdpCannotBypassRevocationOrTenantSwitch(
        bool switchTenant, bool revokeCoverage)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        var seed = await SeedParticipantResourceAsync(factory, externalDestination: true);
        var providerBarrier = new PermissiveAccessBarrier();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(PlatformDefaults.DefaultTenantId);
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEventResourceAuthorizationProvider>();
            services.AddSingleton<IEventResourceAuthorizationProvider>(providerBarrier);
            services.RemoveAll<ITenantContext>();
            services.AddSingleton(tenant);
        }));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await AuthenticateAsync(client, seed.Credentials);

        string retainedAction;
        using (var representation = await client.GetAsync($"/api/eventresource/{seed.ResourceId}"))
        {
            await Assert.That(representation.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because(await representation.Content.ReadAsStringAsync());
            using var json = JsonDocument.Parse(await representation.Content.ReadAsStringAsync());
            retainedAction = json.RootElement.GetProperty("_links").GetProperty("access").GetProperty("href").GetString()!;
            await Assert.That(json.RootElement.GetProperty("title").GetString()).IsEqualTo(seed.ProtectedTitle);
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
                revoke.EnableTenantFilterBypass("Revoke the participant during the protected destination decision.");
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
        string body = await response.Content.ReadAsStringAsync(deadline.Token);
        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await Assert.That(response.Headers.Location).IsNull();
        await Assert.That(response.Content.Headers.ContentDisposition).IsNull();
        await Assert.That(body).DoesNotContain(seed.ProtectedTitle);
        await Assert.That(body).DoesNotContain(seed.Destination!);
        await Assert.That(body).DoesNotContain(seed.ObjectKey);
        foreach (string suffix in new[] { "content", "presigned-url", "public" })
        {
            using var retained = await client.GetAsync($"/api/storageobject/{seed.StorageId}/{suffix}", deadline.Token);
            await Assert.That(retained.IsSuccessStatusCode).IsFalse();
            await Assert.That(retained.Headers.Location).IsNull();
            string retainedBody = await retained.Content.ReadAsStringAsync(deadline.Token);
            await Assert.That(retainedBody).DoesNotContain(seed.Destination!);
            await Assert.That(retainedBody).DoesNotContain(seed.ObjectKey);
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
        LocalAdmissionWebApplicationFactory factory, DateTimeOffset? staffExpiry = null,
        bool externalDestination = false, bool completedRecording = false, DateTimeOffset? sessionStart = null,
        bool separatePurchaser = false, bool speakerAudience = false, bool approvalGated = false,
        bool checkedInAudience = false, bool crossPersonFacts = false)
    {
        var credentials = await factory.SeedLocalUserAsync(emailConfirmed: true);
        var purchaserCredentials = separatePurchaser || crossPersonFacts
            ? await factory.SeedLocalUserAsync(emailConfirmed: true) : null;
        Guid eventId = Guid.CreateVersion7(), sessionId = Guid.CreateVersion7(), resourceId = Guid.CreateVersion7();
        Guid storageId = Guid.CreateVersion7(), otherTenantId = Guid.CreateVersion7(), orderId;
        byte[] bytes = "%PDF-1.7\nworst break bytes\n%%EOF"u8.ToArray();
        string objectKey = $"private/{Guid.CreateVersion7():N}/retained.pdf";
        string title = $"protected-participant-title-{Guid.CreateVersion7():N}";
        string? destination = externalDestination ? $"https://resource.example.org/visit/{Guid.CreateVersion7():N}" : null;
        await using var db = factory.CreateDatabase();
        var user = await db.Users.SingleAsync(row => row.Pii!.Email == credentials.Identifier);
        var actor = await db.Actors.SingleAsync(row => row.UserId == user.Id);
        var purchaserUser = purchaserCredentials is null ? user
            : await db.Users.SingleAsync(row => row.Pii!.Email == purchaserCredentials.Identifier);
        var purchaserActor = purchaserCredentials is null ? actor
            : await db.Actors.SingleAsync(row => row.UserId == purchaserUser.Id);
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
        if (purchaserCredentials is not null)
            db.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                UserId = purchaserUser.Id, User = purchaserUser, ActorId = purchaserActor.Id,
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
        if (externalDestination)
            db.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.EventResources.ExternalOrigins,
                Value = "[\"https://resource.example.org\"]", ValueType = SettingValueType.Json,
                Category = "EventResources"
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
        DateTimeOffset start = sessionStart ?? DateTimeOffset.UtcNow;
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = sessionId, EventId = eventId, Event = parent, TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!, Title = "Participant session", StartTime = start,
            EndTime = start.AddHours(1), CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        parent.Sessions.Add(session);
        var catalog = EventTicketCatalogVersion.Create(PlatformDefaults.DefaultTenantId, eventId, "USD", 1);
        EventTicketType? approvalType = null;
        TicketTypeEntitlement? entitlement = null;
        if (approvalGated || checkedInAudience || crossPersonFacts)
        {
            approvalType = EventTicketType.Create(Guid.CreateVersion7(), PlatformDefaults.DefaultTenantId,
                catalog.Id, "Approval file ticket", "USD", TicketPricingModeEnum.Free,
                null, null, null, ParticipantDataCollectionModeEnum.None,
                null, null, null, false, false, null, null, null, null);
            catalog.AddTicketType(approvalType, null);
            entitlement = TicketTypeEntitlement.CreateForEvent(approvalType.Id,
                PlatformDefaults.DefaultTenantId, eventId, 1);
            catalog.AddEntitlement(approvalType, entitlement);
            catalog.Publish();
        }
        var order = RegistrationOrder.Create(PlatformDefaults.DefaultTenantId, eventId,
            purchaserUser.Id, purchaserActor.Id,
            BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(),
                crossPersonFacts ? 2 : 1, crossPersonFacts ? 2 : 1, crossPersonFacts ? 2 : 1, null),
            null, null, "USD", DateTime.UtcNow, null);
        orderId = order.Id;
        var line = approvalType is null ? null
            : RegistrationOrderLine.Create(catalog, approvalType, order.Id, crossPersonFacts ? 2 : 1, null, null);
        if (line is not null) order.AddLine(line);
        var participant = RegistrationParticipant.Create(PlatformDefaults.DefaultTenantId, order.Id,
            user.Id, ParticipantTypeEnum.Adult, null);
        order.AddParticipant(participant);
        RegistrationTicketAssignment? assignment = null;
        if (line is not null)
        {
            assignment = RegistrationTicketAssignment.CreateAssigned(
                Guid.CreateVersion7(), line.Id, 1, participant, DateTime.UtcNow);
            order.AddAssignment(line, assignment, participant);
            var eligibility = ParticipantAdmissionEligibility.Create(
                PlatformDefaults.DefaultTenantId, eventId, assignment, participant,
                false, approvalGated, DateTime.UtcNow);
            eligibility.RecordSubjectCompletion(participant, user.Id, null, DateTime.UtcNow, Guid.CreateVersion7());
            if (approvalGated) eligibility.Approve(actor.Id, DateTime.UtcNow, Guid.CreateVersion7());
            db.ParticipantAdmissionEligibilities.Add(eligibility);
            if (crossPersonFacts)
            {
                var other = RegistrationParticipant.Create(PlatformDefaults.DefaultTenantId, order.Id,
                    purchaserUser.Id, ParticipantTypeEnum.Adult, null);
                order.AddParticipant(other);
                var otherAssignment = RegistrationTicketAssignment.CreateAssigned(
                    Guid.CreateVersion7(), line.Id, 2, other, DateTime.UtcNow);
                order.AddAssignment(line, otherAssignment, other);
                var otherEligibility = ParticipantAdmissionEligibility.Create(
                    PlatformDefaults.DefaultTenantId, eventId, otherAssignment, other, false, true, DateTime.UtcNow);
                otherEligibility.Approve(purchaserActor.Id, DateTime.UtcNow, Guid.CreateVersion7());
                db.ParticipantAdmissionEligibilities.Add(otherEligibility);
                db.EventRegistrations.Add(new EventRegistration
                {
                    Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                    EventId = eventId, Event = parent, EventSessionId = sessionId, EventSession = session,
                    LinkedUserId = purchaserUser.Id, RegistrationOrderId = order.Id,
                    RegistrationOrderLineId = line.Id, RegistrationParticipantId = other.Id,
                    RegistrationParticipant = other, CoverageEstablishedAt = DateTime.UtcNow,
                    ConcurrencyStamp = Guid.CreateVersion7()
                });
            }
        }
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("USD", 0, 0, 0, 0));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, DateTime.UtcNow);
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, DateTime.UtcNow);
        order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, DateTime.UtcNow);
        AdmissionTarget? checkInTarget = null;
        if (checkedInAudience)
        {
            DateTime now = DateTime.UtcNow;
            var ticket = AdmissionTicket.Issue(order, line!, assignment!, participant, catalog,
                approvalType!, Guid.CreateVersion7(), "RESOURCE-CHECKIN", Guid.CreateVersion7(), 1, 1,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), now);
            checkInTarget = AdmissionTarget.Create(Guid.CreateVersion7(), PlatformDefaults.DefaultTenantId,
                eventId, AdmissionTargetTypeEnum.Event, null, null);
            var checkInPolicy = AdmissionCheckInPolicy.Create(Guid.CreateVersion7(), checkInTarget,
                now.AddHours(-1), now.AddHours(1), 1);
            var checkIn = AdmissionCheckInRules.Decide(ticket, checkInTarget, entitlement!, checkInPolicy,
                AdmissionCheckInState.Create(Guid.CreateVersion7(), ticket, checkInTarget),
                AdmissionCheckInActionEnum.CheckIn, Guid.CreateVersion7(), actor.Id, null, null, now);
            db.AddRange(ticket, checkInTarget, checkInPolicy, checkIn.Event!, checkIn.NextState);
        }
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
        var resource = EventResource.CreateDraft(resourceId, PlatformDefaults.DefaultTenantId, eventId,
            completedRecording ? sessionId : null,
            new EventResourceMetadata
            {
                Title = title, Kind = completedRecording ? EventResourceKindEnum.Recording
                    : EventResourceKindEnum.GeneralDocument,
                DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
            }, externalDestination ? EventResourceDeliveryTypeEnum.ExternalLink
                : EventResourceDeliveryTypeEnum.StoredFile,
            completedRecording
                ? EventResourceAvailability.Create(startAnchor: EventResourceAvailabilityAnchorEnum.SessionEnd,
                    startOffset: TimeSpan.Zero)
                : EventResourceAvailability.Create(),
            [EventResourceAudienceRule.Create(PlatformDefaults.DefaultTenantId, eventId, resourceId,
                staffExpiry.HasValue ? EventResourceAudienceKindEnum.EventStaff
                    : speakerAudience ? EventResourceAudienceKindEnum.SessionSpeaker
                    : checkedInAudience ? EventResourceAudienceKindEnum.CheckedInParticipant
                    : EventResourceAudienceKindEnum.SessionRegistrant,
                sessionId: staffExpiry.HasValue ? null : sessionId,
                ticketTypeId: checkedInAudience ? approvalType!.Id : null,
                ticketCatalogVersionId: checkedInAudience ? catalog.Id : null,
                targetType: checkedInAudience ? AdmissionTargetTypeEnum.Event : null,
                targetId: checkInTarget?.Id, targetScopeId: checkedInAudience ? eventId : null,
                requireApproval: approvalGated || crossPersonFacts,
                requireCompletion: approvalGated || crossPersonFacts)], user.Id, DateTime.UtcNow);
        if (externalDestination)
        {
            using var services = factory.Services.CreateScope();
            var protector = services.ServiceProvider.GetRequiredService<IEventResourceDestinationProtector>();
            resource.SetExternalDestination(protector.Protect(destination!, PlatformDefaults.DefaultTenantId,
                    resourceId, protector.CurrentVersion), protector.CurrentVersion,
                "https://resource.example.org", resource.ConcurrencyStamp, user.Id, DateTime.UtcNow);
        }
        else
            resource.SetStoredFile(storageId, resource.ConcurrencyStamp, user.Id, DateTime.UtcNow);
        resource.Publish(new(PlatformDefaults.DefaultTenantId, eventId, completedRecording ? sessionId : null,
            EventStatusEnum.Published, false, true,
            completedRecording ? EventSessionStatusEnum.Published : null, false,
            completedRecording
                ? new(null, null, session.StartTime, session.EndTime)
                : new(session.StartTime, session.EndTime, null, null)), true,
            resource.ConcurrencyStamp, user.Id, DateTime.UtcNow);
        if (staffExpiry.HasValue)
            db.EventRoleAssignments.Add(EventRoleAssignment.Create(PlatformDefaults.DefaultTenantId, eventId,
                user.Id, (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active,
                staffExpiry.Value.UtcDateTime.AddMinutes(-2), staffExpiry.Value.UtcDateTime, user.Id));
        if (speakerAudience)
            db.EventSessionSpeakers.Add(new EventSessionSpeaker
            {
                Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
                ActorId = actor.Id, Actor = actor, EventSessionId = sessionId, EventSession = session,
                ConcurrencyStamp = Guid.CreateVersion7()
            });
        db.AddRange(parent, catalog, order, binding, storage, resource, new EventRegistration
        {
            Id = Guid.CreateVersion7(), TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!,
            EventId = eventId, Event = parent, EventSessionId = sessionId, EventSession = session,
            LinkedUserId = user.Id, RegistrationOrderId = order.Id,
            RegistrationOrderLineId = line?.Id,
            RegistrationParticipantId = participant.Id, RegistrationParticipant = participant,
            CoverageEstablishedAt = DateTime.UtcNow, ConcurrencyStamp = Guid.CreateVersion7()
        });
        await db.SaveChangesAsync();
        return new(credentials, eventId, sessionId, resourceId, storageId, binding.Id, orderId, otherTenantId, objectKey, title,
            bytes, destination, purchaserCredentials, checkInTarget?.Id);
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

    private sealed class PermissiveAccessBarrier : IEventResourceAuthorizationProvider
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
            if (input.Action is not ("download" or "access") || Volatile.Read(ref _armed) == 0)
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
        Guid EventId, Guid SessionId, Guid ResourceId, Guid StorageId, Guid BindingId, Guid OrderId, Guid OtherTenantId, string ObjectKey,
        string ProtectedTitle, byte[] Bytes, string? Destination,
        Explore.Application.Features.Authentication.Local.Models.LocalAuthRequestDto? PurchaserCredentials,
        Guid? AdmissionTargetId);
}
