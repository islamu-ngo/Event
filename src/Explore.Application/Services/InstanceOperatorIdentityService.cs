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

namespace Explore.Application.Services;

/// <summary>
/// Stored instance operator identity document plus its readiness assessment.
/// <see cref="Settings"/> is null when no document exists or the stored JSON is corrupt.
/// </summary>
public sealed record InstanceOperatorIdentityDocument(
    InstanceOperatorIdentitySettings? Settings,
    InstanceOperatorIdentityReadinessAssessment Readiness);

/// <summary>
/// Result of a successful operator identity save: the fresh document revision and the
/// post-save readiness assessment.
/// </summary>
public sealed record InstanceOperatorIdentitySavedDocument(
    Guid Revision,
    InstanceOperatorIdentityReadinessAssessment Readiness);

/// <summary>
/// Transactional management service for the persisted instance operator identity
/// document (<c>instance.operator_identity</c>). Reads are uncached; saves run under a
/// serializable transaction with optimistic revision comparison so concurrent edits fail
/// closed with a concurrency conflict instead of silently losing updates.
/// </summary>
public sealed class InstanceOperatorIdentityService(
    ISystemSettingRepository systemSettingRepository,
    IInstanceBootstrapStateRepository bootstrapStateRepository,
    IUnitOfWork unitOfWork)
    : IInstanceOperatorIdentityReadinessEvaluator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<InstanceOperatorIdentityReadinessAssessment> EvaluateAsync(
        CancellationToken cancellationToken = default)
        => (await GetCurrentAsync(cancellationToken)).Readiness;

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
            return new(null, Missing());
        }

        InstanceOperatorIdentitySettings? payload = TryDeserialize(setting.Value);
        return payload is null
            ? new(null, Corrupt())
            : new(payload, Assess(payload, payload.Revision));
    }

    /// <summary>
    /// Saves a candidate document. While onboarding is pending, incomplete drafts are
    /// persisted; once the bootstrap is completed only fully valid replacements are
    /// accepted. <see cref="InstanceOperatorIdentitySettings.OperatorId"/>,
    /// <see cref="InstanceOperatorIdentitySettings.IsOfficialInstance"/>, and the
    /// document revision are server-controlled and never taken from the candidate.
    /// Stale <paramref name="expectedRevision"/> values throw
    /// <see cref="ConcurrencyConflictException"/>.
    /// </summary>
    public Task<BaseCommandResponse<InstanceOperatorIdentitySavedDocument>> SaveAsync(
        InstanceOperatorIdentitySettings candidate,
        Guid? expectedRevision,
        CancellationToken cancellationToken = default)
        => unitOfWork.ExecuteSerializableAsync(
            async ct =>
            {
                bool bootstrapCompleted =
                    (await bootstrapStateRepository.GetCurrent(ct))?.Status
                        == InstanceBootstrapStatus.Completed;

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

                if (stored?.Revision != expectedRevision)
                {
                    throw new ConcurrencyConflictException(
                        ConcurrencyConflictException.ConcurrentUpdate,
                        "The instance operator identity changed since it was loaded.",
                        nameof(SystemSetting),
                        InstanceOperatorIdentitySettingKeys.OperatorIdentity);
                }

                InstanceOperatorIdentityDraftValidation draft =
                    InstanceOperatorIdentityReadiness.ValidateDraft(candidate);
                if (!draft.IsValid)
                {
                    return BaseCommandResponse.Validation<InstanceOperatorIdentitySavedDocument>(
                        draft.ReasonCodes,
                        "The instance operator identity candidate contains invalid values.");
                }

                if (bootstrapCompleted)
                {
                    InstanceOperatorIdentityReadiness strict =
                        InstanceOperatorIdentityReadiness.Evaluate(draft.Normalized);
                    if (!strict.IsReady)
                    {
                        return BaseCommandResponse.Validation<InstanceOperatorIdentitySavedDocument>(
                            strict.ReasonCodes,
                            "A completed instance requires a fully valid operator identity.");
                    }
                }

                InstanceOperatorIdentitySettings saved = draft.Normalized with
                {
                    OperatorId = stored?.OperatorId ?? Guid.CreateVersion7(),
                    IsOfficialInstance = stored?.IsOfficialInstance ?? false,
                    Revision = Guid.CreateVersion7()
                };

                if (setting is null)
                {
                    setting = new SystemSetting
                    {
                        Id = Guid.CreateVersion7(),
                        SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                        Value = JsonSerializer.Serialize(saved, SerializerOptions),
                        ValueType = SettingValueType.Json,
                        CreatedAt = DateTime.UtcNow
                    };
                }
                else
                {
                    setting.Value = JsonSerializer.Serialize(saved, SerializerOptions);
                    setting.ValueType = SettingValueType.Json;
                    setting.UpdatedAt = DateTime.UtcNow;
                }

                await systemSettingRepository.UpsertInCurrentTransactionAsync(setting, ct);

                return BaseCommandResponse.Success(
                    new InstanceOperatorIdentitySavedDocument(saved.Revision!.Value, Assess(saved, saved.Revision)),
                    "Instance operator identity saved.");
            },
            cancellationToken);

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
        Guid? revision)
    {
        InstanceOperatorIdentityReadiness readiness = InstanceOperatorIdentityReadiness.Evaluate(payload);
        if (!readiness.IsReady)
        {
            return new(
                false,
                InstanceOperatorIdentityReasonCodes.IncompleteFailureCode,
                readiness.ReasonCodes,
                null,
                revision);
        }

        (InstanceOperatorIdentity? identity, ImmutableArray<string> failures) =
            InstanceOperatorIdentity.TryCreate(ToOptions(readiness.Normalized!));
        return failures.IsEmpty
            ? new(true, null, [], identity, revision)
            : new(false, InstanceOperatorIdentityReasonCodes.IncompleteFailureCode, failures, null, revision);
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
