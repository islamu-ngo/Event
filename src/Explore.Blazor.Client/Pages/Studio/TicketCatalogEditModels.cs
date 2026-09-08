using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Pages.Studio;

public sealed class TicketTypeEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PricingModeId { get; set; } = 2;
    public long? FixedPriceMinor { get; set; }
    public long? MinimumPriceMinor { get; set; }
    public long? SuggestedPriceMinor { get; set; }
    public int DataCollectionModeId { get; set; } = 1;
    public Guid? CapacityPoolId { get; set; }
    public int? MinimumAge { get; set; }
    public int? MaximumAge { get; set; }
    public bool RequiresGuardian { get; set; }
    public bool RequiresApproval { get; set; }
    public int? PerOrderLimit { get; set; }
    public int? PerAccountLimit { get; set; }
    public int? PerVerifiedContactLimit { get; set; }
    public int? PerBookingPartyLimit { get; set; }
    public List<TicketEntitlementEditModel> Entitlements { get; } = [];

    public bool IsValid => !string.IsNullOrWhiteSpace(Name)
        && PricingIsValid()
        && (MinimumAge is null || MaximumAge is null || MinimumAge <= MaximumAge)
        && Entitlements.Count > 0
        && Entitlements.All(item => item.IsValid);

    public static TicketTypeEditModel Create()
    {
        var model = new TicketTypeEditModel();
        model.Entitlements.Add(TicketEntitlementEditModel.Create());
        return model;
    }

    public static TicketTypeEditModel From(EventTicketTypeState state)
    {
        var model = new TicketTypeEditModel
        {
            Id = state.Id,
            Name = state.Name,
            PricingModeId = state.TicketPricingModeId,
            FixedPriceMinor = state.FixedPriceMinor,
            MinimumPriceMinor = state.MinimumPriceMinor,
            SuggestedPriceMinor = state.SuggestedPriceMinor,
            DataCollectionModeId = state.ParticipantDataCollectionModeId,
            CapacityPoolId = state.CapacityPoolId,
            MinimumAge = state.MinimumAge,
            MaximumAge = state.MaximumAge,
            RequiresGuardian = state.RequiresGuardian,
            RequiresApproval = state.RequiresApproval,
            PerOrderLimit = state.PerOrderLimit,
            PerAccountLimit = state.PerAccountLimit,
            PerVerifiedContactLimit = state.PerVerifiedContactLimit,
            PerBookingPartyLimit = state.PerBookingPartyLimit
        };
        model.Entitlements.AddRange(state.Entitlements.Select(TicketEntitlementEditModel.From));
        return model;
    }

    public void SetPricingMode(int modeId)
    {
        PricingModeId = modeId;
        FixedPriceMinor = modeId == 1 ? FixedPriceMinor : null;
        MinimumPriceMinor = modeId is 3 or 4 or 5 ? MinimumPriceMinor : null;
        SuggestedPriceMinor = modeId is 4 or 5 ? SuggestedPriceMinor : null;
    }

    public ManageEventTicketTypeDto ToRequest() => new()
    {
        Name = Name.Trim(),
        TicketPricingModeId = PricingModeId,
        FixedPriceMinor = FixedPriceMinor,
        MinimumPriceMinor = MinimumPriceMinor,
        SuggestedPriceMinor = SuggestedPriceMinor,
        ParticipantDataCollectionModeId = DataCollectionModeId,
        CapacityPoolId = CapacityPoolId,
        MinimumAge = MinimumAge,
        MaximumAge = MaximumAge,
        RequiresGuardian = RequiresGuardian,
        RequiresApproval = RequiresApproval,
        PerOrderLimit = PerOrderLimit,
        PerAccountLimit = PerAccountLimit,
        PerVerifiedContactLimit = PerVerifiedContactLimit,
        PerBookingPartyLimit = PerBookingPartyLimit,
        Entitlements = Entitlements.Select(item => item.ToRequest()).ToArray()
    };

    private bool PricingIsValid() => PricingModeId switch
    {
        1 => FixedPriceMinor > 0 && MinimumPriceMinor is null && SuggestedPriceMinor is null,
        2 => FixedPriceMinor is null && MinimumPriceMinor is null && SuggestedPriceMinor is null,
        3 => FixedPriceMinor is null && SuggestedPriceMinor is null && MinimumPriceMinor is null or >= 0,
        4 => FixedPriceMinor is null && MinimumPriceMinor is null or >= 0 && (SuggestedPriceMinor is null || SuggestedPriceMinor >= (MinimumPriceMinor ?? 0)),
        5 => FixedPriceMinor is null && MinimumPriceMinor >= 0 && SuggestedPriceMinor >= MinimumPriceMinor,
        _ => false
    };
}

