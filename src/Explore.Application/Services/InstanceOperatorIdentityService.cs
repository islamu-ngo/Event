using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using System.Globalization;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Commands;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;

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
    IPolicyMutationExecutionScopeFactory scopeFactory,
    TimeProvider timeProvider)
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
    /// Stale revision preconditions throw
    /// <see cref="ConcurrencyConflictException"/>.
    /// </summary>
    public Task<BaseCommandResponse<InstanceOperatorIdentitySavedDocument>> SaveAsync(
        SaveInstanceOperatorIdentityCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        var candidate = new InstanceOperatorIdentitySettings
        {
            PublicName = request.PublicName,
            LegalName = request.LegalName,
            OperatorKindCode = request.OperatorKindCode,
            JurisdictionCountryCode = request.JurisdictionCountryCode,
            RegistrationIdentifier = request.RegistrationIdentifier,
            PublicContactEmail = request.PublicContactEmail,
            WebsiteUrl = request.WebsiteUrl,
            LegalNoticeUrl = request.LegalNoticeUrl,
            TermsUrl = request.TermsUrl,
            PrivacyUrl = request.PrivacyUrl,
            OfficialOrigin = request.OfficialOrigin
        };
        return SaveCoreAsync(command, candidate, request.ExpectedRevision, null, null, null, cancellationToken);
    }

    /// <summary>
    /// Imports a complete legal identity under the target's revision precondition.
    /// Official registry authority is never portable; no manifest is an attestation.
    /// </summary>
    public Task<BaseCommandResponse<InstanceOperatorIdentitySavedDocument>> ImportAsync(
        ImportInstanceOperatorIdentityCommand command,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        OperatorIdentityManifestJson.Validate(command.Manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ExpectedRevisionHash);
        ArgumentOutOfRangeException.ThrowIfEqual(actorUserId, Guid.Empty);
        var candidate = command.Manifest.Document.Deserialize<InstanceOperatorIdentitySettings>(SerializerOptions)
            ?? throw new OperatorIdentityManifestException();
        return SaveCoreAsync(command, candidate, null, command.ExpectedRevisionHash,
            command.Manifest.ContentDigest, actorUserId, cancellationToken);
    }

    private async Task<BaseCommandResponse<InstanceOperatorIdentitySavedDocument>> SaveCoreAsync(
        object originalCommand,
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
        DateTime occurredAt = timeProvider.GetUtcNow().UtcDateTime;
        // Candidate, preconditions and generated identifiers are frozen before the first await.
        // Global selection is an original physical owner, never a tenant transaction fallback.
        await using var selection = await scopeFactory
            .CreateAsync(PolicyMutationScope.Instance, cancellationToken);
        var selected = selection.Services;
        var authority = selected.GetRequiredService<IOperatorIdentityMutationAuthority>();
        var source = originalCommand switch
        {
            SaveInstanceOperatorIdentityCommand save => await authority.CaptureAsync(save, cancellationToken),
            ImportInstanceOperatorIdentityCommand import =>
                await authority.CaptureAsync(import, actorUserId!.Value, cancellationToken),
            _ => throw new UnauthorizedAccessException("An original operator identity command is required.")
        };
        var owner = selected.GetRequiredService<IUnitOfWork>();
        var writer = selected.GetRequiredService<IOperatorIdentitySettingsWriter>();
        occurredAt = writer.NormalizeEffectTimestamp(occurredAt);
        var setting = await writer.ObserveAsync(source, owner, cancellationToken);
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

        InstanceOperatorIdentityDraftValidation draft = InstanceOperatorIdentityReadiness.ValidateDraft(candidate);
        if (!draft.IsValid)
        {
            return BaseCommandResponse.Validation<InstanceOperatorIdentitySavedDocument>(
                draft.ReasonCodes, "The instance operator identity candidate contains invalid values.");
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
                    || new RegionInfo(saved.JurisdictionCountryCode).TwoLetterISORegionName != saved.JurisdictionCountryCode)
                    throw new ArgumentException("Invalid jurisdiction.");
            }
            catch (ArgumentException)
            {
                return BaseCommandResponse.Failure<InstanceOperatorIdentitySavedDocument>(
                    InstanceOperatorIdentityReasonCodes.IncompleteFailureCode,
                    "The imported identity requires a recognized country jurisdiction.",
                    ["instance_operator_identity_jurisdiction_country_invalid"]);
            }

            InstanceOperatorIdentityReadinessAssessment readiness = Assess(saved, InstanceOperatorIdentityCapability.PaidCommerce);
            if (!readiness.IsReady)
                return BaseCommandResponse.Failure<InstanceOperatorIdentitySavedDocument>(
                    readiness.FailureCode!, "The imported operator identity is incomplete.", readiness.ReasonCodes);
        }

        string value = JsonSerializer.Serialize(saved, SerializerOptions);
        var after = setting is null
            ? new OperatorIdentitySettingSnapshot(newSettingId, value, SettingValueType.Json, false,
                null, null, null, 0, occurredAt, source.Actor, null, null)
            : setting with
            {
                Value = value,
                ValueType = SettingValueType.Json,
                UpdatedAt = occurredAt,
                UpdatedBy = source.Actor
            };
        var audit = importRevisionHash is null ? null : new OperatorIdentityImportAudit(
            source.Actor!.Value, contentDigest!, importRevisionHash, newRevision);
        string? auditPayload = await writer.NormalizeAuditPayloadAsync(audit, cancellationToken);
        using var mutation = new OperatorIdentityMutation(source, owner, setting, after, auditId, audit, auditPayload, occurredAt);
        IHumanSettingsMutationPreparation? preparation = null;
        IPreparedHumanSettingsMutation? proof = null;
        if (source.IsHuman)
        {
            preparation = selected.GetRequiredService<IHumanSettingsMutationPreparation>();
            proof = originalCommand switch
            {
                SaveInstanceOperatorIdentityCommand save => await preparation.PrepareAsync(save, mutation, cancellationToken),
                ImportInstanceOperatorIdentityCommand import => await preparation.PrepareAsync(import, mutation, cancellationToken),
                _ => throw new UnauthorizedAccessException("An original operator identity command is required.")
            };
        }
        Exception? primaryFailure = null;
        try
        {
            mutation.Activate();
            // The original editor owned Serializable directly; it did not enter the generic
            // setting lock's replaying transaction wrapper. Preserve that physical-owner order.
            if (source.IsHuman)
                await preparation!.ExecutePreparedAsync(proof!, async (_, inner) =>
                {
                    await writer.ExecuteAsync(mutation, inner);
                    return true;
                }, cancellationToken);
            else
                await ((ISerializableAttemptOwner)owner).ExecuteAsync(async inner =>
                {
                    await writer.ExecuteAsync(mutation, inner);
                    return true;
                }, cancellationToken);
            mutation.Dispose();
        }
        catch (Exception failure) { primaryFailure = failure; throw; }
        finally { if (proof is not null) await preparation!.CloseAsync(proof, primaryFailure); }
        // The notification observes acknowledged settlement, never provisional writer success.
        await selected.GetServices<INotificationHandler<SettingChangedNotification>>().HandleAsync(
            new(InstanceOperatorIdentitySettingKeys.OperatorIdentity, setting?.Value, value,
                SettingSource.SystemDefault, null, source.Actor, occurredAt), CancellationToken.None);
        return BaseCommandResponse.Success(
            new InstanceOperatorIdentitySavedDocument(
                saved.Revision.Value, saved.OperatorId.Value,
                Assess(saved, InstanceOperatorIdentityCapability.PublicDisclosure),
                Assess(saved, InstanceOperatorIdentityCapability.PaidCommerce)),
            "Instance operator identity saved.");
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
