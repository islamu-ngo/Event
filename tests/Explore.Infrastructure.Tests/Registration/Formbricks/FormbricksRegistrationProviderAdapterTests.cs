using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Registration;
using Explore.Infrastructure.Services.Registration.Providers.Formbricks;

namespace Explore.Infrastructure.Tests.Registration.Formbricks;

public sealed class FormbricksRegistrationProviderAdapterTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid ApiTokenBindingId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000101");
    private static readonly Guid ConnectionWebhookBindingId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000102");
    private static readonly Guid BindingWebhookBindingId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000103");

    [Test]
    public async Task Registry_FormbricksTuplesResolveOnlyExactPinnedEvidence()
    {
        FormbricksRegistrationProviderAdapter adapter = Adapter(new RecordingHandler(_ => Json("{}")), SecretResolver());
        FormbricksCloudRegistrationProviderDescriptor cloud = new(adapter);
        FormbricksSelfHostedRegistrationProviderDescriptor selfHosted = new(adapter);
        RegistrationProviderRegistry registry = new([cloud, selfHosted]);

        await Assert.That(registry.TryResolve(new("FORMBRICKS", "CLOUD", "v1", "ISLAMU_EVENT_FORMBRICKS_V1", "2026-08-10"))).IsSameReferenceAs(cloud);
        await Assert.That(registry.TryResolve(new("FORMBRICKS", "SELF_HOSTED", "v1", "ISLAMU_EVENT_FORMBRICKS_V1", "2026-08-10"))).IsSameReferenceAs(selfHosted);
        await Assert.That(registry.TryResolve(new("FORMBRICKS", "CLOUD", "v2", "ISLAMU_EVENT_FORMBRICKS_V1", "2026-08-10"))).IsNull();
        await Assert.That(cloud.ProvenCapabilities.CallbackVerification).IsTrue();
        await Assert.That(cloud.ProvenCapabilities.SubmissionSink).IsTrue();
        await Assert.That(RegistrationProviderCapabilitySet.FromCodes(["FILE_UPLOAD"])).IsEqualTo(RegistrationProviderCapabilitySet.None);
        await Assert.That(RegistrationProviderCapabilitySet.FromCodes(["MULTILINGUAL_FORMS"])).IsEqualTo(RegistrationProviderCapabilitySet.None);
    }

    [Test]
    public async Task Presentation_AttemptCorrelationUsesHiddenFieldQuery()
    {
        FormbricksRegistrationProviderAdapter adapter = Adapter(new RecordingHandler(_ => Json("{}")), SecretResolver());
        Guid attemptId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000701");
        RegistrationProviderPresentationResult result = await adapter.GetPresentationAsync(new(TenantId, Binding(), Connection(), Tuple(), attemptId), CancellationToken.None);

        await Assert.That(result.RedirectAvailable).IsTrue();
        await Assert.That(result.EmbedAvailable).IsTrue();
        await Assert.That(result.ManualAvailable).IsTrue();
        await Assert.That(result.RedirectUri!.Query).Contains("islamuEventAttemptId=018e4e5c-7f00-7000-8000-000000000701");
        await Assert.That(result.EmbedUri!.Query).Contains("embed=true");
        await Assert.That(result.EmbedUri.Query).Contains("islamuEventAttemptId=018e4e5c-7f00-7000-8000-000000000701");
    }

    [Test]
    public async Task VerifyCallback_AcceptsValidMultiSignatureAndReturnsProviderSubmissionId()
    {
        byte[] body = Encoding.UTF8.GetBytes(Fixture("webhook-response-finished.json"));
        string secret = "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("callback-secret-32-bytes-value"));
        Dictionary<Guid, string> secrets = new() { [BindingWebhookBindingId] = secret, [ConnectionWebhookBindingId] = "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("wrong")) };
        FormbricksRegistrationProviderAdapter adapter = Adapter(new RecordingHandler(_ => Json("{}")), SecretResolver(secrets));
        Dictionary<string, string> headers = SignedHeaders(body, secret, UtcNow, extraInvalidSignature: true);

        RegistrationProviderCallbackVerificationResult result = await adapter.VerifyCallbackAsync(
            new(TenantId, Binding(), Connection(), Tuple(), body, headers), CancellationToken.None);

        await Assert.That(result.IsVerified).IsTrue();
        await Assert.That(result.ProviderSubmissionId).IsEqualTo("resp_123");
    }

    [Test]
    public async Task VerifyCallback_RejectsInvalidAndStaleSignatures()
    {
        byte[] body = Encoding.UTF8.GetBytes(Fixture("webhook-response-finished.json"));
        string secret = "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("callback-secret-32-bytes-value"));
        FormbricksRegistrationProviderAdapter adapter = Adapter(new RecordingHandler(_ => Json("{}")), SecretResolver(new() { [BindingWebhookBindingId] = secret }));

        Dictionary<string, string> invalidHeaders = SignedHeaders(body, secret, UtcNow);
        invalidHeaders["webhook-signature"] = "v1,AAAA";
        RegistrationProviderCallbackVerificationResult invalid = await adapter.VerifyCallbackAsync(
            new(TenantId, Binding(), Connection(), Tuple(), body, invalidHeaders), CancellationToken.None);
        RegistrationProviderCallbackVerificationResult stale = await adapter.VerifyCallbackAsync(
            new(TenantId, Binding(), Connection(), Tuple(), body, SignedHeaders(body, secret, UtcNow.AddMinutes(-6))), CancellationToken.None);

        await Assert.That(invalid.FailureCode).IsEqualTo("formbricks_signature_invalid");
        await Assert.That(stale.FailureCode).IsEqualTo("formbricks_signature_stale");
    }

    [Test]
    public async Task ManagementRequests_UseXApiKeyAndDocumentedV1PathsOnly()
    {
        RecordingHandler handler = new(_ => Json(Fixture("response.json")));
        FormbricksRegistrationProviderAdapter adapter = Adapter(handler, SecretResolver());

        RegistrationProviderSubmissionReadResult result = await adapter.ReadSubmissionAsync(new(TenantId, Binding(), Connection(), Tuple(), "resp_123"), CancellationToken.None);

        await Assert.That(result.ProviderSubmissionId).IsEqualTo("resp_123");
        await Assert.That(result.ReceivedAt).IsEqualTo(new DateTime(2026, 8, 10, 12, 1, 0, DateTimeKind.Utc));
        await Assert.That(result.AttemptId).IsEqualTo(Guid.Parse("018e4e5c-7f00-7000-8000-000000000701"));
        await Assert.That(handler.Requests[0].RequestUri!.ToString()).IsEqualTo("https://api.formbricks.example.test/api/v1/management/responses/resp_123");
        await Assert.That(handler.Requests[0].Headers.TryGetValues("x-api-key", out IEnumerable<string>? values)).IsTrue();
        await Assert.That(values!.Single()).IsEqualTo("api-token");
    }

    [Test]
    public async Task SchemaRead_MapsSurveyQuestionsAndOptions()
    {
        FormbricksRegistrationProviderAdapter adapter = Adapter(new RecordingHandler(_ => Json(Fixture("survey.json"))), SecretResolver());

        RegistrationProviderSchemaReadResult result = await adapter.ReadSchemaAsync(new(TenantId, Binding(), Connection(), Tuple()), CancellationToken.None);

        await Assert.That(result.Snapshot.Fields.Count).IsEqualTo(2);
        await Assert.That(result.Snapshot.Fields[0].Key).IsEqualTo("profile.email");
        await Assert.That(result.Snapshot.Fields[0].IsRequired).IsTrue();
        await Assert.That(result.Snapshot.Fields[1].Type).IsEqualTo(nameof(RegistrationFieldTypeEnum.SingleChoice));
        await Assert.That(result.Snapshot.Fields[1].Options.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ManagedPreflight_UsesActiveSurveyAndMatchingCanonicalFingerprint()
    {
        RecordingHandler handler = new(_ => Json("""
            {"data":{"id":"survey_123","status":"inProgress","questions":[{"id":"q018e4e5c7f0070008000000000000601","type":"openText","headline":{"default":"Email"},"required":true}]}}
            """));
        FormbricksRegistrationProviderAdapter adapter = Adapter(handler, SecretResolver());

        RegistrationProviderFormCompatibilityResult local = adapter.CheckCompatibility(FormVersion());
        RegistrationProviderSchemaReadResult remote = await adapter.ReadSchemaAsync(
            new(TenantId, Binding(), Connection(), Tuple()), CancellationToken.None);

        await Assert.That(local.IsCompatible).IsTrue();
        await Assert.That(remote.IsActive).IsTrue();
        await Assert.That(remote.Fingerprint).IsEqualTo(local.Fingerprint);
    }

    [Test]
    public async Task ManagedPreflight_RejectsUnsupportedFileFieldWithTypedIssue()
    {
        FormbricksRegistrationProviderAdapter adapter = Adapter(new RecordingHandler(_ => Json("{}")), SecretResolver());

        RegistrationProviderFormCompatibilityResult result = adapter.CheckCompatibility(FormVersion(RegistrationFieldTypeEnum.File));

        await Assert.That(result.IsCompatible).IsFalse();
        await Assert.That(result.Issues.Single().Code).IsEqualTo("registration_provider_required_field_unsupported");
    }

    [Test]
    public async Task ProvisionAndSubscription_PostExpectedPayloadsAndReadIds()
    {
        RecordingHandler handler = new(request => request.RequestUri!.AbsolutePath.EndsWith("/webhooks", StringComparison.Ordinal)
            ? Json("{\"data\":{\"id\":\"webhook_123\"}}")
            : Json("{\"data\":{\"id\":\"survey_new\",\"updatedAt\":\"rev_1\"}}"));
        FormbricksRegistrationProviderAdapter adapter = Adapter(handler, SecretResolver());

        RegistrationProviderFormProvisionResult provision = await adapter.ProvisionFormAsync(new(TenantId, Binding(), Connection(), Tuple(), FormVersion()), CancellationToken.None);
        RegistrationProviderSubscriptionResult subscription = await adapter.EnsureSubscriptionAsync(new(TenantId, Binding(), Connection(), Tuple(), new Uri("https://event.example.test/callback")), CancellationToken.None);

        await Assert.That(provision.ProviderFormId).IsEqualTo("survey_new");
        await Assert.That(subscription.ProviderSubscriptionId).IsEqualTo("webhook_123");
        await Assert.That(handler.Requests[0].RequestUri!.AbsolutePath).IsEqualTo("/api/v1/management/surveys");
        await Assert.That(handler.Requests[1].RequestUri!.AbsolutePath).IsEqualTo("/api/v1/webhooks");
        await Assert.That(handler.RequestBodies[0]).Contains("\"headline\":{\"default\":\"Email\"}");
        await Assert.That(handler.RequestBodies[0]).Contains("q018e4e5c7f0070008000000000000601");
        await Assert.That(handler.RequestBodies[0]).Contains("\"hiddenFields\":{\"enabled\":true,\"fieldIds\":[\"islamuEventAttemptId\"]}");
        await Assert.That(handler.RequestBodies[0]).DoesNotContain("profile.email");
        using JsonDocument webhookPayload = JsonDocument.Parse(handler.RequestBodies[1]);
        JsonElement root = webhookPayload.RootElement;
        await Assert.That(root.GetProperty("source").GetString()).IsEqualTo("user");
        await Assert.That(root.GetProperty("workspaceId").GetString()).IsEqualTo("workspace_123");
        await Assert.That(root.GetProperty("triggers")[0].GetString()).IsEqualTo("responseFinished");
        await Assert.That(root.GetProperty("surveyIds")[0].GetString()).IsEqualTo("survey_123");
    }

    [Test]
    public async Task Reconcile_ExactlyLimitResponsesCompletesWhenMetadataDoesNotReportMore()
    {
        string body = "{\"data\":[" + string.Join(',', Enumerable.Range(0, 100).Select(i => $"{{\"id\":\"r{i}\",\"updatedAt\":\"2026-08-11T12:{i % 60:00}:00Z\"}}")) + "]}";
        RecordingHandler handler = new(_ => Json(body));
        FormbricksRegistrationProviderAdapter adapter = Adapter(handler, SecretResolver());

        RegistrationProviderReconciliationResult result = await adapter.ReconcileAsync(new(TenantId, Binding(), Connection(), Tuple(), UtcNow.AddHours(-1)), CancellationToken.None);

        await Assert.That(result.ObservedSubmissionCount).IsEqualTo(100);
        await Assert.That(result.HasMore).IsFalse();
        await Assert.That(result.Responses).Count().IsEqualTo(100);
        await Assert.That(result.Responses![0].ProviderSubmissionId).IsEqualTo("r0");
        await Assert.That(result.Responses[0].ProviderRevisionId).IsEqualTo("2026-08-11T12:00:00Z");
        await Assert.That(result.NextCheckpoint).IsEqualTo("2026-08-11T12:39:00.0000000Z");
        await Assert.That(handler.Requests[0].RequestUri!.Query).Contains("limit=101");
    }

    [Test]
    public async Task Reconcile_LimitPlusOneReportsHasMoreWithoutDroppingQueuedLimitIdentities()
    {
        string body = "{\"data\":[" + string.Join(',', Enumerable.Range(0, 101).Select(i => $"{{\"id\":\"r{i}\",\"updatedAt\":\"2026-08-11T12:{i % 60:00}:00Z\"}}")) + "]}";
        RecordingHandler handler = new(_ => Json(body));
        FormbricksRegistrationProviderAdapter adapter = Adapter(handler, SecretResolver());

        RegistrationProviderReconciliationResult result = await adapter.ReconcileAsync(new(TenantId, Binding(), Connection(), Tuple(), UtcNow.AddHours(-1)), CancellationToken.None);

        await Assert.That(result.HasMore).IsTrue();
        await Assert.That(result.Responses).Count().IsEqualTo(100);
        await Assert.That(result.Responses![99].ProviderSubmissionId).IsEqualTo("r99");
        await Assert.That(result.NextCheckpoint).IsNull();
    }

    [Test]
    public async Task Reconcile_ContinuationCursorAdvancesOffsetAcrossMoreThanOneHundredIdenticalTimestamps()
    {
        string page1 = "{\"data\":[" + string.Join(',', Enumerable.Range(0, 101).Select(i => $"{{\"id\":\"r{i}\",\"updatedAt\":\"2026-08-11T12:00:00Z\"}}")) + "]}";
        string page2 = "{\"data\":[{\"id\":\"r100\",\"updatedAt\":\"2026-08-11T12:00:00Z\"}]}";
        RecordingHandler handler = new(request => request.RequestUri!.Query.Contains("offset=100", StringComparison.Ordinal) ? Json(page2) : Json(page1));
        FormbricksRegistrationProviderAdapter adapter = Adapter(handler, SecretResolver());

        RegistrationProviderReconciliationResult first = await adapter.ReconcileAsync(new(TenantId, Binding(), Connection(), Tuple(), UtcNow.AddHours(-1)), CancellationToken.None);
        RegistrationProviderReconciliationResult second = await adapter.ReconcileAsync(new(TenantId, Binding(), Connection(), Tuple(), UtcNow.AddHours(-1), first.ContinuationCursor), CancellationToken.None);

        await Assert.That(first.HasMore).IsTrue();
        await Assert.That(first.Responses).Count().IsEqualTo(100);
        await Assert.That(second.HasMore).IsFalse();
        await Assert.That(second.Responses!.Single().ProviderSubmissionId).IsEqualTo("r100");
        await Assert.That(handler.Requests[1].RequestUri!.Query).Contains("offset=100");
    }

    [Test]
    public async Task Reconcile_ReturnsIdentifiersOnlyAndCheckpointWhenPageComplete()
    {
        RecordingHandler handler = new(_ => Json("""
            {"data":[
              {"id":"r1","updatedAt":"2026-08-11T12:01:00Z","answers":{"email":"a@example.test"}},
              {"id":"r2","updatedAt":"2026-08-11T12:02:00Z","answers":{"email":"b@example.test"}}
            ],"meta":{"hasMore":false}}
            """));
        FormbricksRegistrationProviderAdapter adapter = Adapter(handler, SecretResolver());

        RegistrationProviderReconciliationResult result = await adapter.ReconcileAsync(new(TenantId, Binding(), Connection(), Tuple(), UtcNow.AddHours(-1)), CancellationToken.None);

        await Assert.That(result.ObservedSubmissionCount).IsEqualTo(2);
        await Assert.That(result.HasMore).IsFalse();
        await Assert.That(result.Responses!.Select(response => response.ProviderSubmissionId)).IsEquivalentTo(["r1", "r2"]);
        await Assert.That(result.Responses![1].ProviderRevisionId).IsEqualTo("2026-08-11T12:02:00Z");
        await Assert.That(result.NextCheckpoint).IsEqualTo("2026-08-11T12:02:00.0000000Z");
    }

    [Test]
    public async Task WriteSubmission_MissingProviderIdIsAmbiguousAndNotClaimedIdempotent()
    {
        RecordingHandler handler = new(_ => Json("{\"data\":{}}"));
        FormbricksRegistrationProviderAdapter adapter = Adapter(handler, SecretResolver());

        RegistrationProviderSubmissionDeliveryException exception = await Assert.That(async () =>
                await adapter.WriteSubmissionAsync(new(TenantId, Binding(), Connection(), Tuple(), Guid.CreateVersion7(), new Dictionary<string, string>()), CancellationToken.None))
            .Throws<RegistrationProviderSubmissionDeliveryException>();
        await Assert.That(exception.FailureKind).IsEqualTo(RegistrationProviderSubmissionDeliveryFailureKind.AmbiguousAfterHandoff);
        await Assert.That(handler.RequestBodies.Single()).Contains("\"workspaceId\":\"workspace_123\"");
    }

    [Test]
    public async Task WriteSubmission_ResponseLossIsParkedAsAmbiguous()
    {
        FormbricksRegistrationProviderAdapter adapter = Adapter(
            new RecordingHandler(_ => throw new HttpRequestException("response lost")), SecretResolver());

        RegistrationProviderSubmissionDeliveryException exception = await Assert.That(async () =>
                await adapter.WriteSubmissionAsync(new(TenantId, Binding(), Connection(), Tuple(), Guid.CreateVersion7(),
                    new Dictionary<string, string> { ["email"] = "safe@example.test" }), CancellationToken.None))
            .Throws<RegistrationProviderSubmissionDeliveryException>();

        await Assert.That(exception.FailureKind).IsEqualTo(RegistrationProviderSubmissionDeliveryFailureKind.AmbiguousAfterHandoff);
        await Assert.That(exception.FailureCode).IsEqualTo("provider_write_outcome_unknown");
    }

    [Test]
    public async Task WriteSubmission_RateLimitIsRetryableBeforeHandoff()
    {
        FormbricksRegistrationProviderAdapter adapter = Adapter(
            new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            }), SecretResolver());

        RegistrationProviderSubmissionDeliveryException exception = await Assert.That(async () =>
                await adapter.WriteSubmissionAsync(new(TenantId, Binding(), Connection(), Tuple(), Guid.CreateVersion7(),
                    new Dictionary<string, string>()), CancellationToken.None))
            .Throws<RegistrationProviderSubmissionDeliveryException>();

        await Assert.That(exception.FailureKind).IsEqualTo(RegistrationProviderSubmissionDeliveryFailureKind.RetryableBeforeHandoff);
    }

    private static FormbricksRegistrationProviderAdapter Adapter(RecordingHandler handler, FakeSecretResolver resolver) =>
        new(new HttpClient(handler), resolver, new FixedTimeProvider(UtcNow));

    private static FakeSecretResolver SecretResolver(Dictionary<Guid, string>? secrets = null) =>
        new(secrets ?? new Dictionary<Guid, string> { [ApiTokenBindingId] = "api-token", [BindingWebhookBindingId] = "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("callback-secret-32-bytes-value")) });

    private static RegistrationProviderConnection Connection() => RegistrationProviderConnection.Create(
        Guid.Parse("018e4e5c-7f00-7000-8000-000000000201"),
        TenantId,
        "Formbricks",
        RegistrationProviderKindEnum.ExternalForm,
        RegistrationProviderDeploymentKindEnum.HostedSaas,
        "FORMBRICKS",
        "CLOUD",
        "v1",
        "ISLAMU_EVENT_FORMBRICKS_V1",
        "2026-08-10",
        "https://api.formbricks.example.test/api/v1",
        "https://forms.example.test",
        "workspace_123",
        ApiTokenBindingId,
        ConnectionWebhookBindingId,
        UtcNow);

    private static RegistrationProviderBinding Binding()
    {
        RegistrationProviderBinding binding = RegistrationProviderBinding.Create(
            TenantId,
            Connection().Id,
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000301"),
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000302"),
            RegistrationProviderPresentationModeEnum.Embed,
            RegistrationProviderCollectionModeEnum.ProviderHosted,
            RegistrationProviderCompletionModeEnum.Callback,
            RegistrationProviderTrustLevelEnum.FullCanonical,
            BindingWebhookBindingId,
            UtcNow);
        binding.SetDraftProvisionedSurvey("survey_123", "rev_123");
        return binding;
    }

    private static RegistrationFormVersion FormVersion(RegistrationFieldTypeEnum fieldType = RegistrationFieldTypeEnum.Email)
    {
        RegistrationForm form = RegistrationForm.Create(Guid.Parse("018e4e5c-7f00-7000-8000-000000000301"), TenantId, Guid.Parse("018e4e5c-7f00-7000-8000-000000000401"), "profile", "main", "Main", UtcNow);
        RegistrationFormVersion version = RegistrationFormVersion.Create(Guid.Parse("018e4e5c-7f00-7000-8000-000000000302"), form, 1, "en", null, null, UtcNow);
        RegistrationFormSection section = RegistrationFormSection.Create(Guid.Parse("018e4e5c-7f00-7000-8000-000000000501"), version, 1, "Profile", UtcNow);
        version.AddSection(section);
        RegistrationFormField field = RegistrationFormField.Create(Guid.Parse("018e4e5c-7f00-7000-8000-000000000601"), section, 1, "profile", "email", "Email", fieldType, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, true, UtcNow);
        version.AddField(section, field);
        version.UpdateFieldValidation(field, true, false, null, null, null, null, null, null, null, null);
        return version;
    }

    private static RegistrationProviderTuple Tuple() => new("FORMBRICKS", "CLOUD", "v1", "ISLAMU_EVENT_FORMBRICKS_V1", "2026-08-10");

    private static Dictionary<string, string> SignedHeaders(byte[] body, string secret, DateTime signedAt, bool extraInvalidSignature = false)
    {
        long timestamp = new DateTimeOffset(signedAt).ToUnixTimeSeconds();
        byte[] key = Convert.FromBase64String(secret["whsec_".Length..]);
        byte[] signed = Encoding.UTF8.GetBytes("msg_123." + timestamp + ".").Concat(body).ToArray();
        string signature = Convert.ToBase64String(HMACSHA256.HashData(key, signed));
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["webhook-id"] = "msg_123",
            ["webhook-timestamp"] = timestamp.ToString(),
            ["webhook-signature"] = extraInvalidSignature ? "v1,AAAA v1," + signature : "v1," + signature
        };
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Fixture(string name) => File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Registration/Formbricks/Fixtures", name)));

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return responder(request);
        }
    }

    private sealed class FakeSecretResolver(Dictionary<Guid, string> secrets) : ISecretResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(string settingKey, Guid? tenantId, CancellationToken cancellationToken = default) => Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope, Guid? scopeId, string qualifier, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretResolutionResult.Unconfigured);

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken = default) =>
            Task.FromResult(secrets.TryGetValue(bindingId, out string? value) ? SecretResolutionResult.Resolved(new ResolvedSecret("test", value, SecretSourceType.EnvironmentVariable, SecretScope.Tenant, tenantId, UtcNow)) : SecretResolutionResult.Unconfigured);

        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
