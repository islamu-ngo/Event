using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Schema;

/// <summary>
/// Preserves existing fixed owners when the writer's key-limited SystemSetting
/// read enables RLS. Policies support existing ACLs; they grant no privileges.
/// </summary>
internal static class PostgresLocalIdentitySystemSettingPolicies
{
    private sealed record Owner(string Role, string[] Operations)
    {
        internal string PolicyPrefix { get; init; } = "li_" + Role;
    }

    private static Owner Migrator(string login) =>
        new(login, ["SELECT", "INSERT", "UPDATE", "DELETE"]) { PolicyPrefix = "local_identity_migrator" };

    private static readonly Owner[] Owners =
    [
        new(PostgresMixedSettingsContract.OwnerRole, ["SELECT", "INSERT", "UPDATE"]),
        new(PostgresOrdinarySettingsContract.OwnerRole, ["SELECT", "INSERT", "UPDATE"]),
        new(PostgresProviderConfigurationContract.OwnerRole, ["SELECT", "INSERT", "UPDATE"]),
        new(PostgresStorageProbeSourceContract.OwnerRole, ["SELECT"]),
        new(PostgresStorageConfigurationContract.OwnerRole, ["SELECT", "INSERT", "UPDATE"]),
        new(PostgresEventResourceProviderContract.OwnerRole, ["SELECT", "INSERT", "UPDATE"]),
        new(PostgresOperatorIdentityEditorContract.OwnerRole, ["SELECT", "INSERT", "UPDATE"]),
        new(PostgresResolverConfigurationContract.OwnerRole, ["SELECT", "INSERT", "UPDATE"]),
        new(PostgresConfigurationImportScalarSettingContract.OwnerRole, ["SELECT", "INSERT", "UPDATE"]),
        new(PostgresConfigurationImportTenantSettingContract.OwnerRole, ["SELECT"]),
        new(PostgresConfigurationImportEventResourceContract.OwnerRole, ["SELECT", "INSERT", "UPDATE"]),
        new(PostgresConfigurationImportPublicationPolicyContract.OwnerRole, ["SELECT", "INSERT", "UPDATE", "DELETE"]),
        new(PostgresConfigurationManifestBootstrapContract.OwnerRole, ["SELECT", "INSERT", "UPDATE"]),
        new(PostgresEmailDeliveryPolicyContract.InstanceMutationRole, ["SELECT", "INSERT", "UPDATE", "DELETE"]),
        new(PostgresEmailDeliveryPolicyContract.TenantMutationRole, ["SELECT"]),
        new(PostgresWebhookLocalDeliveryAdmissionContract.OwnerRole, ["SELECT"])
    ];

    /// <summary>The relation alias is supplied by a fixed source owner's catalogue query.</summary>
    internal static string Compatible(string ownerRole, string relationAlias)
    {
        var owner = Owners.Single(value => value.Role == ownerRole);
        return $"((NOT {relationAlias}.relrowsecurity AND NOT {relationAlias}.relforcerowsecurity) OR " +
            $"({relationAlias}.relrowsecurity AND {relationAlias}.relforcerowsecurity AND ({Required(owner, relationAlias + ".oid")})))";
    }

    internal static string Complete(string relation, string migratorLogin) =>
        string.Join(" AND ", Owners.Select(owner => $"""
            (NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={Text(owner.Role)})
                OR ({Required(owner, $"pg_catalog.to_regclass({Text(relation)})")}))
            """)) + $" AND ({Required(Migrator(migratorLogin), $"pg_catalog.to_regclass({Text(relation)})")})";

    private static string Required(Owner owner, string relationOid) =>
        string.Join(" AND ", owner.Operations.Select(operation => $"""
            EXISTS (SELECT 1 FROM pg_catalog.pg_policy p WHERE p.polrelid={relationOid}
                AND p.polname={Text(Name(owner, operation))} AND p.polpermissive
                AND p.polroles=ARRAY[(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(owner.Role)})]
                AND p.polcmd='{Command(operation)}'
                AND {(operation == "INSERT" ? "p.polqual IS NULL" : "pg_catalog.pg_get_expr(p.polqual,p.polrelid)='true'")}
                AND {(operation is "INSERT" or "UPDATE" ? "pg_catalog.pg_get_expr(p.polwithcheck,p.polrelid)='true'" : "p.polwithcheck IS NULL")})
            """));

    internal static async Task InstallAsync(DbContext context, string relation, string migratorLogin,
        CancellationToken token)
    {
        var names = context.GetService<ISqlGenerationHelper>();
        // FORCE RLS must preserve the declared table owner's existing seed/maintenance access.
        foreach (var owner in Owners.Append(Migrator(migratorLogin)))
        {
            await using var exists = context.Database.GetDbConnection().CreateCommand();
            exists.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            exists.CommandText = $"SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname={Text(owner.Role)})";
            if (await exists.ExecuteScalarAsync(token) is not true) continue;
            foreach (string operation in owner.Operations)
            {
                // Existing policy names are not replaced: a partial/drifting installation must fail.
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
                command.CommandText = $"CREATE POLICY {names.DelimitIdentifier(Name(owner, operation))} ON {relation} FOR {operation} TO {names.DelimitIdentifier(owner.Role)} " +
                    (operation != "INSERT" ? "USING (true) " : "") +
                    (operation is "INSERT" or "UPDATE" ? "WITH CHECK (true)" : "");
                await command.ExecuteNonQueryAsync(token);
            }
        }
    }

    private static string Name(Owner owner, string operation) => owner.PolicyPrefix + "_" + operation.ToLowerInvariant();
    private static char Command(string operation) => operation switch
    {
        "SELECT" => 'r',
        "INSERT" => 'a',
        "UPDATE" => 'w',
        "DELETE" => 'd',
        _ => throw new InvalidOperationException("local_identity_owner_policy_operation_invalid")
    };
    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
