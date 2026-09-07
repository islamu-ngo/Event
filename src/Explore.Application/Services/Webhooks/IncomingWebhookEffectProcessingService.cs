using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.EventReporting.Validators;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Application.Serialization;
using Explore.Domain;
using MediatR;
using Microsoft.Extensions.Options;

namespace Explore.Application.Services.Webhooks;

public sealed class IncomingWebhookEffectProcessingService(
    IIncomingWebhookEffectOutboxRepository pointerRepository,
    IIncomingWebhookMessageRepository messageRepository,
    IIncomingWebhookEffectReceiptRepository receiptRepository,
    IRegistrationProviderSubscriptionStateRepository subscriptionStateRepository,
    IUnitOfWork unitOfWork,
    IMediator mediator,
    IOptions<IncomingWebhookProcessingSettings> settings,
    TimeProvider timeProvider) : IIncomingWebhookEffectProcessingService
{
    public IncomingWebhookEffectProcessingService(
        IIncomingWebhookEffectOutboxRepository pointerRepository,
        IIncomingWebhookMessageRepository messageRepository,
        IIncomingWebhookEffectReceiptRepository receiptRepository,
        IUnitOfWork unitOfWork,
        IMediator mediator,
        IOptions<IncomingWebhookProcessingSettings> settings,
        TimeProvider timeProvider) : this(
        pointerRepository,
        messageRepository,
        receiptRepository,
        new MissingRegistrationProviderSubscriptionStateRepository(),
        unitOfWork,
        mediator,
        settings,
        timeProvider)
    {
    }

    private const string InvalidPayloadCategory = "coop_effect_payload_invalid";
    private const string TenantMismatchCategory = "coop_effect_tenant_mismatch";
    private const string IdentityMismatchCategory = "coop_effect_identity_mismatch";
    private const string PayloadUnavailableCategory = "coop_effect_payload_unavailable";
    private const string CommandRejectedCategory = "coop_effect_command_rejected";
    private const string TransientFailureCategory = "coop_effect_transient_failure";

    private readonly IncomingWebhookProcessingSettings _settings = settings.Value;

    public async Task<IncomingWebhookClaimExecutionResult> ProcessAsync(
        IncomingWebhookEffectClaim claim,
        CancellationToken cancellationToken)
    {
        var observedAt = GetUtcNow();
        var active = await pointerRepository.GetActiveClaimAsync(claim, observedAt, cancellationToken);
        if (active is null)
        {
            return IncomingWebhookClaimExecutionResult.LeaseLost();
        }

        var message = active.IncomingWebhookMessage ??
            await messageRepository.GetByTenantAndIdForUpdateAsync(
                active.TenantId,
                active.IncomingWebhookMessageId,
                cancellationToken);
        if (message is null || message.PayloadBytes.IsEmpty)
        {
            return await TransitionAsync(
                claim,
                PayloadUnavailableCategory,
                "The retained Coop callback payload is unavailable.",
                retry: false,
                cancellationToken);
        }

        if (message.TenantId != active.TenantId)
        {
            return await TransitionAsync(
                claim,
                TenantMismatchCategory,
                "The retained Coop callback tenant does not match its effect pointer.",
                retry: false,
                cancellationToken);
        }

        if (string.Equals(active.EffectKind, ProcessProviderSubmissionEffectCommandHandler.StableEffectKind, StringComparison.Ordinal))
        {
            return await ProcessRegistrationProviderSubmissionAsync(claim, active, message, cancellationToken);
        }

        if (string.Equals(active.EffectKind, RegistrationProviderSubmissionIncomingWebhookHandler.ResponseSweepEffectKind, StringComparison.Ordinal))
        {
            return await ProcessRegistrationProviderResponseSweepAsync(claim, active, message, cancellationToken);
        }

        if (string.Equals(active.EffectKind, QueueManualRegistrationProviderImportCommandHandler.ManualImportEffectKind, StringComparison.Ordinal))
        {
            return await TransitionAsync(
                claim,
                "manual_import_pending",
                "Manual registration-provider import requires organizer reconciliation.",
                retry: false,
                cancellationToken);
        }

        if (!HasMatchingEnvelope(active, message))
        {
            return await TransitionAsync(
                claim,
                IdentityMismatchCategory,
                "The retained Coop callback identity does not match its effect pointer.",
                retry: false,
                cancellationToken);
        }

        CoopDecisionCallbackRequestDto? request;
        try
        {
            request = JsonSerializer.Deserialize(
                message.PayloadBytes.Span,
                ExploreJsonContext.Default.CoopDecisionCallbackRequestDto);
        }
        catch (JsonException)
        {
            return await TransitionAsync(
                claim,
                InvalidPayloadCategory,
                "The retained Coop callback payload is not valid JSON.",
                retry: false,
                cancellationToken);
        }

        if (request is null)
        {
            return await TransitionAsync(
                claim,
                InvalidPayloadCategory,
                "The retained Coop callback payload is empty.",
                retry: false,
                cancellationToken);
        }

        if (ProcessCoopDecisionCallbackCommandValidator.ResolveTenantId(request) != active.TenantId)
        {
            return await TransitionAsync(
                claim,
                TenantMismatchCategory,
                "The Coop callback payload tenant does not match its effect pointer.",
                retry: false,
                cancellationToken);
        }

        var providerDecisionId = ProcessCoopDecisionCallbackCommandValidator.FirstNonBlank(
            request.ProviderDecisionId,
            request.ProviderDecisionIdSnake);
        if (!string.Equals(providerDecisionId, active.ProviderDecisionId, StringComparison.Ordinal))
        {
            return await TransitionAsync(
                claim,
                IdentityMismatchCategory,
                "The Coop callback decision identity does not match its effect pointer.",
                retry: false,
                cancellationToken);
        }

        try
        {
            var response = await mediator.Send(
                new ProcessCoopDecisionCallbackCommand { Request = request },
                cancellationToken);
            if (response.IsSuccess)
            {
                return await CompleteAsync(claim, cancellationToken);
            }

            var retry = string.Equals(
                response.FailureCode,
                EventReportFailureCodes.CaseConcurrencyConflict,
                StringComparison.Ordinal);
            return await TransitionAsync(
                claim,
                retry ? TransientFailureCategory : CommandRejectedCategory,
                retry
                    ? "The Coop decision conflicted with a concurrent report-case transition."
                    : "The Coop decision was rejected by the local moderation workflow.",
                retry,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IncomingWebhookEffectReceiptConflictException)
        {
            return await RecoverConcurrentReceiptAsync(claim, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await TransitionAsync(
                claim,
                TransientFailureCategory,
                "The Coop decision could not be completed because of a transient failure.",
                retry: true,
                cancellationToken);
        }
    }

    private Task<IncomingWebhookClaimExecutionResult> CompleteAsync(
        IncomingWebhookEffectClaim claim,
        CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var pointer = await pointerRepository.GetByTenantAndIdForUpdateAsync(
                claim.TenantId,
                claim.EffectOutboxId,
                token);
            if (pointer is null)
            {
                return IncomingWebhookClaimExecutionResult.LeaseLost();
            }

            var receipt = await receiptRepository.GetByIdentityAsync(
                pointer.TenantId,
                pointer.IncomingWebhookMessageId,
                pointer.EffectKind,
                token);
            if (receipt is null)
            {
                receipt = IncomingWebhookEffectReceipt.Create(
                    pointer.TenantId,
                    pointer.IncomingWebhookMessageId,
                    pointer.EffectKind,
                    pointer.PayloadSha256,
                    pointer.ProcessingGeneration,
                    GetUtcNow(),
                    $"coop-effect:{pointer.Id:N}");
                await receiptRepository.AddAsync(receipt, token);
            }
            else
            {
                receipt.EnsureMatches(
                    pointer.TenantId,
                    pointer.IncomingWebhookMessageId,
                    pointer.EffectKind,
                    pointer.PayloadSha256,
                    pointer.ProcessingGeneration);
            }

            try
            {
                pointer.Complete(
                    claim.LeaseToken,
                    claim.ProcessingFence,
                    claim.ProcessingGeneration,
                    GetUtcNow());
            }
            catch (InvalidOperationException)
            {
                return IncomingWebhookClaimExecutionResult.LeaseLost();
            }

            await pointerRepository.SaveChangesAsync(token);
            return IncomingWebhookClaimExecutionResult.Completed("succeeded");
        }, cancellationToken);

    private Task<IncomingWebhookClaimExecutionResult> TransitionAsync(
        IncomingWebhookEffectClaim claim,
        string failureCategory,
        string safeDetail,
        bool retry,
        CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var pointer = await pointerRepository.GetByTenantAndIdForUpdateAsync(
                claim.TenantId,
                claim.EffectOutboxId,
                token);
            if (pointer is null)
            {
                return IncomingWebhookClaimExecutionResult.LeaseLost();
            }

            var transitionAt = GetUtcNow();
            try
            {
                if (retry && pointer.AttemptCount < _settings.MaxAttempts)
                {
                    pointer.ScheduleRetry(
                        claim.LeaseToken,
                        claim.ProcessingFence,
                        claim.ProcessingGeneration,
                        failureCategory,
                        safeDetail,
                        transitionAt.Add(ComputeRetryDelay(pointer.AttemptCount)),
                        transitionAt);
                }
                else
                {
                    pointer.DeadLetter(
                        claim.LeaseToken,
                        claim.ProcessingFence,
                        claim.ProcessingGeneration,
                        failureCategory,
                        safeDetail,
                        transitionAt);
                }
            }
            catch (InvalidOperationException)
            {
                return IncomingWebhookClaimExecutionResult.LeaseLost();
            }

            await pointerRepository.SaveChangesAsync(token);
            return IncomingWebhookClaimExecutionResult.Completed(
                retry && pointer.Status == OutboxMessageStatus.Failed
                    ? "retry_scheduled"
                    : "dead_lettered");
        }, cancellationToken);

    private async Task<IncomingWebhookClaimExecutionResult> RecoverConcurrentReceiptAsync(
        IncomingWebhookEffectClaim claim,
        CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var pointer = await pointerRepository.GetByTenantAndIdForUpdateAsync(
                claim.TenantId,
                claim.EffectOutboxId,
                token);
            if (pointer is null)
            {
                return IncomingWebhookClaimExecutionResult.LeaseLost();
            }

            var receipt = await receiptRepository.GetByIdentityAsync(
                pointer.TenantId,
                pointer.IncomingWebhookMessageId,
                pointer.EffectKind,
                token);
            if (receipt is null)
            {
                throw new InvalidOperationException("The conflicting Coop effect receipt could not be recovered.");
            }

            receipt.EnsureMatches(
                pointer.TenantId,
                pointer.IncomingWebhookMessageId,
                pointer.EffectKind,
                pointer.PayloadSha256,
                pointer.ProcessingGeneration);
            try
            {
                pointer.Complete(
                    claim.LeaseToken,
                    claim.ProcessingFence,
                    claim.ProcessingGeneration,
                    GetUtcNow());
            }
            catch (InvalidOperationException)
            {
                return IncomingWebhookClaimExecutionResult.LeaseLost();
            }

            await pointerRepository.SaveChangesAsync(token);
            return IncomingWebhookClaimExecutionResult.Completed("recovered");
        }, cancellationToken);
    }

    private static bool HasMatchingEnvelope(
        IncomingWebhookEffectOutbox pointer,
        IncomingWebhookMessage message) =>
        string.Equals(pointer.Provider, "coop", StringComparison.Ordinal) &&
        string.Equals(message.Provider, pointer.Provider, StringComparison.Ordinal) &&
        string.Equals(message.EventType, pointer.EffectKind, StringComparison.Ordinal) &&
        string.Equals(message.ProviderMessageId, pointer.ProviderDecisionId, StringComparison.Ordinal) &&
        string.Equals(message.PayloadHash, pointer.PayloadSha256, StringComparison.Ordinal);

    private async Task<IncomingWebhookClaimExecutionResult> ProcessRegistrationProviderSubmissionAsync(
        IncomingWebhookEffectClaim claim,
        IncomingWebhookEffectOutbox pointer,
        IncomingWebhookMessage message,
        CancellationToken cancellationToken)
    {
        bool sourceEventTypeMatches = string.Equals(message.EventType, pointer.EffectKind, StringComparison.Ordinal) ||
            string.Equals(pointer.EffectKind, ProcessProviderSubmissionEffectCommandHandler.StableEffectKind, StringComparison.Ordinal) &&
            string.Equals(message.EventType, QueueManualRegistrationProviderImportCommandHandler.ManualImportEffectKind, StringComparison.Ordinal);
        if (!string.Equals(message.Provider, pointer.Provider, StringComparison.Ordinal) ||
            !sourceEventTypeMatches ||
            !string.Equals(message.ProviderMessageId, pointer.ProviderDecisionId, StringComparison.Ordinal) ||
            !string.Equals(message.PayloadHash, pointer.PayloadSha256, StringComparison.Ordinal))
        {
            return await TransitionAsync(
                claim,
                "registration_effect_identity_mismatch",
                "The retained registration callback identity does not match its effect pointer.",
                retry: false,
                cancellationToken);
        }

        try
        {
            Guid bindingId = ParseBindingId(pointer.ProviderDecisionId);
            string provider = ReadSafeHeader(message.HeadersJson, "X-Registration-Callback-Provider") ?? "registration-provider";
            ProviderSubmissionEffectResult result = await mediator.Send(
                new ProcessProviderSubmissionEffectCommand(
                    pointer.TenantId,
                    pointer.IncomingWebhookMessageId,
                    bindingId,
                    provider,
                    message.PayloadBytes,
                    ReadSafeHeaders(message.HeadersJson)),
                cancellationToken);
            return result.Outcome switch
            {
                ProviderSubmissionEffectOutcome.Completed => await CompleteAsync(claim, cancellationToken),
                ProviderSubmissionEffectOutcome.Retryable => await TransitionAsync(
                    claim,
                    result.Code.ToLowerInvariant(),
                    "The registration provider submission read failed transiently.",
                    retry: true,
                    cancellationToken),
                _ => await TransitionAsync(
                    claim,
                    result.Code.ToLowerInvariant(),
                    "The registration provider submission needs reconciliation.",
                    retry: false,
                    cancellationToken)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            return await TransitionAsync(
                claim,
                "malformed_evidence",
                "The retained registration callback evidence is malformed.",
                retry: false,
                cancellationToken);
        }
        catch (FormatException)
        {
            return await TransitionAsync(
                claim,
                "malformed_evidence",
                "The retained registration callback evidence is malformed.",
                retry: false,
                cancellationToken);
        }
        catch (JsonException)
        {
            return await TransitionAsync(
                claim,
                "malformed_evidence",
                "The retained registration callback evidence is malformed.",
                retry: false,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await TransitionAsync(
                claim,
                "registration_effect_transient_failure",
                "The registration provider submission could not be completed because of a transient failure.",
                retry: true,
                cancellationToken);
        }
    }

    private async Task<IncomingWebhookClaimExecutionResult> ProcessRegistrationProviderResponseSweepAsync(
        IncomingWebhookEffectClaim claim,
        IncomingWebhookEffectOutbox pointer,
        IncomingWebhookMessage message,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(message.Provider, pointer.Provider, StringComparison.Ordinal) ||
            !string.Equals(message.EventType, pointer.EffectKind, StringComparison.Ordinal) ||
            !string.Equals(message.ProviderMessageId, pointer.ProviderDecisionId, StringComparison.Ordinal) ||
            !string.Equals(message.PayloadHash, pointer.PayloadSha256, StringComparison.Ordinal))
        {
            return await TransitionAsync(
                claim,
                "registration_sweep_effect_identity_mismatch",
                "The retained registration callback identity does not match its sweep effect pointer.",
                retry: false,
                cancellationToken);
        }

        try
        {
            Guid bindingId = ParseBindingId(pointer.ProviderDecisionId);
            RegistrationProviderSubscriptionState? state = await subscriptionStateRepository.GetAsync(
                pointer.TenantId,
                bindingId,
                "RESPONSES",
                cancellationToken);
            if (state is null)
            {
                return await TransitionAsync(
                    claim,
                    "registration_subscription_state_missing",
                    "The registration provider subscription state is missing.",
                    retry: false,
                    cancellationToken);
            }

            string? watchId = ReadPubSubWatchId(message.PayloadBytes.Span);
            if (!string.Equals(watchId, state.WatchId, StringComparison.Ordinal))
            {
                return await TransitionAsync(
                    claim,
                    "registration_subscription_watch_mismatch",
                    "The Google Pub/Sub watch identity does not match the persisted subscription state.",
                    retry: false,
                    cancellationToken);
            }

            state.ReceiveNotification(DateTime.SpecifyKind(message.ReceivedAt, DateTimeKind.Utc));
            await subscriptionStateRepository.SaveChangesAsync(cancellationToken);
            return await CompleteAsync(claim, cancellationToken);
        }
        catch (FormatException)
        {
            return await TransitionAsync(
                claim,
                "malformed_evidence",
                "The retained registration callback evidence is malformed.",
                retry: false,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return await TransitionAsync(
                claim,
                "registration_sweep_transient_failure",
                "The registration provider response sweep could not be recorded because of a transient failure.",
                retry: true,
                cancellationToken);
        }
    }

    private static Guid ParseBindingId(string providerDecisionId)
    {
        string prefix = providerDecisionId.Split(':', 2)[0];
        return Guid.TryParseExact(prefix, "N", out Guid bindingId)
            ? bindingId
            : throw new InvalidOperationException("Registration callback binding identity is invalid.");
    }

    private static string? ReadPubSubWatchId(ReadOnlySpan<byte> payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload.ToArray());
        if (!document.RootElement.TryGetProperty("message", out JsonElement message)) return null;
        if (message.TryGetProperty("attributes", out JsonElement attributes) &&
            attributes.TryGetProperty("watchId", out JsonElement attribute) &&
            attribute.ValueKind == JsonValueKind.String)
        {
            return attribute.GetString()?.Trim();
        }

        if (!message.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.String ||
            Convert.FromBase64String(data.GetString() ?? string.Empty) is not { Length: > 0 } decoded)
        {
            return null;
        }

        using JsonDocument body = JsonDocument.Parse(decoded);
        return body.RootElement.TryGetProperty("watchId", out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
    }

    private static IReadOnlyDictionary<string, string> ReadSafeHeaders(string? headersJson)
    {
        if (string.IsNullOrWhiteSpace(headersJson)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? ReadSafeHeader(string? headersJson, string name) =>
        ReadSafeHeaders(headersJson).TryGetValue(name, out string? value) ? value : null;

    private TimeSpan ComputeRetryDelay(int attemptCount)
    {
        var exponent = Math.Clamp(attemptCount - 1, 0, 30);
        var multiplier = 1L << exponent;
        var seconds = Math.Min(
            checked((long)_settings.InitialRetryDelaySeconds * multiplier),
            _settings.MaxRetryDelaySeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private DateTime GetUtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed class MissingRegistrationProviderSubscriptionStateRepository : IRegistrationProviderSubscriptionStateRepository
    {
        public Task<RegistrationProviderSubscriptionState?> GetAsync(Guid tenantId, Guid registrationProviderBindingId, string providerEventType, CancellationToken cancellationToken) =>
            Task.FromResult<RegistrationProviderSubscriptionState?>(null);

        public Task<IReadOnlyList<RegistrationProviderSubscriptionState>> ClaimDueRenewalsAsync(int batchSize, DateTime renewBefore, DateTime claimedAt, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RegistrationProviderSubscriptionState>>([]);

        public Task<IReadOnlyList<RegistrationProviderSubscriptionState>> ClaimDueSweepsAsync(int batchSize, DateTime claimedAt, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RegistrationProviderSubscriptionState>>([]);

        public Task<IReadOnlyList<RegistrationProviderSubscriptionState>> GetExpiringAsync(DateTime expiresBefore, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RegistrationProviderSubscriptionState>>([]);

        public Task AddAsync(RegistrationProviderSubscriptionState state, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
