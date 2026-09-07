using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Pages.Admin.Components;

public sealed class PaidEventPolicyEditModel
{
    public static readonly int[] RequiredRefundProtectionIds = [1, 2, 3, 4, 5, 6, 7];

    private static readonly PaidEventPolicyOrganizerKindOption[] FixedOrganizerKindOptions =
    [
        new(1, "User"),
        new(2, "Organization"),
        new(4, "Group")
    ];

    public bool IsPaymentsEnabled { get; set; }
    public bool RequiresLocalVerification { get; set; }
    public List<int> AllowedOrganizerKindIds { get; } = [];
    public List<string> AllowedCurrencyCodes { get; } = [];
    public string? DefaultCurrencyCode { get; set; }
    public List<PaidEventPolicyCurrencyRiskLimitEditModel> CurrencyRiskLimits { get; } = [];
    public bool RequiresFirstPaidEventReview { get; set; }
    public int? FarFutureReviewThresholdDays { get; set; }
    public IReadOnlyList<PaidEventPolicyOrganizerKindOption> OrganizerKindOptions { get; private init; } = FixedOrganizerKindOptions;
    public IReadOnlyList<string> CurrencyChoices { get; private init; } = [];

    public static PaidEventPolicyEditModel FromPolicy(PaidEventPolicyDto policy) => FromPolicy(policy, activeInstanceCeiling: null);

    public static PaidEventPolicyEditModel FromTenantConfiguration(HalResourceOfTenantPaidEventPolicyConfigurationDto configuration)
    {
        PaidEventPolicyDto source = configuration.ActiveTenantOverride
            ?? configuration.EffectivePolicy
            ?? configuration.ActiveInstanceCeiling
            ?? new PaidEventPolicyDto();

        return FromPolicy(source, configuration.ActiveInstanceCeiling);
    }

    public RevisePaidEventPolicyDto ToRequest() => new()
    {
        IsPaymentsEnabled = IsPaymentsEnabled,
        AllowedOrganizerKindIds = AllowedOrganizerKindIds.ToArray(),
        RequiresLocalVerification = RequiresLocalVerification,
        AllowedCurrencyCodes = AllowedCurrencyCodes.ToArray(),
        DefaultCurrencyCode = DefaultCurrencyCode,
        RefundProtectionIds = RequiredRefundProtectionIds.ToArray(),
        CurrencyRiskLimits = AllowedCurrencyCodes.Select(ToRiskLimitRequest).ToArray(),
        RequiresFirstPaidEventReview = RequiresFirstPaidEventReview,
        FarFutureReviewThresholdDays = FarFutureReviewThresholdDays
    };

    public PaidEventPolicyValidationResult ValidateTenantNarrowing(PaidEventPolicyDto activeInstanceCeiling)
    {
        var errors = new List<string>();
        bool instancePaymentsEnabled = activeInstanceCeiling.IsPaymentsEnabled == true;
        if (IsPaymentsEnabled && !instancePaymentsEnabled)
        {
            errors.Add("Tenant paid events cannot be enabled when instance paid events are disabled.");
        }

        if (activeInstanceCeiling.RequiresLocalVerification == true && !RequiresLocalVerification)
        {
            errors.Add("Tenant local verification cannot be weaker than the instance policy.");
        }

        if (activeInstanceCeiling.RequiresFirstPaidEventReview == true && !RequiresFirstPaidEventReview)
        {
            errors.Add("Tenant first-paid-event review cannot be weaker than the instance policy.");
        }

        ValidateSubset(AllowedOrganizerKindIds, CeilingOrganizerKinds(activeInstanceCeiling), "Tenant organizer kinds must stay inside the instance policy.", errors);
        ValidateCurrencySubset(activeInstanceCeiling, errors);
        ValidateCeiling(activeInstanceCeiling.FarFutureReviewThresholdDays, FarFutureReviewThresholdDays, "Tenant far-future review threshold cannot exceed or remove the instance threshold.", errors);
        ValidateRiskLimits(activeInstanceCeiling, errors);
        ValidateRefundFloor(activeInstanceCeiling, errors);

        return new PaidEventPolicyValidationResult(errors.Count == 0, errors);
    }

