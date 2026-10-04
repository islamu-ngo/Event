using Explore.Application.Contracts.Infrastructure;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.Settings;
using Explore.Domain.Services.Scheduling;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class EventDiscoveryDisclosureMutationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Identity_epoch_is_compared_after_graph_audit_and_outbox_writes(bool staleRevision)
    {
        await using var store = await Store.CreateAsync();
        var member = EventDiscoveryIdentity.Create(store.TenantId, EventDiscoverySourceKind.LocalEvent,
            Guid.CreateVersion7().ToString("D"));
        var primary = EventDiscoveryIdentity.Create(store.TenantId, EventDiscoverySourceKind.LocalEvent,
            Guid.CreateVersion7().ToString("D"));
        await using (var seed = store.Open())
        {
            seed.AddRange(member, primary);
            (await seed.Set<EventDiscoveryRevision>().SingleAsync(row => row.TenantId == store.TenantId))
                .IdentityEpoch = staleRevision ? 1 : 0;
            await seed.SaveChangesAsync();
        }
        var ordering = new DecisionBeforeEpoch();
        await using (var writer = store.Open(ordering))
        {
            Task operation = new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
            {
                var identities = new EventDiscoveryIdentityRepository(writer);
                await identities.AcquireFenceAsync(store.TenantId, [member.Id, primary.Id], token);
                await identities.ReviewAsync(store.TenantId, member.Id, primary.Id, 0,
                    Guid.CreateVersion7(), "same_event", DateTime.UtcNow, token);
                writer.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.CreateVersion7(), TenantId = store.TenantId, Tenant = null!,
                    EntityType = nameof(EventDiscoveryIdentity), EntityId = member.Id.ToString("D"),
                    Action = "identity-review", Timestamp = DateTime.UtcNow
                });
                writer.Set<OutboxMessage>().Add(new OutboxMessage
                {
                    Id = Guid.CreateVersion7(), AggregateType = nameof(EventDiscoveryIdentity),
                    AggregateId = member.Id, EventType = "identity-review", Payload = "{}",
                    Status = OutboxMessageStatus.Pending, CreatedAt = DateTime.UtcNow, MaxRetries = 5
                });
                await writer.SaveChangesAsync(token);
                return true;
            });
            if (staleRevision)
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await operation);
            else
                await operation;
        }
        await Assert.That(ordering.SawAudit).IsTrue();
        await Assert.That(ordering.SawOutbox).IsTrue();
        await Assert.That(ordering.SawEpoch).IsTrue();
        await using var reader = store.Open();
        await Assert.That(await reader.Set<EventDiscoveryAlias>().CountAsync()).IsEqualTo(staleRevision ? 0 : 1);
        await Assert.That(await reader.AuditLogs.CountAsync()).IsEqualTo(staleRevision ? 0 : 1);
        await Assert.That(await reader.Set<OutboxMessage>().CountAsync()).IsEqualTo(staleRevision ? 0 : 1);
        await Assert.That((await reader.Set<EventDiscoveryRevision>()
            .SingleAsync(row => row.TenantId == store.TenantId)).IdentityEpoch).IsEqualTo(1);
    }

    [Test]
    public async Task Audit_write_after_terminal_epoch_is_rejected()
    {
        await using var store = await Store.CreateAsync();
        await using var writer = store.Open();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
            {
                await new EventDiscoveryDisclosureRepository(writer).AcquireCurrentAsync(store.TenantId, token);
                writer.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.CreateVersion7(), TenantId = store.TenantId, Tenant = null!,
                    EntityType = "Event", EntityId = Guid.CreateVersion7().ToString("D"),
                    Action = "late", Timestamp = DateTime.UtcNow
                });
                await writer.SaveChangesAsync(token);
                return true;
            }));
        await using var reader = store.Open();
        await Assert.That(await reader.AuditLogs.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Every_fanout_parent_is_fenced_before_any_revision_command()
    {
        await using var store = await Store.CreateAsync();
        await using (var seed = store.Open())
            await seed.Set<EventDiscoveryRevision>().IgnoreQueryFilters()
                .Where(row => row.TenantId == store.OtherTenantId).ExecuteDeleteAsync();
        var ordering = new ParentBeforeEpoch([store.TenantId, store.OtherTenantId]);
        await using (var writer = store.Open(ordering))
            await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
            {
                await new EventDiscoveryDisclosureRepository(writer)
                    .AdvanceAsync([store.OtherTenantId, store.TenantId], token);
                return true;
            });
        await Assert.That(ordering.ObservedEpoch).IsTrue();
        await using var reader = store.Open();
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(1);
        await Assert.That(await EpochAsync(reader, store.OtherTenantId)).IsEqualTo(1);
    }

    [Test]
    public async Task Existing_revision_release_fences_its_parent_before_reading_revision()
    {
        await using var store = await Store.CreateAsync();
        var ordering = new ParentBeforeEpoch([store.TenantId]);
        await using var reader = store.Open(ordering);
        var revision = await new EfCoreUnitOfWork(reader).ExecuteSerializableAsync(
            token => new EventDiscoveryDisclosureRepository(reader).AcquireCurrentAsync(store.TenantId, token));
        await Assert.That(ordering.ObservedEpoch).IsTrue();
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(0);
    }

    [Test]
    public async Task Identity_fence_covers_audit_parent_even_when_revision_exists()
    {
        await using var store = await Store.CreateAsync();
        var ordering = new ParentBeforeEpoch([store.TenantId]);
        await using (var reviewer = store.Open(ordering))
            await new EfCoreUnitOfWork(reviewer).ExecuteSerializableAsync(async token =>
            {
                await new EventDiscoveryIdentityRepository(reviewer)
                    .AcquireFenceAsync(store.TenantId, [], token);
                reviewer.AuditLogs.Add(new AuditLog
                {
                    Id = Guid.CreateVersion7(), TenantId = store.TenantId, Tenant = null!,
                    EntityType = nameof(EventDiscoveryIdentity), EntityId = Guid.CreateVersion7().ToString("D"),
                    Action = "different-offering", Timestamp = DateTime.UtcNow
                });
                await reviewer.SaveChangesAsync(token);
                return true;
            });
        await Assert.That(ordering.ObservedEpoch).IsTrue();
        await using var reader = store.Open();
        await Assert.That(await reader.AuditLogs.CountAsync()).IsEqualTo(1);
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Standalone_tracked_mutation_commits_its_disclosure_epoch(bool erasureSave)
    {
        await using var store = await Store.CreateAsync();
        await using (var writer = store.Open())
        {
            Tenant tenant = await writer.Tenants.SingleAsync(value => value.Id == store.TenantId);
            tenant.FullName = "Changed public identity";
            if (erasureSave)
                await new UserLocationPrivacyErasureRepository(writer).SaveChangesAsync([], CancellationToken.None);
            else
                await new TenantRepository(writer).Update(tenant);
        }

        await using var reader = store.Open();
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(1);
        await Assert.That((await reader.Tenants.SingleAsync(value => value.Id == store.TenantId)).FullName)
            .IsEqualTo("Changed public identity");
    }

    [Test]
    public async Task Explicit_advancement_and_native_commit_share_one_finalization()
    {
        await using var store = await Store.CreateAsync();
        await using (var writer = store.Open())
            await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
            {
                (await writer.Tenants.SingleAsync(row => row.Id == store.TenantId, token))
                    .FullName = "Explicit terminal advancement";
                await writer.SaveChangesAsync(token);
                await new EventDiscoveryDisclosureRepository(writer).AdvanceAsync([store.TenantId], token);
                return true;
            });
        await using var reader = store.Open();
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(1);
    }

    [Test]
    public async Task Multiple_source_saves_do_not_acquire_or_advance_the_epoch_early()
    {
        await using var store = await Store.CreateAsync();
        await using var writer = store.Open();
        await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
        {
            Tenant tenant = await writer.Tenants.SingleAsync(value => value.Id == store.TenantId, token);
            tenant.FullName = "First change";
            await writer.SaveChangesAsync(token);
            await Assert.That(await EpochAsync(writer, store.TenantId)).IsEqualTo(0);
            tenant.FullName = "Final change";
            await writer.SaveChangesAsync(token);
            await Assert.That(await EpochAsync(writer, store.TenantId)).IsEqualTo(0);
            return true;
        });

        await using var reader = store.Open();
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(1);
        await Assert.That((await reader.Tenants.SingleAsync(value => value.Id == store.TenantId)).FullName)
            .IsEqualTo("Final change");
    }

    [Test]
    public async Task Bulk_lifecycle_write_cannot_commit_without_the_epoch()
    {
        await using var store = await Store.CreateAsync();
        await using (var writer = store.Open())
        {
            bool changed = await new TenantRepository(writer).TryTransitionStatusAsync(
                store.TenantId, (int)TenantStatusEnum.Active, (int)TenantStatusEnum.Suspended,
                DateTime.UtcNow, Guid.CreateVersion7());
            await Assert.That(changed).IsTrue();
        }
        await using var reader = store.Open();
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(1);
    }

    [Test]
    public async Task Missing_bulk_target_returns_false_without_creating_a_revision()
    {
        await using var store = await Store.CreateAsync();
        Guid missingId = Guid.CreateVersion7();
        await using (var writer = store.Open())
        {
            await Assert.That(await new TenantRepository(writer).TryTransitionStatusAsync(
                missingId, (int)TenantStatusEnum.Active, (int)TenantStatusEnum.Suspended,
                DateTime.UtcNow, Guid.CreateVersion7())).IsFalse();
        }
        await using var reader = store.Open();
        await Assert.That(await reader.Set<EventDiscoveryRevision>().IgnoreQueryFilters()
            .AnyAsync(value => value.TenantId == missingId)).IsFalse();
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(0);
    }

    [Test]
    public async Task Rollback_discards_source_changes_and_pending_epoch_tenants()
    {
        await using var store = await Store.CreateAsync();
        await using var writer = store.Open();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync<bool>(async token =>
            {
                Tenant tenant = await writer.Tenants.SingleAsync(value => value.Id == store.TenantId, token);
                tenant.FullName = "Rolled back";
                await writer.SaveChangesAsync(token);
                throw new InvalidOperationException("rollback");
            }));
        await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(_ => Task.FromResult(true));

        await using var reader = store.Open();
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(0);
        await Assert.That((await reader.Tenants.SingleAsync(value => value.Id == store.TenantId)).FullName)
            .IsEqualTo("Original");
    }

    [Test]
    public async Task Cross_tenant_tracked_changes_advance_each_exact_tenant_once()
    {
        await using var store = await Store.CreateAsync();
        await using (var writer = store.Open())
        {
            foreach (Tenant tenant in await writer.Tenants.ToListAsync())
                tenant.FullName = "Both changed";
            await writer.SaveChangesAsync();
        }
        await using var reader = store.Open();
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(1);
        await Assert.That(await EpochAsync(reader, store.OtherTenantId)).IsEqualTo(1);
    }

    [Test]
    public async Task Direct_native_transaction_commit_cannot_bypass_terminal_advancement()
    {
        await using var store = await Store.CreateAsync();
        await using (var writer = store.Open())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync();
            Tenant tenant = await writer.Tenants.SingleAsync(value => value.Id == store.TenantId);
            tenant.FullName = "Native owner";
            await writer.SaveChangesAsync();
            await Assert.That(await EpochAsync(writer, store.TenantId)).IsEqualTo(0);
            await transaction.CommitAsync();
        }
        await using var reader = store.Open();
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(1);
    }

    [Test]
    public async Task Native_reader_observes_only_the_committed_source_epoch_pair()
    {
        await using var store = await Store.CreateAsync();
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var writer = store.Open();
        Task operation = new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
        {
            Tenant tenant = await writer.Tenants.SingleAsync(value => value.Id == store.TenantId, token);
            tenant.FullName = "Committed";
            await writer.SaveChangesAsync(token);
            saved.TrySetResult();
            await release.Task.WaitAsync(Deadline, token);
            return true;
        });
        try
        {
            await saved.Task.WaitAsync(Deadline);
            await using var reader = store.Open();
            await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(0);
            await Assert.That((await reader.Tenants.SingleAsync(value => value.Id == store.TenantId)).FullName)
                .IsEqualTo("Original");
        }
        finally
        {
            release.TrySetResult();
        }
        await operation.WaitAsync(Deadline);
        await using var committed = store.Open();
        await Assert.That(await EpochAsync(committed, store.TenantId)).IsEqualTo(1);
        await Assert.That((await committed.Tenants.SingleAsync(value => value.Id == store.TenantId)).FullName)
            .IsEqualTo("Committed");
    }

    [Test]
    public async Task Release_fence_reads_the_committed_epoch_not_a_previously_tracked_revision()
    {
        await using var store = await Store.CreateAsync();
        await using var reader = store.Open();
        EventDiscoveryRevision tracked = await reader.Set<EventDiscoveryRevision>()
            .SingleAsync(value => value.TenantId == store.TenantId);
        await using (var writer = store.Open())
        {
            Tenant tenant = await writer.Tenants.SingleAsync(value => value.Id == store.TenantId);
            tenant.FullName = "Revoked projection";
            await writer.SaveChangesAsync();
        }
        long epoch = await new EfCoreUnitOfWork(reader).ExecuteReadCommittedAsync(async token =>
            (await new EventDiscoveryDisclosureRepository(reader).AcquireCurrentAsync(store.TenantId, token)).DisclosureEpoch);
        await Assert.That(tracked.DisclosureEpoch).IsEqualTo(0);
        await Assert.That(epoch).IsEqualTo(1);
    }

    [Test]
    public async Task Source_writes_after_the_release_fence_are_rejected_and_rolled_back()
    {
        await using var store = await Store.CreateAsync();
        await using var writer = store.Open();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
            {
                await new EventDiscoveryDisclosureRepository(writer).AcquireCurrentAsync(store.TenantId, token);
                Tenant tenant = await writer.Tenants.SingleAsync(value => value.Id == store.TenantId, token);
                tenant.FullName = "Must not commit";
                await writer.SaveChangesAsync(token);
                return true;
            }));
        await using var reader = store.Open();
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(0);
        await Assert.That((await reader.Tenants.SingleAsync(value => value.Id == store.TenantId)).FullName)
            .IsEqualTo("Original");
    }

    [Test]
    public async Task Terminal_read_fence_cannot_skip_already_pending_source_advancement()
    {
        await using var store = await Store.CreateAsync();
        await using (var writer = store.Open())
        {
            await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
            {
                Tenant tenant = await writer.Tenants.SingleAsync(value => value.Id == store.TenantId, token);
                tenant.FullName = "Pending";
                await writer.SaveChangesAsync(token);
                await new EventDiscoveryDisclosureRepository(writer).AcquireCurrentAsync(store.TenantId, token);
                return true;
            });
        }
        await using var reader = store.Open();
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(1);
    }

    [Test]
    public async Task Terminal_caught_bulk_rejection_cannot_commit_an_unversioned_source_write()
    {
        await using var store = await Store.CreateAsync();
        await using (var writer = store.Open())
        {
            await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async token =>
            {
                await new EventDiscoveryDisclosureRepository(writer).AcquireCurrentAsync(store.TenantId, token);
                try
                {
                    await new TenantRepository(writer).TryTransitionStatusAsync(
                        store.TenantId, (int)TenantStatusEnum.Active, (int)TenantStatusEnum.Suspended,
                        DateTime.UtcNow, Guid.CreateVersion7(), token);
                }
                catch (InvalidOperationException)
                {
                    // A command may convert a rejected mutation into a typed failure.
                }
                return false;
            });
        }
        await using var reader = store.Open();
        await Assert.That((await reader.Tenants.SingleAsync(value => value.Id == store.TenantId)).TenantStatusId)
            .IsEqualTo((int)TenantStatusEnum.Active);
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(0);
    }

    [Test]
    public async Task Terminal_failed_increment_cannot_be_caught_and_then_committed()
    {
        await using var store = await Store.CreateAsync();
        await using (var seed = store.Open())
        {
            (await seed.Set<EventDiscoveryRevision>().SingleAsync(value => value.TenantId == store.TenantId))
                .DisclosureEpoch = long.MaxValue;
            await seed.SaveChangesAsync();
        }
        await using (var writer = store.Open())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync();
            (await writer.Tenants.SingleAsync(value => value.Id == store.TenantId)).FullName = "Must roll back";
            await writer.SaveChangesAsync();
            await Assert.ThrowsAsync<OverflowException>(() => writer.FlushDisclosureAsync(CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await transaction.CommitAsync());
        }
        await using var reader = store.Open();
        await Assert.That((await reader.Tenants.SingleAsync(value => value.Id == store.TenantId)).FullName)
            .IsEqualTo("Original");
    }

    [Test]
    public async Task Instance_policy_changes_advance_every_tenant_including_without_an_override()
    {
        await using var store = await Store.CreateAsync();
        await using (var writer = store.Open())
        {
            writer.SystemSettings.Add(Policy());
            await writer.SaveChangesAsync();
        }
        await using var reader = store.Open();
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(1);
        await Assert.That(await EpochAsync(reader, store.OtherTenantId)).IsEqualTo(1);
    }

    [Test]
    public async Task Global_fanout_over_the_bound_rolls_back_instead_of_invalidating_a_prefix()
    {
        await using var store = await Store.CreateAsync();
        await using (var seed = store.Open())
        {
            for (int index = 2; index <= Explore.Persistence.Services.EventDiscoveryDisclosureMutationScope.MaximumAffectedTenants; index++)
            {
                Guid id = Guid.CreateVersion7();
                seed.Tenants.Add(new Tenant
                {
                    Id = id, Slug = $"bounded-{id:N}", FullName = "Bounded",
                    TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
                });
            }
            await seed.SaveChangesAsync();
        }
        await using (var writer = store.Open())
        {
            writer.SystemSettings.Add(Policy());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer.SaveChangesAsync());
        }
        await using var reader = store.Open();
        await Assert.That(await reader.SystemSettings.AnyAsync()).IsFalse();
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(0);
        await Assert.That(await EpochAsync(reader, store.OtherTenantId)).IsEqualTo(0);
    }

    private static SystemSetting Policy() => new()
    {
        Id = Guid.CreateVersion7(), SettingKey = "federation.atproto_events_enabled",
        Value = "false", ValueType = SettingValueType.Boolean
    };

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Global_source_changes_include_other_and_suppressed_dependent_tenants(bool tombstone)
    {
        await using var store = await Store.CreateAsync();
        Guid recordId = Guid.CreateVersion7();
        await using (var seed = store.Open())
        {
            seed.AtprotoRecords.Add(new AtprotoRecord
            {
                Id = recordId, Did = "did:plc:disclosuretest", Collection = "community.lexicon.calendar.event",
                RecordKey = "source", Direction = AtprotoRecordDirection.Inbound,
                Provenance = AtprotoRecordProvenance.Jetstream, SourceVersion = 1,
                UpdatedAt = DateTime.UtcNow
            });
            seed.AtprotoEventProjections.Add(new AtprotoEventProjection
            {
                AtprotoRecordId = recordId, Name = "Public source", SourceVersion = 1,
                SourceUrl = "https://example.test/current-source", CreatedAt = DateTimeOffset.UtcNow,
                MaterializedAt = DateTime.UtcNow
            });
            foreach (Guid tenantId in new[] { store.TenantId, store.OtherTenantId })
                seed.AtprotoRecordTenantPresentations.Add(new AtprotoRecordTenantPresentation
                {
                    TenantId = tenantId, AtprotoRecordId = recordId, SourceVersion = 1,
                    IsVisible = tenantId == store.TenantId, EvaluatedAt = DateTime.UtcNow
                });
            await seed.SaveChangesAsync();
        }

        await using (var writer = store.Open())
        {
            if (tombstone)
                (await writer.AtprotoRecords.SingleAsync(value => value.Id == recordId)).TombstonedAt = DateTime.UtcNow;
            else
                (await writer.AtprotoEventProjections.SingleAsync(value => value.AtprotoRecordId == recordId)).SourceUrl = null;
            await writer.SaveChangesAsync();
        }

        await using var reader = store.Open();
        await Assert.That(await EpochAsync(reader, store.TenantId)).IsEqualTo(2);
        await Assert.That(await EpochAsync(reader, store.OtherTenantId)).IsEqualTo(2);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Rank_only_views_preserve_disclosure_for_tracked_detached_and_bulk_writes(int writeKind)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        Explore.Domain.Event entity = AddPublishedEvent(fixture);
        await fixture.Context.SaveChangesAsync();
        long before = await EpochAsync(fixture.Context, fixture.TenantId);
        if (writeKind == 2)
        {
            await fixture.Context.Events.Where(value => value.Id == entity.Id).ExecuteUpdateAsync(
                setters => setters.SetProperty(value => value.TotalViews, value => value.TotalViews + 1));
        }
        else if (writeKind == 1)
        {
            fixture.Context.ChangeTracker.Clear();
            var repository = new EventRepository(fixture.Context);
            Explore.Domain.Event detached = (await repository.GetById(entity.Id))!;
            detached.TotalViews++;
            await repository.Update(detached);
        }
        else
        {
            entity.TotalViews++;
            await fixture.Context.SaveChangesAsync();
        }
        await Assert.That(await EpochAsync(fixture.Context, fixture.TenantId)).IsEqualTo(before);
        await Assert.That(await fixture.Context.Events.AsNoTracking().Where(value => value.Id == entity.Id)
            .Select(value => value.TotalViews).SingleAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Rank_changes_mixed_with_title_or_session_time_still_advance_disclosure(bool sessionTime)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        Explore.Domain.Event entity = AddPublishedEvent(fixture);
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(), TenantId = fixture.TenantId, Tenant = null!,
            EventId = entity.Id, Event = entity,
            StartTime = new DateTimeOffset(2040, 6, 1, 12, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2040, 6, 1, 13, 0, 0, TimeSpan.Zero),
            EndTimeType = SessionEndTimeType.Fixed
        };
        session.ReprojectLocalTimes(entity.GetEffectiveScheduleTimeZoneId(), new EventScheduleProjectionCalculator());
        fixture.Context.EventSessions.Add(session);
        await fixture.Context.SaveChangesAsync();
        long before = await EpochAsync(fixture.Context, fixture.TenantId);
        entity.TotalViews++;
        if (sessionTime)
        {
            session.StartTime = session.StartTime!.Value.AddMinutes(10);
            session.ReprojectLocalTimes(entity.GetEffectiveScheduleTimeZoneId(), new EventScheduleProjectionCalculator());
        }
        else
        {
            entity.Title = "Changed matching title";
        }
        await fixture.Context.SaveChangesAsync();
        await Assert.That(await EpochAsync(fixture.Context, fixture.TenantId)).IsEqualTo(before + 1);
    }

    private static Explore.Domain.Event AddPublishedEvent(EventVisitorCapabilitySqliteFixture fixture)
    {
        var entity = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(), TenantId = fixture.TenantId, Tenant = null!,
            Title = "Ranked event", PublicCode = Guid.CreateVersion7().ToString("N"),
            ActorId = fixture.ActorId, Actor = null!, OrganizerActorId = fixture.ActorId,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!,
            Timezone = "Europe/Brussels", TotalViews = 0, CreatedAt = DateTime.UtcNow
        };
        fixture.Context.Events.Add(entity);
        return entity;
    }

    private static Task<long> EpochAsync(ExploreDbContext context, Guid tenantId) =>
        context.Set<EventDiscoveryRevision>().AsNoTracking().IgnoreQueryFilters()
            .Where(value => value.TenantId == tenantId)
            .Select(value => value.DisclosureEpoch).SingleAsync();

    private sealed record TenantScope(Guid TenantId) : ITenantContext;

    private sealed class Store : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(), $"discovery-disclosure-{Guid.CreateVersion7():N}.db");
        private DbContextOptions<ExploreDbContext> _options = null!;
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid OtherTenantId { get; } = Guid.CreateVersion7();

        public ExploreDbContext Open(params IInterceptor[] interceptors) =>
            new(new DbContextOptionsBuilder<ExploreDbContext>(_options).AddInterceptors(interceptors).Options)
                { TenantContext = new TenantScope(TenantId) };

        public static async Task<Store> CreateAsync()
        {
            var store = new Store();
            store._options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = store._path, DefaultTimeout = 5, Pooling = false
                }.ToString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance,
                    SqliteProjectionLockTransactionInterceptor.Instance)
                .Options;
            await using var seed = store.Open();
            await seed.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(seed, CancellationToken.None);
            store._options = TestDbContextOptions.Create(store._options).UseModel(seed.Model).Options;
            seed.Set<TenantStatus>().AddRange(
                new TenantStatus
                {
                    Id = (int)TenantStatusEnum.Active, MasterCode = "Active",
                    FullName = "Active", IsActiveState = true
                },
                new TenantStatus
                {
                    Id = (int)TenantStatusEnum.Suspended, MasterCode = "Suspended",
                    FullName = "Suspended", IsActiveState = false
                });
            seed.Set<SettingValueTypeLookup>().Add(new SettingValueTypeLookup
            {
                Id = (int)SettingValueType.Boolean, MasterCode = "Boolean", FullName = "Boolean"
            });
            foreach (Guid id in new[] { store.TenantId, store.OtherTenantId })
            {
                seed.Tenants.Add(new Tenant
                {
                    Id = id, Slug = $"disclosure-{id:N}", FullName = "Original",
                    TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
                });
                seed.Set<EventDiscoveryRevision>().Add(new EventDiscoveryRevision
                {
                    Id = Guid.CreateVersion7(), TenantId = id
                });
            }
            await seed.SaveChangesAsync();
            return store;
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            File.Delete(_path + "-shm");
            File.Delete(_path + "-wal");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ParentBeforeEpoch(Guid[] requiredParents) : DbCommandInterceptor
    {
        private readonly HashSet<Guid> _fenced = [];
        public bool ObservedEpoch { get; private set; }

        private void Observe(DbCommand command, CommandEventData eventData)
        {
            var context = (ExploreDbContext)eventData.Context!;
            var sql = context.GetService<ISqlGenerationHelper>();
            var tenant = context.Model.FindEntityType(typeof(Tenant))!;
            var revision = context.Model.FindEntityType(typeof(EventDiscoveryRevision))!;
            string parentTable = sql.DelimitIdentifier(tenant.GetTableName()!, tenant.GetSchema());
            string epochTable = sql.DelimitIdentifier(revision.GetTableName()!, revision.GetSchema());
            if (command.CommandText.StartsWith($"UPDATE {parentTable}", StringComparison.Ordinal))
            {
                foreach (DbParameter parameter in command.Parameters)
                    if (Guid.TryParse(parameter.Value?.ToString(), out Guid id))
                        _fenced.Add(id);
            }
            if (!command.CommandText.Contains(epochTable, StringComparison.Ordinal))
                return;
            ObservedEpoch = true;
            if (requiredParents.Any(id => !_fenced.Contains(id)))
                throw new InvalidOperationException("A revision command preceded its complete native parent fence.");
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

    private sealed class DecisionBeforeEpoch : DbCommandInterceptor
    {
        public bool SawAudit { get; private set; }
        public bool SawOutbox { get; private set; }
        public bool SawEpoch { get; private set; }

        private void Observe(DbCommand command, CommandEventData eventData)
        {
            var context = (ExploreDbContext)eventData.Context!;
            var sql = context.GetService<ISqlGenerationHelper>();
            string Table(Type type)
            {
                var entity = context.Model.FindEntityType(type)!;
                return sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            }
            SawAudit |= command.CommandText.Contains($"INSERT INTO {Table(typeof(AuditLog))}", StringComparison.Ordinal);
            SawOutbox |= command.CommandText.Contains($"INSERT INTO {Table(typeof(OutboxMessage))}", StringComparison.Ordinal);
            if (!command.CommandText.Contains(Table(typeof(EventDiscoveryRevision)), StringComparison.Ordinal))
                return;
            SawEpoch = true;
            if (!SawAudit || !SawOutbox)
                throw new InvalidOperationException("Identity epoch acquisition preceded durable decision evidence.");
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
}
