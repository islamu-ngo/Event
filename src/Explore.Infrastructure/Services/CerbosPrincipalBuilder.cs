using Cerbos.Sdk.Builder;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Constructs the <see cref="Principal"/> sent to the Cerbos PDP for authorization checks.
/// Supports two principal flavours:
/// <list type="bullet">
/// <item><description>User principals — resolved from <see cref="IAdminContext"/> (instance-admin flag, tenant and organization memberships).</description></item>
/// <item><description>Machine principals — resolved from <see cref="IMachinePrincipalAccessor"/> with attributes projected from the API key owner type, synthesising equivalent membership maps so existing Cerbos policies evaluate consistently.</description></item>
/// </list>
/// The builder is the single translation layer: authorization services always call <see cref="BuildPrincipalAsync(System.Nullable{System.Guid}, System.Threading.CancellationToken)"/> and let the builder decide which branch to take.
/// <para>
/// Event-scoped role assignments are hydrated on demand via <see cref="EnrichWithEventAssignmentsAsync"/>
/// so Cerbos derived roles can evaluate event-team authority without a second round-trip.
/// </para>
/// </summary>
public class CerbosPrincipalBuilder
{
    internal static Cerbos.Api.V1.Engine.Principal CapturePrincipal(Principal principal) =>
        principal.ToPrincipal().Clone();

    private readonly IAdminContext _adminContext;
    private readonly IMachinePrincipalAccessor _machinePrincipalAccessor;
    private readonly IEventAuthoritySnapshotService _eventAuthoritySnapshotService;
    private readonly IHumanCerbosPrincipalReader _humanPrincipalReader;

    public CerbosPrincipalBuilder(
        IAdminContext adminContext,
        IMachinePrincipalAccessor machinePrincipalAccessor,
        IEventAuthoritySnapshotService eventAuthoritySnapshotService,
        IHumanCerbosPrincipalReader humanPrincipalReader)
    {
        _adminContext = adminContext;
        _machinePrincipalAccessor = machinePrincipalAccessor;
        _eventAuthoritySnapshotService = eventAuthoritySnapshotService;
        _humanPrincipalReader = humanPrincipalReader;
    }

    /// <summary>
    /// Dispatches to the machine or user principal builder based on whether the current request
    /// was authenticated via an external API key. Call this from authorization services; it
    /// removes the need for callers to check <see cref="IMachinePrincipalAccessor.IsMachineCaller"/>.
    /// </summary>
    /// <param name="userId">The user ID resolved from <see cref="IAdminContext"/>. May be <c>null</c> for machine callers whose API key is not owned by a user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A fully-populated Cerbos <see cref="Principal"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the caller is neither a recognised user nor a machine principal.</exception>
    public Task<Principal> BuildPrincipalAsync(Guid? userId, CancellationToken cancellationToken = default)
    {
        var machineContext = _machinePrincipalAccessor.Current;
        if (machineContext is not null)
            return BuildMachinePrincipalAsync(machineContext, cancellationToken);

        if (userId is null)
            throw new InvalidOperationException(
                "Cannot build a Cerbos principal: no user ID is available and the request is not an API-key (machine) caller.");

        return BuildSdkPrincipalAsync(userId.Value, cancellationToken);
    }

