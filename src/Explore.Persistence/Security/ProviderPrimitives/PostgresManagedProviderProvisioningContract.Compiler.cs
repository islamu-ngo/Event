using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Microsoft.EntityFrameworkCore;
using Model = Explore.Persistence.Security.ManagedProviderProvisioningDataModel;

namespace Explore.Persistence.Security;

public static partial class PostgresManagedProviderProvisioningContract
{
    private static string ProgramBody(ExploreDbContext context)
    {
        string Seed(string name) => "p_request #> '{Seeds," + name + "}'";
        string Input(string path) => "p_request #> '{Input," + path.Replace('.', ',') + "}'";
        string Row<T>(params (string Key, string Value)[] selected)
        {
            var relation = Model.Inventory.Single(item => item.Type == typeof(T));
            var metadata = context.Model.FindEntityType(typeof(T))!;
            if (selected.Select(pair => pair.Key).Distinct(StringComparer.Ordinal).Count() != selected.Length
                || selected.Any(pair => !relation.Columns.Contains(pair.Key, StringComparer.Ordinal)))
                throw new InvalidOperationException("human_provisioning_compiler_column_drift");
            return "pg_catalog.jsonb_build_object(" + string.Join(",", relation.Columns.Select(column =>
            {
                string? value = selected.SingleOrDefault(pair => pair.Key == column).Value;
                var property = metadata.FindProperty(column)!;
                value ??= column == "CreatedAt" ? "p_request->'At'" : column == "CreatedBy" ? "p_request->'Human'"
                    : property.IsNullable ? "NULL" : property.ClrType == typeof(bool) ? "false"
                    : property.ClrType == typeof(int) || property.ClrType == typeof(long) || property.ClrType.IsEnum ? "0" : "NULL";
                return Text(column) + "," + value;
            })) + ")";
        }
        string Effect<T>(string row, string before = "NULL")
        {
            var relation = Model.Inventory.Single(item => item.Type == typeof(T));
            return $"""
                new_row := {row};
                effects := effects || pg_catalog.jsonb_build_array(pg_catalog.jsonb_build_object(
                    'Type',{Text(typeof(T).Name)},'Before',{before},'After',new_row));
                expected := pg_catalog.jsonb_set(expected,ARRAY[{Text(typeof(T).Name)}],
                    (SELECT COALESCE(pg_catalog.jsonb_agg(r ORDER BY r->>{Text(relation.Key)}),'[]'::jsonb)
                        FROM (SELECT r FROM pg_catalog.jsonb_array_elements(expected->{Text(typeof(T).Name)}) r
                            WHERE r->{Text(relation.Key)} IS DISTINCT FROM new_row->{Text(relation.Key)}
                            UNION ALL SELECT new_row) rows(r)));
                """;
        }
        string Add<T>(params (string, string)[] values) => Effect<T>(Row<T>(values));
        string Binding(string id, string system, string externalType, string externalId, string internalType, string internalId, string tenant) =>
            Add<ExternalBinding>(("Id", Seed(id)), ("ProviderKey", Input("ProviderKey")), ("ExternalSystem", system),
                ("ExternalType", Text(externalType)), ("ExternalId", externalId), ("InternalType", Text(internalType)),
                ("InternalId", internalId), ("ScopeTenantId", tenant), ("ExternalBindingStatusId", ((int)ExternalBindingStatusEnum.Active).ToString()),
                ("LastSeenAt", "p_request->'At'"));
        string Result(bool replay) => $"""
            pg_catalog.jsonb_build_object('TenantId',tenant_id,'UserId',user_id,'TenantUserId',member_id,
                'TenantUserProfileId',profile_id,'UserActorId',actor_id,'UserExternalLoginId',login_id,
                'TenantUserRoleGrantId',grant_id,'OrganizerId',{(replay ? "NULL" : "CASE WHEN has_organizer THEN p_request#>'{Seeds,Organizer}' ELSE NULL END")},
                'OrganizerActorId',{(replay ? "NULL" : "CASE WHEN has_organizer THEN p_request#>'{Seeds,OrganizerActor}' ELSE NULL END")},
                'OrganizerKind',{(replay ? "NULL" : "p_request#>'{Input,Organizer,Kind}'")},
                'OrganizerMembershipId',{(replay ? "NULL" : "CASE WHEN has_organizer THEN p_request#>'{Seeds,Membership}' ELSE NULL END")},
                'AlreadyProvisioned',{(replay ? "true" : "false")})
            """;
        string ReplayBinding(string variable, string externalType, string internalType) => $$"""
            SELECT r INTO binding FROM pg_catalog.jsonb_array_elements(image->'ExternalBinding') r
                WHERE r->>'ProviderKey' = p_request#>>'{Input,ProviderKey}' AND r->>'ExternalSystem' = p_request->>'IdentitySystem'
                    AND r->>'ExternalType' = {{Text(externalType)}} AND r->>'ExternalId' = p_request->>'Subject'
                    AND r->>'ScopeTenantId' = tenant_id;
            IF binding IS NULL OR binding->>'InternalType' <> {{Text(internalType)}}
            THEN RAISE EXCEPTION 'tenant_provisioning_binding_incomplete' USING ERRCODE = '22023'; END IF;
            {{variable}} := binding->>'InternalId';
            """;
        string participation(bool group) => group
            ? Add<GroupTenant>(("Id", Seed("Participation")), ("TenantId", Seed("Tenant")), ("GroupId", Seed("Organizer")),
                ("ApprovalStatusId", ((int)ApprovalStatusEnum.Approved).ToString()), ("IsVisible", "true"),
                ("IsOrganizerEligible", "true"), ("ApprovedAt", "p_request->'At'"), ("ApprovedBy", "user_id"),
                ("ConcurrencyStamp", Seed("ParticipationStamp")))
            : Add<OrganizationTenant>(("Id", Seed("Participation")), ("TenantId", Seed("Tenant")), ("OrganizationId", Seed("Organizer")),
                ("ApprovalStatusId", ((int)ApprovalStatusEnum.Approved).ToString()), ("IsVisible", "true"),
                ("IsOrganizerEligible", "true"), ("ApprovedAt", "p_request->'At'"), ("ApprovedBy", "user_id"),
                ("ConcurrencyStamp", Seed("ParticipationStamp")));
        return $$"""
            DECLARE effects jsonb := '[]'::jsonb; expected jsonb := image; new_row jsonb; login_row jsonb; user_row jsonb;
                actor_row jsonb; local_row jsonb; token_row jsonb; metadata jsonb; operation_row jsonb; customer jsonb;
                binding jsonb; role_row jsonb; grant_row jsonb; result jsonb;
                user_id text; actor_id text; login_id text; tenant_id text; member_id text; profile_id text; grant_id text;
                bound_user text; bound_actor text; bound_login text; has_organizer boolean; is_group boolean;
            BEGIN
                IF current_user <> '{{OwnerRole}}'
                THEN RAISE EXCEPTION 'human_provisioning_private_compiler_required' USING ERRCODE = '42501'; END IF;
                {{RequestGuard}}
                SELECT r INTO login_row FROM pg_catalog.jsonb_array_elements(image->'UserExternalLogin') r
                    WHERE r->'AuthenticationProviderId' = p_request->'Provider' AND r->'ProviderKey' = p_request->'Account';
                SELECT r INTO user_row FROM pg_catalog.jsonb_array_elements(image->'User') r WHERE r->'Id' = login_row->'UserId';
                IF login_row IS NOT NULL AND (user_row IS NULL OR (user_row->>'IsDeleted')::boolean)
                THEN
                    IF p_request#>>'{Input,LocalIdentity,LocalSubjectId}' IS NOT NULL
                    THEN RAISE EXCEPTION 'tenant_local_administrator_unavailable' USING ERRCODE = '22023'; END IF;
                    RAISE EXCEPTION 'tenant_administrator_binding_invalid' USING ERRCODE = '22023';
                END IF;
                user_id := COALESCE(user_row->>'Id',p_request#>>'{Seeds,User}');
                SELECT r INTO actor_row FROM pg_catalog.jsonb_array_elements(image->'Actor') r
                    WHERE r->>'UserId' = user_id AND NOT (r->>'IsDeleted')::boolean;
                IF (SELECT count(*) FROM pg_catalog.jsonb_array_elements(image->'Actor') r
                        WHERE r->>'UserId' = user_id AND NOT (r->>'IsDeleted')::boolean) > 1
                    OR actor_row IS NOT NULL AND ((actor_row->>'IsSuspended')::boolean OR actor_row->>'ActorTypeId' <> '{{(int)ActorTypeEnum.User}}')
                THEN RAISE EXCEPTION 'tenant_administrator_binding_invalid' USING ERRCODE = '22023'; END IF;
                actor_id := COALESCE(actor_row->>'Id',p_request#>>'{Seeds,Actor}');
                login_id := COALESCE(login_row->>'Id',p_request#>>'{Seeds,Login}');
                IF p_request#>>'{Input,LocalIdentity,LocalSubjectId}' IS NOT NULL THEN
                    SELECT r INTO local_row FROM pg_catalog.jsonb_array_elements(image->'LocalIdentityUser') r
                        WHERE r->>'Id' = p_request#>>'{Input,LocalIdentity,LocalSubjectId}';
                    SELECT r INTO token_row FROM pg_catalog.jsonb_array_elements(image->'IdentityUserToken`1') r
                        WHERE r->>'UserId' = p_request#>>'{Input,LocalIdentity,LocalSubjectId}'
                            AND r->>'LoginProvider' = 'Explore.LocalIdentity' AND r->>'Name' = 'CredentialState';
                    IF local_row IS NULL OR token_row IS NULL OR NULLIF(local_row->>'PasswordHash','') IS NULL
                        OR NULLIF(local_row->>'SecurityStamp','') IS NULL OR NULLIF(local_row->>'ConcurrencyStamp','') IS NULL
                    THEN RAISE EXCEPTION 'tenant_local_administrator_unavailable' USING ERRCODE = '22023'; END IF;
                    metadata := (token_row->>'Value')::jsonb;
                    SELECT r INTO operation_row FROM pg_catalog.jsonb_array_elements(image->'LocalIdentityCredentialOperation') r
                        WHERE r->'Id' = metadata->'OperationId';
                    IF metadata->>'Version' IS DISTINCT FROM '1' OR NOT COALESCE(metadata->>'State' IN ('2','3'),false)
                        OR ARRAY(SELECT key FROM pg_catalog.jsonb_object_keys(metadata) key ORDER BY key COLLATE "C")
                            IS DISTINCT FROM ARRAY['ApplicationUserId','OperationId','State','Version']::text[]
                        OR pg_catalog.jsonb_typeof(metadata->'Version') IS DISTINCT FROM 'number'
                        OR pg_catalog.jsonb_typeof(metadata->'State') IS DISTINCT FROM 'number'
                        OR pg_catalog.jsonb_typeof(metadata->'OperationId') IS DISTINCT FROM 'string'
                        OR pg_catalog.jsonb_typeof(metadata->'ApplicationUserId') IS DISTINCT FROM 'string'
                        OR metadata->>'ApplicationUserId' IS DISTINCT FROM user_id OR operation_row IS NULL
                        OR operation_row->>'LocalSubjectId' IS DISTINCT FROM user_id OR operation_row->>'Kind' NOT IN ('1','2')
                        OR operation_row->>'Stage' IS DISTINCT FROM (CASE metadata->>'State' WHEN '2' THEN '2' ELSE '3' END)
                        OR user_id IS DISTINCT FROM p_request#>>'{Input,LocalIdentity,LocalSubjectId}'
                        OR user_row IS NULL OR actor_row IS NULL OR login_row IS NULL
                        OR operation_row->>'PersonalActorId' IS DISTINCT FROM actor_id OR operation_row->>'ExternalLoginId' IS DISTINCT FROM login_id
                    THEN RAISE EXCEPTION 'tenant_local_administrator_unavailable' USING ERRCODE = '22023'; END IF;
                END IF;
                SELECT r INTO customer FROM pg_catalog.jsonb_array_elements(image->'ExternalBinding') r
                    WHERE r->'ProviderKey' = p_request#>'{Input,ProviderKey}' AND r->'ExternalSystem' = p_request#>'{Input,ExternalSystem}'
                        AND r->>'ExternalType' = {{Text(ExternalBindingTypes.External.ProviderCustomer)}}
                        AND r->'ExternalId' = p_request#>'{Input,ExternalCustomerId}' AND r->'ScopeTenantId' = 'null'::jsonb;
                IF customer IS NOT NULL THEN
                    tenant_id := customer->>'InternalId';
                    IF customer->>'InternalType' <> {{Text(ExternalBindingTypes.Internal.Tenant)}}
                        OR NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(image->'Tenant') r WHERE r->>'Id' = tenant_id)
                    THEN RAISE EXCEPTION 'tenant_provisioning_customer_conflict' USING ERRCODE = '22023'; END IF;
                    {{ReplayBinding("bound_user", ExternalBindingTypes.External.ExternalAdminUser, ExternalBindingTypes.Internal.User)}}
                    {{ReplayBinding("bound_actor", ExternalBindingTypes.External.ExternalAdminUserActor, ExternalBindingTypes.Internal.Actor)}}
                    {{ReplayBinding("bound_login", ExternalBindingTypes.External.ExternalAdminUserLogin, ExternalBindingTypes.Internal.UserExternalLogin)}}
                    {{ReplayBinding("member_id", ExternalBindingTypes.External.ExternalAdminTenantUser, ExternalBindingTypes.Internal.TenantUser)}}
                    {{ReplayBinding("profile_id", ExternalBindingTypes.External.ExternalAdminTenantUserProfile, ExternalBindingTypes.Internal.TenantUserProfile)}}
                    SELECT r INTO grant_row FROM pg_catalog.jsonb_array_elements(image->'TenantUserRoleGrant') r
                        WHERE r->>'TenantId' = tenant_id AND r->>'TenantUserId' = member_id AND r->>'RoleScopeId' = '{{(int)RoleScopeEnum.Tenant}}'
                            AND r->'RevokedAt' = 'null'::jsonb AND EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(image->'Role') role
                                WHERE role->'Id' = r->'RoleId' AND (role->>'MasterCode' = 'tenant.admin' OR role->>'Id' = '{{(int)RoleEnum.TenantAdmin}}'));
                    IF user_row IS NULL OR actor_row IS NULL OR login_row IS NULL OR grant_row IS NULL
                        OR bound_user IS DISTINCT FROM user_id OR bound_actor IS DISTINCT FROM actor_id OR bound_login IS DISTINCT FROM login_id
                        OR NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(image->'TenantUser') r
                            WHERE r->>'Id' = member_id AND r->>'TenantId' = tenant_id AND r->>'UserId' = user_id
                                AND r->>'ActorId' = actor_id AND NOT (r->>'IsDeleted')::boolean)
                        OR NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(image->'TenantUserProfile') r
                            WHERE r->>'Id' = profile_id AND r->>'TenantId' = tenant_id AND r->>'TenantUserId' = member_id)
                    THEN RAISE EXCEPTION 'tenant_provisioning_binding_incomplete' USING ERRCODE = '22023'; END IF;
                    grant_id := grant_row->>'Id';
                    RETURN pg_catalog.jsonb_build_object('Plan',effects,'Data',expected,'Rows',{{Result(true)}});
                END IF;
                IF EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(image->'Tenant') r WHERE r->'Slug' = p_request#>'{Input,TenantSlug}')
                THEN RAISE EXCEPTION 'tenant_provisioning_slug_conflict' USING ERRCODE = '22023'; END IF;
                SELECT r INTO role_row FROM pg_catalog.jsonb_array_elements(image->'Role') r
                    WHERE r->>'RoleScopeId' = '{{(int)RoleScopeEnum.Tenant}}' AND
                        (r->>'MasterCode' = 'tenant.admin' OR r->>'Id' = '{{(int)RoleEnum.TenantAdmin}}')
                    ORDER BY CASE WHEN r->>'MasterCode' = 'tenant.admin' THEN 0 ELSE 1 END LIMIT 1;
                IF role_row IS NULL THEN RAISE EXCEPTION 'tenant_provisioning_role_unavailable' USING ERRCODE = '22023'; END IF;
                IF user_row IS NOT NULL THEN {{Effect<User>("user_row", "user_row")}} END IF;
                IF actor_row IS NOT NULL THEN {{Effect<Actor>("actor_row", "actor_row")}} END IF;
                {{Add<Tenant>(("Id", Seed("Tenant")), ("FullName", Input("TenantFullName")), ("Slug", Input("TenantSlug")),
                    ("Description", "'Provisioned from ' || (p_request#>>'{Input,ExternalSystem}') || ' customer ' || (p_request#>>'{Input,ExternalCustomerId}') || ' by provider ' || (p_request#>>'{Input,ProviderKey}') || '.'"),
                    ("TenantStatusId", $"CASE WHEN (p_request#>>'{{Input,ActivateTenant}}')::boolean THEN {(int)TenantStatusEnum.Active} ELSE {(int)TenantStatusEnum.Provisioning} END"))}}
                {{Add<TenantSettingsDocument>(("Id", Seed("Branding")), ("TenantId", Seed("Tenant")), ("DocumentKey", "p_request->'BrandingKey'"),
                    ("SchemaVersion", "p_request->'BrandingSchema'"), ("DefaultsVersion", "p_request->'BrandingVersion'"),
                    ("PayloadJson", "p_request->'BrandingPayload'"), ("ConcurrencyStamp", Seed("BrandingStamp")))}}
                {{Add<TenantSettingsDocument>(("Id", Seed("Directory")), ("TenantId", Seed("Tenant")), ("DocumentKey", "p_request->'DirectoryKey'"),
                    ("SchemaVersion", "p_request->'DirectorySchema'"), ("DefaultsVersion", "p_request->'DirectoryVersion'"),
                    ("PayloadJson", "p_request->'DirectoryPayload'"), ("ConcurrencyStamp", Seed("DirectoryStamp")))}}
                IF user_row IS NULL THEN
                    {{Add<User>(("Id", "user_id"), ("EmailVerified", Input("ExternalAdmin.EmailVerified")), ("ConcurrencyStamp", Seed("UserStamp")))}}
                    {{Add<UserPii>(("UserId", "user_id"), ("Email", Input("ExternalAdmin.Email")),
                        ("FirstName", Input("ExternalAdmin.FirstName")), ("LastName", Input("ExternalAdmin.LastName")))}}
                ELSIF p_request#>>'{Input,ExternalAdmin,EmailVerified}' = 'true' AND COALESCE(user_row->>'EmailVerified','false') <> 'true' THEN
                    {{Effect<User>("user_row || pg_catalog.jsonb_build_object('EmailVerified',true,'ConcurrencyStamp',p_request#>'{Seeds,UserStamp}',"
                        + "'UpdatedAt',p_request->'At','UpdatedBy',p_request->'Human')", "user_row")}}
                END IF;
                IF actor_row IS NULL THEN
                    {{Add<Actor>(("Id", "actor_id"), ("ActorTypeId", ((int)ActorTypeEnum.User).ToString()), ("UserId", "user_id"), ("ConcurrencyStamp", Seed("ActorStamp")))}}
                    {{Add<ActorPii>(("ActorId", "actor_id"), ("DisplayName", Input("ExternalAdmin.DisplayName")))}}
                END IF;
                IF login_row IS NULL THEN
                    {{Add<UserExternalLogin>(("Id", "login_id"), ("UserId", "user_id"), ("AuthenticationProviderId", "p_request->'Provider'"),
                        ("ProviderKey", "p_request->'Account'"), ("ProviderDisplayName", Input("ExternalAdmin.IdentityProvider")))}}
                END IF;
                {{Add<TenantUser>(("Id", Seed("Member")), ("TenantId", Seed("Tenant")), ("UserId", "user_id"), ("ActorId", "actor_id"),
                    ("StatusId", ((int)TenantUserStatusEnum.Active).ToString()), ("JoinedAt", "p_request->'At'"))}}
                {{Add<TenantUserProfile>(("Id", Seed("Profile")), ("TenantId", Seed("Tenant")), ("TenantUserId", Seed("Member")),
                    ("DisplayNameOverride", Input("ExternalAdmin.DisplayName")), ("ContactEmailOverride", Input("ExternalAdmin.Email")))}}
                {{Add<TenantUserRoleGrant>(("Id", Seed("Grant")), ("TenantId", Seed("Tenant")), ("TenantUserId", Seed("Member")),
                    ("RoleId", "role_row->'Id'"), ("RoleScopeId", ((int)RoleScopeEnum.Tenant).ToString()), ("GrantedAt", "p_request->'At'"), ("GrantedBy", "user_id"))}}
                has_organizer := p_request#>>'{Input,Organizer,Kind}' IS NOT NULL;
                is_group := p_request#>>'{Input,Organizer,Kind}' = '2';
                IF has_organizer THEN
                    IF NOT EXISTS (SELECT 1 FROM pg_catalog.jsonb_array_elements(image->'Role') r
                        WHERE (r->>'Id')::integer = CASE WHEN is_group THEN {{(int)RoleEnum.GroupAdmin}} ELSE {{(int)RoleEnum.OrgAdmin}} END
                            AND (r->>'RoleScopeId')::integer = CASE WHEN is_group THEN {{(int)RoleScopeEnum.Group}} ELSE {{(int)RoleScopeEnum.Organization}} END)
                    THEN RAISE EXCEPTION 'tenant_provisioning_role_unavailable' USING ERRCODE = '22023'; END IF;
                    IF is_group THEN
                        {{Add<Group>(("Id", Seed("Organizer")), ("FullName", Input("Organizer.FullName")), ("Description", Input("Organizer.Description")),
                            ("ConcurrencyStamp", Seed("OrganizerStamp")))}}
                    ELSE
                        {{Add<Organization>(("Id", Seed("Organizer")), ("WebsiteUrl", Input("Organizer.WebsiteUrl")), ("ConcurrencyStamp", Seed("OrganizerStamp")))}}
                        {{Add<OrganizationPii>(("OrganizationId", Seed("Organizer")), ("FullName", Input("Organizer.FullName")), ("Email", Input("Organizer.Email")),
                            ("Country", Input("Organizer.Country")), ("City", Input("Organizer.City")), ("Address", Input("Organizer.Address")), ("Postcode", Input("Organizer.Postcode")))}}
                    END IF;
                    {{Add<Actor>(("Id", Seed("OrganizerActor")), ("ActorTypeId", $"CASE WHEN is_group THEN {(int)ActorTypeEnum.Group} ELSE {(int)ActorTypeEnum.Organization} END"),
                        ("GroupId", $"CASE WHEN is_group THEN {Seed("Organizer")} ELSE NULL END"),
                        ("OrganizationId", $"CASE WHEN NOT is_group THEN {Seed("Organizer")} ELSE NULL END"), ("ConcurrencyStamp", Seed("OrganizerActorStamp")))}}
                    {{Add<ActorPii>(("ActorId", Seed("OrganizerActor")), ("DisplayName", Input("Organizer.FullName")))}}
                    IF is_group THEN
                        {{participation(true)}}
                        {{Add<GroupMember>(("Id", Seed("Membership")), ("TenantId", Seed("Tenant")), ("GroupTenantId", Seed("Participation")),
                            ("UserId", "user_id"), ("RoleId", ((int)RoleEnum.GroupAdmin).ToString()))}}
                        {{Binding("OrganizerBinding", Input("ExternalSystem"), ExternalBindingTypes.External.CustomerGroup,
                            Input("ExternalCustomerId"), ExternalBindingTypes.Internal.Group, Seed("Organizer"), Seed("Tenant"))}}
                        {{Binding("OrganizerActorBinding", Input("ExternalSystem"), ExternalBindingTypes.External.CustomerGroupActor,
                            Input("ExternalCustomerId"), ExternalBindingTypes.Internal.Actor, Seed("OrganizerActor"), Seed("Tenant"))}}
                    ELSE
                        {{participation(false)}}
                        {{Add<OrganizationMember>(("Id", Seed("Membership")), ("TenantId", Seed("Tenant")), ("OrganizationTenantId", Seed("Participation")),
                            ("UserId", "user_id"), ("RoleId", ((int)RoleEnum.OrgAdmin).ToString()))}}
                        {{Binding("OrganizerBinding", Input("ExternalSystem"), ExternalBindingTypes.External.CustomerOrganization,
                            Input("ExternalCustomerId"), ExternalBindingTypes.Internal.Organization, Seed("Organizer"), Seed("Tenant"))}}
                        {{Binding("OrganizerActorBinding", Input("ExternalSystem"), ExternalBindingTypes.External.CustomerOrganizationActor,
                            Input("ExternalCustomerId"), ExternalBindingTypes.Internal.Actor, Seed("OrganizerActor"), Seed("Tenant"))}}
                    END IF;
                END IF;
                {{Binding("CustomerBinding", Input("ExternalSystem"), ExternalBindingTypes.External.ProviderCustomer,
                    Input("ExternalCustomerId"), ExternalBindingTypes.Internal.Tenant, Seed("Tenant"), "NULL")}}
                {{Binding("UserBinding", "p_request->'IdentitySystem'", ExternalBindingTypes.External.ExternalAdminUser,
                    "p_request->'Subject'", ExternalBindingTypes.Internal.User, "user_id", Seed("Tenant"))}}
                {{Binding("MemberBinding", "p_request->'IdentitySystem'", ExternalBindingTypes.External.ExternalAdminTenantUser,
                    "p_request->'Subject'", ExternalBindingTypes.Internal.TenantUser, Seed("Member"), Seed("Tenant"))}}
                {{Binding("ProfileBinding", "p_request->'IdentitySystem'", ExternalBindingTypes.External.ExternalAdminTenantUserProfile,
                    "p_request->'Subject'", ExternalBindingTypes.Internal.TenantUserProfile, Seed("Profile"), Seed("Tenant"))}}
                {{Binding("ActorBinding", "p_request->'IdentitySystem'", ExternalBindingTypes.External.ExternalAdminUserActor,
                    "p_request->'Subject'", ExternalBindingTypes.Internal.Actor, "actor_id", Seed("Tenant"))}}
                {{Binding("LoginBinding", "p_request->'IdentitySystem'", ExternalBindingTypes.External.ExternalAdminUserLogin,
                    "p_request->'Subject'", ExternalBindingTypes.Internal.UserExternalLogin, "login_id", Seed("Tenant"))}}
                {{Add<EventDiscoveryRevision>(("Id", Seed("Revision")), ("TenantId", Seed("Tenant")), ("IdentityEpoch", "0"), ("DisclosureEpoch", "1"))}}
                IF (p_request#>>'{Input,ActivateTenant}')::boolean THEN
                    expected := pg_catalog.jsonb_set(expected,ARRAY['Capacity','ActiveTenants'],
                        pg_catalog.to_jsonb((image#>>'{Capacity,ActiveTenants}')::integer + 1));
                END IF;
                tenant_id := p_request#>>'{Seeds,Tenant}'; member_id := p_request#>>'{Seeds,Member}';
                profile_id := p_request#>>'{Seeds,Profile}'; grant_id := p_request#>>'{Seeds,Grant}';
                RETURN pg_catalog.jsonb_build_object('Plan',effects,'Data',expected,'Rows',{{Result(false)}});
            END;
            """;
    }
}
