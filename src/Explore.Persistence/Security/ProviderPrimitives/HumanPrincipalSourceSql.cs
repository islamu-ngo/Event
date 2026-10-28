using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Private, fixed source vocabulary shared by native installation and portable dispatch.</summary>
internal sealed class HumanPrincipalSourceSql(ExploreDbContext context, bool currentReads = false)
{
    internal static readonly (Type Type, string[] Columns)[] Sources =
    [
        (typeof(User), ["Id", "IsDeleted"]),
        (typeof(UserExternalLogin), ["Id", "UserId", "AuthenticationProviderId", "ProviderKey"]),
        (typeof(PlatformUserRole), ["Id", "UserId", "RoleId"]),
        (typeof(TenantUser), ["Id", "TenantId", "UserId", "StatusId", "IsDeleted"]),
        (typeof(TenantUserRoleGrant), ["Id", "TenantId", "TenantUserId", "RoleId", "RoleScopeId", "RevokedAt"]),
        (typeof(OrganizationMember), ["Id", "UserId", "TenantId", "OrganizationTenantId", "RoleId", "IsDeleted"]),
        (typeof(GroupMember), ["Id", "UserId", "TenantId", "GroupTenantId", "RoleId", "IsDeleted"]),
        (typeof(Role), ["Id", "RoleScopeId", "MasterCode"]),
        (typeof(RolePermission), ["RoleId", "PermissionId"]),
        (typeof(Permission), ["Id", "MasterCode", "IsActive"]),
        (typeof(Tenant), ["Id", "TenantStatusId"]),
        (typeof(TenantStatus), ["Id", "IsActiveState"]),
        (typeof(OrganizationTenant), ["Id", "TenantId", "OrganizationId", "IsDeleted", "IsSuspended", "ApprovalStatusId"]),
        (typeof(Organization), ["Id", "IsDeleted"]),
        (typeof(GroupTenant), ["Id", "TenantId", "GroupId", "IsDeleted", "IsSuspended", "ApprovalStatusId"]),
        (typeof(Group), ["Id", "IsDeleted"])
    ];

    internal static readonly (Type Type, string[] Columns)[] LocalSources =
    [
        (typeof(LocalIdentityUser), ["Id", "PasswordHash", "SecurityStamp", "EmailConfirmed"]),
        (typeof(IdentityUserToken<Guid>), ["UserId", "LoginProvider", "Name", "Value"]),
        (typeof(LocalIdentityCredentialOperation), ["Id", "LocalSubjectId", "Kind", "Stage", "PersonalActorId", "ExternalLoginId"]),
        (typeof(Actor), ["Id", "UserId", "ActorTypeId", "IsDeleted", "IsSuspended"]),
        (typeof(SystemSetting), ["Id", "SettingKey", "Value"])
    ];

