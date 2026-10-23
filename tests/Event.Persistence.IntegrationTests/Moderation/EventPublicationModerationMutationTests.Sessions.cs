using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class EventPublicationModerationMutationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SessionPublicationWithoutAdoptionPreservesNativeTransitionAndScheduleSummary(bool qualified)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var session = await ReadySessionPublicationAsync(fixture, clock);
        if (qualified)
        {
            var maintenanceId = Guid.CreateVersion7();
            var host = ModerationHostBinding.Establish(Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), maintenanceId);
            fixture.Context.ServicePrincipals.Add(new ServicePrincipal
            {
                Id = maintenanceId,
                Code = $"session-maintenance-{maintenanceId:N}",
                DisplayName = "Session maintenance"
            });
            fixture.Context.ModerationHostBindings.Add(host);
            fixture.Context.ModerationQualificationEpochs.Add(ModerationQualificationEpoch.Initialize(
                Guid.CreateVersion7(), host, await fixture.Context.Tenants.SingleAsync(row => row.Id == fixture.TenantId)));
            await fixture.Context.SaveChangesAsync();
            fixture.Context.ChangeTracker.Clear();
        }
        var before = await SessionPublicationStateAsync(fixture, session.Id);

        var response = await PublishSessionAsync(fixture, session);

        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        await Assert.That(response.Id).IsEqualTo(session.Id);
        var after = await SessionPublicationStateAsync(fixture, session.Id);
        await Assert.That(after.Status).IsEqualTo((int)EventSessionStatusEnum.Published);
        await Assert.That(after.Stamp).IsNotEqualTo(before.Stamp);
        await Assert.That(before.Parent.SessionCount).IsEqualTo((int?)1);
        await Assert.That(after.Parent.SessionCount).IsEqualTo((int?)2);
        await Assert.That(after.Parent.FirstStart).IsEqualTo(session.StartTime);
        await Assert.That(after.Parent.LastEnd).IsEqualTo(before.Parent.LastEnd);
        await Assert.That(after.Parent.Status).IsEqualTo(before.Parent.Status);
        await Assert.That(after.Outboxes).IsEqualTo(before.Outboxes);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CurrentHumanSessionPublicationIsBlockedByTenantOrInstanceSanctionOrProtection(
        bool instanceScope, bool protective)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var session = await ReadySessionPublicationAsync(fixture, clock);
        var adoption = await AdoptAsync(fixture, clock, instanceScope);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now, protective);
        var before = await SessionPublicationStateAsync(fixture, session.Id);

        var response = await PublishSessionAsync(fixture, session);

        await AssertSessionPublicationRejectedAsync(fixture, session, before, response, "event_session_publish_restricted");
    }

    [Test]
    public async Task UnavailableSelectedHistoryCannotBecomeSessionPublicationNonadoption()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var session = await ReadySessionPublicationAsync(fixture, clock);
        await AdoptAsync(fixture, clock);
        await fixture.Context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync();
        var before = await SessionPublicationStateAsync(fixture, session.Id);

        var response = await PublishSessionAsync(fixture, session);

        await AssertSessionPublicationRejectedAsync(fixture, session, before, response,
            "event_session_publish_authority_unavailable");
    }

    [Test]
    public async Task RetiringProfileSupportDoesNotEraseTheSessionCallersRetainedRestriction()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var session = await ReadySessionPublicationAsync(fixture, clock);
        var adoption = await AdoptAsync(fixture, clock);
        await StoreRestrictionAsync(fixture, adoption, fixture.UserId, clock.Now);
        var declared = ModerationPolicyPublicationProjection.RestoreHistory(adoption.Publication)!.Proposed;
        var retired = await PublishPolicyAsync(fixture, clock, adoption.Host, declared with
        {
            PublicationId = Guid.CreateVersion7(),
            ExpectedRevision = adoption.Publication.Revision,
            SupportedProfiles = []
        }, adoption.Publication.Id);
        await Assert.That(retired.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var before = await SessionPublicationStateAsync(fixture, session.Id);

        var response = await PublishSessionAsync(fixture, session);

        await AssertSessionPublicationRejectedAsync(fixture, session, before, response, "event_session_publish_restricted");
    }

    [Test]
    public async Task ParentPublisherRestrictionDoesNotSubstituteForTheCurrentSessionCaller()
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var session = await ReadySessionPublicationAsync(fixture, clock);
        var adoption = await AdoptAsync(fixture, clock);
        var publisher = NativeUser(Guid.CreateVersion7());
        var publisherActor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = publisher.Id,
            User = publisher,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Different session parent publisher" }
        };
        fixture.Context.Actors.Add(publisherActor);
        var parent = await fixture.Context.Events.SingleAsync(row => row.Id == session.EventId);
        parent.ActorId = publisherActor.Id;
        parent.OrganizerActorId = publisherActor.Id;
        parent.SubmittedByUserId = publisher.Id;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        await StoreRestrictionAsync(fixture, adoption, publisher.Id, clock.Now);
        var before = await SessionPublicationStateAsync(fixture, session.Id);

        var response = await PublishSessionAsync(fixture, session);

        await Assert.That(response.IsSuccess).IsTrue().Because(response.Message);
        await Assert.That(response.Id).IsEqualTo(session.Id);
        var after = await SessionPublicationStateAsync(fixture, session.Id);
        await Assert.That(after.Status).IsEqualTo((int)EventSessionStatusEnum.Published);
        await Assert.That(after.Parent.SessionCount).IsEqualTo((int?)2);
        await Assert.That(after.Outboxes).IsEqualTo(before.Outboxes);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnrestrictedHumanPublicationDoesNotExemptSessionReadiness(bool adopted)
    {
        var clock = new PublicationClock();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock);
        var session = await ReadySessionPublicationAsync(fixture, clock, scheduled: false);
        if (adopted) await AdoptAsync(fixture, clock);
        var before = await SessionPublicationStateAsync(fixture, session.Id);

        var response = await PublishSessionAsync(fixture, session);

        await AssertSessionPublicationRejectedAsync(fixture, session, before, response,
            "event_session_publish_readiness_failed");
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SessionAndParentSummaryRollbackWhenOwningCommitLosesPublicationAuthority(
        bool colocated, bool unavailable)
    {
        var clock = new PublicationClock();
        var checkpoint = new AfterSessionAndSummaryFlush(clock, unavailable);
        ModerationNoticePrivacyFixture? privacy = null;
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
            ConfigureServices(services, () => privacy!, clock, [checkpoint]));
        await using var privacyOwner = colocated
            ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(fixture.Context)
            : await ModerationNoticePrivacyFixture.CreateAsync();
        privacy = privacyOwner;
        var session = await ReadySessionPublicationAsync(fixture, clock);
        var adoption = await AdoptAsync(fixture, clock);
        var startsAt = clock.Now.AddMinutes(1);
        if (!unavailable) await StoreRestrictionAsync(fixture, adoption, fixture.UserId, startsAt);
        var before = await SessionPublicationStateAsync(fixture, session.Id);
        checkpoint.SessionId = session.Id;
        checkpoint.EventId = session.EventId;
        checkpoint.Crossing = startsAt;
        checkpoint.Armed = true;

        var response = await PublishSessionAsync(fixture, session);

        await Assert.That(checkpoint.ObservedPublishedSessionAndSummary).IsTrue();
        await Assert.That(checkpoint.ObservedPrivacyOwner).IsEqualTo((bool?)colocated);
        await AssertSessionPublicationRejectedAsync(fixture, session, before, response,
            "event_session_publish_concurrency_conflict");
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(fixture.Context.IdentityFenceOwnsTransaction).IsFalse();
    }

    [Test]
    public async Task SessionMovedAtNativeAcquisitionCannotFollowAReplacementParent()
    {
        var clock = new PublicationClock();
        var move = new MoveSessionAtPublicationAcquisition();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await using var fixture = await CreateFixtureAsync(privacy, clock, move);
        var session = await ReadySessionPublicationAsync(fixture, clock);
        var replacementSession = await ReadySessionPublicationAsync(fixture, clock);
        await AdoptAsync(fixture, clock);
        var before = await SessionPublicationStateAsync(fixture, session.Id);
        var replacementBefore = await SessionPublicationStateAsync(fixture, replacementSession.Id);
        move.SessionId = session.Id;
        move.ReplacementEventId = replacementSession.EventId;
        move.Armed = true;

        var response = await PublishSessionAsync(fixture, session);

        await Assert.That(move.MovedAtAcquisition).IsTrue();
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Id).IsEqualTo(session.Id);
        fixture.Context.ChangeTracker.Clear();
        var persisted = await fixture.Context.EventSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id);
        await Assert.That(persisted.EventSessionStatusId).IsEqualTo(before.Status);
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(before.Stamp);
        await Assert.That(await ParentSessionPublicationStateAsync(fixture, session.EventId)).IsEqualTo(before.Parent);
        await Assert.That(await SessionPublicationStateAsync(fixture, replacementSession.Id)).IsEqualTo(replacementBefore);
    }

    private static Task<BaseCommandResponse<Guid>> PublishSessionAsync(
        EventVisitorCapabilitySqliteFixture fixture, EventSession session) =>
        fixture.Services.GetRequiredService<ICommandHandler<PublishEventSessionCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new PublishEventSessionCommand
            {
                Id = session.Id,
                Request = new PublishEventSessionRequestDto { ExpectedConcurrencyStamp = session.ConcurrencyStamp }
            }, CancellationToken.None);

    private static async Task<EventSession> ReadySessionPublicationAsync(
        EventVisitorCapabilitySqliteFixture fixture, PublicationClock clock, bool scheduled = true)
    {
        var draft = await ReadyDraftAsync(fixture);
        var parent = await fixture.Context.Events.Include(row => row.Sessions).SingleAsync(row => row.Id == draft.Id);
        parent.Publish(clock.Now.UtcDateTime);
        var session = new EventSession(EventSessionStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            EventId = parent.Id,
            Event = parent,
            TenantId = fixture.TenantId,
            Tenant = null!,
            Title = "Native session publication regression",
            SortOrder = 2,
            EventSessionKindId = (int)EventSessionKindEnum.Talk,
            RegistrationModeId = 1,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        if (scheduled)
        {
            var start = clock.Now.AddDays(1);
            session.Reschedule(UtcInstantRange.Create(start, start.AddHours(1)), "UTC",
                new EventScheduleProjectionCalculator());
        }
        parent.Sessions.Add(session);
        parent.RecalculateScheduleSummaryFromSessions();
        fixture.Context.EventRoleAssignments.Add(EventRoleAssignment.Create(
            fixture.TenantId, parent.Id, fixture.UserId, (int)RoleEnum.EventOwner,
            EventRoleAssignmentStatus.Active, clock.Now.UtcDateTime, null, fixture.UserId));
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return await fixture.Context.EventSessions.AsNoTracking().SingleAsync(row => row.Id == session.Id);
    }

    private static async Task AssertSessionPublicationRejectedAsync(EventVisitorCapabilitySqliteFixture fixture,
        EventSession session, SessionPublicationState before, BaseCommandResponse<Guid> response, string code)
    {
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo(code);
        await Assert.That(response.Id).IsEqualTo(session.Id);
        await Assert.That(await SessionPublicationStateAsync(fixture, session.Id)).IsEqualTo(before);
    }

    private static async Task<SessionPublicationState> SessionPublicationStateAsync(
        EventVisitorCapabilitySqliteFixture fixture, Guid sessionId)
    {
        fixture.Context.ChangeTracker.Clear();
        var session = await fixture.Context.EventSessions.AsNoTracking().SingleAsync(row => row.Id == sessionId);
        return new(session.EventId, session.TenantId, session.EventSessionStatusId, session.ConcurrencyStamp,
            session.UpdatedAt, session.StartTime, session.EndTime,
            await ParentSessionPublicationStateAsync(fixture, session.EventId),
            new(await fixture.Context.OutboxMessages.CountAsync(), await fixture.Context.PdsSyncOutbox.CountAsync()));
    }

    private static async Task<SessionParentState> ParentSessionPublicationStateAsync(
        EventVisitorCapabilitySqliteFixture fixture, Guid eventId)
    {
        var parent = await fixture.Context.Events.AsNoTracking().SingleAsync(row => row.Id == eventId);
        return new(parent.EventStatusId, parent.ConcurrencyStamp, parent.UpdatedAt, parent.SessionCount,
            parent.FirstSessionDate, parent.LastSessionDate, parent.FirstSessionStartUtc,
            parent.LastSessionStartUtc, parent.LastSessionEndUtc);
    }

    private sealed record SessionPublicationState(Guid EventId, Guid TenantId, int Status, Guid Stamp,
        DateTime? UpdatedAt, DateTimeOffset? Start, DateTimeOffset? End, SessionParentState Parent,
        SessionPublicationOutboxes Outboxes);

    private sealed record SessionParentState(int Status, Guid Stamp, DateTime? UpdatedAt, int? SessionCount,
        DateOnly? FirstDate, DateOnly? LastDate, DateTimeOffset? FirstStart, DateTimeOffset? LastStart,
        DateTimeOffset? LastEnd);

    private sealed record SessionPublicationOutboxes(int Notifications, int PdsMessages);

    private sealed class AfterSessionAndSummaryFlush(PublicationClock clock, bool unavailable) : SaveChangesInterceptor
    {
        public Guid SessionId { get; set; }
        public Guid EventId { get; set; }
        public DateTimeOffset Crossing { get; set; }
        public bool Armed { get; set; }
        public bool ObservedPublishedSessionAndSummary { get; private set; }
        public bool? ObservedPrivacyOwner { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context) return result;
            if (!await context.EventSessions.AsNoTracking().AnyAsync(row => row.Id == SessionId
                && row.EventSessionStatusId == (int)EventSessionStatusEnum.Published, cancellationToken)
                || !await context.Events.AsNoTracking().AnyAsync(row => row.Id == EventId
                    && row.SessionCount == 2, cancellationToken)) return result;
            Armed = false;
            ObservedPublishedSessionAndSummary = true;
            ObservedPrivacyOwner = context.IdentityFenceOwnsTransaction;
            if (unavailable)
                await context.Set<ModerationPolicyPublicationEffect>().IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
            else
                clock.Now = Crossing;
            return result;
        }
    }

    private sealed class MoveSessionAtPublicationAcquisition : DbCommandInterceptor
    {
        public Guid SessionId { get; set; }
        public Guid ReplacementEventId { get; set; }
        public bool Armed { get; set; }
        public bool MovedAtAcquisition { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context) return result;
            var entity = context.Model.FindEntityType(typeof(EventSession))!;
            var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            if (!command.CommandText.TrimStart().StartsWith($"UPDATE {table} ", StringComparison.OrdinalIgnoreCase)
                || !command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is Guid id && id == SessionId))
                return result;
            Armed = false;
            MovedAtAcquisition = await context.EventSessions.Where(row => row.Id == SessionId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.EventId, ReplacementEventId), cancellationToken) == 1;
            return result;
        }
    }
}
