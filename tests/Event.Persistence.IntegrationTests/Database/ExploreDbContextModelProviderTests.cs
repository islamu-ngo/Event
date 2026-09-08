// ABOUTME: Verifies the shared Explore EF Core model builds for every supported primary provider.
// ABOUTME: Asserts configurable schema or fixed-prefix naming and PostgreSQL-only model defenses.

using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Models;
using Explore.Persistence.Schema;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class ExploreDbContextModelProviderTests
{
    private static readonly Regex LiteralTableMappingPattern = new(
        @"ToTable\(\s*""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task UuidV7DefaultsUseClientGenerationOnEveryProvider(string provider)
    {
        using var context = CreateContext(provider);
        var property = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(Explore.Domain.EventRegistration))!
            .FindProperty(nameof(Explore.Domain.EventRegistration.Id))!;

        await Assert.That(property.GetDefaultValueSql()).IsNull();
        var generator = property.GetValueGeneratorFactory()!(property, property.DeclaringType);
        await Assert.That(generator).IsTypeOf<GuidVersion7ValueGenerator>();
        await Assert.That(((GuidVersion7ValueGenerator)generator).Next(null!).Version).IsEqualTo(7);
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task LocationDerivedKeysHaveExactPortableOrdinalModelContract(string provider)
    {
        using var context = CreateContext(provider);
        IModel model = context.GetService<IDesignTimeModel>().Model;
        IProperty displayKey = model.FindEntityType(typeof(Explore.Domain.Location))!
            .FindProperty(nameof(Explore.Domain.Location.DisplaySortKey))!;
        IProperty displayVersion = model.FindEntityType(typeof(Explore.Domain.Location))!
            .FindProperty(nameof(Explore.Domain.Location.DisplaySortKeyVersion))!;
        IProperty addressKey = model.FindEntityType(typeof(Explore.Domain.LocationPii))!
            .FindProperty(nameof(Explore.Domain.LocationPii.AddressSubstringKey))!;
        IProperty addressVersion = model.FindEntityType(typeof(Explore.Domain.LocationPii))!
            .FindProperty(nameof(Explore.Domain.LocationPii.AddressSubstringKeyVersion))!;

        await Assert.That(displayKey.GetMaxLength()).IsEqualTo(14_000);
        await Assert.That(addressKey.GetMaxLength()).IsEqualTo(14_000);
        await Assert.That(displayKey.GetDefaultValue()).IsEqualTo(string.Empty);
        await Assert.That(addressKey.GetDefaultValue()).IsEqualTo(string.Empty);
        await Assert.That(displayVersion.GetDefaultValue()).IsEqualTo((short)0);
        await Assert.That(addressVersion.GetDefaultValue()).IsEqualTo((short)0);

        string expectedCollation = provider switch
        {
            "PostgreSql" => "C",
            "Sqlite" => "BINARY",
            "SqlServer" => "Latin1_General_100_BIN2",
            "MariaDb" or "MySql" => "ascii_bin",
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        await Assert.That(displayKey.GetCollation()).IsEqualTo(expectedCollation);
        await Assert.That(addressKey.GetCollation()).IsEqualTo(expectedCollation);
        await Assert.That(displayKey.GetCharSet()).IsEqualTo(provider is "MariaDb" or "MySql" ? "ascii" : null);
        await Assert.That(addressKey.GetCharSet()).IsEqualTo(provider is "MariaDb" or "MySql" ? "ascii" : null);

        string checks = string.Join(' ', model.GetEntityTypes()
            .Where(type => type.ClrType == typeof(Explore.Domain.Location) || type.ClrType == typeof(Explore.Domain.LocationPii))
            .SelectMany(type => type.GetCheckConstraints())
            .Select(check => check.Sql));
        await Assert.That(checks).Contains("display_sort_key_version");
        await Assert.That(checks).Contains("address_substring_key_version");
        await Assert.That(checks).Contains("address_visibility_id");
    }

    [Test]
    public async Task PostgreSqlConstraintApplierSkipsOtherProviders()
    {
        await using var context = CreateContext("Sqlite");

        await PostgresModelConstraintApplier.ApplyAsync(context);
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task ModelBuildsWithFixedNamespaceAndProviderCapabilities(string provider)
    {
        using var context = CreateContext(provider);

        var model = context.Model;
        await Assert.That(model.GetRelationalModel().Tables).IsNotEmpty();

        if (provider is "PostgreSql" or "SqlServer")
        {
            await Assert.That(model.GetDefaultSchema()).IsEqualTo("islamu_event");
        }
        else
        {
            await Assert.That(model.GetEntityTypes()
                .Where(entityType => entityType.GetTableName() is not null)
                .All(entityType => entityType.GetTableName()!.StartsWith("ie_", StringComparison.Ordinal))).IsTrue();
        }

        var annotations = model.GetEntityTypes().SelectMany(entityType => entityType.GetAnnotations()).ToArray();
        await Assert.That(annotations.Any(annotation => annotation.Name.StartsWith("Explore:PostgresExclusionConstraint:", StringComparison.Ordinal)))
            .IsEqualTo(provider == "PostgreSql");
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task FinalizedRelationalNamingMatrixUsesProviderNamespaceAndCanonicalNames(
        string provider)
    {
        const string configuredSchema = "operator_matrix";
        using var context = CreateContext(provider, configuredSchema);
        var model = context.GetService<IDesignTimeModel>().Model;
        bool usesSchema = provider is "PostgreSql" or "SqlServer";
        var violations = new List<string>();

        foreach (var table in model.GetRelationalModel().Tables)
        {
            string logicalTableName = usesSchema
                ? table.Name
                : table.Name.StartsWith("ie_", StringComparison.Ordinal)
                    ? table.Name["ie_".Length..]
                    : table.Name;
            if (!IsCanonicalSnakeCase(logicalTableName))
            {
                violations.Add($"{provider}:{table.Name}: non-canonical table name");
            }
            if (usesSchema && !string.Equals(table.Schema, configuredSchema, StringComparison.Ordinal))
            {
                violations.Add($"{provider}:{table.Name}: expected schema {configuredSchema}");
            }
            if (!usesSchema &&
                (!string.IsNullOrEmpty(table.Schema) ||
                 !table.Name.StartsWith("ie_", StringComparison.Ordinal)))
            {
                violations.Add($"{provider}:{table.Name}: expected schema-free ie_ namespace");
            }

            foreach (var column in table.Columns)
            {
                if (!IsCanonicalSnakeCase(column.Name))
                {
                    violations.Add($"{provider}:{table.Name}.{column.Name}: non-canonical column name");
                }
            }
            foreach (var constraint in table.UniqueConstraints)
            {
                string prefix = ReferenceEquals(table.PrimaryKey, constraint) ? "pk_" : "ak_";
                if (!constraint.Name.StartsWith(prefix, StringComparison.Ordinal) ||
                    !IsCanonicalSnakeCase(constraint.Name))
                {
                    violations.Add($"{provider}:{table.Name}.{constraint.Name}: non-canonical key name");
                }
            }
            foreach (var index in table.Indexes)
            {
                if (!index.Name.StartsWith("ix_", StringComparison.Ordinal) ||
                    !IsCanonicalSnakeCase(index.Name))
                {
                    violations.Add($"{provider}:{table.Name}.{index.Name}: non-canonical index name");
                }
            }
            foreach (var foreignKey in table.ForeignKeyConstraints)
            {
                if (!foreignKey.Name.StartsWith("fk_", StringComparison.Ordinal) ||
                    !IsCanonicalSnakeCase(foreignKey.Name))
                {
                    violations.Add($"{provider}:{table.Name}.{foreignKey.Name}: non-canonical foreign key name");
                }
            }
            foreach (var check in table.CheckConstraints)
            {
                if (!check.Name.StartsWith("ck_", StringComparison.Ordinal) ||
                    !IsCanonicalSnakeCase(check.Name))
                {
                    violations.Add($"{provider}:{table.Name}.{check.Name}: non-canonical check name");
                }
            }
        }

        await Assert.That(violations).IsEmpty()
            .Because("the finalized relational model must be schema-configurable and canonically named");
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task ThemeAndPolicyOwnedValuesKeepSemanticFlattenedColumnPrefixes(string provider)
    {
        using var context = CreateContext(provider);
        IModel model = context.GetService<IDesignTimeModel>().Model;
        var violations = new List<string>();

        AddPaletteColumnViolations(
            model,
            typeof(Explore.Domain.UiTheme),
            nameof(Explore.Domain.UiTheme.LightPalette),
            "light",
            violations);
        AddPaletteColumnViolations(
            model,
            typeof(Explore.Domain.UiTheme),
            nameof(Explore.Domain.UiTheme.DarkPalette),
            "dark",
            violations);
        AddPaletteColumnViolations(
            model,
            typeof(Explore.Domain.UiThemePreset),
            nameof(Explore.Domain.UiThemePreset.LightPalette),
            "light",
            violations);
        AddPaletteColumnViolations(
            model,
            typeof(Explore.Domain.UiThemePreset),
            nameof(Explore.Domain.UiThemePreset.DarkPalette),
            "dark",
            violations);
        AddPaletteColumnViolations(
            model,
            typeof(Explore.Domain.UserAppearanceProfile),
            nameof(Explore.Domain.UserAppearanceProfile.LightPaletteSnapshot),
            "light_snapshot",
            violations);
        AddPaletteColumnViolations(
            model,
            typeof(Explore.Domain.UserAppearanceProfile),
            nameof(Explore.Domain.UserAppearanceProfile.DarkPaletteSnapshot),
            "dark_snapshot",
            violations);

        AddRenderPolicySlotColumnViolations(
            model,
            typeof(Explore.Domain.Policies.InstancePolicySet),
            violations);
        AddRenderPolicySlotColumnViolations(
            model,
            typeof(Explore.Domain.Policies.TenantPolicySet),
            violations);

        await Assert.That(violations).IsEmpty()
            .Because("owned theme and policy values need stable semantic prefixes across providers");
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task ConstraintDescriptorsResolveFromEachFinalizedProviderModel(string provider)
    {
        using var context = CreateContext(provider);
        RelationalConstraintDescriptor[] descriptors =
        [
            RelationalConstraintDescriptorResolver.PrimaryKey<Explore.Domain.NotificationIntent>(context),
            RelationalConstraintDescriptorResolver.UniqueIndex<Explore.Domain.IncomingWebhookEffectReceipt>(
                context,
                nameof(Explore.Domain.IncomingWebhookEffectReceipt.TenantId),
                nameof(Explore.Domain.IncomingWebhookEffectReceipt.IncomingWebhookMessageId),
                nameof(Explore.Domain.IncomingWebhookEffectReceipt.EffectKind)),
            RelationalConstraintDescriptorResolver.UniqueIndex<Explore.Domain.WebPushDispatchOutbox>(
                context,
                nameof(Explore.Domain.WebPushDispatchOutbox.TenantId),
                nameof(Explore.Domain.WebPushDispatchOutbox.NotificationId),
                nameof(Explore.Domain.WebPushDispatchOutbox.SubscriptionId))
        ];

        int maximumIdentifierLength = provider switch
        {
            "PostgreSql" => 63,
            "MariaDb" or "MySql" => 64,
            "SqlServer" => 128,
            _ => int.MaxValue
        };
        await Assert.That(descriptors.Select(descriptor => descriptor.Name).Distinct()).Count()
            .IsEqualTo(descriptors.Length);
        await Assert.That(descriptors.All(descriptor =>
            descriptor.Name.Length <= maximumIdentifierLength &&
            IsCanonicalSnakeCase(descriptor.Name))).IsTrue();
        await Assert.That(descriptors.SelectMany(descriptor => descriptor.QualifiedColumns).All(column =>
            !string.IsNullOrWhiteSpace(column) &&
            (provider is "PostgreSql" or "SqlServer" ||
             column.StartsWith("ie_", StringComparison.Ordinal)))).IsTrue();

        if (provider == "PostgreSql")
        {
            await Assert.That(
                RelationalConstraintDescriptorResolver.ExclusionConstraint<Explore.Domain.EventSession>(context))
                .IsNotEmpty();
        }
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task PrivateTablesHaveCanonicalDbSetOwners(PrimaryDatabaseProvider provider)
    {
        var violations = new List<string>();
        foreach (bool external in new[] { false, true })
        foreach (string? schema in new string?[] { null, "operator_private" })
        {
            using DbContext context = CreatePrivateTableContext(provider, external, schema);
            IModel model = context.GetService<IDesignTimeModel>().Model;
            var sets = context.GetService<IDbSetFinder>().FindSets(context.GetType());
            (Type Type, string Set, string Table)[] expected = external
                ? [(typeof(LocalIdentityLifecycleOperation), "LocalIdentityLifecycleOperations", "local_identity_lifecycle_operations")]
                : [
                    (typeof(AnonymousChallengeTenantQuota), "AnonymousChallengeTenantQuotas", "anonymous_challenge_tenant_quotas"),
                    (typeof(AnonymousChallengeEventQuota), "AnonymousChallengeEventQuotas", "anonymous_challenge_event_quotas"),
                    (typeof(LocalIdentityLifecycleOperation), "LocalIdentityLifecycleOperations", "local_identity_lifecycle_operations")
                ];
            bool usesSchema = provider is PrimaryDatabaseProvider.PostgreSql or PrimaryDatabaseProvider.SqlServer;
            foreach (var mapping in expected)
            {
                if (sets.Count(set => set.Type == mapping.Type && set.Name == mapping.Set) != 1)
                    violations.Add($"{provider}/{context.GetType().Name}: missing canonical DbSet {mapping.Set}");
                IEntityType entity = model.FindEntityType(mapping.Type)!;
                await Assert.That(entity.GetTableName()).IsEqualTo(usesSchema ? mapping.Table : "ie_" + mapping.Table);
                await Assert.That(entity.GetSchema()).IsEqualTo(usesSchema
                    ? schema ?? (external ? "islamu_identity" : "islamu_event") : null);
            }
            if (external)
            {
                await Assert.That(model.FindEntityType(typeof(AnonymousChallengeTenantQuota))).IsNull();
                await Assert.That(model.FindEntityType(typeof(AnonymousChallengeEventQuota))).IsNull();
            }
        }
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task PrivateTableModelsMatchExistingSnapshots(PrimaryDatabaseProvider provider)
    {
        foreach (bool external in new[] { false, true })
        {
            using DbContext context = CreatePrivateTableContext(provider, external);
            string ddl = context.Database.GenerateCreateScript();
            Console.WriteLine($"Schema fingerprint {provider}/{context.GetType().Name}: " +
                Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ddl))));
            await Assert.That(context.Database.HasPendingModelChanges()).IsFalse();
        }
    }

    private static DbContext CreatePrivateTableContext(
        PrimaryDatabaseProvider provider, bool external, string? schema = null)
    {
        var database = new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime,
            Provider = provider,
            Host = provider == PrimaryDatabaseProvider.Sqlite ? null : "localhost",
            Database = provider == PrimaryDatabaseProvider.Sqlite
                ? Path.Combine(Path.GetTempPath(), $"private-table-model-{Guid.NewGuid():N}.db") : "model",
            Schema = schema ?? (external ? "islamu_identity" : "islamu_event"),
            Username = provider == PrimaryDatabaseProvider.Sqlite ? null : "model",
            Password = provider == PrimaryDatabaseProvider.Sqlite ? null : TestPassword(),
            ServerFlavor = provider switch
            {
                PrimaryDatabaseProvider.MariaDb => PrimaryDatabaseServerFlavor.MariaDb,
                PrimaryDatabaseProvider.MySql => PrimaryDatabaseServerFlavor.MySql,
                _ => null
            },
            ServerVersion = provider switch
            {
                PrimaryDatabaseProvider.MariaDb => new Version(11, 4),
                PrimaryDatabaseProvider.MySql => new Version(8, 4),
                _ => null
            }
        };
        if (!external)
        {
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, database);
            return new ExploreDbContext(options.Options);
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IdentityDatabase:Topology"] = "external",
            ["IdentityDatabase:Provider"] = provider.ToString(),
            ["IdentityDatabase:ConnectionString"] = PrimaryDatabaseConfiguration.BuildConnectionString(database).ConnectionString,
            ["IdentityDatabase:Schema"] = database.Schema,
            ["IdentityDatabase:ServerVersion"] = database.ServerVersion?.ToString()
        }).Build();
        var identityOptions = new DbContextOptionsBuilder<ExternalIdentityDbContext>();
        IdentityDatabaseProviderComposition.Configure(identityOptions, configuration, PrimaryDatabaseRole.Runtime);
        return new ExternalIdentityDbContext(identityOptions.Options);
    }

    [Test]
    public async Task FinalizedMappingSourceContainsNoLiteralTableNames()
    {
        string persistenceRoot = Path.Combine(GetRepositoryRoot(), "src", "Explore.Persistence");
        var violations = new List<string>();
        foreach (string path in Directory.GetFiles(persistenceRoot, "*.cs", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(GetRepositoryRoot(), path)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (relativePath.Contains("/Migrations/", StringComparison.Ordinal) ||
                relativePath.Contains("/bin/", StringComparison.Ordinal) ||
                relativePath.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            string source = await File.ReadAllTextAsync(path);
            violations.AddRange(LiteralTableMappingPattern.Matches(source).Select(match =>
                $"{relativePath}:{source[..match.Index].Count(character => character == '\n') + 1}"));
        }

        await Assert.That(violations).IsEmpty()
            .Because("snake-case conventions and the provider namespace own every table name");
    }

    [Test]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task MySqlModelsHaveDistinctBoundedCustomPropertyOptionForeignKeys(string provider)
    {
        using var context = CreateContext(provider);

        var relationalModel = context.Model.GetRelationalModel();
        var tables = relationalModel.Tables.ToArray();
        await Assert.That(tables.SelectMany(table => table.UniqueConstraints).Select(constraint => constraint.Name).All(name => name.Length <= 64)).IsTrue();
        await Assert.That(tables.SelectMany(table => table.Indexes).Select(index => index.Name).All(name => name.Length <= 64)).IsTrue();
        await Assert.That(tables.SelectMany(table => table.ForeignKeyConstraints).Select(constraint => constraint.Name).All(name => name.Length <= 64)).IsTrue();

        var optionTable = tables.Single(table => table.Name == "ie_custom_property_options");
        var optionForeignKeys = optionTable.ForeignKeyConstraints
            .Where(constraint => constraint.PrincipalTable.Name is
                "ie_custom_property_options" or "ie_custom_property_definitions")
            .Select(constraint => constraint.Name)
            .ToArray();

        await Assert.That(optionForeignKeys).Count().IsEqualTo(2);
        await Assert.That(optionForeignKeys.Distinct()).Count().IsEqualTo(2);
    }

    [Test]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task MySqlExternalBindingHashKeysPreserveUnicodeValueCapacity(string provider)
    {
        using var context = CreateContext(provider);
        var entityType = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(Explore.Domain.ExternalBinding))!;

        var externalId = entityType.FindProperty(nameof(Explore.Domain.ExternalBinding.ExternalId))!;
        await Assert.That(externalId.GetMaxLength()).IsEqualTo(512);
        await Assert.That(externalId.GetCollation()).IsNull();
        await Assert.That(externalId.GetCharSet()).IsNotEqualTo("ascii");
        await Assert.That(entityType.FindProperty("ExternalGlobalUniquenessHash")!.GetColumnType()).IsEqualTo("binary(32)");
    }

    [Test]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task MySqlExternalBindingHashKeysSeparateGlobalAndTenantScopes(string provider)
    {
        using var context = CreateContext(provider);
        var entityType = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(Explore.Domain.ExternalBinding))!;

        await Assert.That(entityType.FindProperty("ExternalGlobalUniquenessHash")!.IsNullable).IsTrue();
        await Assert.That(entityType.FindProperty("ExternalTenantUniquenessHash")!.IsNullable).IsTrue();
        await Assert.That(entityType.FindProperty("InternalGlobalUniquenessHash")!.IsNullable).IsTrue();
        await Assert.That(entityType.FindProperty("InternalTenantUniquenessHash")!.IsNullable).IsTrue();

        await Assert.That(ExploreDbContext.ComputeMySqlUniquenessHash("provider", "system", "type", "identity"))
            .IsNotEqualTo(ExploreDbContext.ComputeMySqlUniquenessHash(
                "provider", "system", "type", "identity", Guid.Empty.ToString("D")));
    }

    [Test]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task MySqlExternalBindingDuplicateProtectionUsesFourUniqueHashIndexes(string provider)
    {
        using var context = CreateContext(provider);
        var indexes = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(Explore.Domain.ExternalBinding))!
            .GetIndexes()
            .ToArray();

        var hashIndexes = indexes.Where(index => index.GetDatabaseName()?.EndsWith("_hash", StringComparison.Ordinal) == true).ToArray();
        await Assert.That(hashIndexes).Count().IsEqualTo(4);
        await Assert.That(hashIndexes.All(index => index.IsUnique)).IsTrue();
        await Assert.That(indexes.Select(index => index.GetDatabaseName()).Intersect([
                "ix_external_bindings_external_global_unique",
                "ix_external_bindings_external_tenant_unique",
                "ix_external_bindings_internal_global_unique",
                "ix_external_bindings_internal_tenant_unique"
            ])).IsEmpty();
    }

    [Test]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task MySqlExternalBindingHashInputUsesLengthPrefixedUtf8Components(string provider)
    {
        using var context = CreateContext(provider);
        _ = context.Model;
        await Assert.That(ExploreDbContext.ComputeMySqlUniquenessHash("ab", "c"))
            .IsNotEqualTo(ExploreDbContext.ComputeMySqlUniquenessHash("a", "bc"));
        await Assert.That(ExploreDbContext.ComputeMySqlUniquenessHash("مزوّد", "外部識別子")).Count().IsEqualTo(32);
    }

    [Test]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task MySqlIndexesFitInnoDbKeyLimit(string provider)
    {
        using var context = CreateContext(provider);
        var oversized = context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetIndexes())
            .Select(index => new { Index = index, Width = EstimateMySqlIndexWidth(index) })
            .Where(candidate => candidate.Width > 3072)
            .Select(candidate => $"{candidate.Index.DeclaringEntityType.GetTableName()}.{candidate.Index.GetDatabaseName()}={candidate.Width}")
            .ToArray();

        await Assert.That(oversized).IsEmpty();
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task ProviderModelNormalizesPostgreSqlOnlyRelationalAnnotations(string provider)
    {
        using var context = CreateContext(provider);

        var model = context.GetService<IDesignTimeModel>().Model;
        var checkConstraints = model.GetEntityTypes().SelectMany(entityType => entityType.GetCheckConstraints()).ToArray();
        var leaseShape = checkConstraints.Single(constraint =>
            constraint.Name == "ck_atproto_jetstream_lease_shape");

        if (provider == "PostgreSql")
        {
            await Assert.That(leaseShape.Sql).Contains("btrim(lease_owner) <> ''");
            return;
        }

        await Assert.That(leaseShape.Sql).Contains("trim(lease_owner) <> ''");
        await Assert.That(checkConstraints.Any(constraint =>
            constraint.Name == "ck_notifications_entity_reference_shape")).IsFalse();
        await Assert.That(checkConstraints.Any(constraint =>
            new[] { "btrim(", "::", "jsonb_", "num_nonnulls(", "octet_length(", "extract(", "~" }
                .Any(token => constraint.Sql.Contains(token, StringComparison.OrdinalIgnoreCase)))).IsFalse();

        var properties = model.GetEntityTypes().SelectMany(entityType => entityType.GetProperties()).ToArray();
        if (provider is "MariaDb" or "MySql")
        {
            await Assert.That(properties.Where(property => !IsMySqlAsciiIdentityProperty(property)
                    && !IsLocationDerivedKey(property))
                .All(property => property.GetCollation() == null)).IsTrue();
            await Assert.That(model.FindEntityType(typeof(Explore.Domain.AtprotoIdentity))!
                .FindProperty(nameof(Explore.Domain.AtprotoIdentity.Did))!
                .GetCollation()).IsEqualTo("ascii_bin");
        }
        else
        {
            await Assert.That(properties.Where(property => !IsLocationDerivedKey(property))
                .All(property => property.GetCollation() == null)).IsTrue();
        }
        await Assert.That(properties.Any(property =>
        {
            var columnType = property.GetColumnType();
            return
            columnType is not null && new[] { "bytea", "jsonb", "time without time zone", "timestamp with time zone", "uuid" }
                .Contains(columnType, StringComparer.OrdinalIgnoreCase);
        })).IsFalse();
        await Assert.That(properties.Any(property =>
        {
            var defaultSql = property.GetDefaultValueSql();
            return
            defaultSql is not null && new[] { "uuidv7()", "NOW()", "statement_timestamp()" }
                .Contains(defaultSql, StringComparer.OrdinalIgnoreCase);
        })).IsFalse();
        await Assert.That(properties.Any(property => property.GetComputedColumnSql()?.Contains(
            "::uuid", StringComparison.OrdinalIgnoreCase) == true)).IsFalse();

        var sqlAnnotations = properties.SelectMany(property => new[]
            {
                property.GetColumnType(),
                property.GetDefaultValueSql(),
                property.GetComputedColumnSql()
            })
            .Concat(checkConstraints.Select(constraint => constraint.Sql))
            .Concat(model.GetEntityTypes().SelectMany(entityType => entityType.GetIndexes())
                .Select(index => index.GetFilter()))
            .Where(sql => sql is not null)
            .Cast<string>()
            .ToArray();
        var postgreSqlTokens = new[]
        {
            "uuidv7", "jsonb", "btrim", "::", "statement_timestamp", "INTERVAL", "infinity",
            "~", "num_nonnulls", "octet_length", "extract("
        };
        await Assert.That(sqlAnnotations.Any(sql => postgreSqlTokens.Any(token =>
            sql.Contains(token, StringComparison.OrdinalIgnoreCase)))).IsFalse();

        if (provider == "SqlServer")
        {
            await Assert.That(model.GetEntityTypes().SelectMany(entityType => entityType.GetIndexes()).Any(index =>
                index.GetFilter() is { } filter &&
                    (filter.Contains("true", StringComparison.OrdinalIgnoreCase) ||
                     filter.Contains("false", StringComparison.OrdinalIgnoreCase)))).IsFalse();
        }
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task PromotionModelUsesPortableOneActiveReservationShape(string provider)
    {
        using var context = CreateContext(provider);
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType reservation = model.FindEntityType(typeof(Explore.Domain.PromotionReservation))!;
        await Assert.That(reservation).IsNotNull();
        await Assert.That(reservation.FindDeclaredQueryFilter(Explore.Persistence.QueryFilters.QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(reservation.FindIndex([
            reservation.FindProperty(nameof(Explore.Domain.PromotionReservation.RegistrationOrderId))!,
            reservation.FindProperty(nameof(Explore.Domain.PromotionReservation.OrderReservationSlot))!
        ])?.IsUnique).IsTrue();
        await Assert.That(reservation.GetCheckConstraints().Any(constraint => constraint.Name == "ck_promotion_reservation_active_slot")).IsTrue();
        await Assert.That(reservation.GetCheckConstraints().Any(constraint => constraint.Name == "ck_promotion_reservation_status_timestamps")).IsTrue();

        IEntityType code = model.FindEntityType(typeof(Explore.Domain.PromotionCode))!;
        await Assert.That(code.FindProperty("LookupDigest")!.GetMaxLength()).IsEqualTo(128);
        await Assert.That(code.GetIndexes().Any(index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([
            "TenantId", "ScopeEventId", "ScopeTicketCatalogVersionId", "LookupKeyVersion", "LookupDigest"
        ]))).IsTrue();
        await Assert.That(code.GetIndexes().Where(index => index.Properties.Any(property => property.Name == "IsActive")).All(index => index.GetFilter() is null)).IsTrue();
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task PaymentAttemptModelUsesPortableActiveSlotAndDispatchFenceShape(string provider)
    {
        using var context = CreateContext(provider);
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType attempt = model.FindEntityType(typeof(Explore.Domain.PaymentAttempt))!;
        await Assert.That(attempt).IsNotNull();
        await Assert.That(attempt.FindDeclaredQueryFilter(Explore.Persistence.QueryFilters.QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(attempt.FindIndex([
            attempt.FindProperty(nameof(Explore.Domain.PaymentAttempt.ActiveScopeKey))!,
            attempt.FindProperty(nameof(Explore.Domain.PaymentAttempt.ActiveUniquenessSlot))!
        ])?.IsUnique).IsTrue();
        await Assert.That(attempt.FindIndex([attempt.FindProperty(nameof(Explore.Domain.PaymentAttempt.ProviderIdempotencyKey))!])?.IsUnique).IsTrue();
        string shadowStatusPropertyName = nameof(Explore.Domain.PaymentAttempt.PaymentAttemptStatusId) + "1";
        string shadowStatusColumnName = "payment_attempt_status_id" + "1";
        await Assert.That(attempt.FindProperty(shadowStatusPropertyName)).IsNull();
        await Assert.That(attempt.GetProperties().Any(property => property.GetColumnName().Equals(shadowStatusColumnName, StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(attempt.GetCheckConstraints().Any(constraint => constraint.Name == "ck_payment_attempts_active_slot")).IsTrue();
        await Assert.That(attempt.GetCheckConstraints().Any(constraint => constraint.Name == "ck_payment_attempts_amounts")).IsTrue();

        IEntityType effect = model.FindEntityType(typeof(Explore.Domain.CheckoutDispatchEffect))!;
        await Assert.That(effect).IsNotNull();
        await Assert.That(effect.FindDeclaredQueryFilter(Explore.Persistence.QueryFilters.QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(effect.FindIndex([
            effect.FindProperty(nameof(Explore.Domain.CheckoutDispatchEffect.TenantId))!,
            effect.FindProperty(nameof(Explore.Domain.CheckoutDispatchEffect.PaymentAttemptId))!
        ])?.IsUnique).IsTrue();
        await Assert.That(effect.GetCheckConstraints().Any(constraint => constraint.Name == "ck_checkout_dispatch_effects_state")).IsTrue();
        await Assert.That(effect.GetCheckConstraints().Any(constraint => constraint.Name == "ck_checkout_dispatch_effects_processing_fence")).IsTrue();
        await Assert.That(effect.GetIndexes().Any(index =>
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(Explore.Domain.CheckoutDispatchEffect.Status),
                nameof(Explore.Domain.CheckoutDispatchEffect.NextAttemptAt),
                nameof(Explore.Domain.CheckoutDispatchEffect.CreatedAt)
            ]))).IsTrue();
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task PromotionModelMapsHistoricalOrderSnapshotsAndVerifiedEmail(string provider)
    {
        using var context = CreateContext(provider);
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType order = model.FindEntityType(typeof(Explore.Domain.RegistrationOrder))!;
        foreach (string property in new[]
                 {
                     nameof(Explore.Domain.RegistrationOrder.PreDiscountOrganizerDirectedTotalMinorSnapshot),
                     nameof(Explore.Domain.RegistrationOrder.PromotionDiscountTotalMinorSnapshot),
                     nameof(Explore.Domain.RegistrationOrder.PostDiscountOrganizerDirectedTotalMinorSnapshot)
                 })
        {
            await Assert.That(order.FindProperty(property)!.GetColumnType()).IsEqualTo("bigint");
        }

        IEntityType line = model.FindEntityType(typeof(Explore.Domain.RegistrationOrderLine))!;
        foreach (string property in new[]
                 {
                     nameof(Explore.Domain.RegistrationOrderLine.PreDiscountLineSubtotalMinorSnapshot),
                     nameof(Explore.Domain.RegistrationOrderLine.PromotionDiscountAmountMinorSnapshot),
                     nameof(Explore.Domain.RegistrationOrderLine.PostDiscountLineSubtotalMinorSnapshot)
                 })
        {
            await Assert.That(line.FindProperty(property)!.GetColumnType()).IsEqualTo("bigint");
        }

        IEntityType pii = model.FindEntityType(typeof(Explore.Domain.RegistrationOrderPii))!;
        await Assert.That(pii.FindProperty(nameof(Explore.Domain.RegistrationOrderPii.IsEmailVerified))!.GetDefaultValue()).IsEqualTo(false);
    }

    [Test]
    public async Task PostgreSqlRebaselinedInitialContainsCurrentPromotionSnapshots()
    {
        string[] migrationPaths = Directory.GetFiles(
            Path.Combine(GetRepositoryRoot(), "src/Explore.Persistence/Migrations"),
            "*_Init.cs",
            SearchOption.TopDirectoryOnly);
        await Assert.That(migrationPaths).HasSingleItem();
        string source = await File.ReadAllTextAsync(migrationPaths[0]);

        await AssertPromotionSnapshotColumnsAsync(source);
        await Assert.That(source).DoesNotContain("migrationBuilder.Sql");
        await Assert.That(source).DoesNotContain("migrationBuilder.UpdateData");
    }

    [Test]
    [Arguments("src/Explore.Persistence.Migrations.Sqlite/Migrations")]
    [Arguments("src/Explore.Persistence.Migrations.SqlServer/Migrations")]
    [Arguments("src/Explore.Persistence.Migrations.MySql/Migrations")]
    public async Task RebaselinedInitialContainsCurrentPromotionSnapshotsWithoutHistoricalBackfill(
        string migrationDirectory)
    {
        string[] migrationPaths = Directory.GetFiles(
            Path.Combine(GetRepositoryRoot(), migrationDirectory),
            "*_Init.cs",
            SearchOption.TopDirectoryOnly);
        await Assert.That(migrationPaths).HasSingleItem();
        string source = await File.ReadAllTextAsync(migrationPaths[0]);

        await AssertPromotionSnapshotColumnsAsync(source);
        await Assert.That(source).DoesNotContain("migrationBuilder.Sql");
        await Assert.That(source).DoesNotContain("migrationBuilder.UpdateData");
    }

    private static async Task AssertPromotionSnapshotColumnsAsync(string source)
    {
        await Assert.That(source).Contains("pre_discount_organizer_directed_total_minor_snapshot");
        await Assert.That(source).Contains("post_discount_organizer_directed_total_minor_snapshot");
        await Assert.That(source).Contains("organizer_directed_total_minor_snapshot");
        await Assert.That(source).Contains("pre_discount_line_subtotal_minor_snapshot");
        await Assert.That(source).Contains("post_discount_line_subtotal_minor_snapshot");
        await Assert.That(source).Contains("line_subtotal_snapshot");
    }

    [Test]
    [Arguments("PostgreSql")]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task PromotionLookupsAreGlobalStableIntegerTables(string provider)
    {
        using var context = CreateContext(provider);
        IModel model = context.GetService<IDesignTimeModel>().Model;

        foreach (Type lookupType in new[] { typeof(Explore.Domain.PromotionDefinitionStatus), typeof(Explore.Domain.PromotionReservationStatus) })
        {
            IEntityType lookup = model.FindEntityType(lookupType)!;
            await Assert.That(lookup.FindPrimaryKey()!.Properties.Single().ClrType).IsEqualTo(typeof(int));
            await Assert.That(lookup.FindProperty("Id")!.ValueGenerated).IsEqualTo(ValueGenerated.Never);
            await Assert.That(lookup.FindProperty("MasterCode")!.GetMaxLength()).IsEqualTo(100);
            await Assert.That(lookup.GetIndexes().Any(index => index.IsUnique && index.Properties.Single().Name == "MasterCode")).IsTrue();
            await Assert.That(lookup.FindDeclaredQueryFilter(Explore.Persistence.QueryFilters.QueryFilterNames.Tenant)).IsNull();
            await Assert.That(lookup.GetSeedData().Count).IsEqualTo(0);
        }
    }

    private static bool IsLocationDerivedKey(IReadOnlyProperty property) =>
        property.DeclaringType.ClrType == typeof(Explore.Domain.Location)
            && property.Name == nameof(Explore.Domain.Location.DisplaySortKey)
        || property.DeclaringType.ClrType == typeof(Explore.Domain.LocationPii)
            && property.Name == nameof(Explore.Domain.LocationPii.AddressSubstringKey);

    private static bool IsMySqlAsciiIdentityProperty(IReadOnlyProperty property) =>
        (property.DeclaringType.ClrType == typeof(Explore.Domain.AtprotoIdentity) &&
         property.Name == nameof(Explore.Domain.AtprotoIdentity.Did)) ||
        (property.DeclaringType.ClrType == typeof(Explore.Domain.UserAuthenticationToken) &&
         property.Name is nameof(Explore.Domain.UserAuthenticationToken.Provider) or
             nameof(Explore.Domain.UserAuthenticationToken.SubjectDid));

    [Test]
    public async Task SqliteCanCreateTheNormalizedApplicationSchema()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"islamu-event-model-{Guid.NewGuid():N}.db");
        try
        {
            var builder = new DbContextOptionsBuilder<ExploreDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .UseSnakeCaseNamingConvention();
            await using var context = new ExploreDbContext(builder.Options);

            await Assert.That(await context.Database.EnsureCreatedAsync()).IsTrue();
        }
        finally
        {
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    internal static ExploreDbContext CreateContext(string provider, string? modelSchema = null)
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        switch (provider)
        {
            case "PostgreSql":
                builder.UseNpgsql(new NpgsqlConnectionStringBuilder
                {
                    Host = "localhost",
                    Database = "model",
                    Username = "model",
                    Password = TestPassword()
                }.ConnectionString);
                break;
            case "Sqlite":
                builder.UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = ":memory:"
                }.ConnectionString);
                break;
            case "SqlServer":
                builder.UseSqlServer(new SqlConnectionStringBuilder
                {
                    DataSource = "localhost",
                    InitialCatalog = "model",
                    UserID = "model",
                    Password = TestPassword(),
                    TrustServerCertificate = true
                }.ConnectionString);
                break;
            case "MariaDb":
                builder.UseMySql(MySqlConnectionString(), new MariaDbServerVersion(new Version(10, 11)));
                break;
            case "MySql":
                builder.UseMySql(MySqlConnectionString(), new MySqlServerVersion(new Version(8, 0)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
        }

        if (modelSchema is not null)
        {
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                new RelationalNamespaceOptionsExtension(modelSchema, modelSchema));
        }
        builder.UseSnakeCaseNamingConvention();
        return new ExploreDbContext(builder.Options);
    }

    private static string MySqlConnectionString() =>
        new MySqlConnectionStringBuilder
        {
            Server = "localhost",
            Database = "model",
            UserID = "model",
            Password = TestPassword()
        }.ConnectionString;

    private static string TestPassword() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    private static void AddPaletteColumnViolations(
        IModel model,
        Type ownerType,
        string navigationName,
        string prefix,
        ICollection<string> violations)
    {
        IEntityType owner = model.FindEntityType(ownerType)!;
        IEntityType palette = owner.FindNavigation(navigationName)!.TargetEntityType;
        StoreObjectIdentifier table = StoreObjectIdentifier.Create(palette, StoreObjectType.Table)!.Value;

        foreach (var propertyInfo in typeof(Explore.Domain.ValueObjects.UiThemePalette).GetProperties())
        {
            string suffix = Regex.Replace(
                    propertyInfo.Name,
                    "([a-z0-9])([A-Z])",
                    "$1_$2",
                    RegexOptions.CultureInvariant)
                .ToLowerInvariant();
            string expected = $"{prefix}_{suffix}";
            string? actual = palette.FindProperty(propertyInfo.Name)!.GetColumnName(table);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                violations.Add($"{ownerType.Name}.{navigationName}.{propertyInfo.Name}: expected {expected}, got {actual}");
            }
        }
    }

    private static void AddRenderPolicySlotColumnViolations(
        IModel model,
        Type ownerType,
        ICollection<string> violations)
    {
        IEntityType owner = model.FindEntityType(ownerType)!;
        IEntityType renderPolicy = owner.FindNavigation("RenderPolicy")!.TargetEntityType;
        IEntityType slot = renderPolicy.FindNavigation("DisallowInteractiveServerOnOnboarding")!.TargetEntityType;
        StoreObjectIdentifier table = StoreObjectIdentifier.Create(slot, StoreObjectType.Table)!.Value;
        var expectedColumns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LocalValue"] = "render_policy_disallow_interactive_onboarding_local_value",
            ["OverrideMode"] = "render_policy_disallow_interactive_onboarding_override_mode"
        };

        foreach ((string propertyName, string expected) in expectedColumns)
        {
            string? actual = slot.FindProperty(propertyName)!.GetColumnName(table);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                violations.Add($"{ownerType.Name}.RenderPolicy.{propertyName}: expected {expected}, got {actual}");
            }
        }
    }

    private static bool IsCanonicalSnakeCase(string name) =>
        name.Length > 0 &&
        name[0] is >= 'a' and <= 'z' &&
        name.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static int EstimateMySqlIndexWidth(IReadOnlyIndex index)
    {
        var prefixLengths = index.FindAnnotation("MySql:IndexPrefixLength")?.Value as int[];
        return index.Properties.Select((property, position) =>
        {
            if (property.ClrType == typeof(string))
            {
                var configuredLength = property.GetMaxLength() ?? 0;
                var prefixLength = prefixLengths is not null && prefixLengths[position] > 0
                    ? prefixLengths[position]
                    : configuredLength;
                var bytesPerCharacter = property.GetCharSet() == "ascii" ? 1 : 4;
                return prefixLength * bytesPerCharacter;
            }

            if (property.ClrType == typeof(Guid) || property.ClrType == typeof(Guid?))
            {
                return 36;
            }

            if (property.ClrType == typeof(byte[]))
            {
                var columnType = property.GetColumnType();
                return columnType == "binary(32)" ? 32 : 3073;
            }

            return 8;
        }).Sum();
    }
}
