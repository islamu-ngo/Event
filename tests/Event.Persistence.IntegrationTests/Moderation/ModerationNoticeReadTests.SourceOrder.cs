using System.Data.Common;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Application.Contracts.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    [Arguments(false, 0)]
    [Arguments(true, 0)]
    [Arguments(false, 1)]
    [Arguments(true, 1)]
    [Arguments(false, 2)]
    [Arguments(true, 2)]
    public async Task Content_notice_disclosure_and_acknowledgement_hold_their_original_sources_before_tenant(
        bool acknowledge, int change)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        Guid eventId;
        Guid actorId;
        var replacementId = Guid.CreateVersion7();
        await using (var setup = fixture.Database.Context())
        {
            var assignment = await setup.EventRoleAssignments.SingleAsync();
            eventId = assignment.EventId;
            actorId = await setup.Events.Where(row => row.Id == eventId).Select(row => row.ActorId).SingleAsync();
            setup.Actors.Add(new Actor
            {
                Id = replacementId,
                ActorTypeId = (int)ActorTypeEnum.User,
                UserId = fixture.Database.ReviewerId,
                Pii = new ActorPii()
            });
            await setup.SaveChangesAsync();
        }
        var checkpoint = new NoticeSourcesBeforeTenant(eventId, replacementId, change);
        await using var reader = fixture.Reader(fixture.Database.SubjectId, null, checkpoint);
        if (acknowledge)
        {
            var result = await reader.Acknowledge(fixture.SubjectNoticeId, Guid.CreateVersion7(), fixture.SnapshotHash);
            await Assert.That(result.IsSuccess).IsEqualTo(change == 0);
        }
        else
            await Assert.That(await reader.DeliverDetail(fixture.SubjectNoticeId)).IsEqualTo(change == 0
                ? ModerationNoticeReadOutcome.Allowed : ModerationNoticeReadOutcome.NotFound);
        await Assert.That(checkpoint.ObservedTenant).IsTrue();
        await Assert.That(checkpoint.ActorsBeforeTenant.Contains(actorId)).IsTrue();
        await Assert.That(checkpoint.RolesBeforeTenant.Contains((int)RoleEnum.EventOwner)).IsTrue();
        await Assert.That(checkpoint.LateEarlyAnchors).IsEqualTo(0);
        await Assert.That(checkpoint.ActorsBeforeTenant.Contains(replacementId)).IsFalse();
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.Events.Where(row => row.Id == eventId).Select(row => row.ActorId).SingleAsync())
            .IsEqualTo(actorId);
        await Assert.That((await inspect.EventRoleAssignments.SingleAsync()).RoleId).IsEqualTo((int)RoleEnum.EventOwner);
        await Assert.That(await inspect.Set<ModerationNoticeNotificationReceipt>().CountAsync())
            .IsEqualTo(acknowledge && change == 0 ? 1 : 0);
    }

    [Test]
    [Arguments(false, 1)]
    [Arguments(true, 1)]
    [Arguments(true, 2)]
    public async Task Representative_expiry_during_final_recipient_checks_cannot_commit_acknowledgement(
        bool colocated, int recipientCheck)
    {
        var until = Now.AddMinutes(1);
        await using var fixture = await NoticeDatabase.CreateAsync(content: true, representativeUntil: until);
        await using var setup = fixture.Database.Context();
        await using var privacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(setup) : null;
        var expiry = new ExpireRepresentativeDuringRecipientCheck(fixture.Clock, until, recipientCheck);
        await using var reader = new ReaderSession(fixture, fixture.Database.SubjectId, null, [expiry], privacy);
        var result = await reader.Acknowledge(fixture.SubjectNoticeId, Guid.CreateVersion7(), fixture.SnapshotHash);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(expiry.ExpiredDuringRecipientCheck).IsTrue();
        await Assert.That(expiry.PrivacyOwned).IsEqualTo(colocated);
        await Assert.That(reader.Context.Database.CurrentTransaction).IsNull();
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Colocated_content_acknowledgement_keeps_its_acquired_plan_through_the_actual_privacy_commit(
        bool changeAfterSave)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        await using var setup = fixture.Database.Context();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateColocatedAsync(setup);
        var checkpoint = new NoticeSourcesBeforeTenant(Guid.Empty, Guid.Empty, 0);
        var saved = new ChangeRepresentativeAfterAcknowledgement(changeAfterSave);
        await using var reader = new ReaderSession(fixture, fixture.Database.SubjectId, null, [checkpoint, saved], privacy);
        var result = await reader.Acknowledge(fixture.SubjectNoticeId, Guid.CreateVersion7(), fixture.SnapshotHash);
        await Assert.That(result.IsSuccess).IsEqualTo(!changeAfterSave);
        await Assert.That(saved.ObservedPrivacyCommitOwner).IsTrue();
        await Assert.That(checkpoint.LateEarlyAnchors).IsEqualTo(0);
        await Assert.That(reader.Context.IdentityFenceOwnsTransaction).IsFalse();
        await Assert.That(reader.Context.Database.CurrentTransaction).IsNull();
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.Set<ModerationNoticeNotificationReceipt>().CountAsync())
            .IsEqualTo(changeAfterSave ? 0 : 1);
        await Assert.That((await inspect.EventRoleAssignments.SingleAsync()).RoleId).IsEqualTo((int)RoleEnum.EventOwner);
    }

    [Test]
    public async Task Prepared_content_notice_cannot_borrow_replacement_sources_during_terminal_validation()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var checkpoint = new NoticeSourcesBeforeTenant(Guid.Empty, Guid.Empty, 0);
        await using var reader = fixture.Reader(fixture.Database.SubjectId, null, checkpoint);
        var pending = await reader.Detail(fixture.SubjectNoticeId);
        var delivered = false;
        var outcome = await pending.Projection!.DiscloseAsync(async (_, token) =>
        {
            await reader.Context.EventRoleAssignments.ExecuteUpdateAsync(
                setters => setters.SetProperty(row => row.RoleId, (int)RoleEnum.RegistrationManager), token);
            return ReadOnlyMemory<byte>.Empty;
        }, (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
        await Assert.That(checkpoint.LateEarlyAnchors).IsEqualTo(0);
        await using var inspect = fixture.Database.Context();
        await Assert.That((await inspect.EventRoleAssignments.SingleAsync()).RoleId).IsEqualTo((int)RoleEnum.EventOwner);
    }

    private sealed class ChangeRepresentativeAfterAcknowledgement(bool change) : SaveChangesInterceptor
    {
        public bool ObservedPrivacyCommitOwner { get; private set; }
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!ObservedPrivacyCommitOwner && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationNoticeNotificationReceipt>().Any())
            {
                ObservedPrivacyCommitOwner = context.IdentityFenceOwnsTransaction;
                if (change)
                    await context.EventRoleAssignments.ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.RoleId, (int)RoleEnum.RegistrationManager), cancellationToken);
            }
            return result;
        }
    }

    private sealed class ExpireRepresentativeDuringRecipientCheck(TestClock clock, DateTimeOffset until, int targetCheck)
        : DbCommandInterceptor
    {
        private int _checks;
        private bool _observedReceipt;
        public bool ExpiredDuringRecipientCheck { get; private set; }
        public bool PrivacyOwned { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!ExpiredDuringRecipientCheck && eventData.Context is ExploreDbContext context)
            {
                _observedReceipt |= context.ChangeTracker.Entries<ModerationNoticeNotificationReceipt>().Any();
                if (!_observedReceipt) return ValueTask.FromResult(result);
                var users = context.Model.FindEntityType(typeof(User))!;
                var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(users.GetTableName()!, users.GetSchema());
                if (command.CommandText.Contains($"FROM {table} AS ", StringComparison.Ordinal) && ++_checks == targetCheck)
                {
                    ExpiredDuringRecipientCheck = true;
                    PrivacyOwned = context.IdentityFenceOwnsTransaction;
                    clock.UtcNow = until;
                }
            }
            return ValueTask.FromResult(result);
        }
    }

    internal sealed class NoticeSourcesBeforeTenant(Guid eventId, Guid replacementActorId, int change) : DbCommandInterceptor
    {
        private readonly HashSet<Guid> _actors = [];
        private readonly HashSet<int> _roles = [];
        public bool ObservedTenant { get; private set; }
        public Guid[] ActorsBeforeTenant { get; private set; } = [];
        public int[] RolesBeforeTenant { get; private set; } = [];
        public int LateEarlyAnchors { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not ExploreDbContext context) return result;
            var sql = context.GetService<ISqlGenerationHelper>();
            bool Updates(Type type)
            {
                var entity = context.Model.FindEntityType(type)!;
                return command.CommandText.StartsWith($"UPDATE {sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema())} ",
                    StringComparison.Ordinal);
            }
            if (Updates(typeof(Actor)))
                foreach (DbParameter parameter in command.Parameters)
                    if (parameter.Value is Guid id) _actors.Add(id);
            if (Updates(typeof(Role)))
                foreach (DbParameter parameter in command.Parameters)
                    if (parameter.Value is int id) _roles.Add(id);
            if (ObservedTenant && (Updates(typeof(Actor)) || Updates(typeof(Role))
                || Updates(typeof(Permission)) || Updates(typeof(RolePermission)))) LateEarlyAnchors++;
            if (!ObservedTenant && Updates(typeof(Tenant)))
            {
                ObservedTenant = true;
                ActorsBeforeTenant = _actors.Order().ToArray();
                RolesBeforeTenant = _roles.Order().ToArray();
                if (change == 1)
                    await context.Events.Where(row => row.Id == eventId).ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.ActorId, replacementActorId), cancellationToken);
                if (change == 2)
                    await context.EventRoleAssignments.Where(row => row.EventId == eventId).ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.RoleId, (int)RoleEnum.RegistrationManager), cancellationToken);
            }
            return result;
        }
    }
}
