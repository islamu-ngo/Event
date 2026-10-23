using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Model = Explore.Persistence.Security.ManagedProviderProvisioningDataModel;

namespace Explore.Persistence.Security;

/// <summary>Fixed relational projections shared by native installation and SQLite reads.</summary>
internal sealed class ManagedProviderProvisioningSql(ExploreDbContext context, bool postgres)
{
    internal readonly HumanPrincipalSourceSql Names = new(context);
    internal string Table(Type type) => Names.Table(type);
    internal string Column(Type type, string name) => Names.Column(type, name);
    internal static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    internal string Value(string path) => postgres
        ? "p_request #>> '{" + path.Replace('.', ',') + "}'"
        : "json_extract(@request," + Literal("$." + path) + ")";
    private string GuidColumn(Type type, string name, string alias = "r") =>
        $"lower(CAST({alias}.{Column(type, name)} AS text))";
    private string Scalar<T>(string result, string condition) =>
        $"(SELECT {GuidColumn(typeof(T), result, "q")} FROM {Table(typeof(T))} q WHERE {condition})";
    private string Binding(string external, string system, string subject, string result = "InternalId") =>
        Scalar<ExternalBinding>(result,
            $"q.{Column(typeof(ExternalBinding), "ProviderKey")} = {Value("Input.ProviderKey")} AND "
            + $"q.{Column(typeof(ExternalBinding), "ExternalSystem")} = {system} AND "
            + $"q.{Column(typeof(ExternalBinding), "ExternalType")} = {Literal(external)} AND "
            + $"q.{Column(typeof(ExternalBinding), "ExternalId")} = {subject} AND "
            + (external == ExternalBindingTypes.External.ProviderCustomer
                ? $"q.{Column(typeof(ExternalBinding), "ScopeTenantId")} IS NULL"
                : $"{GuidColumn(typeof(ExternalBinding), "ScopeTenantId", "q")} = {Tenant}"));
    internal string Customer => Binding(ExternalBindingTypes.External.ProviderCustomer,
        Value("Input.ExternalSystem"), Value("Input.ExternalCustomerId"));
    internal string Tenant => $"COALESCE({Customer},{Value("Seeds.Tenant")})";
    internal string User => $"COALESCE({Scalar<UserExternalLogin>("UserId",
        $"q.{Column(typeof(UserExternalLogin), "AuthenticationProviderId")} = CAST({Value("Provider")} AS integer) AND "
        + $"q.{Column(typeof(UserExternalLogin), "ProviderKey")} = {Value("Account")}")},{Value("Seeds.User")})";
    internal string Organizer => $"COALESCE({Binding(ExternalBindingTypes.External.CustomerOrganization,
        Value("Input.ExternalSystem"), Value("Input.ExternalCustomerId"))},"
        + $"{Binding(ExternalBindingTypes.External.CustomerGroup, Value("Input.ExternalSystem"), Value("Input.ExternalCustomerId"))},"
        + $"{Value("Seeds.Organizer")})";

    internal string Predicate(Type type)
    {
        string C(string name) => "r." + Column(type, name);
        string G(string name) => GuidColumn(type, name);
        if (type == typeof(Tenant)) return $"{G("Id")} = {Tenant} OR {C("Slug")} = {Value("Input.TenantSlug")}";
        if (type == typeof(User)) return $"{G("Id")} = {User}";
        if (type == typeof(UserPii)) return $"{G("UserId")} = {User}";
        if (type == typeof(UserExternalLogin))
            return $"{C("AuthenticationProviderId")} = CAST({Value("Provider")} AS integer) AND {C("ProviderKey")} = {Value("Account")}";
        if (type == typeof(Actor)) return $"{G("UserId")} = {User} OR {G("OrganizationId")} = {Organizer} OR {G("GroupId")} = {Organizer}";
        if (type == typeof(ActorPii)) return $"{G("ActorId")} IN (SELECT {GuidColumn(typeof(Actor), "Id", "a")} FROM {Table(typeof(Actor))} a "
            + $"WHERE {GuidColumn(typeof(Actor), "UserId", "a")} = {User} OR {GuidColumn(typeof(Actor), "OrganizationId", "a")} = {Organizer} "
            + $"OR {GuidColumn(typeof(Actor), "GroupId", "a")} = {Organizer})";
        if (type == typeof(Organization) || type == typeof(Group)) return $"{G("Id")} = {Organizer}";
        if (type == typeof(OrganizationPii)) return $"{G("OrganizationId")} = {Organizer}";
        if (type == typeof(ExternalBinding)) return $"{C("ProviderKey")} = {Value("Input.ProviderKey")} AND ("
            + $"{G("ScopeTenantId")} = {Tenant} OR {C("ScopeTenantId")} IS NULL AND "
            + $"{C("ExternalSystem")} = {Value("Input.ExternalSystem")} AND "
            + $"{C("ExternalType")} = {Literal(ExternalBindingTypes.External.ProviderCustomer)} AND {C("ExternalId")} = {Value("Input.ExternalCustomerId")})";
        if (type == typeof(Role)) return $"{C("Id")} IN ({(int)RoleEnum.TenantAdmin},{(int)RoleEnum.OrgAdmin},{(int)RoleEnum.GroupAdmin})"
            + $" OR {C("MasterCode")} = 'tenant.admin'";
        if (type == typeof(LocalIdentityUser)) return $"{G("Id")} = {Value("Input.LocalIdentity.LocalSubjectId")}";
        if (type == typeof(IdentityUserToken<Guid>)) return $"{G("UserId")} = {Value("Input.LocalIdentity.LocalSubjectId")}"
            + $" AND {C("LoginProvider")} = {Literal(LocalCredentialStateMetadata.TokenLoginProvider)}"
            + $" AND {C("Name")} = {Literal(LocalCredentialStateMetadata.TokenName)}";
        if (type == typeof(LocalIdentityCredentialOperation)) return $"{G("LocalSubjectId")} = {Value("Input.LocalIdentity.LocalSubjectId")}";
        if (type == typeof(TenantSettingsDocument) || type == typeof(TenantUser) || type == typeof(TenantUserProfile)
            || type == typeof(TenantUserRoleGrant) || type == typeof(OrganizationTenant) || type == typeof(GroupTenant)
            || type == typeof(OrganizationMember) || type == typeof(GroupMember) || type == typeof(EventDiscoveryRevision))
            return $"{G("TenantId")} = {Tenant}";
        throw new InvalidOperationException("provisioning_data_projection_inventory_drift");
    }

    internal string Projection(Model.Relation relation, string alias = "r")
    {
        var metadata = context.Model.FindEntityType(relation.Type)!;
        return "pg_catalog.jsonb_build_object(" + string.Join(",", relation.Columns.Select(name =>
        {
            var property = metadata.FindProperty(name)!;
            string column = alias + "." + Column(relation.Type, name);
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
                column = $"pg_catalog.to_char({column} AT TIME ZONE 'UTC','YYYY-MM-DD\"T\"HH24:MI:SS.US') || '0Z'";
            return Literal(name) + "," + column;
        })) + ")";
    }

    internal string Select(Model.Relation relation) =>
        $"SELECT {string.Join(",", relation.Columns.Select(name => "r." + Column(relation.Type, name)))} "
        + $"FROM {Table(relation.Type)} r WHERE {Predicate(relation.Type)} ORDER BY r.{Column(relation.Type, relation.Key)}";

    internal string JsonProjection => "pg_catalog.jsonb_build_object(" + string.Join(",", Model.Inventory.Select(relation =>
        Literal(relation.Type.Name) + $",(SELECT COALESCE(pg_catalog.jsonb_agg({Projection(relation)} "
        + $"ORDER BY r.{Column(relation.Type, relation.Key)}),'[]'::jsonb) FROM {Table(relation.Type)} r WHERE {Predicate(relation.Type)})")) + ")";

    internal string CapacitySelect
    {
        get
        {
            string C<T>(string name) => Column(typeof(T), name);
            string T<T>() => Table(typeof(T));
            var status = context.Model.FindEntityType(typeof(ManagedTenantProvisioningOperation))!
                .FindProperty(nameof(ManagedTenantProvisioningOperation.Status))!.GetRelationalTypeMapping();
            return $"""
                SELECT COALESCE((SELECT CASE WHEN {C<InstanceBootstrapState>("Status")} = {(int)InstanceBootstrapStatus.Completed}
                    THEN {C<InstanceBootstrapState>("DeploymentMode")} ELSE {(int)DeploymentMode.SingleTenant} END
                    FROM {T<InstanceBootstrapState>()} ORDER BY {C<InstanceBootstrapState>("Generation")} DESC,
                        {C<InstanceBootstrapState>("CreatedAt")} DESC,{C<InstanceBootstrapState>("Id")} DESC LIMIT 1),{(int)DeploymentMode.SingleTenant}),
                    (SELECT count(*) FROM {T<Tenant>()} t JOIN {T<TenantStatus>()} s ON t.{C<Tenant>("TenantStatusId")} = s.{C<TenantStatus>("Id")}
                        WHERE s.{C<TenantStatus>("IsActiveState")} = {(postgres ? "true" : "1")}),
                    (SELECT count(*) FROM {T<ManagedTenantProvisioningOperation>()}
                        WHERE {C<ManagedTenantProvisioningOperation>("Status")} IN ({status.GenerateSqlLiteral(ManagedTenantProvisioningStatus.Pending)},{status.GenerateSqlLiteral(ManagedTenantProvisioningStatus.Processing)}))
                """;
        }
    }
}
