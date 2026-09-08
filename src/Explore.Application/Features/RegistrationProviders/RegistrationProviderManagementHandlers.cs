using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Application.Models.Storage;
using Explore.Application.Responses;
using Explore.Application.Services.Registration;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using FluentValidation;
using MediatR;
using static Explore.Application.Features.RegistrationProviders.Commands.RegistrationProviderManagementHandlerHelpers;

namespace Explore.Application.Features.RegistrationProviders.Commands;

public sealed class GetRegistrationProviderHealthQueryHandler(
    IRegistrationProviderRepository providerRepository,
    IRegistrationProviderRegistry providerRegistry,
    TimeProvider timeProvider)
    : IRequestHandler<GetRegistrationProviderHealthQuery, IReadOnlyList<RegistrationProviderBindingHealthDto>>
{
    public async Task<IReadOnlyList<RegistrationProviderBindingHealthDto>> Handle(GetRegistrationProviderHealthQuery request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        IReadOnlyList<RegistrationProviderBinding> bindings = await providerRepository.GetBindingsForEventAsync(request.TenantId, request.EventId, cancellationToken);
        var result = new List<RegistrationProviderBindingHealthDto>(bindings.Count);
        foreach (RegistrationProviderBinding binding in bindings)
        {
            DateTime? lastCallbackAt = await providerRepository.GetLastCallbackAtAsync(request.TenantId, binding.Id, cancellationToken);
            int queueDepth = await providerRepository.CountParkedItemsAsync(request.TenantId, binding.Id, cancellationToken);
            DateTime? oldestPendingAt = await providerRepository.GetOldestPendingItemAtAsync(request.TenantId, binding.Id, cancellationToken);
            int? callbackAge = lastCallbackAt.HasValue ? Math.Max(0, (int)(now - lastCallbackAt.Value).TotalSeconds) : null;
            int lag = oldestPendingAt.HasValue ? Math.Max(0, (int)(now - oldestPendingAt.Value).TotalSeconds) : 0;
            result.Add(new RegistrationProviderBindingHealthDto
            {
                TenantId = request.TenantId,
                EventId = request.EventId,
                BindingId = binding.Id,
                ConnectionId = binding.RegistrationProviderConnectionId,
                ProviderKind = binding.Connection?.ProviderKindId.ToString() ?? "unknown",
                BindingStatus = ((RegistrationProviderBindingStateEnum)binding.StateId).ToString(),
                ConnectionValidity = binding.Connection is null || binding.Connection.IsDeleted ? "invalid" : "valid",
                LastCallbackAt = lastCallbackAt,
                LastCallbackAgeSeconds = callbackAge,
                CallbackAgeClass = callbackAge is null ? "none" : callbackAge <= 3600 ? "fresh" : callbackAge <= 86400 ? "aging" : "stale",
                DriftClass = ((RegistrationProviderDriftClassEnum)binding.DriftClassId).ToString(),
                ReconciliationLagSeconds = lag,
                ReconciliationLagClass = queueDepth > 0 ? "parked" : "current",
                ParkedQueueDepth = queueDepth,
                CapabilityCodes = binding.Capabilities.Where(capability => !capability.IsDeleted).Select(capability => capability.CapabilityCode).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray()
            });
        }

        return result;
    }
}

public sealed class GetRegistrationProviderQueueQueryHandler(IRegistrationProviderRepository providerRepository)
    : IRequestHandler<GetRegistrationProviderQueueQuery, IReadOnlyList<RegistrationProviderParkedQueueItemDto>>
{
    public async Task<IReadOnlyList<RegistrationProviderParkedQueueItemDto>> Handle(GetRegistrationProviderQueueQuery request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        int limit = Math.Clamp(request.Limit, 1, 100);
        IReadOnlyList<RegistrationProviderParkedItem> rows = await providerRepository.GetParkedItemsForEventAsync(request.TenantId, request.EventId, limit, cancellationToken);
        return rows.Select(row => row.Submission is { } submission ? new RegistrationProviderParkedQueueItemDto
        {
            TenantId = submission.Submission.TenantId,
            EventId = submission.Submission.EventId,
            BindingId = submission.Submission.RegistrationProviderBindingId ?? Guid.Empty,
            SubmissionId = submission.Submission.Id,
            Status = ((RegistrationSubmissionStatusEnum)submission.Submission.StatusId).ToString(),
            FailureCategory = "submission_issue",
            IssueCodes = submission.Issues.Select(issue => issue.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            CreatedAt = submission.Submission.CreatedAt,
            ProcessingGeneration = 1
        } : new RegistrationProviderParkedQueueItemDto
        {
            TenantId = row.Effect!.Effect.TenantId,
            EventId = row.Effect.EventId,
            BindingId = row.Effect.BindingId,
            EffectOutboxId = row.Effect.Effect.Id,
            Status = row.Effect.Effect.Status.ToString(),
            FailureCategory = SafeCategory(row.Effect.Effect.FailureCategory),
            IssueCodes = string.IsNullOrWhiteSpace(row.Effect.Effect.FailureCategory) ? [] : [SafeCategory(row.Effect.Effect.FailureCategory)],
            CreatedAt = row.Effect.Effect.CreatedAt,
            NextAttemptAt = row.Effect.Effect.NextAttemptAt,
            ProcessingGeneration = row.Effect.Effect.ProcessingGeneration
        }).ToArray();
    }
}

public sealed class PollRegistrationProviderReconciliationCommandHandler(
    IRegistrationProviderRepository providerRepository,
    IRegistrationProviderRegistry providerRegistry,
    BusinessMetrics? metrics = null)
    : IRequestHandler<PollRegistrationProviderReconciliationCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(PollRegistrationProviderReconciliationCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        if (request.BindingId == Guid.Empty || request.SinceUtc == default || request.SinceUtc.Kind != DateTimeKind.Utc)
        {
            metrics?.RecordRegistrationProviderManagementAction("poll_reconciliation", "validation_failed");
            return Failure(request.BindingId, "registration_provider_reconciliation_validation_failed", "Reconciliation checkpoint is invalid.");
        }

        RegistrationProviderBinding? binding = await providerRepository.GetBindingAsync(request.TenantId, request.BindingId, cancellationToken);
        if (binding is null || !await BindingBelongsToEventAsync(providerRepository, request, cancellationToken))
        {
            metrics?.RecordRegistrationProviderManagementAction("poll_reconciliation", "not_found");
            return Failure(request.BindingId, "registration_provider_binding_not_found", "Registration provider binding was not found.");
        }

        RegistrationProviderCapability? capability = binding.Capabilities.FirstOrDefault(capability => !capability.IsDeleted && string.Equals(capability.CapabilityCode, RegistrationProviderCapabilityCodes.Reconciliation, StringComparison.OrdinalIgnoreCase));
        if (capability is null)
        {
            metrics?.RecordRegistrationProviderManagementAction("poll_reconciliation", "unsupported");
            return Failure(request.BindingId, "registration_provider_reconciliation_unsupported", "Registration provider reconciliation is not configured for this binding.");
        }

        RegistrationProviderTuple tuple = binding.Connection is null ? RegistrationProviderTuple.Empty : TupleFromConnection(binding.Connection);
        IRegistrationProviderDescriptor? descriptor = providerRegistry.TryResolve(tuple);
        if (descriptor is not IRegistrationProviderReconciliationProvider reconciler || !descriptor.ProvenCapabilities.Reconciliation)
        {
            metrics?.RecordRegistrationProviderManagementAction("poll_reconciliation", "unknown_capability");
            return Failure(request.BindingId, "registration_provider_reconciliation_unknown", "Registration provider reconciliation capability is not available.");
        }

        RegistrationProviderReconciliationResult result = await reconciler.ReconcileAsync(new RegistrationProviderReconciliationRequest(request.TenantId, binding, binding.Connection!, tuple, request.SinceUtc), cancellationToken);
        metrics?.RecordRegistrationProviderManagementAction("poll_reconciliation", "accepted");
        return Success(request.BindingId, $"Reconciliation observed {result.ObservedSubmissionCount} bounded provider submissions.");
    }
}

