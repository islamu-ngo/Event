using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresEmailDeliveryPolicyContract
{
    /// <summary>
    /// Canonical inventory of the contract's own permissive mutation policies. The runtime
    /// readiness query admits exactly these policies for the declared email mutation logins;
    /// install-side stamping marks each policy with the same stamp. New contract policies
    /// must be added here (and to <see cref="BuildMutationGrantsSql"/>) or readiness fails closed.
    /// </summary>
    private sealed record MutationPolicy(string Table, string Policy, string[] Roles, string Command);

    private static IReadOnlyList<MutationPolicy> MutationPolicies(ExploreDbContext context)
    {
        string Table<T>() => context.Model.FindEntityType(typeof(T))!.GetTableName()!;
        return
        [
            new(Table<TenantSetting>(), "email_policy_instance_setting_mutation", [InstanceMutationRole], "*"),
            new(Table<TenantSetting>(), "email_policy_tenant_setting_mutation", [TenantMutationRole], "*"),
            new(Table<EmailDispatchTenantControl>(), "email_policy_instance_control_preview",
                [InstanceMutationRole], "r"),
            new(Table<EmailDispatchTenantControl>(), "email_policy_tenant_control_preview",
                [TenantMutationRole], "r"),
            new(Table<TenantUserRoleGrant>(), "email_policy_tenant_dependency_read",
                [InstanceMutationRole, TenantMutationRole], "r"),
            new(Table<TenantUser>(), "email_policy_tenant_dependency_read",
                [InstanceMutationRole, TenantMutationRole], "r"),
            new(Table<TenantPlanAssignment>(), "email_policy_tenant_dependency_read", [TenantMutationRole], "r"),
            new(Table<EventParticipationConfiguration>(), "email_policy_tenant_dependency_read",
                [TenantMutationRole], "r"),
            new(Table<Event>(), "email_policy_tenant_dependency_read", [TenantMutationRole], "r"),
            new(Table<TenantPlanAssignment>(), "email_policy_tenant_assignment_update", [TenantMutationRole], "w"),
        ];
    }

    /// <summary>
    /// Exact admission exception for the contract's own mutation policies, composed into the
    /// runtime readiness query's p/c/n aliases. Structural pinning plus the install-time stamp
    /// match the tenant-isolation fingerprint bar; expression drift is caught by the stamp's
    /// catalogue hashes rather than a deparser text comparison.
    /// </summary>
    internal static string MutationPolicyException(ExploreDbContext context, string declaredMutationLogin)
    {
        string schema = Schema(context);
        string predicates = string.Join("\n                OR ", MutationPolicies(context).Select(policy => $"""
            (n.nspname={Text(schema)} AND c.relname={Text(policy.Table)} AND p.polname={Text(policy.Policy)}
                AND p.polcmd={Text(policy.Command)} AND p.polpermissive AND p.polrelid=c.oid
                AND p.polroles=ARRAY[{string.Join(", ", policy.Roles.Select(RoleOid))}]
                AND pg_catalog.obj_description(p.oid,'pg_policy')={Text(PolicyStamp(schema, policy))}
                    || pg_catalog.md5(p.polqual::text) || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text),''))
            """));
        return $"""
            COALESCE((session_user={Text(declaredMutationLogin)} AND current_user=session_user
                AND pg_catalog.current_setting('role') IN ('none',session_user::text)
                AND ({predicates})),false)
            """;
    }

    /// <summary>Install-side catalogue stamps for every contract mutation policy.</summary>
    private static string BuildMutationPolicyStampSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = Schema(context);
        return string.Join("\n", MutationPolicies(context).Select(policy =>
        {
            string table = sql.DelimitIdentifier(policy.Table, schema);
            return $"""
                DO $stamp$ BEGIN
                    EXECUTE (SELECT pg_catalog.format('COMMENT ON POLICY %I ON %s IS %L',p.polname,{Text(table)},
                        {Text(PolicyStamp(schema, policy))} || pg_catalog.md5(p.polqual::text) || ':' || COALESCE(pg_catalog.md5(p.polwithcheck::text),''))
                        FROM pg_catalog.pg_policy p WHERE p.polrelid=pg_catalog.to_regclass({Text(table)}) AND p.polname={Text(policy.Policy)});
                END $stamp$;
                """;
        }));
    }

    private static string PolicyStamp(string schema, MutationPolicy policy) => "email-policy:v1:"
        + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{schema}\n{policy.Table}\n{string.Join(",", policy.Roles)}\n{policy.Policy}\n{policy.Command}"))) + ":";

    private static string RoleOid(string role) => $"(SELECT oid FROM pg_catalog.pg_roles WHERE rolname={Text(role)})";

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";
}