    internal string Schema => context.GetService<IDbContextOptions>()
        .FindExtension<RelationalNamespaceOptionsExtension>()?.TargetSchema
        ?? context.Model.FindEntityType(typeof(User))!.GetSchema() ?? "public";
    internal string Name(string name) => context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name, Schema);
    internal string Table(Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!,
            context.Database.IsNpgsql() ? Schema : entity.GetSchema());
    }
    internal string ColumnName(Type type, string property)
    {
        var entity = context.Model.FindEntityType(type)!;
        return entity.FindProperty(property)!.GetColumnName(
            StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()))!;
    }
    internal string Column(Type type, string property) => Quote(ColumnName(type, property));
    internal string Quote(string name) => context.GetService<ISqlGenerationHelper>().DelimitIdentifier(name);
    private string T<T>() => Table(typeof(T));
    private string C<T>(string property) => Column(typeof(T), property);
    private string A(string value, string name) => $"{value} AS {Quote(name)}";
    private string Fields<T>(string alias, params string[] properties) =>
        string.Join(", ", properties.Select(property => A($"{alias}.{C<T>(property)}", property)));
    private string Boolean(string predicate) => context.Database.IsNpgsql()
        ? $"COALESCE(({predicate}), false)"
        : context.Database.IsSqlServer()
            ? $"CAST(CASE WHEN {predicate} THEN 1 ELSE 0 END AS bit)"
            : $"CASE WHEN {predicate} THEN 1 ELSE 0 END";

    private string CurrentRead => currentReads ? " FOR UPDATE" : "";
    internal string ActorRoles => string.Join(" UNION ", new[]
    {
        $"SELECT {C<PlatformUserRole>("RoleId")} FROM {T<PlatformUserRole>()} WHERE {C<PlatformUserRole>("UserId")} = @actor",
        $"""
        SELECT g.{C<TenantUserRoleGrant>("RoleId")} FROM {T<TenantUserRoleGrant>()} g
            JOIN {T<TenantUser>()} m ON m.{C<TenantUser>("Id")} = g.{C<TenantUserRoleGrant>("TenantUserId")}
                AND m.{C<TenantUser>("TenantId")} = g.{C<TenantUserRoleGrant>("TenantId")}
            WHERE m.{C<TenantUser>("UserId")} = @actor
        """,
        $"SELECT {C<OrganizationMember>("RoleId")} FROM {T<OrganizationMember>()} WHERE {C<OrganizationMember>("UserId")} = @actor",
        $"SELECT {C<GroupMember>("RoleId")} FROM {T<GroupMember>()} WHERE {C<GroupMember>("UserId")} = @actor"
    });

    private string ActorRolePredicate(string role) => !currentReads ? $"{role} IN ({ActorRoles})" : $"""
        (EXISTS (SELECT 1 FROM {T<PlatformUserRole>()} a WHERE a.{C<PlatformUserRole>("UserId")} = @actor
            AND a.{C<PlatformUserRole>("RoleId")} = {role} FOR UPDATE)
        OR EXISTS (SELECT 1 FROM {T<TenantUserRoleGrant>()} g JOIN {T<TenantUser>()} m
            ON m.{C<TenantUser>("Id")} = g.{C<TenantUserRoleGrant>("TenantUserId")}
                AND m.{C<TenantUser>("TenantId")} = g.{C<TenantUserRoleGrant>("TenantId")}
            WHERE m.{C<TenantUser>("UserId")} = @actor AND g.{C<TenantUserRoleGrant>("RoleId")} = {role} FOR UPDATE)
        OR EXISTS (SELECT 1 FROM {T<OrganizationMember>()} a WHERE a.{C<OrganizationMember>("UserId")} = @actor
            AND a.{C<OrganizationMember>("RoleId")} = {role} FOR UPDATE)
        OR EXISTS (SELECT 1 FROM {T<GroupMember>()} a WHERE a.{C<GroupMember>("UserId")} = @actor
            AND a.{C<GroupMember>("RoleId")} = {role} FOR UPDATE))
        """;

    internal string[] Queries =>
    [
        $"SELECT {Fields<User>("u", "Id", "IsDeleted")} FROM {T<User>()} u WHERE u.{C<User>("Id")} = @actor",
        $"""
        SELECT {Fields<UserExternalLogin>("l", "Id", "UserId", "AuthenticationProviderId", "ProviderKey")}
        FROM {T<UserExternalLogin>()} l WHERE l.{C<UserExternalLogin>("UserId")} = @actor
            AND l.{C<UserExternalLogin>("AuthenticationProviderId")} = @provider
            AND l.{C<UserExternalLogin>("ProviderKey")} = @account
        """,
        $"SELECT {Fields<PlatformUserRole>("g", "Id", "RoleId")} FROM {T<PlatformUserRole>()} g WHERE g.{C<PlatformUserRole>("UserId")} = @actor",
        $"""
        SELECT {Fields<Role>("r", "Id", "RoleScopeId", "MasterCode")},
            {A(Boolean($"r.{C<Role>("MasterCode")} = 'platform.admin'"), "MatchesPlatformAdministratorCode")}
        FROM {T<Role>()} r WHERE {ActorRolePredicate($"r.{C<Role>("Id")}")}
        """,
        $"SELECT {Fields<TenantUser>("m", "Id", "TenantId", "StatusId", "IsDeleted")} FROM {T<TenantUser>()} m WHERE m.{C<TenantUser>("UserId")} = @actor",
        $"""
        SELECT {Fields<TenantUserRoleGrant>("g", "Id", "TenantId", "TenantUserId", "RoleId", "RoleScopeId", "RevokedAt")}
        FROM {T<TenantUserRoleGrant>()} g JOIN {T<TenantUser>()} m
            ON m.{C<TenantUser>("Id")} = g.{C<TenantUserRoleGrant>("TenantUserId")}
                AND m.{C<TenantUser>("TenantId")} = g.{C<TenantUserRoleGrant>("TenantId")}
        WHERE m.{C<TenantUser>("UserId")} = @actor
        """,
        $"""
        SELECT {Fields<Tenant>("t", "Id", "TenantStatusId")}, {A($"s.{C<TenantStatus>("IsActiveState")}", "IsActiveState")}
        FROM {T<Tenant>()} t JOIN {T<TenantStatus>()} s ON s.{C<TenantStatus>("Id")} = t.{C<Tenant>("TenantStatusId")}
        WHERE t.{C<Tenant>("Id")} = @tenant OR t.{C<Tenant>("Id")} IN
            (SELECT {C<TenantUser>("TenantId")} FROM {T<TenantUser>()} WHERE {C<TenantUser>("UserId")} = @actor{CurrentRead})
        """,
        MembershipQuery(typeof(OrganizationMember), typeof(OrganizationTenant), typeof(Organization), "OrganizationTenantId", "OrganizationId"),
        MembershipQuery(typeof(GroupMember), typeof(GroupTenant), typeof(Group), "GroupTenantId", "GroupId"),
        $"""
        SELECT {Fields<RolePermission>("rp", "RoleId", "PermissionId")}, {Fields<Permission>("p", "MasterCode", "IsActive")},
            {A(Boolean($"p.{C<Permission>("MasterCode")} = '{PermissionCodes.EventCreate}'"), "MatchesEventCreateCode")},
            {A(Boolean($"p.{C<Permission>("MasterCode")} = '{PermissionCodes.EventManageFinance}'"), "MatchesEventFinanceCode")},
            {A(Boolean($"p.{C<Permission>("MasterCode")} = '{PermissionCodes.GroupMemberCreate}'"), "MatchesGroupMemberCreateCode")},
            {A(Boolean($"p.{C<Permission>("MasterCode")} = '{PermissionCodes.GroupMemberUpdate}'"), "MatchesGroupMemberUpdateCode")},
            {A(Boolean($"p.{C<Permission>("MasterCode")} = '{PermissionCodes.GroupMemberDelete}'"), "MatchesGroupMemberDeleteCode")}
        FROM {T<RolePermission>()} rp JOIN {T<Permission>()} p ON p.{C<Permission>("Id")} = rp.{C<RolePermission>("PermissionId")}
        WHERE {ActorRolePredicate($"rp.{C<RolePermission>("RoleId")}")}
        """,
        $"SELECT {C<RolePermission>("RoleId")} AS {Quote("Value")} FROM {T<RolePermission>()}",
        PermissionList(typeof(OrganizationMember), typeof(OrganizationTenant), "OrganizationTenantId", "OrganizationId", PermissionCodes.EventCreate),
        PermissionList(typeof(GroupMember), typeof(GroupTenant), "GroupTenantId", "GroupId", PermissionCodes.EventCreate),
        PermissionList(typeof(OrganizationMember), typeof(OrganizationTenant), "OrganizationTenantId", "OrganizationId", PermissionCodes.EventManageFinance),
        PermissionList(typeof(GroupMember), typeof(GroupTenant), "GroupTenantId", "GroupId", PermissionCodes.EventManageFinance)
    ];

    internal string[] AccountMembershipPolicyQueries =>
    [
        $"""
        SELECT {Fields<Role>("r", "Id", "RoleScopeId", "MasterCode")},
            {A(Boolean($"r.{C<Role>("MasterCode")} = 'platform.admin'"), "MatchesPlatformAdministratorCode")}
        FROM {T<Role>()} r
        ORDER BY r.{C<Role>("Id")}
        """,
        $"""
        SELECT {Fields<RolePermission>("rp", "RoleId", "PermissionId")},
            {Fields<Permission>("p", "MasterCode", "IsActive")},
            {A(Boolean($"p.{C<Permission>("MasterCode")} = '{PermissionCodes.EventCreate}'"), "MatchesEventCreateCode")},
            {A(Boolean($"p.{C<Permission>("MasterCode")} = '{PermissionCodes.EventManageFinance}'"), "MatchesEventFinanceCode")},
            {A(Boolean($"p.{C<Permission>("MasterCode")} = '{PermissionCodes.GroupMemberCreate}'"), "MatchesGroupMemberCreateCode")},
            {A(Boolean($"p.{C<Permission>("MasterCode")} = '{PermissionCodes.GroupMemberUpdate}'"), "MatchesGroupMemberUpdateCode")},
            {A(Boolean($"p.{C<Permission>("MasterCode")} = '{PermissionCodes.GroupMemberDelete}'"), "MatchesGroupMemberDeleteCode")}
        FROM {T<RolePermission>()} rp
        JOIN {T<Permission>()} p
            ON p.{C<Permission>("Id")} = rp.{C<RolePermission>("PermissionId")}
        ORDER BY rp.{C<RolePermission>("RoleId")}, rp.{C<RolePermission>("PermissionId")}
        """
    ];

    internal string[] FallbackQueries =>
    [
        PermissionList(typeof(OrganizationMember), typeof(OrganizationTenant), "OrganizationTenantId", "OrganizationId", null, (int)RoleEnum.OrgAdmin),
        PermissionList(typeof(GroupMember), typeof(GroupTenant), "GroupTenantId", "GroupId", null, (int)RoleEnum.GroupAdmin)
    ];

    internal string[] LocalQueries
    {
        get
        {
            string password = $"u.{C<LocalIdentityUser>("PasswordHash")}";
            if (context.Database.IsSqlServer()) password += " COLLATE Latin1_General_100_BIN2";
            else if (context.Database.ProviderName == RelationalNamedLock.MySqlProvider) password = "BINARY " + password;
            // Match String.IsNullOrWhiteSpace without returning password hashes.
            foreach (char character in "\t\n\v\f\r \u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000")
                password = $"REPLACE({password}, {(context.Database.IsSqlServer() ? "N" : "")}'{character}', '')";
            string stamp = $"u.{C<LocalIdentityUser>("SecurityStamp")}";
            string match = context.Database.IsSqlServer()
                ? $"CONVERT(varbinary(max), {stamp}) = CONVERT(varbinary(max), @stamp)"
                : context.Database.ProviderName == RelationalNamedLock.MySqlProvider
                    ? $"BINARY {stamp} = BINARY @stamp"
                    : context.Database.IsNpgsql() ? $"{stamp} COLLATE \"C\" = @stamp"
                        : $"{stamp} COLLATE BINARY = @stamp";
            return
            [
                $"""
                SELECT {Fields<LocalIdentityUser>("u", "Id")}, {A(Boolean($"{password} <> ''"), "HasPassword")},
                    {A(Boolean(match), "StampMatches")}, {Fields<LocalIdentityUser>("u", "EmailConfirmed")}
                FROM {T<LocalIdentityUser>()} u WHERE u.{C<LocalIdentityUser>("Id")} = @actor
                """,
                $"""
                SELECT {C<IdentityUserToken<Guid>>("Value")} AS {Quote("Value")} FROM {T<IdentityUserToken<Guid>>()}
                WHERE {C<IdentityUserToken<Guid>>("UserId")} = @actor
                    AND {C<IdentityUserToken<Guid>>("LoginProvider")} = '{LocalCredentialStateMetadata.TokenLoginProvider}'
                    AND {C<IdentityUserToken<Guid>>("Name")} = '{LocalCredentialStateMetadata.TokenName}'
                """,
                $"""
                SELECT {Fields<LocalIdentityCredentialOperation>("o", "Id", "LocalSubjectId", "Kind", "Stage", "PersonalActorId", "ExternalLoginId")}
                FROM {T<LocalIdentityCredentialOperation>()} o WHERE o.{C<LocalIdentityCredentialOperation>("LocalSubjectId")} = @actor
                """,
                $"""
                SELECT {Fields<Actor>("a", "Id", "UserId", "ActorTypeId", "IsDeleted", "IsSuspended")}
                FROM {T<Actor>()} a WHERE a.{C<Actor>("UserId")} = @actor
                """,
                $"""
                SELECT {C<SystemSetting>("Value")} AS {Quote("Value")} FROM {T<SystemSetting>()}
                WHERE {C<SystemSetting>("SettingKey")} = '{GovernanceSettingKeys.Email.DeliveryEnabled}'
                """
            ];
        }
    }

    private string MembershipQuery(Type member, Type placement, Type owner, string placementKey, string ownerKey)
    {
        string M(string property) => $"m.{Column(member, property)}";
        string P(string property) => $"p.{Column(placement, property)}";
        return $"""
            SELECT {A(M("Id"), "Id")}, {A(M("TenantId"), "TenantId")}, {A(P("Id"), "PlacementId")},
                {A(P(ownerKey), "OwnerId")}, {A(M("RoleId"), "RoleId")}, {A(M("IsDeleted"), "IsDeleted")},
                {A(P("IsDeleted"), "PlacementIsDeleted")}, {A($"o.{Column(owner, "IsDeleted")}", "OwnerIsDeleted")},
                {A(P("ApprovalStatusId"), "ApprovalStatusId")}, {A(P("IsSuspended"), "PlacementIsSuspended")}
            FROM {Table(member)} m JOIN {Table(placement)} p ON {P("Id")} = {M(placementKey)} AND {P("TenantId")} = {M("TenantId")}
                JOIN {Table(owner)} o ON o.{Column(owner, "Id")} = {P(ownerKey)}
            WHERE {M("UserId")} = @actor
            """;
    }

    private string PermissionList(Type member, Type placement, string placementKey, string ownerKey, string? permission, int? legacyRole = null)
    {
        // Keep the original unordered SELECT and its multiplicity. No DISTINCT, owner join,
        // active-placement predicate, or sorting is added to the ordinary permission lists.
        string M(string property) => $"m.{Column(member, property)}";
        string P(string property) => $"p.{Column(placement, property)}";
        string zero = context.Database.IsNpgsql() ? "false" : "0";
        string one = context.Database.IsNpgsql() ? "true" : "1";
        string predicate = legacyRole is { } role ? $"{M("RoleId")} IN ({role})" : $"""
            EXISTS (SELECT 1 FROM {T<RolePermission>()} rp JOIN {T<Permission>()} permission
                ON permission.{C<Permission>("Id")} = rp.{C<RolePermission>("PermissionId")}
                WHERE rp.{C<RolePermission>("RoleId")} = {M("RoleId")}
                    AND permission.{C<Permission>("MasterCode")} = '{permission}'
                    AND permission.{C<Permission>("IsActive")} = {one}{CurrentRead})
            """;
        return $"""
            SELECT {P(ownerKey)} AS {Quote("Value")} FROM {Table(member)} m JOIN {Table(placement)} p
                ON {P("Id")} = {M(placementKey)} AND {P("TenantId")} = {M("TenantId")}
            WHERE {M("UserId")} = @actor AND {M("TenantId")} = @tenant AND {P("TenantId")} = @tenant
                AND {M("IsDeleted")} = {zero} AND {P("IsDeleted")} = {zero}
                AND {predicate}
            """;
    }
}