public sealed class QueueManualRegistrationProviderImportCommandHandler(
    IRegistrationProviderRepository providerRepository,
    IIncomingWebhookMessageRepository messageRepository,
    IIncomingWebhookEffectOutboxRepository effectRepository,
    IRegistrationProviderCallbackReceiptProtector receiptProtector,
    ISender sender,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    BusinessMetrics? metrics = null)
    : IRequestHandler<QueueManualRegistrationProviderImportCommand, BaseCommandResponse<Guid>>
{
    public const string ManualImportEffectKind = "registration.provider_manual_import";

    public async Task<BaseCommandResponse<Guid>> Handle(QueueManualRegistrationProviderImportCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        if (request.BindingId == Guid.Empty || !Guid.TryParse(request.StorageReference, out Guid storageObjectId) ||
            storageObjectId == Guid.Empty || !BoundedReference(request.SourceReference, 200))
        {
            metrics?.RecordRegistrationProviderManagementAction("manual_import", "validation_failed");
            return Failure(request.BindingId, "registration_provider_manual_import_validation_failed", "Manual import requires bounded storage/reference metadata.");
        }

        RegistrationProviderBinding? binding = await providerRepository.GetBindingAsync(request.TenantId, request.BindingId, cancellationToken);
        if (binding is null || !await BindingBelongsToEventAsync(providerRepository, request.TenantId, request.EventId, request.BindingId, cancellationToken))
        {
            metrics?.RecordRegistrationProviderManagementAction("manual_import", "not_found");
            return Failure(request.BindingId, "registration_provider_binding_not_found", "Registration provider binding was not found for this event.");
        }

        bool manualSupported = binding.Capabilities.Any(capability => !capability.IsDeleted &&
            string.Equals(capability.CapabilityCode, RegistrationProviderCapabilityCodes.Manual, StringComparison.OrdinalIgnoreCase));
        if (!manualSupported || binding.Connection is null || string.IsNullOrWhiteSpace(binding.ProviderSurveyId))
        {
            metrics?.RecordRegistrationProviderManagementAction("manual_import", "unsupported");
            return Failure(request.BindingId, "registration_provider_manual_import_unsupported", "Manual import is not supported by this provider binding.");
        }

        StorageObjectContentResult? content = await sender.Send(new GetStorageObjectContentRequest
        {
            StorageObjectId = storageObjectId,
            TenantId = request.TenantId
        }, cancellationToken);
        if (content is null || content.Length is <= 0 or > 1_048_576)
        {
            return Failure(request.BindingId, "registration_provider_manual_import_file_invalid", "Manual import requires a CSV file no larger than 1 MiB.");
        }

        string csv;
        await using (content.Content)
        using (var reader = new StreamReader(content.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false))
        {
            csv = await reader.ReadToEndAsync(cancellationToken);
        }

        IReadOnlyList<IReadOnlyList<string>> rows;
        try
        {
            rows = ParseCsv(csv);
        }
        catch (FormatException)
        {
            return Failure(request.BindingId, "registration_provider_manual_import_csv_invalid", "Manual import CSV is malformed.");
        }
        if (rows.Count is < 2 or > 501)
        {
            return Failure(request.BindingId, "registration_provider_manual_import_row_count_invalid", "Manual import CSV requires one header row and at most 500 response rows.");
        }

        IReadOnlyList<string> headers = rows[0];
        string[] requiredColumns = ["responseId", "attemptId", "attemptToken", "timestamp"];
        string[] normalizedHeaders = [.. headers.Select(name => name.Trim())];
        if (normalizedHeaders.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Count ||
            requiredColumns.Any(column => !normalizedHeaders.Contains(column, StringComparer.OrdinalIgnoreCase)))
        {
            return Failure(request.BindingId, "registration_provider_manual_import_columns_invalid", "Manual import CSV is missing required identity columns or contains duplicate headers.");
        }
        Dictionary<string, int> columns = normalizedHeaders.Select((name, index) => (name, index))
            .ToDictionary(column => column.name, column => column.index, StringComparer.OrdinalIgnoreCase);

        List<(string ResponseId, Guid AttemptId, string AttemptToken, DateTimeOffset Timestamp, Dictionary<string, string> MappedValues)> importRows = [];
        foreach (IReadOnlyList<string> row in rows.Skip(1))
        {
            if (row.Count != headers.Count || !Guid.TryParse(row[columns["attemptId"]], out Guid attemptId) || attemptId == Guid.Empty ||
                !DateTimeOffset.TryParse(row[columns["timestamp"]], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset timestamp) ||
                string.IsNullOrWhiteSpace(row[columns["responseId"]]) || string.IsNullOrWhiteSpace(row[columns["attemptToken"]]))
            {
                return Failure(request.BindingId, "registration_provider_manual_import_row_invalid", "Manual import CSV contains an invalid response identity row.");
            }

            Dictionary<string, string> mappedValues = columns
                .Where(column => !requiredColumns.Contains(column.Key, StringComparer.OrdinalIgnoreCase))
                .ToDictionary(column => column.Key, column => row[column.Value], StringComparer.OrdinalIgnoreCase);
            importRows.Add((
                row[columns["responseId"]].Trim(),
                attemptId,
                row[columns["attemptToken"]].Trim(),
                timestamp,
                mappedValues));
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        RegistrationProviderTuple tuple = TupleFromConnection(binding.Connection);
        List<(IncomingWebhookMessage Message, string ProviderDecisionId, string Hash)> imports = [];
        foreach (var row in importRows)
        {
            string providerDecisionId = $"{binding.Id:N}:{row.ResponseId}";
            byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                providerCode = binding.Connection.ProviderCode,
                bindingId = binding.Id,
                formId = binding.ProviderSurveyId,
                responseId = row.ResponseId,
                attemptId = row.AttemptId,
                attemptToken = row.AttemptToken,
                timestamp = row.Timestamp,
                mappedValues = row.MappedValues,
                contractVersion = binding.Connection.ApiVersion,
                idempotencyKey = $"{binding.ProviderSurveyId}:{row.ResponseId}"
            });
            string hash = Sha256Identifier(payloadBytes);
            string receipt = receiptProtector.Protect(new RegistrationProviderCallbackReceipt(
                request.TenantId, binding.Connection.Id, binding.Id, binding.Connection.ProviderCode, tuple.Key,
                hash, row.ResponseId, now, Guid.CreateVersion7().ToString("N")));
            string headersJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["X-Registration-Callback-Provider"] = binding.Connection.ProviderCode,
                ["X-Registration-Verification-Receipt"] = receipt
            });
            IncomingWebhookMessage message = IncomingWebhookMessage.CreateVerified(
                request.TenantId, "registration-provider", providerDecisionId, providerDecisionId,
                ManualImportEffectKind, payloadBytes, hash, "application/json", "utf-8",
                headersJson, now, now, now.AddDays(7), "registration-provider-manual-import-v1",
                now.AddDays(30), now.AddDays(30), now.AddDays(7), now.AddDays(30));
            imports.Add((message, providerDecisionId, hash));
        }

        (int accepted, int skipped) = await unitOfWork.ExecuteInTransactionAsync(async transactionToken =>
        {
            int acceptedRows = 0;
            int skippedRows = 0;
            foreach (var import in imports)
            {
                if (await messageRepository.GetByProviderMessageIdForUpdateAsync(
                        request.TenantId, "registration-provider", import.ProviderDecisionId, transactionToken) is not null ||
                    !await messageRepository.TryCreateAsync(import.Message, transactionToken))
                {
                    skippedRows++;
                    continue;
                }

                IncomingWebhookEffectOutbox effect = IncomingWebhookEffectOutbox.CreatePending(
                    request.TenantId, import.Message.Id, "registration-provider", import.ProviderDecisionId,
                    ProcessProviderSubmissionEffectCommandHandler.StableEffectKind, import.Hash, now);
                await effectRepository.AddAsync(effect, transactionToken);
                acceptedRows++;
            }

            await effectRepository.SaveChangesAsync(transactionToken);
            return (acceptedRows, skippedRows);
        }, cancellationToken);
        metrics?.RecordRegistrationProviderManagementAction("manual_import", "accepted");
        return Success(request.BindingId, $"Manual import queued {accepted} response rows and skipped {skipped} existing responses.");
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseCsv(string csv)
    {
        List<IReadOnlyList<string>> rows = [];
        List<string> row = [];
        StringBuilder field = new();
        bool quoted = false;
        for (int index = 0; index < csv.Length; index++)
        {
            char current = csv[index];
            if (current == '"')
            {
                if (quoted && index + 1 < csv.Length && csv[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (!quoted && current == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (!quoted && current is '\r' or '\n')
            {
                if (current == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n') index++;
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(value => value.Length > 0)) rows.Add([.. row]);
                row.Clear();
            }
            else
            {
                field.Append(current);
            }
        }
        if (quoted) throw new FormatException("CSV contains an unterminated quoted field.");
        row.Add(field.ToString());
        if (row.Any(value => value.Length > 0)) rows.Add([.. row]);
        return rows;
    }
}

public sealed class RetryRegistrationProviderParkedItemCommandHandler(
    IRegistrationProviderRepository providerRepository,
    IIncomingWebhookEffectOutboxRepository effectRepository,
    IIncomingWebhookEffectReceiptRepository receiptRepository,
    TimeProvider timeProvider,
    BusinessMetrics? metrics = null)
    : IRequestHandler<RetryRegistrationProviderParkedItemCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(RetryRegistrationProviderParkedItemCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        if (request.EffectOutboxId is not { } effectId || request.ExpectedProcessingGeneration is not { } generation)
        {
            metrics?.RecordRegistrationProviderManagementAction("retry_parked_item", "no_effect");
            return Failure(request.SubmissionId ?? Guid.Empty, "registration_provider_retry_no_effect", "Only retained effect retries are currently replayable.");
        }

        IncomingWebhookEffectOutbox? pointer = await effectRepository.GetByTenantAndIdForUpdateAsync(request.TenantId, effectId, cancellationToken);
        if (pointer is null || !await ValidateRetainedEffectAsync(providerRepository, receiptRepository, pointer, request.TenantId, request.EventId, cancellationToken))
        {
            metrics?.RecordRegistrationProviderManagementAction("retry_parked_item", "not_found");
            return Failure(effectId, "registration_provider_effect_not_found", "Registration provider effect was not found.");
        }

        if (pointer.Status == OutboxMessageStatus.Pending)
        {
            metrics?.RecordRegistrationProviderManagementAction("retry_parked_item", "already_pending");
            return Success(pointer.Id, "Registration provider effect retry was already pending.");
        }

        if (pointer.Status != OutboxMessageStatus.DeadLettered || pointer.ProcessingGeneration != generation)
        {
            metrics?.RecordRegistrationProviderManagementAction("retry_parked_item", "not_eligible");
            return Failure(pointer.Id, "registration_provider_effect_retry_not_eligible", "Registration provider effect retry is not eligible.");
        }

        pointer.Redrive(generation, timeProvider.GetUtcNow().UtcDateTime);
        await effectRepository.SaveChangesAsync(cancellationToken);
        metrics?.RecordRegistrationProviderManagementAction("retry_parked_item", "scheduled");
        return Success(pointer.Id, "Registration provider effect retry scheduled.");
    }
}

public sealed class ResolveRegistrationProviderQueueItemCommandHandler(
    IRegistrationProviderRepository providerRepository,
    IIncomingWebhookEffectOutboxRepository effectRepository,
    IIncomingWebhookEffectReceiptRepository receiptRepository,
    TimeProvider timeProvider,
    BusinessMetrics? metrics = null)
    : IRequestHandler<ResolveRegistrationProviderQueueItemCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(ResolveRegistrationProviderQueueItemCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        bool hasSubmission = request.SubmissionId is { } submissionId && submissionId != Guid.Empty;
        bool hasEffect = request.EffectOutboxId is { } requestedEffectId && requestedEffectId != Guid.Empty;
        bool valid = hasSubmission != hasEffect && BoundedReference(request.DecisionCode, 100) &&
            (string.IsNullOrWhiteSpace(request.NoteReference) || BoundedReference(request.NoteReference, 300));
        if (!valid)
        {
            metrics?.RecordRegistrationProviderManagementAction("resolve_queue_item", "validation_failed");
            return Failure(request.SubmissionId ?? request.EffectOutboxId ?? Guid.Empty, "registration_provider_resolution_validation_failed", "Resolution decision is invalid.");
        }

        if (hasEffect)
        {
            Guid effectId = request.EffectOutboxId!.Value;
            IncomingWebhookEffectOutbox? effect = await effectRepository.GetByTenantAndIdForUpdateAsync(request.TenantId, effectId, cancellationToken);
            if (effect is null || !await ValidateRetainedEffectAsync(providerRepository, receiptRepository, effect, request.TenantId, request.EventId, cancellationToken))
            {
                metrics?.RecordRegistrationProviderManagementAction("resolve_queue_item", "not_found");
                return Failure(effectId, "registration_provider_queue_item_not_found", "Registration provider queue item was not found.");
            }

            effect.AcknowledgeResolution("organizer_" + SafeCategory(request.DecisionCode), timeProvider.GetUtcNow().UtcDateTime);
            await effectRepository.SaveChangesAsync(cancellationToken);
            metrics?.RecordRegistrationProviderManagementAction("resolve_queue_item", "acknowledged");
            return Success(effect.Id, "Registration provider reconciliation decision acknowledged.");
        }

        RegistrationSubmission? submission = await providerRepository.GetParkedSubmissionAsync(request.TenantId, request.EventId, request.SubmissionId!.Value, cancellationToken);
        if (submission is null)
        {
            metrics?.RecordRegistrationProviderManagementAction("resolve_queue_item", "not_found");
            return Failure(request.SubmissionId.Value, "registration_provider_queue_item_not_found", "Registration provider queue item was not found.");
        }

        await providerRepository.AddSubmissionIssueAsync(RegistrationSubmissionIssue.Create(submission, "RESOLVED_" + SafeCategory(request.DecisionCode), timeProvider.GetUtcNow().UtcDateTime), cancellationToken);
        await providerRepository.SaveChangesAsync(cancellationToken);
        metrics?.RecordRegistrationProviderManagementAction("resolve_queue_item", "acknowledged");
        return Success(submission.Id, "Registration provider reconciliation decision acknowledged.");
    }
}

public sealed class GetRegistrationProviderConnectionsQueryHandler(IRegistrationProviderRepository providerRepository)
    : IRequestHandler<GetRegistrationProviderConnectionsQuery, IReadOnlyList<RegistrationProviderConnectionDto>>
{
    public async Task<IReadOnlyList<RegistrationProviderConnectionDto>> Handle(GetRegistrationProviderConnectionsQuery request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        return [.. (await providerRepository.GetConnectionsAsync(request.TenantId, cancellationToken)).Select(connection => ToConnectionDto(connection, request.EventId))];
    }
}

public sealed class GetRegistrationProviderConnectionQueryHandler(IRegistrationProviderRepository providerRepository)
    : IRequestHandler<GetRegistrationProviderConnectionQuery, RegistrationProviderConnectionDto?>
{
    public async Task<RegistrationProviderConnectionDto?> Handle(GetRegistrationProviderConnectionQuery request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        RegistrationProviderConnection? connection = await providerRepository.GetConnectionAsync(request.TenantId, request.ConnectionId, cancellationToken);
        return connection is null ? null : ToConnectionDto(connection, request.EventId);
    }
}

public sealed class UpsertRegistrationProviderConnectionCommandHandler(
    IRegistrationProviderRepository providerRepository,
    IRegistrationProviderRegistry providerRegistry,
    TimeProvider timeProvider)
    : IRequestHandler<UpsertRegistrationProviderConnectionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(UpsertRegistrationProviderConnectionCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        if (!TryConnectionRequest(request.Request, providerRegistry, out RegistrationProviderKindEnum kind, out RegistrationProviderDeploymentKindEnum deployment))
            return Failure(request.ConnectionId ?? Guid.Empty, "registration_provider_connection_validation_failed", "Registration provider connection is invalid.");

        Guid? webhookSecretBindingId = RequiresSharedConnectionWebhookSecret(request.Request, providerRegistry)
            ? request.Request.WebhookSecretBindingId
            : null;

        if (request.ConnectionId is { } id)
        {
            RegistrationProviderConnection? connection = await providerRepository.GetConnectionAsync(request.TenantId, id, cancellationToken);
            if (connection is null) return Failure(id, "registration_provider_connection_not_found", "Registration provider connection was not found.");
            connection.Update(request.Request.Name, kind, deployment, request.Request.ProviderCode, request.Request.ProviderDeploymentCode,
                request.Request.ApiVersion, request.Request.AdapterPolicyVersion, request.Request.ConformanceEvidenceRevision,
                request.Request.ManagementApiBaseUrl, request.Request.PublicBaseUrl, request.Request.ProviderWorkspaceId,
                request.Request.ApiTokenSecretBindingId, webhookSecretBindingId);
            connection.UpdateOAuthMetadata(request.Request.GrantedOAuthScopes, request.Request.ProviderIdentity, request.Request.PubSubConfigurationReference);
            await providerRepository.SaveChangesAsync(cancellationToken);
            return Success(connection.Id, "Registration provider connection updated.");
        }

        RegistrationProviderConnection created = RegistrationProviderConnection.Create(request.TenantId, request.Request.Name, kind, deployment,
            request.Request.ProviderCode, request.Request.ProviderDeploymentCode, request.Request.ApiVersion,
            request.Request.AdapterPolicyVersion, request.Request.ConformanceEvidenceRevision, request.Request.ManagementApiBaseUrl,
            request.Request.PublicBaseUrl, request.Request.ProviderWorkspaceId, request.Request.ApiTokenSecretBindingId,
            webhookSecretBindingId, timeProvider.GetUtcNow().UtcDateTime);
        created.UpdateOAuthMetadata(request.Request.GrantedOAuthScopes, request.Request.ProviderIdentity, request.Request.PubSubConfigurationReference);
        await providerRepository.AddConnectionAsync(created, cancellationToken);
        await providerRepository.SaveChangesAsync(cancellationToken);
        return Success(created.Id, "Registration provider connection created.");
    }
}

