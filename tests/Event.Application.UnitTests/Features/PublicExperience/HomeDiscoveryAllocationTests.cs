using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.Federation.Atproto.Requests.Queries;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.PublicExperience;
using Explore.Application.Features.PublicExperience.Handlers.Queries;
using Explore.Application.Features.PublicExperience.Requests.Queries;
using Explore.Application.Models.PublicExperience;
using Explore.Application.Responses;
using Explore.Application.Specifications.Events;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explore.Application.UnitTests.Features.PublicExperience;

public sealed class HomeDiscoveryAllocationTests
{
    [Test]
    public async Task ReviewedAliasesShareHomeOwnershipWithoutDisclosingThePrivatePrimaryKey()
    {
        Guid root = Guid.CreateVersion7();
        var pool = Pool(3);
        pool[0] = pool[0] with { DiscoveryIdentityId = root };
        pool[1] = pool[1] with { DiscoveryIdentityId = root };
        var allocator = new HomeDiscoveryAllocator(DateTimeOffset.UnixEpoch);
        Task<PaginatedResult<EventDiscoveryItemDto>> Read(GetEventListRequest request, CancellationToken _) =>
            Task.FromResult(new PaginatedResult<EventDiscoveryItemDto>(
                pool, pool.Count, request.PageNumber, request.PageSize));
        var first = await allocator.AllocateAsync(new(), 1, Read, CancellationToken.None);
        var second = await allocator.AllocateAsync(new(), 1, Read, CancellationToken.None);
        await Assert.That(first.Items[0].Event!.Id).IsEqualTo(pool[0].Event!.Id);
        await Assert.That(second.Items[0].Event!.Id).IsEqualTo(pool[2].Event!.Id);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(first.Items[0]));
        await Assert.That(document.RootElement.TryGetProperty("DiscoveryIdentityId", out _)).IsFalse();
        await Assert.That(document.RootElement.ToString().Contains(root.ToString("D"), StringComparison.Ordinal))
            .IsFalse();
    }

    [Test]
    public async Task SectionsAndRefillPagesRetainOneTrustedOperationInstant()
    {
        var now = new DateTimeOffset(2030, 6, 1, 12, 30, 0, TimeSpan.Zero);
        var allocator = new HomeDiscoveryAllocator(now);
        var distinct = Pool(2);
        var pool = Enumerable.Repeat(distinct[0], 100).Append(distinct[1]).ToList();
        var criteria = new GetEventListRequest { OperationNow = DateTimeOffset.UnixEpoch, PageSize = 7 };
        var observed = new List<GetEventListRequest>();
        Task<PaginatedResult<EventDiscoveryItemDto>> Read(GetEventListRequest request, CancellationToken _)
        {
            observed.Add(request);
            var items = pool.Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize).ToList();
            return Task.FromResult(new PaginatedResult<EventDiscoveryItemDto>(
                items, pool.Count, request.PageNumber, request.PageSize));
        }

        await allocator.AllocateAsync(criteria, 1, Read, CancellationToken.None);
        var refill = await allocator.AllocateAsync(criteria, 1, Read, CancellationToken.None);

        await Assert.That(refill.Items[0].Event!.Id).IsEqualTo(distinct[1].Event!.Id);
        await Assert.That(observed.Count).IsEqualTo(3);
        await Assert.That(observed.All(request => request.OperationNow == now)).IsTrue();
        await Assert.That(criteria.OperationNow).IsEqualTo(DateTimeOffset.UnixEpoch);
        await Assert.That(criteria.PageSize).IsEqualTo(7);
    }

    [Test]
    public async Task SourceNamespacesStayDistinctAndLocalAtprotoEchoSharesOneIdentity()
    {
        var allocator = new HomeDiscoveryAllocator(DateTimeOffset.UnixEpoch);
        var pool = Pool(2);
        var recordId = pool[0].Event!.Id;
        pool[1] = pool[1] with { Event = pool[1].Event! with { AtprotoRecordId = recordId } };
        pool.Add(new EventDiscoveryItemDto
        {
            Source = "atproto",
            FederatedEvent = new FederatedEventDto { Id = recordId }
        });
        var result = await allocator.AllocateAsync(new(), 10,
            (criteria, _) => Task.FromResult(new PaginatedResult<EventDiscoveryItemDto>(
                pool, pool.Count, criteria.PageNumber, criteria.PageSize)), CancellationToken.None);

        await Assert.That(result.Items.Length).IsEqualTo(2);
        await Assert.That(result.Items[0].Event!.Id).IsEqualTo(recordId);
        await Assert.That(result.Items[1].Event!.Id).IsEqualTo(pool[1].Event!.Id);
        await Assert.That(result.StopReason).IsEqualTo(HomeDiscoveryAllocationStopReason.Exhausted);
    }

    [Test]
    public async Task EmptyCountedPageCannotClaimExhaustion()
    {
        var result = await new HomeDiscoveryAllocator(DateTimeOffset.UnixEpoch).AllocateAsync(new(), 10,
            (criteria, _) => Task.FromResult(new PaginatedResult<EventDiscoveryItemDto>(
                [], 200, criteria.PageNumber, criteria.PageSize)), CancellationToken.None);

        await Assert.That(result.StopReason).IsEqualTo(HomeDiscoveryAllocationStopReason.ExhaustionUnproven);
        await Assert.That(result.CandidateCount).IsEqualTo(0);
        await Assert.That(result.BatchCount).IsEqualTo(1);
    }

    [Test]
    public async Task CandidateBudgetReportsExactBoundAndReservesReturnedPartialItems()
    {
        var allocator = new HomeDiscoveryAllocator(DateTimeOffset.UnixEpoch);
        var item = Pool(1)[0];
        Task<PaginatedResult<EventDiscoveryItemDto>> Read(
            Explore.Application.Features.Events.Requests.Queries.GetEventListRequest criteria,
            CancellationToken _) =>
            Task.FromResult(new PaginatedResult<EventDiscoveryItemDto>(
                Enumerable.Repeat(item, 100).ToList(), 2000, criteria.PageNumber, criteria.PageSize));
        var first = await allocator.AllocateAsync(new(), 10, Read, CancellationToken.None);
        var second = await allocator.AllocateAsync(new(), 10, Read, CancellationToken.None);

        await Assert.That(first.StopReason).IsEqualTo(HomeDiscoveryAllocationStopReason.CandidateBudgetExceeded);
        await Assert.That(first.CandidateCount).IsEqualTo(1000);
        await Assert.That(first.BatchCount).IsEqualTo(10);
        await Assert.That(first.Items.Length).IsEqualTo(1);
        await Assert.That(second.Items.Length).IsEqualTo(0);
        await Assert.That(second.StopReason).IsEqualTo(HomeDiscoveryAllocationStopReason.CandidateBudgetExceeded);
    }

    [Test]
    public async Task UpcomingUsesInstantEligibilityWithoutUtcCalendarDateGate()
    {
        GetEventListRequest? criteria = null;
        var reader = new CandidateReader(Pool(20))
        {
            Read = (request, _) =>
            {
                if (criteria is null && request.Criteria.SortBy == "date")
                    criteria = request.Criteria;
                return Task.CompletedTask;
            }
        };
        await CreateHandler(reader).QueryAsync(new(), CancellationToken.None);

        await Assert.That(criteria?.View).IsEqualTo(TemporalView.UpcomingAndOngoing);
        await Assert.That(criteria!.DateFrom).IsNull();
        await Assert.That(criteria.DateTo).IsNull();
    }

    [Test]
    public async Task OtherShelvesRetainExclusivePriorityWithoutRestrictingRecentlyAdded()
    {
        var pool = Pool(100);
        var locationId = Guid.CreateVersion7();
        var area = new PublicDiscoveryAreaConfig(
            Guid.CreateVersion7(), "Brussels", "Brussels", "BE", LocationIds: [locationId],
            IsDefault: true);
        var presets = new PublicEventSectionPresetsConfig(Presets:
        [
            new("last", "Last curated", SortOrder: 20),
            new("first", "First curated", SortOrder: 10)
        ]);
        var home = await CreateHandler(new CandidateReader(pool), area, presets)
            .QueryAsync(new(Mode: "area"), CancellationToken.None);

        await Assert.That(home.MostViewedInArea[0].Event!.Id).IsEqualTo(pool[21].Event!.Id);
        await Assert.That(home.MostViewedOnline[0].Event!.Id).IsEqualTo(pool[31].Event!.Id);
        await Assert.That(home.CuratedSections[0].Key).IsEqualTo("first");
        await Assert.That(home.CuratedSections[0].Items[0].Event!.Id).IsEqualTo(pool[41].Event!.Id);
        await Assert.That(home.CuratedSections[1].Items[0].Event!.Id).IsEqualTo(pool[51].Event!.Id);
        await Assert.That(home.RecentlyAdded[0].Event!.Id).IsEqualTo(pool[18].Event!.Id);
        await Assert.That(Items(home).Select(item => item.Event!.Id).Distinct().Count()).IsEqualTo(61);
    }

    [Test]
    public async Task FeaturedEventsRemainInTheirUpcomingChronologicalPositions()
    {
        var reader = new CandidateReader(Pool(100));
        var home = await CreateHandler(reader).QueryAsync(new(), CancellationToken.None);
        var ids = Items(home).Select(item => item.Event!.Id).ToArray();

        await Assert.That(ids.Length).IsEqualTo(51);
        await Assert.That(ids.Distinct().Count()).IsEqualTo(31);
        await Assert.That(home.Hero.Select(item => item.Event!.Id))
            .IsEquivalentTo(home.UpcomingInArea.Take(10).Select(item => item.Event!.Id));
        await Assert.That(home.UpcomingInArea.Select(item => item.Event!.Id).Distinct().Count())
            .IsEqualTo(home.UpcomingInArea.Count);
    }

    [Test]
    public async Task EarlierEligibleSectionsOwnCandidatesBeforeLaterSections()
    {
        var pool = Pool(100);
        var home = await CreateHandler(new CandidateReader(pool)).QueryAsync(new(), CancellationToken.None);

        await Assert.That(home.Hero[0].Event!.Id).IsEqualTo(pool[0].Event!.Id);
        await Assert.That(home.UpcomingInArea[0].Event!.Id).IsEqualTo(pool[0].Event!.Id);
        await Assert.That(home.Spotlight!.Items[0].Event!.Id).IsEqualTo(pool[18].Event!.Id);
        await Assert.That(home.MostViewedOnline[0].Event!.Id).IsEqualTo(pool[21].Event!.Id);
        await Assert.That(home.RecentlyAdded[0].Event!.Id).IsEqualTo(pool[18].Event!.Id);
    }

    [Test]
    public async Task AllocationDoesNotMutateCandidateInputOrPayload()
    {
        var pool = Pool(100);
        var first = pool[0];
        var title = new string('x', 300);
        pool[0] = first with
        {
            Event = first.Event! with
            {
                Title = title,
                Timezone = "Pacific/Kiritimati"
            }
        };
        var reader = new CandidateReader(pool);
        var home = await CreateHandler(reader).QueryAsync(new(), CancellationToken.None);

        await Assert.That(pool.Count).IsEqualTo(100);
        await Assert.That(pool[0].Event!.Title).IsEqualTo(title);
        await Assert.That(home.Hero[0].Event!.Title.Length).IsEqualTo(240);
        await Assert.That(home.Hero[0].Event!.Timezone).IsEqualTo("Pacific/Kiritimati");
    }

    [Test]
    public async Task DuplicateHeavyFirstPagesRefillFromLaterCandidates()
    {
        var unique = Pool(80);
        var pool = Enumerable.Repeat(unique[0], 200).Concat(unique.Skip(1)).ToList();
        var home = await CreateHandler(new CandidateReader(pool)).QueryAsync(new(), CancellationToken.None);

        await Assert.That(home.Hero.Count).IsEqualTo(10);
        await Assert.That(home.UpcomingInArea.Count).IsEqualTo(18);
        await Assert.That(home.Hero.Select(item => item.Event!.Id).Distinct().Count()).IsEqualTo(10);
        await Assert.That(home.UpcomingInArea[0].Event!.Id).IsEqualTo(unique[0].Event!.Id);
    }

    [Test]
    public async Task SmallEligiblePoolRefillsRecentlyAddedWithoutFabricatingCards()
    {
        var home = await CreateHandler(new CandidateReader(Pool(2)))
            .QueryAsync(new(), CancellationToken.None);

        await Assert.That(home.Hero.Count).IsEqualTo(2);
        await Assert.That(home.UpcomingInArea.Count).IsEqualTo(2);
        await Assert.That(home.RecentlyAdded.Count).IsEqualTo(2);
        await Assert.That(home.RecentlyAdded.Select(item => item.Event!.Id).Distinct().Count()).IsEqualTo(2);
        await Assert.That(home.SectionStatuses["hero"]).IsEqualTo(HomeDiscoverySectionStatus.Available);
        await Assert.That(home.SectionStatuses["upcoming"]).IsEqualTo(HomeDiscoverySectionStatus.Available);
        await Assert.That(home.SectionStatuses["recently-added"]).IsEqualTo(HomeDiscoverySectionStatus.Available);
    }

    [Test]
    public async Task RecentlyAddedSelectsNovelThenFeaturedOnlyThenUpcomingAndRestoresNewestOrder()
    {
        var pool = Pool(16);
        var featured = new[] { pool[1], pool[5], pool[8] };
        var upcoming = new[] { pool[0], pool[3], pool[6], pool[9], pool[11], pool[12], pool[13], pool[14], pool[15] };
        var reader = new CandidateReader(pool)
        {
            Candidates = criteria => criteria.SortBy switch
            {
                "views" => featured,
                "date" => upcoming,
                _ => pool
            }
        };

        var home = await CreateHandler(reader).QueryAsync(new(), CancellationToken.None);
        var expected = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 10 }.Select(index => pool[index].Event!.Id);

        await Assert.That(home.RecentlyAdded.Select(item => item.Event!.Id)).IsEquivalentTo(expected);
        await Assert.That(home.RecentlyAdded.Select(item => item.Event!.Id).SequenceEqual(expected)).IsTrue();
    }

    [Test]
    public async Task RecentlyAddedFindsNovelCandidatesBeyondOverlappingFirstPage()
    {
        var unique = Pool(12);
        var pool = Enumerable.Repeat(unique[0], 100).Concat(unique.Skip(1)).ToList();
        var reader = new CandidateReader(pool)
        {
            Candidates = criteria => criteria.SortBy == "createdat"
                ? pool
                : [unique[0], unique[1]]
        };
        var home = await CreateHandler(reader).QueryAsync(new(), CancellationToken.None);

        await Assert.That(home.RecentlyAdded.Select(item => item.Event!.Id)
            .SequenceEqual(unique.Skip(2).Select(item => item.Event!.Id))).IsTrue();
        await Assert.That(home.SectionStatuses["recently-added"]).IsEqualTo(HomeDiscoverySectionStatus.Available);
    }

    [Test]
    public async Task ReviewedAliasesAreUniqueWithinEveryShelfButMayOverlapAcrossShelves()
    {
        var pool = Pool(3);
        var root = Guid.CreateVersion7();
        pool[0] = pool[0] with { DiscoveryIdentityId = root };
        pool[1] = pool[1] with { DiscoveryIdentityId = root };
        var home = await CreateHandler(new CandidateReader(pool)).QueryAsync(new(), CancellationToken.None);

        foreach (var shelf in new[] { home.Hero, home.UpcomingInArea, home.RecentlyAdded })
        {
            await Assert.That(shelf.Select(item => item.Event!.Id)
                .SequenceEqual([pool[0].Event!.Id, pool[2].Event!.Id])).IsTrue();
        }
    }

    [Test]
    public async Task TimedOutSectionRemainsFailedWhileCompletedIdentitiesStayReserved()
    {
        var reader = new CandidateReader(Pool(10)) { FailUpcoming = true };
        var home = await CreateHandler(reader).QueryAsync(new(), CancellationToken.None);

        await Assert.That(home.Hero.Count).IsEqualTo(10);
        await Assert.That(home.SectionStatuses["upcoming"]).IsEqualTo(HomeDiscoverySectionStatus.Failed);
        await Assert.That(home.SectionStatuses["most-viewed-online"]).IsEqualTo(HomeDiscoverySectionStatus.Empty);
        await Assert.That(home.MostViewedOnline.Count).IsEqualTo(0);
    }

    [Test]
    public async Task UnexhaustedDuplicatePoolReportsBudgetFailureRatherThanEmptySuccess()
    {
        var item = Pool(1)[0];
        var reader = new CandidateReader(Enumerable.Repeat(item, 2000).ToList());
        var home = await CreateHandler(reader).QueryAsync(new(), CancellationToken.None);

        await Assert.That(home.SectionStatuses["hero"]).IsEqualTo(HomeDiscoverySectionStatus.Failed);
        await Assert.That(home.SectionStatuses["upcoming"]).IsEqualTo(HomeDiscoverySectionStatus.Failed);
        await Assert.That(reader.LargestWindow).IsLessThanOrEqualTo(1000);
    }

    [Test]
    public async Task SectionDeadlineCancelsSubscribedReadWithoutLosingCompletedHero()
    {
        var clock = new ControlledClock();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new CandidateReader(Pool(10))
        {
            Read = async (request, token) =>
            {
                if (request.Criteria.SortBy != "date" || request.Criteria.ActorId is not null)
                    return;
                var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = token.Register(() => cancelled.TrySetResult());
                started.TrySetResult();
                await cancelled.Task;
                token.ThrowIfCancellationRequested();
            }
        };
        var pending = CreateHandler(reader, clock: clock).QueryAsync(new(), CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Fire(TimeSpan.FromSeconds(1));
        var home = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(home.Hero.Count).IsEqualTo(10);
        await Assert.That(home.SectionStatuses["upcoming"]).IsEqualTo(HomeDiscoverySectionStatus.Failed);
        await Assert.That(home.SectionStatuses["most-viewed-online"]).IsEqualTo(HomeDiscoverySectionStatus.Empty);
    }

    [Test]
    public async Task CompositeDeadlineKeepsCompletedCuratedSections()
    {
        var clock = new ControlledClock();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lastActor = Guid.CreateVersion7();
        var presets = new PublicEventSectionPresetsConfig(Presets:
        [
            new("first", "First", SortOrder: 1),
            new("last", "Last", Owners: new(ActorIds: [lastActor]), SortOrder: 2)
        ]);
        var reader = new CandidateReader(Pool(100))
        {
            Read = async (request, token) =>
            {
                if (request.Criteria.ActorId != lastActor)
                    return;
                var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = token.Register(() => cancelled.TrySetResult());
                started.TrySetResult();
                await cancelled.Task;
                token.ThrowIfCancellationRequested();
            }
        };
        var pending = CreateHandler(reader, presets: presets, clock: clock)
            .QueryAsync(new(), CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Fire(TimeSpan.FromSeconds(3));
        var home = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(home.CuratedSections.Count).IsEqualTo(1);
        await Assert.That(home.CuratedSections[0].Key).IsEqualTo("first");
        await Assert.That(home.CuratedSections[0].Items.Count).IsEqualTo(10);
        await Assert.That(home.SectionStatuses["curated:last"]).IsEqualTo(HomeDiscoverySectionStatus.Failed);
        await Assert.That(home.SectionStatuses["composite"]).IsEqualTo(HomeDiscoverySectionStatus.Failed);
    }

    [Test]
    public async Task CallerCancellationPropagatesAfterSubscribedReadStarts()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var reader = new CandidateReader(Pool(1))
        {
            Read = async (_, token) =>
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                started.TrySetResult();
                await cancelled.Task;
                token.ThrowIfCancellationRequested();
            }
        };
        var pending = CreateHandler(reader).QueryAsync(new(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.That(async () => await pending).Throws<OperationCanceledException>();
    }

    private static List<EventDiscoveryItemDto> Pool(int count) =>
        Enumerable.Range(0, count).Select(index => new EventDiscoveryItemDto
        {
            Event = new EventListDto
            {
                Id = Guid.CreateVersion7(),
                Title = $"Event {index}",
                EventTypeFullName = null,
                AudienceGenderFullName = null,
                AudienceAgeFullName = null,
                ActorDisplayName = null,
                ActorTypeFullName = null,
                EventStatusFullName = null,
                VisibilityTypeFullName = null,
                EventFormatFullName = null
            }
        }).ToList();

    private static IEnumerable<EventDiscoveryItemDto> Items(HomeDiscoveryDto home) =>
        home.Hero.Concat(home.UpcomingInArea)
            .Concat(home.Spotlight?.Items ?? [])
            .Concat(home.MostViewedInArea)
            .Concat(home.MostViewedOnline)
            .Concat(home.CuratedSections.SelectMany(section => section.Items))
            .Concat(home.RecentlyAdded);

    private static GetHomeDiscoveryQueryHandler CreateHandler(
        CandidateReader reader,
        PublicDiscoveryAreaConfig? area = null,
        PublicEventSectionPresetsConfig? presets = null,
        TimeProvider? clock = null)
    {
        var settings = Substitute.For<IHierarchicalSettingsResolver>();
        if (area is not null)
            settings.ResolveAsync<string>(
                    GovernanceSettingKeys.PublicExperience.DiscoveryAreas,
                    Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
                .Returns(JsonSerializer.Serialize(new PublicDiscoveryAreasConfig(Areas: [area])));
        if (presets is not null)
            settings.ResolveAsync<string>(
                    GovernanceSettingKeys.PublicExperience.EventSectionPresets,
                    Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
                .Returns(JsonSerializer.Serialize(presets));
        var tenant = Substitute.For<ITenantContext>();
        var locations = Substitute.For<ILocationRepository>();
        if (area?.LocationIds is { } locationIds)
            locations.GetExistingTenantLocationIdsAsync(
                    Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(locationIds.ToArray());
        return new GetHomeDiscoveryQueryHandler(
            reader, new ShellReader(), tenant, settings, locations,
            clock ?? TimeProvider.System, NullLogger<GetHomeDiscoveryQueryHandler>.Instance);
    }

    private sealed class ShellReader : IQueryHandler<GetPublicExperienceShellQuery, PublicExperienceShellDto>
    {
        public Task<PublicExperienceShellDto> QueryAsync(
            GetPublicExperienceShellQuery request, CancellationToken cancellationToken) =>
            Task.FromResult(new PublicExperienceShellDto
            {
                PrimaryOrganization = new() { ActorId = Guid.CreateVersion7() }
            });
    }

    private sealed class CandidateReader(List<EventDiscoveryItemDto> pool)
        : IQueryHandler<GetPublicEventDiscoveryRequest, PaginatedResult<EventDiscoveryItemDto>>
    {
        public bool FailUpcoming { get; init; }
        public Func<GetPublicEventDiscoveryRequest, CancellationToken, Task>? Read { get; init; }
        public Func<GetEventListRequest, IReadOnlyList<EventDiscoveryItemDto>>? Candidates { get; init; }
        public int LargestWindow { get; private set; }

        public async Task<PaginatedResult<EventDiscoveryItemDto>> QueryAsync(
            GetPublicEventDiscoveryRequest request, CancellationToken cancellationToken)
        {
            if (Read is not null)
                await Read(request, cancellationToken);
            if (FailUpcoming && request.Criteria.SortBy == "date" && request.Criteria.ActorId is null)
                throw new OperationCanceledException();
            var criteria = request.Criteria;
            var candidates = Candidates?.Invoke(criteria) ?? pool;
            LargestWindow = Math.Max(LargestWindow, criteria.PageNumber * criteria.PageSize);
            return new PaginatedResult<EventDiscoveryItemDto>(
                candidates.Skip((criteria.PageNumber - 1) * criteria.PageSize).Take(criteria.PageSize).ToList(),
                candidates.Count, criteria.PageNumber, criteria.PageSize);
        }
    }

    private sealed class ControlledClock : TimeProvider
    {
        private readonly Dictionary<TimeSpan, ControlledTimer> _timers = [];

        public override DateTimeOffset GetUtcNow() => new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ControlledTimer(callback, state);
            _timers[dueTime] = timer;
            return timer;
        }

        public void Fire(TimeSpan deadline) => _timers[deadline].Fire();
    }

    private sealed class ControlledTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;
        public void Fire()
        {
            if (!_disposed)
                callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
