using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<AdmissionAuthorityProviderFixture>(Shared = SharedType.PerClass)]
public sealed class ActorMediaCutoverProviderTests(AdmissionAuthorityProviderFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer, false)]
    [Arguments(PrimaryDatabaseProvider.SqlServer, true)]
    [Arguments(PrimaryDatabaseProvider.MariaDb, false)]
    [Arguments(PrimaryDatabaseProvider.MariaDb, true)]
    [Arguments(PrimaryDatabaseProvider.MySql, false)]
    [Arguments(PrimaryDatabaseProvider.MySql, true)]
    public async Task GeneratedCutoverValidatesBeforeRename(
        PrimaryDatabaseProvider provider, bool hasLegacyMedia)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, fixture.CreateOptions(provider, PrimaryDatabaseRole.Migrator));
        await using var context = new ExploreDbContext(options.Options);
        await context.Database.OpenConnectionAsync();
        string name = $"cutover_{Guid.NewGuid():N}_actor_pii";
        var sql = context.GetService<ISqlGenerationHelper>();
        string table = sql.DelimitIdentifier(name);
        await context.Database.ExecuteSqlRawAsync(
            $"CREATE TABLE {table} (profile_picture_uri varchar(200) NULL)");
        await using (var seed = context.Database.GetDbConnection().CreateCommand())
        {
            seed.CommandText = $"INSERT INTO {table} (profile_picture_uri) VALUES (@uri)";
            var parameter = seed.CreateParameter();
            parameter.ParameterName = "@uri";
            parameter.Value = hasLegacyMedia ? "https://foreign.example.test/image.png" : DBNull.Value;
            seed.Parameters.Add(parameter);
            await seed.ExecuteNonQueryAsync();
        }

        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(
            [new RenameColumnOperation
            {
                Table = name,
                Name = "profile_picture_uri",
                NewName = "external_profile_picture_uri"
            }], context.Model);
        bool rejected = false;
        try
        {
            foreach (var generated in commands)
                await context.Database.ExecuteSqlRawAsync(generated.CommandText);
        }
        catch (DbException exception) when (hasLegacyMedia
            && exception.Message.Contains("ck_actor_media_no_unclassified_legacy_source",
                StringComparison.Ordinal))
        {
            rejected = true;
        }

        await Assert.That(rejected).IsEqualTo(hasLegacyMedia);
        await using var read = context.Database.GetDbConnection().CreateCommand();
        read.CommandText = $"SELECT {sql.DelimitIdentifier(hasLegacyMedia
            ? "profile_picture_uri" : "external_profile_picture_uri")} FROM {table}";
        await Assert.That(await read.ExecuteScalarAsync())
            .IsEqualTo(hasLegacyMedia ? (object)"https://foreign.example.test/image.png" : DBNull.Value);
    }
}
