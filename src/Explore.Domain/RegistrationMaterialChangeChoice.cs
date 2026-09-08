using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationMaterialChangeChoice : ITenantEntity, IAuditableEntity, IConcurrencyAware
{
    private RegistrationMaterialChangeChoice()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid RefundCampaignId { get; private set; }
    public Guid PaymentAttemptId { get; private set; }
    public Guid RegistrationOrderId { get; private set; }
    public Guid PaidOrderAcceptanceSnapshotId { get; private set; }
    public MaterialChangeChoiceStatusEnum Status { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public static RegistrationMaterialChangeChoice Create(
        Guid id,
        RefundCampaign campaign,
        PaymentAttempt payment,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(payment);
        if (id == Guid.Empty || campaign.Kind != RefundCampaignKind.MaterialChange ||
            campaign.TenantId != payment.TenantId || payment.PaidOrderAcceptanceSnapshotId is null ||
            createdAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A material-change choice requires matching paid campaign evidence.");
        }

        return new()
        {
            Id = id,
            TenantId = campaign.TenantId,
            RefundCampaignId = campaign.Id,
            PaymentAttemptId = payment.Id,
            RegistrationOrderId = payment.RegistrationOrderId,
            PaidOrderAcceptanceSnapshotId = payment.PaidOrderAcceptanceSnapshotId.Value,
            Status = MaterialChangeChoiceStatusEnum.Pending,
            CreatedAt = createdAt,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    public bool AcceptNewTerms(Guid actorId, DateTime decidedAt) =>
        Decide(MaterialChangeChoiceStatusEnum.AcceptedNewTerms, actorId, decidedAt);

    public bool RequestRefund(Guid actorId, DateTime decidedAt) =>
        Decide(MaterialChangeChoiceStatusEnum.RefundRequested, actorId, decidedAt);

    public bool MarkNotApplicable(DateTime observedAt)
    {
        if (observedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A material-change closure requires UTC evidence.", nameof(observedAt));
        }
        if (Status == MaterialChangeChoiceStatusEnum.NotApplicable)
        {
            return false;
        }
        if (Status != MaterialChangeChoiceStatusEnum.Pending)
        {
            throw new InvalidOperationException("A decided material-change choice cannot become inapplicable.");
        }

        Status = MaterialChangeChoiceStatusEnum.NotApplicable;
        DecidedAt = observedAt;
        UpdatedAt = observedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }

    private bool Decide(MaterialChangeChoiceStatusEnum desired, Guid actorId, DateTime decidedAt)
    {
        if (actorId == Guid.Empty || decidedAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A material-change choice requires a UTC actor decision.");
        }
        if (Status == desired)
        {
            return false;
        }
        if (Status != MaterialChangeChoiceStatusEnum.Pending)
        {
            throw new InvalidOperationException("A material-change choice cannot be contradicted.");
        }

        Status = desired;
        DecidedAt = decidedAt;
        UpdatedAt = decidedAt;
        UpdatedBy = actorId;
        ConcurrencyStamp = Guid.CreateVersion7();
        return true;
    }
}
