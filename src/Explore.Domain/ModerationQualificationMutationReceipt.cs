namespace Explore.Domain;

public enum ModerationQualificationSourceKind
{
    Case = 1,
    Evidence = 2,
    Reviewer = 3,
    Finding = 4,
    Policy = 5,
    QualificationPolicy = 6,
    QualificationPolicyAdoption = 7,
    NoticeDisclosure = 8,
    ContributionSelection = 9,
    ResourceGeneration = 10,
    ResourceTargetAdoption = 11,
    EventGeneration = 12,
    EventTargetAdoption = 13,
    ProfileAdoption = 14,
    ConsumerInstallation = 15,
    ConsumerMeasurementChallenge = 16,
    MeasurementIssuerEnrollment = 17,
    ConsumerMeasurementReceipt = 18,
    DeploymentSource = 19,
    DeploymentAdoption = 20,
    ConstitutionSource = 21,
    ConstitutionAdoption = 22,
    ConstitutionAvailability = 23,
    RecognitionPrivacySource = 24,
    RecognitionPrivacyAdoption = 25,
    ChildAgePolicySource = 26,
    ChildAgePolicyAdoption = 27,
    EventRightsSource = 28,
    EventRightsAdoption = 29,
    MeasuredOperationsSource = 30,
    MeasuredOperationsAcceptance = 31,
    CaseNarrativePolicySource = 32,
    CaseNarrativeAdoption = 33,
    ParticipantIncidentResponsibilitySource = 34,
    ClassificationTaxonomySource = 35,
    ClassificationTaxonomyAdoption = 36,
    ClassificationMaterialSource = 37,
    ClassificationMaterialReview = 38,
    ProfileCollectionProcedureSource = 39,
    ProfileCollectionProcedureAdoption = 40,
    ClassificationControlSelection = 41,
    ClassificationEventTargetAdoption = 42,
    OperationalBudgetSource = 43,
    OperationalBudgetAdoption = 44
}

/// <summary>Exact completed source mutation coordinates, never private evidence text or reusable permission.</summary>
public sealed class ModerationQualificationMutationReceipt
{
    private ModerationQualificationMutationReceipt()
    {
    }

    public Guid Id { get; private set; }
    public string RequestDigest { get; private set; } = string.Empty;
    public Guid HostBindingId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid TenantId { get; private set; }
    public ModerationQualificationSourceKind SourceKind { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid PrincipalId { get; private set; }
    public long ResultRevision { get; private set; }
    public long QualificationRevision { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }

    public static ModerationQualificationMutationReceipt Complete(
        Guid operationId, string requestDigest, Guid hostBindingId, Guid trustDomainId,
        Guid tenantId, ModerationQualificationSourceKind sourceKind, Guid sourceId,
        Guid principalId, long resultRevision, long qualificationRevision, DateTimeOffset completedAt)
    {
        var receipt = new ModerationQualificationMutationReceipt
        {
            Id = operationId,
            RequestDigest = requestDigest,
            HostBindingId = hostBindingId,
            TrustDomainId = trustDomainId,
            TenantId = tenantId,
            SourceKind = sourceKind,
            SourceId = sourceId,
            PrincipalId = principalId,
            ResultRevision = resultRevision,
            QualificationRevision = qualificationRevision,
            CompletedAt = completedAt.ToUniversalTime()
        };
        receipt.ValidatePersistedState();
        return receipt;
    }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(Id, nameof(Id));
        ModerationHostBinding.RequireUuidV7(HostBindingId, nameof(HostBindingId));
        ModerationHostBinding.RequireUuidV7(TrustDomainId, nameof(TrustDomainId));
        ModerationHostBinding.RequireUuidV7(TenantId, nameof(TenantId));
        ModerationHostBinding.RequireUuidV7(SourceId, nameof(SourceId));
        if (PrincipalId == Guid.Empty || !Enum.IsDefined(SourceKind)
            || string.IsNullOrWhiteSpace(RequestDigest) || RequestDigest.Length != 64
            || !RequestDigest.All(char.IsAsciiHexDigit)
            || ResultRevision < 1 || QualificationRevision < 1
            || CompletedAt == DateTimeOffset.MinValue || CompletedAt == DateTimeOffset.MaxValue
            || CompletedAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A receipt requires the exact qualified source outcome and completion instant.");
        }
    }

    public override string ToString() => nameof(ModerationQualificationMutationReceipt);
}
