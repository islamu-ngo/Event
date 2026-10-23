using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public async Task PreFinding_cannot_issue_a_notice_that_would_disable_existing_recipient_access()
    {
        await using var fixture = await PreFindingFixture.CreateAsync(issueNotice: false);
        for (var index = 0; index < 64; index++)
        {
            var issue = index == 0 ? fixture.Issue : fixture.Issue with { OperationId = Guid.CreateVersion7() };
            await Assert.That((await fixture.ExecuteAsync(fixture.Review.Caller.UserId!.Value, issue)).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
        long before;
        await using (var inspect = fixture.Database.Context())
            before = (await inspect.ModerationQualificationEpochs.SingleAsync()).Revision;
        await Assert.That((await fixture.ExecuteAsync(fixture.Review.Caller.UserId!.Value, fixture.Issue)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var overflow = fixture.Issue with { OperationId = Guid.CreateVersion7() };
        await Assert.That((await fixture.ExecuteAsync(fixture.Review.Caller.UserId!.Value, overflow)).Outcome)
            .IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await using (var inspect = fixture.Database.Context())
        {
            await Assert.That(await inspect.Set<ModerationPreFindingNotice>().CountAsync()).IsEqualTo(64);
            await Assert.That(await inspect.Set<ModerationPreFindingReceipt>().AnyAsync(row => row.Id == overflow.OperationId)).IsFalse();
            await Assert.That((await inspect.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(before);
        }
        var acknowledgement = new AcknowledgeModerationPreFindingNotice(
            Guid.CreateVersion7(), fixture.Case.Id, 1, fixture.NoticeId, 1);
        await Assert.That((await fixture.ExecuteAsync(fixture.Database.CallerId, acknowledgement)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
    }

    [Test]
    [Arguments("issue", 0, false, false)]
    [Arguments("issue", 1, false, false)]
    [Arguments("issue", 2, false, false)]
    [Arguments("acknowledge", 0, false, false)]
    [Arguments("acknowledge", 1, false, false)]
    [Arguments("acknowledge", 2, false, false)]
    [Arguments("respond", 0, false, false)]
    [Arguments("respond", 1, false, false)]
    [Arguments("respond", 2, false, false)]
    [Arguments("disclose", 0, false, false)]
    [Arguments("disclose", 1, false, false)]
    [Arguments("disclose", 2, false, false)]
    [Arguments("evidence", 0, false, false)]
    [Arguments("evidence", 1, false, false)]
    [Arguments("evidence", 2, false, false)]
    [Arguments("issue", 1, true, false)]
    [Arguments("acknowledge", 2, true, false)]
    [Arguments("respond", 1, true, false)]
    [Arguments("issue", 0, false, true)]
    [Arguments("respond", 0, false, true)]
    public async Task PreFinding_content_paths_acquire_original_sources_before_tenant(
        string action, int change, bool replay, bool colocated)
    {
        await using var fixture = await PreFindingFixture.CreateAsync(issueNotice: action != "issue");
        var database = fixture.Database;
        var replacement = Guid.CreateVersion7();
        await using (var seed = database.Context())
        {
            seed.Actors.Add(new Actor
            {
                Id = replacement,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                UserId = fixture.Assessor,
                Pii = new ActorPii { ActorId = replacement, DisplayName = "Replacement" }
            });
            await seed.SaveChangesAsync();
        }
        ModerationPreFindingOperation operation = action switch
        {
            "issue" => fixture.Issue,
            "acknowledge" => new AcknowledgeModerationPreFindingNotice(
                Guid.CreateVersion7(), fixture.Case.Id, 1, fixture.NoticeId, 1),
            _ => fixture.Response()
        };
        var human = action == "issue" ? fixture.Review.Caller.UserId!.Value : database.CallerId;
        if (replay)
            await Assert.That((await fixture.ExecuteAsync(human, operation)).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (action == "evidence")
            await Assert.That((await fixture.ExecuteAsync(database.CallerId, operation)).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var order = new ModerationContentNoticeCommitTests.DisclosureSourceOrder(replacement, change) { Armed = true };
        await using var context = database.Context(order);
        await using var shared = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        var repository = fixture.Repository(context, action == "evidence" ? fixture.Assessor : human,
            privacyAuthority: shared);
        var known = false;
        var writes = 0;
        if (action == "disclose")
            known = await repository.DisclosePreFindingAsync(database.TenantId, fixture.NoticeId, false,
                (_, _) => Task.FromResult<ReadOnlyMemory<byte>>(new byte[] { 1 }),
                (_, _) => { writes++; return Task.CompletedTask; }, default) == ModerationQualificationWriteOutcome.Known;
        else if (action == "evidence")
            await fixture.Privacy.Authority(context, database.Clock).ExecuteSerializedAsync(async token =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await Assert.That(await repository.DiscoverResponseHumansAsync(fixture.Case.Id, token)).IsNotNull();
                known = await repository.AcquireCurrentResponseAsync(fixture.Case.Id, operation.OperationId, token) is not null;
                if (known) known = await repository.RecheckResponseAsync(token);
                return true;
            }, default);
        else
            known = (await repository.ExecutePreFindingAsync(operation, default)).Outcome == ModerationQualificationWriteOutcome.Known;
        await Assert.That(known).IsEqualTo(change == 0);
        await Assert.That(order.ObservedTenant).IsTrue();
        await Assert.That(order.ActorsBeforeTenant.Contains(database.ActorId)).IsTrue();
        await Assert.That(order.RolesBeforeTenant.Contains((int)RoleEnum.EventOwner)).IsTrue();
        await Assert.That(order.UnplannedSources).IsEqualTo(0);
        await Assert.That(order.ActorsBeforeTenant.Contains(replacement)).IsFalse();
        await Assert.That(writes).IsEqualTo(action == "disclose" && change == 0 ? 1 : 0);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var inspect = database.Context();
        await Assert.That((await inspect.Events.SingleAsync(row => row.Id == database.EventId)).ActorId)
            .IsEqualTo(database.ActorId);
        await Assert.That((await inspect.EventRoleAssignments.SingleAsync(row => row.Id == database.RoleId)).RoleId)
            .IsEqualTo((int)RoleEnum.EventOwner);
        if (action is "issue" or "acknowledge" or "respond")
            await Assert.That(await inspect.Set<ModerationPreFindingReceipt>()
                .CountAsync(row => row.Id == operation.OperationId)).IsEqualTo(change == 0 || replay ? 1 : 0);
    }

    [Test]
    public async Task PreFinding_failed_colocated_disclosure_cannot_commit_prepared_native_changes()
    {
        await using var fixture = await PreFindingFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateColocatedAsync(context);
        var original = (await context.Events.AsNoTracking().SingleAsync()).Title;
        var repository = fixture.Repository(context, fixture.Database.CallerId, privacyAuthority: privacy);
        var wrote = false;
        var outcome = await repository.DisclosePreFindingAsync(fixture.Database.TenantId, fixture.NoticeId, false,
            async (_, token) =>
            {
                await Assert.That(context.IdentityFenceOwnsTransaction).IsTrue();
                (await context.Events.SingleAsync(token)).Title = "Must roll back";
                await context.SaveChangesAsync(token);
                fixture.Database.Clock.Now = fixture.Until;
                return new byte[] { 1 };
            }, (_, _) => { wrote = true; return Task.CompletedTask; }, default);
        await Assert.That(outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That(wrote).IsFalse();
        await using var inspect = fixture.Database.Context();
        await Assert.That((await inspect.Events.SingleAsync()).Title).IsEqualTo(original);
    }

    [Test]
    [Arguments(false, 0)]
    [Arguments(false, 1)]
    [Arguments(false, 2)]
    [Arguments(true, 0)]
    [Arguments(true, 1)]
    [Arguments(true, 2)]
    public async Task PreFinding_colocated_commit_rechecks_original_sources_after_disclosure_flush(
        bool responding, int change)
    {
        await using var fixture = await PreFindingFixture.CreateAsync(issueNotice: responding);
        var checkpoint = new PreFindingDisclosureFlush(fixture, change);
        await using var context = fixture.Database.Context(checkpoint, new EnlistPreFindingEvent(checkpoint));
        await using var privacy = await ModerationNoticePrivacyFixture.CreateColocatedAsync(context);
        var originalTitle = (await context.Events.AsNoTracking().SingleAsync()).Title;
        var originalEpoch = (await context.ModerationQualificationEpochs.AsNoTracking().SingleAsync()).Revision;
        ModerationPreFindingOperation operation = responding ? fixture.Response() : fixture.Issue;
        var actor = responding ? fixture.Database.CallerId : fixture.Review.Caller.UserId!.Value;
        var result = await fixture.Repository(context, actor, privacyAuthority: privacy)
            .ExecutePreFindingAsync(operation, default);
        await Assert.That(checkpoint.Flushed).IsTrue();
        await Assert.That(checkpoint.PrivacyOwned).IsTrue();
        await Assert.That(result.Outcome == ModerationQualificationWriteOutcome.Known).IsEqualTo(change == 0);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var inspect = fixture.Database.Context();
        await Assert.That((await inspect.Events.SingleAsync()).Title)
            .IsEqualTo(change == 0 ? PreFindingDisclosureFlush.PreparedTitle : originalTitle);
        await Assert.That((await inspect.EventRoleAssignments.SingleAsync(row => row.Id == fixture.Database.RoleId)).RoleId)
            .IsEqualTo((int)RoleEnum.EventOwner);
        await Assert.That(await inspect.Set<ModerationPreFindingReceipt>().AnyAsync(row => row.Id == operation.OperationId))
            .IsEqualTo(change == 0);
        await Assert.That((await inspect.ModerationQualificationEpochs.SingleAsync()).Revision)
            .IsEqualTo(originalEpoch + (change == 0 ? 1 : 0));
        var notice = await inspect.Set<ModerationPreFindingNotice>().SingleOrDefaultAsync();
        await Assert.That(notice is not null).IsEqualTo(responding || change == 0);
        if (responding) await Assert.That(notice!.ResponseId).IsEqualTo(change == 0 ? operation.OperationId : (Guid?)null);
        await AssertPreFindingPointerGraphAsync(inspect, fixture, responding || change == 0);
    }

    private sealed class EnlistPreFindingEvent(PreFindingDisclosureFlush checkpoint) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (checkpoint.Enlisted || eventData.Context is not ExploreDbContext primary
                || !primary.ChangeTracker.Entries<ModerationPreFindingReceipt>().Any()) return result;
            checkpoint.Enlisted = true;
            if (!primary.IdentityFenceOwnsTransaction)
                throw new InvalidOperationException("The actual privacy authority must own this primary commit.");
            (await primary.Events.SingleAsync(cancellationToken)).Title = PreFindingDisclosureFlush.PreparedTitle;
            await primary.SaveChangesAsync(cancellationToken);
            return result;
        }
    }

    [Test]
    public async Task Participant_preFinding_acquires_original_investigation_humans_before_tenant()
    {
        await using var fixture = await InvestigationFixture.CreateAsync();
        await Assert.That((await fixture.ExecuteAsync()).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        Guid caseId;
        Guid[] expected;
        await using (var setup = fixture.Database.Context())
        {
            var writer = new ModerationCaseAttributionRepository(setup,
                InvestigationAttributionOwner(setup, fixture), fixture.Review.Caller, fixture.Database.Machine);
            var attributed = await writer.AttributeAsync(InvestigationAttribution(fixture), default);
            await Assert.That(attributed).IsNotNull();
            caseId = attributed!.Id;
            var investigation = await setup.ModerationParticipantIncidentResponsibilitySources
                .AsNoTracking().SingleAsync(row => row.Id == fixture.Request.SourceId);
            var narrative = await setup.ModerationCaseNarratives.AsNoTracking()
                .SingleAsync(row => row.Id == investigation.OriginalNarrativeId);
            var policy = await setup.ModerationCaseNarrativePolicySources.AsNoTracking()
                .SingleAsync(row => row.Purpose == ModerationCaseNarrativePurpose.ParticipantInvestigation);
            expected = [narrative.ReporterUserId, policy.Ownership.PrivacyControllerHumanId,
                policy.Ownership.GovernanceControllerHumanId];
        }
        var order = new InvestigationUsersBeforeTenant();
        await using var context = fixture.Database.Context(order);
        await fixture.Privacy.Authority(context, fixture.Database.Clock).ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var authority = await new ModerationQualificationAuthority(context,
                NarrativeAdmin(context, fixture.Review.Caller), fixture.Database.Machine)
                .AcquirePreFindingAsync(caseId, [], token);
            await Assert.That(authority).IsNotNull();
            foreach (var human in expected)
            {
                await Assert.That(authority!.FencedHumans.Contains(human)).IsTrue();
                await Assert.That(order.UsersBeforeTenant.Contains(human)).IsTrue();
            }
            await Assert.That(order.ObservedTenant).IsTrue();
            return true;
        }, default);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private sealed class InvestigationUsersBeforeTenant : DbCommandInterceptor
    {
        private readonly HashSet<Guid> _users = [];
        public bool ObservedTenant { get; private set; }
        public IReadOnlyCollection<Guid> UsersBeforeTenant { get; private set; } = [];

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (ObservedTenant || eventData.Context is not ExploreDbContext primary)
                return ValueTask.FromResult(result);
            var sql = primary.GetService<ISqlGenerationHelper>();
            bool Updates(Type type)
            {
                var entity = primary.Model.FindEntityType(type)!;
                return command.CommandText.StartsWith(
                    $"UPDATE {sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema())} SET ", StringComparison.Ordinal);
            }
            if (Updates(typeof(User)))
                foreach (DbParameter parameter in command.Parameters)
                    if (Guid.TryParse(parameter.Value?.ToString(), out var id)) _users.Add(id);
            if (Updates(typeof(Tenant)))
            {
                UsersBeforeTenant = _users.ToArray();
                ObservedTenant = true;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PreFindingDisclosureFlush(PreFindingFixture fixture, int change) : DbCommandInterceptor
    {
        public const string PreparedTitle = "Prepared pre-finding source";
        public bool Enlisted { get; set; }
        public bool Flushed { get; private set; }
        public bool PrivacyOwned { get; private set; }

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Enlisted || Flushed || eventData.Context is not ExploreDbContext primary) return result;
            var entity = primary.Model.FindEntityType(typeof(EventDiscoveryRevision))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var sql = primary.GetService<ISqlGenerationHelper>();
            var column = sql.DelimitIdentifier(entity.FindProperty(nameof(EventDiscoveryRevision.DisclosureEpoch))!
                .GetColumnName(table)!);
            var update = $"UPDATE {sql.DelimitIdentifier(table.Name, table.Schema)} SET {column} = ";
            if (command.Parameters.Count != 3 || !command.CommandText.StartsWith(update, StringComparison.Ordinal))
                return result;
            Flushed = true;
            PrivacyOwned = primary.IdentityFenceOwnsTransaction;
            if (result != 1 || !PrivacyOwned || primary.Database.CurrentTransaction is null)
                throw new InvalidOperationException("Discovery must flush before the actual co-located commit.");
            if (change == 1)
                await primary.EventRoleAssignments.Where(row => row.Id == fixture.Database.RoleId)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.RoleId, (int)RoleEnum.RegistrationManager), cancellationToken);
            if (change == 2) fixture.Database.Clock.Now = fixture.Until;
            return result;
        }
    }
}
