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
    IUnitOfWork unitOfWork)
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
        => unitOfWork.ExecuteSerializableAsync(
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
                    new InstanceOperatorIdentitySavedDocument(
                        saved.Revision!.Value,
                        saved.OperatorId!.Value,
                        Assess(saved, InstanceOperatorIdentityCapability.PublicDisclosure),
                        Assess(saved, InstanceOperatorIdentityCapability.PaidCommerce)),
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