    private static PaidEventPolicyEditModel FromPolicy(PaidEventPolicyDto policy, PaidEventPolicyDto? activeInstanceCeiling)
    {
        var model = new PaidEventPolicyEditModel
        {
            IsPaymentsEnabled = policy.IsPaymentsEnabled == true,
            RequiresLocalVerification = policy.RequiresLocalVerification == true,
            DefaultCurrencyCode = policy.DefaultCurrencyCode,
            RequiresFirstPaidEventReview = policy.RequiresFirstPaidEventReview == true,
            FarFutureReviewThresholdDays = policy.FarFutureReviewThresholdDays,
            OrganizerKindOptions = OrganizerOptions(activeInstanceCeiling),
            CurrencyChoices = CeilingCurrencyChoices(activeInstanceCeiling)
        };

        model.AllowedOrganizerKindIds.AddRange((policy.AllowedOrganizerKindIds ?? []).Where(IsKnownOrganizerKind));
        model.AllowedCurrencyCodes.AddRange(policy.AllowedCurrencyCodes ?? []);
        model.CurrencyRiskLimits.AddRange((policy.CurrencyRiskLimits ?? []).Select(PaidEventPolicyCurrencyRiskLimitEditModel.FromDto));
        return model;
    }

    private static IReadOnlyList<PaidEventPolicyOrganizerKindOption> OrganizerOptions(PaidEventPolicyDto? ceiling) => ceiling is null
        ? FixedOrganizerKindOptions
        : FixedOrganizerKindOptions.Where(option => CeilingOrganizerKinds(ceiling).Contains(option.Id)).ToArray();

    private static IReadOnlyList<string> CeilingCurrencyChoices(PaidEventPolicyDto? ceiling) => ceiling?.IsPaymentsEnabled == true
        ? (ceiling.AllowedCurrencyCodes ?? []).ToArray()
        : [];

    private static int[] CeilingOrganizerKinds(PaidEventPolicyDto ceiling) => (ceiling.AllowedOrganizerKindIds ?? [])
        .Where(IsKnownOrganizerKind)
        .ToArray();

    private static bool IsKnownOrganizerKind(int id) => FixedOrganizerKindOptions.Any(option => option.Id == id);

    private PaidEventPolicyCurrencyRiskLimitDto ToRiskLimitRequest(string currencyCode)
    {
        PaidEventPolicyCurrencyRiskLimitEditModel? limit = CurrencyRiskLimits.SingleOrDefault(candidate => candidate.CurrencyCode == currencyCode);
        return new PaidEventPolicyCurrencyRiskLimitDto
        {
            CurrencyCode = currencyCode,
            PerEventSalesCeilingMinor = limit?.PerEventSalesCeilingMinor,
            RollingOrganizerSalesCeilingMinor = limit?.RollingOrganizerSalesCeilingMinor,
            HighValueReviewThresholdMinor = limit?.HighValueReviewThresholdMinor
        };
    }

    private static void ValidateSubset<T>(IEnumerable<T> selected, IEnumerable<T> ceiling, string message, List<string> errors)
        where T : notnull
    {
        var allowed = ceiling.ToHashSet();
        if (selected.Any(item => !allowed.Contains(item)))
        {
            errors.Add(message);
        }
    }

    private void ValidateCurrencySubset(PaidEventPolicyDto activeInstanceCeiling, List<string> errors)
    {
        string[] expectedOrder = (activeInstanceCeiling.AllowedCurrencyCodes ?? [])
            .Where(AllowedCurrencyCodes.Contains)
            .ToArray();
        if (!AllowedCurrencyCodes.SequenceEqual(expectedOrder, StringComparer.Ordinal))
        {
            errors.Add("Tenant currencies must stay inside the instance policy and keep its order.");
        }

        if (!string.IsNullOrWhiteSpace(DefaultCurrencyCode) && !AllowedCurrencyCodes.Contains(DefaultCurrencyCode, StringComparer.Ordinal))
        {
            errors.Add("Default currency must be one of the selected currencies.");
        }
    }

