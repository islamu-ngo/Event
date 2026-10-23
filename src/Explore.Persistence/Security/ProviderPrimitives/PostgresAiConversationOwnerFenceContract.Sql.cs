using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Persistence.Security;

public static partial class PostgresAiConversationOwnerFenceContract
{
    private static string FenceSql(SourceSql s)
    {
        string Grant(bool organization)
        {
            string member = organization ? s.T<OrganizationMember>() : s.T<GroupMember>();
            string placement = organization ? s.T<OrganizationTenant>() : s.T<GroupTenant>();
            string entity = organization ? s.T<Organization>() : s.T<Group>();
            string M(string property) => organization ? s.C<OrganizationMember>("m", property) : s.C<GroupMember>("m", property);
            string P(string property) => organization ? s.C<OrganizationTenant>("pt", property) : s.C<GroupTenant>("pt", property);
            string E(string property) => organization ? s.C<Organization>("e", property) : s.C<Group>("e", property);
            return $"""
                PERFORM {M("Id")}, {P("Id")}, {E("Id")}, {s.C<Role>("r", "Id")},
                    {s.C<RolePermission>("rp", "RoleId")}, {s.C<Permission>("p", "Id")}
                FROM {s.T<Actor>()} a
                JOIN {entity} e ON {E("Id")} = {s.C<Actor>("a", organization ? "OrganizationId" : "GroupId")}
                JOIN {placement} pt ON {P(organization ? "OrganizationId" : "GroupId")} = {E("Id")}
                JOIN {member} m ON {M(organization ? "OrganizationTenantId" : "GroupTenantId")} = {P("Id")}
                JOIN {s.T<Role>()} r ON {s.C<Role>("r", "Id")} = {M("RoleId")}
                JOIN {s.T<RolePermission>()} rp ON {s.C<RolePermission>("rp", "RoleId")} = {s.C<Role>("r", "Id")}
                JOIN {s.T<Permission>()} p ON {s.C<Permission>("p", "Id")} = {s.C<RolePermission>("rp", "PermissionId")}
                WHERE {s.C<Actor>("a", "Id")} = p_admitted
                    AND {s.C<Actor>("a", "ActorTypeId")} = {(int)(organization ? ActorTypeEnum.Organization : ActorTypeEnum.Group)}
                    AND {s.C<Actor>("a", "UserId")} IS NULL
                    AND {s.C<Actor>("a", organization ? "GroupId" : "OrganizationId")} IS NULL
                    AND NOT {E("IsDeleted")} AND {P("TenantId")} = p_tenant
                    AND NOT {P("IsDeleted")} AND NOT {P("IsSuspended")}
                    AND {P("ApprovalStatusId")} = {(int)ApprovalStatusEnum.Approved}
                    AND (NOT p_organizer OR {P("IsOrganizerEligible")})
                    AND {M("TenantId")} = p_tenant AND {M("UserId")} = p_principal AND NOT {M("IsDeleted")}
                    AND {s.C<Role>("r", "RoleScopeId")} = {(int)(organization ? RoleScopeEnum.Organization : RoleScopeEnum.Group)}
                    AND {s.C<Permission>("p", "MasterCode")} = {s.Literal<Permission>("MasterCode", PermissionCodes.EventCreate)}
                    AND {s.C<Permission>("p", "IsActive")}
                ORDER BY {E("Id")}, {P("Id")}, {M("Id")}, {s.C<Role>("r", "Id")}, {s.C<Permission>("p", "Id")}
                FOR UPDATE OF e,pt,m,r,rp,p;
                IF FOUND THEN RETURN true; END IF;
                """;
        }
        return $"""
            PERFORM {s.C<Actor>("a", "Id")} FROM {s.T<Actor>()} a
                WHERE {s.C<Actor>("a", "Id")} IN (p_actor,p_admitted)
                ORDER BY {s.C<Actor>("a", "Id")} FOR UPDATE;
            PERFORM {s.C<User>("u", "Id")} FROM {s.T<User>()} u
                WHERE {s.C<User>("u", "Id")} = p_principal FOR UPDATE;
            IF NOT FOUND THEN RETURN false; END IF;
            PERFORM {s.C<Tenant>("t", "Id")} FROM {s.T<Tenant>()} t
                WHERE {s.C<Tenant>("t", "Id")} = p_tenant FOR UPDATE;
            IF NOT FOUND THEN RETURN false; END IF;
            PERFORM {s.C<TenantStatus>("ts", "Id")} FROM {s.T<TenantStatus>()} ts
                JOIN {s.T<Tenant>()} t ON {s.C<Tenant>("t", "TenantStatusId")} = {s.C<TenantStatus>("ts", "Id")}
                WHERE {s.C<Tenant>("t", "Id")} = p_tenant FOR UPDATE OF ts;
            IF p_actor IS NOT NULL AND NOT EXISTS (SELECT 1 FROM {s.T<Actor>()} a
                WHERE {s.C<Actor>("a", "Id")} = p_actor)
            THEN RETURN false; END IF;
            IF p_privacy THEN RETURN true; END IF;
            IF NOT EXISTS (SELECT 1 FROM {s.T<User>()} u
                WHERE {s.C<User>("u", "Id")} = p_principal AND NOT {s.C<User>("u", "IsDeleted")})
                OR NOT EXISTS (SELECT 1 FROM {s.T<Tenant>()} t JOIN {s.T<TenantStatus>()} ts
                    ON {s.C<TenantStatus>("ts", "Id")} = {s.C<Tenant>("t", "TenantStatusId")}
                    WHERE {s.C<Tenant>("t", "Id")} = p_tenant
                        AND {s.C<Tenant>("t", "TenantStatusId")} = {(int)TenantStatusEnum.Active}
                        AND {s.C<TenantStatus>("ts", "IsActiveState")})
            THEN RETURN false; END IF;
            IF p_admitted IS NULL THEN RETURN true; END IF;
            PERFORM {s.C<TenantUser>("tu", "Id")} FROM {s.T<TenantUser>()} tu
                WHERE {s.C<TenantUser>("tu", "TenantId")} = p_tenant AND {s.C<TenantUser>("tu", "UserId")} = p_principal
                    AND NOT {s.C<TenantUser>("tu", "IsDeleted")}
                    AND {s.C<TenantUser>("tu", "StatusId")} = {(int)TenantUserStatusEnum.Active}
                ORDER BY {s.C<TenantUser>("tu", "Id")} FOR UPDATE;
            IF NOT FOUND OR NOT EXISTS (SELECT 1 FROM {s.T<Actor>()} a
                WHERE {s.C<Actor>("a", "Id")} = p_admitted AND NOT {s.C<Actor>("a", "IsDeleted")}
                    AND NOT {s.C<Actor>("a", "IsSuspended")} AND {s.C<Actor>("a", "ServicePrincipalId")} IS NULL
                    AND {s.C<Actor>("a", "ExternalActorSubjectId")} IS NULL)
            THEN RETURN false; END IF;
            IF EXISTS (SELECT 1 FROM {s.T<Actor>()} a WHERE {s.C<Actor>("a", "Id")} = p_admitted
                AND {s.C<Actor>("a", "ActorTypeId")} = {(int)ActorTypeEnum.User}
                AND {s.C<Actor>("a", "UserId")} = p_principal
                AND {s.C<Actor>("a", "OrganizationId")} IS NULL AND {s.C<Actor>("a", "GroupId")} IS NULL)
            THEN RETURN true; END IF;
            {Grant(true)}
            {Grant(false)}
            RETURN false;
            """;
    }
}
