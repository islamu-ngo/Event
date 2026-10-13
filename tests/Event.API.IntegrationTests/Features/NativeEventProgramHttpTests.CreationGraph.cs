using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventProgramHttpTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SqliteCreationGraph_OmittedScheduleAndMixedDraftRetainNullsWithoutInferredDays(bool mixed)
    {
        await using var factory = await ProgramFactory.CreateAsync(useSqlite: true);
        using var owner = factory.Client(factory.OwnerId);
        using var anonymous = factory.CreateClient();
        var title = $"Unscheduled graph {Guid.CreateVersion7():N}";
        var unscheduled = GraphSession("Prepared discussion");
        var sessions = mixed
            ? new[] { unscheduled, GraphSession("Scheduled discussion", scheduled: true) }
            : new[] { unscheduled, GraphSession("Another prepared discussion") };
        using var response = await owner.PostAsJsonAsync("/api/event", GraphRequest(title, sessions));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created)
            .Because(await response.Content.ReadAsStringAsync());
        var id = (await JsonAsync(response)).GetProperty("id").GetGuid();

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var stored = await scope.ServiceProvider.GetRequiredService<IEventSessionRepository>().GetSessionsByEvent(id);
        await Assert.That(stored.Count).IsEqualTo(2);
        var prepared = stored.Single(session => session.Title == "Prepared discussion");
        await Assert.That(prepared.StartTime).IsNull();
        await Assert.That(prepared.EndTime).IsNull();
        await Assert.That(prepared.LocalStartDate).IsNull();
        await Assert.That(prepared.LocalEndDate).IsNull();
        await Assert.That(prepared.LocalStartTime).IsNull();
        await Assert.That(prepared.LocalEndTime).IsNull();
        await Assert.That(prepared.LocalStartMinuteOfDay).IsNull();
        await Assert.That(prepared.LocalEndMinuteOfDay).IsNull();
        await Assert.That(prepared.EventDayId).IsNull();
        await Assert.That(prepared.Description).IsEqualTo("Prepared metadata");
        await Assert.That(prepared.Slug).IsEqualTo("prepared-discussion");
        await Assert.That(prepared.MaxAudienceAttendees).IsEqualTo(37);
        await Assert.That(prepared.SortOrder).IsEqualTo(9);
        await Assert.That(stored.All(session => session.EventSessionStatusId == (int)EventSessionStatusEnum.Draft)).IsTrue();
        var parent = await scope.ServiceProvider.GetRequiredService<IEventRepository>().GetById(id);
        await Assert.That(parent!.FirstSessionStartUtc).IsNull();
        await Assert.That(parent.LastSessionEndUtc).IsNull();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await db.EventDays.CountAsync(day => day.EventId == id)).IsEqualTo(0);
        await Assert.That(parent.EventProvenanceTypeId).IsEqualTo((int)EventProvenanceTypeEnum.CommunityReported);
        await Assert.That(parent.SubmittedByUserId).IsEqualTo(factory.OwnerId);
        await Assert.That(await db.EventRoleAssignments.CountAsync(grant => grant.EventId == id
            && grant.UserId == factory.OwnerId && grant.RoleId == (int)RoleEnum.EventOwner)).IsEqualTo(0);
        await Assert.That(await scope.ServiceProvider.GetRequiredService<IEventSessionRepository>()
            .GetPublicSessionsByEventAsync(id, default)).IsEmpty();

        var managed = await SummaryAsync(owner, Managed(id));
        var item = Items(managed).Single(item => item.GetProperty("sessionId").GetGuid() == prepared.Id);
        await Assert.That(item.GetProperty("title").GetString()).IsEqualTo("Prepared discussion");
        foreach (var field in new[] { "startsAtUtc", "endsAtUtc", "localDate", "localStartTime", "localEndTime" })
            await Assert.That(AbsentOrNull(item, field)).IsTrue();
        await Assert.That(item.GetProperty("capacity").GetInt32()).IsEqualTo(37);
        using var publicResponse = await anonymous.GetAsync(Public(id));
        await ProblemAsync(publicResponse, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SqliteCreationGraph_UnscheduledDraftPreservesExplicitDayRoomAndVenueReferences()
    {
        await using var factory = await ProgramFactory.CreateAsync(useSqlite: true);
        using var owner = factory.Client(factory.OwnerId);
        var session = GraphSession("Referenced prepared discussion");
        session["locationId"] = factory.LocationId;
        session["roomTempKey"] = "prepared-room";
        session["dayTempKey"] = "prepared-day";
        var request = GraphRequest($"Explicit draft graph {Guid.CreateVersion7():N}", [session]);
        request["days"] = new[] { new
        {
            tempKey = "prepared-day", localDate = "2040-07-21", label = "Prepared day",
            description = "Day metadata", bannerText = "Day banner", isPublished = false, sortOrder = 4
        } };
        request["rooms"] = new[] { new
        {
            tempKey = "prepared-room", locationId = factory.LocationId, name = "Prepared room",
            slug = "prepared-room", description = "Room metadata", capacity = 51, sortOrder = 3
        } };
        using var response = await owner.PostAsJsonAsync("/api/event", request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var id = (await JsonAsync(response)).GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var stored = (await scope.ServiceProvider.GetRequiredService<IEventSessionRepository>().GetSessionsByEvent(id)).Single();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var day = await db.EventDays.AsNoTracking().SingleAsync(day => day.EventId == id);
        var room = await db.LocationRooms.AsNoTracking().SingleAsync(room => room.Id == stored.RoomId);
        await Assert.That(stored.StartTime).IsNull();
        await Assert.That(stored.EndTime).IsNull();
        await Assert.That(stored.LocalStartDate).IsNull();
        await Assert.That(stored.EventDayId).IsEqualTo(day.Id);
        await Assert.That(day.Label).IsEqualTo("Prepared day");
        await Assert.That(day.Description).IsEqualTo("Day metadata");
        await Assert.That(day.BannerText).IsEqualTo("Day banner");
        await Assert.That(day.IsPublished).IsFalse();
        await Assert.That(day.LocalDate).IsEqualTo(new DateOnly(2040, 7, 21));
        await Assert.That(room.Name).IsEqualTo("Prepared room");
        await Assert.That(room.Description).IsEqualTo("Room metadata");
        await Assert.That(room.Capacity).IsEqualTo(51);
        await Assert.That(stored.LocationId).IsEqualTo(factory.LocationId);
        await Assert.That(room.LocationId).IsEqualTo(factory.LocationId);
        var placement = await db.EventLocations.AsNoTracking().SingleAsync(placement => placement.Id == stored.EventLocationId);
        await Assert.That(placement.EventId).IsEqualTo(id);
        await Assert.That(placement.LocationId).IsEqualTo(factory.LocationId);
    }

    [Test]
    public async Task SqliteCreationGraph_DuplicateLocalDayDatesRejectBeforeAnyGraphWrite()
    {
        await using var factory = await ProgramFactory.CreateAsync(useSqlite: true);
        using var owner = factory.Client(factory.OwnerId);
        var before = await GraphStorageAsync(factory);
        var session = GraphSession("Prepared second-day discussion");
        session["dayTempKey"] = "second";
        var request = GraphRequest($"Duplicate day graph {Guid.CreateVersion7():N}", [session]);
        request["days"] = new[]
        {
            new { tempKey = "first", localDate = "2040-07-21", label = "First", sortOrder = 1 },
            new { tempKey = "second", localDate = "2040-07-21", label = "Second", sortOrder = 2 }
        };
        using var response = await owner.PostAsJsonAsync("/api/event", request);
        await GraphFieldProblemAsync(response, "Days[1].LocalDate");
        await Assert.That(await GraphStorageAsync(factory)).IsEqualTo(before);
    }

    [Test]
    public async Task SqliteCreationGraph_PublicationWithUnscheduledChildRejectsBeforeAnyGraphWrite()
    {
        await using var factory = await ProgramFactory.CreateAsync(useSqlite: true);
        using var owner = factory.Client(factory.OwnerId);
        var before = await GraphStorageAsync(factory);
        var request = GraphRequest($"Rejected publication {Guid.CreateVersion7():N}",
            [GraphSession("Scheduled child", scheduled: true), GraphSession("Unscheduled child")],
            published: true);
        using var response = await owner.PostAsJsonAsync("/api/event", request);
        await GraphFieldProblemAsync(response, "Sessions[1].StartTime");
        await Assert.That(await GraphStorageAsync(factory)).IsEqualTo(before);
    }

    [Test]
    [Arguments(SessionEndTimeType.Fixed)]
    [Arguments(SessionEndTimeType.OpenEnded)]
    public async Task SqliteCreationGraph_ValidFixedAndStartOnlyOpenEndedSchedulesPreserveNativeInstants(
        SessionEndTimeType endTimeType)
    {
        await using var factory = await ProgramFactory.CreateAsync(useSqlite: true);
        using var owner = factory.Client(factory.OwnerId);
        var session = GraphSession("Scheduled native bounds");
        session["startTime"] = "2040-07-21T23:30:00+02:00";
        session["endTimeType"] = (int)endTimeType;
        if (endTimeType == SessionEndTimeType.Fixed)
            session["endTime"] = "2040-07-22T01:00:00+02:00";
        using var response = await owner.PostAsJsonAsync("/api/event",
            GraphRequest($"Schedule truth table {Guid.CreateVersion7():N}", [session]));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var id = (await JsonAsync(response)).GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var stored = (await scope.ServiceProvider.GetRequiredService<IEventSessionRepository>().GetSessionsByEvent(id)).Single();
        await Assert.That(stored.StartTime).IsEqualTo(new DateTimeOffset(2040, 7, 21, 21, 30, 0, TimeSpan.Zero));
        await Assert.That(stored.LocalStartDate).IsEqualTo(new DateOnly(2040, 7, 21));
        await Assert.That(stored.LocalStartTime).IsEqualTo(new TimeOnly(23, 30));
        await Assert.That(stored.EndTimeType).IsEqualTo(endTimeType);
        if (endTimeType == SessionEndTimeType.Fixed)
        {
            await Assert.That(stored.EndTime).IsEqualTo(new DateTimeOffset(2040, 7, 21, 23, 0, 0, TimeSpan.Zero));
            await Assert.That(stored.LocalEndDate).IsEqualTo(new DateOnly(2040, 7, 22));
            await Assert.That(stored.LocalEndTime).IsEqualTo(new TimeOnly(1, 0));
        }
        else
        {
            await Assert.That(stored.EndTime).IsNull();
            await Assert.That(stored.LocalEndDate).IsNull();
            await Assert.That(stored.LocalEndTime).IsNull();
            await Assert.That(stored.LocalEndMinuteOfDay).IsNull();
        }
        var managed = Items(await SummaryAsync(owner, Managed(id))).Single();
        await Assert.That(managed.GetProperty("startsAtUtc").GetDateTimeOffset())
            .IsEqualTo(new DateTimeOffset(2040, 7, 21, 21, 30, 0, TimeSpan.Zero));
        await Assert.That(AbsentOrNull(managed, "endsAtUtc")).IsEqualTo(endTimeType == SessionEndTimeType.OpenEnded);
    }

    [Test]
    [Arguments("fixed-missing-end", "Sessions[0].EndTime")]
    [Arguments("fixed-missing-start", "Sessions[0].StartTime")]
    [Arguments("open-ended-end-only", "Sessions[0].StartTime")]
    [Arguments("open-ended-with-end", "Sessions[0].EndTime")]
    [Arguments("equal", "Sessions[0].EndTime")]
    [Arguments("reversed", "Sessions[0].EndTime")]
    [Arguments("unscheduled-relative-end", "Sessions[0].StartTime")]
    [Arguments("unscheduled-relative-start", "Sessions[0].StartTime")]
    public async Task SqliteCreationGraph_InvalidScheduleTruthTableReturnsFieldErrorsWithoutWrites(
        string scenario, string field)
    {
        await using var factory = await ProgramFactory.CreateAsync(useSqlite: true);
        using var owner = factory.Client(factory.OwnerId);
        var before = await GraphStorageAsync(factory);
        var session = GraphSession("Invalid native bounds");
        switch (scenario)
        {
            case "fixed-missing-end":
                session["startTime"] = "2040-07-21T09:00:00+02:00";
                break;
            case "fixed-missing-start":
                session["endTime"] = "2040-07-21T10:00:00+02:00";
                break;
            case "open-ended-end-only":
                session["endTimeType"] = (int)SessionEndTimeType.OpenEnded;
                session["endTime"] = "2040-07-21T10:00:00+02:00";
                break;
            case "open-ended-with-end":
                session["endTimeType"] = (int)SessionEndTimeType.OpenEnded;
                session["startTime"] = "2040-07-21T09:00:00+02:00";
                session["endTime"] = "2040-07-21T10:00:00+02:00";
                break;
            case "equal":
            case "reversed":
                session["startTime"] = "2040-07-21T09:00:00+02:00";
                session["endTime"] = scenario == "equal" ? "2040-07-21T09:00:00+02:00" : "2040-07-21T08:59:00+02:00";
                break;
            case "unscheduled-relative-end":
                session["endTimeType"] = (int)SessionEndTimeType.RelativeToPrayer;
                break;
            case "unscheduled-relative-start":
                session["islamicAspect"] = new { startTimeType = (int)SessionStartTimeType.RelativeToPrayer };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        using var response = await owner.PostAsJsonAsync("/api/event",
            GraphRequest($"Rejected schedule {Guid.CreateVersion7():N}", [session]));
        await GraphFieldProblemAsync(response, field);
        await Assert.That(await GraphStorageAsync(factory)).IsEqualTo(before);
    }

    [Test]
    [Arguments("foreign-location", "Sessions[0].LocationId")]
    [Arguments("foreign-room", "Sessions[0].RoomId")]
    [Arguments("missing-room", "Sessions[0].RoomId")]
    [Arguments("missing-day", "Sessions[0].DayTempKey")]
    [Arguments("missing-location-key", "Sessions[0].LocationTempKey")]
    [Arguments("missing-room-key", "Sessions[0].RoomTempKey")]
    [Arguments("missing-media", "Sessions[0].FeaturedImageId")]
    [Arguments("duplicate-session-key", "Sessions[1].TempKey")]
    public async Task SqliteCreationGraph_InvalidAndForeignReferencesFailClosedWithFieldErrors(
        string attack, string field)
    {
        await using var factory = await ProgramFactory.CreateAsync(useSqlite: true);
        using var owner = factory.Client(factory.OwnerId);
        var before = await GraphStorageAsync(factory);
        // A complete schedule prevents legacy schedule validation from masking reference failures.
        var session = GraphSession("Reference attack", scheduled: true);
        switch (attack)
        {
            case "foreign-location": session["locationId"] = factory.ForeignLocationId; break;
            case "foreign-room": session["roomId"] = factory.ForeignRoomId; break;
            case "missing-room": session["roomId"] = Guid.CreateVersion7(); break;
            case "missing-day": session["dayTempKey"] = "missing-day"; break;
            case "missing-location-key": session["locationTempKey"] = "missing-location"; break;
            case "missing-room-key": session["roomTempKey"] = "missing-room"; break;
            case "missing-media": session["featuredImageId"] = Guid.CreateVersion7(); break;
            case "duplicate-session-key": session["tempKey"] = "duplicate"; break;
            default: throw new ArgumentOutOfRangeException(nameof(attack));
        }
        var sessions = attack == "duplicate-session-key" ? new[] { session, new Dictionary<string, object?>(session) } : [session];
        using var response = await owner.PostAsJsonAsync("/api/event",
            GraphRequest($"Rejected reference {Guid.CreateVersion7():N}", sessions));
        await GraphFieldProblemAsync(response, field);
        await Assert.That(await GraphStorageAsync(factory)).IsEqualTo(before);
        await Assert.That((await JsonAsync(response)).GetRawText()).DoesNotContain("Foreign tenant venue");
        await Assert.That((await JsonAsync(response)).GetRawText()).DoesNotContain("Foreign tenant room");
    }

    [Test]
    public async Task SqliteCreationGraph_FailureAfterFirstPersistedChildRollsBackRootChildrenOwnerAndOutbox()
    {
        var title = $"Interrupted native graph {Guid.CreateVersion7():N}";
        var failure = new CreationGraphChildFailure(title);
        await using var factory = await ProgramFactory.CreateAsync(
            useSqlite: true, creationGraphInterceptor: failure, organizerPublisher: true);
        using var owner = factory.Client(factory.OwnerId);
        var before = await GraphStorageAsync(factory);
        using var response = await owner.PostAsJsonAsync("/api/event", GraphRequest(title,
            [GraphSession("First child", scheduled: true), GraphSession("Second child", scheduled: true)],
            organizationId: factory.OrganizationId));
        await ProblemAsync(response, HttpStatusCode.InternalServerError);
        await Assert.That(failure.RootWritten).IsTrue();
        await Assert.That(failure.OwnerWritten).IsTrue();
        await Assert.That(failure.ChildWritten).IsTrue();
        await Assert.That(failure.Injected).IsTrue();
        await Assert.That(await GraphStorageAsync(factory)).IsEqualTo(before);
    }

    [Test]
    public async Task SqliteCreationGraph_PreCommitPublicationFailureRollsBackWrittenChildrenOwnerAndOutbox()
    {
        var title = $"Interrupted publication graph {Guid.CreateVersion7():N}";
        var failure = new CreationGraphCommitFailure(title);
        await using var factory = await ProgramFactory.CreateAsync(
            useSqlite: true, creationGraphInterceptor: failure, organizerPublisher: true);
        using var owner = factory.Client(factory.OwnerId);
        var before = await GraphStorageAsync(factory);
        using var response = await owner.PostAsJsonAsync("/api/event", GraphRequest(title,
            [GraphSession("First published child", scheduled: true), GraphSession("Second published child", scheduled: true)],
            published: true, organizationId: factory.OrganizationId));
        await ProblemAsync(response, HttpStatusCode.InternalServerError);
        await Assert.That(failure.ChildrenWritten).IsEqualTo(2);
        await Assert.That(failure.OwnerWritten).IsTrue();
        await Assert.That(failure.OutboxWritten).IsTrue();
        await Assert.That(failure.Injected).IsTrue();
        await Assert.That(await GraphStorageAsync(factory)).IsEqualTo(before);
    }

    private static Dictionary<string, object?> GraphSession(string title, bool scheduled = false)
    {
        var session = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["description"] = "Prepared metadata",
            ["slug"] = title.ToLowerInvariant().Replace(' ', '-'),
            ["sortOrder"] = 9,
            ["maxAudienceAttendees"] = 37
        };
        if (scheduled)
        {
            session["startTime"] = "2040-07-21T09:00:00+02:00";
            session["endTime"] = "2040-07-21T10:00:00+02:00";
        }
        return session;
    }

    private static Dictionary<string, object?> GraphRequest(
        string title, Dictionary<string, object?>[] sessions, bool published = false,
        Guid? organizationId = null) => new()
        {
            ["title"] = title,
            ["description"] = "Native creation graph",
            ["eventTimeZoneId"] = "Europe/Brussels",
            ["eventStatusId"] = (int)(published ? EventStatusEnum.Published : EventStatusEnum.Draft),
            ["organizationId"] = organizationId,
            ["participationConfiguration"] = new
            {
                participationHandlingModeId = (int)ParticipationHandlingModeEnum.InformationOnly,
                advanceRegistrationObligationId = (int)AdvanceRegistrationObligationEnum.NotApplicable
            },
            ["sessions"] = sessions
        };

    private static async Task GraphFieldProblemAsync(HttpResponseMessage response, string field)
    {
        await ProblemAsync(response, HttpStatusCode.BadRequest);
        // Controller Produces metadata retains the native command failure media contract.
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
        var problem = await JsonAsync(response);
        var errors = problem.GetProperty("errors").EnumerateObject().ToArray();
        // The existing response groups command errors under "event". A native validator may
        // retain the property path in that group's values or expose it as an error key.
        await Assert.That(errors.Any(error =>
            error.Name.Equals(field, StringComparison.OrdinalIgnoreCase)
            || error.Value.EnumerateArray().Any(value =>
                value.GetString()!.Contains(field, StringComparison.OrdinalIgnoreCase)))).IsTrue();
    }

    private sealed record GraphStorage(int Roots, int Sessions, int Days, int Rooms, int Placements,
        int Owners, int Outbox, int FederationOutbox, int Participation);

    private static async Task<GraphStorage> GraphStorageAsync(ProgramFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        return new(await db.Events.CountAsync(), await db.EventSessions.CountAsync(), await db.EventDays.CountAsync(),
            await db.LocationRooms.CountAsync(), await db.EventLocations.CountAsync(), await db.EventRoleAssignments.CountAsync(),
            await db.OutboxMessages.CountAsync(), await db.PdsSyncOutbox.CountAsync(),
            await db.EventParticipationConfigurations.CountAsync());
    }

    private sealed class CreationGraphChildFailure(string rootTitle) : SaveChangesInterceptor
    {
        public bool RootWritten { get; private set; }
        public bool OwnerWritten { get; private set; }
        public bool ChildWritten { get; private set; }
        public bool Injected { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Injected || eventData.Context is not ExploreDbContext db
                || db.Database.CurrentTransaction is null)
                return result;
            var child = db.ChangeTracker.Entries<EventSession>()
                .FirstOrDefault(entry => entry.Entity.Title == "First child");
            if (child is null)
                return result;
            var id = child.Entity.EventId;
            RootWritten = await db.Events.AnyAsync(root => root.Id == id && root.Title == rootTitle, cancellationToken);
            ChildWritten = await db.EventSessions.AnyAsync(session => session.Id == child.Entity.Id, cancellationToken);
            OwnerWritten = await db.EventRoleAssignments.AnyAsync(grant => grant.EventId == id
                && grant.RoleId == (int)RoleEnum.EventOwner, cancellationToken);
            if (!RootWritten || !ChildWritten || !OwnerWritten)
                throw new InvalidOperationException("The failure seam did not reach a persisted native graph.");
            Injected = true;
            throw new CreationGraphWriteFailureException();
        }
    }

    private sealed class CreationGraphCommitFailure(string rootTitle) : DbTransactionInterceptor
    {
        public int ChildrenWritten { get; private set; }
        public bool OwnerWritten { get; private set; }
        public bool OutboxWritten { get; private set; }
        public bool Injected { get; private set; }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Injected || eventData.Context is not ExploreDbContext db)
                return result;
            var rootId = await db.Events.Where(root => root.Title == rootTitle)
                .Select(root => (Guid?)root.Id).SingleOrDefaultAsync(cancellationToken);
            if (rootId is null)
                return result;
            ChildrenWritten = await db.EventSessions.CountAsync(session => session.EventId == rootId.Value, cancellationToken);
            OwnerWritten = await db.EventRoleAssignments.AnyAsync(grant => grant.EventId == rootId.Value
                && grant.RoleId == (int)RoleEnum.EventOwner, cancellationToken);
            OutboxWritten = await db.OutboxMessages.AnyAsync(message => message.AggregateId == rootId.Value, cancellationToken);
            if (ChildrenWritten != 2 || !OwnerWritten || !OutboxWritten)
                throw new InvalidOperationException("The commit failure seam did not observe a complete published graph and outbox.");
            Injected = true;
            throw new CreationGraphWriteFailureException();
        }
    }

    private sealed class CreationGraphWriteFailureException : Exception;
}
