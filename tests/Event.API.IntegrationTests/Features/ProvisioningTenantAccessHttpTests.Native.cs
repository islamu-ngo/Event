using System.Net;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.LegalDocuments.Requests.Queries;
using Explore.Domain.ValueObjects;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.Events.OpenGraph;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.Federation.Atproto.Requests.Queries;
using Explore.Application.Features.PublicExperience.Requests.Queries;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Application.Features.Tenants.Requests.Queries;
using Explore.Application.Models.Storage;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class ProvisioningTenantAccessHttpTests
{
    [Test]
    public async Task ResidualHostOnlyRoutingUsesWarmDomainWithoutSlugHint()
    {
        await using var factory = await CreateReadyAsync();
        await using (var db = factory.CreateDatabase())
        {
            (await db.InstanceBootstrapStates.SingleAsync(Token)).TransitionDeploymentMode(DeploymentMode.MultiTenant);
            db.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = TenantId,
                Tenant = null!,
                SettingKey = GovernanceSettingKeys.Domains.TenantCustomDomain,
                Value = "\"private.example.test\"",
                CreatedAt = DateTime.UtcNow
            });
            db.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = GovernanceSettingKeys.Routing.ResolverCustomDomainEnabled,
                Value = "true",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(Token);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IDeploymentModeProvider>().InvalidateCacheAsync();
        await factory.Services.GetRequiredService<ITenantSlugCache>().RefreshAsync(Token);
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Host = "private.example.test";
        await Assert.That(client.DefaultRequestHeaders.Contains("X-Tenant-Slug")).IsFalse();
        using (var active = await client.GetAsync("/api/PublicExperience/settings", Token))
            await Assert.That(active.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await SetLifecycleAsync(factory, TenantStatusEnum.Provisioning);
        using var denied = await client.GetAsync("/api/PublicExperience/settings", Token);
        await AssertLifecycleDenialAsync(denied);
    }

    [Test]
    [Arguments("settings")]
    [Arguments("shell")]
    [Arguments("tenant-list")]
    [Arguments("tenant-detail")]
    [Arguments("navigation")]
    [Arguments("event-list")]
    [Arguments("event-detail")]
    [Arguments("public-detail")]
    [Arguments("calendar")]
    [Arguments("open-graph")]
    [Arguments("media")]
    [Arguments("legal")]
    [Arguments("discovery")]
    [Arguments("home")]
    public async Task ResidualNativePublicReadsRecheckLifecycleAfterActiveResult(string path)
    {
        await using var factory = await CreateReadyAsync();
        var fixture = await SeedPublicContentAsync(factory, enableFederation: path == "discovery");
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.OpenReadAsync(Arg.Any<FileStorageReadInput>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<FileStorageReadInput>()?.ObjectKey == fixture.ObjectKey
                ? new FileStorageReadResult(new MemoryStream(fixture.ImageBytes), "image/png", fixture.ImageBytes.Length, null)
                : throw new FileNotFoundException());
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFileStorageProvider>();
            services.AddSingleton(provider);
        }));
        // Keep the same handler scope and real HybridCache across the persisted lifecycle change.
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(TenantId);
        await Assert.That(await ReadPublicAsync(scope.ServiceProvider, path, fixture.EventId, fixture.ImageId)).IsTrue();
        await SetLifecycleAsync(factory, TenantStatusEnum.Provisioning);
        await Assert.That(await ReadPublicAsync(scope.ServiceProvider, path, fixture.EventId, fixture.ImageId)).IsFalse();
    }

    private static async Task<bool> ReadPublicAsync(IServiceProvider services, string path, Guid eventId, Guid imageId)
    {
        switch (path)
        {
            case "settings":
                return (await QueryAsync<GetPublicExperienceSettingsQuery, PublicExperienceSettingsDto>(services, new())).IsAvailable;
            case "shell":
                return (await QueryAsync<GetPublicExperienceShellQuery, PublicExperienceShellDto>(services, new())).IsAvailable;
            case "tenant-list":
                return (await QueryAsync<GetTenantListRequest, List<TenantListDto>>(services, new())).Any(value => value.Id == TenantId);
            case "tenant-detail":
                return await QueryAsync<GetTenantDetailsRequest, TenantDto?>(services, new(TenantId)) is not null;
            case "navigation":
                return (await QueryAsync<GetTenantNavLinksQuery, List<TenantNavigationLinkDto>>(services, new())).Count > 0;
            case "event-list":
                return (await QueryAsync<GetEventListRequest, PaginatedResult<EventListDto>>(services, new())).Items.Any(value => value.Id == eventId);
            case "event-detail":
                return await QueryAsync<GetEventDetailsRequest, EventDto?>(services, new(eventId)) is not null;
            case "public-detail":
                return await QueryAsync<GetPublicEventDetailsRequest, EventDto?>(services, new() { SlugCode = "lifecycle-ABCDEFGH" }) is not null;
            case "calendar":
                return await QueryAsync<GetEventCalendarExportRequest, EventCalendarExportDto?>(services, new(eventId)) is not null;
            case "open-graph":
                return await QueryAsync<GetPublicEventOpenGraphImageRequest, EventOpenGraphImageRenderResult?>(services, new() { SlugCode = "lifecycle-ABCDEFGH" }) is not null;
            case "media":
                var image = await QueryAsync<GetPublicImageRequest, StorageObjectContentResult?>(services, new(imageId));
                if (image is null) return false;
                await using (image.Content)
                {
                    using var bytes = new MemoryStream();
                    await image.Content.CopyToAsync(bytes, Token);
                    await Assert.That(bytes.Length).IsGreaterThan(0);
                }
                return true;
            case "legal":
                return (await QueryAsync<GetPublicLegalDocumentQuery, PublicLegalDocumentQueryResult>(services, new("tenant-terms", "en"))).IsAvailable;
            case "discovery":
                return (await QueryAsync<GetPublicEventDiscoveryRequest, PaginatedResult<EventDiscoveryItemDto>>(
                    services, new(new() { View = Explore.Application.Specifications.Events.TemporalView.All, SortBy = "title" })))
                    .Items.Any(value => value.Source == "atproto");
            case "home":
                var home = await QueryAsync<GetHomeDiscoveryQuery, HomeDiscoveryDto>(services, new());
                return home.Hero.Count + home.RecentlyAdded.Count + home.MostViewedOnline.Count > 0;
            default: throw new ArgumentOutOfRangeException(nameof(path));
        }
    }

    private static Task<TResult> QueryAsync<TQuery, TResult>(IServiceProvider services, TQuery query)
        where TQuery : IQuery<TResult> => services.GetRequiredService<IQueryHandler<TQuery, TResult>>().QueryAsync(query, Token);

    private static async Task<(Guid EventId, Guid ImageId, string ObjectKey, byte[] ImageBytes)> SeedPublicContentAsync(LocalAdmissionWebApplicationFactory factory, bool enableFederation)
    {
        await using var db = factory.CreateDatabase();
        var user = await db.Users.SingleAsync(Token);
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id,
            Pii = new ActorPii { DisplayName = "Public organizer" },
            CreatedAt = DateTime.UtcNow
        };
        db.Actors.Add(actor);
        db.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            Tenant = null!,
            UserId = user.Id,
            User = user,
            ActorId = actor.Id,
            Actor = actor,
            StatusId = (int)TenantUserStatusEnum.Active,
            CreatedAt = DateTime.UtcNow
        });
        var entity = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = actor,
            Title = "Lifecycle public event",
            Slug = "lifecycle",
            PublicCode = "ABCDEFGH",
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            CreatedAt = DateTime.UtcNow,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!,
            FirstSessionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            LastSessionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            FirstSessionStartUtc = DateTimeOffset.UtcNow.AddDays(1),
            LastSessionEndUtc = DateTimeOffset.UtcNow.AddDays(1).AddHours(1)
        };
        entity.Sessions.Add(new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            Tenant = null!,
            EventId = entity.Id,
            Event = entity,
            Title = "Public session",
            StartTime = DateTimeOffset.UtcNow.AddDays(1),
            EndTime = DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            CreatedAt = DateTime.UtcNow
        });
        db.Events.Add(entity);
        db.TenantNavigationLinks.Add(new TenantNavigationLink
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            Label = "Public navigation",
            Url = "/events",
            CreatedAt = DateTime.UtcNow
        });
        var image = new StorageObject
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Image,
            FileType = null!,
            Uri = "lifecycle.png",
            ObjectKey = $"tenants/{TenantId:N}/lifecycle.png",
            Provider = StorageProviders.Local,
            FullName = "lifecycle.png",
            SafeDisplayName = "lifecycle.png",
            Extension = ".png",
            ContentType = "image/png",
            Visibility = StorageObjectVisibilities.PublicImage,
            Purpose = StorageObjectPurposes.EventImage,
            LifecycleState = StorageObjectLifecycleStates.Active,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = user.Id
        };
        db.StorageObjects.Add(image);
        var now = DateTime.UtcNow.AddHours(-1);
        var legal = LegalDocument.CreateDraft(LegalDocumentScope.Tenant, TenantId, LegalDocumentKind.TenantTerms,
            LegalDocumentAudience.Public, [LegalDocumentLocalizedSource.Create("en", "Terms", "Summary", "{{accountable_identity}}")],
            null, "identity-reviewed", false, now);
        legal.SubmitForReview(now.AddMinutes(1));
        legal.Approve(user.Id, "review-approved", now.AddMinutes(2));
        legal.Schedule(now.AddMinutes(4), now.AddMinutes(3));
        legal.Publish(now.AddMinutes(4));
        db.LegalDocuments.Add(legal);
        var record = new AtprotoRecord
        {
            Id = Guid.CreateVersion7(),
            Did = "did:plc:lifecyclefixture",
            Collection = "community.lexicon.calendar.event",
            RecordKey = "lifecycle",
            Direction = AtprotoRecordDirection.Inbound,
            Provenance = AtprotoRecordProvenance.Jetstream,
            SourceVersion = 1,
            UpdatedAt = now
        };
        db.AtprotoRecords.Add(record);
        db.AtprotoIdentities.Add(new AtprotoIdentity(AtprotoDid.Parse(record.Did))
        {
            Id = Guid.CreateVersion7(),
            ActorId = actor.Id,
            Actor = actor,
            IsActive = true,
            PdsHost = "https://pds.example.test",
            CreatedAt = now
        });
        db.Events.Add(new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = actor,
            Title = "Federated lifecycle event",
            PublicCode = "IJKLMNOP",
            AtprotoRecordId = record.Id,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.Federated,
            CreatedAt = now,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!
        });
        db.AtprotoRecordTenantPresentations.Add(new AtprotoRecordTenantPresentation
        {
            TenantId = TenantId,
            AtprotoRecordId = record.Id,
            IsVisible = true,
            SourceVersion = 1,
            EvaluatedAt = now
        });
        db.AtprotoEventProjections.Add(new AtprotoEventProjection
        {
            AtprotoRecordId = record.Id,
            Name = "Federated lifecycle event",
            CreatedAt = now,
            StartsAt = DateTimeOffset.UtcNow.AddDays(1),
            EndsAt = DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            Mode = "virtual",
            SourceVersion = 1,
            MaterializedAt = now
        });
        (await db.SystemSettings.SingleAsync(
            setting => setting.SettingKey == GovernanceSettingKeys.Federation.AtprotoEventsEnabled, Token)).Value = enableFederation ? "true" : "false";
        await db.SaveChangesAsync(Token);
        return (entity.Id, image.Id, image.ObjectKey, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a0ioAAAAASUVORK5CYII="));
    }
}
