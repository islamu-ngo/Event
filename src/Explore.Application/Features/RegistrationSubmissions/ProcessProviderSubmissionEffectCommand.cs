using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.RegistrationSubmissions.Commands;

public sealed record ProcessProviderSubmissionEffectCommand(
    Guid TenantId,
    Guid IncomingWebhookMessageId,
    Guid BindingId,
    string Provider,
    ReadOnlyMemory<byte> PayloadBytes,
    IReadOnlyDictionary<string, string> Headers) : IRequest<ProviderSubmissionEffectResult>;

public sealed record ProviderSubmissionEffectResult(ProviderSubmissionEffectOutcome Outcome, string Code)
{
    public static ProviderSubmissionEffectResult Completed(string code = "completed") => new(ProviderSubmissionEffectOutcome.Completed, code);
    public static ProviderSubmissionEffectResult NeedsReconciliation(string code) => new(ProviderSubmissionEffectOutcome.NeedsReconciliation, code);
    public static ProviderSubmissionEffectResult Retryable(string code) => new(ProviderSubmissionEffectOutcome.Retryable, code);
}

public enum ProviderSubmissionEffectOutcome
{
    Completed = 1,
    NeedsReconciliation = 2,
    Retryable = 3
}

public sealed class ProcessProviderSubmissionEffectCommandValidator : AbstractValidator<ProcessProviderSubmissionEffectCommand>
{
    public ProcessProviderSubmissionEffectCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.IncomingWebhookMessageId).NotEmpty();
        RuleFor(command => command.BindingId).NotEmpty();
        RuleFor(command => command.Provider).NotEmpty().MaximumLength(100);
        RuleFor(command => command.PayloadBytes.Length).InclusiveBetween(1, 256 * 1024);
        RuleFor(command => command.Headers).NotNull();
    }
}

