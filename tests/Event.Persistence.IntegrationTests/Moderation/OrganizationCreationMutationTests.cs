using System.Security.Claims;
using Explore.Application.Exceptions;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.DTOs.Organization;
using Explore.Application.Features.Organizations.Handlers.Commands;
using Explore.Application.Features.Organizations.Requests.Commands;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class OrganizationCreationMutationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Creation_preserves_business_input_and_fenced_tenant_approval(bool administrator, bool absentHost)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        if (administrator)
        {
            var membership = await context.TenantUsers.SingleAsync(row => row.UserId == database.SubjectId);
            context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = database.TenantId,
                Tenant = null!,
                TenantUserId = membership.Id,
                TenantUser = membership,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow,
                GrantedBy = database.AdministratorId
            });
        }
        if (absentHost)
        {
            context.ModerationQualificationEpochs.RemoveRange(await context.ModerationQualificationEpochs.ToArrayAsync());
            context.ModerationHostBindings.RemoveRange(await context.ModerationHostBindings.ToArrayAsync());
        }
        await context.SaveChangesAsync();
        await using var session = OrganizationMemberMutationBoundaryTests.CreateBoundary(context,
            database.Privacy.Authority(context, TimeProvider.System), database.SubjectId);
        await session.InitializeAsync();
        using var services = Services();
        var result = await Handler(database, context, session.Boundary, services).ExecuteAsync(Command(), default);
        await Assert.That(result.IsSuccess).IsTrue();
        var organization = await context.Organizations.AsNoTracking().Include(row => row.Pii).SingleAsync();
        var participation = await context.OrganizationTenants.AsNoTracking().SingleAsync();
        var member = await context.OrganizationMembers.AsNoTracking().SingleAsync();
        var actor = await context.Actors.AsNoTracking().Include(row => row.Pii)
            .SingleAsync(row => row.OrganizationId == organization.Id);
        await Assert.That(organization.Id).IsEqualTo(result.Id);
        await Assert.That(organization.FullName).IsEqualTo("New organization");
        await Assert.That(organization.WebsiteUrl).IsEqualTo("https://new.example.test");
        await Assert.That(organization.Email).IsEqualTo("office@example.test");
        await Assert.That(organization.Country).IsEqualTo("BE");
        await Assert.That(organization.City).IsEqualTo("Brussels");
        await Assert.That(organization.Postcode).IsEqualTo("1000");
        await Assert.That(organization.Address).IsEqualTo("Square 1");
        await Assert.That(organization.IsDeleted).IsFalse();
        await Assert.That(participation.TenantId).IsEqualTo(database.TenantId);
        await Assert.That(participation.OrganizationId).IsEqualTo(organization.Id);
        await Assert.That(participation.ApprovalStatusId).IsEqualTo(administrator
            ? (int)ApprovalStatusEnum.Approved : (int)ApprovalStatusEnum.Pending);
        await Assert.That(participation.IsVisible).IsEqualTo(administrator);
        await Assert.That(participation.IsOrganizerEligible).IsEqualTo(administrator);
        await Assert.That(participation.ApprovedBy).IsEqualTo(administrator ? database.SubjectId : (Guid?)null);
        await Assert.That(participation.ProfilePictureId).IsNull();
        await Assert.That(member.OrganizationTenantId).IsEqualTo(participation.Id);
        await Assert.That(member.UserId).IsEqualTo(database.SubjectId);
        await Assert.That(member.RoleId).IsEqualTo((int)RoleEnum.OrgAdmin);
        await Assert.That(actor.Pii.DisplayName).IsEqualTo(organization.FullName);
        await Assert.That(await context.ModerationHostBindings.CountAsync()).IsEqualTo(absentHost ? 0 : 1);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).ToArrayAsync())
            .IsEquivalentTo(absentHost ? Array.Empty<long>() : [2L]);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("erased")]
    [Arguments("unauthenticated")]
    [Arguments("provider")]
    public async Task Invalid_creator_cannot_leave_any_initial_organization_records(string denial)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var context = database.Context();
        var privacy = database.Privacy.Authority(context, TimeProvider.System);
        if (denial == "erased") await privacy.AppendAsync(PrivacyErasureRequest.Create(Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User, database.SubjectId, PrivacyErasureReasonCode.SubjectErasureRequest, 1));
        await using var session = OrganizationMemberMutationBoundaryTests.CreateBoundary(context, privacy, database.SubjectId, denial != "provider");
        await session.InitializeAsync();
        if (denial == "unauthenticated") session.Http.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity());
        using var services = Services();
        await Assert.That(async () => await Handler(database, context, session.Boundary, services)
            .ExecuteAsync(Command(), default)).Throws<AuthorizationException>();
        await AssertEmptyAsync(context);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Changed_creator_at_final_flush_rolls_back_every_created_record(bool colocated)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var baseline = database.Context();
        var checkpoint = new ChangeCreatorOnEpochSave(database.ReviewerId);
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(baseline.Database.GetDbConnection()).UseSnakeCaseNamingConvention()
            .AddInterceptors(checkpoint).Options)
        { TenantContext = database };
        await using var shared = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        await using var session = OrganizationMemberMutationBoundaryTests.CreateBoundary(context,
            (shared ?? database.Privacy).Authority(context, TimeProvider.System), database.SubjectId);
        await session.InitializeAsync();
        checkpoint.Http = session.Http;
        checkpoint.Armed = true;
        using var services = Services();
        await Assert.That(async () => await Handler(database, context, session.Boundary, services)
            .ExecuteAsync(Command(), default)).Throws<AuthorizationException>();
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(checkpoint.PrivacyOwned).IsEqualTo(colocated);
        await using var inspect = database.Context();
        await AssertEmptyAsync(inspect);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Lost_tenant_approval_authority_rolls_back_the_created_organization(bool colocated)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var baseline = database.Context();
        var membership = await baseline.TenantUsers.SingleAsync(row => row.UserId == database.SubjectId);
        var grantId = Guid.CreateVersion7();
        baseline.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = grantId,
            TenantId = database.TenantId,
            Tenant = null!,
            TenantUserId = membership.Id,
            TenantUser = membership,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = DateTime.UtcNow,
            GrantedBy = database.AdministratorId
        });
        await baseline.SaveChangesAsync();
        var checkpoint = new RevokeApprovalOnEpochSave(grantId);
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(baseline.Database.GetDbConnection()).UseSnakeCaseNamingConvention()
            .AddInterceptors(checkpoint).Options)
        { TenantContext = database };
        await using var shared = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(context) : null;
        await using var session = OrganizationMemberMutationBoundaryTests.CreateBoundary(context,
            (shared ?? database.Privacy).Authority(context, TimeProvider.System), database.SubjectId);
        await session.InitializeAsync();
        checkpoint.Armed = true;
        using var services = Services();
        await Assert.That(async () => await Handler(database, context, session.Boundary, services)
            .ExecuteAsync(Command(), default)).Throws<AuthorizationException>();
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(checkpoint.PrivacyOwned).IsEqualTo(colocated);
        await using var inspect = database.Context();
        await AssertEmptyAsync(inspect);
        await Assert.That((await inspect.TenantUserRoleGrants.SingleAsync(row => row.Id == grantId)).RevokedAt).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, false, true)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    [Arguments(false, true, false)]
    public async Task Uncertain_commit_preserves_one_exact_graph_or_the_original_rollback_failure(bool verify, bool rollback, bool retry)
    {
        await using var database = await SourceDatabase.CreateAsync();
        await using var baseline = database.Context();
        var fault = new TenantUserRoleGrantMutationBoundaryTests.LostRoleCommit(verify, rollback) { Armed = false };
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(baseline.Database.GetDbConnection())
            .UseSnakeCaseNamingConvention().AddInterceptors(fault);
        if (retry) options.ReplaceService<IExecutionStrategyFactory, TenantUserRoleGrantMutationBoundaryTests.RoleCommitRetryFactory>();
        await using var context = new ExploreDbContext(options.Options) { TenantContext = database };
        await using var session = OrganizationMemberMutationBoundaryTests.CreateBoundary(context,
            database.Privacy.Authority(context, TimeProvider.System), database.SubjectId);
        await session.InitializeAsync();
        using var services = Services();
        var handler = Handler(database, context, session.Boundary, services);
        fault.Armed = true;
        if (rollback && !retry)
            await Assert.That(async () => await handler.ExecuteAsync(Command(), default))
                .Throws<TenantUserRoleGrantMutationBoundaryTests.LostRoleCommitException>();
        else await Assert.That((await handler.ExecuteAsync(Command(), default)).IsSuccess).IsTrue();
        await Assert.That(fault.Injected).IsTrue();
        await using var inspect = database.Context();
        var count = rollback && !retry ? 0 : 1;
        await Assert.That(await inspect.Organizations.CountAsync()).IsEqualTo(count);
        await Assert.That(await inspect.OrganizationPii.CountAsync()).IsEqualTo(count);
        await Assert.That(await inspect.OrganizationTenants.CountAsync()).IsEqualTo(count);
        await Assert.That(await inspect.OrganizationMembers.CountAsync()).IsEqualTo(count);
        await Assert.That(await inspect.Actors.CountAsync(row => row.OrganizationId != null)).IsEqualTo(count);
        await Assert.That(await inspect.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(1L + count);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private static async Task AssertEmptyAsync(ExploreDbContext context)
    {
        await Assert.That(await context.Organizations.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.OrganizationPii.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.OrganizationTenants.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.OrganizationMembers.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.Actors.CountAsync(row => row.OrganizationId != null)).IsEqualTo(0);
        await Assert.That(await context.ModerationQualificationEpochs.Select(row => row.Revision).SingleAsync()).IsEqualTo(1L);
    }
    private static CreateOrganizationCommand Command() => new()
    {
        OrganizationDto = new CreateOrganizationDto
        {
            FullName = "New organization",
            WebsiteUrl = "https://new.example.test",
            Email = "office@example.test",
            Country = "BE",
            City = "Brussels",
            Postcode = 1000,
            Address = "Square 1"
        }
    };
    private static ServiceProvider Services()
    {
        var services = new ServiceCollection(); services.AddHybridCache(); services.AddMetrics();
        services.AddSingleton<BusinessMetrics>(); return services.BuildServiceProvider();
    }
    private static CreateOrganizationCommandHandler Handler(SourceDatabase database, ExploreDbContext context,
        OrganizationMemberMutationBoundary boundary, ServiceProvider services) => new(
        new OrganizationRepository(context), new OrganizationTenantRepository(context), new OrganizationMemberRepository(context),
        new ActorRepository(context), new StorageObjectRepository(context), database,
        services.GetRequiredService<HybridCache>(), services.GetRequiredService<BusinessMetrics>(), boundary);

    private sealed class RevokeApprovalOnEpochSave(Guid grantId) : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public bool Observed { get; private set; }
        public bool PrivacyOwned { get; private set; }
        private bool _pending;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _pending = Armed && !Observed && eventData.Context!.ChangeTracker.Entries<ModerationQualificationEpoch>()
                .Any(row => row.State == EntityState.Modified);
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (_pending)
            {
                _pending = false; Observed = true;
                var context = (ExploreDbContext)eventData.Context!;
                PrivacyOwned = context.IdentityFenceOwnsTransaction;
                await context.TenantUserRoleGrants.Where(row => row.Id == grantId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RevokedAt, DateTime.UtcNow), cancellationToken);
            }
            return result;
        }
    }

    private sealed class ChangeCreatorOnEpochSave(Guid replacement) : SaveChangesInterceptor
    {
        public IHttpContextAccessor? Http { get; set; }
        public bool Armed { get; set; }
        public bool Observed { get; private set; }
        public bool PrivacyOwned { get; private set; }
        private bool _pending;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _pending = Armed && !Observed && eventData.Context!.ChangeTracker.Entries<ModerationQualificationEpoch>()
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
