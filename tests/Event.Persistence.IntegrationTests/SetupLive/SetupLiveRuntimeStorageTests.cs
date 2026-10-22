using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.SetupLive;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class SetupLiveRuntimeStorageTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task SelectedRuntimeCanObserveAnUnusedEnrollmentIssuanceKey()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using var context = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenantId));
        context.BindTenantLease();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            ISetupLiveRepository repository = new SetupLiveRepository(context);
            await Assert.That(await repository.FindIssuanceClaimAsync(
                tenantId, Guid.CreateVersion7(), CancellationToken.None)).IsNull();
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await transaction.RollbackAsync();
        });
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
