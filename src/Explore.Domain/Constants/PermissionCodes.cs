namespace Explore.Domain.Constants;

/// <summary>
/// Well-known permission codes following the {resource_kind}:{action} format.
/// These match the Permission.MasterCode values stored in the database.
/// </summary>
public static class PermissionCodes
{
    // ===== Event =====
    public const string EventView = "event:view";
    public const string EventCreate = "event:create";
    public const string EventUpdate = "event:update";
    public const string EventDelete = "event:delete";
    public const string EventPublish = "event:publish";
    public const string EventApprovePublish = "event:approve-publish";
    public const string EventManageTeam = "event:manage-team";
    public const string EventManageOwner = "event:manage-owner";
    public const string EventTransferOwnership = "event:transfer-ownership";
    public const string EventManageFinance = "event:manage-finance";
    public const string EventManagePublicActions = "event:manage-public-actions";
    public const string EventManageTickets = "event:manage-tickets";
    public const string EventViewOrganizerClaims = "event:view-organizer-claims";
    public const string EventReviewOrganizerClaim = "event:review-organizer-claim";

    // ===== Event Day =====
    public const string EventDayCreate = "event_day:create";
    public const string EventDayUpdate = "event_day:update";
    public const string EventDayDelete = "event_day:delete";

    // ===== Event Agenda Item =====
    public const string EventAgendaItemCreate = "event_agenda_item:create";
    public const string EventAgendaItemUpdate = "event_agenda_item:update";
    public const string EventAgendaItemDelete = "event_agenda_item:delete";

    // ===== Event Registration =====
    public const string EventRegistrationView = "event_registration:view";
    public const string EventRegistrationManage = "event_registration:manage";

    // ===== Event Check-in =====
    public const string EventCheckInView = "event_check_in:view";
    public const string EventCheckInManage = "event_check_in:manage";

    // ===== Organization =====
    public const string OrganizationManage = "organization:manage";
    public const string OrganizationUpdate = "organization:update";
    public const string OrganizationDelete = "organization:delete";

    // ===== Organization Member =====
    public const string OrganizationMemberCreate = "organization_member:create";
    public const string OrganizationMemberUpdate = "organization_member:update";
    public const string OrganizationMemberDelete = "organization_member:delete";

    // ===== Group =====
    public const string GroupManage = "group:manage";
    public const string GroupUpdate = "group:update";
    public const string GroupDelete = "group:delete";

    // ===== Group Member =====
    public const string GroupMemberCreate = "group_member:create";
    public const string GroupMemberUpdate = "group_member:update";
    public const string GroupMemberDelete = "group_member:delete";

    // ===== Event Session =====
    public const string EventSessionCreate = "event_session:create";
    public const string EventSessionUpdate = "event_session:update";
    public const string EventSessionDelete = "event_session:delete";
}
