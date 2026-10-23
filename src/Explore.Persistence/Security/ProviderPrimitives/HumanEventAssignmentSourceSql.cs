using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

/// <summary>Fixed mapped source vocabulary; no caller-provided SQL or order normalization.</summary>
internal sealed class HumanEventAssignmentSourceSql(ExploreDbContext context, int eventCount, bool currentReads = false)
{
    internal static readonly (Type Type, string[] Columns)[] Sources =
    [
        (typeof(Explore.Domain.Event), ["Id", "TenantId", "ActorId", "OrganizerActorId", "EventProvenanceTypeId",
            "SubmittedByUserId", "IsDeleted", "ConcurrencyStamp"]),
        (typeof(EventRoleAssignment), ["Id", "TenantId", "EventId", "UserId", "RoleId", "Status",
            "StartsAtUtc", "ExpiresAtUtc", "RevokedAtUtc", "Version", "RevokedByUserId", "CreatedBy"]),
        (typeof(Role), ["Id", "MasterCode", "RoleScopeId", "FullName", "Description"]),
        (typeof(RolePermission), ["RoleId", "PermissionId"]),
        (typeof(Permission), ["Id", "MasterCode", "IsActive", "IsFiltered"]),
        (typeof(Actor), ["Id", "UserId", "OrganizationId", "GroupId", "IsDeleted", "IsSuspended"]),
        (typeof(TenantUser), ["Id", "TenantId", "UserId", "ActorId", "StatusId", "IsDeleted"]),
        (typeof(OrganizationMember), ["Id", "TenantId", "UserId", "OrganizationTenantId", "RoleId", "IsDeleted"]),
        (typeof(OrganizationTenant), ["Id", "TenantId", "OrganizationId", "IsDeleted", "IsSuspended",
            "IsOrganizerEligible", "ApprovalStatusId"]),
        (typeof(GroupMember), ["Id", "TenantId", "UserId", "GroupTenantId", "RoleId", "IsDeleted"]),
        (typeof(GroupTenant), ["Id", "TenantId", "GroupId", "IsDeleted", "IsSuspended", "IsOrganizerEligible", "ApprovalStatusId"]),
        (typeof(EventProvenanceType), ["Id", "MasterCode"]),
        (typeof(User), ["Id", "IsDeleted"]),
        // Writer participation only. No PII value is projected or granted.
        (typeof(UserPii), ["UserId"])
    ];

    internal HumanPrincipalSourceSql Names { get; } = new(context);
    private string Table<T>() => Names.Table(typeof(T));
    private string Col<T>(string property) => Names.Column(typeof(T), property);
    private string Fields<T>(string alias, params string[] properties) => string.Join(",", properties.Select(
        property => $"{alias}.{Col<T>(property)} AS {Names.Quote(property)}"));
    private string Current => currentReads ? " FOR UPDATE" : "";
    private string Targets(string value) => context.Database.IsNpgsql()
        ? $"{value} = ANY(@events)"
        : eventCount == 0 ? "1 = 0" : $"{value} IN ({string.Join(",", Enumerable.Range(0, eventCount).Select(i => $"@event{i}"))})";
    private string Events(string alias) => $"{alias}.{Col<Explore.Domain.Event>("TenantId")} = @tenant AND "
        + Targets($"{alias}.{Col<Explore.Domain.Event>("Id")}");
    private string Assignments(string alias) => $"{alias}.{Col<EventRoleAssignment>("TenantId")} = @tenant "
        + $"AND {alias}.{Col<EventRoleAssignment>("UserId")} = @actor AND "
        + Targets($"{alias}.{Col<EventRoleAssignment>("EventId")}");

    internal (string Name, string Query)[] TeamQueries =>
    [
        ("event_team_roles", $"SELECT {Fields<Role>("r", Sources[2].Columns)} FROM {Table<Role>()} r"),
        ("event_team_permissions", $"SELECT {Fields<Permission>("p", Sources[4].Columns)} FROM {Table<Permission>()} p"),
        ("event_team_role_permissions", $"SELECT {Fields<RolePermission>("rp", Sources[3].Columns)} FROM {Table<RolePermission>()} rp"),
        ("event_team_assignments", $"""
            SELECT {Fields<EventRoleAssignment>("a", Sources[1].Columns)} FROM {Table<EventRoleAssignment>()} a
            WHERE a.{Col<EventRoleAssignment>("TenantId")} = @tenant AND {Targets($"a.{Col<EventRoleAssignment>("EventId")}")}
            """)
    ];

