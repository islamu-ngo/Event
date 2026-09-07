namespace Explore.Domain;

public sealed record AdmissionRefundLineAllocation
{
    private AdmissionRefundLineAllocation(
        Guid? registrationTicketAssignmentId,
        Guid registrationOrderLineId,
        bool isAdmissionRelevant,
        long acceptedAmountMinor,
        long refundedAmountMinor)
    {
        RegistrationTicketAssignmentId = registrationTicketAssignmentId;
        RegistrationOrderLineId = registrationOrderLineId;
        IsAdmissionRelevant = isAdmissionRelevant;
        AcceptedAmountMinor = acceptedAmountMinor;
        RefundedAmountMinor = refundedAmountMinor;
    }

    public Guid? RegistrationTicketAssignmentId { get; }
    public Guid RegistrationOrderLineId { get; }
    public bool IsAdmissionRelevant { get; }
    public long AcceptedAmountMinor { get; }
    public long RefundedAmountMinor { get; }

    public static AdmissionRefundLineAllocation Create(
        Guid? registrationTicketAssignmentId,
        Guid registrationOrderLineId,
        bool isAdmissionRelevant,
        long acceptedAmountMinor,
        long refundedAmountMinor)
    {
        if (registrationOrderLineId == Guid.Empty || registrationTicketAssignmentId == Guid.Empty ||
            (isAdmissionRelevant && registrationTicketAssignmentId is null))
        {
            throw new ArgumentException("Refund allocation identity is invalid.");
        }

        if (acceptedAmountMinor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(acceptedAmountMinor));
        }

        if (refundedAmountMinor < 0 || refundedAmountMinor > acceptedAmountMinor)
        {
            throw new ArgumentOutOfRangeException(nameof(refundedAmountMinor));
        }

        return new AdmissionRefundLineAllocation(
            registrationTicketAssignmentId,
            registrationOrderLineId,
            isAdmissionRelevant,
            acceptedAmountMinor,
            refundedAmountMinor);
    }
}
