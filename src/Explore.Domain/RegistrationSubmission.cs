using System.Security.Cryptography;
using System.Text;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationSubmission : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private readonly List<RegistrationSubmissionRevision> _revisions = [];

    private RegistrationSubmission()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationOrderId { get; private set; }
    public Guid RegistrationWorkflowId { get; private set; }
    public Guid RegistrationRequirementId { get; private set; }
    public Guid RegistrationChannelId { get; private set; }
    public Guid RegistrationFormId { get; private set; }
    public Guid RegistrationFormVersionId { get; private set; }
    public Guid RegistrationAttemptId { get; private set; }
    public int AttemptStatusAtReceiptId { get; private set; }
    public string BusinessDeduplicationKey { get; private set; } = string.Empty;
    public RegistrationEvidenceHash ReceivedEvidenceHash { get; private set; } = null!;
    public RegistrationTransportIdempotencyHash? HttpIdempotencyKeyHash { get; private set; }
    public Guid? RegistrationProviderBindingId { get; private set; }
    public RegistrationEvidenceHash? ProviderMappingRevisionHash { get; private set; }
    public string? ProviderSubmissionId { get; private set; }
    public string? ProviderResponseRevision { get; private set; }
    public string? ProviderSubjectId { get; private set; }
    public string? ProviderCorrelationId { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public DateTime? FinalizedAt { get; private set; }
    public Guid? AttemptConsumptionClaimId { get; private set; }
    public bool IsFinalizable { get; private set; }
    public int StatusId { get; private set; }
    public RegistrationSubmissionStatus? Status { get; private set; }
    public IReadOnlyCollection<RegistrationSubmissionRevision> Revisions => _revisions.AsReadOnly();
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public static RegistrationSubmission Create(
        RegistrationAttempt attempt,
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime receivedAt,
        RegistrationTransportIdempotencyHash? httpIdempotencyKeyHash,
        string? providerSubmissionId,
        string? providerSubjectId,
        string? providerCorrelationId)
    {
        if (providerSubmissionId is not null || providerSubjectId is not null || providerCorrelationId is not null)
        {
            throw new ArgumentException("Use provider submission APIs for provider evidence.");
        }

        return CreateNativeEvidenceOnly(attempt, receivedEvidenceHash, receivedAt, httpIdempotencyKeyHash);
    }

    public static RegistrationSubmission Create(
        Guid id,
        RegistrationAttempt attempt,
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime receivedAt,
        RegistrationTransportIdempotencyHash? httpIdempotencyKeyHash,
        string? providerSubmissionId,
        string? providerSubjectId,
        string? providerCorrelationId)
    {
        if (providerSubmissionId is not null || providerSubjectId is not null || providerCorrelationId is not null)
        {
            throw new ArgumentException("Use provider submission APIs for provider evidence.");
        }

        return CreateNative(id, attempt, receivedEvidenceHash, receivedAt, httpIdempotencyKeyHash, null, false);
    }

    public static RegistrationSubmission CreateNativeEvidenceOnly(
        RegistrationAttempt attempt,
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime receivedAt,
        RegistrationTransportIdempotencyHash? httpIdempotencyKeyHash) => CreateNative(
        Guid.CreateVersion7(), attempt, receivedEvidenceHash, receivedAt, httpIdempotencyKeyHash, null, false);

    internal static RegistrationSubmission CreateAcceptedNative(
        RegistrationAttempt attempt,
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime receivedAt,
        RegistrationTransportIdempotencyHash? httpIdempotencyKeyHash,
        Guid consumptionClaimId) => CreateNative(
        Guid.CreateVersion7(), attempt, receivedEvidenceHash, receivedAt, httpIdempotencyKeyHash, consumptionClaimId, true);

    public static RegistrationSubmission CreateProviderEvidenceOnly(
        RegistrationAttempt attempt,
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime receivedAt,
        RegistrationTransportIdempotencyHash? httpIdempotencyKeyHash,
        string providerSubmissionId,
        string providerResponseRevision,
        string? providerSubjectId,
        string? providerCorrelationId) => CreateProvider(
        Guid.CreateVersion7(), attempt, receivedEvidenceHash, receivedAt, httpIdempotencyKeyHash, providerSubmissionId,
        providerResponseRevision, providerSubjectId, providerCorrelationId, null, false);

    internal static RegistrationSubmission CreateAcceptedProvider(
        RegistrationAttempt attempt,
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime receivedAt,
        RegistrationTransportIdempotencyHash? httpIdempotencyKeyHash,
        string providerSubmissionId,
        string providerResponseRevision,
        string? providerSubjectId,
        string? providerCorrelationId,
        Guid consumptionClaimId) => CreateProvider(
        Guid.CreateVersion7(), attempt, receivedEvidenceHash, receivedAt, httpIdempotencyKeyHash, providerSubmissionId,
        providerResponseRevision, providerSubjectId, providerCorrelationId, consumptionClaimId, true);

    internal static RegistrationSubmission CreateAcceptedHeadlessProvider(
        RegistrationAttempt attempt,
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime receivedAt,
        RegistrationTransportIdempotencyHash? httpIdempotencyKeyHash,
        Guid consumptionClaimId)
    {
        ValidateBase(Guid.CreateVersion7(), attempt, receivedEvidenceHash, receivedAt, out DateTime timestamp);
        EnsureProviderAttempt(attempt);
        return CreateCore(
            Guid.CreateVersion7(), attempt, receivedEvidenceHash, timestamp, httpIdempotencyKeyHash,
            attempt.RegistrationProviderBindingId, attempt.ProviderMappingRevisionHash,
            null, null, null, null, consumptionClaimId, true);
    }

    internal static void ValidateAcceptedNative(RegistrationAttempt attempt, RegistrationEvidenceHash receivedEvidenceHash, DateTime receivedAt)
    {
        ValidateBase(Guid.CreateVersion7(), attempt, receivedEvidenceHash, receivedAt, out _);
        EnsureNativeAttempt(attempt);
    }

    internal static void ValidateAcceptedProvider(RegistrationAttempt attempt, RegistrationEvidenceHash receivedEvidenceHash, DateTime receivedAt, string providerSubmissionId, string providerResponseRevision)
    {
        ValidateBase(Guid.CreateVersion7(), attempt, receivedEvidenceHash, receivedAt, out _);
        EnsureProviderAttempt(attempt);
        _ = NormalizeEvidence(providerSubmissionId, nameof(providerSubmissionId));
        _ = NormalizeEvidence(providerResponseRevision, nameof(providerResponseRevision));
    }

    public void Finalize(RegistrationAttempt attempt, DateTime finalizedAt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        DateTime timestamp = EnsureUtc(finalizedAt, nameof(finalizedAt));
        if (StatusId != (int)RegistrationSubmissionStatusEnum.Received || !IsFinalizable || AttemptConsumptionClaimId is null)
        {
            throw new InvalidOperationException("Only received finalizable registration evidence can be finalized.");
        }

        if (attempt.Id != RegistrationAttemptId || attempt.StatusId != (int)RegistrationAttemptStatusEnum.Consumed ||
            attempt.SubmissionConsumptionClaimId != AttemptConsumptionClaimId)
        {
            throw new InvalidOperationException("Submission finalization requires the current consumed attempt fence.");
        }

        if (timestamp < ReceivedAt)
        {
            throw new ArgumentException("Finalization cannot predate evidence receipt.", nameof(finalizedAt));
        }

        StatusId = (int)RegistrationSubmissionStatusEnum.Finalized;
        FinalizedAt = timestamp;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public RegistrationSubmissionRevision AddRevision(
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime receivedAt,
        string? providerRevisionId)
    {
        ArgumentNullException.ThrowIfNull(receivedEvidenceHash);
        DateTime timestamp = EnsureUtc(receivedAt, nameof(receivedAt));
        if (StatusId != (int)RegistrationSubmissionStatusEnum.Received)
        {
            throw new InvalidOperationException("Only received, unsettled registration submissions can be revised.");
        }

        DateTime latestReceipt = _revisions.Count == 0 ? ReceivedAt : _revisions.Max(revision => revision.ReceivedAt);
        if (timestamp <= latestReceipt)
        {
            throw new InvalidOperationException("Submission revisions must be received after the current evidence version.");
        }

        RegistrationSubmissionRevision revision = RegistrationSubmissionRevision.Create(
            this,
            _revisions.Count + 1,
            receivedEvidenceHash,
            timestamp,
            providerRevisionId);
        _revisions.Add(revision);
        ConcurrencyStamp = Guid.CreateVersion7();
        return revision;
    }

    private static RegistrationSubmission CreateNative(
        Guid id,
        RegistrationAttempt attempt,
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime receivedAt,
        RegistrationTransportIdempotencyHash? httpIdempotencyKeyHash,
        Guid? consumptionClaimId,
        bool accepted)
    {
        ValidateBase(id, attempt, receivedEvidenceHash, receivedAt, out DateTime timestamp);
        EnsureNativeAttempt(attempt);
        return CreateCore(id, attempt, receivedEvidenceHash, timestamp, httpIdempotencyKeyHash, null, null, null, null, null, null, consumptionClaimId, accepted && consumptionClaimId.HasValue);
    }

    private static RegistrationSubmission CreateProvider(
        Guid id,
        RegistrationAttempt attempt,
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime receivedAt,
        RegistrationTransportIdempotencyHash? httpIdempotencyKeyHash,
        string providerSubmissionId,
        string providerResponseRevision,
        string? providerSubjectId,
        string? providerCorrelationId,
        Guid? consumptionClaimId,
        bool accepted)
    {
        ValidateBase(id, attempt, receivedEvidenceHash, receivedAt, out DateTime timestamp);
        EnsureProviderAttempt(attempt);

        string normalizedSubmissionId = NormalizeEvidence(providerSubmissionId, nameof(providerSubmissionId))!;
        string normalizedRevision = NormalizeEvidence(providerResponseRevision, nameof(providerResponseRevision))!;
        return CreateCore(
            id,
            attempt,
            receivedEvidenceHash,
            timestamp,
            httpIdempotencyKeyHash,
            attempt.RegistrationProviderBindingId,
            attempt.ProviderMappingRevisionHash,
            normalizedSubmissionId,
            normalizedRevision,
            NormalizeEvidence(providerSubjectId, nameof(providerSubjectId)),
            NormalizeEvidence(providerCorrelationId, nameof(providerCorrelationId)),
            consumptionClaimId,
            accepted && consumptionClaimId.HasValue);
    }

    private static RegistrationSubmission CreateCore(
        Guid id,
        RegistrationAttempt attempt,
        RegistrationEvidenceHash receivedEvidenceHash,
        DateTime timestamp,
        RegistrationTransportIdempotencyHash? httpIdempotencyKeyHash,
        Guid? providerBindingId,
        RegistrationEvidenceHash? providerMappingRevisionHash,
        string? providerSubmissionId,
        string? providerResponseRevision,
        string? providerSubjectId,
        string? providerCorrelationId,
        Guid? consumptionClaimId,
        bool isFinalizable) => new()
        {
            Id = id,
            TenantId = attempt.TenantId,
            EventId = attempt.EventId,
            RegistrationOrderId = attempt.RegistrationOrderId,
            RegistrationWorkflowId = attempt.RegistrationWorkflowId,
            RegistrationRequirementId = attempt.RegistrationRequirementId,
            RegistrationChannelId = attempt.RegistrationChannelId,
            RegistrationFormId = attempt.RegistrationFormId,
            RegistrationFormVersionId = attempt.RegistrationFormVersionId,
            RegistrationAttemptId = attempt.Id,
            AttemptStatusAtReceiptId = attempt.StatusId,
            BusinessDeduplicationKey = CreateBusinessDeduplicationKey(attempt, receivedEvidenceHash, providerBindingId, providerMappingRevisionHash, providerSubmissionId, providerResponseRevision),
            ReceivedEvidenceHash = receivedEvidenceHash,
            HttpIdempotencyKeyHash = httpIdempotencyKeyHash,
            RegistrationProviderBindingId = providerBindingId,
            ProviderMappingRevisionHash = providerMappingRevisionHash,
            ProviderSubmissionId = providerSubmissionId,
            ProviderResponseRevision = providerResponseRevision,
            ProviderSubjectId = providerSubjectId,
            ProviderCorrelationId = providerCorrelationId,
            ReceivedAt = timestamp,
            CreatedAt = timestamp,
            AttemptConsumptionClaimId = consumptionClaimId,
            IsFinalizable = isFinalizable,
            StatusId = isFinalizable ? (int)RegistrationSubmissionStatusEnum.Received : (int)RegistrationSubmissionStatusEnum.EvidenceOnly
        };

    private static void ValidateBase(Guid id, RegistrationAttempt attempt, RegistrationEvidenceHash receivedEvidenceHash, DateTime receivedAt, out DateTime timestamp)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(receivedEvidenceHash);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Submission identity is required.", nameof(id));
        }

        timestamp = EnsureUtc(receivedAt, nameof(receivedAt));
        if (timestamp < attempt.CreatedAt)
        {
            throw new ArgumentException("Submission receipt cannot predate attempt creation.", nameof(receivedAt));
        }
    }

    private static string CreateBusinessDeduplicationKey(
        RegistrationAttempt attempt,
        RegistrationEvidenceHash receivedEvidenceHash,
        Guid? providerBindingId,
        RegistrationEvidenceHash? providerMappingRevisionHash,
        string? providerSubmissionId,
        string? providerResponseRevision) => providerBindingId.HasValue && providerSubmissionId is not null
        ? CreateOpaqueBusinessKey("provider", attempt.TenantId.ToString("N"), providerBindingId.Value.ToString("N"), providerSubmissionId, providerResponseRevision!)
        : providerBindingId.HasValue
            ? CreateOpaqueBusinessKey("headless", attempt.TenantId.ToString("N"), attempt.Id.ToString("N"), receivedEvidenceHash.Value)
        : CreateOpaqueBusinessKey("native", attempt.TenantId.ToString("N"), attempt.Id.ToString("N"), receivedEvidenceHash.Value);

    private static string CreateOpaqueBusinessKey(params string[] components)
    {
        StringBuilder builder = new();
        foreach (string component in components)
        {
            builder.Append(component.Length).Append(':').Append(component).Append(';');
        }

        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void EnsureNativeAttempt(RegistrationAttempt attempt)
    {
        if (attempt.RegistrationProviderBindingId is not null || attempt.ProviderMappingRevisionHash is not null)
        {
            throw new ArgumentException("Native submissions require a native registration attempt.", nameof(attempt));
        }
    }

    private static void EnsureProviderAttempt(RegistrationAttempt attempt)
    {
        if (attempt.RegistrationProviderBindingId is null || attempt.ProviderMappingRevisionHash is null)
        {
            throw new ArgumentException("Provider submissions require a provider-pinned registration attempt.", nameof(attempt));
        }
    }

    private static string? NormalizeEvidence(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        string normalized = value.Trim();
        return normalized.Length is > 0 and <= 200
            ? normalized
            : throw new ArgumentException("Provider evidence identifiers must be non-blank and bounded.", parameterName);
    }

    private static DateTime EnsureUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", parameterName);
        }

        return value;
    }
}