    private string RolePredicate(string role) => $"""
        (EXISTS (SELECT 1 FROM {Table<EventRoleAssignment>()} a
            WHERE {Assignments("a")} AND a.{Col<EventRoleAssignment>("RoleId")} = {role}{Current})
        OR EXISTS (SELECT 1 FROM {Table<OrganizationMember>()} m
            WHERE m.{Col<OrganizationMember>("TenantId")} = @tenant AND m.{Col<OrganizationMember>("UserId")} = @actor
                AND m.{Col<OrganizationMember>("RoleId")} = {role}{Current})
        OR EXISTS (SELECT 1 FROM {Table<GroupMember>()} m
            WHERE m.{Col<GroupMember>("TenantId")} = @tenant AND m.{Col<GroupMember>("UserId")} = @actor
                AND m.{Col<GroupMember>("RoleId")} = {role}{Current}))
        """;

    internal string[] Queries =>
    [
        $"SELECT {Fields<Explore.Domain.Event>("e", Sources[0].Columns)} FROM {Table<Explore.Domain.Event>()} e WHERE {Events("e")}",
        $"SELECT {Fields<EventRoleAssignment>("a", Sources[1].Columns)} FROM {Table<EventRoleAssignment>()} a WHERE {Assignments("a")}",
        $"SELECT {Fields<Role>("r", "Id", "MasterCode")} FROM {Table<Role>()} r WHERE {RolePredicate($"r.{Col<Role>("Id")}")}",
        $"""
        SELECT {Fields<RolePermission>("rp", "RoleId", "PermissionId")}, {Fields<Permission>("p", "MasterCode", "IsActive")}
        FROM {Table<RolePermission>()} rp JOIN {Table<Permission>()} p ON p.{Col<Permission>("Id")} = rp.{Col<RolePermission>("PermissionId")}
        WHERE {RolePredicate($"rp.{Col<RolePermission>("RoleId")}")}
        """,
        $"""
        SELECT {Fields<Actor>("a", Sources[5].Columns)} FROM {Table<Actor>()} a
        WHERE EXISTS (SELECT 1 FROM {Table<Explore.Domain.Event>()} e WHERE {Events("e")}
            AND (e.{Col<Explore.Domain.Event>("ActorId")} = a.{Col<Actor>("Id")}
                OR e.{Col<Explore.Domain.Event>("OrganizerActorId")} = a.{Col<Actor>("Id")}){Current})
        """,
        $"""
        SELECT {Fields<TenantUser>("m", "Id", "ActorId", "StatusId", "IsDeleted")} FROM {Table<TenantUser>()} m
        WHERE m.{Col<TenantUser>("TenantId")} = @tenant AND m.{Col<TenantUser>("UserId")} = @actor
        """,
        Members(typeof(OrganizationMember), typeof(OrganizationTenant), "OrganizationTenantId", "OrganizationId"),
        Members(typeof(GroupMember), typeof(GroupTenant), "GroupTenantId", "GroupId"),
        $"""
        SELECT {Fields<EventProvenanceType>("p", "Id", "MasterCode")} FROM {Table<EventProvenanceType>()} p
        WHERE EXISTS (SELECT 1 FROM {Table<Explore.Domain.Event>()} e WHERE {Events("e")}
            AND e.{Col<Explore.Domain.Event>("EventProvenanceTypeId")} = p.{Col<EventProvenanceType>("Id")}{Current})
        """
    ];

    private string Members(Type member, Type placement, string placementKey, string ownerKey)
    {
        string M(string property) => $"m.{Names.Column(member, property)}";
        string P(string property) => $"p.{Names.Column(placement, property)}";
        string Field(string value, string name) => $"{value} AS {Names.Quote(name)}";
        return $"""
            SELECT {Field(M("Id"), "Id")}, {Field(P("Id"), "PlacementId")}, {Field(P(ownerKey), "OwnerId")},
                {Field(M("RoleId"), "RoleId")}, {Field(M("IsDeleted"), "IsDeleted")},
                {Field(P("IsDeleted"), "PlacementIsDeleted")}, {Field(P("IsSuspended"), "IsSuspended")},
                {Field(P("IsOrganizerEligible"), "IsOrganizerEligible")}, {Field(P("ApprovalStatusId"), "ApprovalStatusId")}
            FROM {Names.Table(member)} m JOIN {Names.Table(placement)} p
                ON {P("Id")} = {M(placementKey)} AND {P("TenantId")} = {M("TenantId")}
            WHERE {M("TenantId")} = @tenant AND {M("UserId")} = @actor
            """;
    }
}