public sealed class ReplaceRegistrationProviderApprovedOriginsCommandHandler(IRegistrationProviderRepository providerRepository, TimeProvider timeProvider)
    : IRequestHandler<ReplaceRegistrationProviderApprovedOriginsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(ReplaceRegistrationProviderApprovedOriginsCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        RegistrationProviderConnection? connection = await providerRepository.GetConnectionAsync(request.TenantId, request.ConnectionId, cancellationToken);
        if (connection is null) return Failure(request.ConnectionId, "registration_provider_connection_not_found", "Registration provider connection was not found.");
        try
        {
            connection.ReplaceApprovedOrigins(request.Origins, timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (ArgumentException ex)
        {
            return Failure(request.ConnectionId, "registration_provider_origin_validation_failed", ex.Message);
        }

        await providerRepository.SaveChangesAsync(cancellationToken);
        return Success(connection.Id, "Registration provider approved origins replaced.");
    }
}

public sealed class DeleteRegistrationProviderConnectionCommandHandler(IRegistrationProviderRepository providerRepository, TimeProvider timeProvider)
    : IRequestHandler<DeleteRegistrationProviderConnectionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(DeleteRegistrationProviderConnectionCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        RegistrationProviderConnection? connection = await providerRepository.GetConnectionAsync(request.TenantId, request.ConnectionId, cancellationToken);
        if (connection is null) return Failure(request.ConnectionId, "registration_provider_connection_not_found", "Registration provider connection was not found.");
        if ((await providerRepository.GetBindingsAsync(request.TenantId, cancellationToken)).Any(binding => binding.RegistrationProviderConnectionId == request.ConnectionId))
            return Failure(request.ConnectionId, "registration_provider_connection_in_use", "Registration provider connection still has bindings.");
        connection.Remove(timeProvider.GetUtcNow().UtcDateTime);
        await providerRepository.SaveChangesAsync(cancellationToken);
        return Success(connection.Id, "Registration provider connection deleted.");
    }
}

public sealed class GetRegistrationProviderBindingsQueryHandler(IRegistrationProviderRepository providerRepository)
    : IRequestHandler<GetRegistrationProviderBindingsQuery, IReadOnlyList<RegistrationProviderBindingDto>>
{
    public async Task<IReadOnlyList<RegistrationProviderBindingDto>> Handle(GetRegistrationProviderBindingsQuery request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        return [.. (await providerRepository.GetBindingsForEventAsync(request.TenantId, request.EventId, cancellationToken)).Select(binding => ToBindingDto(binding, request.EventId))];
    }
}

public sealed class GetRegistrationProviderBindingQueryHandler(IRegistrationProviderRepository providerRepository)
    : IRequestHandler<GetRegistrationProviderBindingQuery, RegistrationProviderBindingDto?>
{
    public async Task<RegistrationProviderBindingDto?> Handle(GetRegistrationProviderBindingQuery request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        RegistrationProviderBinding? binding = await providerRepository.GetBindingAsync(request.TenantId, request.BindingId, cancellationToken);
        return binding is null || !await BindingBelongsToEventAsync(providerRepository, request.TenantId, request.EventId, request.BindingId, cancellationToken)
            ? null
            : ToBindingDto(binding, request.EventId);
    }
}

public sealed class CreateRegistrationProviderBindingCommandHandler(
    IRegistrationProviderRepository providerRepository,
    ISecretBindingRepository secretBindingRepository,
    IRegistrationProviderRegistry providerRegistry,
    TimeProvider timeProvider)
    : IRequestHandler<CreateRegistrationProviderBindingCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(CreateRegistrationProviderBindingCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        RegistrationProviderConnection? connection = await providerRepository.GetConnectionAsync(request.TenantId, request.Request.ConnectionId, cancellationToken);
        if (connection is null ||
            !await providerRepository.FormVersionBelongsToEventAsync(request.TenantId, request.EventId, request.Request.FormId, request.Request.FormVersionId, cancellationToken) ||
            !TryBindingRequest(request.Request, out RegistrationProviderPresentationModeEnum presentation, out RegistrationProviderCollectionModeEnum collection, out RegistrationProviderCompletionModeEnum completion, out RegistrationProviderTrustLevelEnum trust))
        {
            return Failure(Guid.Empty, "registration_provider_binding_validation_failed", "Registration provider binding is invalid.");
        }

        RegistrationProviderBinding binding = RegistrationProviderBinding.Create(request.TenantId, request.Request.ConnectionId, request.Request.FormId,
            request.Request.FormVersionId, presentation, collection, completion, trust, request.Request.WebhookSecretBindingId, timeProvider.GetUtcNow().UtcDateTime);
        if (!await BindingWebhookSecretIsValidAsync(secretBindingRepository, request.TenantId, binding.Id, binding.WebhookSecretBindingId, cancellationToken))
        {
            return Failure(Guid.Empty, "registration_provider_binding_validation_failed", "Registration provider binding is invalid.");
        }

        ApplyProviderProvisioning(binding, request.Request);
        ReplaceCapabilitiesFromDescriptor(binding, connection, providerRegistry);
        await providerRepository.AddBindingAsync(binding, cancellationToken);
        await providerRepository.SaveChangesAsync(cancellationToken);
        return Success(binding.Id, "Registration provider binding created.");
    }
}

