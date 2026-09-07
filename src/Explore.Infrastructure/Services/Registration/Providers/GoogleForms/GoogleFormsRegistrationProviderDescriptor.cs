using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IdentityModel.Tokens.Jwt;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Services.Webhooks;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Explore.Infrastructure.Services.Registration.Providers.GoogleForms;

public sealed class GoogleFormsRegistrationProviderDescriptor(GoogleFormsRegistrationProviderAdapter adapter) :
    IRegistrationProviderDescriptor,
    IRegistrationProviderPresentation,
    IRegistrationProviderSchemaReader,
    IRegistrationProviderFormProvisioner,
    IRegistrationProviderFormCompatibilityChecker,
    IRegistrationProviderSubmissionReader,
    IRegistrationProviderCallbackVerifier,
    IRegistrationProviderSubscriptionManager,
    IRegistrationProviderReconciliationProvider,
    IRegistrationProviderDelegatedAutomation
{
    public const string ContractVersion = "GOOGLE_FORMS_ENTRY_CORRELATION_V1";
    public const string CorrelationPlatformFieldKey = "system.registration_attempt_token";
    public static readonly string[] ImportScopes =
    [
        "openid",
        "email",
        "https://www.googleapis.com/auth/forms.body.readonly",
        "https://www.googleapis.com/auth/forms.responses.readonly"
    ];

    public static readonly string[] ManagedProvisionScopes =
    [
        .. ImportScopes,
        "https://www.googleapis.com/auth/forms.body"
    ];

    public static RegistrationProviderTuple SupportedTuple { get; } = new(
        "GOOGLE_FORMS",
        "GOOGLE_WORKSPACE",
        "v1",
        "ISLAMU_EVENT_GOOGLE_FORMS_PUBSUB_V1",
        "2026-08-11");

    public RegistrationProviderTuple Tuple => SupportedTuple;

    public RegistrationProviderCapabilitySet ProvenCapabilities { get; } = new(
        Redirect: true,
        Embed: true,
        Manual: true,
        SchemaRead: true,
        FormProvision: true,
        SubmissionWrite: false,
        SubmissionRead: true,
        CallbackVerification: true,
        SubscriptionManagement: true,
        Reconciliation: true,
        SubmissionSink: false,
        AutoFinalize: false);

    public string ConnectorContractVersion => ContractVersion;
    public string RequiredCorrelationPlatformFieldKey => CorrelationPlatformFieldKey;

    public Task<RegistrationProviderPresentationResult> GetPresentationAsync(RegistrationProviderPresentationRequest request, CancellationToken cancellationToken) =>
        adapter.GetPresentationAsync(request, cancellationToken);

    public Task<RegistrationProviderSchemaReadResult> ReadSchemaAsync(RegistrationProviderSchemaReadRequest request, CancellationToken cancellationToken) =>
        adapter.ReadSchemaAsync(request, cancellationToken);

    public Task<RegistrationProviderFormProvisionResult> ProvisionFormAsync(RegistrationProviderFormProvisionRequest request, CancellationToken cancellationToken) =>
        adapter.ProvisionFormAsync(request, cancellationToken);

    public RegistrationProviderFormCompatibilityResult CheckCompatibility(RegistrationFormVersion formVersion) =>
        adapter.CheckCompatibility(formVersion);

    public Task<RegistrationProviderSubmissionReadResult> ReadSubmissionAsync(RegistrationProviderSubmissionReadRequest request, CancellationToken cancellationToken) =>
        adapter.ReadSubmissionAsync(request, cancellationToken);

    public Task<RegistrationProviderCallbackVerificationResult> VerifyCallbackAsync(RegistrationProviderCallbackVerificationRequest request, CancellationToken cancellationToken) =>
        adapter.VerifyCallbackAsync(request, cancellationToken);

    public Task<RegistrationProviderSubscriptionResult> EnsureSubscriptionAsync(RegistrationProviderSubscriptionRequest request, CancellationToken cancellationToken) =>
        adapter.EnsureSubscriptionAsync(request, cancellationToken);

    public Task<RegistrationProviderReconciliationResult> ReconcileAsync(RegistrationProviderReconciliationRequest request, CancellationToken cancellationToken) =>
        adapter.ReconcileAsync(request, cancellationToken);
}

