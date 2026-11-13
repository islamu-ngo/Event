using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Database;

/// <summary>Uses the fixture's production installation and actual Runtime login, without test-only grants.</summary>
[NotInParallel("PersistenceDb")]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed partial class ExternalTenantEnrollmentPermissionTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(true, true)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task RuntimePersistsOrRollsBackTheEntireExternalGraph(bool verifiedEmail, bool commit)
    {
        var tenant = await SeedTenantAsync();
        var userId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var loginId = Guid.CreateVersion7();
        var membershipId = Guid.CreateVersion7();
        string email = $"enrollment-{userId:N}@example.test";
        string providerKey = userId.ToString("N");
        var occurredAt = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);
        await using (var runtime = Open(PrimaryDatabaseRole.Runtime, tenant))
        {
            await runtime.Database.OpenConnectionAsync();
            string login = fixture.NativeQueueEnrollment.RuntimeLogin;
            await Assert.That(await runtime.Database.SqlQuery<bool>(
                $"SELECT session_user={login} AND current_user={login} AS \"Value\"").SingleAsync()).IsTrue();
            await PostgresExternalTenantEnrollmentPermissions.RequireReadyAsync(
                runtime, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
            await Assert.That(await runtime.Tenants.AsNoTracking().SingleAsync(row => row.Id == tenant)).IsNotNull();
            await Assert.That(await new PrivacyErasureStateRepository(runtime)
                .ExistsBySubjectAsync(userId, CancellationToken.None)).IsFalse();
            await using var securityGate = new HumanSecuritySourceGate(runtime);
            await using var securityCustody = await securityGate.AcquireAsync();
            await using var enrollmentGate = new EventDiscoveryEnrollmentGate(runtime);
            await using var enrollmentCustody = await enrollmentGate.AcquireAsync();
            await using var transaction = await runtime.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var user = new User
            {
                Id = userId, EmailVerified = verifiedEmail, CreatedAt = occurredAt, CreatedBy = userId,
                Pii = new UserPii { Email = email, FirstName = "Native", LastName = "Enrollment" }
            };
            var actor = new Actor
            {
                Id = actorId, UserId = userId, User = user,
                ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                Pii = new ActorPii { DisplayName = "Native Enrollment" },
                CreatedAt = occurredAt, CreatedBy = userId
            };
            var binding = new UserExternalLogin
            {
                Id = loginId, UserId = userId, User = user,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak, AuthenticationProvider = null!,
                ProviderKey = providerKey, ProviderDisplayName = "Keycloak",
                CreatedAt = occurredAt, CreatedBy = userId
            };
            runtime.AddRange(user, actor, binding, new TenantUser
            {
                Id = membershipId, TenantId = tenant, Tenant = null!,
                UserId = userId, User = user, ActorId = actorId, Actor = actor,
                StatusId = (int)TenantUserStatusEnum.Active, JoinedAt = occurredAt,
                CreatedAt = occurredAt, CreatedBy = userId
            });
            await runtime.SaveChangesAsync();
            var emails = new UserIdentityEmailRepository(runtime);
            var outcome = await new IdentityEmailSynchronizationOperation(emails).ExecuteAsync(userId, binding,
                new(email, verifiedEmail, true, Guid.CreateVersion7(), Guid.CreateVersion7(), occurredAt),
                CancellationToken.None);
            await Assert.That(outcome).IsEqualTo(verifiedEmail
                ? IdentityEmailSynchronizationOutcome.Confirmed : IdentityEmailSynchronizationOutcome.Unsupported);
            await Assert.That(await runtime.Users.AsNoTracking().AnyAsync(row => row.Id == userId && !row.IsDeleted)).IsTrue();
            await Assert.That(await runtime.Actors.AsNoTracking().AnyAsync(row => row.Id == actorId
                && row.UserId == userId && row.ActorTypeId == (int)ActorTypeEnum.User
                && !row.IsDeleted && !row.IsSuspended)).IsTrue();
            await Assert.That((await new UserExternalLoginRepository(runtime)
                .GetByProviderAndKey(new(AuthenticationProviderKind.Keycloak, providerKey)))!.Id).IsEqualTo(loginId);
            await Assert.That(await runtime.TenantUsers.AsNoTracking().AnyAsync(row => row.Id == membershipId
                && row.TenantId == tenant && row.UserId == userId && row.ActorId == actorId
                && row.StatusId == (int)TenantUserStatusEnum.Active && !row.IsDeleted)).IsTrue();
            await PostgresExternalTenantEnrollmentPermissions.RequireReadyAsync(
                runtime, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
            if (commit) await transaction.CommitAsync();
            else await transaction.RollbackAsync();
        }

        await using var observer = Open(PrimaryDatabaseRole.Migrator, tenant);
        var storedUser = await observer.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == userId);
        var storedActor = await observer.Actors.AsNoTracking().SingleOrDefaultAsync(row => row.Id == actorId);
        await Assert.That(storedUser is not null).IsEqualTo(commit);
        await Assert.That(storedActor is not null).IsEqualTo(commit);
        if (commit)
        {
            await Assert.That(storedUser!.Pii!.Email).IsEqualTo(email);
            await Assert.That(storedUser.Pii.FirstName).IsEqualTo("Native");
            await Assert.That(storedActor!.Pii!.DisplayName).IsEqualTo("Native Enrollment");
        }
        await Assert.That(await observer.UserExternalLogins.CountAsync(row => row.Id == loginId)).IsEqualTo(commit ? 1 : 0);
        await Assert.That(await observer.TenantUsers.CountAsync(row => row.Id == membershipId)).IsEqualTo(commit ? 1 : 0);
        await Assert.That(await observer.TenantUserRoleGrants.CountAsync(row => row.TenantUserId == membershipId)).IsEqualTo(0);
        await Assert.That(await observer.UserIdentityEmailClaims.CountAsync(row => row.UserId == userId)).IsEqualTo(commit && verifiedEmail ? 1 : 0);
        await Assert.That(await observer.UserIdentityEmailEvidence.CountAsync(row => row.ExternalLoginId == loginId
            && row.UserId == userId && row.IsActive)).IsEqualTo(commit && verifiedEmail ? 1 : 0);
        await using var other = Open(PrimaryDatabaseRole.Runtime, Guid.CreateVersion7());
        await Assert.That(await other.TenantUsers.AnyAsync(row => row.Id == membershipId)).IsFalse();
    }

    [Test]
    [Arguments("user-update")]
    [Arguments("pii-read")]
    [Arguments("claim-delete")]
    [Arguments("role-insert")]
    public async Task EnrollmentDoesNotAuthorizeExistingIdentityMutationOrRoleGrants(string operation)
    {
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, Guid.CreateVersion7());
        await runtime.Database.OpenConnectionAsync();
        await PostgresExternalTenantEnrollmentPermissions.RequireReadyAsync(
            runtime, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        var names = new HumanPrincipalSourceSql(runtime);
        string command = operation switch
        {
            "user-update" => $"UPDATE {names.Table(typeof(User))} SET {names.Column(typeof(User), "Id")}={names.Column(typeof(User), "Id")} WHERE false",
            "pii-read" => $"SELECT {names.Column(typeof(UserPii), "Email")} FROM {names.Table(typeof(UserPii))} WHERE false",
            "claim-delete" => $"DELETE FROM {names.Table(typeof(UserIdentityEmailClaim))} WHERE false",
            _ => $"INSERT INTO {names.Table(typeof(TenantUserRoleGrant))} DEFAULT VALUES"
        };
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(runtime, command));
        await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Test]
    public async Task RuntimeMembershipInsertRetainsDatabaseTenantIsolation()
    {
        await using var runtime = Open(PrimaryDatabaseRole.Runtime, Guid.CreateVersion7());
        await runtime.Database.OpenConnectionAsync();
        await PostgresExternalTenantEnrollmentPermissions.RequireReadyAsync(
            runtime, fixture.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated);
        var names = new HumanPrincipalSourceSql(runtime);
        string table = names.Table(typeof(TenantUser));
        string columns = string.Join(',', new[] { "Id", "TenantId", "UserId", "StatusId", "JoinedAt", "CreatedAt" }
            .Select(property => names.Column(typeof(TenantUser), property)));
        await using var command = runtime.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"INSERT INTO {table} ({columns}) VALUES (@id,@tenant,@user,@status,@time,@time)";
        foreach (var (name, value) in new (string, object)[]
        {
            ("id", Guid.CreateVersion7()), ("tenant", Guid.CreateVersion7()), ("user", Guid.CreateVersion7()),
            ("status", (int)TenantUserStatusEnum.Active), ("time", DateTime.UtcNow)
        })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        var failure = await Assert.ThrowsAsync<PostgresException>(async () => { await command.ExecuteNonQueryAsync(); });
        await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await Assert.That(failure.MessageText.Contains("row-level security", StringComparison.Ordinal)).IsTrue();
    }

    private async Task<Guid> SeedTenantAsync()
    {
        var tenant = Guid.CreateVersion7();
        await using var writer = Open(PrimaryDatabaseRole.Migrator, tenant);
        writer.Tenants.Add(new Tenant
        {
            Id = tenant, FullName = "External enrollment storage", Slug = $"external-enrollment-{tenant:N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        });
        await writer.SaveChangesAsync();
        return tenant;
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid? tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        options.AddInterceptors(role == PrimaryDatabaseRole.Runtime
            ? PostgresTenantSessionInterceptor.RuntimeInstance : PostgresTenantSessionInterceptor.Instance);
        options.UseNpgsql(provider => provider.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        var context = new ExploreDbContext(options.Options) { TenantContext = tenant is { } id ? new SelectedTenant(id) : null };
        context.BindTenantLease();
        return context;
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;

    private static async Task ExecuteAsync(ExploreDbContext context, string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
