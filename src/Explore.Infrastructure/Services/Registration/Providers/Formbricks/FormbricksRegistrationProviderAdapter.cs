using System.Buffers;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services.Registration.Providers.Formbricks;

public sealed class FormbricksRegistrationProviderAdapter(
    HttpClient httpClient,
    ISecretResolver secretResolver,
    TimeProvider timeProvider) :
    IRegistrationProviderPresentation,
    IRegistrationProviderSchemaReader,
    IRegistrationProviderFormProvisioner,
    IRegistrationProviderFormCompatibilityChecker,
    IRegistrationProviderSubmissionWriter,
    IRegistrationProviderSubmissionReader,
    IRegistrationProviderCallbackVerifier,
    IRegistrationProviderSubscriptionManager,
    IRegistrationProviderReconciliationProvider,
    IRegistrationProviderSubmissionSink
{
    public const string HttpClientName = "RegistrationProvider.Formbricks";
    private const int MaxResponseBytes = 256 * 1024;
    private const int ReconciliationLimit = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<RegistrationProviderPresentationResult> GetPresentationAsync(
        RegistrationProviderPresentationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? surveyId = NormalizeId(request.Binding.ProviderSurveyId);
        if (surveyId is null || request.AttemptId is not { } attemptId)
        {
            return Task.FromResult(new RegistrationProviderPresentationResult(false, false, true));
        }

        string correlation = "islamuEventAttemptId=" + Uri.EscapeDataString(attemptId.ToString("D"));
        Uri redirect = BuildPublicUri(request.Connection, $"s/{Uri.EscapeDataString(surveyId)}", correlation);
        Uri embed = BuildPublicUri(request.Connection, $"s/{Uri.EscapeDataString(surveyId)}", "embed=true&" + correlation);
        return Task.FromResult(new RegistrationProviderPresentationResult(true, true, true, redirect, embed));
    }

    public async Task<RegistrationProviderSchemaReadResult> ReadSchemaAsync(
        RegistrationProviderSchemaReadRequest request,
        CancellationToken cancellationToken)
    {
        string surveyId = RequireSurveyId(request.Binding);
        using JsonDocument document = await SendManagementJsonAsync(
            request.TenantId,
            request.Connection,
            HttpMethod.Get,
            $"management/surveys/{Uri.EscapeDataString(surveyId)}",
            payload: null,
            cancellationToken);

        JsonElement survey = UnwrapData(document.RootElement);
        RegistrationProviderSchemaSnapshot snapshot = new(ReadSurveyFields(survey));
        return new RegistrationProviderSchemaReadResult(
            snapshot,
            string.Equals(FirstString(survey, "status"), "inProgress", StringComparison.OrdinalIgnoreCase),
            Fingerprint(snapshot));
    }

    public RegistrationProviderFormCompatibilityResult CheckCompatibility(RegistrationFormVersion formVersion)
    {
        List<RegistrationProviderPreflightIssue> issues = [];
        foreach (RegistrationFormField field in formVersion.Sections
                     .Where(section => !section.IsDeleted)
                     .SelectMany(section => section.Fields)
                     .Where(field => !field.IsDeleted))
        {
            RegistrationFieldTypeEnum type = (RegistrationFieldTypeEnum)field.FieldTypeId;
            if (type is not (RegistrationFieldTypeEnum.ShortText or RegistrationFieldTypeEnum.LongText or
                RegistrationFieldTypeEnum.Email or RegistrationFieldTypeEnum.SingleChoice or RegistrationFieldTypeEnum.MultipleChoice))
            {
                issues.Add(new(
                    field.IsRequired ? "registration_provider_required_field_unsupported" : "registration_provider_field_unsupported",
                    $"Field '{FieldKey(field)}' is not supported by Formbricks.",
                    field.Id));
            }
            else if (type is RegistrationFieldTypeEnum.SingleChoice or RegistrationFieldTypeEnum.MultipleChoice &&
                     field.Options.Count(option => !option.IsDeleted && option.RetiredAt is null) < 2)
            {
                issues.Add(new("registration_provider_options_unsupported", $"Field '{FieldKey(field)}' requires at least two active options.", field.Id));
            }
        }

        if (formVersion.Rules.Any(rule => !rule.IsDeleted))
        {
            issues.Add(new("registration_provider_conditions_unsupported", "Formbricks managed provisioning does not support conditional form rules."));
        }

        return new(Fingerprint(Snapshot(formVersion)), issues);
    }

    public async Task<RegistrationProviderFormProvisionResult> ProvisionFormAsync(
        RegistrationProviderFormProvisionRequest request,
        CancellationToken cancellationToken)
    {
        object payload = new
        {
            name = $"ISLAMU Event registration {request.FormVersion.Id:N}",
            type = "link",
            status = "inProgress",
            workspaceId = request.Connection.ProviderWorkspaceId,
            hiddenFields = new { enabled = true, fieldIds = new[] { "islamuEventAttemptId" } },
            questions = BuildSurveyQuestions(request.FormVersion)
        };

        using JsonDocument document = await SendManagementJsonAsync(
            request.TenantId,
            request.Connection,
            HttpMethod.Post,
            "management/surveys",
            payload,
            cancellationToken);

        JsonElement survey = UnwrapData(document.RootElement);
        return new RegistrationProviderFormProvisionResult(RequiredString(survey, "id"), OptionalRevision(survey));
    }

    public async Task<RegistrationProviderSubmissionWriteResult> WriteSubmissionAsync(
        RegistrationProviderSubmissionWriteRequest request,
        CancellationToken cancellationToken)
    {
        string surveyId = RequireSurveyId(request.Binding);
        object payload = new
        {
            surveyId,
            workspaceId = request.Connection.ProviderWorkspaceId,
            finished = true,
            data = request.Answers,
            variables = new { islamuEventAttemptId = request.AttemptId.ToString("D") },
            meta = new { source = "islamu-server-side" }
        };

        using JsonDocument document = await SendSubmissionWriteAsync(
            request.TenantId, request.Connection, payload, request.DisclosureUntilUtc, cancellationToken);

        try
        {
            JsonElement response = UnwrapData(document.RootElement);
            return new RegistrationProviderSubmissionWriteResult(RequiredString(response, "id"), OptionalRevision(response));
        }
        catch (Exception exception) when (exception is not RegistrationProviderSubmissionDeliveryException)
        {
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.AmbiguousAfterHandoff,
                "provider_write_outcome_unknown",
                innerException: exception);
        }
    }

    private async Task<JsonDocument> SendSubmissionWriteAsync(
        Guid tenantId,
        RegistrationProviderConnection connection,
        object payload,
        DateTime? disclosureUntilUtc,
        CancellationToken cancellationToken)
    {
        string apiToken;
        try
        {
            apiToken = await ResolveApiTokenAsync(tenantId, connection, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff,
                "provider_credentials_unavailable",
                innerException: exception);
        }

        using HttpRequestMessage message = new(HttpMethod.Post, BuildManagementUri(connection, "management/responses"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Add("x-api-key", apiToken);
        message.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        if (disclosureUntilUtc is { } deadline && timeProvider.GetUtcNow().UtcDateTime >= deadline)
        {
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff,
                "registration_data_retention_expired");
        }

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            string body = await ReadBoundedStringAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                RegistrationProviderSubmissionDeliveryFailureKind kind =
                    (int)response.StatusCode is 408 or 429
                        ? RegistrationProviderSubmissionDeliveryFailureKind.RetryableBeforeHandoff
                        : (int)response.StatusCode >= 500
                            ? RegistrationProviderSubmissionDeliveryFailureKind.AmbiguousAfterHandoff
                            : RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff;
                throw new RegistrationProviderSubmissionDeliveryException(
                    kind,
                    kind == RegistrationProviderSubmissionDeliveryFailureKind.AmbiguousAfterHandoff
                        ? "provider_write_outcome_unknown"
                        : "provider_write_rejected");
            }

            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException exception)
            {
                throw new RegistrationProviderSubmissionDeliveryException(
                    RegistrationProviderSubmissionDeliveryFailureKind.AmbiguousAfterHandoff,
                    "provider_write_outcome_unknown",
                    innerException: exception);
            }
        }
        catch (RegistrationProviderSubmissionDeliveryException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.AmbiguousAfterHandoff,
                "provider_write_outcome_unknown",
                innerException: exception);
        }
    }

    public async Task<RegistrationProviderSubmissionReadResult> ReadSubmissionAsync(
        RegistrationProviderSubmissionReadRequest request,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await SendManagementJsonAsync(
            request.TenantId,
            request.Connection,
            HttpMethod.Get,
            $"management/responses/{Uri.EscapeDataString(request.ProviderSubmissionId)}",
            payload: null,
            cancellationToken);

        JsonElement response = UnwrapData(document.RootElement);
        return new RegistrationProviderSubmissionReadResult(
            RequiredString(response, "id"),
            OptionalRevision(response),
            OptionalReceivedAt(response),
            ReadAttemptId(response),
            ReadAnswerDictionary(response));
    }

    public async Task<RegistrationProviderCallbackVerificationResult> VerifyCallbackAsync(
        RegistrationProviderCallbackVerificationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetHeader(request.Headers, "webhook-id", out string? webhookId) ||
            !TryGetHeader(request.Headers, "webhook-timestamp", out string? timestampValue) ||
            !TryGetHeader(request.Headers, "webhook-signature", out string? signatureHeader) ||
            !long.TryParse(timestampValue, NumberStyles.None, CultureInfo.InvariantCulture, out long timestampSeconds))
        {
            return new RegistrationProviderCallbackVerificationResult(false, "formbricks_signature_headers_missing");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset signedAt = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);
        if (Duration(now, signedAt) > TimeSpan.FromMinutes(5))
        {
            return new RegistrationProviderCallbackVerificationResult(false, "formbricks_signature_stale");
        }

        string? secret = await ResolveWebhookSecretAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(secret) || !secret.StartsWith("whsec_", StringComparison.Ordinal))
        {
            return new RegistrationProviderCallbackVerificationResult(false, "formbricks_webhook_secret_unavailable");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(secret["whsec_".Length..]);
        }
        catch (FormatException)
        {
            return new RegistrationProviderCallbackVerificationResult(false, "formbricks_webhook_secret_invalid");
        }

        byte[] signedContent = BuildSignedContent(webhookId, timestampValue, request.Body.Span);
        byte[] expected = HMACSHA256.HashData(key, signedContent);
        bool matched = ReadSignatures(signatureHeader).Any(signature => MatchesSignature(signature, expected));
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(expected);
        CryptographicOperations.ZeroMemory(signedContent);

        if (!matched)
        {
            return new RegistrationProviderCallbackVerificationResult(false, "formbricks_signature_invalid");
        }

        return new RegistrationProviderCallbackVerificationResult(
            true,
            Receipt: "formbricks:v1",
            ProviderSubmissionId: TryReadProviderSubmissionId(request.Body.Span));
    }

    public async Task<RegistrationProviderSubscriptionResult> EnsureSubscriptionAsync(
        RegistrationProviderSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        object payload = new
        {
            url = request.CallbackUri.ToString(),
            source = "user",
            workspaceId = request.Connection.ProviderWorkspaceId,
            triggers = new[] { "responseFinished" },
            surveyIds = new[] { RequireSurveyId(request.Binding) },
            name = "ISLAMU Event responseFinished"
        };

        using JsonDocument document = await SendManagementJsonAsync(
            request.TenantId,
            request.Connection,
            HttpMethod.Post,
            "webhooks",
            payload,
            cancellationToken);

        JsonElement webhook = UnwrapData(document.RootElement);
        return new RegistrationProviderSubscriptionResult(
            true,
            RequiredString(webhook, "id"),
            ExternalSecretProvisioningRequired: !string.IsNullOrWhiteSpace(FirstString(webhook, "secret")));
    }

    public async Task<RegistrationProviderReconciliationResult> ReconcileAsync(
        RegistrationProviderReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        string surveyId = RequireSurveyId(request.Binding);
        (DateTime sinceUtc, int offset) = DecodeCursor(request.ContinuationCursor, "formbricks", request.SinceUtc);
        List<RegistrationProviderReconciledSubmission> responses = [];
        string? checkpoint = null;
        string since = Uri.EscapeDataString(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture));
        using JsonDocument document = await SendManagementJsonAsync(
            request.TenantId,
            request.Connection,
            HttpMethod.Get,
            $"management/responses?surveyId={Uri.EscapeDataString(surveyId)}&startDate={since}&limit={ReconciliationLimit + 1}&offset={offset}",
            payload: null,
            cancellationToken);

        JsonElement root = document.RootElement;
        JsonElement items = root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array ? data : root;
        int pageCount = items.ValueKind == JsonValueKind.Array ? items.GetArrayLength() : 0;
        if (items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray().Take(ReconciliationLimit))
            {
                string id = RequiredString(item, "id");
                DateTime? receivedAt = OptionalReceivedAt(item);
                if (receivedAt is { } timestamp)
                {
                    checkpoint = timestamp.ToString("O", CultureInfo.InvariantCulture);
                }

                responses.Add(new(id, OptionalRevision(item), receivedAt));
            }
        }

        bool explicitHasMore = root.TryGetProperty("meta", out JsonElement meta) && meta.TryGetProperty("hasMore", out JsonElement property) && property.ValueKind == JsonValueKind.True;
        bool hasMore = explicitHasMore || pageCount > ReconciliationLimit;
        int nextOffset = offset + responses.Count;

        return new RegistrationProviderReconciliationResult(
            responses.Count,
            hasMore,
            responses,
            hasMore ? null : checkpoint,
            hasMore ? EncodeCursor("formbricks", sinceUtc, nextOffset.ToString(CultureInfo.InvariantCulture)) : null);
    }

    public async Task<RegistrationProviderSubmissionSinkResult> AcceptAsync(
        RegistrationProviderSubmissionSinkRequest request,
        CancellationToken cancellationToken)
    {
        RegistrationProviderSubmissionWriteResult written = await WriteSubmissionAsync(
            new RegistrationProviderSubmissionWriteRequest(
                request.TenantId,
                request.Binding,
                request.Connection,
                request.Tuple,
                request.AttemptId,
                request.Answers) { DisclosureUntilUtc = request.DisclosureUntilUtc },
            cancellationToken);

        return new RegistrationProviderSubmissionSinkResult(true, request.AttemptId, AutoFinalizable: true);
    }

    private async Task<JsonDocument> SendManagementJsonAsync(
        Guid tenantId,
        RegistrationProviderConnection connection,
        HttpMethod method,
        string relativePathAndQuery,
        object? payload,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = new(method, BuildManagementUri(connection, relativePathAndQuery));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Add("x-api-key", await ResolveApiTokenAsync(tenantId, connection, cancellationToken));
        if (payload is not null)
        {
            message.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception) when (method == HttpMethod.Post)
        {
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.AmbiguousAfterHandoff,
                "formbricks_post_response_lost",
                innerException: exception);
        }
        catch (TaskCanceledException exception) when (method == HttpMethod.Post && !cancellationToken.IsCancellationRequested)
        {
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.AmbiguousAfterHandoff,
                "formbricks_post_response_lost",
                innerException: exception);
        }

        using (response)
        {
            string body = await ReadBoundedStringAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (method == HttpMethod.Post)
                {
                    throw new RegistrationProviderSubmissionDeliveryException(
                        (int)response.StatusCode is 400 or 401 or 403 or 404 or 409 or 422
                            ? RegistrationProviderSubmissionDeliveryFailureKind.PermanentBeforeHandoff
                            : RegistrationProviderSubmissionDeliveryFailureKind.RetryableBeforeHandoff,
                        $"formbricks_http_{(int)response.StatusCode}");
                }

                throw new HttpRequestException($"Formbricks management request failed with HTTP {(int)response.StatusCode}.");
            }

            return JsonDocument.Parse(body);
        }
    }

    private async Task<string> ResolveApiTokenAsync(Guid tenantId, RegistrationProviderConnection connection, CancellationToken cancellationToken)
    {
        if (connection.ApiTokenSecretBindingId is not { } bindingId)
        {
            throw new InvalidOperationException("Formbricks API token binding is required.");
        }

        SecretResolutionResult secret = await secretResolver.ResolveTenantBindingAsync(tenantId, bindingId, cancellationToken);
        return !string.IsNullOrWhiteSpace(secret?.Value)
            ? secret.Value
            : throw new InvalidOperationException("Formbricks API token could not be resolved.");
    }

    private async Task<string?> ResolveWebhookSecretAsync(RegistrationProviderCallbackVerificationRequest request, CancellationToken cancellationToken)
    {
        Guid? bindingId = request.Binding.WebhookSecretBindingId ?? request.Connection.WebhookSecretBindingId;
        if (bindingId is null)
        {
            return null;
        }

        return (await secretResolver.ResolveTenantBindingAsync(request.TenantId, bindingId.Value, cancellationToken)).Value;
    }

    private static IReadOnlyList<object> BuildSurveyQuestions(RegistrationFormVersion version) =>
        [.. version.Sections
            .Where(section => !section.IsDeleted)
            .OrderBy(section => section.Ordinal)
            .SelectMany(section => section.Fields.Where(field => !field.IsDeleted).OrderBy(field => field.Ordinal))
            .Select(BuildSurveyQuestion)];

    private static RegistrationProviderSchemaSnapshot Snapshot(RegistrationFormVersion version) => new(
        [.. version.Sections
            .Where(section => !section.IsDeleted)
            .OrderBy(section => section.Ordinal)
            .SelectMany(section => section.Fields.Where(field => !field.IsDeleted).OrderBy(field => field.Ordinal))
            .Select(field => new RegistrationProviderSchemaFieldSnapshot(
                ProviderQuestionId(field),
                field.Label,
                ToPlatformFieldType(ToFormbricksQuestionType((RegistrationFieldTypeEnum)field.FieldTypeId, field.IsMulti)),
                field.IsRequired,
                [.. field.Options
                    .Where(option => !option.IsDeleted && option.RetiredAt is null)
                    .OrderBy(option => option.Ordinal)
                    .Select(option => new RegistrationProviderSchemaOptionSnapshot(ProviderOptionId(option), option.Label))]))]);

    private static string Fingerprint(RegistrationProviderSchemaSnapshot snapshot)
    {
        StringBuilder canonical = new();
        foreach (RegistrationProviderSchemaFieldSnapshot field in snapshot.Fields)
        {
            canonical.Append(field.Key.Length).Append(':').Append(field.Key)
                .Append('|').Append(field.Label.Length).Append(':').Append(field.Label)
                .Append('|').Append(field.Type)
                .Append('|').Append(field.IsRequired ? '1' : '0').Append('\n');
            foreach (RegistrationProviderSchemaOptionSnapshot option in field.Options)
            {
                canonical.Append(option.Key.Length).Append(':').Append(option.Key)
                    .Append('|').Append(option.Label.Length).Append(':').Append(option.Label).Append('\n');
            }
        }

        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static object BuildSurveyQuestion(RegistrationFormField field)
    {
        RegistrationFieldTypeEnum type = (RegistrationFieldTypeEnum)field.FieldTypeId;

        return new
        {
            id = ProviderQuestionId(field),
            type = ToFormbricksQuestionType(type, field.IsMulti),
            headline = new Dictionary<string, string> { ["default"] = field.Label },
            required = field.IsRequired,
            choices = field.Options
                .Where(option => !option.IsDeleted && option.RetiredAt is null)
                .OrderBy(option => option.Ordinal)
                .Select(option => new
                {
                    id = ProviderOptionId(option),
                    label = new Dictionary<string, string> { ["default"] = option.Label }
                })
                .ToArray()
        };
    }

    private static IReadOnlyList<RegistrationProviderSchemaFieldSnapshot> ReadSurveyFields(JsonElement survey)
    {
        JsonElement questions = survey.TryGetProperty("questions", out JsonElement property) ? property : default;
        if (questions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<RegistrationProviderSchemaFieldSnapshot> fields = [];
        foreach (JsonElement question in questions.EnumerateArray())
        {
            string key = FirstString(question, "id", "key", "name") ?? $"question_{fields.Count + 1}";
            string label = FirstLocalizedString(question, "headline", "label", "title") ?? key;
            string providerType = FirstString(question, "type") ?? "unknown";
            bool required = question.TryGetProperty("required", out JsonElement requiredProperty) && requiredProperty.ValueKind == JsonValueKind.True;
            fields.Add(new RegistrationProviderSchemaFieldSnapshot(key, label, ToPlatformFieldType(providerType), required, ReadOptions(question)));
        }

        return fields;
    }

    private static IReadOnlyList<RegistrationProviderSchemaOptionSnapshot> ReadOptions(JsonElement question)
    {
        JsonElement choices = question.TryGetProperty("choices", out JsonElement property) ? property : default;
        if (choices.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<RegistrationProviderSchemaOptionSnapshot> options = [];
        foreach (JsonElement choice in choices.EnumerateArray())
        {
            string key = FirstString(choice, "id", "key", "value") ?? $"option_{options.Count + 1}";
            string label = FirstLocalizedString(choice, "label", "text", "name") ?? key;
            options.Add(new RegistrationProviderSchemaOptionSnapshot(key, label));
        }

        return options;
    }

    private static Dictionary<string, JsonElement> ReadAnswerDictionary(JsonElement response)
    {
        JsonElement data = response.TryGetProperty("data", out JsonElement property) ? property : default;
        if (data.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        Dictionary<string, JsonElement> answers = new(StringComparer.Ordinal);
        foreach (JsonProperty answer in data.EnumerateObject())
        {
            answers[answer.Name] = answer.Value.Clone();
        }

        return answers;
    }

    private static Guid? ReadAttemptId(JsonElement response)
    {
        foreach (string containerName in new[] { "variables", "data" })
        {
            if (response.TryGetProperty(containerName, out JsonElement container) &&
                container.ValueKind == JsonValueKind.Object &&
                FirstString(container, "islamuEventAttemptId") is { } value &&
                Guid.TryParse(value, out Guid attemptId) && attemptId != Guid.Empty)
            {
                return attemptId;
            }
        }

        return null;
    }

    private static async Task<string> ReadBoundedStringAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            using MemoryStream memory = new();
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                if (memory.Length + read > MaxResponseBytes)
                {
                    throw new InvalidOperationException("Formbricks response body exceeded the configured bound.");
                }

                memory.Write(buffer, 0, read);
            }

            return Encoding.UTF8.GetString(memory.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Uri BuildManagementUri(RegistrationProviderConnection connection, string relativePathAndQuery)
    {
        Uri baseUri = new(connection.ManagementApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, relativePathAndQuery.TrimStart('/'));
    }

    private static Uri BuildPublicUri(RegistrationProviderConnection connection, string path, string? query = null)
    {
        UriBuilder builder = new(connection.PublicBaseUrl.TrimEnd('/') + "/" + path.TrimStart('/'));
        if (!string.IsNullOrWhiteSpace(query)) builder.Query = query;
        return builder.Uri;
    }

    private static JsonElement UnwrapData(JsonElement root) =>
        root.TryGetProperty("data", out JsonElement data) && data.ValueKind is JsonValueKind.Object ? data : root;

    private static string RequiredString(JsonElement element, string name) =>
        FirstString(element, name) ?? throw new InvalidOperationException($"Formbricks response did not include '{name}'. Remote write acceptance is ambiguous.");

    private static string OptionalRevision(JsonElement element) =>
        FirstString(element, "updatedAt", "revisionId", "updated_at", "createdAt") ?? string.Empty;

    private static DateTime? OptionalReceivedAt(JsonElement element)
    {
        foreach (string name in new[] { "finishedAt", "updatedAt", "createdAt" })
        {
            if (element.TryGetProperty(name, out JsonElement value) && value.TryGetDateTime(out DateTime timestamp))
            {
                return timestamp.ToUniversalTime();
            }
        }

        return null;
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String)
            {
                string? value = property.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }

        return null;
    }

    private static string? FirstLocalizedString(JsonElement element, params string[] names)
    {
        string? direct = FirstString(element, names);
        if (direct is not null) return direct;
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.Object)
            {
                return FirstString(property, "default");
            }
        }

        return null;
    }

    private static string RequireSurveyId(RegistrationProviderBinding binding) =>
        NormalizeId(binding.ProviderSurveyId) ?? throw new InvalidOperationException("Formbricks survey id is required.");

    private static string? NormalizeId(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FieldKey(RegistrationFormField field) => $"{field.Namespace}.{field.Key}";

    private static string ProviderQuestionId(RegistrationFormField field) => "q" + field.Id.ToString("N");

    private static string ProviderOptionId(RegistrationFormFieldOption option) => "o" + option.Id.ToString("N");

    private static string ToFormbricksQuestionType(RegistrationFieldTypeEnum type, bool isMulti) => type switch
    {
        RegistrationFieldTypeEnum.LongText => "openText",
        RegistrationFieldTypeEnum.SingleChoice => "multipleChoiceSingle",
        RegistrationFieldTypeEnum.MultipleChoice => "multipleChoiceMulti",
        RegistrationFieldTypeEnum.Boolean or RegistrationFieldTypeEnum.Consent => "consent",
        RegistrationFieldTypeEnum.Rating => "rating",
        _ => isMulti ? "multipleChoiceMulti" : "openText"
    };

    private static string ToPlatformFieldType(string providerType) => providerType.Trim().ToLowerInvariant() switch
    {
        "multiplechoicesingle" or "multiplechoice" or "nps" => nameof(RegistrationFieldTypeEnum.SingleChoice),
        "multiplechoicemulti" => nameof(RegistrationFieldTypeEnum.MultipleChoice),
        "rating" => nameof(RegistrationFieldTypeEnum.Rating),
        "consent" or "cta" => nameof(RegistrationFieldTypeEnum.Consent),
        "file" or "fileupload" => nameof(RegistrationFieldTypeEnum.OpaqueExternal),
        _ => nameof(RegistrationFieldTypeEnum.ShortText)
    };

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string? value)
    {
        foreach (KeyValuePair<string, string> header in headers)
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(header.Value))
            {
                value = header.Value.Trim();
                return true;
            }
        }

        value = null;
        return false;
    }

    private static IEnumerable<string> ReadSignatures(string header) => header
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .SelectMany(part => part.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Select(part => part.StartsWith("v1,", StringComparison.Ordinal) ? part[3..] : string.Empty)
        .Where(part => part.Length > 0);

    private static bool MatchesSignature(string signature, byte[] expected)
    {
        try
        {
            byte[] actual = Convert.FromBase64String(signature);
            try
            {
                return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actual);
            }
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] BuildSignedContent(string webhookId, string timestamp, ReadOnlySpan<byte> body)
    {
        byte[] prefix = Encoding.UTF8.GetBytes($"{webhookId}.{timestamp}.");
        byte[] result = new byte[prefix.Length + body.Length];
        prefix.CopyTo(result, 0);
        body.CopyTo(result.AsSpan(prefix.Length));
        CryptographicOperations.ZeroMemory(prefix);
        return result;
    }

    private static TimeSpan Duration(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left - right : right - left;

    private static string? TryReadProviderSubmissionId(ReadOnlySpan<byte> body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body.ToArray());
            JsonElement root = document.RootElement;
            if (FirstString(root, "providerSubmissionId", "responseId", "id") is { } id) return id;
            if (root.TryGetProperty("data", out JsonElement data) && FirstString(data, "id", "responseId") is { } nested) return nested;
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string EncodeCursor(string provider, DateTime sinceUtc, string position) =>
        "registration-provider-cursor:" + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{provider}|{sinceUtc:O}|{position}"));

    private static (DateTime SinceUtc, int Offset) DecodeCursor(string? cursor, string provider, DateTime fallbackSinceUtc)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (fallbackSinceUtc, 0);
        if (!cursor.StartsWith("registration-provider-cursor:", StringComparison.Ordinal)) return (fallbackSinceUtc, 0);

        string value = Encoding.UTF8.GetString(Convert.FromBase64String(cursor["registration-provider-cursor:".Length..]));
        string[] parts = value.Split('|', 3);
        if (parts.Length != 3 || !string.Equals(parts[0], provider, StringComparison.Ordinal) ||
            !DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime sinceUtc) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int offset) || offset < 0)
        {
            throw new InvalidOperationException("Registration provider continuation cursor is invalid.");
        }

        return (DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), offset);
    }
}