public sealed class GoogleFormsRegistrationProviderAdapter(
    HttpClient httpClient,
    ISecretResolver secretResolver,
    IRegistrationProviderConnectionCheckpoint connectionCheckpoint,
    IGooglePubSubOidcTokenValidator pubSubOidcTokenValidator) :
    IRegistrationProviderPresentation,
    IRegistrationProviderSchemaReader,
    IRegistrationProviderFormProvisioner,
    IRegistrationProviderFormCompatibilityChecker,
    IRegistrationProviderSubmissionReader,
    IRegistrationProviderCallbackVerifier,
    IRegistrationProviderSubscriptionManager,
    IRegistrationProviderReconciliationProvider
{
    public const string HttpClientName = "RegistrationProvider.GoogleForms";
    private const int MaxResponseBytes = 256 * 1024;
    private const string GoogleOAuthTokenEndpoint = "https://oauth2.googleapis.com/token";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex EntryFieldKeyPattern = new("^entry\\.(\\d{1,20})$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public Task<RegistrationProviderPresentationResult> GetPresentationAsync(
        RegistrationProviderPresentationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? formId = NormalizeId(request.Binding.ProviderSurveyId);
        if (formId is null)
        {
            return Task.FromResult(new RegistrationProviderPresentationResult(false, false, true));
        }

        Uri redirect = BuildPublicUri(request.Connection, $"forms/d/e/{Uri.EscapeDataString(formId)}/viewform");
        Uri embed = BuildPublicUri(request.Connection, $"forms/d/e/{Uri.EscapeDataString(formId)}/viewform", "embedded=true");
        if (TryGetSingleCorrelationMapping(request.Binding, out RegistrationProviderFieldMapping? correlation) &&
            request.AttemptId is { } attemptId &&
            !string.IsNullOrWhiteSpace(request.AttemptCapabilityToken))
        {
            string correlationValue = $"{attemptId:D}|{request.AttemptCapabilityToken}";
            redirect = AppendQuery(redirect, correlation!.ProviderFieldKey, correlationValue);
            embed = AppendQuery(embed, correlation.ProviderFieldKey, correlationValue);
        }
        return Task.FromResult(new RegistrationProviderPresentationResult(true, true, true, redirect, embed));
    }

    public async Task<RegistrationProviderSchemaReadResult> ReadSchemaAsync(
        RegistrationProviderSchemaReadRequest request,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await SendJsonAsync(
            request.TenantId,
            request.Connection,
            HttpMethod.Get,
            $"forms/{Uri.EscapeDataString(RequireFormId(request.Binding))}",
            payload: null,
            cancellationToken);
        RegistrationProviderSchemaSnapshot snapshot = new(ReadFields(document.RootElement));
        return new RegistrationProviderSchemaReadResult(snapshot, IsPublishedAndAcceptingResponses(document.RootElement), Fingerprint(snapshot));
    }

    public RegistrationProviderFormCompatibilityResult CheckCompatibility(RegistrationFormVersion formVersion)
    {
        List<RegistrationProviderPreflightIssue> issues = [];
        foreach (RegistrationFormField field in ActiveFields(formVersion))
        {
            RegistrationFieldTypeEnum type = (RegistrationFieldTypeEnum)field.FieldTypeId;
            if (type == RegistrationFieldTypeEnum.File)
            {
                issues.Add(new("google_forms_file_upload_unsupported", $"Field '{FieldKey(field)}' is a file upload field. Google Forms file uploads require Drive scope, which this adapter does not request.", field.Id));
            }
            else if (type is not (RegistrationFieldTypeEnum.ShortText or RegistrationFieldTypeEnum.LongText or RegistrationFieldTypeEnum.Email or
                     RegistrationFieldTypeEnum.Phone or RegistrationFieldTypeEnum.Url or RegistrationFieldTypeEnum.SingleChoice or
                     RegistrationFieldTypeEnum.MultipleChoice or RegistrationFieldTypeEnum.Date or RegistrationFieldTypeEnum.Time or
                     RegistrationFieldTypeEnum.Integer or RegistrationFieldTypeEnum.Decimal))
            {
                issues.Add(new("google_forms_field_unsupported", $"Field '{FieldKey(field)}' is not supported by Google Forms managed provisioning.", field.Id));
            }
            else if (type is RegistrationFieldTypeEnum.SingleChoice or RegistrationFieldTypeEnum.MultipleChoice && ActiveOptions(field).Count < 1)
            {
                issues.Add(new("google_forms_options_unsupported", $"Field '{FieldKey(field)}' requires at least one active option.", field.Id));
            }
            else if (type is RegistrationFieldTypeEnum.SingleChoice or RegistrationFieldTypeEnum.MultipleChoice &&
                     ActiveOptions(field).Select(option => option.Label).Distinct(StringComparer.Ordinal).Count() != ActiveOptions(field).Count)
            {
                issues.Add(new("google_forms_duplicate_option_labels_unsupported", $"Field '{FieldKey(field)}' has duplicate option labels. Google Forms does not preserve local option identifiers.", field.Id));
            }
        }

        if (formVersion.Rules.Any(rule => !rule.IsDeleted))
        {
            issues.Add(new("google_forms_conditions_unsupported", "Google Forms managed provisioning does not support local conditional form rules."));
        }

        return new(Fingerprint(Snapshot(formVersion)), issues);
    }

    public async Task<RegistrationProviderFormProvisionResult> ProvisionFormAsync(
        RegistrationProviderFormProvisionRequest request,
        CancellationToken cancellationToken)
    {
        RegistrationProviderFormCompatibilityResult compatibility = CheckCompatibility(request.FormVersion);
        if (!compatibility.IsCompatible)
        {
            throw new InvalidOperationException("Google Forms managed provisioning rejected unsupported fields, including possible file upload fields.");
        }

        using JsonDocument create = await SendJsonAsync(
            request.TenantId,
            request.Connection,
            HttpMethod.Post,
            "forms",
            new { info = new { title = $"ISLAMU Event registration {request.FormVersion.Id:N}" } },
            cancellationToken);
        string formId = RequiredString(create.RootElement, "formId");

        using JsonDocument batch = await SendJsonAsync(
            request.TenantId,
            request.Connection,
            HttpMethod.Post,
            $"forms/{Uri.EscapeDataString(formId)}:batchUpdate",
            new { requests = BuildCreateItemRequests(request.FormVersion) },
            cancellationToken);

        _ = OptionalRevision(batch.RootElement);

        using JsonDocument publish = await SendJsonAsync(
            request.TenantId,
            request.Connection,
            HttpMethod.Post,
            $"forms/{Uri.EscapeDataString(formId)}:setPublishSettings",
            new { publishSettings = new { publishState = new { isPublished = true, isAcceptingResponses = true } } },
            cancellationToken);

        using JsonDocument published = await SendJsonAsync(
            request.TenantId,
            request.Connection,
            HttpMethod.Get,
            $"forms/{Uri.EscapeDataString(formId)}",
            payload: null,
            cancellationToken);
        if (!IsPublishedAndAcceptingResponses(published.RootElement))
        {
            throw new InvalidOperationException("Google Forms managed provisioning failed closed because the created form is not published and accepting responses.");
        }

        return new RegistrationProviderFormProvisionResult(formId, OptionalRevision(published.RootElement) ?? OptionalRevision(create.RootElement) ?? string.Empty);
    }

    public async Task<RegistrationProviderSubmissionReadResult> ReadSubmissionAsync(
        RegistrationProviderSubmissionReadRequest request,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await SendJsonAsync(
            request.TenantId,
            request.Connection,
            HttpMethod.Get,
            $"forms/{Uri.EscapeDataString(RequireFormId(request.Binding))}/responses/{Uri.EscapeDataString(request.ProviderSubmissionId)}",
            payload: null,
            cancellationToken);
        JsonElement response = document.RootElement;
        if (ContainsFileUploadAnswers(response))
        {
            throw new RegistrationProviderUnsupportedSubmissionException("PROVIDER_FILE_UPLOAD_UNSUPPORTED");
        }

        Dictionary<string, JsonElement> answers = ReadAnswerDictionary(response);
        (Guid? attemptId, string? token) = ReadCorrelation(request.Binding, answers);
        return new RegistrationProviderSubmissionReadResult(
            RequiredString(response, "responseId"),
            OptionalRevision(response) ?? string.Empty,
            OptionalReceivedAt(response),
            attemptId,
            answers,
            token);
    }

    public Task<RegistrationProviderCallbackVerificationResult> VerifyCallbackAsync(
        RegistrationProviderCallbackVerificationRequest request,
        CancellationToken cancellationToken) => VerifyPubSubCallbackAsync(request, cancellationToken);

    public async Task<RegistrationProviderSubscriptionResult> EnsureSubscriptionAsync(
        RegistrationProviderSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        GooglePubSubConfigurationReference pubSub = GooglePubSubConfigurationReference.Parse(request.Connection.PubSubConfigurationReference);
        string formId = RequireFormId(request.Binding);
        string relativePath = string.IsNullOrWhiteSpace(request.Binding.ProviderWebhookId)
            ? $"forms/{Uri.EscapeDataString(formId)}/watches"
            : $"forms/{Uri.EscapeDataString(formId)}/watches/{Uri.EscapeDataString(request.Binding.ProviderWebhookId)}:renew";
        using JsonDocument document = await SendJsonAsync(
            request.TenantId,
            request.Connection,
            HttpMethod.Post,
            relativePath,
            new { watch = new { target = new { topic = new { topicName = pubSub.TopicName } }, eventType = "RESPONSES" } },
            cancellationToken);
        JsonElement watch = document.RootElement.TryGetProperty("watch", out JsonElement wrapped) ? wrapped : document.RootElement;
        return new RegistrationProviderSubscriptionResult(
            true,
            FirstString(watch, "id", "watchId"),
            false,
            OptionalTimestamp(watch, "expireTime", "expirationTime"));
    }

    public async Task<RegistrationProviderReconciliationResult> ReconcileAsync(
        RegistrationProviderReconciliationRequest request,
        CancellationToken cancellationToken)
    {
        (DateTime sinceUtc, string? pageToken) = DecodeCursor(request.ContinuationCursor, "google", request.SinceUtc);
        string filter = Uri.EscapeDataString("timestamp >= " + DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc).ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        List<RegistrationProviderReconciledSubmission> submissions = [];
        string? nextCheckpoint = null;
        for (int page = 0; page < 5; page++)
        {
            string query = $"filter={filter}&pageSize=100" + (string.IsNullOrWhiteSpace(pageToken) ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            using JsonDocument document = await SendJsonAsync(
                request.TenantId,
                request.Connection,
                HttpMethod.Get,
                $"forms/{Uri.EscapeDataString(RequireFormId(request.Binding))}/responses?{query}",
                payload: null,
                cancellationToken);
            if (document.RootElement.TryGetProperty("responses", out JsonElement responses) && responses.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement response in responses.EnumerateArray())
                {
                    string responseId = RequiredString(response, "responseId");
                    string revision = OptionalRevision(response) ?? responseId;
                    DateTime? receivedAt = OptionalReceivedAt(response);
                    if (receivedAt is { } timestamp)
                    {
                        nextCheckpoint = timestamp.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
                    }
                    submissions.Add(new(responseId, revision, receivedAt));
                }
            }

            pageToken = FirstString(document.RootElement, "nextPageToken");
            if (string.IsNullOrWhiteSpace(pageToken))
            {
                return new RegistrationProviderReconciliationResult(submissions.Count, false, submissions, nextCheckpoint);
            }
        }

        return new RegistrationProviderReconciliationResult(submissions.Count, true, submissions, null,
            string.IsNullOrWhiteSpace(pageToken) ? null : EncodeCursor("google", sinceUtc, pageToken));
    }

    private async Task<RegistrationProviderCallbackVerificationResult> VerifyPubSubCallbackAsync(
        RegistrationProviderCallbackVerificationRequest request,
        CancellationToken cancellationToken)
    {
        GooglePubSubConfigurationReference pubSub = GooglePubSubConfigurationReference.Parse(request.Connection.PubSubConfigurationReference);
        if (!await pubSubOidcTokenValidator.ValidateAsync(request.Headers, pubSub, cancellationToken))
        {
            return new RegistrationProviderCallbackVerificationResult(false, "google_forms_pubsub_auth_invalid");
        }

        using JsonDocument document = JsonDocument.Parse(request.Body);
        if (!document.RootElement.TryGetProperty("message", out JsonElement message))
        {
            return new RegistrationProviderCallbackVerificationResult(false, "google_forms_pubsub_envelope_invalid");
        }

        string messageId = RequiredString(message, "messageId");
        string formId = AttributeString(message, "formId") ?? ReadDataString(message, "formId") ?? string.Empty;
        string watchId = AttributeString(message, "watchId") ?? ReadDataString(message, "watchId") ?? string.Empty;
        string eventType = AttributeString(message, "eventType") ?? ReadDataString(message, "eventType") ?? string.Empty;
        if (!string.Equals(formId, RequireFormId(request.Binding), StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(watchId) ||
            !string.Equals(eventType, "RESPONSES", StringComparison.Ordinal))
        {
            return new RegistrationProviderCallbackVerificationResult(false, "google_forms_pubsub_binding_mismatch");
        }

        return new RegistrationProviderCallbackVerificationResult(
            true,
            Receipt: "google-forms-pubsub:v1",
            ProviderSubmissionId: messageId,
            EffectKind: RegistrationProviderSubmissionIncomingWebhookHandler.ResponseSweepEffectKind);
    }

    private async Task<JsonDocument> SendJsonAsync(Guid tenantId, RegistrationProviderConnection connection, HttpMethod method, string relativePathAndQuery, object? payload, CancellationToken cancellationToken)
    {
        Uri uri = BuildManagementUri(connection, relativePathAndQuery);
        if (!string.Equals(uri.Host, "forms.googleapis.com", StringComparison.OrdinalIgnoreCase) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Google Forms management origin must be pinned to https://forms.googleapis.com.");
        }

        using HttpRequestMessage message = new(method, uri);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await ResolveAccessTokenAsync(tenantId, connection, cancellationToken));
        if (payload is not null)
        {
            message.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        string body = await ReadBoundedStringAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"google_forms_http_{(int)response.StatusCode}");
        }

        await connectionCheckpoint.RecordAccessValidatedAsync(tenantId, connection.Id, cancellationToken);
        return JsonDocument.Parse(body);
    }

    private async Task<string> ResolveAccessTokenAsync(Guid tenantId, RegistrationProviderConnection connection, CancellationToken cancellationToken)
    {
        if (connection.ApiTokenSecretBindingId is not { } bindingId)
        {
            throw new InvalidOperationException("Google Forms OAuth token binding is required.");
        }

        SecretResolutionResult secret = await secretResolver.ResolveTenantBindingAsync(tenantId, bindingId, cancellationToken);
        string value = secret?.Value?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            throw new InvalidOperationException("Google Forms OAuth token could not be resolved.");
        }

        if (!value.StartsWith('{'))
        {
            return value;
        }

        using JsonDocument token = JsonDocument.Parse(value);
        if (FirstString(token.RootElement, "access_token") is { } accessToken)
        {
            return accessToken;
        }

        return await ExchangeRefreshTokenAsync(tenantId, connection.Id, token.RootElement, cancellationToken);
    }

    private async Task<string> ExchangeRefreshTokenAsync(Guid tenantId, Guid connectionId, JsonElement token, CancellationToken cancellationToken)
    {
        string refreshToken = RequiredString(token, "refresh_token");
        string clientId = RequiredString(token, "client_id");
        string clientSecret = RequiredString(token, "client_secret");
        using HttpRequestMessage message = new(HttpMethod.Post, GoogleOAuthTokenEndpoint);
        message.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        });

        using HttpResponseMessage response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        string body = await ReadBoundedStringAsync(response, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException($"google_forms_oauth_http_{(int)response.StatusCode}");
        }

        using JsonDocument document = JsonDocument.Parse(body);
        string accessToken = FirstString(document.RootElement, "access_token") ??
            throw new InvalidOperationException("Google Forms OAuth refresh response did not include an access_token.");
        await connectionCheckpoint.RecordCredentialRefreshAsync(tenantId, connectionId, cancellationToken);
        return accessToken;
    }

    private static List<object> BuildCreateItemRequests(RegistrationFormVersion version)
    {
        List<object> requests = [];
        int index = 0;
        foreach (RegistrationFormField field in ActiveFields(version))
        {
            requests.Add(new
            {
                createItem = new
                {
                    item = BuildItem(field),
                    location = new { index }
                }
            });
            index++;
        }

        return requests;
    }

    private static object BuildItem(RegistrationFormField field)
    {
        RegistrationFieldTypeEnum type = (RegistrationFieldTypeEnum)field.FieldTypeId;
        return new
        {
            itemId = ProviderQuestionId(field),
            title = field.Label,
            questionItem = new
            {
                question = new
                {
                    required = field.IsRequired,
                    textQuestion = IsText(type) ? new { paragraph = type == RegistrationFieldTypeEnum.LongText } : null,
                    choiceQuestion = IsChoice(type) ? new
                    {
                        type = type == RegistrationFieldTypeEnum.MultipleChoice ? "CHECKBOX" : "RADIO",
                        options = ActiveOptions(field).Select(option => new { value = option.Label }).ToArray(),
                        shuffle = false
                    } : null,
                    dateQuestion = type == RegistrationFieldTypeEnum.Date ? new { includeTime = false, includeYear = true } : null,
                    timeQuestion = type == RegistrationFieldTypeEnum.Time ? new { duration = false } : null
                }
            }
        };
    }

    private static RegistrationProviderSchemaSnapshot Snapshot(RegistrationFormVersion version) => new(
        [.. ActiveFields(version).Select(field => new RegistrationProviderSchemaFieldSnapshot(
            ProviderQuestionId(field),
            field.Label,
            ToPlatformFieldType((RegistrationFieldTypeEnum)field.FieldTypeId),
            field.IsRequired,
            [.. ActiveOptions(field).Select(option => new RegistrationProviderSchemaOptionSnapshot(option.Label, option.Label))]))]);

    private static List<RegistrationProviderSchemaFieldSnapshot> ReadFields(JsonElement form)
    {
        if (!form.TryGetProperty("items", out JsonElement items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<RegistrationProviderSchemaFieldSnapshot> fields = [];
        foreach (JsonElement item in items.EnumerateArray())
        {
            JsonElement question = item.TryGetProperty("questionItem", out JsonElement questionItem) && questionItem.TryGetProperty("question", out JsonElement value) ? value : default;
            string key = FirstString(item, "itemId") ?? $"item_{fields.Count + 1}";
            fields.Add(new RegistrationProviderSchemaFieldSnapshot(
                key,
                FirstString(item, "title") ?? key,
                ToPlatformFieldType(question),
                question.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.True,
                ReadOptions(question)));
        }

        return fields;
    }

    private static List<RegistrationProviderSchemaOptionSnapshot> ReadOptions(JsonElement question)
    {
        if (!question.TryGetProperty("choiceQuestion", out JsonElement choice) ||
            !choice.TryGetProperty("options", out JsonElement options) || options.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<RegistrationProviderSchemaOptionSnapshot> result = [];
        foreach (JsonElement option in options.EnumerateArray())
        {
            string label = FirstString(option, "value") ?? $"option_{result.Count + 1}";
            result.Add(new RegistrationProviderSchemaOptionSnapshot(label, label));
        }

        return result;
    }

    private static Dictionary<string, JsonElement> ReadAnswerDictionary(JsonElement response)
    {
        if (!response.TryGetProperty("answers", out JsonElement answers) || answers.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        Dictionary<string, JsonElement> result = new(StringComparer.Ordinal);
        foreach (JsonProperty answer in answers.EnumerateObject())
        {
            result[answer.Name] = answer.Value.Clone();
        }

        return result;
    }

    private static bool TryGetSingleCorrelationMapping(RegistrationProviderBinding binding, out RegistrationProviderFieldMapping? mapping)
    {
        List<RegistrationProviderFieldMapping> matches = [.. binding.FieldMappings.Where(candidate =>
            !candidate.IsDeleted &&
            string.Equals(candidate.PlatformFieldKey, GoogleFormsRegistrationProviderDescriptor.CorrelationPlatformFieldKey, StringComparison.Ordinal) &&
            EntryFieldKeyPattern.IsMatch(candidate.ProviderFieldKey))];
        mapping = matches.Count == 1 ? matches[0] : null;
        return mapping is not null;
    }

    private static (Guid? AttemptId, string? Token) ReadCorrelation(
        RegistrationProviderBinding binding,
        Dictionary<string, JsonElement> answers)
    {
        if (!TryGetSingleCorrelationMapping(binding, out RegistrationProviderFieldMapping? mapping))
        {
            return (null, null);
        }

        Match match = EntryFieldKeyPattern.Match(mapping!.ProviderFieldKey);
        string answerKey = match.Groups[1].Value;
        if (!answers.TryGetValue(answerKey, out JsonElement answer) || TryReadFirstTextAnswer(answer) is not { } raw)
        {
            return (null, null);
        }

        string[] parts = raw.Split('|', StringSplitOptions.None);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out Guid attemptId) || attemptId == Guid.Empty || string.IsNullOrWhiteSpace(parts[1]) || parts[1].Length > 256)
        {
            throw new FormatException("Google Forms correlation answer must be a bounded attemptId|token pair.");
        }

        return (attemptId, parts[1].Trim());
    }

    private static string? TryReadFirstTextAnswer(JsonElement answer)
    {
        if (answer.ValueKind == JsonValueKind.String)
        {
            return Bounded(answer.GetString());
        }

        if (answer.ValueKind == JsonValueKind.Object &&
            answer.TryGetProperty("textAnswers", out JsonElement textAnswers) &&
            textAnswers.TryGetProperty("answers", out JsonElement answers) &&
            answers.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in answers.EnumerateArray())
            {
                if (FirstString(item, "value") is { } value)
                {
                    return Bounded(value);
                }
            }
        }

        return null;
    }

    private static string? Bounded(string? value) => value?.Trim() is { Length: > 0 and <= 512 } text ? text : null;

    private static bool ContainsFileUploadAnswers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "fileUploadAnswers", StringComparison.Ordinal))
                {
                    return true;
                }
                if (ContainsFileUploadAnswers(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (ContainsFileUploadAnswers(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

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

    private static bool IsPublishedAndAcceptingResponses(JsonElement form) =>
        form.TryGetProperty("settings", out JsonElement settings) &&
        settings.TryGetProperty("publishSettings", out JsonElement publishSettings) &&
        publishSettings.TryGetProperty("publishState", out JsonElement publishState) &&
        publishState.TryGetProperty("isPublished", out JsonElement isPublished) &&
        isPublished.ValueKind == JsonValueKind.True &&
        publishState.TryGetProperty("isAcceptingResponses", out JsonElement accepting) &&
        accepting.ValueKind == JsonValueKind.True;

    private static DateTime? OptionalReceivedAt(JsonElement element)
    {
        foreach (string name in new[] { "lastSubmittedTime", "createTime" })
        {
            if (element.TryGetProperty(name, out JsonElement value) && value.TryGetDateTime(out DateTime timestamp))
            {
                return timestamp.ToUniversalTime();
            }
        }

        return null;
    }

    private static DateTime? OptionalTimestamp(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement value) && value.TryGetDateTime(out DateTime timestamp))
            {
                return DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
            }
        }

        return null;
    }

    private static string? OptionalRevision(JsonElement element) => FirstString(element, "revisionId", "lastSubmittedTime", "createTime");

    private static string RequiredString(JsonElement element, string name) =>
        FirstString(element, name) ?? throw new InvalidOperationException($"Google Forms response did not include '{name}'.");

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String)
            {
                string? value = property.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }

        return null;
    }

    private static string? AttributeString(JsonElement message, string name) =>
        message.TryGetProperty("attributes", out JsonElement attributes) ? FirstString(attributes, name) : null;

    private static string? ReadDataString(JsonElement message, string name)
    {
        string? data = FirstString(message, "data");
        if (string.IsNullOrWhiteSpace(data)) return null;
        byte[] bytes = Convert.FromBase64String(data);
        using JsonDocument document = JsonDocument.Parse(bytes);
        return FirstString(document.RootElement, name);
    }

    private static List<RegistrationFormField> ActiveFields(RegistrationFormVersion version) =>
        [.. version.Sections
            .Where(section => !section.IsDeleted)
            .OrderBy(section => section.Ordinal)
            .SelectMany(section => section.Fields.Where(field => !field.IsDeleted).OrderBy(field => field.Ordinal))];

    private static List<RegistrationFormFieldOption> ActiveOptions(RegistrationFormField field) =>
        [.. field.Options.Where(option => !option.IsDeleted && option.RetiredAt is null).OrderBy(option => option.Ordinal)];

    private static string ToPlatformFieldType(JsonElement question)
    {
        if (question.ValueKind != JsonValueKind.Object) return nameof(RegistrationFieldTypeEnum.OpaqueExternal);
        if (question.TryGetProperty("fileUploadQuestion", out _)) return nameof(RegistrationFieldTypeEnum.File);
        if (question.TryGetProperty("textQuestion", out JsonElement textQuestion))
        {
            return textQuestion.TryGetProperty("paragraph", out JsonElement paragraph) && paragraph.ValueKind == JsonValueKind.True
                ? nameof(RegistrationFieldTypeEnum.LongText)
                : nameof(RegistrationFieldTypeEnum.ShortText);
        }
        if (question.TryGetProperty("choiceQuestion", out JsonElement choice))
        {
            string? type = FirstString(choice, "type");
            return string.Equals(type, "CHECKBOX", StringComparison.OrdinalIgnoreCase)
                ? nameof(RegistrationFieldTypeEnum.MultipleChoice)
                : nameof(RegistrationFieldTypeEnum.SingleChoice);
        }
        if (question.TryGetProperty("dateQuestion", out _)) return nameof(RegistrationFieldTypeEnum.Date);
        if (question.TryGetProperty("timeQuestion", out _)) return nameof(RegistrationFieldTypeEnum.Time);
        return nameof(RegistrationFieldTypeEnum.OpaqueExternal);
    }

    private static string ToPlatformFieldType(RegistrationFieldTypeEnum type) => type switch
    {
        RegistrationFieldTypeEnum.LongText => nameof(RegistrationFieldTypeEnum.LongText),
        RegistrationFieldTypeEnum.SingleChoice => nameof(RegistrationFieldTypeEnum.SingleChoice),
        RegistrationFieldTypeEnum.MultipleChoice => nameof(RegistrationFieldTypeEnum.MultipleChoice),
        RegistrationFieldTypeEnum.Date => nameof(RegistrationFieldTypeEnum.Date),
        RegistrationFieldTypeEnum.Time => nameof(RegistrationFieldTypeEnum.Time),
        _ => nameof(RegistrationFieldTypeEnum.ShortText)
    };

    private static bool IsText(RegistrationFieldTypeEnum type) => type is RegistrationFieldTypeEnum.ShortText or RegistrationFieldTypeEnum.LongText or RegistrationFieldTypeEnum.Email or RegistrationFieldTypeEnum.Phone or RegistrationFieldTypeEnum.Url or RegistrationFieldTypeEnum.Integer or RegistrationFieldTypeEnum.Decimal;

    private static bool IsChoice(RegistrationFieldTypeEnum type) => type is RegistrationFieldTypeEnum.SingleChoice or RegistrationFieldTypeEnum.MultipleChoice;

    private static Uri BuildManagementUri(RegistrationProviderConnection connection, string relativePathAndQuery) =>
        new(new Uri(connection.ManagementApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute), relativePathAndQuery.TrimStart('/'));

    private static Uri BuildPublicUri(RegistrationProviderConnection connection, string path, string? query = null)
    {
        Uri publicBase = new(connection.PublicBaseUrl, UriKind.Absolute);
        if (publicBase.Scheme != Uri.UriSchemeHttps || !string.Equals(publicBase.Host, "docs.google.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Google Forms public origin must be pinned to https://docs.google.com.");
        }

        UriBuilder builder = new("https://docs.google.com/" + path.TrimStart('/'));
        if (!string.IsNullOrWhiteSpace(query)) builder.Query = query;
        return builder.Uri;
    }

    private static Uri AppendQuery(Uri uri, string name, string value)
    {
        UriBuilder builder = new(uri);
        string prefix = string.IsNullOrWhiteSpace(builder.Query) ? string.Empty : builder.Query.TrimStart('?') + "&";
        builder.Query = prefix + Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value);
        return builder.Uri;
    }

    private static string RequireFormId(RegistrationProviderBinding binding) =>
        NormalizeId(binding.ProviderSurveyId) ?? throw new InvalidOperationException("Google Forms form id is required.");

    private static string? NormalizeId(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EncodeCursor(string provider, DateTime sinceUtc, string position) =>
        "registration-provider-cursor:" + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{provider}|{sinceUtc:O}|{position}"));

    private static (DateTime SinceUtc, string? PageToken) DecodeCursor(string? cursor, string provider, DateTime fallbackSinceUtc)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (fallbackSinceUtc, null);
        if (!cursor.StartsWith("registration-provider-cursor:", StringComparison.Ordinal)) return (fallbackSinceUtc, null);

        string value = Encoding.UTF8.GetString(Convert.FromBase64String(cursor["registration-provider-cursor:".Length..]));
        string[] parts = value.Split('|', 3);
        if (parts.Length != 3 || !string.Equals(parts[0], provider, StringComparison.Ordinal) ||
            !DateTime.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime sinceUtc) ||
            string.IsNullOrWhiteSpace(parts[2]))
        {
            throw new InvalidOperationException("Registration provider continuation cursor is invalid.");
        }

        return (DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc), parts[2]);
    }

    private static string FieldKey(RegistrationFormField field) => $"{field.Namespace}.{field.Key}";

    private static string ProviderQuestionId(RegistrationFormField field) => "q" + field.Id.ToString("N");

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
                    throw new InvalidOperationException("Google Forms response body exceeded the configured bound.");
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
}

