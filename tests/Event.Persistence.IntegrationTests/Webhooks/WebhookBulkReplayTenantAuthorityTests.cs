using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Webhooks;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class WebhookBulkReplayTenantAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task QueueAcquisitionRequiresAnExactLeaseAndNativeTransaction()
    {
        var rows = await SeedAsync();
        await using var context = fixture.CreateDbContext();
        context.ClearTenantFilterBypass();
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new WebhookBulkReplayRepository(context).GetNextQueuedForUpdateAsync(default));
            await transaction.RollbackAsync();
        }
        await using var bound = fixture.CreateDbContext();
        bound.TenantContext = new TenantScope(rows.TenantId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WebhookBulkReplayRepository(bound).GetNextQueuedForUpdateAsync(default));
    }

    [Test]
    public async Task EarlierForeignQueueCannotReplaceTheExactTenantOperation()
    {
        var rows = await SeedAsync();
        await using var context = fixture.CreateDbContext();
        context.TenantContext = new TenantScope(rows.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var selected = await new WebhookBulkReplayRepository(context).GetNextQueuedForUpdateAsync(default);

        await Assert.That(selected!.Id).IsEqualTo(rows.OperationId);
        await Assert.That(selected.TenantId).IsEqualTo(rows.TenantId);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task CancellationRevokesQueuedExecutionWithoutSelectingAnotherTenant()
    {
        var rows = await SeedAsync();
        await using (var cancellation = fixture.CreateDbContext())
        {
            var operation = await cancellation.WebhookBulkReplayOperations.SingleAsync(row => row.Id == rows.OperationId);
            operation.Cancel("operator.cancelled", Now.AddMinutes(1));
            await cancellation.SaveChangesAsync();
        }
        await using var context = fixture.CreateDbContext();
        context.TenantContext = new TenantScope(rows.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var selected = await new WebhookBulkReplayRepository(context).GetNextQueuedForUpdateAsync(default);

        await Assert.That(selected).IsNull();
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task CurrentSuspensionInvalidatesEarlierTenantDirectorySelection()
    {
        var rows = await SeedAsync();
        await using (var suspension = fixture.CreateDbContext())
        {
            var tenant = await suspension.Tenants.SingleAsync(row => row.Id == rows.TenantId);
            tenant.TenantStatusId = (int)TenantStatusEnum.Suspended;
            await suspension.SaveChangesAsync();
        }
        await using var context = fixture.CreateDbContext();
        context.TenantContext = new TenantScope(rows.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var selected = await new WebhookBulkReplayRepository(context).GetNextQueuedForUpdateAsync(default);

        await Assert.That(selected).IsNull();
        await transaction.RollbackAsync();
    }

    private async Task<Rows> SeedAsync()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            FullName = "Exact replay tenant",
            Slug = $"replay-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var foreign = new Tenant
        {
            FullName = "Foreign replay tenant",
            Slug = $"foreign-replay-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        context.Tenants.AddRange(tenant, foreign);
        await context.SaveChangesAsync();
        var operation = Operation(tenant.Id, Now);
        context.WebhookBulkReplayOperations.AddRange(operation, Operation(foreign.Id, Now.AddMinutes(-1)));
        await context.SaveChangesAsync();
        return new Rows(tenant.Id, operation.Id);
    }

    private static WebhookBulkReplayOperation Operation(Guid tenantId, DateTime queuedAt) =>
        WebhookBulkReplayOperation.Create(tenantId, Guid.CreateVersion7(), $"sha256:{new string('a', 64)}",
            Now.AddDays(-1), Now, null, null, null, 1, "operator.recovery",
            new WebhookBulkReplayPreviewSnapshot(0, 0, 0, 0, 0, 0, 0, 0, 0), queuedAt);

    private sealed record Rows(Guid TenantId, Guid OperationId);
    private sealed record TenantScope(Guid TenantId) : ITenantContext;
}
