using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresTenantSessionProbeTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments("match", false, true)]
    [Arguments("other", false, false)]
    [Arguments("malformed", false, false)]
    [Arguments("empty", false, false)]
    [Arguments("empty", true, true)]
    [Arguments("match", true, false)]
    [Arguments("malformed", true, false)]
    public async Task OriginalTransactionMustCarryTheExpectedTenant(string state, bool absentTenant, bool expected)
    {
        await using var context = fixture.CreateDbContext();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var tenantId = Guid.CreateVersion7();
        string setting = state switch
        {
            "match" => tenantId.ToString(),
            "other" => Guid.CreateVersion7().ToString(),
            "malformed" => "invalid-tenant",
            "empty" => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.current_tenant_id', {setting}, true)");

        bool result = await PostgresTenantSessionProbe.MatchesAsync(
            context, transaction, absentTenant ? null : tenantId, CancellationToken.None);

        await Assert.That(result).IsEqualTo(expected);
        await transaction.RollbackAsync();
    }
}
