using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Responses;

namespace Explore.Application.Features.Settings;

public sealed partial class TypedSettingsOriginal
{
    internal TypedSettingsOriginal(ApplyInstanceTenantPlanAssignmentCommand command)
    {
        PlanAssignment = command with { };
        Actor = command.AppliedByUserId;
        TenantId = command.TenantId;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApplyInstanceTenantPlanAssignmentCommand? PlanAssignment { get; }
}

/// <summary>The observed original footprint selects locks before acquisition, not authority.</summary>
public sealed record TenantPlanAssignmentSelection(
    ImmutableArray<string> OrdinaryKeys,
    bool HasEmail,
    bool HasVisitor,
    bool HasPublication,
    bool HasStorageQuota);

public interface IPreparedHumanTenantPlanAssignment : IPreparedHumanSettingsMutation
{
    TenantPlanAssignmentSelection PlanSelection { get; }
}

/// <summary>Closed plan entry on the same typed store; no caller supplied effects or audit.</summary>
public interface ITypedTenantPlanOriginalStore
{
    TenantPlanAssignmentSelection SelectPlan(TypedSettingsOriginal original);
    Task<TenantPlanAssignmentApplication> ApplyPlanAsync(TypedSettingsOriginal original,
        CancellationToken cancellationToken);
}

public sealed record TenantPlanAssignmentApplication(
    BaseCommandResponse<Guid> Response,
    ImmutableArray<SettingChangedNotification> Notifications);

/// <summary>A rejected later group must escape the physical owner before becoming a response.</summary>
public sealed class TenantPlanOriginalRejectedException(BaseCommandResponse<Guid> response) : Exception
{
    public BaseCommandResponse<Guid> Response { get; } = response;
}
