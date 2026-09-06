// ABOUTME: Verifies current PostgreSQL address-governance constraints and five-provider schema parity.
// ABOUTME: Pins the four development-only rebaselines while proving lookup IDs, FKs, defaults, and checks.

#nullable enable

using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using TUnit.Assertions.Enums;

namespace Event.Persistence.IntegrationTests.Migrations;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class LocationAddressGovernanceMigrationTests(PostgreSqlContainerFixture fixture)
{
    private static readonly Guid TenantId = Id(1);
    private static readonly Guid ForeignTenantId = Id(2);
    private static readonly Guid LocationId = Id(3);
    private static readonly Guid OrganizationId = Id(4);
    private static readonly Guid ForeignOrganizationId = Id(5);
    private static readonly Guid ActorId = Id(6);

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task ProviderMigrationTopologyMatchesDevelopmentRebaselineContract(string provider)
    {
        await using ExploreDbContext context = CreateModelContext(provider);
        string[] migrations = context.Database.GetMigrations().ToArray();

        await Assert.That(migrations).HasSingleItem();
        await Assert.That(migrations[0]).EndsWith("_Init");
        await Assert.That(HasPendingModelChanges(context)).IsFalse();
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task ProviderInitEmbedsRequiredLegalIdentityConstraints(string provider)
    {
        await using ExploreDbContext context = CreateModelContext(provider);
        IMigrationsAssembly migrations = context.GetService<IMigrationsAssembly>();
        string migrationId = context.Database.GetMigrations().Single();
        Migration init = migrations.CreateMigration(
            migrations.Migrations[migrationId],
            context.Database.ProviderName);
        string tableName = provider is "Sqlite" or "MariaDb" or "MySql"
            ? "ie_paid_order_acceptance_snapshots"
            : "paid_order_acceptance_snapshots";
        CreateTableOperation acceptance = init.UpOperations
            .OfType<CreateTableOperation>()
            .Single(operation => operation.Name == tableName);

        await AssertRequiredColumnAsync(acceptance, "organizer_actor_id");
        await AssertRequiredColumnAsync(
            acceptance,
            "organizer_payment_provider_connection_id");
        await AssertRequiredColumnAsync(
            acceptance,
            "tenant_directory_operator_document_id");
        await AssertRequiredColumnAsync(
            acceptance,
            "tenant_directory_operator_revision_id");
        await AssertRequiredColumnAsync(acceptance, "connect_platform_id", 120);
        await AssertRequiredColumnAsync(acceptance, "external_account_id", 200);
        await AssertRequiredColumnAsync(acceptance, "merchant_country_code", 2);
        await AssertRequiredColumnAsync(acceptance, "operator_legal_name", 300);
        await AssertRequiredColumnAsync(acceptance, "operator_kind_code", 80);
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task ProviderModelHasExactPortableColumnsDefaultsForeignKeysAndSemanticChecks(string provider)
    {
        await using ExploreDbContext context = CreateModelContext(provider);
        IEntityType location = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Location))
            ?? throw new InvalidOperationException("Location model is missing.");
        IProperty source = location.FindProperty("AddressSourceId")
            ?? throw new InvalidOperationException("AddressSourceId model property is missing.");
        IProperty visibility = location.FindProperty("AddressVisibilityId")
            ?? throw new InvalidOperationException("AddressVisibilityId model property is missing.");
        IProperty organization = location.FindProperty("AddressOrganizationId")
            ?? throw new InvalidOperationException("AddressOrganizationId model property is missing.");
        IProperty displayKey = location.FindProperty(nameof(Location.DisplaySortKey))
            ?? throw new InvalidOperationException("DisplaySortKey model property is missing.");
        IProperty displayVersion = location.FindProperty(nameof(Location.DisplaySortKeyVersion))
            ?? throw new InvalidOperationException("DisplaySortKeyVersion model property is missing.");
        IEntityType pii = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(LocationPii))
            ?? throw new InvalidOperationException("LocationPii model is missing.");
        IProperty addressKey = pii.FindProperty(nameof(LocationPii.AddressSubstringKey))
            ?? throw new InvalidOperationException("AddressSubstringKey model property is missing.");
        IProperty addressVersion = pii.FindProperty(nameof(LocationPii.AddressSubstringKeyVersion))
            ?? throw new InvalidOperationException("AddressSubstringKeyVersion model property is missing.");

