using Explore.Domain;
using Explore.Persistence;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class DesignTimeModelOnlyFactoryTests
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    public async Task ScaffoldingRetainsProviderModelWithoutConnectionAuthority(PrimaryDatabaseProvider provider)
    {
        await using var context = new ExploreDbContextFactory().CreateDbContext(
            ["--DesignTime:ModelOnly=true", "--DOTNET_ENVIRONMENT=Testing",
                $"--Database:Provider={provider}",
                $"--Database:ServerVersion={(provider == PrimaryDatabaseProvider.MariaDb ? "10.11.0" : "8.4.0")}"]);
        await Assert.That(context.Database.GetConnectionString()).IsNull();
        await Assert.That(provider switch
        {
            PrimaryDatabaseProvider.PostgreSql => context.Database.IsNpgsql(),
            PrimaryDatabaseProvider.Sqlite => context.Database.IsSqlite(),
            PrimaryDatabaseProvider.SqlServer => context.Database.IsSqlServer(),
            _ => context.Database.IsMySql()
        }).IsTrue();
        var operation = context.Model.FindEntityType(typeof(ConfigurationImportOperation))!;
        var index = operation.GetIndexes().Single(index => index.IsUnique
            && index.Properties.Any(property => property.Name == nameof(ConfigurationImportOperation.SessionId)));
        if (provider is PrimaryDatabaseProvider.MySql or PrimaryDatabaseProvider.MariaDb)
        {
            var slot = index.Properties.Single(property => property.Name == "SuccessSlot");
            await Assert.That(slot.IsNullable).IsTrue();
            await Assert.That(slot.GetComputedColumnSql()).IsNotNull();
        }
        else
            await Assert.That(index.GetFilter()).IsNotNull();
    }

    [Test]
    public async Task ProductionCannotSelectModelOnlyScaffolding()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            new ExploreDbContextFactory().CreateDbContext(
                ["--DesignTime:ModelOnly=true", "--DOTNET_ENVIRONMENT=Production", "--Database:Provider=Sqlite"]);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task ModelOnlyScaffoldingRejectsUndefinedProvider()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            new ExploreDbContextFactory().CreateDbContext(
                ["--DesignTime:ModelOnly=true", "--DOTNET_ENVIRONMENT=Testing", "--Database:Provider=999"]);
            return Task.CompletedTask;
        });
    }
}
