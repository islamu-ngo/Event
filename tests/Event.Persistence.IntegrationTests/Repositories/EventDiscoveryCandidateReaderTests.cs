using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Features.Events.Discovery;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Specifications.Events;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.Services.Discovery;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class EventDiscoveryCandidateReaderTests
{
    private static readonly DateTimeOffset Now = new(2028, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly GetEventListRequest Criteria = new()
    {
        View = TemporalView.All,
        OperationNow = Now,
        SortBy = "title",
        SortDescending = false
    };

    [Test]
    public async Task Generated_ids_tracked_set_values_and_materialization_preserve_both_rank_keys()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var entity = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Title = "initial",
            TenantId = fixture.TenantId,
            Tenant = null!,
            ActorId = fixture.ActorId,
            Actor = null!,
            OrganizerActorId = fixture.ActorId,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated
        };
        fixture.Context.Events.Add(entity);
        await Assert.That(entity.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(entity.DiscoverySourceSortKey).IsEqualTo(entity.Id.ToString("N"));
        Guid recordId = AddRemote(fixture, Now);
        await SaveAsync(fixture);
        var local = await fixture.Context.Events.SingleAsync(row => row.Id == entity.Id);
        var remote = await fixture.Context.AtprotoEventProjections.SingleAsync(row => row.AtprotoRecordId == recordId);
        fixture.Context.Entry(local).CurrentValues.SetValues(new { Title = "\u00e9 " });
        fixture.Context.Entry(remote).CurrentValues.SetValues(new { Name = "\u00e9 " });
        await SaveAsync(fixture);
        var localKeys = await fixture.Context.Events.Where(row => row.Id == entity.Id)
            .Select(row => new { row.DiscoveryTitleSortKey, row.DiscoverySourceSortKey }).SingleAsync();
        var remoteKeys = await fixture.Context.AtprotoEventProjections.Where(row => row.AtprotoRecordId == recordId)
            .Select(row => new { row.DiscoveryTitleSortKey, row.DiscoverySourceSortKey }).SingleAsync();
        await Assert.That(localKeys.DiscoveryTitleSortKey).IsEqualTo("00C90020");
        await Assert.That(remoteKeys.DiscoveryTitleSortKey).IsEqualTo("00C90020");
        await Assert.That(localKeys.DiscoverySourceSortKey).IsEqualTo(entity.Id.ToString("N"));
        await Assert.That(remoteKeys.DiscoverySourceSortKey).IsEqualTo(recordId.ToString("N"));
        await Assert.That((await fixture.Context.Events.AsNoTracking().SingleAsync(row => row.Id == entity.Id))
            .DiscoveryTitleSortKey).IsEqualTo("00C90020");
    }

    [Test]
    public async Task Migration_rank_backfill_crosses_batches_and_repairs_suppressed_rows_before_reads()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        for (int index = 0; index < 257; index++)
        {
            var entity = AddEvent(fixture, index == 256 ? "\u00e9 " : $"title {index:D3}");
            if (index == 256)
                entity.IsDeleted = true;
        }
        Guid recordId = AddRemote(fixture, Now);
        await SaveAsync(fixture);
        // Reproduce the empty defaults introduced by adding the required key columns
        // to an existing catalog, without changing source fields or invoking setters.
        await fixture.Context.Events.IgnoreQueryFilters([Explore.Persistence.QueryFilters.QueryFilterNames.SoftDelete])
            .Where(row => row.TenantId == fixture.TenantId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.DiscoveryTitleSortKey, string.Empty)
                .SetProperty(row => row.DiscoverySourceSortKey, string.Empty));
        await fixture.Context.AtprotoEventProjections.Where(row => row.AtprotoRecordId == recordId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.DiscoveryTitleSortKey, string.Empty)
                .SetProperty(row => row.DiscoverySourceSortKey, string.Empty));
        await EventDiscoveryRankBackfill.ApplyAsync(fixture.Context, CancellationToken.None);
        await EventDiscoveryRankBackfill.ApplyAsync(fixture.Context, CancellationToken.None);
        var keys = await fixture.Context.Events.IgnoreQueryFilters([Explore.Persistence.QueryFilters.QueryFilterNames.SoftDelete])
            .Where(row => row.TenantId == fixture.TenantId)
            .Select(row => new { row.Id, row.Title, row.DiscoveryTitleSortKey, row.DiscoverySourceSortKey })
            .ToListAsync();
        await Assert.That(keys.Count).IsEqualTo(258);
        await Assert.That(keys.All(row => row.DiscoverySourceSortKey == row.Id.ToString("N"))).IsTrue();
        await Assert.That(keys.Single(row => row.Title == "\u00e9 ").DiscoveryTitleSortKey).IsEqualTo("00C90020");
        var remote = await fixture.Context.AtprotoEventProjections.SingleAsync(row => row.AtprotoRecordId == recordId);
        await Assert.That(remote.DiscoverySourceSortKey).IsEqualTo(recordId.ToString("N"));
        await Assert.That(remote.DiscoveryTitleSortKey).IsEqualTo("00520045004D004F00540045");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Mixed_case_title_rank_selects_the_correct_prefix_before_the_unique_membership_cap(bool descending)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        for (int index = 1; index <= 10; index++)
        {
            string title = $"Z{index:D2}";
            ids.Add(title, AddEvent(fixture, title).Id);
        }
        ids.Add("a00", AddEvent(fixture, "a00").Id);
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var capture = await CreateReader(fixture).CaptureAsync(
            Criteria with { SortDescending = descending }, new(maxIdentities: 10));
        string[] expected = descending
            ? ["Z10", "Z09", "Z08", "Z07", "Z06", "Z05", "Z04", "Z03", "Z02", "Z01"]
            : ["a00", "Z01", "Z02", "Z03", "Z04", "Z05", "Z06", "Z07", "Z08", "Z09"];

        await Assert.That(capture.Membership.Select(item => item.SourceId)
            .SequenceEqual(expected.Select(title => ids[title]))).IsTrue();
        await Assert.That(capture.Membership.Length).IsEqualTo(10);
        await Assert.That(capture.ExaminedRows).IsEqualTo(11);
        await Assert.That(capture.SourceSeeks).IsEqualTo(1);
        await Assert.That(capture.Truncated).IsTrue();
    }

    [Test]
    public async Task Local_owned_federation_record_ids_cannot_change_source_ties_or_capture_prefixes()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var first = AddEvent(fixture, "same title", Guid.Parse("00000000-0000-7000-8000-000000000011"));
        var second = AddEvent(fixture, "same title", Guid.Parse("00000000-0000-7000-8000-000000000012"));
        AddOwnership(first, Guid.Parse("00000000-0000-7000-8000-0000000000f2"));
        AddOwnership(second, Guid.Parse("00000000-0000-7000-8000-0000000000f1"));
        var enabled = await fixture.Context.Set<SystemSetting>().SingleOrDefaultAsync(
            setting => setting.SettingKey == GovernanceSettingKeys.Federation.AtprotoEventsEnabled);
        if (enabled is null)
            fixture.Context.Set<SystemSetting>().Add(new()
            {
                Id = Guid.CreateVersion7(),
                SettingKey = GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
                Value = "true",
                ValueType = SettingValueType.Boolean,
                IsLocked = true
            });
        else
            enabled.Value = "true";
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var reader = CreateReader(fixture);
        var one = await reader.CaptureAsync(Criteria, new(maxIdentities: 1));
        var two = await reader.CaptureAsync(Criteria, new(maxIdentities: 2));

        await Assert.That(one.Membership.Single().SourceId).IsEqualTo(first.Id);
        await Assert.That(two.Membership.Select(item => item.SourceId)
            .SequenceEqual(new[] { first.Id, second.Id })).IsTrue();
        await Assert.That(one.Membership.Single().SourceId).IsEqualTo(two.Membership[0].SourceId);
        await Assert.That(two.ExaminedRows).IsEqualTo(2);
        await Assert.That(two.SourceSeeks).IsEqualTo(2);

        void AddOwnership(Explore.Domain.Event entity, Guid recordId)
        {
            var record = new AtprotoRecord
            {
                Id = recordId,
                Did = $"did:plc:{recordId:N}",
                Collection = "community.lexicon.calendar.event",
                RecordKey = entity.Id.ToString("N"),
                Direction = AtprotoRecordDirection.Outbound,
                Provenance = AtprotoRecordProvenance.LocalLifecycle,
                SourceVersion = 1,
                UpdatedAt = Now.UtcDateTime
            };
            entity.AtprotoRecordId = recordId;
            entity.AtprotoRecord = record;
            fixture.Context.AddRange(record, new AtprotoOutboundRecordOwnership
            {
                TenantId = fixture.TenantId,
                UserId = fixture.UserId,
                AtprotoRecordId = recordId,
                AtprotoRecord = record,
                SourceEntityType = nameof(Explore.Domain.Event),
                SourceEntityId = entity.Id,
                SourceVersion = Guid.CreateVersion7(),
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime
            });
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Persisted_remote_title_seek_uses_the_same_mixed_case_rank_before_take(bool descending)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        for (int index = 0; index <= 10; index++)
        {
            Guid recordId = AddRemote(fixture, Now);
            fixture.Context.AtprotoEventProjections.Local.Single(row => row.AtprotoRecordId == recordId).Name =
                index == 0 ? "a00" : $"Z{index:D2}";
        }
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var query = new AtprotoEventProjectionQuery(10, null, null, null, null,
            AtprotoEventTemporalFilter.All, AtprotoEventDiscoverySort.Title, descending, Now);
        var rows = await new AtprotoEventProjectionRepository(fixture.Context)
            .SeekPublicDiscoveryAsync(query, null, CancellationToken.None);
        string[] expected = descending
            ? ["Z10", "Z09", "Z08", "Z07", "Z06", "Z05", "Z04", "Z03", "Z02", "Z01"]
            : ["a00", "Z01", "Z02", "Z03", "Z04", "Z05", "Z06", "Z07", "Z08", "Z09"];

        await Assert.That(rows.Select(row => row.Name).SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task Native_views_and_creation_ranks_cannot_silently_fall_back_to_occurrence_order()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var first = AddEvent(fixture, "equal", Guid.Parse("00000000-0000-7000-8000-000000000001"));
        var second = AddEvent(fixture, "equal", Guid.Parse("00000000-0000-7000-8000-000000000002"));
        var third = AddEvent(fixture, "equal", Guid.Parse("00000000-0000-7000-8000-000000000003"));
        first.TotalViews = 20;
        second.TotalViews = 30;
        third.TotalViews = 10;
        first.CreatedAt = Now.AddDays(2).UtcDateTime;
        second.CreatedAt = Now.AddDays(1).UtcDateTime;
        third.CreatedAt = Now.AddDays(3).UtcDateTime;
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        foreach (var (sort, descending, expected) in new[]
        {
            ("views", true, second.Id), ("views", false, third.Id),
            ("createdat", true, third.Id), ("createdat", false, second.Id)
        })
        {
            var capture = await CreateReader(fixture).CaptureAsync(
                Criteria with { SortBy = sort, SortDescending = descending }, new(maxIdentities: 1));
            await Assert.That(capture.Membership.Single().SourceId).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task Source_kind_precedes_source_key_for_equal_primary_ranks_in_both_directions()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var local = AddEvent(fixture, "remote", Guid.Parse("ffffffff-ffff-7fff-bfff-ffffffffffff"));
        AddRemote(fixture, Now);
        var setting = await fixture.Context.Set<SystemSetting>().SingleOrDefaultAsync(
            value => value.SettingKey == GovernanceSettingKeys.Federation.AtprotoEventsEnabled);
        if (setting is null)
            fixture.Context.Set<SystemSetting>().Add(new()
            {
                Id = Guid.CreateVersion7(),
                SettingKey = GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
                Value = "true",
                ValueType = SettingValueType.Boolean,
                IsLocked = true
            });
        else
            setting.Value = "true";
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        foreach (string sort in new[] { "title", "views", "createdat", "date" })
            foreach (bool descending in new[] { false, true })
            {
                var capture = await CreateReader(fixture).CaptureAsync(
                    Criteria with { SortBy = sort, SortDescending = descending }, new(maxIdentities: 1));
                await Assert.That(capture.Membership.Single().SourceKind).IsEqualTo(EventDiscoverySourceKind.LocalEvent);
                await Assert.That(capture.Membership.Single().SourceId).IsEqualTo(local.Id);
            }
    }

    [Test]
    public async Task Omitted_date_sort_and_explicit_date_sort_capture_the_same_earliest_bounded_membership()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var expected = new List<Guid>();
        for (int index = 0; index < 11; index++)
        {
            var entity = AddEvent(fixture, $"scheduled {index:D2}");
            var session = fixture.Context.ChangeTracker.Entries<EventSession>()
                .Single(entry => entry.Entity.EventId == entity.Id).Entity;
            session.StartTime = Now.AddDays(index + 1);
            session.EndTime = session.StartTime!.Value.AddHours(1);
            session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
            if (index < 10)
                expected.Add(entity.Id);
        }
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var reader = CreateReader(fixture);
        var limits = new EventDiscoveryTraversalLimits(maxIdentities: 10);
        var omitted = await reader.CaptureAsync(Criteria with { SortBy = null }, limits);
        var explicitDate = await reader.CaptureAsync(Criteria with { SortBy = "date" }, limits);

        await Assert.That(omitted.Membership.Select(item => item.SourceId).SequenceEqual(expected)).IsTrue();
        await Assert.That(explicitDate.Membership.Select(item => item.SourceId).SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task Alias_heavy_capture_refills_and_preserves_unique_visible_membership()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var primary = AddEvent(fixture, "hidden primary");
        primary.VisibilityTypeId = (int)VisibilityTypeEnum.Private;
        var root = Bind(fixture, primary);
        for (var index = 0; index < 140; index++)
            Alias(fixture, Bind(fixture, AddEvent(fixture, $"alias {index:D3}",
                index == 0 ? Guid.Parse("00000000-0000-7000-8000-000000000001") : null)), root);
        var distinct = AddEvent(fixture, "second result");
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var reader = CreateReader(fixture);
        var capture = await reader.CaptureAsync(Criteria, new EventDiscoveryTraversalLimits());

        await Assert.That(capture.Membership.Length).IsEqualTo(2);
        await Assert.That(capture.Membership.Select(item => (item.CanonicalKind, item.CanonicalId)).Distinct().Count())
            .IsEqualTo(2);
        await Assert.That(capture.SourceSeeks).IsGreaterThan(1);
        await Assert.That(capture.ExaminedRows).IsEqualTo(141);
        await Assert.That(capture.LocalSourceComplete).IsTrue();
        await Assert.That(capture.Truncated).IsFalse();
        var cards = await reader.ReprojectAsync(Criteria, capture.Membership)
            ?? throw new InvalidOperationException("Unchanged captured members must reproject.");
        await Assert.That(cards.Any(card => card.Event!.Id == primary.Id)).IsFalse();
        await Assert.That(cards.Any(card => card.Event!.Id == distinct.Id)).IsTrue();
        await Assert.That(cards.Single(card => card.DiscoveryIdentityId == root.Id).Event!.Title)
            .IsEqualTo("alias 000");
    }

    [Test]
    [Arguments(20, 32, 20)]
    [Arguments(10000, 1, 128)]
    public async Task Alias_duplicates_consume_shared_budgets_without_claiming_source_exhaustion(
        int rows, int seeks, int examined)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var primary = Bind(fixture, AddEvent(fixture, "alias 000"));
        for (var index = 1; index < 150; index++)
            Alias(fixture, Bind(fixture, AddEvent(fixture, $"alias {index:D3}")), primary);
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var capture = await CreateReader(fixture).CaptureAsync(
            Criteria, new EventDiscoveryTraversalLimits(maxExaminedRows: rows, maxSourceSeeks: seeks));

        await Assert.That(capture.Membership.Length).IsEqualTo(1);
        await Assert.That(capture.ExaminedRows).IsEqualTo(examined);
        await Assert.That(capture.SourceSeeks).IsEqualTo(1);
        await Assert.That(capture.LocalSourceComplete).IsFalse();
        await Assert.That(capture.Truncated).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Current_page_rejects_removed_or_moved_matching_session_without_substituting_another(
        bool move)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var entity = AddEvent(fixture, "current result");
        AddSession(fixture, entity, Now.AddHours(1));
        await SaveAsync(fixture);
        var criteria = Criteria with { DateFrom = new(2028, 6, 15), DateTo = new(2028, 6, 15) };
        IReadOnlyList<EventDiscoverySnapshotItem> membership;
        await using (var captureTransaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            membership = (await CreateReader(fixture).CaptureAsync(criteria, new())).Membership;
            await captureTransaction.CommitAsync();
        }
        var selected = await fixture.Context.EventSessions.SingleAsync(
            session => session.Id == membership.Single().MatchingSessionId);
        if (move)
        {
            selected.StartTime = Now.AddDays(1);
            selected.EndTime = Now.AddDays(1).AddHours(1);
            selected.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
        }
        else
            selected.IsDeleted = true;
        await SaveAsync(fixture);
        await using var readTransaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var current = await CreateReader(fixture).ReprojectAsync(criteria, membership);

        await Assert.That(current).IsNull();
    }

    [Test]
    public async Task Exact_full_window_is_truncated_until_a_bounded_seek_proves_exhaustion()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        for (var index = 0; index < 128; index++)
            AddEvent(fixture, $"result {index:D3}");
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var reader = CreateReader(fixture);
        var capped = await reader.CaptureAsync(Criteria, new(maxSourceSeeks: 1));
        var complete = await reader.CaptureAsync(Criteria, new());
        await Assert.That(capped.Truncated).IsTrue();
        await Assert.That(capped.LocalSourceComplete).IsFalse();
        await Assert.That(complete.Truncated).IsFalse();
        await Assert.That(complete.LocalSourceComplete).IsTrue();
        await Assert.That(complete.ExaminedRows).IsEqualTo(128);
        await Assert.That(complete.Membership.Select(item => item.SourceId).Distinct().Count()).IsEqualTo(128);
    }

    [Test]
    public async Task Unique_membership_cap_never_claims_unprocessed_buffer_rows_are_exhausted()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        AddEvent(fixture, "first");
        AddEvent(fixture, "second");
        AddEvent(fixture, "third");
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var capture = await CreateReader(fixture).CaptureAsync(Criteria, new(maxIdentities: 2));

        await Assert.That(capture.Membership.Length).IsEqualTo(2);
        await Assert.That(capture.ExaminedRows).IsEqualTo(3);
        await Assert.That(capture.LocalSourceComplete).IsFalse();
        await Assert.That(capture.Truncated).IsTrue();
    }

    [Test]
    public async Task Capture_expires_at_the_selected_locations_earlier_reveal_boundary()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var entity = AddEvent(fixture, "time bounded");
        var session = fixture.Context.EventSessions.Local.Single();
        session.StartTime = Now.AddHours(1);
        session.EndTime = Now.AddHours(2);
        session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            FullName = "Public venue",
            City = "Brussels",
            Country = "BE"
        };
        location.SetManualAddress("Test venue", "1000");
        fixture.Context.Locations.Add(location);
        var carrier = EventLocation.CreatePhysical(
            fixture.TenantId, entity.Id, location.Id, fixture.UserId, Now.AddDays(-31).UtcDateTime);
        carrier.ChangeDisclosurePolicy(EventLocationDisclosureFields.City | EventLocationDisclosureFields.Country,
            LocationDisclosureAudienceEnum.Never, Now.AddMinutes(5).UtcDateTime, carrier.PolicyVersion,
            fixture.UserId, EventLocationDisclosureAuditReasonEnum.GovernanceTightening, Now.UtcDateTime,
            needsPrivacyReview: false);
        fixture.Context.EventLocations.Add(carrier);
        session.AssignEventLocation(carrier);
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var capture = await CreateReader(fixture).CaptureAsync(Criteria, new());

        await Assert.That(capture.ValidUntilUtc).IsEqualTo(Now.AddMinutes(5));
    }

    [Test]
    public async Task Local_and_persisted_remote_sources_share_one_row_and_seek_budget()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        AddEvent(fixture, "local");
        AddRemote(fixture, Now.AddMinutes(7));
        var setting = await fixture.Context.Set<SystemSetting>().SingleOrDefaultAsync(
            item => item.SettingKey == GovernanceSettingKeys.Federation.AtprotoEventsEnabled);
        if (setting is null)
            fixture.Context.Set<SystemSetting>().Add(new()
            {
                Id = Guid.CreateVersion7(),
                SettingKey = GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
                Value = "true",
                ValueType = SettingValueType.Boolean,
                IsLocked = true
            });
        else
            setting.Value = "true";
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var reader = CreateReader(fixture);
        var bounded = await reader.CaptureAsync(Criteria, new(maxExaminedRows: 2, maxSourceSeeks: 2));
        var exhausted = await reader.CaptureAsync(Criteria, new());

        await Assert.That(bounded.ExaminedRows).IsEqualTo(2);
        await Assert.That(bounded.SourceSeeks).IsEqualTo(2);
        await Assert.That(bounded.Membership.Length).IsEqualTo(2);
        await Assert.That(bounded.Membership.Select(item => item.SourceKind).Distinct().Count()).IsEqualTo(2);
        await Assert.That(bounded.LocalSourceComplete).IsTrue();
        await Assert.That(bounded.RemoteSourceComplete).IsFalse();
        await Assert.That(bounded.Truncated).IsTrue();
        await Assert.That(exhausted.Truncated).IsFalse();
        await Assert.That(exhausted.ValidUntilUtc).IsEqualTo(Now.AddMinutes(7));
    }

    [Test]
    [Arguments("date", false)]
    [Arguments("date", true)]
    [Arguments("createdat", false)]
    [Arguments("createdat", true)]
    [Arguments("views", false)]
    [Arguments("title", false)]
    public async Task Complete_source_keysets_keep_all_tied_ranks_exactly_once(string sort, bool descending)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        for (var index = 0; index < 130; index++)
            AddEvent(fixture, "equal");
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var capture = await CreateReader(fixture).CaptureAsync(
            Criteria with { SortBy = sort, SortDescending = descending }, new());

        await Assert.That(capture.ExaminedRows).IsEqualTo(130);
        await Assert.That(capture.Membership.Length).IsEqualTo(130);
        await Assert.That(capture.Membership.Select(item => item.SourceId).Distinct().Count()).IsEqualTo(130);
        await Assert.That(capture.LocalSourceComplete).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Persisted_remote_keyset_handles_null_and_equal_instants_without_offset_fallback(bool descending)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var withoutDate = AddRemote(fixture, null);
        var first = AddRemote(fixture, Now);
        var second = AddRemote(fixture, Now.ToOffset(TimeSpan.FromHours(4)));
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = new AtprotoEventProjectionRepository(fixture.Context);
        var query = new AtprotoEventProjectionQuery(1, null, null, null, null,
            AtprotoEventTemporalFilter.All, AtprotoEventDiscoverySort.Date, descending, Now);
        var seen = new List<Guid>();
        EventDiscoverySourceCursor? cursor = null;
        for (var index = 0; index < 4; index++)
        {
            var rows = await repository.SeekPublicDiscoveryAsync(query, cursor, CancellationToken.None);
            if (rows.Count == 0)
                break;
            var row = rows.Single();
            seen.Add(row.AtprotoRecordId);
            cursor = new(row.AtprotoRecordId, row.Name, 0, row.CreatedAt.UtcDateTime, row.StartsAt);
        }
        await Assert.That(seen.Count).IsEqualTo(3);
        await Assert.That(seen[descending ? 2 : 0]).IsEqualTo(withoutDate);
        var dated = seen.Where(id => id != withoutDate).ToArray();
        var expectedDated = new[] { first, second }.OrderBy(id => id.ToString("N"), StringComparer.Ordinal).ToArray();
        await Assert.That(dated.SequenceEqual(expectedDated)).IsTrue();
        await Assert.That(seen).IsEquivalentTo(new[] { withoutDate, first, second });
    }

    [Test]
    public async Task Current_clock_reprojection_cannot_reuse_the_captures_old_operation_time()
    {
        var clock = new ControlledClock { Now = Now };
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(
            services => services.AddSingleton<TimeProvider>(clock));
        var entity = AddEvent(fixture, "starts soon");
        var session = fixture.Context.EventSessions.Local.Single();
        session.StartTime = Now.AddMinutes(2);
        session.EndTime = Now.AddHours(1);
        session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
        await SaveAsync(fixture);
        var criteria = Criteria with { View = TemporalView.Upcoming };
        EventDiscoveryCandidateCapture capture;
        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            capture = await CreateReader(fixture).CaptureAsync(criteria, new());
            await transaction.CommitAsync();
        }
        clock.Now = Now.AddMinutes(3);
        await using var readTransaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        await Assert.That(capture.ValidUntilUtc).IsEqualTo(Now.AddMinutes(2));
        await Assert.That(await CreateReader(fixture).ReprojectAsync(criteria, capture.Membership)).IsNull();
    }

    [Test]
    public async Task Bounded_source_materializes_one_matching_session_instead_of_the_full_schedule()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var entity = AddEvent(fixture, "large schedule");
        for (var index = 1; index < 500; index++)
            AddSession(fixture, entity, Now.AddMinutes(index));
        await SaveAsync(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var specification = new EventQuerySpecification()
            .WithOccurrence(new(null, null, TemporalView.All, Now, null)).SortBy(EventSort.Date);
        var rows = await new EventRepository(fixture.Context).SeekPublicDiscoveryAsync(
            specification, null, 1, CancellationToken.None);

        await Assert.That(rows.Single().Sessions.Count).IsEqualTo(1);
        await Assert.That(rows.Single().DiscoveryAdditionalSessionCount).IsEqualTo(499);
        await Assert.That(fixture.Context.ChangeTracker.Entries().Count()).IsEqualTo(0);
    }

    private sealed class ControlledClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static EventDiscoveryCandidateReader CreateReader(EventVisitorCapabilitySqliteFixture fixture) =>
        ActivatorUtilities.CreateInstance<EventDiscoveryCandidateReader>(fixture.Services,
            ActivatorUtilities.CreateInstance<EventDiscoveryLocalSource>(fixture.Services));

    private static Explore.Domain.Event AddEvent(
        EventVisitorCapabilitySqliteFixture fixture, string title, Guid? id = null,
        EventStatusEnum status = EventStatusEnum.Published)
    {
        var entity = new Explore.Domain.Event(status)
        {
            Id = id ?? Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            Title = title,
            PublicCode = Guid.CreateVersion7().ToString("N"),
            ActorId = fixture.ActorId,
            Actor = null!,
            OrganizerActorId = fixture.ActorId,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            Timezone = "UTC",
            CreatedAt = Now.UtcDateTime
        };
        fixture.Context.Events.Add(entity);
        AddSession(fixture, entity, Now);
        return entity;
    }

    private static Guid AddRemote(EventVisitorCapabilitySqliteFixture fixture, DateTimeOffset? start)
    {
        var id = Guid.CreateVersion7();
        var did = $"did:plc:{id:N}";
        var record = new AtprotoRecord
        {
            Id = id,
            Did = did,
            Collection = "community.lexicon.calendar.event",
            RecordKey = id.ToString("N"),
            Cid = "bafy-source-test",
            Uri = $"at://{did}/community.lexicon.calendar.event/{id:N}",
            Direction = AtprotoRecordDirection.Inbound,
            Provenance = AtprotoRecordProvenance.Jetstream,
            SourceVersion = 1,
            RecordJson = "{}",
            RecordHash = new string('a', 64),
            IndexedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime
        };
        var imported = AddEvent(fixture, "remote", status: EventStatusEnum.Draft);
        imported.EventProvenanceTypeId = (int)EventProvenanceTypeEnum.Federated;
        imported.AtprotoRecordId = id;
        imported.AtprotoRecord = record;
        fixture.Context.AddRange(record, new AtprotoEventProjection
        {
            AtprotoRecordId = id,
            Name = "remote",
            CreatedAt = Now,
            StartsAt = start,
            EndsAt = start?.AddHours(1),
            SourceVersion = 1,
            MaterializedAt = Now.UtcDateTime
        }, new AtprotoRecordTenantPresentation
        {
            TenantId = fixture.TenantId,
            AtprotoRecordId = id,
            IsVisible = true,
            SourceVersion = 1,
            EvaluatedAt = Now.UtcDateTime
        }, new AtprotoIdentity(AtprotoDid.Parse(did))
        {
            Id = Guid.CreateVersion7(),
            ActorId = fixture.ActorId,
            Actor = null!,
            PdsHost = "https://pds.example.test",
            IsActive = true,
            LastResolvedAt = Now.UtcDateTime,
            LastSeenAt = Now.UtcDateTime,
            CreatedAt = Now.UtcDateTime
        });
        return id;
    }

    private static void AddSession(EventVisitorCapabilitySqliteFixture fixture, Explore.Domain.Event entity,
        DateTimeOffset start)
    {
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            EventId = entity.Id,
            Event = entity,
            StartTime = start,
            EndTime = start.AddHours(1),
            EndTimeType = SessionEndTimeType.Fixed
        };
        session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
        fixture.Context.EventSessions.Add(session);
    }

    private static EventDiscoveryIdentity Bind(EventVisitorCapabilitySqliteFixture fixture, Explore.Domain.Event entity)
    {
        var identity = EventDiscoveryIdentity.Create(
            fixture.TenantId, EventDiscoverySourceKind.LocalEvent, entity.Id.ToString("D"));
        fixture.Context.Set<EventDiscoveryIdentity>().Add(identity);
        return identity;
    }

    private static void Alias(EventVisitorCapabilitySqliteFixture fixture,
        EventDiscoveryIdentity member, EventDiscoveryIdentity primary) =>
        fixture.Context.Set<EventDiscoveryAlias>().Add(new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            MemberIdentityId = member.Id,
            Member = member,
            PrimaryIdentityId = primary.Id,
            Primary = primary,
            RelationshipRevision = 1,
            ReviewerId = fixture.UserId,
            ReasonCode = "same_event",
            ReviewedAtUtc = Now.UtcDateTime
        });

    private static async Task SaveAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }
}
