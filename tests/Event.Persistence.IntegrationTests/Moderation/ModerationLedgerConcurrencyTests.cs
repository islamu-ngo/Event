using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Moderation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ModerationLedgerConcurrencyTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task Native_qualification_row_remains_locked_through_the_owner_transaction()
    {
        var seed = await SeedAsync();
        await using var owner = BoundContext(seed.Scope);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authority = new RoleAuthority(owner, seed, entered, release);
        var service = Service(owner, authority);
        var operation = service.ExecuteAsync(
            ModerationOperation.Evaluate(seed.Subject, seed.Scope, ModerationCapability.Publish));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var contender = BoundContext(seed.Scope);
            await using var transaction = await contender.Database.BeginTransactionAsync();
            var entity = contender.Model.FindEntityType(typeof(TenantUserRoleGrant))!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var sql = contender.GetService<ISqlGenerationHelper>();
            var table = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            var key = sql.DelimitIdentifier(entity.FindProperty(nameof(TenantUserRoleGrant.Id))!.GetColumnName(store)!);
            var blocked = false;
            try
            {
                await using var probe = contender.Database.GetDbConnection().CreateCommand();
                probe.Transaction = transaction.GetDbTransaction();
                probe.CommandText = $"SELECT {key} FROM {table} WHERE {key} = @grant FOR UPDATE NOWAIT";
                var grantId = probe.CreateParameter();
                grantId.ParameterName = "grant";
                grantId.Value = seed.GrantId;
                probe.Parameters.Add(grantId);
                await probe.ExecuteScalarAsync(CancellationToken.None);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
            {
                blocked = true;
            }

            await Assert.That(blocked).IsTrue();
        }
        finally
        {
            release.TrySetResult();
        }
        await Assert.That((await operation).Outcome).IsEqualTo(ModerationOperationOutcome.Known);

        await using var writer = BoundContext(seed.Scope);
        var repository = new TenantUserRoleGrantRepository(writer);
        var grant = (await repository.GetById(seed.GrantId))!;
        grant.RevokedAt = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        await repository.Update(grant);
        await using var nextOwner = BoundContext(seed.Scope);
        var nextAuthority = new RoleAuthority(nextOwner, seed);
        var next = await Service(nextOwner, nextAuthority).ExecuteAsync(
            ModerationOperation.Evaluate(seed.Subject, seed.Scope, ModerationCapability.Publish));
        await Assert.That(next.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
    }

    [Test]
    public async Task Qualification_revocation_committed_first_cannot_be_bypassed_by_a_cached_grant()
    {
        var seed = await SeedAsync();
        await using var owner = BoundContext(seed.Scope);
        var authority = new RoleAuthority(owner, seed);
        var operation = ModerationOperation.Evaluate(seed.Subject, seed.Scope, ModerationCapability.Publish);
        await Assert.That((await Service(owner, authority).ExecuteAsync(operation)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Known);
        await using (var writer = BoundContext(seed.Scope))
        {
            var repository = new TenantUserRoleGrantRepository(writer);
            var grant = (await repository.GetById(seed.GrantId))!;
            grant.RevokedAt = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
            await repository.Update(grant);
        }
        await Assert.That((await Service(owner, authority).ExecuteAsync(operation)).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Denied);
    }

    private ModerationOperationService Service(ExploreDbContext context, RoleAuthority authority) =>
        new(new ModerationStore(context, authority, TimeProvider.System), authority, new ChangePointer(), TimeProvider.System);

    private ExploreDbContext BoundContext(AuthorityScope scope)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(scope.Id);
        return fixture.CreateTenantFilteredDbContext(tenant, PostgresTenantSessionInterceptor.Instance);
    }

    private async Task<Seed> SeedAsync()
    {
        await fixture.ResetAsync();
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7());
        var principalId = Guid.CreateVersion7();
        var user = new User
        {
            Id = principalId,
            ConcurrencyStamp = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                UserId = principalId, Email = $"{principalId:N}@example.invalid", FirstName = "Test", LastName = "Reviewer"
            }
        };
        var tenant = new Tenant
        {
            Id = scope.Id, FullName = "Moderation race scope", Slug = $"moderation-{scope.Id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        };
        var membership = new TenantUser
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant, UserId = user.Id, User = user,
            StatusId = (int)TenantUserStatusEnum.Active
        };
        var grant = new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
            TenantUserId = membership.Id, TenantUser = membership,
            RoleId = (int)RoleEnum.TenantAdmin, RoleScopeId = (int)RoleScopeEnum.Tenant, Role = null!
        };
        await using var context = fixture.CreateDbContext();
        context.AddRange(user, tenant, membership, grant);
        await context.SaveChangesAsync();
        return new Seed(scope, SubjectReference.Create(ModerationSubjectKind.Human, principalId, scope),
            grant.Id, membership.Id);
    }

    private sealed record Seed(AuthorityScope Scope, SubjectReference Subject, Guid GrantId, Guid MembershipId);

    private sealed class RoleAuthority(
        ExploreDbContext context, Seed seed, TaskCompletionSource? entered = null,
        TaskCompletionSource? release = null) : IModerationAuthorityParticipant
    {
        public async Task<ModerationAuthorityResolution> AcquireAsync(
            IModerationSession session, ModerationOperation operation, CancellationToken token)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, seed.Scope.Id, token);
            await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, seed.Subject.Id, token);
            await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, seed.Scope.Id,
                entity => entity.Id, seed.MembershipId, token);
            await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, seed.Scope.Id,
                entity => entity.Id, seed.GrantId, token);
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [(int)RoleEnum.TenantAdmin], token);
            var result = await ResolveAsync(session, operation, token);
            entered?.TrySetResult();
            if (release is not null) await release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            return result;
        }

        public async Task<ModerationAuthorityResolution> ResolveAsync(
            IModerationSession session, ModerationOperation operation, CancellationToken token)
        {
            var qualified = await context.TenantUserRoleGrants.AsNoTracking().AnyAsync(
                grant => grant.Id == seed.GrantId && grant.TenantId == seed.Scope.Id && grant.RevokedAt == null
                    && grant.RoleId == (int)RoleEnum.TenantAdmin && grant.TenantUser.UserId == seed.Subject.Id
                    && !grant.TenantUser.IsDeleted && grant.TenantUser.StatusId == (int)TenantUserStatusEnum.Active,
                token);
            return qualified
                ? ModerationAuthorityResolution.Known(new ModerationAuthorityGrant(
                    seed.Subject, operation.Subject, seed.Scope, operation.Kind, 1, 1, ModerationAuthorityState.Known))
                : ModerationAuthorityResolution.Denied();
        }

        public Task<ModerationAuthorityState> QualifyActivationAsync(
            IModerationSession session, ModerationDecision decision, ModerationAuthorityGrant grant,
            CancellationToken token) => Task.FromResult(ModerationAuthorityState.Unavailable);
    }

    private sealed class ChangePointer : IModerationEffectPlanner
    {
        public ModerationPlannedEffect Plan(
            ModerationOperation operation, ModerationDecision decision, ModerationSanction? sanction,
            ModerationSanction? supersededSanction) =>
            new(operation.EffectId, operation.OperationId, decision.Id, decision.Revision,
                sanction?.Id, sanction?.Revision, decision.Subject, decision.Scope, operation.Kind,
                supersededSanction?.Id, supersededSanction?.Revision);
    }
}