public sealed class UpdateRegistrationProviderBindingCommandHandler(
    IRegistrationProviderRepository providerRepository,
    ISecretBindingRepository secretBindingRepository,
    IRegistrationProviderRegistry providerRegistry)
    : IRequestHandler<UpdateRegistrationProviderBindingCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(UpdateRegistrationProviderBindingCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        RegistrationProviderBinding? binding = await providerRepository.GetBindingAsync(request.TenantId, request.BindingId, cancellationToken);
        if (binding is null || !await BindingBelongsToEventAsync(providerRepository, request.TenantId, request.EventId, request.BindingId, cancellationToken))
            return Failure(request.BindingId, "registration_provider_binding_not_found", "Registration provider binding was not found.");
        RegistrationProviderConnection? connection = await providerRepository.GetConnectionAsync(request.TenantId, request.Request.ConnectionId, cancellationToken);
        if (connection is null ||
            !await providerRepository.FormVersionBelongsToEventAsync(request.TenantId, request.EventId, request.Request.FormId, request.Request.FormVersionId, cancellationToken) ||
            !TryBindingRequest(request.Request, out RegistrationProviderPresentationModeEnum presentation, out RegistrationProviderCollectionModeEnum collection, out RegistrationProviderCompletionModeEnum completion, out RegistrationProviderTrustLevelEnum trust))
            return Failure(request.BindingId, "registration_provider_binding_validation_failed", "Registration provider binding is invalid.");
        try
        {
            if (!await BindingWebhookSecretIsValidAsync(secretBindingRepository, request.TenantId, binding.Id, request.Request.WebhookSecretBindingId, cancellationToken))
            {
                return Failure(request.BindingId, "registration_provider_binding_validation_failed", "Registration provider binding is invalid.");
            }

            binding.UpdateDraft(request.Request.ConnectionId, request.Request.FormId, request.Request.FormVersionId, presentation, collection, completion, trust, request.Request.WebhookSecretBindingId);
            ApplyProviderProvisioning(binding, request.Request);
            ReplaceCapabilitiesFromDescriptor(binding, connection, providerRegistry);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Failure(request.BindingId, "registration_provider_binding_update_failed", ex.Message);
        }

        await providerRepository.SaveChangesAsync(cancellationToken);
        return Success(binding.Id, "Registration provider binding updated.");
    }
}

public sealed class DeleteRegistrationProviderBindingCommandHandler(IRegistrationProviderRepository providerRepository, TimeProvider timeProvider)
    : IRequestHandler<DeleteRegistrationProviderBindingCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(DeleteRegistrationProviderBindingCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        RegistrationProviderBinding? binding = await providerRepository.GetBindingAsync(request.TenantId, request.BindingId, cancellationToken);
        if (binding is null || !await BindingBelongsToEventAsync(providerRepository, request.TenantId, request.EventId, request.BindingId, cancellationToken))
            return Failure(request.BindingId, "registration_provider_binding_not_found", "Registration provider binding was not found.");
        try
        {
            binding.Remove(timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (InvalidOperationException ex)
        {
            return Failure(request.BindingId, "registration_provider_binding_delete_failed", ex.Message);
        }

        await providerRepository.SaveChangesAsync(cancellationToken);
        return Success(binding.Id, "Registration provider binding deleted.");
    }
}

public sealed class PublishEventRegistrationProviderBindingCommandHandler(
    IRegistrationProviderRepository providerRepository,
    IMediator mediator,
    IRegistrationProviderManagedPublishPreflight managedPublishPreflight,
    TimeProvider timeProvider)
    : IRequestHandler<PublishEventRegistrationProviderBindingCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(PublishEventRegistrationProviderBindingCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        RegistrationProviderBinding? binding = await providerRepository.GetBindingAsync(request.TenantId, request.BindingId, cancellationToken);
        if (binding is null || !await BindingBelongsToEventAsync(providerRepository, request.TenantId, request.EventId, request.BindingId, cancellationToken))
            return Failure(request.BindingId, "registration_provider_binding_not_found", "Registration provider binding was not found.");
        RegistrationProviderManagedPublishPreflightResult preflight = await managedPublishPreflight.RunAsync(
            request.TenantId,
            request.EventId,
            binding,
            cancellationToken);
        if (!preflight.Succeeded)
        {
            string code = preflight.FailureCode ?? "registration_provider_preflight_failed";
            return BaseCommandResponse.Failure<Guid>(code, code, preflight.Errors, request.BindingId);
        }
        return await mediator.Send(new PublishRegistrationProviderBindingCommand(
            request.TenantId,
            request.BindingId,
            ToSchemaDriftClass((RegistrationProviderDriftClassEnum)binding.DriftClassId),
            timeProvider.GetUtcNow().UtcDateTime), cancellationToken);
    }
}

public sealed class ReplaceEventDraftRegistrationProviderMappingsCommandHandler(IRegistrationProviderRepository providerRepository, IMediator mediator)
    : IRequestHandler<ReplaceEventDraftRegistrationProviderMappingsCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(ReplaceEventDraftRegistrationProviderMappingsCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        if (!await BindingBelongsToEventAsync(providerRepository, request.TenantId, request.EventId, request.BindingId, cancellationToken))
            return Failure(request.BindingId, "registration_provider_binding_not_found", "Registration provider binding was not found.");

        return await mediator.Send(new ReplaceDraftRegistrationProviderMappingsCommand(
            request.TenantId,
            request.BindingId,
            [.. request.Request.FieldMappings.Select(mapping => new RegistrationProviderFieldMappingInput(mapping.PlatformFieldKey, mapping.ProviderFieldKey, mapping.IsRequired))],
            [.. request.Request.OptionMappings.Select(mapping => new RegistrationProviderOptionMappingInput(mapping.PlatformFieldKey, mapping.PlatformOptionKey, mapping.ProviderOptionKey))]), cancellationToken);
    }
}

