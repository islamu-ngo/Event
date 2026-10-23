using System.Data.Common;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationContentNoticeCommitTests
{
    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(4, false)]
    [Arguments(1, true)]
    [Arguments(2, true)]
    public async Task Disclosure_approval_and_its_terminal_recheck_cannot_add_unplanned_sources(
        int change, bool replay)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = await PrepareAsync(database, RoleEnum.EventOwner);
        Guid originalActor;
        var replacementActor = Guid.CreateVersion7();
        await using (var setup = database.Context())
        {
            originalActor = (await setup.Events.SingleAsync()).ActorId;
            setup.Actors.Add(new Actor
            {
                Id = replacementActor, ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                UserId = database.ReviewerId,
                Pii = new ActorPii { ActorId = replacementActor, DisplayName = "Unplanned notice parent" }
            });
            await setup.SaveChangesAsync();
        }
        var checkpoint = new CommitCheckpoint(database, expireAtFlush: false);
        var order = new DisclosureSourceOrder(replacementActor, change);
        await using (var context = Context(database, checkpoint, order, new ChangeDisclosureAfterSave(order)))
        {
            var authority = await AuthorityAsync(context, checkpoint);
            var writer = Writer(database, context, authority);
            if (replay)
                await Assert.That((await writer.ExecuteAsync(source)).IsSuccess).IsTrue();
            order.Armed = true;
            var result = await writer.ExecuteAsync(source);
            await Assert.That(result.IsSuccess).IsEqualTo(change == 0);
            await Assert.That(order.ObservedTenant).IsTrue();
            await Assert.That(order.ActorsBeforeTenant.Contains(originalActor)).IsTrue();
            await Assert.That(order.RolesBeforeTenant.Contains((int)RoleEnum.EventOwner)).IsTrue();
            await Assert.That(order.UnplannedSources).IsEqualTo(0);
            await Assert.That(context.Database.CurrentTransaction).IsNull();
        }
        await using var reader = Read(database);
        await Assert.That((await reader.Events.SingleAsync()).ActorId).IsEqualTo(originalActor);
        await Assert.That((await reader.EventRoleAssignments.SingleAsync()).RoleId).IsEqualTo((int)RoleEnum.EventOwner);
        var committed = replay || change == 0;
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(committed ? 1 : 0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts
            .CountAsync(row => row.Id == source.OperationId)).IsEqualTo(committed ? 1 : 0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision)
            .IsEqualTo(committed ? 4L : 3L);
        await AssertNoConsequencesAsync(reader);
    }

    internal sealed class DisclosureSourceOrder(Guid replacementActor, int change) : DbCommandInterceptor
    {
        private readonly HashSet<Guid> _actors = [];
        private readonly HashSet<int> _roles = [];
        private bool _changedAfterSave;
        public bool Armed { get; set; }
        public bool ObservedTenant { get; private set; }
        public Guid[] ActorsBeforeTenant { get; private set; } = [];
        public int[] RolesBeforeTenant { get; private set; } = [];
        public int UnplannedSources { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context) return result;
            var sql = context.GetService<ISqlGenerationHelper>();
            bool Updates(Type type)
            {
                var entity = context.Model.FindEntityType(type)!;
                return command.CommandText.StartsWith(
                    $"UPDATE {sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema())} ", StringComparison.Ordinal);
            }
            if (Updates(typeof(Actor)))
                foreach (DbParameter parameter in command.Parameters)
                    if (parameter.Value is Guid id)
                    {
                        if (ObservedTenant && !_actors.Contains(id)) UnplannedSources++;
                        _actors.Add(id);
                    }
            if (Updates(typeof(Role)))
                foreach (DbParameter parameter in command.Parameters)
                    if (parameter.Value is int id)
                    {
                        if (ObservedTenant && !_roles.Contains(id)) UnplannedSources++;
                        _roles.Add(id);
                    }
            if (!ObservedTenant && Updates(typeof(Tenant)))
            {
                ObservedTenant = true;
                ActorsBeforeTenant = _actors.Order().ToArray();
                RolesBeforeTenant = _roles.Order().ToArray();
                if (change is 1 or 2) await ChangeAsync(context, cancellationToken);
            }
            return result;
        }

        public async Task AfterSaveAsync(ExploreDbContext context, CancellationToken token)
        {
            if (!Armed || _changedAfterSave || change is not (3 or 4)
                || !context.ChangeTracker.Entries<ModerationNoticeDisclosureApproval>().Any()) return;
            _changedAfterSave = true;
            await ChangeAsync(context, token);
        }

        private async Task ChangeAsync(ExploreDbContext context, CancellationToken token)
        {
            if (change is 1 or 3)
                await context.Events.ExecuteUpdateAsync(setters => setters.SetProperty(
                    row => row.ActorId, replacementActor), token);
            else
                await context.EventRoleAssignments.ExecuteUpdateAsync(setters => setters.SetProperty(
                    row => row.RoleId, (int)RoleEnum.RegistrationManager), token);
        }
    }

    private sealed class ChangeDisclosureAfterSave(DisclosureSourceOrder order) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            await order.AfterSaveAsync((ExploreDbContext)eventData.Context!, cancellationToken);
            return result;
        }
    }
}
