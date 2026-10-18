using System.Data;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed partial class HumanAdministratorNativeCommitFenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelectedEmailLoginFencesEveryContributorWithoutIamProjection(bool tenant)
    {
        var rows = await SeedAsync();
        await using var context = Open(tenant ? PrimaryDatabaseRole.EmailPolicyTenantMutation
            : PrimaryDatabaseRole.EmailPolicyInstanceMutation, tenant ? rows.Tenant : null);
        var scope = await ScopeAsync(context, rows.User, tenant
            ? HumanAdministratorCommitPurpose.EmailTenant : HumanAdministratorCommitPurpose.EmailInstance);
        var authority = new ExternalApiKeyIssuanceAuthority(context, new HttpContextAccessor(), scope);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await CheckAsync(authority, rows, tenant)).IsTrue();
        await Assert.That(ReferenceEquals(transaction, context.Database.CurrentTransaction)).IsTrue();
        await using var contender = fixture.CreateDbContext();
        (Type Type, object Id)[] contributors = tenant
            ? [(typeof(User), rows.User), (typeof(Tenant), rows.Tenant), (typeof(TenantUser), rows.Member),
                (typeof(TenantUserRoleGrant), rows.Grant), (typeof(Role), (int)RoleEnum.TenantAdmin),
                (typeof(TenantStatus), (int)TenantStatusEnum.Active)]
            : [(typeof(User), rows.User), (typeof(PlatformUserRole), rows.PlatformGrant),
                (typeof(Role), (int)RoleEnum.Admin)];
        foreach (var contributor in contributors)
        {
            await using var attempt = await contender.Database.BeginTransactionAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(async () =>
                await contender.Database.ExecuteSqlRawAsync(
                    $"SELECT id FROM {Table(contender, contributor.Type)} WHERE id = {{0}} FOR UPDATE NOWAIT",
                    [contributor.Id]));
            await Assert.That(error!.SqlState).IsEqualTo("55P03");
            await attempt.RollbackAsync();
        }
        await transaction.RollbackAsync();
        await using (var released = await contender.Database.BeginTransactionAsync())
        {
            await contender.Database.ExecuteSqlRawAsync(
                $"SELECT id FROM {Table(contender, typeof(User))} WHERE id = {{0}} FOR UPDATE NOWAIT", rows.User);
            await released.CommitAsync();
        }
        var projection = await Assert.ThrowsAsync<PostgresException>(async () =>
            await context.Users.AsNoTracking().AnyAsync());
        await Assert.That(projection!.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task ExistingInactiveMembershipCannotBecomeActiveBehindTenantFence()
    {
        var rows = await SeedAsync();
        await using var seed = fixture.CreateDbContext();
        await seed.TenantUsers.Where(value => value.Id == rows.Member)
            .ExecuteUpdateAsync(update => update.SetProperty(value => value.StatusId, (int)TenantUserStatusEnum.Suspended));
        await using var context = Open(PrimaryDatabaseRole.EmailPolicyTenantMutation, rows.Tenant);
        var scope = await ScopeAsync(context, rows.User, HumanAdministratorCommitPurpose.EmailTenant);
        var authority = new ExternalApiKeyIssuanceAuthority(context, new HttpContextAccessor(), scope);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await CheckAsync(authority, rows, true)).IsFalse();
        await using var contender = fixture.CreateDbContext();
        await using var attempt = await contender.Database.BeginTransactionAsync();
        var blocked = await Assert.ThrowsAsync<PostgresException>(async () =>
            await contender.Database.ExecuteSqlRawAsync(
                $"SELECT id FROM {Table(contender, typeof(TenantUser))} WHERE id = {{0}} FOR UPDATE NOWAIT", rows.Member));
        await Assert.That(blocked!.SqlState).IsEqualTo("55P03");
        await attempt.RollbackAsync();
        await owner.RollbackAsync();
    }

    [Test]
    public async Task NewMembershipFkInsertionCannotPassHeldPrincipalFence()
    {
        var rows = await SeedAsync();
        await using var seed = fixture.CreateDbContext();
        var otherTenant = NewTenant();
        seed.Tenants.Add(otherTenant);
        await seed.SaveChangesAsync();
        await using var context = Open(PrimaryDatabaseRole.EmailPolicyInstanceMutation, null);
        var scope = await ScopeAsync(context, rows.User, HumanAdministratorCommitPurpose.EmailInstance);
        var authority = new ExternalApiKeyIssuanceAuthority(context, new HttpContextAccessor(), scope);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await CheckAsync(authority, rows, false)).IsTrue();
        await using var contender = fixture.CreateDbContext();
        await using var attempt = await contender.Database.BeginTransactionAsync();
        // A server lock deadline bounds the actual FK insert, not an orchestration sleep.
        await contender.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
        var blocked = await Assert.ThrowsAsync<PostgresException>(async () =>
            await contender.Database.ExecuteSqlRawAsync($$"""
                INSERT INTO {{Table(contender, typeof(TenantUser))}}
                    (id, tenant_id, user_id, status_id, joined_at, created_at, is_deleted)
                VALUES ({0}, {1}, {2}, {3}, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, false)
                """, Guid.CreateVersion7(), otherTenant.Id, rows.User, (int)TenantUserStatusEnum.Active));
        await Assert.That(blocked!.SqlState).IsEqualTo("55P03");
        await attempt.RollbackAsync();
        await owner.RollbackAsync();
        await contender.Database.ExecuteSqlRawAsync($$"""
            INSERT INTO {{Table(contender, typeof(TenantUser))}}
                (id, tenant_id, user_id, status_id, joined_at, created_at, is_deleted)
            VALUES ({0}, {1}, {2}, {3}, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, false)
            """, Guid.CreateVersion7(), otherTenant.Id, rows.User, (int)TenantUserStatusEnum.Active);
    }

    [Test]
    public async Task CommittedRevocationAndWrongTenantDenyWithoutNewTransaction()
    {
        var rows = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.EmailPolicyTenantMutation, rows.Tenant);
        var scope = await ScopeAsync(context, rows.User, HumanAdministratorCommitPurpose.EmailTenant);
        var authority = new ExternalApiKeyIssuanceAuthority(context, new HttpContextAccessor(), scope);
        await using (var revoke = fixture.CreateDbContext())
            await revoke.TenantUserRoleGrants.Where(value => value.Id == rows.Grant)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.RevokedAt, DateTime.UtcNow));
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await CheckAsync(authority, rows, true)).IsFalse();
        await Assert.That(await ((ITenantAdministratorCommitAuthority)authority).IsTenantAdministratorForCommitAsync(
            rows.User, Guid.CreateVersion7(), CancellationToken.None)).IsFalse();
        await Assert.That(await ((ITenantAdministratorCommitAuthority)authority).IsTenantAdministratorForCommitAsync(
            Guid.CreateVersion7(), rows.Tenant, CancellationToken.None)).IsFalse();
        await Assert.That(ReferenceEquals(owner, context.Database.CurrentTransaction)).IsTrue();
        await owner.RollbackAsync();
    }

    [Test]
    public async Task NonSerializableOwnerAndWrongPurposeFailClosed()
    {
        var rows = await SeedAsync();
        await using var context = Open(PrimaryDatabaseRole.EmailPolicyTenantMutation, rows.Tenant);
        var scope = await ScopeAsync(context, rows.User, HumanAdministratorCommitPurpose.RuntimeTenant);
        var authority = new ExternalApiKeyIssuanceAuthority(context, new HttpContextAccessor(), scope);
        await using (var owner = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => CheckAsync(authority, rows, true));
            await owner.RollbackAsync();
        }
        await using var serializable = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => CheckAsync(authority, rows, true));
        await Assert.That(rejected!.SqlState).IsEqualTo("42501");
        await serializable.RollbackAsync();
    }

    private static Task<bool> CheckAsync(ExternalApiKeyIssuanceAuthority authority, Rows rows, bool tenant) =>
        tenant ? ((ITenantAdministratorCommitAuthority)authority).IsTenantAdministratorForCommitAsync(rows.User, rows.Tenant, CancellationToken.None)
            : ((IInstanceAdministratorCommitAuthority)authority).IsInstanceAdministratorForCommitAsync(rows.User, CancellationToken.None);

    private async Task<HumanAdministratorCommitScope> ScopeAsync(
        ExploreDbContext context, Guid user, HumanAdministratorCommitPurpose purpose)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.ToString())], "fixture"));
        var resolver = new ResolveCurrentUserIdByIdentityRequestHandler(
            new ExternalLoginAuthenticationRepository(
                new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)), fixture.NativeQueueEnrollment));
        var subject = await HumanCommitSubject.ResolveAsync(principal, resolver, CancellationToken.None);
        return new(context, subject!, purpose, fixture.NativeQueueEnrollment);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid? tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new ExploreDbContext(options.Options) { TenantContext = tenant is { } id ? new TenantIdentity(id) : null };
    }

    private async Task<Rows> SeedAsync()
    {
        await using var seed = fixture.CreateDbContext();
        await PostgresHumanAdministratorCommitAuthorityContract.ApplyAsync(seed, fixture.NativeQueueEnrollment);
        var tenant = NewTenant();
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = string.Empty, FirstName = "Fence", LastName = "Owner" }
        };
        var member = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var grant = new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            TenantUserId = member.Id,
            TenantUser = member,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        var platform = new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = DateTime.UtcNow
        };
        seed.AddRange(tenant, user, member, grant, platform);
        await seed.SaveChangesAsync();
        return new(user.Id, tenant.Id, member.Id, grant.Id, platform.Id);
    }

    private static Tenant NewTenant() => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = "Administrator fence",
        Slug = $"human-fence-{Guid.CreateVersion7():N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }

    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
    private sealed record Rows(Guid User, Guid Tenant, Guid Member, Guid Grant, Guid PlatformGrant);
}
