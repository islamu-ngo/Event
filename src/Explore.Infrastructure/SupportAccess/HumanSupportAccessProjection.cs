using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Application.SupportAccess;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.SupportAccess;

/// <summary>
/// Original support-context and boundary semantics over persisted facts. Call only after all
/// blocking final reads, with the original owner's trusted final UTC sample. No I/O or clock.
/// </summary>
public static class HumanSupportAccessProjection
{
    public static SupportAccessContext Context(HumanSupportAccessSourceFacts facts, DateTimeOffset nowUtc)
    {
        var forwarding = facts.Forwarding;
        if (!forwarding.WasActive)
            return forwarding.WasForwarded ? SupportAccessContext.InactiveForwarded : SupportAccessContext.Inactive;
        var settings = Settings(facts);
        var session = facts.Sessions.SingleOrDefault();
        if (!settings.Enabled || session is null || session.Id != forwarding.SessionId
            || session.ActorUserId != forwarding.Subject.PrincipalId
            || forwarding.ResolvedTenantId.HasValue && session.TargetTenantId != forwarding.ResolvedTenantId.Value
            || session.StatusId != (int)SupportAccessSessionStatusEnum.Active || session.EndedAtUtc is not null
            || session.StartedAtUtc > nowUtc || nowUtc >= session.ExpiresAtUtc
            || session.ModeId == (int)SupportAccessModeEnum.Write && !settings.AllowWriteMode)
            return SupportAccessContext.InactiveForwarded;
        return new(true, session.Id, session.ActorUserId, session.TargetTenantId, session.TargetTenantUserId,
            (SupportAccessModeEnum)session.ModeId, session.StartedAtUtc, session.ExpiresAtUtc,
            session.ReasonCode, session.TicketReference, WasForwarded: true);
    }

    public static SupportAccessSettingGroup Settings(HumanSupportAccessSourceFacts facts)
    {
        var rows = facts.Settings.ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var resolved = new Dictionary<string, ResolvedSetting>(StringComparer.Ordinal);
        foreach (string key in SupportAccessSettingGroup.SettingKeys)
        {
            rows.TryGetValue(key, out var row);
            var value = HierarchicalSettingMerge.Resolve(key, new SettingMergeFacts(
                row is null ? null : new SystemSettingFacts(row.Value, (SettingValueType)row.SettingValueTypeId,
                    row.IsLocked, row.Description, row.Category, row.AllowedValues),
                null, null, null, null, false, null));
            if (value is not null) resolved.Add(key, value);
        }
        var group = new SupportAccessSettingGroup();
        group.Populate(resolved);
        return group;
    }

    /// <summary>
    /// A boundary denial only, not permission to perform the action. Attributes must come from
    /// the parent's persisted trusted resource projection, never request/body attributes.
    /// </summary>
    public static string? BoundaryDenialReason(HumanSupportAccessSourceFacts facts, DateTimeOffset nowUtc,
        string resourceKind, string action, IReadOnlyDictionary<string, object>? trustedAttributes)
    {
        if (!IsBounded(resourceKind, trustedAttributes)) return null;
        var context = Context(facts, nowUtc);
        if (context.WasForwarded && !context.IsActive) return "support_access_inactive";
        if (!context.IsActive) return null;
        if (!context.AllowsWrites && !IsReadOnlyCompatible(action)) return "support_access_read_only";
        if (!context.TargetTenantId.HasValue || context.TargetTenantId.Value == Guid.Empty)
            return "support_access_missing_target_tenant";
        Guid tenant;
        if (trustedAttributes?.TryGetValue("tenantId", out var attribute) != true)
            return "support_access_missing_tenant_context";
        if (attribute is Guid id) tenant = id;
        else if (attribute is not string text || !Guid.TryParse(text, out tenant))
            return "support_access_missing_tenant_context";
        return tenant == context.TargetTenantId.Value ? null : "support_access_target_tenant_mismatch";
    }

    private static bool IsReadOnlyCompatible(string action) => action is
        AuthorizationActions.View or AuthorizationActions.SupportAccessSessions.List or
        AuthorizationActions.SupportAccessSessions.ViewAudit or AuthorizationActions.Events.ViewManagement or
        AuthorizationActions.StorageObjects.Download or AuthorizationActions.StorageObjects.PresignedDownload or
        AuthorizationActions.ViewSharedContacts or AuthorizationActions.ExportSharedContacts
        || action.EndsWith(":view", StringComparison.Ordinal)
        || action.EndsWith(":view-delivery", StringComparison.Ordinal);

    private static bool IsBounded(string resourceKind, IReadOnlyDictionary<string, object>? attributes)
    {
        if (attributes?.ContainsKey("tenantId") == true) return resourceKind is not ResourceKinds.SupportAccessSession;
        return resourceKind is ResourceKinds.Tenant or ResourceKinds.TenantSetting or ResourceKinds.TenantUserRoleGrant
            or ResourceKinds.Category or ResourceKinds.Tag or ResourceKinds.Location or ResourceKinds.LocationRoom
            or ResourceKinds.CustomPropertyDefinition or ResourceKinds.CustomPropertyTemplate or ResourceKinds.CustomPropertyValue
            or ResourceKinds.CustomPropertyProjection or ResourceKinds.CustomPropertyGovernance or ResourceKinds.EmailDispatch
            or ResourceKinds.Webhook or ResourceKinds.Organization or ResourceKinds.OrganizationMember
            or ResourceKinds.OrganizationReview or ResourceKinds.Group or ResourceKinds.GroupMember or ResourceKinds.Event
            or ResourceKinds.EventResource or ResourceKinds.EventSession or ResourceKinds.EventSessionGroup
            or ResourceKinds.EventSessionAgendaItem or ResourceKinds.EventDay or ResourceKinds.EventAgendaItem
            or ResourceKinds.EventContactShareConsent or ResourceKinds.StorageObject or ResourceKinds.Actor;
    }
}