public sealed class ImportExternalRegistrationProviderFormVersionCommandHandler(
    IRegistrationProviderRepository providerRepository,
    IRegistrationProviderRegistry providerRegistry,
    SchemaDriftClassifier driftClassifier,
    FormSchemaArtifactPublicationService publicationService,
    TimeProvider timeProvider)
    : IRequestHandler<ImportExternalRegistrationProviderFormVersionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(ImportExternalRegistrationProviderFormVersionCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        if (!ValidImportRequest(request.Request))
        {
            return Failure(Guid.Empty, "registration_provider_external_import_validation_failed", "External schema import request is invalid.");
        }

        RegistrationProviderConnection? connection = await providerRepository.GetConnectionAsync(request.TenantId, request.ConnectionId, cancellationToken);
        if (connection is null)
        {
            return Failure(request.ConnectionId, "registration_provider_connection_not_found", "Registration provider connection was not found.");
        }

        RegistrationProviderTuple tuple = TupleFromConnection(connection);
        IRegistrationProviderDescriptor? descriptor = providerRegistry.TryResolve(tuple);
        if (descriptor is not IRegistrationProviderSchemaReader schemaReader)
        {
            return Failure(request.ConnectionId, "registration_provider_schema_read_unsupported", "Registration provider schema read is unavailable.");
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        RegistrationProviderBinding transientBinding = TransientBinding(request, connection.Id, now);
        RegistrationProviderSchemaReadResult remoteSchema;
        try
        {
            remoteSchema = await schemaReader.ReadSchemaAsync(new(request.TenantId, transientBinding, connection, tuple), cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException or FormatException)
        {
            return Failure(request.ConnectionId, "registration_provider_schema_read_failed", "Registration provider schema could not be read.");
        }

        if (!remoteSchema.IsActive)
        {
            return Failure(request.ConnectionId, "registration_provider_survey_inactive", "Registration provider schema is not active.");
        }

        CanonicalProviderSchemaSnapshot canonical = CanonicalProviderSchemaSnapshot.From(remoteSchema.Snapshot);
        RegistrationEvidenceHash revisionHash = RegistrationEvidenceHash.Create(canonical.Base64Hash);
        string providerSurveyId = request.Request.ProviderSurveyId.Trim();
        string providerSurveyRevisionId = request.Request.ProviderSurveyRevisionId ?? remoteSchema.Fingerprint;
        RegistrationForm? form = request.Request.FormId is { } formId
            ? await providerRepository.GetFormForExternalImportAsync(request.TenantId, request.EventId, formId, cancellationToken)
            : await providerRepository.GetExternalImportFormAsync(request.TenantId, request.EventId, connection.Id, providerSurveyId, cancellationToken);
        if (request.Request.FormId is not null && form is null)
        {
            return Failure(request.Request.FormId.Value, "registration_form_not_found", "Registration form was not found.");
        }

        RegistrationProviderSchemaDriftClass driftClass = RegistrationProviderSchemaDriftClass.NoDrift;
        RegistrationProviderSchemaRevision? existingRevision = null;
        if (form is not null)
        {
            existingRevision = await providerRepository.GetSchemaRevisionByHashAsync(request.TenantId, connection.Id, providerSurveyId, revisionHash, cancellationToken);
            if (existingRevision is not null)
            {
                RegistrationFormVersion? existingVersion = form.Versions.SingleOrDefault(version =>
                    version.SourceKindId == (int)RegistrationFormVersionSourceKindEnum.ExternalImported &&
                    version.StatusId == (int)RegistrationFormStatusEnum.Published &&
                    version.ExternalRegistrationProviderConnectionId == connection.Id &&
                    version.ExternalProviderSurveyId == providerSurveyId &&
                    version.ExternalRegistrationProviderSchemaRevisionId == existingRevision.Id);
                if (existingVersion is not null)
                {
                    return Success(existingVersion.Id, "External registration provider schema import already exists.");
                }

                if (SchemaDriftClassifier.BlocksPublication(RegistrationProviderManagementHandlerHelpers.ToSchemaDriftClass((RegistrationProviderDriftClassEnum)existingRevision.DriftClassId)))
                {
                    return Failure(existingRevision.Id, "registration_provider_schema_drift_blocked", "Registration provider schema drift requires mapping review.");
                }
            }

            RegistrationProviderSchemaRevision? previous = await providerRepository.GetLatestExternalImportSchemaRevisionAsync(
                request.TenantId, request.EventId, form.Id, connection.Id, providerSurveyId, cancellationToken);
            if (previous is not null)
            {
                driftClass = driftClassifier.Classify(CanonicalProviderSchemaSnapshot.Parse(previous.ProviderSnapshotJson), canonical.Snapshot);
            }
        }

        RegistrationProviderSchemaRevision revision = RegistrationProviderSchemaRevision.Create(
            request.TenantId,
            connection.Id,
            RegistrationProviderSchemaAuthorityEnum.ProviderDiscovered,
            revisionHash,
            providerSurveyId,
            providerSurveyRevisionId,
            canonical.Json,
            canonical.HexHash,
            ToDomain(driftClass),
            now);
        await providerRepository.AddSchemaRevisionAsync(revision, cancellationToken);

        if (SchemaDriftClassifier.BlocksPublication(driftClass))
        {
            await providerRepository.SaveChangesAsync(cancellationToken);
            return Failure(revision.Id, "registration_provider_schema_drift_blocked", "Registration provider schema drift requires mapping review.");
        }

        bool createForm = form is null;
        form ??= RegistrationForm.Create(request.TenantId, request.EventId, request.Request.Namespace, request.Request.Key, request.Request.Name, now);
        int nextVersion = form.Versions.Select(version => version.Version).DefaultIfEmpty().Max() + 1;
        RegistrationFormVersion version = RegistrationFormVersion.CreateExternalImported(
            form,
            nextVersion,
            request.Request.LanguageTag,
            connection.Id,
            revision.Id,
            providerSurveyId,
            providerSurveyRevisionId,
            ExternalImportMappingRevision.Hash(tuple, providerSurveyId, providerSurveyRevisionId, canonical.Snapshot),
            now);
        AddSnapshotFields(version, canonical.Snapshot, now);
        publicationService.Publish(version, now);
        form.AddVersion(version);
        if (createForm)
        {
            await providerRepository.AddFormAsync(form, cancellationToken);
        }

        await providerRepository.SaveChangesAsync(cancellationToken);
        return Success(version.Id, "External registration provider schema imported as a frozen published form version.");
    }

    private static bool ValidImportRequest(ImportExternalRegistrationProviderFormVersionRequestDto request) =>
        (request.FormId is null || request.FormId != Guid.Empty) &&
        BoundedReference(request.ProviderSurveyId, 200) &&
        (string.IsNullOrWhiteSpace(request.ProviderSurveyRevisionId) || BoundedReference(request.ProviderSurveyRevisionId, 200)) &&
        (request.FormId is not null || BoundedReference(request.Key, 100) && BoundedReference(request.Name, 200));

    private static RegistrationProviderBinding TransientBinding(ImportExternalRegistrationProviderFormVersionCommand request, Guid connectionId, DateTime now)
    {
        RegistrationProviderBinding binding = RegistrationProviderBinding.Create(
            request.TenantId,
            connectionId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            RegistrationProviderPresentationModeEnum.Manual,
            RegistrationProviderCollectionModeEnum.ProviderHosted,
            RegistrationProviderCompletionModeEnum.Manual,
            RegistrationProviderTrustLevelEnum.FullCanonical,
            null,
            now);
        binding.SetDraftProvisionedSurvey(request.Request.ProviderSurveyId, request.Request.ProviderSurveyRevisionId);
        return binding;
    }

    private static void AddSnapshotFields(RegistrationFormVersion version, RegistrationProviderSchemaSnapshot snapshot, DateTime now)
    {
        if (snapshot.Fields.Count == 0)
        {
            throw new InvalidOperationException("External provider schema must contain at least one field.");
        }

        RegistrationFormSection section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Imported questions", now);
        version.AddSection(section);
        int ordinal = 1;
        foreach (RegistrationProviderSchemaFieldSnapshot fieldSnapshot in snapshot.Fields)
        {
            RegistrationFieldTypeEnum fieldType = ToFieldType(fieldSnapshot.Type, fieldSnapshot.IsRequired);
            RegistrationFormField field = RegistrationFormField.Create(
                Guid.CreateVersion7(),
                section,
                ordinal++,
                "external",
                fieldSnapshot.Key,
                fieldSnapshot.Label,
                fieldType,
                1,
                RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
                false,
                true,
                false,
                null,
                false,
                false,
                now);
            version.AddField(section, field);
            version.UpdateFieldValidation(field, fieldSnapshot.IsRequired, fieldType == RegistrationFieldTypeEnum.MultipleChoice, null, null, null, null, null, null, null, null);
            int optionOrdinal = 1;
            foreach (RegistrationProviderSchemaOptionSnapshot optionSnapshot in fieldSnapshot.Options)
            {
                version.AddOption(field, RegistrationFormFieldOption.Create(Guid.CreateVersion7(), field, optionOrdinal++, optionSnapshot.Key, optionSnapshot.Label, now));
            }
        }
    }

    private static RegistrationFieldTypeEnum ToFieldType(string type, bool isRequired)
    {
        if (!Enum.TryParse(type, ignoreCase: true, out RegistrationFieldTypeEnum parsed))
        {
            return RegistrationFieldTypeEnum.ShortText;
        }

        return parsed switch
        {
            RegistrationFieldTypeEnum.Consent => RegistrationFieldTypeEnum.Boolean,
            RegistrationFieldTypeEnum.Rating => RegistrationFieldTypeEnum.Integer,
            RegistrationFieldTypeEnum.OpaqueExternal when isRequired => throw new InvalidOperationException("Required opaque external fields cannot be imported as frozen platform forms."),
            _ => parsed
        };
    }

    private static RegistrationProviderDriftClassEnum ToDomain(RegistrationProviderSchemaDriftClass driftClass) => driftClass switch
    {
        RegistrationProviderSchemaDriftClass.NoDrift => RegistrationProviderDriftClassEnum.NoDrift,
        RegistrationProviderSchemaDriftClass.AdditiveOptionalChange => RegistrationProviderDriftClassEnum.AdditiveOptionalChange,
        RegistrationProviderSchemaDriftClass.LabelOnlyChange => RegistrationProviderDriftClassEnum.LabelOnlyChange,
        RegistrationProviderSchemaDriftClass.MappingRequired => RegistrationProviderDriftClassEnum.MappingRequired,
        RegistrationProviderSchemaDriftClass.RequiredFieldRemoved => RegistrationProviderDriftClassEnum.RequiredFieldRemoved,
        RegistrationProviderSchemaDriftClass.TypeChanged => RegistrationProviderDriftClassEnum.TypeChanged,
        RegistrationProviderSchemaDriftClass.OptionSetChanged => RegistrationProviderDriftClassEnum.OptionSetChanged,
        _ => RegistrationProviderDriftClassEnum.UnsupportedChange
    };
}

public sealed class GetRegistrationChannelsQueryHandler(IRegistrationProviderRepository providerRepository)
    : IRequestHandler<GetRegistrationChannelsQuery, IReadOnlyList<RegistrationChannelDto>>
{
    public async Task<IReadOnlyList<RegistrationChannelDto>> Handle(GetRegistrationChannelsQuery request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        RegistrationRequirement? requirement = await providerRepository.GetRequirementAsync(request.TenantId, request.EventId, request.WorkflowId, request.RequirementId, cancellationToken);
        return requirement is null ? [] : [.. requirement.Channels.Where(channel => !channel.IsDeleted).OrderBy(channel => channel.Ordinal).Select(ToChannelDto)];
    }
}

public sealed class UpsertRegistrationChannelCommandHandler(IRegistrationProviderRepository providerRepository, TimeProvider timeProvider)
    : IRequestHandler<UpsertRegistrationChannelCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(UpsertRegistrationChannelCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        RegistrationRequirement? requirement = await providerRepository.GetRequirementAsync(request.TenantId, request.EventId, request.WorkflowId, request.RequirementId, cancellationToken);
        if (requirement is null) return Failure(request.RequirementId, "registration_requirement_not_found", "Registration requirement was not found.");
        if (!await ProviderBindingUsableAsync(providerRepository, request.TenantId, request.EventId, request.Request, cancellationToken))
            return Failure(request.Request.RegistrationProviderBindingId ?? Guid.Empty, "registration_channel_provider_binding_invalid", "Provider channel requires a published same-event binding.");

        try
        {
            if (request.ChannelId is { } id)
            {
                RegistrationChannel? channel = await providerRepository.GetChannelAsync(request.TenantId, request.EventId, request.WorkflowId, request.RequirementId, id, cancellationToken);
                if (channel is null) return Failure(id, "registration_channel_not_found", "Registration channel was not found.");
                channel.Update(request.Request.Ordinal, request.Request.IsNative, request.Request.RegistrationProviderBindingId);
                await providerRepository.SaveChangesAsync(cancellationToken);
                return Success(channel.Id, "Registration channel updated.");
            }

            RegistrationChannel? deleted = requirement.Channels.FirstOrDefault(channel => channel.IsDeleted && channel.Ordinal == request.Request.Ordinal);
            if (deleted is not null)
            {
                deleted.Revive(request.Request.Ordinal, request.Request.IsNative, request.Request.RegistrationProviderBindingId);
                await providerRepository.SaveChangesAsync(cancellationToken);
                return Success(deleted.Id, "Registration channel created.");
            }

            RegistrationChannel created = RegistrationChannel.Create(requirement, request.Request.Ordinal, request.Request.IsNative,
                request.Request.RegistrationProviderBindingId, timeProvider.GetUtcNow().UtcDateTime);
            requirement.AddChannel(created);
            await providerRepository.SaveChangesAsync(cancellationToken);
            return Success(created.Id, "Registration channel created.");
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return Failure(request.ChannelId ?? Guid.Empty, "registration_channel_validation_failed", ex.Message);
        }
    }
}

public sealed class DeleteRegistrationChannelCommandHandler(IRegistrationProviderRepository providerRepository, TimeProvider timeProvider)
    : IRequestHandler<DeleteRegistrationChannelCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(DeleteRegistrationChannelCommand request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        RegistrationChannel? channel = await providerRepository.GetChannelAsync(request.TenantId, request.EventId, request.WorkflowId, request.RequirementId, request.ChannelId, cancellationToken);
        if (channel is null) return Failure(request.ChannelId, "registration_channel_not_found", "Registration channel was not found.");
        channel.Remove(timeProvider.GetUtcNow().UtcDateTime);
        await providerRepository.SaveChangesAsync(cancellationToken);
        return Success(channel.Id, "Registration channel deleted.");
    }
}

