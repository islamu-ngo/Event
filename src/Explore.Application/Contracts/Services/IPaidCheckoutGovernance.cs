namespace Explore.Application.Contracts.Services;

public interface IPaidCheckoutGovernance
{
    string ComplaintOwner { get; }
    string RefundOwner { get; }
    string DisputeOwner { get; }
    string ReconciliationOwner { get; }
    string ActivationStatus { get; }
    string RefundPolicyLanguageTag { get; }
    string StatementDescriptor { get; }
    string ChargeType { get; }
    bool IsConfigured { get; }
    bool IsActivated { get; }
}

public sealed class PaidCheckoutGovernanceOptions : IPaidCheckoutGovernance
{
    public const string SectionName = "Payments:CheckoutGovernance";

    public string ComplaintOwner { get; set; } = string.Empty;
    public string RefundOwner { get; set; } = string.Empty;
    public string DisputeOwner { get; set; } = string.Empty;
    public string ReconciliationOwner { get; set; } = string.Empty;
    public string ActivationStatus { get; set; } = "suspended";
    public string RefundPolicyLanguageTag { get; set; } = string.Empty;
    public string StatementDescriptor { get; set; } = string.Empty;
    public string ChargeType { get; set; } = "direct-charge";

    public bool IsConfigured => IsComplete();
    public bool IsActivated => IsComplete() && string.Equals(ActivationStatus, "approved", StringComparison.OrdinalIgnoreCase);

    public bool IsComplete() =>
        !string.IsNullOrWhiteSpace(ComplaintOwner)
        && !string.IsNullOrWhiteSpace(RefundOwner)
        && !string.IsNullOrWhiteSpace(DisputeOwner)
        && !string.IsNullOrWhiteSpace(ReconciliationOwner)
        && (ActivationStatus.Equals("approved", StringComparison.OrdinalIgnoreCase)
            || ActivationStatus.Equals("suspended", StringComparison.OrdinalIgnoreCase))
        && !string.IsNullOrWhiteSpace(RefundPolicyLanguageTag)
        && !string.IsNullOrWhiteSpace(StatementDescriptor)
        && ChargeType.Equals("direct-charge", StringComparison.Ordinal);
}
