using System.Data;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;
using Npgsql;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[RequiresStructuredPrimaryDatabase]
[NotInParallel("PrimaryDatabaseProviderBehaviorContract")]
public sealed class EventDiscoveryWriterProviderTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    [Test]
    public async Task Detached_classification_already_holds_both_native_actor_anchors()
    {
        using var setupTimeout = new CancellationTokenSource(Deadline);
        var scope = await SeedAsync(setupTimeout.Token);
        var owners = Enumerable.Range(0, 2).Select(index => new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"writer-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Writer",
                LastName = "Owner"
            }
        }).ToArray();
        var actors = owners.Select(user => new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Writer owner" }
        }).ToArray();
        var entity = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantId,
            Tenant = null!,
            ActorId = actors[0].Id,
            Actor = actors[0],
            OrganizerActorId = actors[0].Id,
            Title = "Detached owner",
            PublicCode = Guid.CreateVersion7().ToString("N")[..12],
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            Timezone = "UTC"
        };
        await using (var seed = scope.Fixture.CreateSystemContext())
        {
            seed.AddRange(actors);
            seed.Add(entity);
            await seed.SaveChangesAsync(setupTimeout.Token);
        }
        using var timeout = new CancellationTokenSource(Deadline);
        var classification = new BeforeNativeCommand(typeof(Explore.Domain.Event), async () =>
        {
            foreach (var actor in actors)
                await Assert.That(await IsLockedAsync(scope, typeof(Actor), "Id", actor.Id, timeout.Token)).IsTrue();
            await Assert.That(await IsLockedAsync(scope, typeof(Tenant), "Id", scope.TenantId, timeout.Token)).IsTrue();
        }, lockingOnly: true);
        var planning = new BeforeNativeCommand(typeof(Actor), async () =>
        {
            if (scope.Fixture.Provider != PrimaryDatabaseProvider.Sqlite)
                await Assert.That(await IsLockedAsync(scope, typeof(Explore.Domain.Event),
                    "Id", entity.Id, timeout.Token)).IsFalse();
        }, lockingOnly: true);
        await using var writer = scope.Open(classification, planning);
        var detached = await writer.Events.AsNoTracking().SingleAsync(row => row.Id == entity.Id, timeout.Token);
        detached.ActorId = actors[1].Id;
        await new EventRepository(writer).Update(detached);
        await Assert.That(classification.Observed).IsTrue();
        await Assert.That(planning.Observed).IsTrue();
        await Assert.That((await writer.Events.AsNoTracking().SingleAsync(row => row.Id == entity.Id, timeout.Token)).ActorId)
            .IsEqualTo(actors[1].Id);
    }

    [Test]
    [RequiresRowLockProvider]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Independent_source_inserts_hold_tenant_before_foreign_keys_and_seal_once(
        bool secondIsMembership)
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var token = timeout.Token;
        var scope = await SeedAsync(token);
        var users = Enumerable.Range(0, 2).Select(_ => new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"source-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Source",
                LastName = "Owner"
            }
        }).ToArray();
        var actors = users.Select(user => new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Independent source owner" }
        }).ToArray();
        await using (var seed = scope.Fixture.CreateSystemContext())
        {
            seed.AddRange(actors);
            await seed.SaveChangesAsync(token);
        }

        Guid firstId = Guid.CreateVersion7();
        Guid secondId = Guid.CreateVersion7();
        var firstAtSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int firstAttempts = 0;
        int secondAttempts = 0;
        var firstSource = new BeforeNativeCommand(typeof(Explore.Domain.Event), async () =>
        {
            // No source INSERT (hence no implicit Tenant FK lock) has happened yet.
            // This native witness fails deterministically with terminal-only fencing.
            await Assert.That(await IsLockedAsync(scope, typeof(Tenant), "Id", scope.TenantId, token)).IsTrue();
            await Assert.That(await IsLockedAsync(scope, typeof(User), "Id", users[0].Id, token)).IsTrue();
            await Assert.That(await IsLockedAsync(scope, typeof(Actor), "Id", actors[0].Id, token)).IsTrue();
            await Assert.That(await IsLockedAsync(scope, typeof(EventDiscoveryRevision),
                nameof(EventDiscoveryRevision.TenantId), scope.TenantId, token)).IsFalse();
            firstAtSource.TrySetResult();
            await releaseFirst.Task.WaitAsync(token);
        });
        var secondParent = new BeforeNativeCommand(typeof(Tenant), async () =>
        {
            // A different User/Actor path must reach its Tenant fence without
            // taking a source FK first or waiting on the first writer's actors.
            await Assert.That(await IsLockedAsync(scope, typeof(User), "Id", users[1].Id, token)).IsTrue();
            await Assert.That(await IsLockedAsync(scope, typeof(Actor), "Id", actors[1].Id, token)).IsTrue();
            await Assert.That(await IsLockedAsync(scope, typeof(Tenant), "Id", scope.TenantId, token)).IsTrue();
            releaseFirst.TrySetResult();
        }, lockingOnly: true);

        await Task.WhenAll(FirstAsync(), SecondAsync()).WaitAsync(token);
        await Assert.That(firstSource.Observed).IsTrue();
        await Assert.That(secondParent.Observed).IsTrue();
        await Assert.That(firstAttempts).IsEqualTo(1);
        // PostgreSQL can reject the second snapshot after the first parent's
        // no-op UPDATE; replay is required there, not an FK conversion deadlock.
        if (scope.Fixture.Provider != PrimaryDatabaseProvider.PostgreSql)
            await Assert.That(secondAttempts).IsEqualTo(1);
        await using var reader = scope.Open();
        await Assert.That(await reader.Events.CountAsync(
            row => row.Id == firstId || row.Id == secondId, token)).IsEqualTo(secondIsMembership ? 1 : 2);
        await Assert.That(await reader.TenantUsers.CountAsync(
            row => row.Id == secondId, token)).IsEqualTo(secondIsMembership ? 1 : 0);
        var revision = await reader.Set<EventDiscoveryRevision>().SingleAsync(
            row => row.TenantId == scope.TenantId, token);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(2);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(0);

        async Task FirstAsync()
        {
            try
            {
                await using var writer = scope.Open(firstSource);
                await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async ct =>
                {
                    firstAttempts++;
                    writer.Events.Add(SourceEvent(firstId, 0));
                    await writer.SaveChangesAsync(ct);
                    return true;
                }, token);
            }
            catch (Exception exception)
            {
                firstAtSource.TrySetException(exception);
                throw;
            }
        }

        async Task SecondAsync()
        {
            try
            {
                await firstAtSource.Task.WaitAsync(token);
                await using var writer = scope.Open(secondParent);
                await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async ct =>
                {
                    secondAttempts++;
                    if (secondIsMembership)
                        writer.TenantUsers.Add(new TenantUser
                        {
                            Id = secondId,
                            TenantId = scope.TenantId,
                            Tenant = null!,
                            UserId = users[1].Id,
                            User = null!,
                            ActorId = actors[1].Id,
                            StatusId = (int)TenantUserStatusEnum.Active
                        });
                    else
                        writer.Events.Add(SourceEvent(secondId, 1));
                    await writer.SaveChangesAsync(ct);
                    return true;
                }, token);
            }
            finally
            {
                releaseFirst.TrySetResult();
            }
        }

        Explore.Domain.Event SourceEvent(Guid id, int owner) => new(EventStatusEnum.Published)
        {
            Id = id,
            TenantId = scope.TenantId,
            Tenant = null!,
            ActorId = actors[owner].Id,
            Actor = null!,
            SubmittedByUserId = users[owner].Id,
            Title = "Independent tenant source",
            PublicCode = id.ToString("N")[^12..],
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            Timezone = "UTC"
        };
    }

    [Test]
    [RequiresRowLockProvider]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Source_insert_and_identity_review_share_actor_tenant_event_order(bool indirectOwner)
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var token = timeout.Token;
        var scope = await SeedAsync(token);
        DateTime now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"review-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Review",
                LastName = "Owner"
            }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Review owner" }
        };
        var entity = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = scope.TenantId,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = actor,
            Title = "Reviewed parent",
            PublicCode = Guid.CreateVersion7().ToString("N")[^12..],
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            Timezone = "UTC"
        };
        await using (var seed = scope.Fixture.CreateSystemContext())
        {
            seed.Add(entity);
            seed.Add(EventRoleAssignment.Create(scope.TenantId, entity.Id, user.Id,
                (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active,
                now.AddDays(-1), now.AddDays(1), user.Id));
            await seed.SaveChangesAsync(token);
        }

        Guid sessionId = Guid.CreateVersion7();
        var beforeInsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reviewAtEvent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReview = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new BeforeNativeCommand(indirectOwner ? typeof(EventTechAspect) : typeof(EventSession), async () =>
        {
            await Assert.That(await IsLockedAsync(scope, typeof(Tenant), "Id", scope.TenantId, token)).IsTrue();
            beforeInsert.TrySetResult();
            if (!indirectOwner)
                await releaseInsert.Task.WaitAsync(token);
        });
        var reviewTenant = new BeforeNativeCommand(typeof(Tenant), async () =>
        {
            if (indirectOwner)
                return;
            // The real authority planner has its Actor anchor, but must not hold
            // Event while waiting for the inserting session's Tenant parent.
            await Assert.That(await IsLockedAsync(scope, typeof(Actor), "Id", actor.Id, token)).IsTrue();
            await Assert.That(await IsLockedAsync(scope, typeof(Explore.Domain.Event), "Id", entity.Id, token)).IsFalse();
            await Assert.That(await IsLockedAsync(scope, typeof(Tenant), "Id", scope.TenantId, token)).IsTrue();
            releaseInsert.TrySetResult();
        }, lockingOnly: true);
        var reviewEvent = new BeforeNativeCommand(typeof(Explore.Domain.Event), async () =>
        {
            if (!indirectOwner)
                return;
            reviewAtEvent.TrySetResult();
            await releaseReview.Task.WaitAsync(token);
        }, lockingOnly: true);
        var sourceParent = new BeforeNativeCommand(typeof(Tenant), async () =>
        {
            if (!indirectOwner)
                return;
            // Indirect ownership planning must not retain Event S before Tenant:
            // review already owns Tenant and is about to fence this same Event.
            await Assert.That(await IsLockedAsync(scope, typeof(Explore.Domain.Event), "Id", entity.Id, token)).IsFalse();
            await Assert.That(await IsLockedAsync(scope, typeof(Actor), "Id", actor.Id, token)).IsTrue();
            await Assert.That(await IsLockedAsync(scope, typeof(Tenant), "Id", scope.TenantId, token)).IsTrue();
            releaseReview.TrySetResult();
        }, lockingOnly: true);
        await Task.WhenAll(InsertAsync(), ReviewAsync()).WaitAsync(token);
        await Assert.That(source.Observed).IsTrue();
        await Assert.That(reviewTenant.Observed).IsTrue();
        await Assert.That(reviewEvent.Observed).IsTrue();
        await Assert.That(sourceParent.Observed).IsTrue();
        await using var reader = scope.Open();
        await Assert.That(await reader.EventSessions.CountAsync(row => row.Id == sessionId, token))
            .IsEqualTo(indirectOwner ? 0 : 1);
        await Assert.That(await reader.Set<EventTechAspect>().IgnoreQueryFilters()
            .CountAsync(row => row.Id == entity.Id, token)).IsEqualTo(indirectOwner ? 1 : 0);
        var revision = await reader.Set<EventDiscoveryRevision>().SingleAsync(
            row => row.TenantId == scope.TenantId, token);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(2);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(0);

        async Task InsertAsync()
        {
            try
            {
                if (indirectOwner)
                    await reviewAtEvent.Task.WaitAsync(token);
                await using var writer = scope.Open(source, sourceParent);
                await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async ct =>
                {
                    if (indirectOwner)
                        writer.Set<EventTechAspect>().Add(new EventTechAspect { Id = entity.Id, RequiresLaptop = true });
                    else
                        writer.EventSessions.Add(new EventSession(EventSessionStatusEnum.Draft)
                        {
                            Id = sessionId,
                            EventId = entity.Id,
                            Event = null!,
                            TenantId = scope.TenantId,
                            Tenant = null!,
                            Title = "Competing session"
                        });
                    await writer.SaveChangesAsync(ct);
                    return true;
                }, token);
            }
            catch (Exception exception)
            {
                beforeInsert.TrySetException(exception);
                throw;
            }
            finally
            {
                releaseReview.TrySetResult();
            }
        }

        async Task ReviewAsync()
        {
            try
            {
                if (!indirectOwner)
                    await beforeInsert.Task.WaitAsync(token);
                await using var reviewer = scope.Open(reviewTenant, reviewEvent);
                await new EfCoreUnitOfWork(reviewer).ExecuteSerializableAsync(async ct =>
                {
                    var authority = await new EventAuthoritySnapshotService(reviewer)
                        .GetCommitBoundForUserAndEventsAsync(scope.TenantId, user.Id, [entity.Id], now, ct);
                    await Assert.That(authority.Events[entity.Id].IsManager).IsTrue();
                    await new EventDiscoveryIdentityRepository(reviewer).AcquireFenceAsync(scope.TenantId, [], ct);
                    return true;
                }, token);
            }
            catch (Exception exception)
            {
                reviewAtEvent.TrySetException(exception);
                throw;
            }
            finally
            {
                releaseInsert.TrySetResult();
            }
        }
    }

    [Test]
    public async Task Fanout_holds_every_native_parent_before_its_first_epoch_command()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var scope = await SeedAsync(timeout.Token);
        var firstEpoch = new BeforeNativeCommand(typeof(EventDiscoveryRevision), async () =>
        {
            await Assert.That(await IsLockedAsync(scope, typeof(Tenant), "Id", scope.TenantId, timeout.Token)).IsTrue();
            await Assert.That(await IsLockedAsync(scope, typeof(Tenant), "Id", scope.OtherTenantId, timeout.Token)).IsTrue();
        });
        await using var writer = scope.Open(firstEpoch);
        await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
        {
            await new EventDiscoveryDisclosureRepository(writer)
                .AdvanceAsync([scope.OtherTenantId, scope.TenantId], token);
            return true;
        }, timeout.Token);
        await Assert.That(firstEpoch.Observed).IsTrue();
        await using var reader = scope.Fixture.CreateSystemContext();
        var revisions = await reader.Set<EventDiscoveryRevision>()
            .Where(row => row.TenantId == scope.TenantId || row.TenantId == scope.OtherTenantId)
            .Select(row => row.DisclosureEpoch).ToArrayAsync(timeout.Token);
        await Assert.That(revisions.Length).IsEqualTo(2);
        await Assert.That(revisions.All(value => value == 1)).IsTrue();
    }

    [Test]
    public async Task Identity_keeps_epoch_unlocked_until_after_audit_and_outbox()
    {
        using var timeout = new CancellationTokenSource(Deadline);
        var scope = await SeedAsync(timeout.Token);
        await using var writer = scope.Open();
        await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
        {
            var identities = new EventDiscoveryIdentityRepository(writer);
            await identities.AcquireFenceAsync(scope.TenantId, [], token);
            identities.ExpectRevisionAtCommit(scope.TenantId, 0);
            await Assert.That(await IsLockedAsync(scope, typeof(Tenant), "Id", scope.TenantId, token)).IsTrue();
            // SQLite excludes every writer at database scope; it cannot prove a
            // particular row is unlocked. Row-level engines must prove this boundary.
            if (scope.Fixture.Provider != PrimaryDatabaseProvider.Sqlite)
                await Assert.That(await IsLockedAsync(scope, typeof(EventDiscoveryRevision),
                    nameof(EventDiscoveryRevision.TenantId), scope.TenantId, token)).IsFalse();
            writer.AuditLogs.Add(new AuditLog
            {
                Id = Guid.CreateVersion7(),
                TenantId = scope.TenantId,
                Tenant = null!,
                EntityType = nameof(EventDiscoveryIdentity),
                EntityId = Guid.CreateVersion7().ToString("D"),
                Action = "different-offering",
                Timestamp = DateTime.UtcNow
            });
            writer.Set<OutboxMessage>().Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                AggregateType = nameof(EventDiscoveryIdentity),
                AggregateId = Guid.CreateVersion7(),
                EventType = "identity-review",
                Payload = "{}",
                Status = OutboxMessageStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                MaxRetries = 5
            });
            await writer.SaveChangesAsync(token);
            await writer.FlushDisclosureAsync(token);
            await Assert.That(await IsLockedAsync(scope, typeof(EventDiscoveryRevision),
                nameof(EventDiscoveryRevision.TenantId), scope.TenantId, token)).IsTrue();
            return true;
        }, timeout.Token);
        await Assert.That(await IsLockedAsync(scope, typeof(EventDiscoveryRevision),
            nameof(EventDiscoveryRevision.TenantId), scope.TenantId, timeout.Token)).IsFalse();
    }

    private static async Task<bool> IsLockedAsync(
        Scope scope, Type rowType, string propertyName, Guid id, CancellationToken token)
    {
        // A lock probe needs a native connection, not another uncached EF model.
        // Reuse the seeded mapping so model construction cannot consume its deadline.
        await using var connection = scope.ConnectionFactory.CreateConnection()
            ?? throw new InvalidOperationException("Native lock witnesses require a provider connection.");
        connection.ConnectionString = scope.ConnectionString;
        await connection.OpenAsync(token);
        if (connection is SqliteConnection sqlite)
        {
            int result = SQLitePCL.raw.sqlite3_exec(sqlite.Handle, "BEGIN IMMEDIATE");
            if (result == SQLitePCL.raw.SQLITE_OK)
            {
                await Assert.That(SQLitePCL.raw.sqlite3_exec(sqlite.Handle, "ROLLBACK")).IsEqualTo(SQLitePCL.raw.SQLITE_OK);
                return false;
            }
            await Assert.That(result & 255).IsEqualTo(SQLitePCL.raw.SQLITE_BUSY);
            return true;
        }
        var entity = scope.Model.FindEntityType(rowType)!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var property = entity.FindProperty(propertyName)!;
        var sql = scope.Sql;
        string table = sql.DelimitIdentifier(store.Name, store.Schema);
        string key = sql.DelimitIdentifier(property.GetColumnName(store)!);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 5;
        command.CommandText = scope.Fixture.Provider == PrimaryDatabaseProvider.SqlServer
            ? $"SELECT {key} FROM {table} WITH (XLOCK, ROWLOCK, NOWAIT) WHERE {key} = @id"
            : $"SELECT {key} FROM {table} WHERE {key} = @id FOR UPDATE NOWAIT";
        command.Parameters.Add(property.GetRelationalTypeMapping().CreateParameter(command, "@id", id));
        try
        {
            if (await command.ExecuteScalarAsync(token) is null or DBNull)
                throw new InvalidOperationException("Native lock witnesses require an existing visible row.");
            return false;
        }
        catch (PostgresException exception) when (exception.SqlState == "55P03") { return true; }
        catch (SqlException exception) when (exception.Number == 1222) { return true; }
        catch (MySqlException exception) when (exception.Number is 3572 or 1205) { return true; }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    private static async Task<Scope> SeedAsync(CancellationToken token)
    {
        var fixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        await fixture.PrepareAsync();
        await using var seed = fixture.CreateSystemContext();
        Console.WriteLine($"Writer authority witness provider: {fixture.Provider}; {seed.Database.ProviderName}.");
        var first = Tenant();
        var second = Tenant();
        seed.AddRange(first, second, new EventDiscoveryRevision
        {
            Id = Guid.CreateVersion7(),
            TenantId = first.Id
        });
        await seed.SaveChangesAsync(token);
        var relational = seed.GetService<IDbContextOptions>().Extensions
            .OfType<RelationalOptionsExtension>().Single();
        return new(fixture, first.Id, second.Id, seed.Model, seed.GetService<ISqlGenerationHelper>(),
            DbProviderFactories.GetFactory(seed.Database.GetDbConnection())
                ?? throw new InvalidOperationException("Native lock witnesses require a provider connection factory."),
            relational.ConnectionString
                ?? throw new InvalidOperationException("Native lock witnesses require configured connection authority."));

        static Tenant Tenant() => new()
        {
            Id = Guid.CreateVersion7(),
            Slug = $"writer-witness-{Guid.CreateVersion7():N}",
            FullName = "Writer witness",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
    }

    private sealed record Scope(
        PrimaryDatabaseProviderBehaviorFixture Fixture, Guid TenantId, Guid OtherTenantId,
        IModel Model, ISqlGenerationHelper Sql, DbProviderFactory ConnectionFactory, string ConnectionString)
    {
        public ExploreDbContext Open(params IInterceptor[] interceptors) =>
            Fixture.CreateTenantContext(TenantId, interceptors);
    }

    public sealed class RequiresRowLockProviderAttribute()
        : SkipAttribute("The competing-source witness requires a native row-locking provider, not SQLite.")
    {
        public override Task<bool> ShouldSkip(TestRegisteredContext _) =>
            Task.FromResult(string.Equals(Environment.GetEnvironmentVariable("Database__Provider"),
                "Sqlite", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class BeforeNativeCommand(
        Type entityType, Func<Task> witness, bool lockingOnly = false) : DbCommandInterceptor
    {
        public bool Observed { get; private set; }

        private async Task ObserveAsync(DbCommand command, CommandEventData eventData)
        {
            if (Observed)
                return;
            var context = eventData.Context!;
            var entity = context.Model.FindEntityType(entityType)!;
            var sql = context.GetService<ISqlGenerationHelper>();
            string table = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            if (!command.CommandText.Contains(table, StringComparison.Ordinal))
                return;
            if (lockingOnly && !command.CommandText.StartsWith($"UPDATE {table}", StringComparison.Ordinal)
                && !command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                && !command.CommandText.Contains("UPDLOCK", StringComparison.Ordinal))
                return;
            Observed = true;
            await witness();
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await ObserveAsync(command, eventData);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await ObserveAsync(command, eventData);
            return result;
        }
    }
}