public sealed class TicketEntitlementEditModel
{
    public int ScopeTypeId { get; set; } = 1;
    public Guid? EventDayId { get; set; }
    public Guid? EventSessionId { get; set; }
    public int IncludedQuantity { get; set; } = 1;
    public int SelectionRuleId { get; set; } = 1;

    public bool IsValid => IncludedQuantity > 0 && ScopeTypeId switch
    {
        1 => EventDayId is null && EventSessionId is null && SelectionRuleId == 1,
        2 => EventDayId is not null && EventSessionId is null && SelectionRuleId is 1 or 2,
        3 => EventDayId is null && EventSessionId is not null && SelectionRuleId is >= 1 and <= 4,
        _ => false
    };

    public static TicketEntitlementEditModel Create() => new();

    public static TicketEntitlementEditModel From(TicketTypeEntitlementState state) => new()
    {
        ScopeTypeId = state.EntitlementScopeTypeId,
        EventDayId = state.EventDayId,
        EventSessionId = state.EventSessionId,
        IncludedQuantity = state.IncludedQuantity,
        SelectionRuleId = state.EntitlementSelectionRuleId
    };

    public void SetScope(int scopeTypeId)
    {
        ScopeTypeId = scopeTypeId;
        EventDayId = null;
        EventSessionId = null;
        SelectionRuleId = 1;
    }

    public ManageTicketTypeEntitlementDto ToRequest() => new()
    {
        EntitlementScopeTypeId = ScopeTypeId,
        EventDayId = EventDayId,
        EventSessionId = EventSessionId,
        IncludedQuantity = IncludedQuantity,
        EntitlementSelectionRuleId = SelectionRuleId
    };
}

public sealed class CapacityPoolEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? MaximumQuantity { get; set; }
    public int HoldDurationSeconds { get; set; } = 900;
    public int HoldPolicyId { get; set; }
    public int OversellPolicyId { get; set; } = 1;
    public bool IsActive { get; set; } = true;

    public bool IsValid => !string.IsNullOrWhiteSpace(Name)
        && MaximumQuantity is null or > 0
        && HoldDurationSeconds > 0
        && HoldPolicyId is 1 or 2 or 3 or 4
        && OversellPolicyId is 1 or 2;

    public static CapacityPoolEditModel Create() => new();

    public static CapacityPoolEditModel From(EventCapacityPoolState state) => new()
    {
        Id = state.Id,
        Name = state.Name,
        MaximumQuantity = state.MaximumQuantity,
        HoldDurationSeconds = state.HoldDurationSeconds,
        HoldPolicyId = state.CapacityHoldPolicyId,
        OversellPolicyId = state.CapacityOversellPolicyId,
        IsActive = state.IsActive
    };

    public ManageEventCapacityPoolDto ToRequest() => new()
    {
        Name = Name.Trim(),
        MaximumQuantity = MaximumQuantity,
        HoldDurationSeconds = HoldDurationSeconds,
        CapacityHoldPolicyId = HoldPolicyId,
        CapacityOversellPolicyId = OversellPolicyId,
        IsActive = IsActive
    };
}
