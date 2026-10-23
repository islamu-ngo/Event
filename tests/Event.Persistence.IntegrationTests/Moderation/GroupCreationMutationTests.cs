using System.Data.Common;
using System.Security.Claims;
using Explore.Application.Authorization;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.DTOs.Group;
using Explore.Application.Features.Groups.Handlers.Commands;
using Explore.Application.Features.Groups.Requests.Commands;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class GroupCreationMutationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Creation_owns_business_input_pending_participation_and_initial_administrator(bool absentHost)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        if (absentHost)
        {
            context.ModerationQualificationEpochs.RemoveRange(await context.ModerationQualificationEpochs.ToArrayAsync());
            context.ModerationHostBindings.RemoveRange(await context.ModerationHostBindings.ToArrayAsync());
            await context.SaveChangesAsync();
        }
        var session = GroupMemberMutationBoundaryTests.CreateBoundary(context,
            database.Privacy.Authority(context, TimeProvider.System), database.SubjectId);
        using var services = Services();
        var result = await Handler(database, context, session.Boundary, services).ExecuteAsync(Command(), default);
        await Assert.That(result.IsSuccess).IsTrue();
        var group = await context.Groups.AsNoTracking().SingleAsync();
        var participation = await context.GroupTenants.AsNoTracking().SingleAsync();
        var member = await context.GroupMembers.AsNoTracking().SingleAsync();
        var actor = await context.Actors.AsNoTracking().Include(row => row.Pii)
            .SingleAsync(row => row.GroupId == group.Id);
        await Assert.That(group.Id).IsEqualTo(result.Id);
        await Assert.That(group.FullName).IsEqualTo("New group");
        await Assert.That(group.Description).IsEqualTo("New description");
        await Assert.That(group.IsDeleted).IsFalse();
        await Assert.That(participation.TenantId).IsEqualTo(database.TenantId);
        await Assert.That(participation.GroupId).IsEqualTo(group.Id);
        await Assert.That(participation.ApprovalStatusId).IsEqualTo((int)ApprovalStatusEnum.Pending);
        await Assert.That(participation.IsOrganizerEligible).IsFalse();
        await Assert.That(participation.ProfilePictureId).IsNull();
        await Assert.That(member.GroupTenantId).IsEqualTo(participation.Id);
        await Assert.That(member.UserId).IsEqualTo(database.SubjectId);
        await Assert.That(member.RoleId).IsEqualTo((int)RoleEnum.GroupAdmin);
        await Assert.That(actor.Pii.DisplayName).IsEqualTo(group.FullName);
        await Assert.That(await context.ModerationHostBindings.CountAsync()).IsEqualTo(absentHost ? 0 : 1);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).ToArrayAsync())
            .IsEquivalentTo(absentHost ? Array.Empty<long>() : [2L]);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("erased")]
    [Arguments("unauthenticated")]
    [Arguments("provider")]
    public async Task Invalid_creator_authority_cannot_leave_any_initial_group_records(string denial)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var privacy = database.Privacy.Authority(context, TimeProvider.System);
        if (denial == "erased") await privacy.AppendAsync(PrivacyErasureRequest.Create(Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User, database.SubjectId, PrivacyErasureReasonCode.SubjectErasureRequest, 1));
        var session = GroupMemberMutationBoundaryTests.CreateBoundary(context, privacy, database.SubjectId, denial != "provider");
        if (denial == "unauthenticated") session.Http.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity());
        using var services = Services();
        await Assert.That(async () => await Handler(database, context, session.Boundary, services)
            .ExecuteAsync(Command(), default)).Throws<AuthorizationException>();
        await Assert.That(await context.Groups.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.GroupTenants.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.GroupMembers.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.Actors.CountAsync(row => row.GroupId != null)).IsEqualTo(0);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Changed_creator_at_final_flush_rolls_back_the_entire_initial_group(bool colocated)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var baseline = database.Context();
        var checkpoint = new ChangeCreatorOnEpochSave(database.ReviewerId);
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(baseline.Database.GetDbConnection()).UseSnakeCaseNamingConvention()
            .AddInterceptors(checkpoint).Options)
        { TenantContext = database };
        await using var shared = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        var session = GroupMemberMutationBoundaryTests.CreateBoundary(context,
            (shared ?? database.Privacy).Authority(context, TimeProvider.System), database.SubjectId);
        checkpoint.Http = session.Http;
        using var services = Services();
        await Assert.That(async () => await Handler(database, context, session.Boundary, services)
            .ExecuteAsync(Command(), default)).Throws<AuthorizationException>();
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(checkpoint.PrivacyOwned).IsEqualTo(colocated);
        await using var inspect = database.Context();
        await Assert.That(await inspect.Groups.CountAsync()).IsEqualTo(0);
        await Assert.That(await inspect.GroupTenants.CountAsync()).IsEqualTo(0);
        await Assert.That(await inspect.GroupMembers.CountAsync()).IsEqualTo(0);
        await Assert.That(await inspect.Actors.CountAsync(row => row.GroupId != null)).IsEqualTo(0);
        await Assert.That(await inspect.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(1L);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, false, true)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    [Arguments(false, true, false)]
    public async Task Unknown_commit_preserves_one_exact_group_or_the_original_rollback_failure(bool verify, bool rollback, bool retry)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var baseline = database.Context();
        var fault = new TenantUserRoleGrantMutationBoundaryTests.LostRoleCommit(verify, rollback);
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(fault);
        if (retry) options.ReplaceService<IExecutionStrategyFactory, TenantUserRoleGrantMutationBoundaryTests.RoleCommitRetryFactory>();
        await using var context = new ExploreDbContext(options.Options) { TenantContext = database };
        var session = GroupMemberMutationBoundaryTests.CreateBoundary(context,
            database.Privacy.Authority(context, TimeProvider.System), database.SubjectId);
        using var services = Services();
        var handler = Handler(database, context, session.Boundary, services);
        if (rollback && !retry)
            await Assert.That(async () => await handler.ExecuteAsync(Command(), default))
                .Throws<TenantUserRoleGrantMutationBoundaryTests.LostRoleCommitException>();
        else await Assert.That((await handler.ExecuteAsync(Command(), default)).IsSuccess).IsTrue();
        await Assert.That(fault.Injected).IsTrue();
        await using var inspect = database.Context();
        var count = rollback && !retry ? 0 : 1;
        await Assert.That(await inspect.Groups.CountAsync()).IsEqualTo(count);
        await Assert.That(await inspect.GroupTenants.CountAsync()).IsEqualTo(count);
        await Assert.That(await inspect.GroupMembers.CountAsync()).IsEqualTo(count);
        await Assert.That(await inspect.Actors.CountAsync(row => row.GroupId != null)).IsEqualTo(count);
        await Assert.That(await inspect.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(1L + count);
    }

    [Test]
    public async Task Hierarchy_lock_borrows_the_callers_serializable_transaction_without_committing_it()
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        await using (var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
        {
            await new GroupRepository(context).ExecuteWithHierarchyMutationLock(database.TenantId, async token =>
            {
                context.Groups.Add(new Group { Id = Guid.CreateVersion7(), FullName = "Must roll back" });
                await context.SaveChangesAsync(token);
                return true;
            }, default);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
        }
        await using var inspect = database.Context();
        await Assert.That(await inspect.Groups.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Creation_holds_the_hierarchy_lock_before_its_first_tenant_source_anchor(bool colocated)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var baseline = database.Context();
        var checkpoint = new HierarchyBeforeTenant(database.TenantId);
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(baseline.Database.GetDbConnection()).UseSnakeCaseNamingConvention()
            .AddInterceptors(checkpoint).Options)
        { TenantContext = database };
        await using var shared = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        var session = GroupMemberMutationBoundaryTests.CreateBoundary(context,
            (shared ?? database.Privacy).Authority(context, TimeProvider.System), database.SubjectId);
        using var services = Services();
        var result = await Handler(database, context, session.Boundary, services).ExecuteAsync(Command(), default);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(checkpoint.HierarchyHeld).IsTrue();
        await Assert.That(checkpoint.PrivacyOwned).IsEqualTo(colocated);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(await context.GroupMembers.CountAsync(row => row.UserId == database.SubjectId
            && row.RoleId == (int)RoleEnum.GroupAdmin)).IsEqualTo(1);
    }

    private static CreateGroupCommand Command() => new()
    { GroupDto = new CreateGroupDto { FullName = "New group", Description = "New description" } };
    private static ServiceProvider Services()
    {
        var services = new ServiceCollection(); services.AddHybridCache(); services.AddMetrics();
        services.AddSingleton<BusinessMetrics>();
        return services.BuildServiceProvider();
    }
    private static CreateGroupCommandHandler Handler(SourceDatabase database, ExploreDbContext context,
        Explore.Persistence.Services.GroupMemberMutationBoundary boundary, ServiceProvider services) => new(
        new GroupRepository(context), new GroupTenantRepository(context), new OrganizationTenantRepository(context),
        new GroupMemberRepository(context), new ActorRepository(context), new StorageObjectRepository(context), database,
        services.GetRequiredService<HybridCache>(),
        services.GetRequiredService<BusinessMetrics>(), boundary);

    private sealed class HierarchyBeforeTenant(Guid tenantId) : DbCommandInterceptor
    {
        public bool Observed { get; private set; }
        public bool HierarchyHeld { get; private set; }
        public bool PrivacyOwned { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Observed && eventData.Context is ExploreDbContext context)
            {
                var entity = context.Model.FindEntityType(typeof(Tenant))!;
                var table = context.GetService<ISqlGenerationHelper>()
                    .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
                if (command.CommandText.TrimStart().StartsWith($"UPDATE {table} ", StringComparison.Ordinal))
                {
                    Observed = true;
                    HierarchyHeld = command.Transaction is { } transaction
                        && SqliteNamedLockTransactionInterceptor.Instance.IsTracked(transaction, $"group-hierarchy:{tenantId}");
                    PrivacyOwned = context.IdentityFenceOwnsTransaction;
                }
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ChangeCreatorOnEpochSave(Guid replacement) : SaveChangesInterceptor
    {
        public HttpContextAccessor? Http { get; set; }
        public bool Observed { get; private set; }
        public bool PrivacyOwned { get; private set; }
        private bool _pending;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _pending = !Observed && eventData.Context!.ChangeTracker.Entries<ModerationQualificationEpoch>()
                .Any(row => row.State == EntityState.Modified);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (_pending)
            {
                _pending = false; Observed = true;
                PrivacyOwned = ((ExploreDbContext)eventData.Context!).IdentityFenceOwnsTransaction;
                Http!.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, replacement.ToString())], "changed-creator"));
            }
            return ValueTask.FromResult(result);
        }
    }
}
