using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using System.Globalization;
using Explore.Application.Features.ConfigurationManifest.Application;

namespace Explore.Application.Services;

/// <summary>
/// Stored instance operator identity document plus its readiness assessment.
/// <see cref="Settings"/> is null when no document exists or the stored JSON is corrupt.
/// </summary>
public sealed record InstanceOperatorIdentityDocument(
    InstanceOperatorIdentitySettings? Settings,
    InstanceOperatorIdentityReadinessAssessment PublicDisclosure,
    InstanceOperatorIdentityReadinessAssessment PaidCommerce);

/// <summary>
/// Result of a successful operator identity save: the fresh document revision and the
/// post-save readiness assessment.
/// </summary>
public sealed record InstanceOperatorIdentitySavedDocument(
    Guid Revision,
    Guid OperatorId,
    InstanceOperatorIdentityReadinessAssessment PublicDisclosure,
    InstanceOperatorIdentityReadinessAssessment PaidCommerce);

/// <summary>
/// Transactional management service for the persisted instance operator identity
/// document (<c>instance.operator_identity</c>). Reads are uncached; saves run under a
/// serializable transaction with optimistic revision comparison so concurrent edits fail
/// closed with a concurrency conflict instead of silently losing updates.
/// </summary>
public sealed class InstanceOperatorIdentityService(
    ISystemSettingRepository systemSettingRepository,
    IUnitOfWork unitOfWork,
    IOutboxRepository outbox)
    : IInstanceOperatorIdentityReadinessEvaluator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<InstanceOperatorIdentityReadinessAssessment> EvaluateAsync(
        InstanceOperatorIdentityCapability capability,
        CancellationToken cancellationToken = default)
    {
        InstanceOperatorIdentityDocument document = await GetCurrentAsync(cancellationToken);
        return capability switch
        {
            InstanceOperatorIdentityCapability.PublicDisclosure => document.PublicDisclosure,
            InstanceOperatorIdentityCapability.PaidCommerce => document.PaidCommerce,
            _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unsupported identity capability.")
        };
    }

    /// <summary>
    /// Returns the stored document (or null when missing or corrupt) with its readiness
    /// assessment, without any cross-request caching.
    /// </summary>
    public async Task<InstanceOperatorIdentityDocument> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        SystemSetting? setting = await systemSettingRepository.GetByKey(
            InstanceOperatorIdentitySettingKeys.OperatorIdentity,
            cancellationToken);

        if (setting is null)
        {
            return new(null, Missing(), Missing());
        }

        InstanceOperatorIdentitySettings? payload = TryDeserialize(setting.Value);
        return payload is null
            ? new(null, Corrupt(), Corrupt())
            : new(payload,
                Assess(payload, InstanceOperatorIdentityCapability.PublicDisclosure),
                Assess(payload, InstanceOperatorIdentityCapability.PaidCommerce));
    }

    /// <summary>
    /// Saves a syntactically valid candidate, including incomplete administrative drafts.
    /// Protected operations independently require capability readiness.
    /// <see cref="InstanceOperatorIdentitySettings.OperatorId"/>,
    /// <see cref="InstanceOperatorIdentitySettings.IsOfficialInstance"/>, and the
    /// document revision are server-controlled and never taken from the candidate.
    /// Stale <paramref name="expectedRevision"/> values throw
    /// <see cref="ConcurrencyConflictException"/>.
    /// </summary>
    public Task<BaseCommandResponse<InstanceOperatorIdentitySavedDocument>> SaveAsync(
        InstanceOperatorIdentitySettings candidate,
        Guid? expectedRevision,
        CancellationToken cancellationToken = default)
        => SaveCoreAsync(candidate, expectedRevision, null, null, null, cancellationToken);

    /// <summary>
    /// Imports a complete legal identity under the target's revision precondition.
    /// Official registry authority is never portable; no manifest is an attestation.
    /// </summary>
    public Task<BaseCommandResponse<InstanceOperatorIdentitySavedDocument>> ImportAsync(
        InstanceOperatorIdentitySettings candidate,
        string expectedRevisionHash,
        string contentDigest,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevisionHash);
        ArgumentOutOfRangeException.ThrowIfEqual(actorUserId, Guid.Empty);
        return SaveCoreAsync(candidate, null, expectedRevisionHash, contentDigest, actorUserId, cancellationToken);
    }

    private Task<BaseCommandResponse<InstanceOperatorIdentitySavedDocument>> SaveCoreAsync(
        InstanceOperatorIdentitySettings candidate,
        Guid? expectedRevision,
        string? importRevisionHash,
        string? contentDigest,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        Guid newOperatorId = Guid.CreateVersion7();
        Guid newRevision = Guid.CreateVersion7();
        Guid newSettingId = Guid.CreateVersion7();
        Guid auditId = Guid.CreateVersion7();
        DateTime occurredAt = DateTime.UtcNow;
        return unitOfWork.ExecuteSerializableAsync(
            async ct =>
            {
                SystemSetting? setting = await systemSettingRepository.GetByKey(
                    InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                    ct);
                InstanceOperatorIdentitySettings? stored = setting is null ? null : TryDeserialize(setting.Value);
                if (setting is not null && stored is null)
                {
                    return BaseCommandResponse.Failure<InstanceOperatorIdentitySavedDocument>(
                        InstanceOperatorIdentityFailureCodes.IntegrityError,
                        "The stored instance operator identity document could not be parsed; it must be repaired before it can be replaced.");
                }

                if (importRevisionHash is not null
                    ? !string.Equals(OperatorIdentityManifestJson.RevisionHash(stored?.Revision),
                        importRevisionHash, StringComparison.Ordinal)
                    : stored?.Revision != expectedRevision)
                {
                    throw new ConcurrencyConflictException(
                        ConcurrencyConflictException.ConcurrentUpdate,
                        "The instance operator identity changed since it was loaded.",
                        nameof(SystemSetting),
                        InstanceOperatorIdentitySettingKeys.OperatorIdentity);
                }

                if (importRevisionHash is not null && (candidate.IsOfficialInstance || stored?.IsOfficialInstance == true))
                {
                    return BaseCommandResponse.Failure<InstanceOperatorIdentitySavedDocument>(
                        "operator_identity_official_authority_not_portable",
                        "Official registry authority cannot be imported or replaced by a manifest.");
                }

                InstanceOperatorIdentityDraftValidation draft =
                    InstanceOperatorIdentityReadiness.ValidateDraft(candidate);
                if (!draft.IsValid)
                {
                    return BaseCommandResponse.Validation<InstanceOperatorIdentitySavedDocument>(
                        draft.ReasonCodes,
                        "The instance operator identity candidate contains invalid values.");
                }

                InstanceOperatorIdentitySettings saved = draft.Normalized with
                {
                    OperatorId = stored?.OperatorId ?? newOperatorId,
                    IsOfficialInstance = stored?.IsOfficialInstance ?? false,
                    Revision = newRevision
                };

                if (importRevisionHash is not null)
                {
                    if (saved.OperatorKindCode == "registered_organization"
                        && string.IsNullOrWhiteSpace(saved.RegistrationIdentifier))
                        return BaseCommandResponse.Failure<InstanceOperatorIdentitySavedDocument>(
                            InstanceOperatorIdentityReasonCodes.IncompleteFailureCode,
                            "A registered organization import requires its registration identifier.",
                            ["instance_operator_identity_registration_identifier_missing"]);

                    try
                    {
                        if (saved.JurisdictionCountryCode is null
                            || new RegionInfo(saved.JurisdictionCountryCode).TwoLetterISORegionName
                                != saved.JurisdictionCountryCode)
                            throw new ArgumentException("Invalid jurisdiction.");
                    }
                    catch (ArgumentException)
                    {
                        return BaseCommandResponse.Failure<InstanceOperatorIdentitySavedDocument>(
                            InstanceOperatorIdentityReasonCodes.IncompleteFailureCode,
                            "The imported identity requires a recognized country jurisdiction.",
                            ["instance_operator_identity_jurisdiction_country_invalid"]);
                    }

                    InstanceOperatorIdentityReadinessAssessment readiness =
                        Assess(saved, InstanceOperatorIdentityCapability.PaidCommerce);
                    if (!readiness.IsReady)
                    {
                        return BaseCommandResponse.Failure<InstanceOperatorIdentitySavedDocument>(
                            readiness.FailureCode!, "The imported operator identity is incomplete.", readiness.ReasonCodes);
                    }
                }

                if (setting is null)
                {
                    setting = new SystemSetting
                    {
                        Id = newSettingId,
                        SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                        Value = JsonSerializer.Serialize(saved, SerializerOptions),
                        ValueType = SettingValueType.Json,
                        CreatedAt = occurredAt,
                        CreatedBy = actorUserId
                    };
                }
                else
                {
                    setting.Value = JsonSerializer.Serialize(saved, SerializerOptions);
                    setting.ValueType = SettingValueType.Json;
                    setting.UpdatedAt = occurredAt;
                    setting.UpdatedBy = actorUserId;
                }

                await systemSettingRepository.UpsertInCurrentTransactionAsync(setting, ct);

                if (importRevisionHash is not null)
                {
                    InstanceOperatorIdentityReadinessAssessment persisted =
                        await ((IInstanceOperatorIdentityReadinessEvaluator)this).EvaluateAsync(
                            InstanceOperatorIdentityCapability.PaidCommerce, ct);
                    if (!persisted.IsReady || persisted.DocumentRevision != newRevision)
                    {
                        throw new InvalidOperationException("The imported identity did not retain its readiness and revision.");
                    }
                    await outbox.CreateRange([new OutboxMessage
                    {
                        Id = auditId,
                        AggregateType = OperatorIdentityImportAudit.AggregateType,
                        AggregateId = saved.OperatorId.Value,
                        EventType = OperatorIdentityImportAudit.EventType,
                        Payload = JsonSerializer.Serialize(new OperatorIdentityImportAudit(
                            actorUserId!.Value, contentDigest!, importRevisionHash, newRevision)),
                        Status = OutboxMessageStatus.Pending,
                        CreatedAt = occurredAt,
                        MaxRetries = 5
                    }], ct);
                }

                return BaseCommandResponse.Success(
                    new InstanceOperatorIdentitySavedDocument(
                        saved.Revision.Value,
                        saved.OperatorId.Value,
                        Assess(saved, InstanceOperatorIdentityCapability.PublicDisclosure),
                        Assess(saved, InstanceOperatorIdentityCapability.PaidCommerce)),
                    "Instance operator identity saved.");
            },
            cancellationToken);
    }

    private static InstanceOperatorIdentityReadinessAssessment Missing() => new(
        false,
        InstanceOperatorIdentityFailureCodes.Missing,
        [],
        null,
        null);

    private static InstanceOperatorIdentityReadinessAssessment Corrupt() => new(
        false,
        InstanceOperatorIdentityFailureCodes.IntegrityError,
        [],
        null,
        null);

    private static InstanceOperatorIdentityReadinessAssessment Assess(
        InstanceOperatorIdentitySettings payload,
        InstanceOperatorIdentityCapability capability)
    {
        (InstanceOperatorIdentity? identity, ImmutableArray<string> failures) =
            InstanceOperatorIdentity.TryCreate(ToOptions(payload), capability);
        return failures.IsEmpty
            ? new(true, null, [], identity, payload.Revision)
            : new(false, InstanceOperatorIdentityReasonCodes.IncompleteFailureCode, failures, null, payload.Revision);
    }

    private static InstanceOperatorIdentityOptions ToOptions(InstanceOperatorIdentitySettings settings) => new()
    {
        OperatorId = settings.OperatorId ?? Guid.Empty,
        PublicName = settings.PublicName ?? string.Empty,
        LegalName = settings.LegalName ?? string.Empty,
        IsOfficialInstance = settings.IsOfficialInstance,
        OfficialOrigin = settings.OfficialOrigin ?? string.Empty,
        OperatorKindCode = settings.OperatorKindCode ?? string.Empty,
        JurisdictionCountryCode = settings.JurisdictionCountryCode ?? string.Empty,
        RegistrationIdentifier = settings.RegistrationIdentifier,
        PublicContactEmail = settings.PublicContactEmail ?? string.Empty,
        WebsiteUrl = settings.WebsiteUrl ?? string.Empty,
        LegalNoticeUrl = settings.LegalNoticeUrl ?? string.Empty,
        TermsUrl = settings.TermsUrl ?? string.Empty,
        PrivacyUrl = settings.PrivacyUrl ?? string.Empty
    };

    private static InstanceOperatorIdentitySettings? TryDeserialize(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<InstanceOperatorIdentitySettings>(value, SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
