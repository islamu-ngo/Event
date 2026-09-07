using Explore.Domain.Enums;

namespace Explore.Domain.Services.Registration;

public static class RegistrationApprovalStatusRules
{
    public static bool IsCapacityBearing(int? approvalStatusId)
    {
        return approvalStatusId is
            (int)ApprovalStatusEnum.Pending or
            (int)ApprovalStatusEnum.Approved;
    }

    public static bool IsLiveForLocationDisclosure(int? approvalStatusId, bool isDeleted = false)
    {
        return !isDeleted && approvalStatusId is
            (int)ApprovalStatusEnum.Pending or
            (int)ApprovalStatusEnum.Approved or
            (int)ApprovalStatusEnum.Waitlisted;
    }

    public static bool IsTerminal(int? approvalStatusId)
    {
        return approvalStatusId is
            (int)ApprovalStatusEnum.Rejected or
            (int)ApprovalStatusEnum.Cancelled or
            (int)ApprovalStatusEnum.Revoked;
    }

    public static bool CanTransition(int? currentApprovalStatusId, int? desiredApprovalStatusId)
    {
        return currentApprovalStatusId == desiredApprovalStatusId
            || !IsTerminal(currentApprovalStatusId);
    }

    public static int? ResolveParentApprovalStatus(
        IEnumerable<int?> childApprovalStatusIds,
        int? noLiveChildStatusId)
    {
        var statuses = childApprovalStatusIds.ToHashSet();
        if (statuses.Contains((int)ApprovalStatusEnum.Waitlisted))
        {
            return (int)ApprovalStatusEnum.Waitlisted;
        }

        if (statuses.Contains((int)ApprovalStatusEnum.Pending))
        {
            return (int)ApprovalStatusEnum.Pending;
        }

        if (statuses.Contains((int)ApprovalStatusEnum.Approved))
        {
            return (int)ApprovalStatusEnum.Approved;
        }

        return IsTerminal(noLiveChildStatusId) ? noLiveChildStatusId : null;
    }

    public static bool PreservesRegistrationIdentity(
        Guid? originalIntentId,
        Guid? desiredIntentId,
        Guid originalUserId,
        Guid desiredUserId,
        Guid originalEventId,
        Guid desiredEventId,
        Guid originalTenantId,
        Guid desiredTenantId)
    {
        return originalIntentId == desiredIntentId
            && originalUserId == desiredUserId
            && originalEventId == desiredEventId
            && originalTenantId == desiredTenantId;
    }
}