public sealed record GooglePubSubConfigurationReference(string TopicName, string Audience, string ServiceAccountEmail)
{
    public static GooglePubSubConfigurationReference Parse(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0) throw new InvalidOperationException("Google Pub/Sub configuration reference is required.");

        if (trimmed.StartsWith('{'))
        {
            using JsonDocument document = JsonDocument.Parse(trimmed);
            return new(Required(document.RootElement, "topicName", "topic"), Required(document.RootElement, "audience"), Required(document.RootElement, "serviceAccountEmail", "email"));
        }

        Dictionary<string, string> values = trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
        return new(
            values.TryGetValue("topic", out string? topic) ? topic : trimmed,
            values.TryGetValue("audience", out string? audience) ? audience : string.Empty,
            values.TryGetValue("serviceAccountEmail", out string? email) ? email : string.Empty);
    }

    private static string Required(JsonElement element, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String)
            {
                string? value = property.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }

        throw new InvalidOperationException("Google Pub/Sub configuration reference is incomplete.");
    }
}

public interface IGooglePubSubOidcTokenValidator
{
    Task<bool> ValidateAsync(IReadOnlyDictionary<string, string> headers, GooglePubSubConfigurationReference reference, CancellationToken cancellationToken);
}

public sealed class GooglePubSubOidcTokenValidator : IGooglePubSubOidcTokenValidator
{
    private static readonly ConfigurationManager<OpenIdConnectConfiguration> ConfigurationManager = new(
        "https://accounts.google.com/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever());

    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager;

    public GooglePubSubOidcTokenValidator() : this(ConfigurationManager)
    {
    }

    public GooglePubSubOidcTokenValidator(IConfigurationManager<OpenIdConnectConfiguration> configurationManager)
    {
        _configurationManager = configurationManager;
    }

    public async Task<bool> ValidateAsync(IReadOnlyDictionary<string, string> headers, GooglePubSubConfigurationReference reference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reference.Audience) || string.IsNullOrWhiteSpace(reference.ServiceAccountEmail) || !TryBearer(headers, out string? token))
        {
            return false;
        }

        OpenIdConnectConfiguration configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);
        TokenValidationParameters parameters = new()
        {
            ValidateIssuer = true,
            ValidIssuers = ["accounts.google.com", "https://accounts.google.com"],
            ValidateAudience = true,
            ValidAudience = reference.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        try
        {
            JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };
            ClaimsPrincipal principal = handler.ValidateToken(token, parameters, out _);
            return string.Equals(principal.FindFirst("email")?.Value, reference.ServiceAccountEmail, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(principal.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryBearer(IReadOnlyDictionary<string, string> headers, out string? token)
    {
        token = null;
        if (!headers.TryGetValue("Authorization", out string? value) && !headers.TryGetValue("authorization", out value)) return false;
        const string prefix = "Bearer ";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        token = value[prefix.Length..].Trim();
        return token.Length > 0;
    }
}
