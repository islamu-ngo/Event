using Explore.Domain;
using Explore.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

public static partial class PostgresEmailDeliveryPolicyContract
{
    private static IReadOnlyList<PostgresPrivilegeGuard.Relation> MutationPrivileges(ExploreDbContext context)
    {
        PostgresPrivilegeGuard.Relation Projection<T>(string role, params string[] properties)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return new(entity.GetTableName()!, role, [], properties.Select(property =>
                new PostgresPrivilegeGuard.Column(entity.FindProperty(property)!.GetColumnName(store)!, ["SELECT"])).ToArray());
        }
        PostgresPrivilegeGuard.Relation Entity<T>(string role, bool insert = false, bool update = false, bool delete = false)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return new(entity.GetTableName()!, role, delete ? ["DELETE"] : [],
                entity.GetProperties().Select(property => new PostgresPrivilegeGuard.Column(property.GetColumnName(store)!,
                    [
                        "SELECT", .. (insert ? new[] { "INSERT" } : []),
                        .. (update && property.Name != nameof(SystemSetting.Id) ? new[] { "UPDATE" } : [])
                    ])).ToArray());
        }
        var processor = context.Model.FindEntityType(typeof(EmailDispatchProcessorState))!;
        var processorStore = StoreObjectIdentifier.Table(processor.GetTableName()!, processor.GetSchema());
        string[] processorUpdates =
        [
            nameof(EmailDispatchProcessorState.DeliveryPolicyRevision),
            nameof(EmailDispatchProcessorState.OptionalSuppressedThroughRevision),
            nameof(EmailDispatchProcessorState.OptionalSuppressedThroughUtc),
            nameof(EmailDispatchProcessorState.UpdatedAt), nameof(EmailDispatchProcessorState.UpdatedBy)
        ];
        var relations = new List<PostgresPrivilegeGuard.Relation>
        {
            Entity<SystemSetting>(InstanceMutationRole, insert: true, update: true, delete: true),
            Entity<SystemSetting>(TenantMutationRole),
            Entity<TenantSetting>(InstanceMutationRole, insert: true, update: true, delete: true),
            Entity<TenantSetting>(TenantMutationRole, insert: true, update: true, delete: true),
            Projection<Tenant>(InstanceMutationRole, nameof(Tenant.Id)),
            Projection<Tenant>(TenantMutationRole, nameof(Tenant.Id)),
            new(processor.GetTableName()!, InstanceMutationRole, [], processor.GetProperties().Select(property =>
                new PostgresPrivilegeGuard.Column(property.GetColumnName(processorStore)!,
                    ["SELECT", "INSERT", .. (processorUpdates.Contains(property.Name, StringComparer.Ordinal)
                        ? new[] { "UPDATE" } : [])])).ToArray()),
            Projection<EmailDispatchTenantControl>(InstanceMutationRole,
                nameof(EmailDispatchTenantControl.TenantId), nameof(EmailDispatchTenantControl.DeliveryPolicyRevision)),
            Projection<EmailDispatchTenantControl>(TenantMutationRole,
                nameof(EmailDispatchTenantControl.TenantId), nameof(EmailDispatchTenantControl.DeliveryPolicyRevision)),
            Entity<TenantPlan>(TenantMutationRole),
            Entity<TenantPlanVersion>(TenantMutationRole),
            Entity<TenantPlanVersionSetting>(TenantMutationRole),
            Entity<TenantPlanVersionQuota>(TenantMutationRole),
            Entity<TenantPlanAssignment>(TenantMutationRole, update: true),
            Entity<EventParticipationConfiguration>(TenantMutationRole),
            Projection<Event>(TenantMutationRole, nameof(Event.Id), nameof(Event.TenantId), nameof(Event.IsDeleted))
        };
        foreach (string role in new[] { InstanceMutationRole, TenantMutationRole })
        {
            relations.Add(Projection<TenantUserRoleGrant>(role, nameof(TenantUserRoleGrant.TenantId),
                nameof(TenantUserRoleGrant.TenantUserId), nameof(TenantUserRoleGrant.RoleId), nameof(TenantUserRoleGrant.RevokedAt)));
            relations.Add(Projection<TenantUser>(role, nameof(TenantUser.Id), nameof(TenantUser.TenantId),
                nameof(TenantUser.UserId), nameof(TenantUser.StatusId), nameof(TenantUser.IsDeleted)));
            relations.Add(Projection<PlatformUserRole>(role, nameof(PlatformUserRole.UserId), nameof(PlatformUserRole.RoleId)));
            relations.Add(Projection<Role>(role, nameof(Role.Id), nameof(Role.RoleScopeId), nameof(Role.MasterCode)));
        }
        return relations;
    }

    private static string BuildMutationGrantsSql(ExploreDbContext context)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        string grants = string.Join("\n", MutationPrivileges(context).Select(relation =>
        {
            string table = sql.DelimitIdentifier(relation.Name, Schema(context));
            string role = sql.DelimitIdentifier(relation.Role);
            string grants = string.Join("\n", new[] { "SELECT", "INSERT", "UPDATE" }.Select(privilege =>
            {
                string[] columns = relation.Columns.Where(column => column.Privileges.Contains(privilege, StringComparer.Ordinal))
                    .Select(column => sql.DelimitIdentifier(column.Name)).ToArray();
                return columns.Length == 0 ? "" : $"GRANT {privilege} ({string.Join(", ", columns)}) ON {table} TO {role};";
            }));
            if (relation.Privileges.Contains("DELETE", StringComparer.Ordinal))
                grants += $"\nGRANT DELETE ON {table} TO {role};";
            return grants;
        }));
        string Table<T>() => sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!, Schema(context));
        string Column<T>(string property)
        {
            var entity = context.Model.FindEntityType(typeof(T))!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            return sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(store)!);
        }
        string selectedTenant = $"NULLIF(pg_catalog.current_setting('{PostgresTenantSessionInterceptor.CurrentTenantSettingName}', true), '')::uuid";
        string emailKeys = string.Join(", ", EmailDeliverySettingKeys.All.Select(LoginLiteral));
        string tenantSettings = Table<TenantSetting>();
        string controls = Table<EmailDispatchTenantControl>();
        string policies = $"""
            DROP POLICY IF EXISTS email_policy_instance_setting_mutation ON {tenantSettings};
            CREATE POLICY email_policy_instance_setting_mutation ON {tenantSettings} FOR ALL TO {InstanceMutationRole}
                USING ({Column<TenantSetting>(nameof(TenantSetting.SettingKey))} IN ({emailKeys}))
                WITH CHECK ({Column<TenantSetting>(nameof(TenantSetting.SettingKey))} IN ({emailKeys}));
            DROP POLICY IF EXISTS email_policy_tenant_setting_mutation ON {tenantSettings};
            CREATE POLICY email_policy_tenant_setting_mutation ON {tenantSettings} FOR ALL TO {TenantMutationRole}
                USING ({Column<TenantSetting>(nameof(TenantSetting.TenantId))} = {selectedTenant})
                WITH CHECK ({Column<TenantSetting>(nameof(TenantSetting.TenantId))} = {selectedTenant});
            DROP POLICY IF EXISTS email_policy_instance_control_preview ON {controls};
            CREATE POLICY email_policy_instance_control_preview ON {controls} FOR SELECT TO {InstanceMutationRole} USING (true);
            DROP POLICY IF EXISTS email_policy_tenant_control_preview ON {controls};
            CREATE POLICY email_policy_tenant_control_preview ON {controls} FOR SELECT TO {TenantMutationRole}
                USING ({Column<EmailDispatchTenantControl>(nameof(EmailDispatchTenantControl.TenantId))} = {selectedTenant});
            """;
        foreach (Type type in new[]
                 {
                     typeof(TenantUserRoleGrant), typeof(TenantUser), typeof(TenantPlanAssignment),
                     typeof(EventParticipationConfiguration), typeof(Event)
                 })
        {
            var entity = context.Model.FindEntityType(type)!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            string table = sql.DelimitIdentifier(entity.GetTableName()!, Schema(context));
            string tenant = sql.DelimitIdentifier(entity.FindProperty(nameof(TenantSetting.TenantId))!.GetColumnName(store)!);
            string roles = type == typeof(TenantUserRoleGrant) || type == typeof(TenantUser)
                ? $"{InstanceMutationRole}, {TenantMutationRole}"
                : TenantMutationRole;
            policies += $"""

                DROP POLICY IF EXISTS email_policy_tenant_dependency_read ON {table};
                CREATE POLICY email_policy_tenant_dependency_read ON {table} FOR SELECT TO {roles}
                    USING ({tenant} = {selectedTenant});
                """;
            if (type == typeof(TenantPlanAssignment))
                policies += $"""

                    DROP POLICY IF EXISTS email_policy_tenant_assignment_update ON {table};
                    CREATE POLICY email_policy_tenant_assignment_update ON {table} FOR UPDATE TO {TenantMutationRole}
                        USING ({tenant} = {selectedTenant}) WITH CHECK ({tenant} = {selectedTenant});
                    """;
        }
        return grants + "\n" + policies;
    }
}