public sealed class GetRegistrationProviderLaunchDescriptorQueryHandler(IRegistrationProviderRepository providerRepository, IRegistrationProviderRegistry providerRegistry)
    : IRequestHandler<GetRegistrationProviderLaunchDescriptorQuery, RegistrationProviderLaunchDescriptorDto>
{
    public async Task<RegistrationProviderLaunchDescriptorDto> Handle(GetRegistrationProviderLaunchDescriptorQuery request, CancellationToken cancellationToken)
    {
        RequireEventScope(request.TenantId, request.EventId);
        RegistrationChannel? channel = await providerRepository.GetChannelAsync(request.TenantId, request.EventId, request.WorkflowId, request.RequirementId, request.ChannelId, cancellationToken);
        if (channel is null || channel.IsDeleted || channel.IsNative || channel.RegistrationProviderBindingId != request.BindingId)
            return Descriptor(request.TenantId, request.EventId, request.WorkflowId, request.RequirementId, request.BindingId, request.ChannelId, "unavailable", false, null, "Provider registration", false, "manual", "channel_not_found");

        RegistrationProviderBinding? binding = await providerRepository.GetBindingAsync(request.TenantId, request.BindingId, cancellationToken);
        if (binding is null || binding.Connection is null || !await BindingBelongsToEventAsync(providerRepository, request.TenantId, request.EventId, request.BindingId, cancellationToken))
            return Descriptor(request.TenantId, request.EventId, request.WorkflowId, request.RequirementId, request.BindingId, request.ChannelId, "unavailable", false, null, "Provider registration", false, "manual", "binding_not_found");
        if (binding.StateId != (int)RegistrationProviderBindingStateEnum.Published || binding.DriftClassId >= (int)RegistrationProviderDriftClassEnum.MappingRequired)
            return Descriptor(request.TenantId, request.EventId, request.WorkflowId, request.RequirementId, binding.Id, request.ChannelId, "unavailable", false, null, "Provider registration", false, "manual", "binding_not_launchable");

        if (!BindingLaunchContractMatchesCapabilities(binding))
            return Descriptor(request.TenantId, request.EventId, request.WorkflowId, request.RequirementId, binding.Id, request.ChannelId, "unavailable", false, null, "Provider registration", false, "manual", "channel_binding_mismatch");

        RegistrationProviderCapability? capability = binding.Capabilities.FirstOrDefault(item => !item.IsDeleted &&
            item.CapabilityCode is RegistrationProviderCapabilityCodes.Redirect or RegistrationProviderCapabilityCodes.Embed);
        if (capability is null) return Descriptor(request.TenantId, request.EventId, request.WorkflowId, request.RequirementId, binding.Id, request.ChannelId, "manual", true, null, "Provider registration", true, "manual", "manual_only");

        RegistrationProviderTuple tuple = TupleFromConnection(binding.Connection);
        IRegistrationProviderDescriptor? descriptor = providerRegistry.TryResolve(tuple);
        if (descriptor is not IRegistrationProviderPresentation presentationProvider)
            return Descriptor(request.TenantId, request.EventId, request.WorkflowId, request.RequirementId, binding.Id, request.ChannelId, "manual", true, null, "Provider registration", true, "manual", "presentation_unavailable");
        if (!BindingLaunchContractMatchesCapabilities(binding, descriptor.ProvenCapabilities))
            return Descriptor(request.TenantId, request.EventId, request.WorkflowId, request.RequirementId, binding.Id, request.ChannelId, "unavailable", false, null, "Provider registration", false, "manual", "channel_capability_mismatch");

        RegistrationProviderPresentationResult result = await presentationProvider.GetPresentationAsync(new(request.TenantId, binding, binding.Connection, tuple), cancellationToken);
        Uri? uri = capability.CapabilityCode == RegistrationProviderCapabilityCodes.Embed && result.EmbedAvailable ? result.EmbedUri : result.RedirectUri;
        string mode = capability.CapabilityCode == RegistrationProviderCapabilityCodes.Embed && result.EmbedAvailable ? "embed" : "redirect";
        // DNS caveat: this descriptor is browser navigation metadata only. The server never fetches or proxies
        // provider URLs; literal localhost/private/metadata host blocking remains the SSRF guard at origin approval.
        if (uri is null || !binding.Connection.IsOriginApproved(uri))
            return Descriptor(request.TenantId, request.EventId, request.WorkflowId, request.RequirementId, binding.Id, request.ChannelId, "manual", true, null, "Provider registration", true, "manual", "origin_not_approved");

        return Descriptor(request.TenantId, request.EventId, request.WorkflowId, request.RequirementId, binding.Id, request.ChannelId, mode, true, uri.ToString(), "Provider registration", mode == "redirect", "manual", "ok");
    }
}

