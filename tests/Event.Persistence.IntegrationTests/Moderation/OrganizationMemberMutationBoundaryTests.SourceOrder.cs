using System.Data.Common;
using Explore.Application.Features.OrganizationMembers.Handlers.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class OrganizationMemberMutationBoundaryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Membership_changes_hold_the_organization_and_its_actor_before_tenant(bool colocated)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var (_, target) = await SeedAsync(database);
        var actorId = Guid.CreateVersion7();
        Guid organizationId;
        await using (var seed = database.Context())
        {
            organizationId = await seed.OrganizationMembers.Where(row => row.Id == target)
                .Select(row => row.OrganizationTenant.OrganizationId).SingleAsync();
            seed.Actors.Add(new Actor
            {
                Id = actorId,
                OrganizationId = organizationId,
                ActorTypeId = (int)ActorTypeEnum.Organization,
                ActorType = null!,
                Pii = new ActorPii { ActorId = actorId, DisplayName = "Native organization" }
            });
            await seed.SaveChangesAsync();
        }
        var order = new OrganizationSourcesBeforeTenant(organizationId, actorId);
        await using var context = database.Context(order);
        await using var shared = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        await using var session = CreateBoundary(context, (shared ?? database.Privacy).Authority(context, TimeProvider.System), database.AdministratorId);
        await session.InitializeAsync();
        order.Armed = true;
        var handler = new UpdateOrganizationMemberRoleCommandHandler(new OrganizationMemberRepository(context),
            new OrganizationRepository(context), new UserContext(session.Http), session.Boundary, session.Preparation);
        var revision = await RevisionAsync(context);
        var result = await handler.ExecuteAsync(new()
        {
            UpdateOrganizationMemberRoleDto = new()
            { Id = target, Role = RoleEnum.OrgAdmin }
        }, default);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(order.ObservedTenant).IsTrue();
        await Assert.That(order.OrganizationWasHeld).IsTrue();
        await Assert.That(order.ActorWasHeld).IsTrue();
        await Assert.That(order.PrivacyOwned).IsEqualTo(colocated);
        await Assert.That(await context.OrganizationMembers.Where(row => row.Id == target).Select(row => row.RoleId).SingleAsync())
            .IsEqualTo((int)RoleEnum.OrgAdmin);
        await Assert.That(await RevisionAsync(context)).IsEqualTo(revision + 1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private sealed class OrganizationSourcesBeforeTenant(Guid organizationId, Guid actorId) : DbCommandInterceptor
    {
        private bool _organization;
        private bool _actor;
        public bool Armed { get; set; }
        public bool ObservedTenant { get; private set; }
        public bool OrganizationWasHeld { get; private set; }
        public bool ActorWasHeld { get; private set; }
        public bool PrivacyOwned { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context || ObservedTenant) return ValueTask.FromResult(result);
            var sql = context.GetService<ISqlGenerationHelper>();
            bool Updates<TEntity>()
            {
                var entity = context.Model.FindEntityType(typeof(TEntity))!;
                return command.CommandText.StartsWith($"UPDATE {sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema())} ",
                    StringComparison.Ordinal);
            }
            bool Contains(Guid id) => command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is Guid value && value == id);
            if (Updates<Organization>() && Contains(organizationId)) _organization = true;
            if (Updates<Actor>() && Contains(actorId)) _actor = true;
            if (Updates<Tenant>())
            {
                ObservedTenant = true;
                OrganizationWasHeld = _organization;
                ActorWasHeld = _actor;
                PrivacyOwned = context.IdentityFenceOwnsTransaction;
            }
            return ValueTask.FromResult(result);
        }
    }
}