public sealed class ProcessProviderSubmissionEffectCommandHandler(
    IRegistrationProviderRepository providerRepository,
    IRegistrationSubmissionRepository submissionRepository,
    IRegistrationInventoryRepository inventoryRepository,
    IRegistrationFormAuthoringRepository formRepository,
    IEventParticipationConfigurationRepository participationConfigurationRepository,
    IRegistrationParticipantRepository participantRepository,
    IRegistrationSensitiveValueProtector protector,
    ISender sender,
    IRegistrationProviderRegistry providerRegistry,
    IRegistrationProviderCallbackReceiptProtector receiptProtector,
    IGuestCapabilityTokenService capabilities,
    TimeProvider timeProvider)
    : IRequestHandler<ProcessProviderSubmissionEffectCommand, ProviderSubmissionEffectResult>
{
    public const string StableEffectKind = "registration.provider_submission";

    public async Task<ProviderSubmissionEffectResult> Handle(
        ProcessProviderSubmissionEffectCommand request,
        CancellationToken cancellationToken)
    {
        await new ProcessProviderSubmissionEffectCommandValidator().ValidateAndThrowAsync(request, cancellationToken);

        RegistrationProviderBinding? binding = await providerRepository.GetBindingAsync(
            request.TenantId, request.BindingId, cancellationToken);
        if (binding is null)
        {
            return ProviderSubmissionEffectResult.NeedsReconciliation("UNKNOWN_BINDING");
        }

        RegistrationProviderTuple tuple = ResolveTuple(binding, request.Provider);
        if (tuple.ProviderCode.Length == 0)
        {
            return await ParkAsync(request, null, "UNKNOWN_TUPLE", cancellationToken);
        }

        if (!TryValidateReceipt(request, binding, tuple, out RegistrationProviderCallbackReceipt? receipt))
        {
            return await ParkAsync(request, null, "UNVERIFIABLE_EVIDENCE", cancellationToken);
        }
        RegistrationProviderCallbackReceipt verifiedReceipt = receipt!;

        ProviderSubmissionEnvelope envelope;
        try
        {
            envelope = ProviderSubmissionEnvelope.Parse(request.PayloadBytes.Span);
        }
        catch (JsonException)
        {
            if (binding.Connection is null || providerRegistry.TryResolve(tuple) is not IRegistrationProviderSubmissionReader reader)
            {
                return await ParkAsync(request, null, "SUBMISSION_READ_UNSUPPORTED", cancellationToken);
            }

            try
            {
                RegistrationProviderSubmissionReadResult fetched = await reader.ReadSubmissionAsync(
                    new RegistrationProviderSubmissionReadRequest(
                        request.TenantId,
                        binding,
                        binding.Connection,
                        tuple,
                        verifiedReceipt.ProviderSubmissionId),
                    cancellationToken);
                if (fetched.AttemptId is not { } attemptId)
                {
                    return await ParkAsync(request, null, "PROVIDER_CORRELATION_MISSING", cancellationToken);
                }

                envelope = new ProviderSubmissionEnvelope(
                    attemptId,
                    fetched.ProviderSubmissionId,
                    fetched.ProviderRevisionId,
                    fetched.ReceivedAt,
                    null,
                    null,
                    fetched.AttemptCapabilityToken,
                    fetched.Answers);
            }
            catch (RegistrationProviderUnsupportedSubmissionException exception)
            {
                return await ParkAsync(request, null, exception.FailureCode, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
            {
                return ProviderSubmissionEffectResult.Retryable("SUBMISSION_FETCH_FAILED");
            }
            catch (Exception exception) when (exception is JsonException or FormatException)
            {
                return await ParkAsync(request, null, "SUBMISSION_FETCH_FAILED", cancellationToken);
            }
        }

        if (!string.Equals(verifiedReceipt.ProviderSubmissionId, envelope.ProviderSubmissionId, StringComparison.Ordinal))
        {
            return await ParkAsync(request, null, "UNVERIFIABLE_EVIDENCE", cancellationToken);
        }

        RegistrationAttempt? attempt = await submissionRepository.GetAttemptAsync(
            request.TenantId, envelope.AttemptId, cancellationToken);
        if (attempt is null || attempt.RegistrationProviderBindingId != request.BindingId)
        {
            return await ParkAsync(request, attempt, "ATTEMPT_NOT_FOUND", cancellationToken);
        }
        IRegistrationProviderDescriptor? descriptor = providerRegistry.TryResolve(tuple);
        if (descriptor is IRegistrationProviderDelegatedAutomation &&
            !capabilities.Matches(envelope.AttemptCapabilityToken, attempt.CapabilityTokenHash))
        {
            return await ParkAsync(request, attempt, "PROVIDER_CORRELATION_INVALID", cancellationToken);
        }

        if (IsGoogleTokenOnlyDelegatedCorrelation(tuple, descriptor, envelope))
        {
            EventParticipationConfiguration? participationConfiguration = await participationConfigurationRepository.GetByEventAndTenantAsync(
                attempt.EventId, request.TenantId, cancellationToken);
            if (participationConfiguration?.IdentityAccessModeId == (int)IdentityAccessModeEnum.AccountRequired)
            {
                return await ParkAsync(request, attempt, "TOKEN_ONLY_IDENTITY_BELOW_POLICY", cancellationToken);
            }
        }

        RegistrationRequirement requirement = await submissionRepository.GetRequirementAsync(
            request.TenantId, attempt.RegistrationRequirementId, cancellationToken)
            ?? throw new InvalidOperationException("Pinned registration requirement was not found.");
        DateTime receivedAt = envelope.ReceivedAt ?? timeProvider.GetUtcNow().UtcDateTime;
        RegistrationEvidenceHash evidenceHash = RegistrationEvidenceHash.Create(HashBase64(request.PayloadBytes.Span));
        RegistrationTransportIdempotencyHash transportHash = RegistrationTransportIdempotencyHash.Create(
            HashBase64(request.IncomingWebhookMessageId.ToByteArray()));

        if ((RegistrationAnswerSyncModeEnum)requirement.AnswerSyncModeId == RegistrationAnswerSyncModeEnum.NONE)
        {
            return ProviderSubmissionEffectResult.Completed("sync_none_no_storage");
        }

        if (BindingCannotAutoFinalize(binding))
        {
            return await ParkAsync(request, attempt, "BLOCKING_DRIFT", cancellationToken);
        }

        if ((RegistrationAnswerSyncModeEnum)requirement.AnswerSyncModeId == RegistrationAnswerSyncModeEnum.MIRROR_ONLY)
        {
            if (HasCapability(binding, RegistrationProviderCapabilityCodes.SubmissionSink))
            {
                RegistrationSubmissionPersistenceResult mirrorPersisted = await submissionRepository.PersistEvidenceOnlyAsync(RegistrationSubmission.CreateProviderEvidenceOnly(
                    attempt,
                    evidenceHash,
                    receivedAt,
                    transportHash,
                    envelope.ProviderSubmissionId,
                    envelope.ProviderResponseRevision,
                    envelope.ProviderSubjectId,
                    envelope.ProviderCorrelationId),
                    cancellationToken);
                return mirrorPersisted.Outcome is RegistrationSubmissionPersistenceOutcome.Inserted or RegistrationSubmissionPersistenceOutcome.Existing
                    ? ProviderSubmissionEffectResult.Completed("mirror_only_recorded")
                    : ProviderSubmissionEffectResult.NeedsReconciliation("PERSISTENCE_CONFLICT");
            }

            return await ParkAsync(request, attempt, "MIRROR_SINK_UNSUPPORTED", cancellationToken);
        }

        if (!HasMinimumTrust(binding, (RegistrationAnswerSyncModeEnum)requirement.AnswerSyncModeId))
        {
            return await ParkAsync(request, attempt, "BELOW_MINIMUM_TRUST", cancellationToken);
        }

        Guid expectedAttemptConcurrencyStamp = attempt.ConcurrencyStamp;
        RegistrationSubmission submission;
        try
        {
            submission = attempt.SubmitProvider(
                evidenceHash,
                receivedAt,
                transportHash,
                envelope.ProviderSubmissionId,
                envelope.ProviderResponseRevision,
                envelope.ProviderSubjectId,
                envelope.ProviderCorrelationId);
        }
        catch (InvalidOperationException)
        {
            return await ParkAsync(request, attempt, "STALE_OR_OUT_OF_ORDER", cancellationToken);
        }

        RegistrationSubmissionPersistenceResult persisted = (RegistrationAnswerSyncModeEnum)requirement.AnswerSyncModeId switch
        {
            RegistrationAnswerSyncModeEnum.COMPLETION_ONLY => await PersistCompletionOnlyAsync(
                attempt, submission, requirement, expectedAttemptConcurrencyStamp, cancellationToken),
            RegistrationAnswerSyncModeEnum.SELECTED_FIELDS or RegistrationAnswerSyncModeEnum.FULL_CANONICAL =>
                await PersistCanonicalAsync(
                    attempt, submission, binding, envelope, expectedAttemptConcurrencyStamp, cancellationToken),
            _ => await PersistEvidenceOnlyAsync(submission, "UNSUPPORTED_SYNC_MODE", cancellationToken)
        };

        return persisted.Outcome is RegistrationSubmissionPersistenceOutcome.Inserted or RegistrationSubmissionPersistenceOutcome.Existing
            ? ProviderSubmissionEffectResult.Completed(persisted.Outcome.ToString().ToLowerInvariant())
            : ProviderSubmissionEffectResult.NeedsReconciliation("PERSISTENCE_CONFLICT");
    }

    private async Task<RegistrationSubmissionPersistenceResult> PersistCompletionOnlyAsync(
        RegistrationAttempt attempt,
        RegistrationSubmission submission,
        RegistrationRequirement requirement,
        Guid expectedAttemptConcurrencyStamp,
        CancellationToken cancellationToken)
    {
        RegistrationOrder order = await inventoryRepository.GetOrderWithLinesAsync(
            submission.RegistrationOrderId, submission.TenantId, cancellationToken)
            ?? throw new InvalidOperationException("Registration order was not found.");
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        return await submissionRepository.PersistAcceptedWithNormalizationAsync(
            attempt,
            submission,
            expectedAttemptConcurrencyStamp,
            [],
            [],
            [],
            [RegistrationRequirementFulfillment.CreateFulfilled(
                order,
                requirement,
                submission,
                RegistrationAnswerSubjectTypeEnum.RegistrationOrder,
                order.Id,
                now)],
            cancellationToken);
    }

    private async Task<RegistrationSubmissionPersistenceResult> PersistCanonicalAsync(
        RegistrationAttempt attempt,
        RegistrationSubmission submission,
        RegistrationProviderBinding binding,
        ProviderSubmissionEnvelope envelope,
        Guid expectedAttemptConcurrencyStamp,
        CancellationToken cancellationToken)
    {
        RegistrationOrder order = await inventoryRepository.GetOrderWithLinesAsync(
            submission.RegistrationOrderId, submission.TenantId, cancellationToken)
            ?? throw new InvalidOperationException("Registration order was not found.");
        IReadOnlyList<RegistrationParticipant> participants = await participantRepository.GetParticipantsByOrderAsync(
            order.Id, submission.TenantId, cancellationToken);
        IReadOnlyList<RegistrationTicketAssignment> assignments = await participantRepository
            .GetAssignmentsWithParticipantsByOrderAsync(order.Id, submission.TenantId, cancellationToken);
        IReadOnlyList<RegistrationSubmissionAnswerInput> inputs = await MapAnswersAsync(
            submission, binding, envelope.Answers, cancellationToken);
        RegistrationSubmissionNormalizationDraft draft = await NormalizeRegistrationSubmissionCommandHandler.PrepareAsync(
            submission, inputs, order, participants, assignments, submissionRepository, formRepository,
            protector, timeProvider, cancellationToken);
        return await submissionRepository.PersistAcceptedWithNormalizationAsync(
            attempt,
            submission,
            expectedAttemptConcurrencyStamp,
            draft.Answers,
            draft.ConsentRecords,
            draft.Issues,
            draft.Fulfillments,
            cancellationToken);
    }

    private async Task<IReadOnlyList<RegistrationSubmissionAnswerInput>> MapAnswersAsync(
        RegistrationSubmission submission,
        RegistrationProviderBinding binding,
        IReadOnlyDictionary<string, JsonElement> providerAnswers,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await formRepository.GetVersionAsync(
            submission.EventId, submission.RegistrationFormId, submission.RegistrationFormVersionId, cancellationToken)
            ?? throw new InvalidOperationException("Pinned registration form version was not found.");
        Dictionary<string, RegistrationFormField> fields = version.Sections
            .SelectMany(section => section.Fields)
            .Where(field => !field.IsDeleted)
            .ToDictionary(field => field.Namespace + "." + field.Key, StringComparer.OrdinalIgnoreCase);
        List<RegistrationSubmissionAnswerInput> inputs = [];
        foreach (RegistrationProviderFieldMapping mapping in binding.FieldMappings.Where(mapping => !mapping.IsDeleted))
        {
            if (!fields.TryGetValue(mapping.PlatformFieldKey, out RegistrationFormField? field) ||
                !providerAnswers.TryGetValue(mapping.ProviderFieldKey, out JsonElement value))
            {
                continue;
            }

            inputs.Add(new RegistrationSubmissionAnswerInput(
                field.Id,
                RegistrationAnswerSubjectTypeEnum.RegistrationOrder,
                submission.RegistrationOrderId,
                null,
                value.Clone()));
        }

        return inputs;
    }

    private async Task<ProviderSubmissionEffectResult> ParkAsync(
        ProcessProviderSubmissionEffectCommand request,
        RegistrationAttempt? attempt,
        string code,
        CancellationToken cancellationToken)
    {
        if (attempt is null)
        {
            return ProviderSubmissionEffectResult.NeedsReconciliation(code);
        }

        RegistrationSubmission submission = RegistrationSubmission.CreateProviderEvidenceOnly(
            attempt,
            RegistrationEvidenceHash.Create(HashBase64(request.PayloadBytes.Span)),
            timeProvider.GetUtcNow().UtcDateTime,
            RegistrationTransportIdempotencyHash.Create(HashBase64(request.IncomingWebhookMessageId.ToByteArray())),
            "parked-" + request.IncomingWebhookMessageId.ToString("N"),
            "parked",
            null,
            null);
        await PersistEvidenceOnlyAsync(submission, code, cancellationToken);
        return ProviderSubmissionEffectResult.NeedsReconciliation(code);
    }

    private async Task<RegistrationSubmissionPersistenceResult> PersistEvidenceOnlyAsync(
        RegistrationSubmission submission,
        string code,
        CancellationToken cancellationToken)
    {
        RegistrationSubmissionPersistenceResult result = await submissionRepository.PersistEvidenceOnlyAsync(
            submission,
            cancellationToken);
        if (result.Outcome == RegistrationSubmissionPersistenceOutcome.Inserted)
        {
            await submissionRepository.PersistNormalizationAsync(
                [],
                [],
                [RegistrationSubmissionIssue.Create(submission, code, timeProvider.GetUtcNow().UtcDateTime)],
                cancellationToken);
        }

        return result;
    }

    private static RegistrationProviderTuple ResolveTuple(RegistrationProviderBinding binding, string provider)
    {
        return binding.Connection is not null && string.Equals(binding.Connection.ProviderCode, provider, StringComparison.OrdinalIgnoreCase) &&
               binding.Capabilities.Any(capability => !capability.IsDeleted &&
                   string.Equals(capability.CapabilityCode, RegistrationProviderCapabilityCodes.CallbackVerification, StringComparison.OrdinalIgnoreCase))
            ? new RegistrationProviderTuple(binding.Connection.ProviderCode, binding.Connection.ProviderDeploymentCode, binding.Connection.ApiVersion,
                binding.Connection.AdapterPolicyVersion, binding.Connection.ConformanceEvidenceRevision)
            : RegistrationProviderTuple.Empty;
    }

    private static bool BindingCannotAutoFinalize(RegistrationProviderBinding binding) =>
        binding.DriftClassId >= (int)RegistrationProviderDriftClassEnum.MappingRequired ||
        binding.StateId != (int)RegistrationProviderBindingStateEnum.Published;

    private static bool HasMinimumTrust(RegistrationProviderBinding binding, RegistrationAnswerSyncModeEnum syncMode) => syncMode switch
    {
        RegistrationAnswerSyncModeEnum.COMPLETION_ONLY => binding.TrustLevelId >= (int)RegistrationProviderTrustLevelEnum.CompletionOnly,
        RegistrationAnswerSyncModeEnum.SELECTED_FIELDS => binding.TrustLevelId >= (int)RegistrationProviderTrustLevelEnum.SelectedFields,
        RegistrationAnswerSyncModeEnum.FULL_CANONICAL => binding.TrustLevelId >= (int)RegistrationProviderTrustLevelEnum.FullCanonical,
        RegistrationAnswerSyncModeEnum.MIRROR_ONLY => false,
        RegistrationAnswerSyncModeEnum.NONE => true,
        _ => false
    };

    private static bool HasCapability(RegistrationProviderBinding binding, string capabilityCode) =>
        binding.Capabilities.Any(capability =>
            !capability.IsDeleted &&
            string.Equals(capability.CapabilityCode, capabilityCode, StringComparison.OrdinalIgnoreCase));

    private static bool IsGoogleTokenOnlyDelegatedCorrelation(
        RegistrationProviderTuple tuple,
        IRegistrationProviderDescriptor? descriptor,
        ProviderSubmissionEnvelope envelope) =>
        descriptor is IRegistrationProviderDelegatedAutomation &&
        string.Equals(tuple.ProviderCode, "GOOGLE_FORMS", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(envelope.AttemptCapabilityToken);

    private bool TryValidateReceipt(
        ProcessProviderSubmissionEffectCommand request,
        RegistrationProviderBinding binding,
        RegistrationProviderTuple tuple,
        out RegistrationProviderCallbackReceipt? receipt)
    {
        receipt = null;
        if (!request.Headers.TryGetValue("X-Registration-Verification-Receipt", out string? protectedReceipt) ||
            string.IsNullOrWhiteSpace(protectedReceipt))
        {
            return false;
        }

        try
        {
            receipt = receiptProtector.Unprotect(protectedReceipt);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or CryptographicException or JsonException or FormatException)
        {
            return false;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        return receipt is not null &&
               receipt.TenantId == request.TenantId &&
               receipt.ConnectionId == binding.RegistrationProviderConnectionId &&
               receipt.BindingId == request.BindingId &&
               string.Equals(receipt.Provider, request.Provider, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(receipt.TupleKey, tuple.Key, StringComparison.Ordinal) &&
               string.Equals(receipt.BodySha256, HashSha256Hex(request.PayloadBytes.Span), StringComparison.Ordinal) &&
               !string.IsNullOrWhiteSpace(receipt.Nonce) &&
               receipt.VerifiedAt <= now.AddMinutes(5);
    }

    private static string HashBase64(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(SHA256.HashData(bytes));

    private static string HashSha256Hex(ReadOnlySpan<byte> bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record ProviderSubmissionEnvelope(
        Guid AttemptId,
        string ProviderSubmissionId,
        string ProviderResponseRevision,
        DateTime? ReceivedAt,
        string? ProviderSubjectId,
        string? ProviderCorrelationId,
        string? AttemptCapabilityToken,
        IReadOnlyDictionary<string, JsonElement> Answers)
    {
        public static ProviderSubmissionEnvelope Parse(ReadOnlySpan<byte> payload)
        {
            using JsonDocument document = JsonDocument.Parse(payload.ToArray());
            JsonElement root = document.RootElement;
            Dictionary<string, JsonElement> answers = new(StringComparer.OrdinalIgnoreCase);
            JsonElement answerObject = root.TryGetProperty("answers", out JsonElement answersProperty)
                ? answersProperty
                : root.TryGetProperty("mappedValues", out JsonElement mappedValues) ? mappedValues : default;
            if (answerObject.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in answerObject.EnumerateObject())
                {
                    answers[property.Name] = property.Value.Clone();
                }
            }

            JsonElement attemptIdElement = root.TryGetProperty("attemptId", out JsonElement attemptIdProperty)
                ? attemptIdProperty
                : root.TryGetProperty("attemptTokenId", out JsonElement attemptTokenId) ? attemptTokenId : default;
            if (attemptIdElement.ValueKind != JsonValueKind.String ||
                !attemptIdElement.TryGetGuid(out Guid attemptId))
            {
                throw new JsonException("Provider submission envelope is missing a valid attempt id.");
            }

            return new(
                attemptId,
                Optional(root, "providerSubmissionId") ?? Required(root, "responseId"),
                Optional(root, "providerResponseRevision") ?? Required(root, "contractVersion"),
                (root.TryGetProperty("receivedAt", out JsonElement receivedAt) || root.TryGetProperty("timestamp", out receivedAt)) && receivedAt.ValueKind == JsonValueKind.String
                    ? receivedAt.GetDateTime().ToUniversalTime()
                    : null,
                Optional(root, "providerSubjectId"),
                Optional(root, "providerCorrelationId"),
                Optional(root, "attemptToken"),
                answers);
        }

        private static string Required(JsonElement root, string name) =>
            Optional(root, name) is { Length: > 0 } value && value.Length <= 200
                ? value
                : throw new JsonException("Provider submission envelope is missing a bounded identity.");

        private static string? Optional(JsonElement root, string name) =>
            root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim()
                : null;
    }
}
