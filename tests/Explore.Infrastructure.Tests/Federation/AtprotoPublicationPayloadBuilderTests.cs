using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services.Federation;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoPublicationPayloadBuilderTests
{
    [Test]
    [Arguments("https://events.example.test/community/", null, true)]
    [Arguments(null, "https://events.example.test/community/", true)]
    [Arguments(null, null, false)]
    [Arguments("https://invalid.example.test/?query=not-an-origin", "https://events.example.test/community/", false)]
    public async Task ManagedImagePublicationUsesOnlyAuthoritativePublicOrigin(
        string? configuredOrigin, string? storedOrigin, bool expectedValid)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid imageId = Guid.CreateVersion7();
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Organization,
            ActorType = new ActorType { MasterCode = "ORG", FullName = "Organization" },
            Pii = new ActorPii { DisplayName = "Organizer" }
        };
        var eventEntity = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            Title = "Public image event",
            ActorId = actor.Id,
            Actor = actor,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = new VisibilityType { MasterCode = "PUBLIC", FullName = "Public" },
            EventStatus = new EventStatus { MasterCode = "DRAFT", FullName = "Draft" },
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = new EventFormat { MasterCode = "DIGITAL", FullName = "Digital" },
            CreatedAt = new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc),
            FeaturedImage = new StorageObject
            {
                Id = imageId,
                TenantId = tenantId,
                Tenant = null!,
                FileType = null!,
                FileTypeId = (int)FileTypeEnum.Image,
                Provider = StorageProviders.Local,
                StorageProviderBindingId = Guid.CreateVersion7(),
                ObjectKey = $"images/{imageId:N}.png",
                SourceUri = "https://provider.example.test/provenance-canary",
                FullName = "image.png",
                SafeDisplayName = "image.png",
                Extension = "png",
                ContentType = "image/png",
                Purpose = StorageObjectPurposes.EventImage,
                Visibility = StorageObjectVisibilities.PublicImage,
                LifecycleState = StorageObjectLifecycleStates.Active
            }
        };
        eventEntity.ParticipationConfiguration = EventParticipationConfiguration.Create(
            eventEntity.Id, tenantId, (int)ParticipationHandlingModeEnum.InformationOnly,
            (int)AdvanceRegistrationObligationEnum.NotApplicable, null, null, eventEntity.CreatedAt);
        var graph = new AtprotoEventPublicationEntityGraph(eventEntity,
            [], [], [], [], [], [], [], [], [], [], [], [], [], [], []);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["PUBLIC_BASE_URL"] = configuredOrigin }).Build();
        var settings = Substitute.For<ISystemSettingRepository>();
        settings.GetByKey(GovernanceSettingKeys.Domains.PublicBaseUrl, Arg.Any<CancellationToken>())
            .Returns(storedOrigin is null ? null : new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Domains.PublicBaseUrl,
                Value = JsonSerializer.Serialize(storedOrigin)
            });
        var governance = Substitute.For<ILocationPrivacyGovernanceService>();
        var snapshotFactory = new AtprotoEventPublicationSnapshotFactory(
            new PublicEventLocationDisclosureEvaluator(governance, new EventLocationDisclosureEvaluator()));
        var builder = new AtprotoPublicationPayloadBuilder(snapshotFactory, configuration, settings);

        var result = await builder.BuildEventAsync(graph,
            new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero), CancellationToken.None);

        await Assert.That(result.IsValid).IsEqualTo(expectedValid);
        if (expectedValid)
        {
            using var payload = JsonDocument.Parse(result.Payload!.Json);
            await Assert.That(payload.RootElement.GetProperty("uris")[0].GetProperty("uri").GetString())
                .IsEqualTo($"https://events.example.test/community/api/storageobject/{imageId}/public");
            await Assert.That(result.Payload.Json).DoesNotContain("provenance-canary");
        }
        else
        {
            await Assert.That(result.FailureCode).IsEqualTo("public_origin_unavailable");
            await Assert.That(result.Payload).IsNull();
        }
    }
}
