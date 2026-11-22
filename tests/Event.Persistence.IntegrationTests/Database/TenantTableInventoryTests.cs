using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Database;

[Category("TenantModelInventory")]
public sealed class TenantTableInventoryTests
{
    [Test]
    public async Task PreparedInventoryCannotBeChangedByAnotherConsumer()
    {
        await using var context = CreateContext();
        var inventory = context.GetTenantTableInventory();
        bool mutationRefused = inventory is not IList<TenantTableMetadata>;
        if (inventory is IList<TenantTableMetadata> writable)
        {
            try
            {
                writable.Clear();
            }
            catch (NotSupportedException)
            {
                mutationRefused = true;
            }
        }

        await Assert.That(mutationRefused).IsTrue();
        await Assert.That(context.GetTenantTableInventory().Any(table =>
            table.TableName == "events" && table.TenantIdColumn == "tenant_id")).IsTrue();
    }

    [Test]
    public async Task IndependentOwnersKeepTheirPreparedInventory()
    {
        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstInventory = first.GetTenantTableInventory();
        var secondInventory = second.GetTenantTableInventory();

        await Assert.That(ReferenceEquals(firstInventory, first.GetTenantTableInventory())).IsTrue();
        await Assert.That(ReferenceEquals(firstInventory, secondInventory)).IsFalse();
        await Assert.That(secondInventory.Single(table => table.TableName == "events").TenantIdColumn)
            .IsEqualTo("tenant_id");
    }

    [Test]
    public async Task ArbitraryMutableModelIsDerivedAgainAfterItsMappingChanges()
    {
        var builder = new ModelBuilder();
        builder.Entity<TenantRow>().HasKey(row => row.Id);
        builder.Entity<TenantRow>().Property(row => row.TenantId);
        builder.Entity<TenantRow>().ToTable("before");
        builder.Entity<TenantRow>().HasQueryFilter(QueryFilterNames.Tenant,
            row => row.TenantId == Guid.Empty);
        var before = PostgresTenantRowLevelSecurityModel.GetTenantTables((Microsoft.EntityFrameworkCore.Metadata.IModel)builder.Model);

        builder.Entity<TenantRow>().ToTable("after");
        var after = PostgresTenantRowLevelSecurityModel.GetTenantTables((Microsoft.EntityFrameworkCore.Metadata.IModel)builder.Model);

        await Assert.That(before.Single().TableName).IsEqualTo("before");
        await Assert.That(after.Single().TableName).IsEqualTo("after");
    }

    private static ExploreDbContext CreateContext() => new(
        TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(new NpgsqlConnection())
            .UseSnakeCaseNamingConvention().Options);

    private sealed class TenantRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
    }
}
