using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Model = Explore.Persistence.Security.ManagedProviderProvisioningDataModel;

namespace Explore.Persistence.Security;

/// <summary>Private SQLite compiler. PostgreSQL independently compiles the same closed request in its definer.</summary>
internal static class ManagedProviderProvisioningCompiler
{
    internal sealed record Effect(Model.Relation Relation, JsonObject? Before, JsonObject After);
    internal sealed record Plan(IReadOnlyList<Effect> Effects, JsonObject Postimage, HumanManagedProviderProvisioningRows Rows);

    internal static Plan Compile(ExploreDbContext context, Model.Request request, JsonObject original)
    {
        var effects = new List<Effect>();
        var after = (JsonObject)original.DeepClone();
        var input = request.Input;
        var seed = request.Seeds;
        string at = request.At.ToString("O", CultureInfo.InvariantCulture);
        JsonArray Rows<T>() => original[typeof(T).Name]!.AsArray();
        JsonObject? One<T>(Func<JsonObject, bool> predicate) => Rows<T>().Select(row => row!.AsObject()).SingleOrDefault(predicate);
        Guid Id(JsonObject row, string key = "Id") => Guid.Parse(row[key]!.GetValue<string>());
        string? Text(JsonObject row, string key) => row[key]?.GetValue<string>();
        bool Flag(JsonObject row, string key) => row[key]?.GetValue<bool>() == true;
        int Number(JsonObject row, string key) => row[key]!.GetValue<int>();
        JsonObject? login = One<UserExternalLogin>(row => Number(row, "AuthenticationProviderId") == request.Provider
            && Text(row, "ProviderKey") == request.Account);
        JsonObject? user = login is null ? null : One<User>(row => Id(row) == Id(login, "UserId"));
        if (login is not null && (user is null || Flag(user, "IsDeleted")))
            Fail(input.LocalIdentity is null ? "tenant_administrator_binding_invalid" : "tenant_local_administrator_unavailable");
        Guid userId = user is null ? seed.User : Id(user);
        JsonObject? actor = One<Actor>(row => Text(row, "UserId") == userId.ToString("D") && !Flag(row, "IsDeleted"));
        if (actor is not null && (Flag(actor, "IsSuspended") || Number(actor, "ActorTypeId") != (int)ActorTypeEnum.User))
            Fail("tenant_administrator_binding_invalid");
        Guid actorId = actor is null ? seed.Actor : Id(actor);
        Guid loginId = login is null ? seed.Login : Id(login);
        if (input.LocalIdentity is { } local)
        {
            var account = One<LocalIdentityUser>(row => Id(row) == local.LocalSubjectId);
            var token = One<IdentityUserToken<Guid>>(row => Id(row, "UserId") == local.LocalSubjectId
                && Text(row, "LoginProvider") == LocalCredentialStateMetadata.TokenLoginProvider
                && Text(row, "Name") == LocalCredentialStateMetadata.TokenName);
            if (account is null || token is null || new[] { "PasswordHash", "SecurityStamp", "ConcurrencyStamp" }
                .Any(key => string.IsNullOrWhiteSpace(Text(account, key)))) Fail("tenant_local_administrator_unavailable");
            LocalCredentialStateMetadata? metadata = null;
            try
            {
                metadata = JsonSerializer.Deserialize<LocalCredentialStateMetadata>(Text(token!, "Value")!, Model.Json);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                Fail("tenant_local_administrator_unavailable");
            }
            var operation = metadata is null ? null : One<LocalIdentityCredentialOperation>(row => Id(row) == metadata.OperationId);
            if (metadata is null || metadata.ApplicationUserId != local.LocalSubjectId
                || metadata.State is not (LocalCredentialState.ChangeRequired or LocalCredentialState.Ready)
                || operation is null || Id(operation, "LocalSubjectId") != local.LocalSubjectId
                || Number(operation, "Kind") is not ((int)LocalCredentialOperationKind.Create) and not ((int)LocalCredentialOperationKind.Reset)
                || Number(operation, "Stage") != (metadata.State == LocalCredentialState.Ready
                    ? (int)LocalCredentialOperationStage.Replaced : (int)LocalCredentialOperationStage.ChangeRequired)
                || user is null || actor is null || login is null || userId != local.LocalSubjectId
                || Id(operation, "PersonalActorId") != actorId || Id(operation, "ExternalLoginId") != loginId)
                Fail("tenant_local_administrator_unavailable");
        }
        var customer = One<ExternalBinding>(row => Text(row, "ProviderKey") == input.ProviderKey
            && Text(row, "ExternalSystem") == input.ExternalSystem
            && Text(row, "ExternalType") == ExternalBindingTypes.External.ProviderCustomer
            && Text(row, "ExternalId") == input.ExternalCustomerId && row["ScopeTenantId"] is null);
        if (customer is not null)
        {
            if (Text(customer, "InternalType") != ExternalBindingTypes.Internal.Tenant) Fail("tenant_provisioning_customer_conflict");
            Guid tenantId = Id(customer, "InternalId");
            if (One<Tenant>(row => Id(row) == tenantId) is null) Fail("tenant_provisioning_customer_conflict");
            Guid Bound(string type, string internalType)
            {
                var binding = One<ExternalBinding>(row => Text(row, "ProviderKey") == input.ProviderKey
                    && Text(row, "ExternalSystem") == request.IdentitySystem && Text(row, "ExternalType") == type
                    && Text(row, "ExternalId") == request.Subject && Text(row, "ScopeTenantId") == tenantId.ToString("D"));
                if (binding is null || Text(binding, "InternalType") != internalType) Fail("tenant_provisioning_binding_incomplete");
                return Id(binding!, "InternalId");
            }
            Guid boundUser = Bound(ExternalBindingTypes.External.ExternalAdminUser, ExternalBindingTypes.Internal.User);
            Guid boundActor = Bound(ExternalBindingTypes.External.ExternalAdminUserActor, ExternalBindingTypes.Internal.Actor);
            Guid boundLogin = Bound(ExternalBindingTypes.External.ExternalAdminUserLogin, ExternalBindingTypes.Internal.UserExternalLogin);
            Guid member = Bound(ExternalBindingTypes.External.ExternalAdminTenantUser, ExternalBindingTypes.Internal.TenantUser);
            Guid profile = Bound(ExternalBindingTypes.External.ExternalAdminTenantUserProfile, ExternalBindingTypes.Internal.TenantUserProfile);
            var memberRow = One<TenantUser>(row => Id(row) == member && Id(row, "TenantId") == tenantId && Id(row, "UserId") == boundUser
                && Text(row, "ActorId") == boundActor.ToString("D") && !Flag(row, "IsDeleted"));
            var profileRow = One<TenantUserProfile>(row => Id(row) == profile && Id(row, "TenantId") == tenantId && Id(row, "TenantUserId") == member);
            var grant = One<TenantUserRoleGrant>(row => Id(row, "TenantId") == tenantId && Id(row, "TenantUserId") == member
                && Number(row, "RoleScopeId") == (int)RoleScopeEnum.Tenant && row["RevokedAt"] is null
                && Rows<Role>().Any(role => role!["Id"]!.GetValue<int>() == Number(row, "RoleId")
                    && (role["MasterCode"]?.GetValue<string>() == "tenant.admin" || role["Id"]!.GetValue<int>() == (int)RoleEnum.TenantAdmin)));
            if (user is null || actor is null || login is null || boundUser != userId || boundActor != actorId
                || boundLogin != loginId || memberRow is null || profileRow is null || grant is null)
                Fail("tenant_provisioning_binding_incomplete");
            return new(effects, after, new(tenantId, userId, member, profile, actorId, loginId, Id(grant!),
                null, null, null, null, true));
        }
        if (Rows<Tenant>().Any(row => row!["Slug"]!.GetValue<string>() == input.TenantSlug))
            Fail("tenant_provisioning_slug_conflict");
        var role = One<Role>(row => Text(row, "MasterCode") == "tenant.admin" && Number(row, "RoleScopeId") == (int)RoleScopeEnum.Tenant)
            ?? One<Role>(row => Number(row, "Id") == (int)RoleEnum.TenantAdmin && Number(row, "RoleScopeId") == (int)RoleScopeEnum.Tenant);
        if (role is null) Fail("tenant_provisioning_role_unavailable");
        // Existing global anchors precede every new Tenant FK. These are explicit
        // no-op writes, not an accidental side effect of a tracked graph.
        if (user is not null) Effect<User>(user, (JsonObject)user.DeepClone());
        if (actor is not null) Effect<Actor>(actor, (JsonObject)actor.DeepClone());
        Add<Tenant>(("Id", seed.Tenant), ("FullName", input.TenantFullName), ("Slug", input.TenantSlug),
            ("Description", $"Provisioned from {input.ExternalSystem} customer {input.ExternalCustomerId} by provider {input.ProviderKey}."),
            ("TenantStatusId", input.ActivateTenant ? (int)TenantStatusEnum.Active : (int)TenantStatusEnum.Provisioning));
        Add<TenantSettingsDocument>(("Id", seed.Branding), ("TenantId", seed.Tenant), ("DocumentKey", request.BrandingKey),
            ("SchemaVersion", request.BrandingSchema), ("DefaultsVersion", request.BrandingVersion),
            ("PayloadJson", request.BrandingPayload), ("ConcurrencyStamp", seed.BrandingStamp));
        Add<TenantSettingsDocument>(("Id", seed.Directory), ("TenantId", seed.Tenant), ("DocumentKey", request.DirectoryKey),
            ("SchemaVersion", request.DirectorySchema), ("DefaultsVersion", request.DirectoryVersion),
            ("PayloadJson", request.DirectoryPayload), ("ConcurrencyStamp", seed.DirectoryStamp));
        if (user is null)
        {
            Add<User>(("Id", userId), ("EmailVerified", input.ExternalAdmin!.EmailVerified), ("ConcurrencyStamp", seed.UserStamp));
            Add<UserPii>(("UserId", userId), ("Email", input.ExternalAdmin.Email),
                ("FirstName", input.ExternalAdmin.FirstName), ("LastName", input.ExternalAdmin.LastName));
        }
        else if (input.ExternalAdmin?.EmailVerified == true && !Flag(user, "EmailVerified"))
        {
            var updated = (JsonObject)user.DeepClone();
            updated["EmailVerified"] = true;
            updated["ConcurrencyStamp"] = seed.UserStamp.ToString("D");
            updated["UpdatedAt"] = at;
            updated["UpdatedBy"] = request.Human.ToString("D");
            Effect<User>(user, updated);
        }
        if (actor is null)
        {
            Add<Actor>(("Id", actorId), ("ActorTypeId", (int)ActorTypeEnum.User), ("UserId", userId), ("ConcurrencyStamp", seed.ActorStamp));
            Add<ActorPii>(("ActorId", actorId), ("DisplayName", input.ExternalAdmin!.DisplayName));
        }
        if (login is null)
            Add<UserExternalLogin>(("Id", loginId), ("UserId", userId), ("AuthenticationProviderId", request.Provider),
                ("ProviderKey", request.Account), ("ProviderDisplayName", input.ExternalAdmin!.IdentityProvider));
        Add<TenantUser>(("Id", seed.Member), ("TenantId", seed.Tenant), ("UserId", userId), ("ActorId", actorId),
            ("StatusId", (int)TenantUserStatusEnum.Active), ("JoinedAt", request.At));
        Add<TenantUserProfile>(("Id", seed.Profile), ("TenantId", seed.Tenant), ("TenantUserId", seed.Member),
            ("DisplayNameOverride", input.ExternalAdmin?.DisplayName), ("ContactEmailOverride", input.ExternalAdmin?.Email));
        Add<TenantUserRoleGrant>(("Id", seed.Grant), ("TenantId", seed.Tenant), ("TenantUserId", seed.Member),
            ("RoleId", Number(role!, "Id")), ("RoleScopeId", (int)RoleScopeEnum.Tenant), ("GrantedAt", request.At), ("GrantedBy", userId));
        if (input.Organizer is { } organizer)
        {
            bool group = organizer.Kind == ManagedProviderOrganizerKind.Group;
            if (!Rows<Role>().Any(row => row!["Id"]!.GetValue<int>() == (group ? (int)RoleEnum.GroupAdmin : (int)RoleEnum.OrgAdmin)
                && row["RoleScopeId"]!.GetValue<int>() == (group ? (int)RoleScopeEnum.Group : (int)RoleScopeEnum.Organization)))
                Fail("tenant_provisioning_role_unavailable");
            if (group) Add<Group>(("Id", seed.Organizer), ("FullName", organizer.FullName), ("Description", organizer.Description),
                ("ConcurrencyStamp", seed.OrganizerStamp));
            else
            {
                Add<Organization>(("Id", seed.Organizer), ("WebsiteUrl", organizer.WebsiteUrl), ("ConcurrencyStamp", seed.OrganizerStamp));
                Add<OrganizationPii>(("OrganizationId", seed.Organizer), ("FullName", organizer.FullName), ("Email", organizer.Email),
                    ("Country", organizer.Country), ("City", organizer.City), ("Address", organizer.Address), ("Postcode", organizer.Postcode));
            }
            Add<Actor>(("Id", seed.OrganizerActor), ("ActorTypeId", group ? (int)ActorTypeEnum.Group : (int)ActorTypeEnum.Organization),
                (group ? "GroupId" : "OrganizationId", seed.Organizer), ("ConcurrencyStamp", seed.OrganizerActorStamp));
            Add<ActorPii>(("ActorId", seed.OrganizerActor), ("DisplayName", organizer.FullName));
            var participation = new (string, object?)[] { ("Id", seed.Participation), ("TenantId", seed.Tenant),
                (group ? "GroupId" : "OrganizationId", seed.Organizer), ("ApprovalStatusId", (int)ApprovalStatusEnum.Approved),
                ("IsVisible", true), ("IsOrganizerEligible", true), ("ApprovedAt", request.At), ("ApprovedBy", userId),
                ("ConcurrencyStamp", seed.ParticipationStamp) };
            var membership = new (string, object?)[] { ("Id", seed.Membership), ("TenantId", seed.Tenant),
                (group ? "GroupTenantId" : "OrganizationTenantId", seed.Participation), ("UserId", userId),
                ("RoleId", group ? (int)RoleEnum.GroupAdmin : (int)RoleEnum.OrgAdmin) };
            if (group) { Add<GroupTenant>(participation); Add<GroupMember>(membership); }
            else { Add<OrganizationTenant>(participation); Add<OrganizationMember>(membership); }
            Binding(seed.OrganizerBinding, input.ExternalSystem, group ? ExternalBindingTypes.External.CustomerGroup
                : ExternalBindingTypes.External.CustomerOrganization, input.ExternalCustomerId,
                group ? ExternalBindingTypes.Internal.Group : ExternalBindingTypes.Internal.Organization, seed.Organizer, seed.Tenant);
            Binding(seed.OrganizerActorBinding, input.ExternalSystem, group ? ExternalBindingTypes.External.CustomerGroupActor
                : ExternalBindingTypes.External.CustomerOrganizationActor, input.ExternalCustomerId, ExternalBindingTypes.Internal.Actor,
                seed.OrganizerActor, seed.Tenant);
        }
        Binding(seed.CustomerBinding, input.ExternalSystem, ExternalBindingTypes.External.ProviderCustomer,
            input.ExternalCustomerId, ExternalBindingTypes.Internal.Tenant, seed.Tenant, null);
        Binding(seed.UserBinding, request.IdentitySystem, ExternalBindingTypes.External.ExternalAdminUser, request.Subject,
            ExternalBindingTypes.Internal.User, userId, seed.Tenant);
        Binding(seed.MemberBinding, request.IdentitySystem, ExternalBindingTypes.External.ExternalAdminTenantUser, request.Subject,
            ExternalBindingTypes.Internal.TenantUser, seed.Member, seed.Tenant);
        Binding(seed.ProfileBinding, request.IdentitySystem, ExternalBindingTypes.External.ExternalAdminTenantUserProfile, request.Subject,
            ExternalBindingTypes.Internal.TenantUserProfile, seed.Profile, seed.Tenant);
        Binding(seed.ActorBinding, request.IdentitySystem, ExternalBindingTypes.External.ExternalAdminUserActor, request.Subject,
            ExternalBindingTypes.Internal.Actor, actorId, seed.Tenant);
        Binding(seed.LoginBinding, request.IdentitySystem, ExternalBindingTypes.External.ExternalAdminUserLogin, request.Subject,
            ExternalBindingTypes.Internal.UserExternalLogin, loginId, seed.Tenant);
        Add<EventDiscoveryRevision>(("Id", seed.Revision), ("TenantId", seed.Tenant), ("IdentityEpoch", 0L), ("DisclosureEpoch", 1L));
        if (input.ActivateTenant) after["Capacity"]!["ActiveTenants"] = checked(original["Capacity"]!["ActiveTenants"]!.GetValue<int>() + 1);
        return new(effects, after, new(seed.Tenant, userId, seed.Member, seed.Profile, actorId, loginId, seed.Grant,
            input.Organizer is null ? null : seed.Organizer, input.Organizer is null ? null : seed.OrganizerActor,
            input.Organizer?.Kind, input.Organizer is null ? null : seed.Membership, false));

        void Binding(Guid id, string system, string externalType, string externalId, string internalType, Guid internalId, Guid? tenant) =>
            Add<ExternalBinding>(("Id", id), ("ProviderKey", input.ProviderKey), ("ExternalSystem", system),
                ("ExternalType", externalType), ("ExternalId", externalId), ("InternalType", internalType), ("InternalId", internalId),
                ("ScopeTenantId", tenant), ("ExternalBindingStatusId", (int)ExternalBindingStatusEnum.Active), ("LastSeenAt", request.At));
        void Add<T>(params (string Key, object? Value)[] values)
        {
            var relation = Model.Inventory.Single(item => item.Type == typeof(T));
            var metadata = context.Model.FindEntityType(typeof(T))!;
            var row = new JsonObject();
            foreach (string column in relation.Columns)
            {
                var property = metadata.FindProperty(column)!;
                row[column] = property.IsNullable ? null : property.ClrType == typeof(bool) ? JsonValue.Create(false)
                    : property.ClrType == typeof(int) || property.ClrType.IsEnum ? JsonValue.Create(0)
                    : property.ClrType == typeof(long) ? JsonValue.Create(0L) : null;
            }
            if (relation.Columns.Contains("CreatedAt")) { row["CreatedAt"] = at; row["CreatedBy"] = request.Human.ToString("D"); }
            foreach (var value in values) row[value.Key] = value.Value is DateTime instant
                ? JsonValue.Create(instant.ToString("O", CultureInfo.InvariantCulture))
                : JsonSerializer.SerializeToNode(value.Value, Model.Json);
            Effect<T>(null, row);
        }
        void Effect<T>(JsonObject? before, JsonObject row)
        {
            var relation = Model.Inventory.Single(item => item.Type == typeof(T));
            effects.Add(new(relation, before is null ? null : (JsonObject)before.DeepClone(), row));
            var rows = after[typeof(T).Name]!.AsArray();
            var previous = rows.SingleOrDefault(candidate => JsonNode.DeepEquals(candidate![relation.Key], row[relation.Key]));
            if (previous is not null) rows.Remove(previous);
            rows.Add(row.DeepClone());
            after[typeof(T).Name] = new JsonArray(rows.OrderBy(candidate => candidate![relation.Key]!.ToJsonString(),
                StringComparer.Ordinal).Select(candidate => candidate!.DeepClone()).ToArray());
        }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string reason) => throw new BusinessRefusal(reason switch
    {
        "tenant_provisioning_slug_conflict" => new(null, "A tenant with this slug already exists.",
            "Tenant slug must be unique across managed provider provisioning requests."),
        "tenant_administrator_binding_invalid" => new(null, "External admin identity is linked to a missing user.",
            "The external login points to a user that could not be found."),
        "tenant_local_administrator_unavailable" => new(reason, "Local administrator is unavailable.",
            "An exact linked ChangeRequired or Ready Local identity is required."),
        "tenant_provisioning_binding_incomplete" => new(null, "Managed provider provisioning binding is incomplete.",
            "The existing provider customer binding is missing the tenant user state, user actor, external login, or tenant-admin role grant."),
        "tenant_provisioning_customer_conflict" => new(null, "External provider customer binding is invalid.",
            "The provider customer binding does not point to an Event tenant."),
        _ => new(reason, "Managed provider provisioning is unavailable.", "Required provisioning lookup state is unavailable.")
    });

    internal sealed class BusinessRefusal(HumanManagedProviderProvisioningRefusal refusal) : Exception(refusal.Message)
    {
        internal HumanManagedProviderProvisioningRefusal Refusal { get; } = refusal;
    }
}