internal static class ExternalImportMappingRevision
{
    public static string Hash(
        RegistrationProviderTuple tuple,
        string providerSurveyId,
        string? providerSurveyRevisionId,
        RegistrationProviderSchemaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(tuple);
        ArgumentNullException.ThrowIfNull(snapshot);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "islamu.external-import-mapping-revision.v1");
            writer.WriteString("importerPolicyVersion", tuple.AdapterPolicyVersion);
            writer.WriteString("providerCode", tuple.ProviderCode);
            writer.WriteString("providerDeploymentCode", tuple.ProviderDeploymentCode);
            writer.WriteString("apiVersion", tuple.ApiVersion);
            writer.WriteString("conformanceEvidenceRevision", tuple.ConformanceEvidenceRevision);
            writer.WriteString("providerSurveyId", providerSurveyId.Trim());
            writer.WriteString("providerSurveyRevisionId", providerSurveyRevisionId?.Trim() ?? string.Empty);
            writer.WriteStartArray("fields");
            foreach (RegistrationProviderSchemaFieldSnapshot field in snapshot.Fields.OrderBy(field => field.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("providerKey", field.Key);
                writer.WriteString("providerType", field.Type);
                writer.WriteBoolean("isRequired", field.IsRequired);
                writer.WriteString("mappingKey", $"external/{field.Key}");
                writer.WriteStartArray("options");
                foreach (RegistrationProviderSchemaOptionSnapshot option in field.Options.OrderBy(option => option.Key, StringComparer.Ordinal))
                {
                    writer.WriteStartObject();
                    writer.WriteString("providerKey", option.Key);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToBase64String(SHA256.HashData(stream.ToArray()));
    }
}

internal sealed record CanonicalProviderSchemaSnapshot(string Json, string HexHash, string Base64Hash, RegistrationProviderSchemaSnapshot Snapshot)
{
    public static CanonicalProviderSchemaSnapshot From(RegistrationProviderSchemaSnapshot snapshot)
    {
        RegistrationProviderSchemaSnapshot normalized = Normalize(snapshot);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "islamu.registration-provider-schema-snapshot.v1");
            writer.WriteStartArray("fields");
            foreach (RegistrationProviderSchemaFieldSnapshot field in normalized.Fields)
            {
                writer.WriteStartObject();
                writer.WriteString("key", field.Key);
                writer.WriteString("label", field.Label);
                writer.WriteString("type", field.Type);
                writer.WriteBoolean("isRequired", field.IsRequired);
                writer.WriteStartArray("options");
                foreach (RegistrationProviderSchemaOptionSnapshot option in field.Options)
                {
                    writer.WriteStartObject();
                    writer.WriteString("key", option.Key);
                    writer.WriteString("label", option.Label);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        byte[] bytes = stream.ToArray();
        byte[] hash = SHA256.HashData(bytes);
        return new(Encoding.UTF8.GetString(bytes), Convert.ToHexStringLower(hash), Convert.ToBase64String(hash), normalized);
    }

    public static RegistrationProviderSchemaSnapshot Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement fields = document.RootElement.GetProperty("fields");
        return new RegistrationProviderSchemaSnapshot([
            .. fields.EnumerateArray().Select(field => new RegistrationProviderSchemaFieldSnapshot(
                field.GetProperty("key").GetString() ?? string.Empty,
                field.GetProperty("label").GetString() ?? string.Empty,
                field.GetProperty("type").GetString() ?? string.Empty,
                field.GetProperty("isRequired").GetBoolean(),
                [.. field.GetProperty("options").EnumerateArray().Select(option => new RegistrationProviderSchemaOptionSnapshot(
                    option.GetProperty("key").GetString() ?? string.Empty,
                    option.GetProperty("label").GetString() ?? string.Empty))]))
        ]);
    }

    private static RegistrationProviderSchemaSnapshot Normalize(RegistrationProviderSchemaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RegistrationProviderSchemaFieldSnapshot[] fields = [.. snapshot.Fields
            .Select(field => new RegistrationProviderSchemaFieldSnapshot(
                Required(field.Key, 200, nameof(field.Key)),
                Required(field.Label, 500, nameof(field.Label)),
                Required(field.Type, 100, nameof(field.Type)),
                field.IsRequired,
                [.. field.Options
                    .Select(option => new RegistrationProviderSchemaOptionSnapshot(
                        Required(option.Key, 200, nameof(option.Key)),
                        Required(option.Label, 500, nameof(option.Label))))
                    .OrderBy(option => option.Key, StringComparer.Ordinal)]))
            .OrderBy(field => field.Key, StringComparer.Ordinal)];
        if (fields.Select(field => field.Key).Distinct(StringComparer.Ordinal).Count() != fields.Length ||
            fields.Any(field => field.Options.Select(option => option.Key).Distinct(StringComparer.Ordinal).Count() != field.Options.Count))
        {
            throw new InvalidOperationException("External provider schema contains duplicate field or option keys.");
        }

        return new(fields);
    }

    private static string Required(string value, int maxLength, string parameterName)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 && normalized.Length <= maxLength && !normalized.Any(char.IsControl)
            ? normalized
            : throw new ArgumentException("Provider schema values must be bounded non-control text.", parameterName);
    }
}

internal static class RegistrationProviderManagementHandlerHelpers
{
    public static void RequireEventScope(Guid tenantId, Guid eventId)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty)
        {
            throw new ValidationException("Tenant and event identifiers are required.");
        }
    }

    public static bool BoundedReference(string value, int maxLength) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength && !value.Any(char.IsControl);

    public static async Task<bool> BindingBelongsToEventAsync(IRegistrationProviderRepository providerRepository, PollRegistrationProviderReconciliationCommand request, CancellationToken cancellationToken) =>
        await BindingBelongsToEventAsync(providerRepository, request.TenantId, request.EventId, request.BindingId, cancellationToken);

    public static async Task<bool> BindingBelongsToEventAsync(IRegistrationProviderRepository providerRepository, Guid tenantId, Guid eventId, Guid bindingId, CancellationToken cancellationToken) =>
        (await providerRepository.GetBindingsForEventAsync(tenantId, eventId, cancellationToken)).Any(binding => binding.Id == bindingId);

    public static bool TryParseBindingId(string providerDecisionId, out Guid bindingId) =>
        Guid.TryParseExact(providerDecisionId.Split(':', 2)[0], "N", out bindingId);

    public static string SafeCategory(string? value) => string.IsNullOrWhiteSpace(value)
        ? "none"
        : new string(value.Trim().ToLowerInvariant().Where(character => char.IsAsciiLetterOrDigit(character) || character == '_').Take(100).ToArray());

    public static string Sha256Identifier(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static RegistrationProviderTuple TupleFromConnection(RegistrationProviderConnection connection) => new(
        connection.ProviderCode,
        connection.ProviderDeploymentCode,
        connection.ApiVersion,
        connection.AdapterPolicyVersion,
        connection.ConformanceEvidenceRevision);

    public static void ApplyProviderProvisioning(RegistrationProviderBinding binding, RegistrationProviderBindingRequestDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.ProviderSurveyId))
        {
            binding.SetDraftProvisionedSurvey(request.ProviderSurveyId, request.ProviderSurveyRevisionId);
        }

        if (!string.IsNullOrWhiteSpace(request.ProviderWebhookId))
        {
            if (request.WebhookSecretBindingId is not { } secretBindingId)
            {
                throw new ArgumentException("Provider webhook provisioning requires a binding-level secret reference.", nameof(request));
            }

            binding.SetDraftProvisionedSubscription(request.ProviderWebhookId, secretBindingId);
        }
    }

    public static void ReplaceCapabilitiesFromDescriptor(
        RegistrationProviderBinding binding,
        RegistrationProviderConnection connection,
        IRegistrationProviderRegistry providerRegistry)
    {
        RegistrationProviderTuple tuple = TupleFromConnection(connection);
        IRegistrationProviderDescriptor? descriptor = providerRegistry.TryResolve(tuple);
        IReadOnlyList<RegistrationProviderCapability> capabilities = descriptor is null
            ? []
            : [.. descriptor.ProvenCapabilities.ToCodes().Select(code => RegistrationProviderCapability.Create(
                binding,
                tuple.ProviderCode,
                tuple.DeploymentKind,
                tuple.ApiVersion,
                tuple.AdapterPolicyVersion,
                tuple.ConformanceEvidenceRevision,
                code))];
        binding.ReplaceDraftCapabilities(capabilities);
    }

    public static BaseCommandResponse<Guid> Success(Guid id, string message) => BaseCommandResponse.Success(id, message);

    public static BaseCommandResponse<Guid> Failure(Guid id, string code, string message) =>
        BaseCommandResponse.Failure<Guid>(code, message, [message], id);

    public static RegistrationProviderConnectionDto ToConnectionDto(RegistrationProviderConnection connection, Guid eventId = default) => new()
    {
        Id = connection.Id,
        TenantId = connection.TenantId,
        EventId = eventId,
        Name = connection.Name,
        ProviderKindId = connection.ProviderKindId,
        DeploymentKindId = connection.DeploymentKindId,
        ProviderCode = connection.ProviderCode,
        ProviderDeploymentCode = connection.ProviderDeploymentCode,
        ApiVersion = connection.ApiVersion,
        AdapterPolicyVersion = connection.AdapterPolicyVersion,
        ConformanceEvidenceRevision = connection.ConformanceEvidenceRevision,
        ManagementApiBaseUrl = connection.ManagementApiBaseUrl,
        PublicBaseUrl = connection.PublicBaseUrl,
        ProviderWorkspaceId = connection.ProviderWorkspaceId,
        ApiTokenSecretBindingId = connection.ApiTokenSecretBindingId,
        WebhookSecretBindingId = connection.WebhookSecretBindingId,
        ApprovedOrigins = [.. connection.ApprovedOrigins.Where(origin => !origin.IsDeleted).Select(origin => origin.Origin).Order(StringComparer.OrdinalIgnoreCase)],
        GrantedOAuthScopes = connection.GrantedOAuthScopes,
        ProviderIdentity = connection.ProviderIdentity,
        PubSubConfigurationReference = connection.PubSubConfigurationReference,
        LastCredentialRefreshAt = connection.LastCredentialRefreshAt,
        LastAccessValidatedAt = connection.LastAccessValidatedAt
    };

    public static RegistrationProviderBindingDto ToBindingDto(RegistrationProviderBinding binding, Guid eventId = default)
    {
        Dictionary<Guid, string> fieldKeys = binding.FieldMappings
            .Where(mapping => !mapping.IsDeleted)
            .ToDictionary(mapping => mapping.Id, mapping => mapping.PlatformFieldKey);

        return new()
        {
            Id = binding.Id,
            TenantId = binding.TenantId,
            EventId = eventId,
            ConnectionId = binding.RegistrationProviderConnectionId,
            FormId = binding.RegistrationFormId,
            FormVersionId = binding.RegistrationFormVersionId,
            ProviderSurveyId = binding.ProviderSurveyId,
            ProviderSurveyRevisionId = binding.ProviderSurveyRevisionId,
            ProviderWebhookId = binding.ProviderWebhookId,
            WebhookSecretBindingId = binding.WebhookSecretBindingId,
            PresentationModeId = binding.PresentationModeId,
            CollectionModeId = binding.CollectionModeId,
            CompletionModeId = binding.CompletionModeId,
            TrustLevelId = binding.TrustLevelId,
            DriftClassId = binding.DriftClassId,
            StateId = binding.StateId,
            PublishedAt = binding.PublishedAt,
            CapabilityCodes = [.. binding.Capabilities.Where(capability => !capability.IsDeleted).Select(capability => capability.CapabilityCode).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)],
            FieldMappings = [.. binding.FieldMappings
                .Where(mapping => !mapping.IsDeleted)
                .OrderBy(mapping => mapping.PlatformFieldKey, StringComparer.Ordinal)
                .Select(mapping => new RegistrationProviderFieldMappingDto
                {
                    PlatformFieldKey = mapping.PlatformFieldKey,
                    ProviderFieldKey = mapping.ProviderFieldKey,
                    IsRequired = mapping.IsRequired
                })],
            OptionMappings = [.. binding.OptionMappings
                .Where(mapping => !mapping.IsDeleted && fieldKeys.ContainsKey(mapping.RegistrationProviderFieldMappingId))
                .OrderBy(mapping => fieldKeys[mapping.RegistrationProviderFieldMappingId], StringComparer.Ordinal)
                .ThenBy(mapping => mapping.PlatformOptionKey, StringComparer.Ordinal)
                .Select(mapping => new RegistrationProviderOptionMappingDto
                {
                    PlatformFieldKey = fieldKeys[mapping.RegistrationProviderFieldMappingId],
                    PlatformOptionKey = mapping.PlatformOptionKey,
                    ProviderOptionKey = mapping.ProviderOptionKey
                })]
        };
    }

    public static RegistrationChannelDto ToChannelDto(RegistrationChannel channel) => new()
    {
        Id = channel.Id,
        TenantId = channel.TenantId,
        EventId = channel.EventId,
        RegistrationWorkflowId = channel.RegistrationWorkflowId,
        RegistrationRequirementId = channel.RegistrationRequirementId,
        Ordinal = channel.Ordinal,
        IsNative = channel.IsNative,
        RegistrationProviderBindingId = channel.RegistrationProviderBindingId
    };

    public static RegistrationProviderLaunchDescriptorDto Descriptor(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, Guid bindingId, Guid channelId, string mode, bool available, string? url, string title, bool openInNewTab, string fallbackMode, string reason) => new()
    {
        BindingId = bindingId,
        ChannelId = channelId,
        TenantId = tenantId,
        EventId = eventId,
        WorkflowId = workflowId,
        RequirementId = requirementId,
        Mode = mode,
        Available = available,
        Url = url,
        Title = title,
        OpenInNewTab = openInNewTab,
        FallbackMode = fallbackMode,
        Reason = reason
    };

    public static bool BindingLaunchContractMatchesCapabilities(RegistrationProviderBinding binding, RegistrationProviderCapabilitySet? effectiveCapabilities = null)
    {
        RegistrationProviderCapabilitySet capabilities = effectiveCapabilities ?? RegistrationProviderCapabilitySet.FromCodes(
            binding.Capabilities.Where(capability => !capability.IsDeleted).Select(capability => capability.CapabilityCode));
        return (RegistrationProviderPresentationModeEnum)binding.PresentationModeId switch
        {
            RegistrationProviderPresentationModeEnum.Redirect => capabilities.Redirect,
            RegistrationProviderPresentationModeEnum.Embed => capabilities.Embed,
            RegistrationProviderPresentationModeEnum.Manual => capabilities.Manual,
            _ => false
        } && (RegistrationProviderCollectionModeEnum)binding.CollectionModeId switch
        {
            RegistrationProviderCollectionModeEnum.ProviderHosted => capabilities.Redirect || capabilities.Embed || capabilities.Manual,
            RegistrationProviderCollectionModeEnum.ProviderApi => capabilities.SubmissionSink,
            RegistrationProviderCollectionModeEnum.MirrorOnly => capabilities.SubmissionSink,
            _ => false
        } && (RegistrationProviderCompletionModeEnum)binding.CompletionModeId switch
        {
            RegistrationProviderCompletionModeEnum.Callback => capabilities.CallbackVerification,
            RegistrationProviderCompletionModeEnum.Polling => capabilities.SubmissionRead && capabilities.Reconciliation,
            RegistrationProviderCompletionModeEnum.Manual => capabilities.Manual,
            _ => false
        } && (RegistrationProviderTrustLevelEnum)binding.TrustLevelId switch
        {
            RegistrationProviderTrustLevelEnum.Untrusted => true,
            RegistrationProviderTrustLevelEnum.CompletionOnly => capabilities.CallbackVerification || capabilities.SubmissionRead || capabilities.Manual,
            RegistrationProviderTrustLevelEnum.SelectedFields or RegistrationProviderTrustLevelEnum.FullCanonical => capabilities.SubmissionRead || capabilities.SubmissionWrite || capabilities.SubmissionSink,
            _ => false
        };
    }

    public static bool TryConnectionRequest(
        RegistrationProviderConnectionRequestDto request,
        IRegistrationProviderRegistry providerRegistry,
        out RegistrationProviderKindEnum providerKind,
        out RegistrationProviderDeploymentKindEnum deploymentKind)
    {
        providerKind = (RegistrationProviderKindEnum)request.ProviderKindId;
        deploymentKind = (RegistrationProviderDeploymentKindEnum)request.DeploymentKindId;
        RegistrationProviderTuple tuple = new(
            request.ProviderCode,
            request.ProviderDeploymentCode,
            request.ApiVersion,
            request.AdapterPolicyVersion,
            request.ConformanceEvidenceRevision);
        IRegistrationProviderDescriptor? descriptor = providerRegistry.TryResolve(tuple);
        if (descriptor is null)
        {
            return false;
        }

        bool requiresWebhookSecret = RequiresSharedConnectionWebhookSecret(descriptor);
        return BoundedReference(request.Name, 120) && Enum.IsDefined(providerKind) && Enum.IsDefined(deploymentKind) &&
            request.ApiTokenSecretBindingId != Guid.Empty &&
            (requiresWebhookSecret ? request.WebhookSecretBindingId != Guid.Empty : request.WebhookSecretBindingId == Guid.Empty) &&
            BoundedReference(request.ProviderCode, 100) && BoundedReference(request.ProviderDeploymentCode, 100) &&
            BoundedReference(request.ApiVersion, 100) && BoundedReference(request.AdapterPolicyVersion, 100) &&
            BoundedReference(request.ConformanceEvidenceRevision, 120) && BoundedReference(request.ProviderWorkspaceId, 200) &&
            OptionalBoundedReference(request.ProviderIdentity, 200) && OptionalBoundedReference(request.PubSubConfigurationReference, 300) &&
            ValidProviderScopes(request.ProviderCode, request.GrantedOAuthScopes) && ValidProviderMetadata(request.ProviderCode, request.ProviderIdentity, request.PubSubConfigurationReference) &&
            IsHttpsBaseUrl(request.ManagementApiBaseUrl) && IsHttpsBaseUrl(request.PublicBaseUrl);
    }

    public static bool RequiresSharedConnectionWebhookSecret(RegistrationProviderConnectionRequestDto request, IRegistrationProviderRegistry providerRegistry) =>
        providerRegistry.TryResolve(new(
            request.ProviderCode,
            request.ProviderDeploymentCode,
            request.ApiVersion,
            request.AdapterPolicyVersion,
            request.ConformanceEvidenceRevision)) is { } descriptor && RequiresSharedConnectionWebhookSecret(descriptor);

    private static bool RequiresSharedConnectionWebhookSecret(IRegistrationProviderDescriptor descriptor) =>
        descriptor is IRegistrationProviderCallbackVerifier && descriptor is not IRegistrationProviderDelegatedAutomation;

    private static bool OptionalBoundedReference(string value, int maxLength) => value.Trim().Length <= maxLength && !value.Any(char.IsControl);

    private static bool ValidProviderScopes(string providerCode, string scopes)
    {
        if (!OptionalBoundedReference(scopes, 1000)) return false;

        if (!string.Equals(providerCode.Trim(), "GOOGLE_FORMS", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string normalized = string.Join(' ', scopes.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        return normalized is
            "email https://www.googleapis.com/auth/forms.body.readonly https://www.googleapis.com/auth/forms.responses.readonly openid" or
            "email https://www.googleapis.com/auth/forms.body https://www.googleapis.com/auth/forms.body.readonly https://www.googleapis.com/auth/forms.responses.readonly openid";
    }

    private static bool ValidProviderMetadata(string providerCode, string providerIdentity, string pubSubReference) =>
        !string.Equals(providerCode.Trim(), "GOOGLE_FORMS", StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(providerIdentity) && !string.IsNullOrWhiteSpace(pubSubReference));

    private static bool IsHttpsBaseUrl(string value) => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) &&
        uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    public static RegistrationProviderSchemaDriftClass ToSchemaDriftClass(RegistrationProviderDriftClassEnum driftClass) => driftClass switch
    {
        RegistrationProviderDriftClassEnum.NoDrift => RegistrationProviderSchemaDriftClass.NoDrift,
        RegistrationProviderDriftClassEnum.AdditiveOptionalChange => RegistrationProviderSchemaDriftClass.AdditiveOptionalChange,
        RegistrationProviderDriftClassEnum.LabelOnlyChange => RegistrationProviderSchemaDriftClass.LabelOnlyChange,
        RegistrationProviderDriftClassEnum.MappingRequired => RegistrationProviderSchemaDriftClass.MappingRequired,
        RegistrationProviderDriftClassEnum.RequiredFieldRemoved => RegistrationProviderSchemaDriftClass.RequiredFieldRemoved,
        RegistrationProviderDriftClassEnum.TypeChanged => RegistrationProviderSchemaDriftClass.TypeChanged,
        RegistrationProviderDriftClassEnum.OptionSetChanged => RegistrationProviderSchemaDriftClass.OptionSetChanged,
        _ => RegistrationProviderSchemaDriftClass.UnsupportedChange
    };

    public static bool TryBindingRequest(RegistrationProviderBindingRequestDto request, out RegistrationProviderPresentationModeEnum presentation, out RegistrationProviderCollectionModeEnum collection, out RegistrationProviderCompletionModeEnum completion, out RegistrationProviderTrustLevelEnum trust)
    {
        presentation = (RegistrationProviderPresentationModeEnum)request.PresentationModeId;
        collection = (RegistrationProviderCollectionModeEnum)request.CollectionModeId;
        completion = (RegistrationProviderCompletionModeEnum)request.CompletionModeId;
        trust = (RegistrationProviderTrustLevelEnum)request.TrustLevelId;
        return request.ConnectionId != Guid.Empty && request.FormId != Guid.Empty && request.FormVersionId != Guid.Empty &&
            (string.IsNullOrWhiteSpace(request.ProviderSurveyId) || BoundedReference(request.ProviderSurveyId, 200)) &&
            (string.IsNullOrWhiteSpace(request.ProviderSurveyRevisionId) || BoundedReference(request.ProviderSurveyRevisionId, 200)) &&
            (string.IsNullOrWhiteSpace(request.ProviderWebhookId) || BoundedReference(request.ProviderWebhookId, 200)) &&
            (string.IsNullOrWhiteSpace(request.ProviderWebhookId) || request.WebhookSecretBindingId is { } webhookSecretBindingId && webhookSecretBindingId != Guid.Empty) &&
            Enum.IsDefined(presentation) && Enum.IsDefined(collection) && Enum.IsDefined(completion) && Enum.IsDefined(trust);
    }

    public static async Task<bool> BindingWebhookSecretIsValidAsync(ISecretBindingRepository secretBindingRepository, Guid tenantId, Guid bindingId, Guid? secretBindingId, CancellationToken cancellationToken)
    {
        if (secretBindingId is null)
        {
            return true;
        }

        SecretBinding? secret = await secretBindingRepository.GetByTenantAndIdAsync(tenantId, secretBindingId.Value, cancellationToken);
        return secret is not null &&
            string.Equals(secret.SettingKey, SecretDefinitionRegistry.Keys.RegistrationProviders.WebhookSecret, StringComparison.Ordinal) &&
            string.Equals(secret.Qualifier, bindingId.ToString("N"), StringComparison.Ordinal);
    }

    public static async Task<bool> ProviderBindingUsableAsync(IRegistrationProviderRepository providerRepository, Guid tenantId, Guid eventId, RegistrationChannelRequestDto request, CancellationToken cancellationToken)
    {
        if (request.IsNative) return request.RegistrationProviderBindingId is null;
        if (request.RegistrationProviderBindingId is not { } bindingId || bindingId == Guid.Empty) return false;
        RegistrationProviderBinding? binding = await providerRepository.GetBindingAsync(tenantId, bindingId, cancellationToken);
        return binding is not null && binding.StateId == (int)RegistrationProviderBindingStateEnum.Published &&
            await BindingBelongsToEventAsync(providerRepository, tenantId, eventId, bindingId, cancellationToken);
    }

    public static async Task<bool> ValidateRetainedEffectAsync(
        IRegistrationProviderRepository providerRepository,
        IIncomingWebhookEffectReceiptRepository receiptRepository,
        IncomingWebhookEffectOutbox pointer,
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        if (pointer.IncomingWebhookMessage is null ||
            pointer.TenantId != tenantId ||
            !TryParseBindingId(pointer.ProviderDecisionId, out Guid bindingId) ||
            !string.Equals(pointer.Provider, "registration-provider", StringComparison.Ordinal) ||
            !IsRegistrationProviderManagementEffect(pointer.EffectKind) ||
            !string.Equals(pointer.IncomingWebhookMessage.Provider, pointer.Provider, StringComparison.Ordinal) ||
            !string.Equals(pointer.IncomingWebhookMessage.ProviderMessageId, pointer.ProviderDecisionId, StringComparison.Ordinal) ||
            !string.Equals(pointer.IncomingWebhookMessage.PayloadHash, pointer.PayloadSha256, StringComparison.Ordinal) ||
            !await BindingBelongsToEventAsync(providerRepository, tenantId, eventId, bindingId, cancellationToken))
        {
            return false;
        }

        IncomingWebhookEffectReceipt? receipt = await receiptRepository.GetByIdentityAsync(tenantId, pointer.IncomingWebhookMessageId, pointer.EffectKind, cancellationToken);
        if (receipt is null)
        {
            return true;
        }

        try
        {
            receipt.EnsureMatches(tenantId, pointer.IncomingWebhookMessageId, pointer.EffectKind, pointer.PayloadSha256, pointer.ProcessingGeneration);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsRegistrationProviderManagementEffect(string effectKind) =>
        string.Equals(effectKind, ProcessProviderSubmissionEffectCommandHandler.StableEffectKind, StringComparison.Ordinal) ||
        string.Equals(effectKind, QueueManualRegistrationProviderImportCommandHandler.ManualImportEffectKind, StringComparison.Ordinal);
}