        await Assert.That(source.IsNullable).IsFalse();
        await Assert.That(source.GetDefaultValue()).IsEqualTo(1);
        await Assert.That(visibility.IsNullable).IsFalse();
        await Assert.That(visibility.GetDefaultValue()).IsEqualTo(1);
        await Assert.That(organization.IsNullable).IsTrue();
        await Assert.That(displayKey.FindAnnotation(RelationalAnnotationNames.DefaultValue)).IsNull();
        await Assert.That(displayVersion.FindAnnotation(RelationalAnnotationNames.DefaultValue)).IsNull();
        await Assert.That(addressKey.FindAnnotation(RelationalAnnotationNames.DefaultValue)).IsNull();
        await Assert.That(addressVersion.FindAnnotation(RelationalAnnotationNames.DefaultValue)).IsNull();
        await Assert.That(displayKey.GetMaxLength()).IsEqualTo(2_000);
        await Assert.That(addressKey.GetMaxLength()).IsEqualTo(2_000);
        string expectedCollation = provider switch
        {
            "PostgreSql" => "C",
            "Sqlite" => "BINARY",
            "SqlServer" => "Latin1_General_100_BIN2",
            "MariaDb" or "MySql" => "utf8mb4_bin",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };
        await Assert.That(displayKey.GetCollation()).IsEqualTo(expectedCollation);
        await Assert.That(addressKey.GetCollation()).IsEqualTo(expectedCollation);
        await Assert.That(location.FindProperty(nameof(LocationPii.AddressSubstringKey))).IsNull();
        await Assert.That(pii.FindProperty(nameof(Location.DisplaySortKey))).IsNull();

