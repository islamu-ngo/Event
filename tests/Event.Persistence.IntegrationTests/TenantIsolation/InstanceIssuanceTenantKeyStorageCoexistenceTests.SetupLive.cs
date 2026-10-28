using System.Data;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.SetupLive;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

public sealed partial class InstanceIssuanceTenantKeyStorageCoexistenceTests
{
    [Test]
    public async Task SetupStorageSupportsNativeLifecycleWithoutCrossTenantOrImmutableWrites()
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        Guid tenantId = await CreateSetupTenantAsync(token);
        Guid otherTenantId = await CreateSetupTenantAsync(token);
        Guid actorId = Guid.CreateVersion7();
        DateTime now = DateTime.UtcNow;
        // Public inert digests represent stored commitments, never plaintext credentials.
        string digest = new('a', 64);
        var enrollment = SetupTargetEnrollment.Create(Guid.CreateVersion7(), tenantId, actorId,
            digest, digest, digest, now, now.AddHours(1));
        var claim = SetupEnrollmentIssuanceClaim.Create(Guid.CreateVersion7(), tenantId, actorId,
            Guid.CreateVersion7(), enrollment.Id, enrollment.Generation, digest, now);
        var operation = SetupSecretBindingOperation.CreateAccepted(Guid.CreateVersion7(), tenantId,
            actorId, enrollment.Id, enrollment.Generation, Guid.CreateVersion7(), "setup.signing",
            digest, 1, digest, now);