    /// <summary>
    /// Builds the original human's Cerbos principal from current native source facts.
    /// This idle observation is not authority to commit a later mutation.
    /// </summary>
    public async Task<Principal> BuildSdkPrincipalAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var facts = await _humanPrincipalReader.ReadAsync(userId, cancellationToken);
        return BuildPrincipalFromSources(facts);
    }

    public static Principal BuildPrincipalFromSources(HumanPrincipalSourceFacts facts)
    {
        bool instanceAdmin = facts.PlatformGrants.Any(grant => facts.Roles.Any(role =>
            role.Id == grant.RoleId && role.RoleScopeId == (int)RoleScopeEnum.Platform
                && role.MatchesPlatformAdministratorCode));
        var tenants = facts.TenantGrants.Where(grant => grant.RevokedAt is null
                && grant.RoleId == (int)RoleEnum.TenantAdmin
                && facts.TenantMemberships.Any(member => member.Id == grant.TenantUserId
                    && member.StatusId == (int)TenantUserStatusEnum.Active && !member.IsDeleted))
            .Select(grant => grant.TenantId).Distinct();
        var organizations = facts.Organizations.Where(member => member.RoleId == (int)RoleEnum.OrgAdmin
                && !member.IsDeleted && !member.PlacementIsDeleted && !member.OwnerIsDeleted)
            .Select(member => member.OwnerId).Distinct();
        var groups = facts.Groups.Where(member => member.RoleId == (int)RoleEnum.GroupAdmin
                && !member.IsDeleted && !member.PlacementIsDeleted && !member.OwnerIsDeleted)
            .Select(member => member.OwnerId).Distinct();
        return BuildHumanPrincipal(facts.PrincipalId, instanceAdmin, tenants, organizations, groups,
            facts.EventCreateOrganizations, facts.EventCreateGroups,
            facts.EventFinanceOrganizations, facts.EventFinanceGroups)
            .WithAttribute("groupMemberCreateScopes", AttributeValue.ListValue(MembershipScopes(mapping => mapping.MatchesGroupMemberCreateCode)))
            .WithAttribute("groupMemberUpdateScopes", AttributeValue.ListValue(MembershipScopes(mapping => mapping.MatchesGroupMemberUpdateCode)))
            .WithAttribute("groupMemberDeleteScopes", AttributeValue.ListValue(MembershipScopes(mapping => mapping.MatchesGroupMemberDeleteCode)));

        AttributeValue[] MembershipScopes(Func<HumanPrincipalPermissionFact, bool> matchesPermission) => facts.Groups
            .Where(member => member.TenantId == facts.SelectedTenantId
                && !member.IsDeleted && !member.PlacementIsDeleted && !member.OwnerIsDeleted
                && (member.RoleId == (int)RoleEnum.GroupAdmin || facts.Permissions.Any(mapping =>
                    mapping.RoleId == member.RoleId && mapping.IsActive && matchesPermission(mapping))))
            .Select(member => $"{member.TenantId:D}/{member.OwnerId:D}")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(AttributeValue.StringValue).ToArray();
    }

    private static Principal BuildHumanPrincipal(Guid userId, bool isInstanceAdmin,
        IEnumerable<Guid> adminTenantIds, IEnumerable<Guid> adminOrgIds, IEnumerable<Guid> adminGroupIds,
        IEnumerable<Guid> eventCreateOrganizationIds, IEnumerable<Guid> eventCreateGroupIds,
        IEnumerable<Guid> eventFinanceOrganizationIds, IEnumerable<Guid> eventFinanceGroupIds)
    {
        var tenantMemberships = adminTenantIds
            .ToDictionary(id => id.ToString(), _ => AttributeValue.StringValue("admin"));
        var orgMemberships = adminOrgIds
            .ToDictionary(id => id.ToString(), _ => AttributeValue.StringValue("admin"));
        var groupMemberships = adminGroupIds
            .ToDictionary(id => id.ToString(), _ => AttributeValue.StringValue("admin"));

        return Principal
            .NewInstance(userId.ToString(), "islamuevent_authenticated_user")
            .WithAttribute("userId", AttributeValue.StringValue(userId.ToString()))
            .WithAttribute("isInstanceAdmin", AttributeValue.BoolValue(isInstanceAdmin))
            .WithAttribute("tenantMemberships", AttributeValue.MapValue(tenantMemberships))
            .WithAttribute("orgMemberships", AttributeValue.MapValue(orgMemberships))
            .WithAttribute("groupMemberships", AttributeValue.MapValue(groupMemberships))
            .WithAttribute("eventCreateOrganizations", AttributeValue.ListValue(
                eventCreateOrganizationIds.Select(id => AttributeValue.StringValue(id.ToString())).ToArray()))
            .WithAttribute("eventCreateGroups", AttributeValue.ListValue(
                eventCreateGroupIds.Select(id => AttributeValue.StringValue(id.ToString())).ToArray()))
            .WithAttribute("eventFinanceOrganizations", AttributeValue.ListValue(
                eventFinanceOrganizationIds.Select(id => AttributeValue.StringValue(id.ToString())).ToArray()))
            .WithAttribute("eventFinanceGroups", AttributeValue.ListValue(
                eventFinanceGroupIds.Select(id => AttributeValue.StringValue(id.ToString())).ToArray()));
    }

    /// <summary>
    /// Builds a Cerbos SDK <see cref="Principal"/> for an API-key-authenticated machine caller.
    /// Projects owner-type authority into the same <c>isInstanceAdmin</c>, <c>tenantMemberships</c>,
    /// and <c>orgMemberships</c> shape used for human principals so existing policies are reusable.
    /// Adds machine-specific attributes (<c>is_machine</c>, <c>api_key_id</c>, <c>owner_type</c>,
    /// <c>owner_id</c>, <c>tenant_id</c>, <c>scopes</c>) so policies can tighten rules when desired.
    /// </summary>
    public async Task<Principal> BuildMachinePrincipalAsync(
        ApiKeyPrincipalContext machineContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machineContext);

        var (isInstanceAdmin, tenantMemberships, orgMemberships, groupMemberships) =
            await ResolveMachineAuthorityAsync(machineContext, cancellationToken);

        var scopeAttrs = machineContext.Scopes
            .Select(AttributeValue.StringValue)
            .ToArray();

        var membershipScopes = Array.Empty<AttributeValue>();
        if (machineContext.TenantId is { } membershipTenant)
        {
            var groups = machineContext.OwnerType switch
            {
                ExternalApiKeyOwnerType.Group => new[] { machineContext.OwnerId },
                ExternalApiKeyOwnerType.User => (await _adminContext.GetAdminGroupIdsAsync(
                    machineContext.OwnerId, membershipTenant, cancellationToken)).ToArray(),
                _ => Array.Empty<Guid>()
            };
            membershipScopes = groups.Distinct().Order()
                .Select(group => AttributeValue.StringValue($"{membershipTenant:D}/{group:D}")).ToArray();
        }

        var principal = Principal
            .NewInstance($"api_key:{machineContext.KeyId}", "islamuevent_authenticated_user")
            .WithAttribute("isInstanceAdmin", AttributeValue.BoolValue(isInstanceAdmin))
            .WithAttribute("tenantMemberships", AttributeValue.MapValue(tenantMemberships))
            .WithAttribute("orgMemberships", AttributeValue.MapValue(orgMemberships))
            .WithAttribute("groupMemberships", AttributeValue.MapValue(groupMemberships))
            .WithAttribute("groupMemberCreateScopes", AttributeValue.ListValue(membershipScopes))
            .WithAttribute("groupMemberUpdateScopes", AttributeValue.ListValue(membershipScopes))
            .WithAttribute("groupMemberDeleteScopes", AttributeValue.ListValue(membershipScopes))
            .WithAttribute("eventCreateOrganizations", AttributeValue.ListValue([]))
            .WithAttribute("eventCreateGroups", AttributeValue.ListValue([]))
            .WithAttribute("eventFinanceOrganizations", AttributeValue.ListValue([]))
            .WithAttribute("eventFinanceGroups", AttributeValue.ListValue([]))
            .WithAttribute("is_machine", AttributeValue.BoolValue(true))
            .WithAttribute("api_key_id", AttributeValue.StringValue(machineContext.KeyId))
            .WithAttribute("owner_type", AttributeValue.StringValue(machineContext.OwnerType.ToString().ToLowerInvariant()))
            .WithAttribute("owner_id", AttributeValue.StringValue(machineContext.OwnerId.ToString()))
            .WithAttribute("scopes", AttributeValue.ListValue(scopeAttrs));

        if (machineContext.TenantId is { } tenantId)
            principal = principal.WithAttribute("tenant_id", AttributeValue.StringValue(tenantId.ToString()));

        return principal;
    }

    private async Task<(
        bool IsInstanceAdmin,
        Dictionary<string, AttributeValue> TenantMemberships,
        Dictionary<string, AttributeValue> OrgMemberships,
        Dictionary<string, AttributeValue> GroupMemberships)>
        ResolveMachineAuthorityAsync(ApiKeyPrincipalContext context, CancellationToken cancellationToken)
    {
        switch (context.OwnerType)
        {
            case ExternalApiKeyOwnerType.InstanceAdmin:
                return (true, [], [], []);

            case ExternalApiKeyOwnerType.Tenant:
                var tenantMap = new Dictionary<string, AttributeValue>();
                if (context.TenantId is { } tenantTarget)
                    tenantMap[tenantTarget.ToString()] = AttributeValue.StringValue("admin");
                return (false, tenantMap, [], []);

            case ExternalApiKeyOwnerType.Organization:
                var orgMap = new Dictionary<string, AttributeValue>
                {
                    [context.OwnerId.ToString()] = AttributeValue.StringValue("admin"),
                };
                return (false, [], orgMap, []);

            case ExternalApiKeyOwnerType.Group:
                var groupMap = new Dictionary<string, AttributeValue>
                {
                    [context.OwnerId.ToString()] = AttributeValue.StringValue("admin")
                };
                return (false, [], [], groupMap);

            case ExternalApiKeyOwnerType.User:
                // Machine principal owned by a user borrows the owner's actual authority profile.
                var ownerIsInstanceAdmin = await _adminContext.IsInstanceAdminAsync(context.OwnerId, cancellationToken);
                var ownerTenantIds = await _adminContext.GetAdminTenantIdsAsync(context.OwnerId, cancellationToken);
                var ownerOrgIds = await _adminContext.GetAdminOrganizationIdsAsync(context.OwnerId, cancellationToken);
                var ownerGroupIds = await _adminContext.GetAdminGroupIdsAsync(context.OwnerId, cancellationToken);

                var userTenantMap = ownerTenantIds
                    .ToDictionary(id => id.ToString(), _ => AttributeValue.StringValue("admin"));
                var userOrgMap = ownerOrgIds
                    .ToDictionary(id => id.ToString(), _ => AttributeValue.StringValue("admin"));
                var userGroupMap = ownerGroupIds
                    .ToDictionary(id => id.ToString(), _ => AttributeValue.StringValue("admin"));
                return (ownerIsInstanceAdmin, userTenantMap, userOrgMap, userGroupMap);

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(context),
                    context.OwnerType,
                    "Unknown ExternalApiKeyOwnerType — cannot project to Cerbos authority.");
        }
    }

    public async Task EnrichWithEventAssignmentsAsync(
        Principal principal,
        Guid userId,
        Guid tenantId,
        IReadOnlyCollection<Guid> eventIds,
        CancellationToken ct)
    {
        if (eventIds.Count == 0)
            return;

        var snapshot = await _eventAuthoritySnapshotService.GetForUserAndEventsAsync(
            tenantId, userId, eventIds, DateTime.UtcNow, ct);

        if (snapshot.Events.Count == 0)
            return;

        var eventAssignments = new Dictionary<string, AttributeValue>();
        foreach (var (eventId, authority) in snapshot.Events)
        {
            var roles = authority.RoleCodes
                .Select(code => AttributeValue.StringValue(code))
                .ToArray();

            var permissions = authority.PermissionCodes
                .Select(code => AttributeValue.StringValue(code))
                .ToArray();

            eventAssignments[eventId.ToString()] = AttributeValue.MapValue(new Dictionary<string, AttributeValue>
            {
                ["tenantId"] = AttributeValue.StringValue(tenantId.ToString()),
                ["roles"] = AttributeValue.ListValue(roles),
                ["permissions"] = AttributeValue.ListValue(permissions)
            });
        }

        principal.WithAttribute("eventAssignments", AttributeValue.MapValue(eventAssignments));
        principal.WithAttribute("nowUtc", AttributeValue.StringValue(DateTime.UtcNow.ToString("o")));
    }
}