        IForeignKey organizationForeignKey = location.GetForeignKeys().Single(foreignKey =>
            foreignKey.Properties.Contains(organization));
        await Assert.That(organizationForeignKey.PrincipalEntityType.ClrType).IsEqualTo(typeof(OrganizationTenant));
        await Assert.That(organizationForeignKey.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(Location.TenantId), "AddressOrganizationId"], CollectionOrdering.Matching);
        await Assert.That(organizationForeignKey.PrincipalKey.Properties.Select(property => property.Name))
            .IsEquivalentTo([nameof(OrganizationTenant.TenantId), nameof(OrganizationTenant.OrganizationId)], CollectionOrdering.Matching);

        string semantics = string.Join(" ", location.GetCheckConstraints().Select(constraint => constraint.Sql))
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        foreach (string token in new[]
        {
            "address_visibility_id", "address_organization_id", "created_by", "location_kind_id", "is null", "is not null"
        })
        {
            await Assert.That(semantics).Contains(token);
        }
        await Assert.That(semantics).Contains("= 2");
        await Assert.That(semantics).Contains("= 3");
        await Assert.That(semantics).Contains("= 4");
        await Assert.That(semantics).Contains("= 5");
    }

    [Test]
    public async Task PostgreSqlInitialInstallsGovernanceDefaultsAndEnforcesEveryCombination()
    {
        string databaseName = $"address_governance_{Guid.CreateVersion7():N}";
        await CreatePostgreSqlDatabaseAsync(databaseName);
        PrimaryDatabaseConnectionOptions database = PostgreSqlOptions(databaseName);
        try
        {
            await using ExploreDbContext context = CreateApplicationContext(database);
            await context.Database.MigrateAsync();
            await LookupTableSeeder.SeedAsync(context);
            await InsertCurrentGraphAsync(context);

            await AssertObservableStateAsync(context);
            await SeedOrganizationsAsync(context);
            await AssertPostgreSqlChecksAsync(context.Database.GetConnectionString()!);
        }
        finally
        {
            await DropPostgreSqlDatabaseAsync(databaseName);
        }
    }

    [Test]
    public async Task SqliteRebaselineInstallsCurrentGovernanceSchemaFromEmpty()
    {
        string path = Path.Combine(Path.GetTempPath(), "location-address-governance-initial.db");
        PrimaryDatabaseConnectionOptions database = SqliteOptions(path);
        DeleteSqlite(path);
        try
        {
            await using ExploreDbContext context = CreateApplicationContext(database);
            string migration = context.Database.GetMigrations().Single();
            await context.GetService<IMigrator>().MigrateAsync(migration);
            await LookupTableSeeder.SeedAsync(context);

            await Assert.That(await context.Database.GetAppliedMigrationsAsync())
                .IsEquivalentTo([migration], CollectionOrdering.Matching);
            await Assert.That(await ReadLookupAsync(context, "ie_location_address_sources"))
                .IsEquivalentTo(["1:UNKNOWN_LEGACY", "2:MANUAL", "3:PROVIDER_SELECTION"], CollectionOrdering.Matching);
            await Assert.That(await ReadLookupAsync(context, "ie_location_address_visibilities"))
                .IsEquivalentTo(
                    ["1:QUARANTINED", "2:CREATOR_PRIVATE", "3:ORGANIZATION_SCOPED", "4:TENANT_APPROVED"],
                    CollectionOrdering.Matching);
        }
        finally
        {
            DeleteSqlite(path);
        }
    }

    [Test]
    public async Task PostgreSqlAndSqliteControlsExecuteRealRelationalCommands()
    {
        await using (var postgres = new NpgsqlConnection(fixture.ConnectionString))
        {
            await postgres.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT 1", postgres);
            await Assert.That(Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture))
                .IsEqualTo(1);
        }

        await using var sqlite = new SqliteConnection("Data Source=:memory:");
        await sqlite.OpenAsync();
        await using var sqliteCommand = sqlite.CreateCommand();
        sqliteCommand.CommandText = "SELECT 1";
        await Assert.That(Convert.ToInt32(await sqliteCommand.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture))
            .IsEqualTo(1);
    }

    private static bool HasPendingModelChanges(ExploreDbContext context)
    {
        IMigrationsAssembly migrationsAssembly = context.GetService<IMigrationsAssembly>();
        IMigrationsModelDiffer differ = context.GetService<IMigrationsModelDiffer>();
        IModel runtime = context.GetService<IDesignTimeModel>().Model;
        IModel snapshot = migrationsAssembly.ModelSnapshot?.Model
            ?? throw new InvalidOperationException("Provider migration snapshot is missing.");
        IModel initialized = context.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot, designTime: true, validationLogger: null);
        return differ.HasDifferences(initialized.GetRelationalModel(), runtime.GetRelationalModel());
    }

    private static async Task AssertRequiredColumnAsync(
        CreateTableOperation table,
        string columnName,
        int? maxLength = null)
    {
        AddColumnOperation column = table.Columns.Single(candidate =>
            candidate.Name == columnName);
        await Assert.That(column.IsNullable).IsFalse();
        if (maxLength is not null)
        {
            await Assert.That(column.MaxLength).IsEqualTo(maxLength);
        }
    }

    private static async Task InsertCurrentGraphAsync(ExploreDbContext context)
    {
        string prefix = context.Database.IsSqlite() ? "ie_" : string.Empty;
        string schema = context.Database.IsNpgsql() ? "islamu_event." : string.Empty;
        string statuses = schema + prefix + "tenant_statuses";
        string tenants = schema + prefix + "tenants";
        string kinds = schema + prefix + "location_kinds";
        string privacyStates = schema + prefix + "location_privacy_states";
        string locations = schema + prefix + "locations";
        string pii = schema + prefix + "location_pii";
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {statuses} (id, master_code, full_name, is_active_state) SELECT {{0}}, {{1}}, {{2}}, {{3}} WHERE NOT EXISTS (SELECT 1 FROM {statuses} WHERE id={{0}})",
            (int)TenantStatusEnum.Active, "ACTIVE", "Active", true);
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {tenants} (id, full_name, slug, tenant_status_id, created_at) VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}), ({{5}}, {{6}}, {{7}}, {{8}}, {{9}})",
            TenantId, "Synthetic legacy tenant", "synthetic-legacy-tenant", (int)TenantStatusEnum.Active, DateTime.UnixEpoch,
            ForeignTenantId, "Synthetic foreign tenant", "synthetic-foreign-tenant", (int)TenantStatusEnum.Active, DateTime.UnixEpoch);
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {kinds} (id, master_code, full_name) SELECT {{0}}, {{1}}, {{2}} WHERE NOT EXISTS (SELECT 1 FROM {kinds} WHERE id={{0}})",
            (int)LocationKindEnum.Unclassified, "UNCLASSIFIED", "Unclassified");
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {privacyStates} (id, master_code, full_name) SELECT {{0}}, {{1}}, {{2}} WHERE NOT EXISTS (SELECT 1 FROM {privacyStates} WHERE id={{0}})",
            (int)LocationPrivacyStateEnum.Active, "ACTIVE", "Active");
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {locations} (id, full_name, country, city, tenant_id, location_kind_id, location_privacy_state_id, created_at, concurrency_stamp, display_sort_key, display_sort_key_version) VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}}, {{5}}, {{6}}, {{7}}, {{8}}, {{9}}, {{10}})",
            LocationId, "Synthetic location", "BE", "Brussels", TenantId,
            (int)LocationKindEnum.Unclassified, (int)LocationPrivacyStateEnum.Active,
            DateTime.UnixEpoch, Id(20), "SYNTHETIC LOCATION", (short)2);
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {pii} (location_id, address, postcode, address_substring_key, address_substring_key_version) VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}})",
            LocationId, "Synthetic address", "0000", "SYNTHETIC ADDRESS", (short)2);
        context.ChangeTracker.Clear();
    }

    private static async Task AssertObservableStateAsync(ExploreDbContext context)
    {
        string prefix = context.Database.IsSqlite() ? "ie_" : string.Empty;
        string schema = context.Database.IsNpgsql() ? "islamu_event." : string.Empty;
        string sources = schema + prefix + "location_address_sources";
        string visibilities = schema + prefix + "location_address_visibilities";
        string locations = schema + prefix + "locations";
        string pii = schema + prefix + "location_pii";

        IReadOnlyList<string> sourceRows = await ReadLookupAsync(context, sources);
        IReadOnlyList<string> visibilityRows = await ReadLookupAsync(context, visibilities);
        await Assert.That(sourceRows).IsEquivalentTo(["1:UNKNOWN_LEGACY", "2:MANUAL", "3:PROVIDER_SELECTION"], CollectionOrdering.Matching);
        await Assert.That(visibilityRows).IsEquivalentTo(
            ["1:QUARANTINED", "2:CREATOR_PRIVATE", "3:ORGANIZATION_SCOPED", "4:TENANT_APPROVED"],
            CollectionOrdering.Matching);

        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        if (command.Connection?.State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync();
        }
        command.CommandText = $"SELECT l.address_source_id, l.address_visibility_id, l.address_organization_id, l.display_sort_key, l.display_sort_key_version, p.address_substring_key, p.address_substring_key_version FROM {locations} l JOIN {pii} p ON p.location_id=l.id WHERE l.id = @id";
        AddParameter(command, "@id", LocationId);
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        await Assert.That(await reader.ReadAsync()).IsTrue();
        await Assert.That(reader.GetInt32(0)).IsEqualTo(1);
        await Assert.That(reader.GetInt32(1)).IsEqualTo(1);
        await Assert.That(reader.IsDBNull(2)).IsTrue();
        await Assert.That(reader.GetString(3)).IsEqualTo("SYNTHETIC LOCATION");
        await Assert.That(reader.GetInt16(4)).IsEqualTo((short)2);
        await Assert.That(reader.GetString(5)).IsEqualTo("SYNTHETIC ADDRESS");
        await Assert.That(reader.GetInt16(6)).IsEqualTo((short)2);
    }

    private static async Task<IReadOnlyList<string>> ReadLookupAsync(ExploreDbContext context, string table)
    {
        await context.Database.OpenConnectionAsync();
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT id, master_code FROM {table} ORDER BY id";
        var rows = new List<string>();
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add($"{reader.GetInt32(0)}:{reader.GetString(1)}");
        }
        return rows;
    }

    private static async Task SeedOrganizationsAsync(ExploreDbContext context)
    {
        Tenant tenant = await context.Set<Tenant>().SingleAsync(item => item.Id == TenantId);
        Tenant foreignTenant = await context.Set<Tenant>().SingleAsync(item => item.Id == ForeignTenantId);
        ApprovalStatus approval = await context.ApprovalStatuses.FirstAsync();
        var organization = NewOrganization(OrganizationId, "Synthetic organization");
        var foreignOrganization = NewOrganization(ForeignOrganizationId, "Synthetic foreign organization");
        organization.TenantParticipations.Add(NewParticipation(Id(30), tenant, organization, approval));
        foreignOrganization.TenantParticipations.Add(NewParticipation(Id(31), foreignTenant, foreignOrganization, approval));
        context.AddRange(organization, foreignOrganization);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static async Task AssertPostgreSqlChecksAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        const string table = "islamu_event.locations";
        await AssertGovernanceChecksAsync(connection, table);
    }

    private static async Task AssertSqliteChecksAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False;Foreign Keys=True");
        await connection.OpenAsync();
        const string table = "ie_locations";
        await AssertGovernanceChecksAsync(connection, table);
    }

    private static async Task AssertGovernanceChecksAsync(DbConnection connection, string table)
    {
        await ExecuteAsync(connection, $"UPDATE {table} SET address_visibility_id=2, created_by=@actor, address_organization_id=NULL WHERE id=@id", false);
        await ExecuteAsync(connection, $"UPDATE {table} SET address_visibility_id=3, created_by=@actor, address_organization_id=@organization WHERE id=@id", false, OrganizationId);
        await ExecuteAsync(connection, $"UPDATE {table} SET address_visibility_id=3, created_by=NULL, address_organization_id=@organization WHERE id=@id", true, OrganizationId);
        await ExecuteAsync(connection, $"UPDATE {table} SET address_visibility_id=4, created_by=@actor, address_organization_id=@organization WHERE id=@id", false, OrganizationId);
        await ExecuteAsync(connection, $"UPDATE {table} SET display_sort_key_version=0 WHERE id=@id", true);
        await ExecuteAsync(connection, $"UPDATE {table} SET display_sort_key_version=1 WHERE id=@id", true);
        await ExecuteAsync(connection, $"UPDATE {table} SET address_visibility_id=4, created_by=@actor, address_organization_id=NULL WHERE id=@id", false);
        await ExecuteAsync(connection, $"UPDATE {table} SET address_visibility_id=2, created_by=@actor, address_organization_id=@organization WHERE id=@id", true, OrganizationId);
        await ExecuteAsync(connection, $"UPDATE {table} SET address_visibility_id=3, created_by=@actor, address_organization_id=NULL WHERE id=@id", true);
        await ExecuteAsync(connection, $"UPDATE {table} SET address_visibility_id=3, created_by=@actor, address_organization_id=@organization WHERE id=@id", true, ForeignOrganizationId);
        await ExecuteAsync(connection, $"UPDATE {table} SET location_kind_id=5, address_visibility_id=4, created_by=@actor, address_organization_id=NULL WHERE id=@id", true);
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        string sql,
        bool expectRejection,
        Guid? organizationId = null)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "@id", LocationId);
        AddParameter(command, "@actor", ActorId);
        AddParameter(command, "@organization", organizationId ?? OrganizationId);
        Exception? failure = null;
        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (DbException exception)
        {
            failure = exception;
        }
        await Assert.That(failure is not null).IsEqualTo(expectRejection);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static Organization NewOrganization(Guid id, string name) => new()
    {
        Id = id,
        Pii = new OrganizationPii { FullName = name },
        CreatedAt = DateTime.UnixEpoch,
        ConcurrencyStamp = Id(40 + id.ToByteArray()[15])
    };

    private static OrganizationTenant NewParticipation(
        Guid id,
        Tenant tenant,
        Organization organization,
        ApprovalStatus approval) => new()
    {
        Id = id,
        TenantId = tenant.Id,
        Tenant = tenant,
        OrganizationId = organization.Id,
        Organization = organization,
        ApprovalStatusId = approval.Id,
        ApprovalStatus = approval,
        CreatedAt = DateTime.UnixEpoch,
        ConcurrencyStamp = Id(50 + id.ToByteArray()[15])
    };

    private async Task CreatePostgreSqlDatabaseAsync(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task DropPostgreSqlDatabaseAsync(string databaseName)
    {
        NpgsqlConnection.ClearAllPools();
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using (var terminate = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname=@database AND pid<>pg_backend_pid()",
            connection))
        {
            terminate.Parameters.AddWithValue("database", databaseName);
            await terminate.ExecuteNonQueryAsync();
        }
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", connection);
        await drop.ExecuteNonQueryAsync();
    }

    private PrimaryDatabaseConnectionOptions PostgreSqlOptions(string databaseName)
    {
        var source = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        return new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = source.Host,
            Port = source.Port,
            Database = databaseName,
            Schema = PrimaryDatabaseConnectionOptions.DefaultSchema,
            Username = source.Username,
            Password = source.Password,
            TlsMode = source.SslMode switch
            {
                SslMode.Disable => PrimaryDatabaseTlsMode.Disabled,
                SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull => PrimaryDatabaseTlsMode.Required,
                _ => PrimaryDatabaseTlsMode.Prefer,
            },
            TrustServerCertificate = source.SslMode == SslMode.Require,
        };
    }

    private static PrimaryDatabaseConnectionOptions SqliteOptions(string path) => new()
    {
        Role = PrimaryDatabaseRole.Migrator,
        Provider = PrimaryDatabaseProvider.Sqlite,
        Database = path,
    };

    private static ExploreDbContext CreateApplicationContext(
        PrimaryDatabaseConnectionOptions database,
        IModel? model = null)
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        builder.EnableServiceProviderCaching(false);
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, database);
        builder.ConfigureWarnings(warnings =>
            warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning));
        if (model is not null)
        {
            builder.UseModel(model);
        }
        return new ExploreDbContext(builder.Options);
    }

    private static ExploreDbContext CreateModelContext(string provider)
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        builder.EnableServiceProviderCaching(false);
        switch (provider)
        {
            case "PostgreSql":
                builder.UseNpgsql("Host=localhost;Database=model;Username=model", options => options.MigrationsAssembly("Explore.Persistence"));
                break;
            case "Sqlite":
                builder.UseSqlite("Data Source=:memory:", options => options.MigrationsAssembly("Explore.Persistence.Migrations.Sqlite"));
                break;
            case "SqlServer":
                builder.UseSqlServer("Server=localhost;Database=model;Integrated Security=True;TrustServerCertificate=True", options => options.MigrationsAssembly("Explore.Persistence.Migrations.SqlServer"));
                break;
            case "MariaDb":
                builder.UseMySql("Server=localhost;Database=model;User=model", new MariaDbServerVersion(new Version(11, 4, 12)), options => options.MigrationsAssembly("Explore.Persistence.Migrations.MySql"));
                break;
            case "MySql":
                builder.UseMySql("Server=localhost;Database=model;User=model", new MySqlServerVersion(new Version(8, 4, 6)), options => options.MigrationsAssembly("Explore.Persistence.Migrations.MySql"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }
        builder.UseSnakeCaseNamingConvention();
        builder.ConfigureWarnings(warnings =>
            warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning));
        return new ExploreDbContext(builder.Options);
    }

    private static void DeleteSqlite(string path)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        File.Delete(path + "-shm");
        File.Delete(path + "-wal");
    }

    private static Guid Id(int suffix) => Guid.Parse($"019b0000-0002-7000-8000-{suffix:000000000000}");

}
