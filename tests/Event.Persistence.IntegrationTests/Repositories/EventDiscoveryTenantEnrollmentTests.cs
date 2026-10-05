using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PersistenceDb")]
public sealed class EventDiscoveryTenantEnrollmentTests(PostgreSqlContainerFixture postgres)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    [Test]
    public async Task Cold_lookup_bootstrap_and_global_source_save_fence_the_first_tenant_until_commit()
    {
        await postgres.ResetAsync();
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        await using var writer = postgres.CreateDbContext();
        await Assert.That(await writer.Tenants.AnyAsync(token)).IsFalse();
        await Assert.That(await writer.Set<TenantStatus>()
            .AnyAsync(status => status.Id == (int)TenantStatusEnum.Active, token)).IsTrue();
        var tenant = Tenant();
        var policy = Policy();
        await new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async ct =>
        {
            writer.SystemSettings.Add(policy);
            await writer.SaveChangesAsync(ct);
            await Assert.That(await EnrollmentAvailableAsync(ct)).IsFalse();
            await Assert.That(await writer.Tenants.AnyAsync(ct)).IsFalse();
            writer.Tenants.Add(tenant);
            await writer.SaveChangesAsync(ct);
            await Assert.That(await EnrollmentAvailableAsync(ct)).IsFalse();
            return true;
        }, token);
        await Assert.That(await EnrollmentAvailableAsync(token)).IsTrue();
        await using var reader = postgres.CreateDbContext();
        await Assert.That(await new TenantRepository(reader).GetByIdAsNoTrackingAsync(tenant.Id, token)).IsNotNull();
        await Assert.That(await reader.SystemSettings.AnyAsync(row => row.Id == policy.Id, token)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Global_fanout_rejects_a_snapshot_predating_committed_tenant_enrollment(bool emptyCatalog)
    {
        await postgres.ResetAsync();
        using var deadline = new CancellationTokenSource(Deadline);
        var token = deadline.Token;
        if (!emptyCatalog)
        {
            await using var seed = postgres.CreateDbContext();
            seed.Tenants.Add(Tenant());
            await seed.SaveChangesAsync(token);
        }

        var enrolled = Tenant();
        var policy = Policy();
        await using (var stale = postgres.CreateDbContext())
            await stale.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var snapshot = await stale.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
                await Assert.That(await stale.Tenants.CountAsync(token)).IsEqualTo(emptyCatalog ? 0 : 1);
                // The completed writer is the synchronization signal: the global source
                // must never commit against the older catalog, including an empty one.
                await using (var creator = postgres.CreateDbContext())
                {
                    creator.Tenants.Add(enrolled);
                    await creator.SaveChangesAsync(token);
                }
                stale.SystemSettings.Add(policy);
                if (emptyCatalog)
                    await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () => await stale.SaveChangesAsync(token));
                else
                {
                    var failure = await Assert.ThrowsAsync<PostgresException>(async () => await stale.SaveChangesAsync(token));
                    await Assert.That(failure.SqlState).IsEqualTo(PostgresErrorCodes.SerializationFailure);
                }
                await snapshot.RollbackAsync(token);
            });

        await using var fresh = postgres.CreateDbContext();
        await Assert.That(await fresh.SystemSettings.AnyAsync(row => row.Id == policy.Id, token)).IsFalse();
        fresh.SystemSettings.Add(policy);
        await fresh.SaveChangesAsync(token);
        await using var tenantReader = postgres.CreateTenantFilteredDbContext(new TenantScope(enrolled.Id));
        var revision = await new EventDiscoveryIdentityRepository(tenantReader).GetRevisionAsync(enrolled.Id, token);
        await Assert.That(revision).IsNotNull();
        await Assert.That(revision!.DisclosureEpoch).IsEqualTo(1);
    }

    private async Task<bool> EnrollmentAvailableAsync(CancellationToken token)
    {
        await using var probe = postgres.CreateDbContext();
        return await probe.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await probe.Database.BeginTransactionAsync(token);
            return await probe.Database.SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_xact_lock({0}) AS \"Value\"",
                RelationalNamedLock.ComputeStableKey(EventDiscoverySourceProviderOperations.TenantEnrollmentResource))
                .SingleAsync(token);
        });
    }

    private static Tenant Tenant() => new()
    {
        Id = Guid.CreateVersion7(),
        Slug = $"discovery-enrollment-{Guid.CreateVersion7():N}",
        FullName = "Discovery enrollment tenant",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private static SystemSetting Policy() => new()
    {
        Id = Guid.CreateVersion7(),
        SettingKey = $"public_experience.enrollment_{Guid.CreateVersion7():N}",
        Value = "false",
        ValueType = SettingValueType.Boolean
    };

    private sealed record TenantScope(Guid TenantId) : ITenantContext;
}
