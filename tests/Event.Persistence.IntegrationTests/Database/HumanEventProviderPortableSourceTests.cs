using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;

namespace Event.Persistence.IntegrationTests.Database;

/// <summary>
/// Actual portable engine predicates, including empty contributor tables. These exercise the
/// source SQL input rather than substituting a successful human lease; authenticated custody
/// and final own-transition admission are exercised separately by the API cohort.
/// </summary>
[NotInParallel("AdmissionAuthorityProviderDb")]
[ClassDataSource<AdmissionAuthorityProviderFixture>(Shared = SharedType.PerClass)]
public sealed class HumanEventProviderPortableSourceTests(AdmissionAuthorityProviderFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    public async Task ActualBindingProfileAndEmptyCapabilityRangeRemainCurrentInTheOriginalOwner(PrimaryDatabaseProvider provider)
    {
        Guid tenant = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        DateTime now = DateTime.UtcNow;
        await using var setup = Open(provider, tenant);
        await CreateSourceRelationsAsync(setup, provider);
        var connection = RegistrationProviderConnection.Create(tenant, "Portable source",
            RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.HostedSaas,
            "source", "hosted", "1", "1", "source", "https://provider.example", "https://provider.example", "source", null, null, now);
        var form = RegistrationForm.Create(tenant, eventId, "source", "form", "Source form", now);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, now);
        form.AddVersion(version);
        var binding = RegistrationProviderBinding.Create(tenant, connection.Id, form.Id, version.Id,
            RegistrationProviderPresentationModeEnum.Manual, RegistrationProviderCollectionModeEnum.ProviderHosted,
            RegistrationProviderCompletionModeEnum.Manual, RegistrationProviderTrustLevelEnum.FullSync, null, now);
        binding.SetDraftProvisionedSurvey("source", "1");
        setup.AddRange(connection, form, binding);
        await setup.SaveChangesAsync();

        await using var context = Open(provider, tenant);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var initial = await ReadAsync(context, tenant, eventId, binding.Id);
        await Assert.That(initial[HumanEventProviderSource.Binding].Single()).IsEqualTo(binding.Id);
        await Assert.That(initial[HumanEventProviderSource.Connection].Single()).IsEqualTo(connection.Id);
        await Assert.That(initial[HumanEventProviderSource.Capability].Count).IsEqualTo(0);
        await using (var writer = Open(provider, tenant))
        {
            await writer.Database.OpenConnectionAsync();
            // The owner remains held throughout. This bounds a negative lock assertion; no
            // timer releases it and no success depends on scheduling or a polling interval.
            await writer.Database.ExecuteSqlRawAsync(provider == PrimaryDatabaseProvider.SqlServer
                ? "SET LOCK_TIMEOUT 0" : "SET SESSION innodb_lock_wait_timeout = 1");
            await using var attempt = await writer.Database.BeginTransactionAsync();
            writer.Add(RegistrationProviderCapability.Create(binding, "source", "hosted", "1", "1", "source", "MANUAL"));
            Exception? failure = null;
            try { await writer.SaveChangesAsync(); }
            catch (DbUpdateException error) { failure = error.InnerException; }
            await Assert.That(failure is SqlException { Number: 1222 } or MySqlException { Number: 1205 }).IsTrue();
            await attempt.RollbackAsync();
        }
        var capability = RegistrationProviderCapability.Create(binding, "source", "hosted", "1", "1", "source", "MANUAL");
        context.Add(capability);
        await context.SaveChangesAsync();
        var final = await ReadAsync(context, tenant, eventId, binding.Id);
        await Assert.That(final[HumanEventProviderSource.Capability].Single()).IsEqualTo(capability.Id);
        await Assert.That(initial[HumanEventProviderSource.Capability].Count).IsEqualTo(0);
        await owner.RollbackAsync();
        await Assert.That(await setup.RegistrationProviderCapabilities.AsNoTracking()
            .AnyAsync(row => row.RegistrationProviderBindingId == binding.Id)).IsFalse();
    }

    private static async Task<Dictionary<HumanEventProviderSource, List<Guid>>> ReadAsync(
        ExploreDbContext context, Guid tenant, Guid eventId, Guid binding)
    {
        var sourceSql = new HumanEventProviderSourceSql(context, current: true);
        var result = new Dictionary<HumanEventProviderSource, List<Guid>>();
        foreach (var source in HumanEventProviderSourceSql.Sources)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
            command.CommandText = sourceSql.Query(source.Source, source.Type);
            foreach (var argument in new (string Name, Guid? Value)[]
            {
                ("tenant", tenant), ("event", eventId), ("binding", binding), ("connection", null),
                ("form", null), ("version", null), ("submission", null), ("effect", null), ("secret", null)
            })
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = argument.Name;
                parameter.DbType = DbType.Guid;
                parameter.Value = argument.Value.HasValue ? argument.Value.Value : DBNull.Value;
                command.Parameters.Add(parameter);
            }
            await using var reader = await command.ExecuteReaderAsync();
            var ids = new List<Guid>();
            while (await reader.ReadAsync())
            {
                object value = reader.GetValue(reader.GetOrdinal("Id"));
                ids.Add(value is Guid id ? id : Guid.Parse((string)value));
            }
            result.Add(source.Source, ids);
        }
        return result;
    }

    private static async Task CreateSourceRelationsAsync(ExploreDbContext context, PrimaryDatabaseProvider provider)
    {
        bool sqlServer = provider == PrimaryDatabaseProvider.SqlServer;
        if (sqlServer)
            await context.Database.ExecuteSqlRawAsync("IF SCHEMA_ID(N'islamu_event') IS NULL EXEC(N'CREATE SCHEMA [islamu_event]')");
        var names = new HumanPrincipalSourceSql(context);
        foreach (var source in HumanEventProviderSourceSql.Sources)
        {
            var entity = context.Model.FindEntityType(source.Type)!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var helper = context.GetService<ISqlGenerationHelper>();
            string columns = string.Join(",", entity.GetProperties().Select(property =>
                $"{helper.DelimitIdentifier(property.GetColumnName(store)!)} {property.GetRelationalTypeMapping().StoreType}"
                + (property.IsNullable ? " NULL" : " NOT NULL")));
            string table = names.Table(source.Type);
            string primaryKey = names.Column(source.Type, "Id");
            await context.Database.ExecuteSqlRawAsync(sqlServer
                ? $"IF OBJECT_ID(N'{table}',N'U') IS NULL CREATE TABLE {table} ({columns},PRIMARY KEY({primaryKey}))"
                : $"CREATE TABLE IF NOT EXISTS {table} ({columns},PRIMARY KEY({primaryKey})) ENGINE=InnoDB");
        }
    }

    private ExploreDbContext Open(PrimaryDatabaseProvider provider, Guid tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.CreateOptions(provider));
        return new ExploreDbContext(options.Options) { TenantContext = new TenantIdentity(tenant) };
    }
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
}
