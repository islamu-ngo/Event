// ABOUTME: Normalizes PostgreSQL-oriented relational annotations for the other supported database providers.
// ABOUTME: Preserves PostgreSQL types while emitting portable defaults and constraint SQL for every provider.

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Explore.Persistence.Schema;

internal static partial class PortableRelationalModelPolicy
{
    private const string PostgreSqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string SqliteProvider = "Microsoft.EntityFrameworkCore.Sqlite";
    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";
    private const string MySqlProvider = "Microting.EntityFrameworkCore.MySql";
    private const string InMemoryProvider = "Microsoft.EntityFrameworkCore.InMemory";
    private const int MySqlLookupIndexPrefixLength = 512;

    private static readonly HashSet<string> PostgreSqlColumnTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bytea",
        "jsonb",
        "time without time zone",
        "timestamp with time zone",
        "uuid"
    };

    private static readonly string[] UnsupportedCheckSqlTokens =
    {
        "::",
        "jsonb_",
        "num_nonnulls(",
        "octet_length(",
        "extract(",
        "~"
    };

    public static void Apply(ModelBuilder modelBuilder, string? providerName)
    {
        if (providerName is null || providerName == InMemoryProvider)
        {
            return;
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (providerName == MySqlProvider && entityType.ClrType == typeof(ExternalBinding))
            {
                ConfigureMySqlExternalBindingUniqueness(entityType);
            }
            else if (providerName == MySqlProvider && entityType.ClrType == typeof(StorageObject))
            {
                ConfigureMySqlStorageObjectUniqueness(entityType);
            }
            else if (providerName == MySqlProvider && entityType.ClrType == typeof(UserExternalLogin))
            {
                ConfigureMySqlUserExternalLoginUniqueness(entityType);
            }
            else if (providerName == MySqlProvider && entityType.ClrType == typeof(WebPushSubscription))
            {
                ConfigureMySqlWebPushSubscriptionUniqueness(entityType);
            }
            else if (providerName == MySqlProvider && entityType.ClrType == typeof(WebhookConsumerProviderBinding))
            {
                ConfigureMySqlWebhookProviderBindingUniqueness(entityType);
            }

            NormalizeProperties(entityType, providerName);
            if (providerName == SqlServerProvider)
            {
                NormalizeSqlServerDeleteBehaviors(entityType);
            }
            if (providerName != PostgreSqlProvider)
            {
                NormalizeCheckConstraints(entityType, providerName);
                NormalizeIndexFilters(entityType, providerName);
            }

            NormalizeRelationalObjectNames(entityType, providerName);
        }
    }

    private static void NormalizeRelationalObjectNames(
        IMutableEntityType entityType,
        string providerName)
    {
        string? tableName = entityType.GetTableName();
        if (string.IsNullOrEmpty(tableName))
        {
            return;
        }

        var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
        int maximumIdentifierLength = providerName switch
        {
            PostgreSqlProvider => 63,
            MySqlProvider => 64,
            SqlServerProvider => 128,
            _ => 128
        };

        foreach (IMutableIndex index in entityType.GetIndexes())
        {
            string columns = string.Join(
                '_',
                index.Properties.Select(property => property.GetColumnName(storeObject)));
            string canonicalName = $"ix_{tableName}_{columns}".ToLowerInvariant();
            index.SetDatabaseName(ShortenIdentifier(canonicalName, maximumIdentifierLength));
        }

        foreach (IMutableForeignKey foreignKey in entityType.GetForeignKeys())
        {
            string? principalTableName = foreignKey.PrincipalEntityType.GetTableName();
            if (string.IsNullOrEmpty(principalTableName))
            {
                continue;
            }

            string columns = string.Join(
                '_',
                foreignKey.Properties.Select(property => property.GetColumnName(storeObject)));
            string canonicalName =
                $"fk_{tableName}_{principalTableName}_{columns}".ToLowerInvariant();
            foreignKey.SetConstraintName(
                ShortenIdentifier(canonicalName, maximumIdentifierLength));
        }

        foreach (IMutableCheckConstraint checkConstraint in entityType.GetCheckConstraints())
        {
            string? name = checkConstraint.Name;
            if (!string.IsNullOrEmpty(name))
            {
                checkConstraint.Name = ShortenIdentifier(
                    name.ToLowerInvariant(),
                    maximumIdentifierLength);
            }
        }
    }

    private static string ShortenIdentifier(string name, int maximumLength)
    {
        if (name.Length <= maximumLength && !name.EndsWith('~'))
        {
            return name;
        }

        const int hashLength = 12;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..hashLength]
            .ToLowerInvariant();
        string stem = name.TrimEnd('~');
        int stemLength = Math.Min(stem.Length, maximumLength - hashLength - 1);
        return stem[..stemLength] + "_" + hash;
    }

    private static void NormalizeProperties(IMutableEntityType entityType, string providerName)
    {
        foreach (var property in entityType.GetProperties())
        {
            if (providerName == SqlServerProvider &&
                property.ClrType == typeof(int) &&
                property.IsPrimaryKey())
            {
                property.ValueGenerated = ValueGenerated.Never;
            }

            var configuredCollation = property.GetCollation();
            if (property.FindAnnotation(LocationUnicodePropertyExtensions.AnnotationName)?.Value is true)
            {
                ConfigureLocationUnicode(property, providerName);
            }
            else if (property.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value is true)
            {
                ConfigurePortableOrdinalAscii(property, providerName);
            }
            else if (providerName == MySqlProvider &&
                     string.Equals(configuredCollation, "C", StringComparison.Ordinal))
            {
                property.SetCharSet("ascii");
                property.SetCollation("ascii_bin");
            }
            else if (providerName == MySqlProvider &&
                     entityType.ClrType == typeof(UserAuthenticationToken) &&
                     property.Name is nameof(UserAuthenticationToken.Provider) or
                         nameof(UserAuthenticationToken.SubjectDid))
            {
                property.SetCharSet("ascii");
                property.SetCollation("ascii_bin");
            }
            else if (providerName != PostgreSqlProvider && configuredCollation is not null)
            {
                property.SetCollation(null);
            }

            if (providerName != PostgreSqlProvider &&
                PostgreSqlColumnTypes.Contains(property.GetColumnType() ?? string.Empty))
            {
                property.SetColumnType(null);
            }

            if (providerName == SqlServerProvider &&
                string.Equals(property.GetColumnType(), "text", StringComparison.OrdinalIgnoreCase))
            {
                property.SetColumnType(null);
            }

            var computedSql = property.GetComputedColumnSql();
            if (providerName == MySqlProvider &&
                computedSql?.Contains("COALESCE(", StringComparison.OrdinalIgnoreCase) == true)
            {
                property.SetComputedColumnSql(null);
                property.ValueGenerated = ValueGenerated.Never;
                property.SetBeforeSaveBehavior(PropertySaveBehavior.Save);
                computedSql = null;
            }

            if (providerName != PostgreSqlProvider && computedSql is not null)
            {
                property.SetComputedColumnSql(computedSql.Replace("::uuid", string.Empty, StringComparison.OrdinalIgnoreCase));
            }

            var defaultSql = property.GetDefaultValueSql();
            if (defaultSql is null)
            {
                continue;
            }

            if (defaultSql.Equals("uuidv7()", StringComparison.OrdinalIgnoreCase))
            {
                property.SetDefaultValueSql(null);
                property.SetValueGeneratorFactory((_, _) => new GuidVersion7ValueGenerator());
                continue;
            }

            if (providerName != PostgreSqlProvider)
            {
                property.SetDefaultValueSql(NormalizeTimestampDefault(defaultSql, providerName));
            }
        }
    }

    private static void ConfigureLocationUnicode(IMutableProperty property, string providerName)
    {
        property.SetIsUnicode(true);
        property.SetCollation(providerName switch
        {
            PostgreSqlProvider => "C",
            SqliteProvider => "BINARY",
            SqlServerProvider => "Latin1_General_100_BIN2",
            MySqlProvider => "utf8mb4_bin",
            _ => throw new InvalidOperationException("Location Unicode text requires a supported relational provider.")
        });
        if (providerName == MySqlProvider)
        {
            property.SetCharSet("utf8mb4");
        }
    }

    private static void ConfigurePortableOrdinalAscii(IMutableProperty property, string providerName)
    {
        switch (providerName)
        {
            case PostgreSqlProvider:
                property.SetCollation("C");
                break;
            case SqliteProvider:
                property.SetCollation("BINARY");
                break;
            case SqlServerProvider:
                property.SetCollation("Latin1_General_100_BIN2");
                break;
            case MySqlProvider:
                property.SetCharSet("ascii");
                property.SetCollation("ascii_bin");
                break;
            default:
                throw new InvalidOperationException("Portable ordinal ASCII keys require a supported relational provider.");
        }
    }

    private static string NormalizeTimestampDefault(string sql, string providerName)
    {
        var interval = PostgreSqlDayInterval().Match(sql);
        if (interval.Success)
        {
            var days = interval.Groups[1].Value;
            return providerName switch
            {
                SqlServerProvider => $"DATEADD(day, {days}, SYSUTCDATETIME())",
                MySqlProvider => $"(TIMESTAMPADD(DAY, {days}, UTC_TIMESTAMP()))",
                _ => $"datetime('now', '+{days} days')"
            };
        }

        if (sql.Equals("'infinity'::timestamp with time zone", StringComparison.OrdinalIgnoreCase))
        {
            return providerName switch
            {
                SqlServerProvider => "CONVERT(datetimeoffset, '9999-12-31T23:59:59.9999999+00:00', 127)",
                MySqlProvider => "(CAST('9999-12-31 23:59:59.999999' AS DATETIME(6)))",
                _ => "'9999-12-31 23:59:59.9999999+00:00'"
            };
        }

        if (sql.Equals("NOW()", StringComparison.OrdinalIgnoreCase) ||
            sql.Equals("statement_timestamp()", StringComparison.OrdinalIgnoreCase))
        {
            return providerName switch
            {
                SqlServerProvider => "SYSUTCDATETIME()",
                MySqlProvider => "(UTC_TIMESTAMP())",
                _ => "CURRENT_TIMESTAMP"
            };
        }

        if (sql.Equals("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase))
        {
            return providerName switch
            {
                SqlServerProvider => "SYSUTCDATETIME()",
                MySqlProvider => "CURRENT_TIMESTAMP(6)",
                _ => "CURRENT_TIMESTAMP"
            };
        }

        return sql;
    }

    private static void NormalizeCheckConstraints(IMutableEntityType entityType, string providerName)
    {
        foreach (var constraint in entityType.GetCheckConstraints().ToArray())
        {
            var constraintName = constraint.Name;
            if (constraintName is null)
            {
                continue;
            }

            var sql = NonBlankBtrim().Replace(constraint.Sql, "trim(${column}) <> ''")
                .Replace("btrim(", "trim(", StringComparison.OrdinalIgnoreCase);

            if (providerName == MySqlProvider)
            {
                sql = NonnegativeCursor().Replace(
                    IsNotDistinctFrom().Replace(sql, "${left} <=> ${right}"),
                    "`cursor` >= 0");
            }

            if (UnsupportedCheckSqlTokens.Any(token => sql.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                entityType.RemoveCheckConstraint(constraintName);
                continue;
            }

            if (providerName == SqlServerProvider)
            {
                sql = NonnegativeCursor().Replace(
                    sql.Replace("length(", "len(", StringComparison.OrdinalIgnoreCase),
                    "[cursor] >= 0");
                sql = NormalizeSqlServerBooleanPredicates(entityType, sql);

                var equivalence = PredicateEquivalence().Match(sql);
                if (equivalence.Success)
                {
                    sql = $"(CASE WHEN {equivalence.Groups["left"].Value} THEN 1 ELSE 0 END) = " +
                          $"(CASE WHEN {equivalence.Groups["right"].Value} THEN 1 ELSE 0 END)";
                }
            }

            var normalizedSql = NormalizeBooleanLiterals(sql, providerName);
            if (!normalizedSql.Equals(constraint.Sql, StringComparison.Ordinal))
            {
                entityType.RemoveCheckConstraint(constraintName);
                entityType.AddCheckConstraint(constraintName, normalizedSql);
            }
        }
    }

    private static void NormalizeIndexFilters(IMutableEntityType entityType, string providerName)
    {
        foreach (var index in entityType.GetIndexes())
        {
            if (providerName == MySqlProvider && !index.IsUnique)
            {
                var prefixLengths = index.Properties
                    .Select(property => property.ClrType == typeof(string) &&
                                        property.GetMaxLength() is > MySqlLookupIndexPrefixLength
                        ? MySqlLookupIndexPrefixLength
                        : 0)
                    .ToArray();
                if (prefixLengths.Any(length => length > 0))
                {
                    index.SetPrefixLength(prefixLengths);
                }
            }

            var filter = index.GetFilter();
            if (filter is not null)
            {
                index.SetFilter(NormalizeBooleanLiterals(filter, providerName));
            }
        }
    }

    private static void ConfigureMySqlExternalBindingUniqueness(IMutableEntityType entityType)
    {
        var replacedIndexNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "ix_external_bindings_external_global_unique",
            "ix_external_bindings_external_tenant_unique",
            "ix_external_bindings_internal_global_unique",
            "ix_external_bindings_internal_tenant_unique"
        };

        foreach (var index in entityType.GetIndexes()
                     .Where(index => replacedIndexNames.Contains(index.GetDatabaseName() ?? string.Empty))
                     .ToArray())
        {
            entityType.RemoveIndex(index);
        }

        AddMySqlUniquenessHash(
            entityType,
            "ExternalGlobalUniquenessHash",
            "external_global_uniqueness_hash",
            "ux_external_bindings_external_global_hash",
            "scope_tenant_id IS NULL",
            ["provider_key", "external_system", "external_type", "external_id"]);
        AddMySqlUniquenessHash(
            entityType,
            "ExternalTenantUniquenessHash",
            "external_tenant_uniqueness_hash",
            "ux_external_bindings_external_tenant_hash",
            "scope_tenant_id IS NOT NULL",
            ["provider_key", "external_system", "external_type", "external_id", "scope_tenant_id"]);
        AddMySqlUniquenessHash(
            entityType,
            "InternalGlobalUniquenessHash",
            "internal_global_uniqueness_hash",
            "ux_external_bindings_internal_global_hash",
            "scope_tenant_id IS NULL",
            ["provider_key", "external_system", "internal_type", "internal_id"]);
        AddMySqlUniquenessHash(
            entityType,
            "InternalTenantUniquenessHash",
            "internal_tenant_uniqueness_hash",
            "ux_external_bindings_internal_tenant_hash",
            "scope_tenant_id IS NOT NULL",
            ["provider_key", "external_system", "internal_type", "internal_id", "scope_tenant_id"]);
    }

    private static void AddMySqlUniquenessHash(
        IMutableEntityType entityType,
        string propertyName,
        string columnName,
        string indexName,
        string scopePredicate,
        IReadOnlyList<string> inputColumns)
    {
        var property = entityType.AddProperty(propertyName, typeof(byte[]));
        property.IsNullable = true;
        property.SetColumnName(columnName);
        property.SetColumnType("binary(32)");
        property.ValueGenerated = ValueGenerated.Never;

        var index = entityType.AddIndex(property);
        index.IsUnique = true;
        index.SetDatabaseName(indexName);
    }

    private static void ConfigureMySqlStorageObjectUniqueness(IMutableEntityType entityType)
    {
        var providerObjectKeyIndex = entityType.GetIndexes().Single(index =>
            index.GetDatabaseName() == "ux_storage_objects_provider_object_key");
        entityType.RemoveIndex(providerObjectKeyIndex);

        var property = entityType.AddProperty("ProviderObjectKeyUniquenessHash", typeof(byte[]));
        property.IsNullable = true;
        property.SetColumnName("provider_object_key_uniqueness_hash");
        property.SetColumnType("binary(32)");
        property.ValueGenerated = ValueGenerated.Never;

        var index = entityType.AddIndex(property);
        index.IsUnique = true;
        index.SetDatabaseName("ux_storage_objects_provider_object_key_hash");
    }

    private static void ConfigureMySqlUserExternalLoginUniqueness(IMutableEntityType entityType)
    {
        var providerKeyIndex = entityType.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(UserExternalLogin.AuthenticationProviderId),
                nameof(UserExternalLogin.ProviderKey)
            ]));
        entityType.RemoveIndex(providerKeyIndex);

        var property = entityType.AddProperty("ProviderKeyUniquenessHash", typeof(byte[]));
        property.IsNullable = false;
        property.SetColumnName("provider_key_uniqueness_hash");
        property.SetColumnType("binary(32)");
        property.SetComputedColumnSql(
            "unhex(sha2(concat(cast(authentication_provider_id as char), ':', provider_key), 256))");
        property.SetIsStored(true);
        property.ValueGenerated = ValueGenerated.OnAddOrUpdate;

        var index = entityType.AddIndex(property);
        index.IsUnique = true;
        index.SetDatabaseName("ux_user_external_logins_provider_key_hash");
    }

    private static void ConfigureMySqlWebPushSubscriptionUniqueness(IMutableEntityType entityType)
    {
        foreach (var index in entityType.GetIndexes().Where(index => index.GetDatabaseName() is
                     "ux_web_push_subscriptions_active_endpoint" or
                     "ux_web_push_subscriptions_active_user_device").ToArray())
        {
            entityType.RemoveIndex(index);
        }

        AddMySqlActiveWebPushHash(
            entityType,
            "ActiveEndpointUniquenessHash",
            "active_endpoint_uniqueness_hash",
            "ux_web_push_subscriptions_active_endpoint_hash",
            ["endpoint"]);
        AddMySqlActiveWebPushHash(
            entityType,
            "ActiveUserDeviceUniquenessHash",
            "active_user_device_uniqueness_hash",
            "ux_web_push_subscriptions_active_user_device_hash",
            ["tenant_id", "user_id", "device_identifier"]);
    }

    private static void AddMySqlActiveWebPushHash(
        IMutableEntityType entityType,
        string propertyName,
        string columnName,
        string indexName,
        IReadOnlyList<string> inputColumns)
    {
        var property = entityType.AddProperty(propertyName, typeof(byte[]));
        property.IsNullable = true;
        property.SetColumnName(columnName);
        property.SetColumnType("binary(32)");
        property.ValueGenerated = ValueGenerated.Never;

        var index = entityType.AddIndex(property);
        index.IsUnique = true;
        index.SetDatabaseName(indexName);
    }

    private static void ConfigureMySqlWebhookProviderBindingUniqueness(IMutableEntityType entityType)
    {
        var replacedPropertySets = new HashSet<string>(StringComparer.Ordinal)
        {
            $"{nameof(WebhookConsumerProviderBinding.ProviderKindId)}|{nameof(WebhookConsumerProviderBinding.NormalizedEnvironment)}|{nameof(WebhookConsumerProviderBinding.NormalizedApplicationUid)}",
            $"{nameof(WebhookConsumerProviderBinding.ProviderKindId)}|{nameof(WebhookConsumerProviderBinding.NormalizedEnvironment)}|{nameof(WebhookConsumerProviderBinding.NormalizedExternalApplicationId)}",
            $"{nameof(WebhookConsumerProviderBinding.ProviderKindId)}|{nameof(WebhookConsumerProviderBinding.NormalizedEnvironment)}|{nameof(WebhookConsumerProviderBinding.NormalizedExternalApplicationId)}|{nameof(WebhookConsumerProviderBinding.NormalizedApplicationUid)}"
        };
        foreach (var index in entityType.GetIndexes().Where(index =>
                     replacedPropertySets.Contains(string.Join('|', index.Properties.Select(property => property.Name))))
                     .ToArray())
        {
            entityType.RemoveIndex(index);
        }

        AddMySqlWebhookProviderBindingHash(
            entityType,
            "ProviderEnvironmentApplicationUidHash",
            "provider_environment_application_uid_hash",
            "ux_webhook_provider_environment_application_uid_hash",
            "1 = 1",
            ["provider_kind_id", "normalized_environment", "normalized_application_uid"]);
        AddMySqlWebhookProviderBindingHash(
            entityType,
            "ProviderEnvironmentExternalAppHash",
            "provider_environment_external_app_hash",
            "ux_webhook_provider_environment_external_app_hash",
            "normalized_external_application_id IS NOT NULL",
            ["provider_kind_id", "normalized_environment", "normalized_external_application_id"]);
        AddMySqlWebhookProviderBindingHash(
            entityType,
            "ProviderApplicationIdentityHash",
            "provider_application_identity_hash",
            "ux_webhook_provider_application_identity_hash",
            "normalized_external_application_id IS NOT NULL",
            ["provider_kind_id", "normalized_environment", "normalized_external_application_id", "normalized_application_uid"]);
    }

    private static void AddMySqlWebhookProviderBindingHash(
        IMutableEntityType entityType,
        string propertyName,
        string columnName,
        string indexName,
        string predicate,
        IReadOnlyList<string> inputColumns)
    {
        var property = entityType.AddProperty(propertyName, typeof(byte[]));
        property.IsNullable = true;
        property.SetColumnName(columnName);
        property.SetColumnType("binary(32)");
        property.ValueGenerated = ValueGenerated.Never;

        var index = entityType.AddIndex(property);
        index.IsUnique = true;
        index.SetDatabaseName(indexName);
    }

    private static void NormalizeSqlServerDeleteBehaviors(IMutableEntityType entityType)
    {
        foreach (var foreignKey in entityType.GetForeignKeys()
                     .Where(foreignKey => foreignKey.DeleteBehavior is
                         DeleteBehavior.Cascade or DeleteBehavior.SetNull))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
        }
    }

    private static string NormalizeBooleanLiterals(string sql, string providerName) =>
        providerName == SqlServerProvider
            ? TrueLiteral().Replace(FalseLiteral().Replace(sql, "0"), "1")
            : sql;

    private static string NormalizeSqlServerBooleanPredicates(
        IMutableEntityType entityType,
        string sql)
    {
        foreach (var property in entityType.GetProperties()
                     .Where(property => property.ClrType == typeof(bool)))
        {
            var columnName = property.GetColumnName();
            if (string.IsNullOrWhiteSpace(columnName))
            {
                continue;
            }

            var escapedColumn = Regex.Escape(columnName);
            sql = Regex.Replace(
                sql,
                $@"\bNOT\s+{escapedColumn}\b",
                $"{columnName} = 0",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            sql = Regex.Replace(
                sql,
                $@"\b{escapedColumn}\b(?=\s+(?:AND|OR)|\s*\))",
                $"{columnName} = 1",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return sql;
    }

    [GeneratedRegex(@"length\(btrim\((?<column>[a-z0-9_]+)\)\)\s*>\s*0", RegexOptions.IgnoreCase)]
    private static partial Regex NonBlankBtrim();

    [GeneratedRegex(@"^statement_timestamp\(\)\s*\+\s*INTERVAL\s*'(\d+) days'$", RegexOptions.IgnoreCase)]
    private static partial Regex PostgreSqlDayInterval();

    [GeneratedRegex(@"\bcursor\s*>=\s*0\b", RegexOptions.IgnoreCase)]
    private static partial Regex NonnegativeCursor();

    [GeneratedRegex(@"\bfalse\b", RegexOptions.IgnoreCase)]
    private static partial Regex FalseLiteral();

    [GeneratedRegex(@"\btrue\b", RegexOptions.IgnoreCase)]
    private static partial Regex TrueLiteral();

    [GeneratedRegex(@"^\((?<left>.+)\)\s*=\s*\((?<right>.+)\)$", RegexOptions.Singleline)]
    private static partial Regex PredicateEquivalence();

    [GeneratedRegex(@"(?<left>[a-z0-9_]+)\s+IS\s+NOT\s+DISTINCT\s+FROM\s+(?<right>[a-z0-9_]+)", RegexOptions.IgnoreCase)]
    private static partial Regex IsNotDistinctFrom();
}
