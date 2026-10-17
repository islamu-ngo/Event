using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresActorConsolidationAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false, false, true)]
    [Arguments(true, false, true)]
    [Arguments(false, true, true)]
    [Arguments(false, false, false)]
    public async Task ConsolidationMovesBothTenantReferencesInTheOwningTransaction(bool rollback, bool collision, bool capability)
    {
        await using var rows = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
        Guid sourceId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        Guid identityId = Guid.CreateVersion7();
        Guid[] seriesIds = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        Guid[] eventIds = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        Guid[] speakerIds = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        Guid[] subscriptionIds = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        string[] tables;
        await using (var seed = fixture.CreateDbContext())
        {
            var subject = new ExternalActorSubject
            {
                Id = Guid.CreateVersion7(),
                FirstObservedAt = DateTime.UtcNow,
                LastObservedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            var organization = new Organization
            {
                Id = Guid.CreateVersion7(),
                Pii = new OrganizationPii { FullName = "Consolidation target" },
                CreatedAt = DateTime.UtcNow
            };
            seed.Actors.AddRange(
                new Actor
                {
                    Id = sourceId,
                    ActorTypeId = (int)ActorTypeEnum.ExternalUnclassified,
                    ActorType = null!,
                    ExternalActorSubjectId = subject.Id,
                    ExternalActorSubject = subject,
                    Pii = new ActorPii { DisplayName = "Consolidation source" },
                    CreatedAt = DateTime.UtcNow
                },
                new Actor
                {
                    Id = targetId,
                    ActorTypeId = (int)ActorTypeEnum.Organization,
                    ActorType = null!,
                    OrganizationId = organization.Id,
                    Organization = organization,
                    Pii = new ActorPii { DisplayName = "Consolidation target" },
                    CreatedAt = DateTime.UtcNow
                });
            seed.AtprotoIdentities.Add(new AtprotoIdentity(AtprotoDid.Parse("did:plc:" + sourceId.ToString("N")[..24]))
            {
                Id = identityId,
                ActorId = sourceId,
                Actor = null!,
                PdsHost = "https://pds.example.test",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                LastResolvedAt = DateTime.UtcNow
            });
            seed.EventSeries.AddRange(
                Series(seriesIds[0], rows.A, sourceId), Series(seriesIds[1], rows.B, sourceId));
            for (int index = 0; index < 2; index++)
            {
                Guid tenant = index == 0 ? rows.A : rows.B;
                var sourceEvent = new Explore.Domain.Event(EventStatusEnum.Draft)
                {
                    Id = eventIds[index],
                    TenantId = tenant,
                    Tenant = null!,
                    ActorId = sourceId,
                    Actor = null!,
                    OrganizerActorId = sourceId,
                    Title = "Consolidation event",
                    EventStatus = null!,
                    EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                    VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                    VisibilityType = null!,
                    EventFormatId = (int)EventFormatEnum.Digital,
                    EventFormat = null!,
                    CreatedAt = DateTime.UtcNow
                };
                var session = new EventSession
                {
                    Id = Guid.CreateVersion7(),
                    EventId = sourceEvent.Id,
                    Event = sourceEvent,
                    TenantId = tenant,
                    Tenant = null!,
                    CreatedAt = DateTime.UtcNow
                };
                seed.EventSessionSpeakers.Add(new EventSessionSpeaker
                {
                    Id = speakerIds[index],
                    ActorId = sourceId,
                    Actor = null!,
                    TenantId = tenant,
                    Tenant = null!,
                    EventSessionId = session.Id,
                    EventSession = session
                });
                Guid member = await seed.TenantUsers.Where(row =>
                    row.TenantId == tenant && row.UserId == rows.UserId).Select(row => row.Id).SingleAsync();
                seed.ActorSubscriptions.Add(Subscription(subscriptionIds[index], tenant, member, rows.UserId, sourceId,
                    ActorTypeEnum.ExternalUnclassified));
                if (collision && index == 1)
                    seed.ActorSubscriptions.Add(Subscription(Guid.CreateVersion7(), tenant, member, rows.UserId, targetId,
                        ActorTypeEnum.Organization));
            }
            await seed.SaveChangesAsync();
            var sql = seed.GetService<ISqlGenerationHelper>();
            tables = new[] { typeof(Actor), typeof(AtprotoIdentity), typeof(Explore.Domain.Event),
                typeof(EventSeries), typeof(EventSessionSpeaker), typeof(ActorSubscription) }
                .Select(type => seed.Model.FindEntityType(type)!)
                .Select(entity => sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema())).ToArray();
        }
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        string role = rows.RuntimeOptions.Username ?? throw new InvalidOperationException("The native fixture requires a Runtime login.");
        await using (var grant = administration.CreateCommand())
        {
            grant.CommandText = $"""
                GRANT SELECT ON {string.Join(", ", tables)} TO "{role}";
                GRANT UPDATE ON {tables[0]} TO "{role}";
                GRANT "{PostgresDiscoveryDisclosureContract.RuntimeRole}" TO "{role}" WITH INHERIT TRUE, SET FALSE;
                """;
            if (capability)
                grant.CommandText += $"\nGRANT \"{PostgresActorConsolidationContract.RuntimeRole}\" TO \"{role}\" WITH INHERIT TRUE, SET FALSE;";
            await grant.ExecuteNonQueryAsync();
        }
        try
        {
            var factory = new TenantExploreDbContextFactory(rows.RuntimeOptions);
            await using var context = await factory.CreateDbContextAsync(rows.A);
            await rows.AssertOrdinaryRoleAsync(context);
            await Assert.That(await context.EventSeries.IgnoreQueryFilters()
                .CountAsync(series => seriesIds.Contains(series.Id))).IsEqualTo(1);
            if (!capability)
            {
                var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                    await new ActorReferenceConsolidationRepository(context).MoveMutableReferencesAsync(
                        sourceId, targetId, (int)ActorTypeEnum.Organization));
                await Assert.That(denied!.SqlState).IsEqualTo("42501");
                await using var unchanged = fixture.CreateDbContext();
                await Assert.That(await unchanged.EventSeries.IgnoreQueryFilters()
                    .CountAsync(series => seriesIds.Contains(series.Id) && series.ActorId == sourceId)).IsEqualTo(2);
                return;
            }
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var repository = new ActorReferenceConsolidationRepository(context);
                await Assert.That(await repository.MoveMutableReferencesAsync(
                    sourceId, targetId, (int)ActorTypeEnum.Organization)).IsEqualTo(!collision);
                if (rollback)
                    await transaction.RollbackAsync();
                else
                    await transaction.CommitAsync();
            });
            await Assert.That(context.TenantFilterTenantId).IsEqualTo(rows.A);
            await Assert.That(await context.EventSeries.IgnoreQueryFilters()
                .CountAsync(series => seriesIds.Contains(series.Id))).IsEqualTo(1);
            await using var persisted = fixture.CreateDbContext();
            Guid expected = rollback || collision ? sourceId : targetId;
            await Assert.That(await persisted.AtprotoIdentities.IgnoreQueryFilters()
                .Where(identity => identity.Id == identityId).Select(identity => identity.ActorId).SingleAsync())
                .IsEqualTo(expected);
            await Assert.That(await persisted.EventSeries.IgnoreQueryFilters()
                .CountAsync(series => seriesIds.Contains(series.Id)
                    && series.ActorId == expected)).IsEqualTo(2);
            await Assert.That(await persisted.Events.IgnoreQueryFilters().CountAsync(row =>
                eventIds.Contains(row.Id) && row.ActorId == expected && row.OrganizerActorId == expected)).IsEqualTo(2);
            await Assert.That(await persisted.EventSessionSpeakers.IgnoreQueryFilters().CountAsync(row =>
                speakerIds.Contains(row.Id) && row.ActorId == expected)).IsEqualTo(2);
            await Assert.That(await persisted.ActorSubscriptions.IgnoreQueryFilters().CountAsync(row =>
                subscriptionIds.Contains(row.Id) && row.TargetActorId == expected
                    && row.TargetActorTypeId == (expected == sourceId
                        ? (int)ActorTypeEnum.ExternalUnclassified : (int)ActorTypeEnum.Organization))).IsEqualTo(2);
        }
        finally
        {
            await using var revoke = administration.CreateCommand();
            revoke.CommandText = $"""
                REVOKE SELECT ON {string.Join(", ", tables)} FROM "{role}";
                REVOKE UPDATE ON {tables[0]} FROM "{role}";
                REVOKE "{PostgresDiscoveryDisclosureContract.RuntimeRole}" FROM "{role}";
                """;
            if (capability)
                revoke.CommandText += $"\nREVOKE \"{PostgresActorConsolidationContract.RuntimeRole}\" FROM \"{role}\";";
            await revoke.ExecuteNonQueryAsync();
        }
    }

    private static EventSeries Series(Guid id, Guid tenant, Guid actor) => new()
    {
        Id = id,
        TenantId = tenant,
        ActorId = actor,
        Title = "Consolidation reference",
        VisibilityTypeId = (int)VisibilityTypeEnum.Public,
        VisibilityType = null!,
        CreatedAt = DateTime.UtcNow
    };

    private static ActorSubscription Subscription(Guid id, Guid tenant, Guid member, Guid user, Guid actor, ActorTypeEnum type) => new()
    {
        Id = id,
        TenantId = tenant,
        Tenant = null!,
        SubscriberTenantUserId = member,
        SubscriberTenantUser = null!,
        SubscriberUserId = user,
        SubscriberUser = null!,
        TargetActorId = actor,
        TargetActor = null!,
        TargetActorTypeId = (int)type,
        TargetActorType = null!,
        StatusId = (int)ActorSubscriptionStatusEnum.Active,
        Status = null!,
        NotificationLevelId = (int)ActorSubscriptionNotificationLevelEnum.All,
        NotificationLevel = null!,
        SubscribedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };
}
