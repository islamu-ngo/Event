using Event.Persistence.IntegrationTests.ConfigurationManifest;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Settings;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class AtprotoFederationPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task SeededInstanceGovernance_IsDisabledPlatformAndLockedWithoutSystemConsent()
    {
        await fixture.ResetAsync();
        await using var context = FederationManifestStorageSnapshot.OpenMigrator(fixture);

        var settings = await context.Set<SystemSetting>()
            .AsNoTracking()
            .Where(setting => setting.Category == "AtprotoFederation")
            .OrderBy(setting => setting.DisplayOrder)
            .ToArrayAsync();

        await Assert.That(settings).Count().IsEqualTo(1);
        await Assert.That(settings[0].SettingKey).IsEqualTo(GovernanceSettingKeys.Federation.AtprotoEventValidationProfile);
        await Assert.That(settings[0].Value).IsEqualTo("\"platform\"");
        await Assert.That(settings[0].IsLocked).IsTrue();
        var dedicated = await context.Set<AtprotoFederationInstanceSetting>().AsNoTracking()
            .OrderBy(setting => setting.SettingKey).ToArrayAsync();
        await Assert.That(dedicated.Select(setting => (setting.Id, setting.SettingKey, setting.Value, setting.IsLocked)))
            .IsEquivalentTo([
                (SeedIds.AtprotoFederationEventsEnabledId, GovernanceSettingKeys.Federation.AtprotoEventsEnabled, "false", true),
                (SeedIds.AtprotoFederationEventsBackfillEnabledId, GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled, "false", true),
                (SeedIds.AtprotoFederationEventsBackfillModeId, GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode, "\"downtime_only\"", true)
            ]);
        string[] movedKeys = dedicated.Select(setting => setting.SettingKey).ToArray();
        await Assert.That(await context.SystemSettings.AnyAsync(setting => movedKeys.Contains(setting.SettingKey))).IsFalse();
        await Assert.That(await context.SystemSettings.AnyAsync(setting =>
            setting.SettingKey == "federation.decentralization_enabled")).IsFalse();
        await Assert.That(AtprotoFederationSettingDefinitions.EventsBackfillMode.AllowedValues!)
            .IsEquivalentTo(["downtime_only", "full"]);
        await Assert.That(settings.Any(setting =>
            setting.SettingKey == GovernanceSettingKeys.Federation.AtprotoPublishMyEvents)).IsFalse();
    }

    [Test]
    public async Task CommunityMinimumEvent_PersistsLocallyWithoutSchedule()
    {
        await using var context = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            FullName = "ATProto community tenant",
            Slug = $"atproto-community-{Guid.NewGuid():N}",
            TenantStatusId = 2,
            TenantStatus = null!
        };
        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"atproto-{Guid.NewGuid():N}@example.test",
                FirstName = "Community",
                LastName = "Publisher"
            }
        };
        context.Tenants.Add(tenant);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Community publisher" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var eventId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        var repository = new EventRepository(context);
        await repository.Create(new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = eventId,
            Title = "Community minimum event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.Federated,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            CreatedAt = createdAt,
            CreatedBy = user.Id,
            IsDeleted = false
        });

        await using var verifyContext = fixture.CreateDbContext();
        var persisted = await verifyContext.Events
            .AsNoTracking()
            .SingleAsync(@event => @event.Id == eventId);

        await Assert.That(persisted.Title).IsEqualTo("Community minimum event");
        await Assert.That(persisted.TenantId).IsEqualTo(tenant.Id);
        await Assert.That(persisted.ActorId).IsEqualTo(actor.Id);
        await Assert.That(persisted.CreatedBy).IsEqualTo(user.Id);
        await Assert.That(persisted.FirstSessionStartUtc).IsNull();
        await Assert.That(await verifyContext.EventSessions
            .AsNoTracking()
            .AnyAsync(session => session.EventId == eventId)).IsFalse();
    }
}
