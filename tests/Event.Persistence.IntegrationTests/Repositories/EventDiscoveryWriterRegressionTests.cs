using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class EventDiscoveryWriterRegressionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Option_mutation_requires_a_resolved_exact_owner(bool hiddenOwner)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var entity = await fixture.SeedEventAsync(published: true);
        var definition = new EventCustomPropertyDefinition
        {
            Id = Guid.CreateVersion7(),
            EventId = entity.Id,
            TenantId = fixture.TenantId,
            Namespace = "test",
            Key = "owner",
            DisplayName = "Owner",
            PropertyType = PropertyType.Text
        };
        var option = new EventCustomPropertyOption
        {
            Id = Guid.CreateVersion7(),
            EventCustomPropertyDefinitionId = definition.Id,
            Namespace = "test",
            Key = "option",
            DisplayName = "Option",
            Value = "before"
        };
        fixture.Context.AddRange(definition, option);
        await fixture.Context.SaveChangesAsync();
        long before = await EpochAsync(fixture.Context, fixture.TenantId);
        await using (var writer = Open(fixture))
        {
            if (hiddenOwner)
                writer.TenantContext = new TenantScope(Guid.CreateVersion7());
            (await writer.EventCustomPropertyOptions.SingleAsync(row => row.Id == option.Id)).Value = "after";
            if (hiddenOwner)
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer.SaveChangesAsync());
            else
                await writer.SaveChangesAsync();
        }
        await using var reader = Open(fixture);
        await Assert.That((await reader.EventCustomPropertyOptions.SingleAsync(row => row.Id == option.Id)).Value)
            .IsEqualTo(hiddenOwner ? "before" : "after");
        await Assert.That(await EpochAsync(reader, fixture.TenantId)).IsEqualTo(before + (hiddenOwner ? 0 : 1));
    }

    [Test]
    [Arguments(typeof(Madhab))]
    [Arguments(typeof(Language))]
    [Arguments(typeof(EventProvenanceType))]
    [Arguments(typeof(ParticipationHandlingMode))]
    [Arguments(typeof(AdvanceRegistrationObligation))]
    [Arguments(typeof(IdentityAccessMode))]
    [Arguments(typeof(EventPublicActionKind))]
    [Arguments(typeof(EventPublicActionHealthState))]
    [Arguments(typeof(EventSessionKind))]
    [Arguments(typeof(RegistrationMode))]
    public async Task Public_lookup_mutation_advances_all_tenants(Type lookupType)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        Guid otherId = Guid.CreateVersion7();
        fixture.Context.Tenants.Add(new Tenant
        {
            Id = otherId,
            Slug = $"lookup-{otherId:N}",
            FullName = "Other",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        });
        fixture.Context.Set<EventDiscoveryRevision>().Add(new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = otherId
        });
        await fixture.Context.SaveChangesAsync();
        long before = await EpochAsync(fixture.Context, fixture.TenantId);
        object lookup = await fixture.Context.FindAsync(lookupType, 1)
            ?? throw new InvalidOperationException($"The fixture must seed {lookupType.Name}.");
        fixture.Context.Entry(lookup).Property("FullName").CurrentValue = "Revised public label";
        await fixture.Context.SaveChangesAsync();
        await using var reader = Open(fixture);
        await Assert.That(await EpochAsync(reader, fixture.TenantId)).IsEqualTo(before + 1);
        await Assert.That(await reader.Set<EventDiscoveryRevision>().IgnoreQueryFilters()
            .Where(row => row.TenantId == otherId).Select(row => row.DisclosureEpoch).SingleAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Detached_ownership_change_fences_anchors_and_rechecks_stored_keys(bool changeBeforeReread)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var entity = await fixture.SeedEventAsync(published: true);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii { Email = "new-owner@example.test", FirstName = "New", LastName = "Owner" }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "New owner" }
        };
        fixture.Context.AddRange(user, actor);
        await fixture.Context.SaveChangesAsync();
        long before = await EpochAsync(fixture.Context, fixture.TenantId);
        var ordering = new ActorsBeforeEvent([fixture.ActorId, actor.Id]);
        await using (var writer = Open(fixture, ordering,
                         new ChangeOrganizerBeforeClassification(entity.Id, actor.Id, changeBeforeReread)))
        {
            var detached = await writer.Events.AsNoTracking().SingleAsync(row => row.Id == entity.Id);
            detached.ActorId = actor.Id;
            if (changeBeforeReread)
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                    () => new EventRepository(writer).Update(detached));
            else
                await new EventRepository(writer).Update(detached);
        }
        await Assert.That(ordering.ObservedEventWrite).IsTrue();
        await using var reader = Open(fixture);
        var committed = await reader.Events.SingleAsync(row => row.Id == entity.Id);
        await Assert.That(committed.ActorId).IsEqualTo(changeBeforeReread ? fixture.ActorId : actor.Id);
        await Assert.That(committed.OrganizerActorId).IsEqualTo(fixture.ActorId);
        await Assert.That(await EpochAsync(reader, fixture.TenantId)).IsEqualTo(before + (changeBeforeReread ? 0 : 1));
    }

    [Test]
    public async Task Settlement_replay_reloads_source_and_claim_after_terminal_failure()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        DateTime now = new(2028, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        Guid sourceId = Guid.CreateVersion7();
        var record = new AtprotoRecord
        {
            Id = Guid.CreateVersion7(),
            Did = "did:plc:terminal-replay",
            Collection = "community.lexicon.calendar.event",
            RecordKey = "replay",
            Uri = "at://did:plc:terminal-replay/community.lexicon.calendar.event/replay",
            Cid = "old-cid",
            RecordJson = "{\"name\":\"old\"}",
            RecordHash = new string('a', 64),
            Direction = AtprotoRecordDirection.Outbound,
            Provenance = AtprotoRecordProvenance.LocalLifecycle,
            UpdatedAt = now,
            SourceVersion = 1
        };
        var outbox = new PdsSyncOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            UserId = fixture.UserId,
            Did = record.Did,
            Collection = record.Collection,
            RecordKey = record.RecordKey,
            Operation = PdsSyncOperation.Update,
            ExpectedCid = record.Cid,
            Payload = "{\"name\":\"new\"}",
            PayloadHash = new string('b', 64),
            IdempotencyKey = Guid.CreateVersion7().ToString("N"),
            PdsHost = "https://pds.example.test",
            SourceEntityType = "Event",
            SourceEntityId = sourceId,
            SourceVersion = Guid.CreateVersion7(),
            AtprotoRecordId = record.Id,
            Status = PdsSyncStatus.Processing,
            CreatedAt = now,
            MaxRetries = 5,
            LeaseOwner = "terminal-replay",
            LeaseToken = Guid.CreateVersion7(),
            LeaseFence = 1,
            LeaseExpiresAt = now.AddMinutes(5)
        };
        fixture.Context.AddRange(record, outbox, new AtprotoOutboundRecordOwnership
        {
            AtprotoRecordId = record.Id,
            TenantId = fixture.TenantId,
            UserId = fixture.UserId,
            SourceEntityType = "Event",
            SourceEntityId = sourceId,
            SourceVersion = Guid.CreateVersion7(),
            CreatedAt = now,
            UpdatedAt = now
        }, new AtprotoRecordTenantPresentation
        {
            AtprotoRecordId = record.Id,
            TenantId = fixture.TenantId,
            IsVisible = false,
            SourceVersion = 1,
            EvaluatedAt = now
        });
        await fixture.Context.SaveChangesAsync();
        long before = await EpochAsync(fixture.Context, fixture.TenantId);

        var failure = new FailFirstTerminalWrite();
        await using (var writer = Open(fixture, failure))
        {
            bool settled = await new PdsSyncOutboxRepository(writer).TrySettleAsync(
                new PdsSyncClaim(outbox.Id, fixture.TenantId, fixture.UserId, outbox.LeaseToken!.Value, 1),
                record.Uri, "new-cid", now.AddSeconds(1));
            await Assert.That(settled).IsTrue();
        }
        await Assert.That(failure.Failures).IsEqualTo(1);
        await using var reader = Open(fixture);
        var committed = await reader.PdsSyncOutbox.SingleAsync(row => row.Id == outbox.Id);
        await Assert.That(committed.Status).IsEqualTo(PdsSyncStatus.Completed);
        await Assert.That(committed.LeaseToken).IsNull();
        await Assert.That(committed.SettledCid).IsEqualTo("new-cid");
        await Assert.That((await reader.AtprotoRecords.SingleAsync(row => row.Id == record.Id)).Cid)
            .IsEqualTo("new-cid");
        await Assert.That((await reader.AtprotoRecordTenantPresentations
            .SingleAsync(row => row.AtprotoRecordId == record.Id)).IsVisible).IsTrue();
        await Assert.That(await EpochAsync(reader, fixture.TenantId)).IsEqualTo(before + 1);
    }

    private static ExploreDbContext Open(
        EventVisitorCapabilitySqliteFixture fixture, params IInterceptor[] interceptors) =>
        new(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = fixture.DatabasePath,
                Pooling = false
            }.ToString())
            .UseSnakeCaseNamingConvention()
            .UseModel(fixture.Context.Model)
            .ReplaceService<IExecutionStrategyFactory, RetryOnceFactory>()
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance,
                SqliteProjectionLockTransactionInterceptor.Instance)
            .AddInterceptors(interceptors).Options)
        { TenantContext = fixture };

    private static Task<long> EpochAsync(ExploreDbContext context, Guid tenant) =>
        context.Set<EventDiscoveryRevision>().AsNoTracking()
            .Where(row => row.TenantId == tenant).Select(row => row.DisclosureEpoch).SingleAsync();

    private sealed record TenantScope(Guid TenantId) : ITenantContext;

    private sealed class RetryOnceFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryOnceStrategy(dependencies.CurrentContext.Context);
    }

    private sealed class RetryOnceStrategy(DbContext context)
        : ExecutionStrategy(context, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TerminalWriteFailure;
    }

    private sealed class TerminalWriteFailure : Exception;

    private sealed class ChangeOrganizerBeforeClassification(Guid eventId, Guid organizerId, bool enabled)
        : DbCommandInterceptor
    {
        private bool _changed;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (enabled && !_changed && IsUpdateOf<Explore.Domain.Event>(command, eventData))
            {
                _changed = true;
                // A deterministic database-state change between discovery and the
                // held reread. Provider witnesses separately prove native exclusion.
                await ((ExploreDbContext)eventData.Context!).Events.Where(row => row.Id == eventId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.OrganizerActorId, organizerId),
                        cancellationToken);
            }
            return result;
        }
    }

    private sealed class ActorsBeforeEvent(Guid[] actors) : DbCommandInterceptor
    {
        private readonly HashSet<Guid> _fenced = [];
        public bool ObservedEventWrite { get; private set; }

        private void Observe(DbCommand command, CommandEventData eventData)
        {
            if (IsUpdateOf<Actor>(command, eventData))
                foreach (DbParameter parameter in command.Parameters)
                    if (Guid.TryParse(parameter.Value?.ToString(), out Guid id))
                        _fenced.Add(id);
            if (!IsUpdateOf<Explore.Domain.Event>(command, eventData))
                return;
            ObservedEventWrite = true;
            if (actors.Any(id => !_fenced.Contains(id)))
                throw new InvalidOperationException("Event classification/write preceded old and new Actor fences.");
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command, eventData);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailFirstTerminalWrite : DbCommandInterceptor
    {
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            // Final advancement binds disclosure, identity and tenant. The earlier
            // native row fence must not trigger this post-source rollback witness.
            if (Failures == 0 && command.Parameters.Count == 3
                && IsUpdateOf<EventDiscoveryRevision>(command, eventData))
            {
                Failures++;
                throw new TerminalWriteFailure();
            }
            return ValueTask.FromResult(result);
        }
    }

    private static bool IsUpdateOf<TEntity>(DbCommand command, CommandEventData eventData)
    {
        var context = eventData.Context!;
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        string table = context.GetService<ISqlGenerationHelper>()
            .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        return command.CommandText.StartsWith($"UPDATE {table} ", StringComparison.Ordinal);
    }
}