    private void ValidateRiskLimits(PaidEventPolicyDto activeInstanceCeiling, List<string> errors)
    {
        foreach (PaidEventPolicyCurrencyRiskLimitEditModel limit in CurrencyRiskLimits)
        {
            if (!AllowedCurrencyCodes.Contains(limit.CurrencyCode, StringComparer.Ordinal))
            {
                errors.Add("Tenant risk limits must stay inside selected currencies.");
                break;
            }
        }

        foreach (PaidEventPolicyCurrencyRiskLimitDto ceiling in
                 activeInstanceCeiling.CurrencyRiskLimits ?? [])
        {
            if (string.IsNullOrWhiteSpace(ceiling.CurrencyCode) || !AllowedCurrencyCodes.Contains(ceiling.CurrencyCode, StringComparer.Ordinal))
            {
                continue;
            }

            PaidEventPolicyCurrencyRiskLimitEditModel? selected = CurrencyRiskLimits.SingleOrDefault(limit => limit.CurrencyCode == ceiling.CurrencyCode);
            ValidateCeiling(ceiling.PerEventSalesCeilingMinor, selected?.PerEventSalesCeilingMinor, "Tenant per-event sales ceiling cannot exceed or remove the instance ceiling.", errors);
            ValidateCeiling(ceiling.RollingOrganizerSalesCeilingMinor, selected?.RollingOrganizerSalesCeilingMinor, "Tenant rolling organizer sales ceiling cannot exceed or remove the instance ceiling.", errors);
            ValidateCeiling(ceiling.HighValueReviewThresholdMinor, selected?.HighValueReviewThresholdMinor, "Tenant high-value review threshold cannot exceed or remove the instance threshold.", errors);
        }
    }

    private static void ValidateRefundFloor(PaidEventPolicyDto activeInstanceCeiling, List<string> errors)
    {
        int[] ceilingFloor = (activeInstanceCeiling.RefundProtectionIds ?? []).ToArray();
        if (ceilingFloor.Any(id => !RequiredRefundProtectionIds.Contains(id)))
        {
            errors.Add("Tenant refund protections cannot weaken the instance refund floor.");
        }
    }

    private static void ValidateCeiling<T>(T? ceiling, T? selected, string message, List<string> errors)
        where T : struct, IComparable<T>
    {
        if (ceiling.HasValue && (!selected.HasValue || selected.Value.CompareTo(ceiling.Value) > 0))
        {
            errors.Add(message);
        }
    }
}

public sealed record PaidEventPolicyOrganizerKindOption(int Id, string Label);

public sealed class PaidEventPolicyCurrencyRiskLimitEditModel
{
    public required string CurrencyCode { get; init; }
    public long? PerEventSalesCeilingMinor { get; set; }
    public long? RollingOrganizerSalesCeilingMinor { get; set; }
    public long? HighValueReviewThresholdMinor { get; set; }

    public static PaidEventPolicyCurrencyRiskLimitEditModel FromDto(
        PaidEventPolicyCurrencyRiskLimitDto dto) => new()
    {
        CurrencyCode = dto.CurrencyCode ?? string.Empty,
        PerEventSalesCeilingMinor = dto.PerEventSalesCeilingMinor,
        RollingOrganizerSalesCeilingMinor = dto.RollingOrganizerSalesCeilingMinor,
        HighValueReviewThresholdMinor = dto.HighValueReviewThresholdMinor
    };
}

public sealed record PaidEventPolicyValidationResult(bool IsValid, IReadOnlyList<string> Errors);
