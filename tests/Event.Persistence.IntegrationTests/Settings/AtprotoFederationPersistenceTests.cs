// ABOUTME: PostgreSQL integration tests for ATProto event governance seed state and community-minimum events.
// ABOUTME: Proves locked instance defaults and local persistence without requiring a scheduled session.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
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
        await using var context = fixture.CreateDbContext();

        var settings = await context.Set<SystemSetting>()
            .AsNoTracking()
            .Where(setting => setting.Category == "AtprotoFederation")
            .OrderBy(setting => setting.DisplayOrder)
            .ToArrayAsync();

        await Assert.That(settings).Count().IsEqualTo(4);
        await Assert.That(settings[0].SettingKey).IsEqualTo(GovernanceSettingKeys.Federation.AtprotoEventsEnabled);
        await Assert.That(settings[0].Value).IsEqualTo("false");
        await Assert.That(settings[0].IsLocked).IsTrue();
        await Assert.That(settings[1].SettingKey).IsEqualTo(GovernanceSettingKeys.Federation.AtprotoEventValidationProfile);
        await Assert.That(settings[1].Value).IsEqualTo("\"platform\"");
        await Assert.That(settings[1].IsLocked).IsTrue();
        await Assert.That(settings[2].SettingKey).IsEqualTo("federation.atproto_events_backfill_enabled");
        await Assert.That(settings[2].Value).IsEqualTo("false");
        await Assert.That(settings[2].IsLocked).IsTrue();
        await Assert.That(settings[3].SettingKey).IsEqualTo("federation.atproto_events_backfill_mode");
        await Assert.That(settings[3].Value).IsEqualTo("\"downtime_only\"");
        var allowedModes = System.Text.Json.JsonSerializer.Deserialize<string[]>(settings[3].AllowedValues!);
        await Assert.That(allowedModes).IsEquivalentTo(["downtime_only", "full"]);
        await Assert.That(settings[3].IsLocked).IsTrue();
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