        await using (var runtime = SetupRuntime(tenantId))
        {
            var repository = new SetupLiveRepository(runtime);
            await Assert.That(await repository.FindIssuanceClaimAsync(tenantId, claim.OperationKey, token)).IsNull();
            await repository.AddAsync(enrollment, token);
            await repository.AddAsync(claim, token);
            await repository.AddAsync(operation, token);
            await repository.SaveChangesAsync(token);
        }
        await using (var runtime = SetupRuntime(tenantId))
        {
            var repository = new SetupLiveRepository(runtime);
            var stored = (await repository.FindEnrollmentAsync(tenantId, enrollment.Id, token))!;
            await Assert.That((await repository.FindIssuanceClaimAsync(tenantId, claim.OperationKey, token))?.Id)
                .IsEqualTo(claim.Id);
            var accepted = (await repository.FindOperationAsync(tenantId, operation.OperationKey, token))!;
            accepted.Succeed(now.AddMinutes(1));
            stored.RotateCapability(new string('b', 64), now.AddHours(2), now.AddMinutes(1));
            await repository.SaveChangesAsync(token);
        }
        await using (var runtime = SetupRuntime(tenantId))
        {
            var repository = new SetupLiveRepository(runtime);
            await Assert.That((await repository.FindEnrollmentAsync(tenantId, enrollment.Id, token))?.Generation).IsEqualTo(2L);
            await Assert.That((await repository.FindOperationAsync(tenantId, operation.OperationKey, token))?.State)
                .IsEqualTo(SetupSecretBindingOperationState.Succeeded);
            var names = new HumanPrincipalSourceSql(runtime);
            string enrollments = names.Table(typeof(SetupTargetEnrollment));
            string claims = names.Table(typeof(SetupEnrollmentIssuanceClaim));
            await runtime.Database.OpenConnectionAsync(token);
            await using (var foreignInsert = Command(runtime, $"""
                INSERT INTO {enrollments}
                    (id,tenant_id,actor_id,challenge_digest,capability_digest,scope_digest,generation,state,created_at,expires_at,concurrency_stamp)
                SELECT @id,@foreign,actor_id,challenge_digest,capability_digest,scope_digest,generation,state,created_at,expires_at,@stamp
                FROM {enrollments} WHERE id=@original
                """))
            {
                foreignInsert.Parameters.AddWithValue("id", Guid.CreateVersion7());
                foreignInsert.Parameters.AddWithValue("foreign", otherTenantId);
                foreignInsert.Parameters.AddWithValue("stamp", Guid.CreateVersion7());
                foreignInsert.Parameters.AddWithValue("original", enrollment.Id);
                var rejected = await Assert.ThrowsAsync<PostgresException>(() => foreignInsert.ExecuteNonQueryAsync(token));
                await Assert.That(rejected.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
            foreach (string sql in new[]
            {
                $"UPDATE {enrollments} SET tenant_id=tenant_id WHERE id='{enrollment.Id:D}'",
                $"UPDATE {claims} SET request_fingerprint=request_fingerprint WHERE id='{claim.Id:D}'",
                $"DELETE FROM {enrollments} WHERE id='{enrollment.Id:D}'"
            })
            {
                var rejected = await Assert.ThrowsAsync<PostgresException>(() => runtime.Database.ExecuteSqlRawAsync(sql, token));
                await Assert.That(rejected.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
        }
        await using (var foreign = SetupRuntime(otherTenantId))
        {
            var repository = new SetupLiveRepository(foreign);
            await Assert.That(await repository.FindEnrollmentAsync(tenantId, enrollment.Id, token)).IsNull();
            string table = new HumanPrincipalSourceSql(foreign).Table(typeof(SetupTargetEnrollment));
            // Raw SQL has no EF tenant predicate: the database must still hide the foreign row.
            await using var command = Command(foreign, $"SELECT count(*) FROM {table} WHERE id=@id");
            await foreign.Database.OpenConnectionAsync(token);
            command.Parameters.AddWithValue("id", enrollment.Id);
            await Assert.That(await command.ExecuteScalarAsync(token)).IsEqualTo(0L);
        }
    }

    [Test]
    [Arguments("claim-update")]
    [Arguments("tenant-update")]
    [Arguments("delete")]
    public async Task SetupStorageRejectsWidenedGrantsWithoutRepair(string drift)
    {
        CancellationToken token = TestContext.Current!.Execution.CancellationToken;
        await using var migrator = OpenMigrator();
        await migrator.Database.OpenConnectionAsync(token);
        await Assert.That(await CompleteKeyManifestAsync(migrator, token)).IsEqualTo(true);
        await PostgresTenantApiKeyStoragePermissions.ApplySetupLiveAsync(migrator, fixture.NativeQueueEnrollment, token);
        await Assert.That(await CompleteKeyManifestAsync(migrator, token)).IsEqualTo(true);
        var names = new HumanPrincipalSourceSql(migrator);
        string table = names.Table(drift == "claim-update" ? typeof(SetupEnrollmentIssuanceClaim) : typeof(SetupTargetEnrollment));
        string privilege = drift switch
        {
            "claim-update" => "UPDATE (request_fingerprint)",
            "tenant-update" => "UPDATE (tenant_id)",
            _ => "DELETE"
        };
        await migrator.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await migrator.Database.BeginTransactionAsync(token);
            await migrator.Database.ExecuteSqlRawAsync(
                $"GRANT {privilege} ON {table} TO {names.Quote(fixture.NativeQueueEnrollment.RuntimeLogin)}", token);
            string original = await RuntimeColumnAclAsync(migrator, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                PostgresTenantApiKeyStoragePermissions.ApplySetupLiveAsync(migrator, fixture.NativeQueueEnrollment, token));
            await Assert.That(await RuntimeColumnAclAsync(migrator, token)).IsEqualTo(original);
            if (drift == "delete")
            {
                await using var unchanged = Command(migrator, "SELECT pg_catalog.has_table_privilege(@runtime,@table,'DELETE')");
                unchanged.Parameters.AddWithValue("runtime", fixture.NativeQueueEnrollment.RuntimeLogin);
                unchanged.Parameters.AddWithValue("table", table);
                await Assert.That(await unchanged.ExecuteScalarAsync(token)).IsEqualTo(true);
            }
            await transaction.RollbackAsync(CancellationToken.None);
        });
        await PostgresTenantApiKeyStoragePermissions.ApplySetupLiveAsync(migrator, fixture.NativeQueueEnrollment, token);
    }

    private ExploreDbContext SetupRuntime(Guid tenantId)
    {
        var context = fixture.CreateEnrolledTenantDbContext(new SetupTenant(tenantId));
        context.BindTenantLease();
        return context;
    }

    private async Task<Guid> CreateSetupTenantAsync(CancellationToken token)
    {
        Guid id = Guid.CreateVersion7();
        await using var owner = OpenMigrator();
        await owner.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var humanGate = new HumanSecuritySourceGate(owner);
            await using var human = await humanGate.AcquireAsync(token);
            await using var discoveryGate = new EventDiscoveryEnrollmentGate(owner);
            await using var discovery = await discoveryGate.AcquireAsync(token);
            await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            owner.Tenants.Add(new Tenant
            {
                Id = id,
                FullName = "Setup storage",
                Slug = $"setup-storage-{id:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            await owner.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        });
        return id;
    }

    private sealed record SetupTenant(Guid TenantId) : ITenantContext;
}
