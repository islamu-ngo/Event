using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Registration;
using Explore.Infrastructure.Services.Registration.Providers.GoogleForms;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Explore.Infrastructure.Tests.Registration.GoogleForms;

public sealed class GoogleFormsRegistrationProviderAdapterTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid OAuthTokenBindingId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000101");

    [Test]
    public async Task Registry_GoogleFormsTupleResolvesOnlyExactPinnedEvidenceAndHonestCapabilities()
    {
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => Json("{}")));
        RegistrationProviderRegistry registry = new([descriptor]);

        await Assert.That(registry.TryResolve(new("GOOGLE_FORMS", "GOOGLE_WORKSPACE", "v1", "ISLAMU_EVENT_GOOGLE_FORMS_PUBSUB_V1", "2026-08-11"))).IsSameReferenceAs(descriptor);
        await Assert.That(registry.TryResolve(new("GOOGLE_FORMS", "GOOGLE_WORKSPACE", "v2", "ISLAMU_EVENT_GOOGLE_FORMS_PUBSUB_V1", "2026-08-11"))).IsNull();
        await Assert.That(descriptor.ProvenCapabilities.Redirect).IsTrue();
        await Assert.That(descriptor.ProvenCapabilities.Embed).IsTrue();
        await Assert.That(descriptor.ProvenCapabilities.Manual).IsTrue();
        await Assert.That(descriptor.ProvenCapabilities.SchemaRead).IsTrue();
        await Assert.That(descriptor.ProvenCapabilities.FormProvision).IsTrue();
        await Assert.That(descriptor.ProvenCapabilities.SubmissionRead).IsTrue();
        await Assert.That(descriptor.ProvenCapabilities.CallbackVerification).IsTrue();
        await Assert.That(descriptor.ProvenCapabilities.SubscriptionManagement).IsTrue();
        await Assert.That(descriptor.ProvenCapabilities.Reconciliation).IsTrue();
        await Assert.That(descriptor.ProvenCapabilities.SubmissionWrite).IsFalse();
        await Assert.That(descriptor.ProvenCapabilities.SubmissionSink).IsFalse();
        await Assert.That(descriptor.ProvenCapabilities.AutoFinalize).IsFalse();
        await Assert.That(RegistrationProviderCapabilitySet.FromCodes(["FILE_UPLOAD", "HEADLESS_SUBMIT", "MULTILINGUAL_FORMS"])).IsEqualTo(RegistrationProviderCapabilitySet.None);
    }

    [Test]
    public async Task Presentation_BuildsRedirectAndEmbedUrlsFromPublishedFormId()
    {
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => Json("{}")));
        RegistrationProviderPresentationResult result = await descriptor.GetPresentationAsync(new(TenantId, Binding(), Connection(), descriptor.Tuple, Guid.Parse("018e4e5c-7f00-7000-8000-000000000701")), CancellationToken.None);

        await Assert.That(result.RedirectAvailable).IsTrue();
        await Assert.That(result.EmbedAvailable).IsTrue();
        await Assert.That(result.ManualAvailable).IsTrue();
        await Assert.That(result.RedirectUri!.ToString()).Contains("/forms/d/e/form_123/viewform");
        await Assert.That(result.RedirectUri.Query).DoesNotContain("islamuEventAttemptId");
        await Assert.That(result.EmbedUri!.ToString()).Contains("embedded=true");
    }

    [Test]
    public async Task Presentation_AppendsAttemptCorrelationOnlyForOneValidEntryMapping()
    {
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => Json("{}")));
        RegistrationProviderBinding valid = BindingWithCorrelation("entry.123456");
        Guid attemptId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000701");

        RegistrationProviderPresentationResult result = await descriptor.GetPresentationAsync(new(TenantId, valid, Connection(), descriptor.Tuple, attemptId, "raw-token"), CancellationToken.None);

        await Assert.That(descriptor).IsAssignableTo<IRegistrationProviderDelegatedAutomation>();
        IRegistrationProviderDelegatedAutomation delegated = descriptor;
        await Assert.That(delegated.RequiredCorrelationPlatformFieldKey).IsEqualTo("system.registration_attempt_token");
        await Assert.That(delegated.ConnectorContractVersion).IsEqualTo("GOOGLE_FORMS_ENTRY_CORRELATION_V1");
        await Assert.That(result.RedirectUri!.Query).Contains("entry.123456=");
        await Assert.That(Uri.UnescapeDataString(result.RedirectUri.Query)).Contains(attemptId + "|raw-token");
        await Assert.That(result.EmbedUri!.Query).Contains("embedded=true");
        await Assert.That(result.EmbedUri.Query).Contains("entry.123456=");
    }

    [Test]
    public async Task Presentation_RejectsNonGooglePublicOriginBeforeAppendingAttemptToken()
    {
        RegistrationProviderConnection connection = Connection();
        typeof(RegistrationProviderConnection).GetProperty(nameof(RegistrationProviderConnection.PublicBaseUrl))!.SetValue(connection, "https://evil.example.test");
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => Json("{}")));

        InvalidOperationException exception = await Assert.That(async () =>
                await descriptor.GetPresentationAsync(new(TenantId, BindingWithCorrelation("entry.123456"), connection, descriptor.Tuple,
                    Guid.Parse("018e4e5c-7f00-7000-8000-000000000701"), "raw-token"), CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception.Message).Contains("docs.google.com");
        await Assert.That(exception.ToString()).DoesNotContain("raw-token");
        await Assert.That(exception.ToString()).DoesNotContain("evil.example.test");
    }

    [Test]
    [Arguments(null)]
    [Arguments("profile.email")]
    [Arguments("entry.abc")]
    public async Task Presentation_DoesNotCorrelateWhenMappingMissingOrMalformed(string? providerFieldKey)
    {
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => Json("{}")));
        RegistrationProviderBinding binding = providerFieldKey is null ? Binding() : BindingWithCorrelation(providerFieldKey);

        RegistrationProviderPresentationResult result = await descriptor.GetPresentationAsync(new(TenantId, binding, Connection(), descriptor.Tuple, Guid.Parse("018e4e5c-7f00-7000-8000-000000000701"), "raw-token"), CancellationToken.None);

        await Assert.That(result.RedirectUri!.Query).DoesNotContain("entry.");
        await Assert.That(result.RedirectUri.Query).DoesNotContain("raw-token");
        await Assert.That(result.EmbedUri!.Query).DoesNotContain("raw-token");
    }

    [Test]
    public async Task Presentation_DoesNotCorrelateWhenDuplicateMappingsExist()
    {
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => Json("{}")));
        RegistrationProviderBinding binding = BindingWithCorrelation("entry.123456");
        AddFieldMappingUnsafe(binding, RegistrationProviderFieldMapping.Create(binding, "system.registration_attempt_token", "entry.789", true));

        RegistrationProviderPresentationResult result = await descriptor.GetPresentationAsync(new(TenantId, binding, Connection(), descriptor.Tuple, Guid.Parse("018e4e5c-7f00-7000-8000-000000000701"), "raw-token"), CancellationToken.None);

        await Assert.That(result.RedirectUri!.Query).DoesNotContain("entry.");
        await Assert.That(result.EmbedUri!.Query).DoesNotContain("raw-token");
    }

    [Test]
    public async Task Descriptor_ExposesPinnedOAuthScopesWithoutDriveOrMutableConnectionState()
    {
        await Assert.That(GoogleFormsRegistrationProviderDescriptor.ImportScopes).IsEquivalentTo([
            "openid",
            "email",
            "https://www.googleapis.com/auth/forms.body.readonly",
            "https://www.googleapis.com/auth/forms.responses.readonly"
        ]);
        await Assert.That(GoogleFormsRegistrationProviderDescriptor.ManagedProvisionScopes).Contains("https://www.googleapis.com/auth/forms.body");
        await Assert.That(GoogleFormsRegistrationProviderDescriptor.ManagedProvisionScopes).DoesNotContain("https://www.googleapis.com/auth/drive");
    }

    [Test]
    public async Task SchemaRead_MapsFileUploadAsUnsupportedExternalField()
    {
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => Json("""
            {"formId":"form_123","info":{"title":"Registration"},"items":[
              {"itemId":"email","title":"Email","questionItem":{"question":{"required":true,"textQuestion":{}}}},
              {"itemId":"upload","title":"Upload","questionItem":{"question":{"fileUploadQuestion":{}}}}
            ]}
            """)));

        RegistrationProviderSchemaReadResult result = await descriptor.ReadSchemaAsync(new(TenantId, Binding(), Connection(), descriptor.Tuple), CancellationToken.None);

        await Assert.That(result.Snapshot.Fields.Count).IsEqualTo(2);
        await Assert.That(result.Snapshot.Fields[0].Type).IsEqualTo(nameof(RegistrationFieldTypeEnum.ShortText));
        await Assert.That(result.Snapshot.Fields[1].Type).IsEqualTo(nameof(RegistrationFieldTypeEnum.File));
        await Assert.That(result.Fingerprint).StartsWith("sha256:");
    }

    [Test]
    public async Task SchemaRead_PreservesLongTextAndMarksUnsupportedQuestionShapesAsOpaqueExternal()
    {
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => Json("""
            {"formId":"form_123","items":[
              {"itemId":"bio","title":"Bio","questionItem":{"question":{"textQuestion":{"paragraph":true}}}},
              {"itemId":"scale","title":"Scale","questionItem":{"question":{"scaleQuestion":{"low":1,"high":5}}}}
            ]}
            """)));

        RegistrationProviderSchemaReadResult result = await descriptor.ReadSchemaAsync(new(TenantId, Binding(), Connection(), descriptor.Tuple), CancellationToken.None);

        await Assert.That(result.Snapshot.Fields[0].Type).IsEqualTo(nameof(RegistrationFieldTypeEnum.LongText));
        await Assert.That(result.Snapshot.Fields[1].Type).IsEqualTo(nameof(RegistrationFieldTypeEnum.OpaqueExternal));
    }

    [Test]
    public async Task SchemaRead_ChoiceOptionKeysMatchProvisionedGooglePreservedLabels()
    {
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => Json("""
            {"formId":"form_123","items":[{"itemId":"q018e4e5c7f0070008000000000000601","title":"Email","questionItem":{"question":{"required":true,"choiceQuestion":{"type":"RADIO","options":[{"value":"Yes"},{"value":"No"}]}}}}]}
            """)));
        RegistrationProviderSchemaReadResult remote = await descriptor.ReadSchemaAsync(new(TenantId, Binding(), Connection(), descriptor.Tuple), CancellationToken.None);
        RegistrationProviderFormCompatibilityResult local = descriptor.CheckCompatibility(FormVersion(RegistrationFieldTypeEnum.SingleChoice, ["Yes", "No"]));

        await Assert.That(remote.Snapshot.Fields.Single().Options.Select(option => option.Key)).IsEquivalentTo(["Yes", "No"]);
        await Assert.That(remote.Fingerprint).IsEqualTo(local.Fingerprint);
    }

    [Test]
    public async Task Provision_PerformsCreateBatchUpdatePublishGetAndFailsIfUnpublished()
    {
        Queue<HttpResponseMessage> responses = new([
            Json("{\"formId\":\"form_new\",\"revisionId\":\"rev_create\"}"),
            Json("{\"writeControl\":{\"targetRevisionId\":\"rev_batch\"}}"),
            Json("{}"),
            Json("{\"formId\":\"form_new\",\"revisionId\":\"rev_get\",\"responderUri\":\"https://docs.google.com/forms/d/e/form_new/viewform\",\"settings\":{\"publishSettings\":{\"publishState\":{\"isPublished\":true,\"isAcceptingResponses\":true}}}}")
        ]);
        RecordingHandler handler = new(_ => responses.Dequeue());
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(handler);

        RegistrationProviderFormProvisionResult result = await descriptor.ProvisionFormAsync(new(TenantId, Binding(), Connection(), descriptor.Tuple, FormVersion()), CancellationToken.None);

        await Assert.That(result.ProviderFormId).IsEqualTo("form_new");
        await Assert.That(result.ProviderRevisionId).IsEqualTo("rev_get");
        await Assert.That(handler.Requests.Select(request => request.Method + " " + request.RequestUri!.PathAndQuery)).IsEquivalentTo([
            "POST /v1/forms",
            "POST /v1/forms/form_new:batchUpdate",
            "POST /v1/forms/form_new:setPublishSettings",
            "GET /v1/forms/form_new"
        ]);
        await Assert.That(handler.RequestBodies[0]).Contains("\"title\"");
        await Assert.That(handler.RequestBodies[0]).DoesNotContain("requests");
        await Assert.That(handler.RequestBodies[1]).Contains("createItem");
        await Assert.That(handler.RequestBodies[2]).Contains("isPublished");

        GoogleFormsRegistrationProviderDescriptor unpublished = Descriptor(new RecordingHandler(request => request.Method == HttpMethod.Get
            ? Json("{\"formId\":\"form_new\",\"settings\":{\"publishSettings\":{\"publishState\":{\"isPublished\":true,\"isAcceptingResponses\":false}}}}")
            : Json("{\"formId\":\"form_new\",\"revisionId\":\"rev\"}")));
        InvalidOperationException exception = await Assert.That(async () =>
                await unpublished.ProvisionFormAsync(new(TenantId, Binding(), Connection(), unpublished.Tuple, FormVersion()), CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(exception.Message).Contains("not published and accepting responses");
    }

    [Test]
    public async Task ManagedPreflight_FingerprintKeepsLocalTypeParityForChoiceDateAndTime()
    {
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => Json("{}")));

        await Assert.That(descriptor.CheckCompatibility(FormVersion(RegistrationFieldTypeEnum.SingleChoice)).Fingerprint)
            .IsNotEqualTo(descriptor.CheckCompatibility(FormVersion(RegistrationFieldTypeEnum.Date)).Fingerprint);
        await Assert.That(descriptor.CheckCompatibility(FormVersion(RegistrationFieldTypeEnum.Time)).Fingerprint)
            .IsNotEqualTo(descriptor.CheckCompatibility(FormVersion(RegistrationFieldTypeEnum.Email)).Fingerprint);
    }

    [Test]
    public async Task Provision_RejectsFileUploadFieldsBeforeCallingGoogle()
    {
        RecordingHandler handler = new(_ => Json("{}"));
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(handler);

        RegistrationProviderFormCompatibilityResult compatibility = descriptor.CheckCompatibility(FormVersion(RegistrationFieldTypeEnum.File));
        InvalidOperationException exception = await Assert.That(async () =>
                await descriptor.ProvisionFormAsync(new(TenantId, Binding(), Connection(), descriptor.Tuple, FormVersion(RegistrationFieldTypeEnum.File)), CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(compatibility.IsCompatible).IsFalse();
        await Assert.That(compatibility.Issues.Single().Code).IsEqualTo("google_forms_file_upload_unsupported");
        await Assert.That(exception.Message).Contains("file upload");
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task Provision_RejectsDuplicateChoiceLabelsBecauseGoogleDoesNotPreserveOptionIds()
    {
        RecordingHandler handler = new(_ => Json("{}"));
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(handler);

        RegistrationProviderFormCompatibilityResult compatibility = descriptor.CheckCompatibility(FormVersion(RegistrationFieldTypeEnum.SingleChoice, ["Same", "Same"]));
        InvalidOperationException exception = await Assert.That(async () =>
                await descriptor.ProvisionFormAsync(new(TenantId, Binding(), Connection(), descriptor.Tuple, FormVersion(RegistrationFieldTypeEnum.SingleChoice, ["Same", "Same"])), CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(compatibility.Issues.Single().Code).IsEqualTo("google_forms_duplicate_option_labels_unsupported");
        await Assert.That(exception.Message).Contains("unsupported fields");
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task SubmissionRead_MapsAnswersAndRejectsMalformedJson()
    {
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => Json("""
            {"responseId":"resp_123","lastSubmittedTime":"2026-08-11T12:01:00Z","answers":{"email":{"textAnswers":{"answers":[{"value":"safe@example.test"}]}},"attempt":{"textAnswers":{"answers":[{"value":"018e4e5c-7f00-7000-8000-000000000701"}]}}}}
            """)));

        RegistrationProviderSubmissionReadResult result = await descriptor.ReadSubmissionAsync(new(TenantId, Binding(), Connection(), descriptor.Tuple, "resp_123"), CancellationToken.None);

        await Assert.That(result.ProviderSubmissionId).IsEqualTo("resp_123");
        await Assert.That(result.AttemptId).IsNull();
        await Assert.That(result.Answers.Keys).Contains("email");

        GoogleFormsRegistrationProviderDescriptor malformed = Descriptor(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json")
        }));
        await Assert.That(async () => await malformed.ReadSubmissionAsync(new(TenantId, Binding(), Connection(), malformed.Tuple, "resp_123"), CancellationToken.None)).Throws<JsonException>();
    }

    [Test]
    public async Task SubmissionRead_ParsesExactMappedEntryCorrelationAndIgnoresArbitraryGuids()
    {
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => Json("""
            {"responseId":"resp_123","lastSubmittedTime":"2026-08-11T12:01:00Z","answers":{
              "111111":{"textAnswers":{"answers":[{"value":"018e4e5c-7f00-7000-8000-000000000999|wrong"}]}},
              "123456":{"textAnswers":{"answers":[{"value":"018e4e5c-7f00-7000-8000-000000000701|raw-token"}]}},
              "email":{"textAnswers":{"answers":[{"value":"safe@example.test"}]}}
            }}
            """)));

        RegistrationProviderSubmissionReadResult result = await descriptor.ReadSubmissionAsync(new(TenantId, BindingWithCorrelation("entry.123456"), Connection(), descriptor.Tuple, "resp_123"), CancellationToken.None);

        await Assert.That(result.AttemptId).IsEqualTo(Guid.Parse("018e4e5c-7f00-7000-8000-000000000701"));
        await Assert.That(result.AttemptCapabilityToken).IsEqualTo("raw-token");
        await Assert.That(result.Answers.Keys).Contains("email");
        await Assert.That(result.Answers.Keys).DoesNotContain("entry.123456");
    }

    [Test]
    [Arguments("018e4e5c-7f00-7000-8000-000000000701")]
    [Arguments("018e4e5c-7f00-7000-8000-000000000701|raw-token|extra")]
    [Arguments("not-a-guid|raw-token")]
    public async Task SubmissionRead_RejectsMalformedMappedCorrelation(string value)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            responseId = "resp_123",
            answers = new Dictionary<string, object>
            {
                ["123456"] = new { textAnswers = new { answers = new[] { new { value } } } }
            }
        });
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => Json(Encoding.UTF8.GetString(payload))));

        await Assert.That(async () => await descriptor.ReadSubmissionAsync(new(TenantId, BindingWithCorrelation("entry.123456"), Connection(), descriptor.Tuple, "resp_123"), CancellationToken.None))
            .Throws<FormatException>();
    }

    [Test]
    public async Task SubmissionRead_FileUploadAnswersThrowTypedUnsupportedOutcomeWithoutDriveMetadata()
    {
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => Json("""
            {"responseId":"resp_123","answers":{"upload":{"fileUploadAnswers":{"answers":[{"fileId":"drive-id","fileName":"secret.pdf","mimeType":"application/pdf"}]}}}}
            """)));

        RegistrationProviderUnsupportedSubmissionException exception = await Assert.That(async () =>
                await descriptor.ReadSubmissionAsync(new(TenantId, Binding(), Connection(), descriptor.Tuple, "resp_123"), CancellationToken.None))
            .Throws<RegistrationProviderUnsupportedSubmissionException>();

        await Assert.That(exception.FailureCode).IsEqualTo("PROVIDER_FILE_UPLOAD_UNSUPPORTED");
        await Assert.That(exception.ToString()).DoesNotContain("drive-id");
        await Assert.That(exception.ToString()).DoesNotContain("secret.pdf");
        await Assert.That(exception.ToString()).DoesNotContain("application/pdf");
    }

    [Test]
    public async Task Reconcile_ListsResponsesWithTimestampFilterAndReportsPagination()
    {
        RecordingHandler handler = new(request => request.RequestUri!.PathAndQuery.Contains("pageToken=next", StringComparison.Ordinal)
            ? Json("{\"responses\":[{\"responseId\":\"r2\",\"lastSubmittedTime\":\"2026-08-11T12:02:00Z\"}]}")
            : Json("{\"responses\":[{\"responseId\":\"r1\",\"lastSubmittedTime\":\"2026-08-11T12:01:00Z\"}],\"nextPageToken\":\"next\"}"));
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(handler);

        RegistrationProviderReconciliationResult result = await descriptor.ReconcileAsync(new(TenantId, Binding(), Connection(), descriptor.Tuple, UtcNow.AddHours(-1)), CancellationToken.None);

        await Assert.That(result.ObservedSubmissionCount).IsEqualTo(2);
        await Assert.That(result.HasMore).IsFalse();
        await Assert.That(result.Responses!.Select(response => response.ProviderSubmissionId)).IsEquivalentTo(["r1", "r2"]);
        await Assert.That(result.NextCheckpoint).IsEqualTo("2026-08-11T12:02:00.0000000Z");
        await Assert.That(handler.Requests[0].RequestUri!.PathAndQuery).Contains("/responses?filter=timestamp%20%3E%3D%202026");
        await Assert.That(handler.Requests[1].RequestUri!.PathAndQuery).Contains("pageToken=next");
    }

    [Test]
    public async Task SubscriptionRenewal_UsesWatchRenewEndpointWhenBindingAlreadyHasWatch()
    {
        RecordingHandler handler = new(_ => Json("{\"watch\":{\"id\":\"watch_123\",\"expireTime\":\"2026-08-18T12:00:00Z\"}}"));
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(handler);
        RegistrationProviderBinding binding = Binding();
        binding.SetDraftProvisionedSubscription("watch_123");

        await descriptor.EnsureSubscriptionAsync(
            new(TenantId, binding, Connection(), descriptor.Tuple, new Uri("https://event.example.test/api/registration-provider-callback")), CancellationToken.None);

        await Assert.That(handler.Requests.Single().RequestUri!.PathAndQuery)
            .IsEqualTo("/v1/forms/form_123/watches/watch_123:renew");
    }

    [Test]
    public async Task ManagementCalls_PinGoogleFormsOriginBeforeResolvingOAuthToken()
    {
        RegistrationProviderConnection connection = Connection();
        typeof(RegistrationProviderConnection).GetProperty(nameof(RegistrationProviderConnection.ManagementApiBaseUrl))!.SetValue(connection, "https://evil.example.test/v1");
        GoogleFormsRegistrationProviderDescriptor descriptor = new(new GoogleFormsRegistrationProviderAdapter(
            new HttpClient(new RecordingHandler(_ => Json("{}"))),
            new ThrowingSecretResolver(),
            new RecordingConnectionCheckpoint(),
            new AcceptingOidcValidator()));

        InvalidOperationException exception = await Assert.That(async () =>
                await descriptor.ReadSchemaAsync(new(TenantId, Binding(), connection, descriptor.Tuple), CancellationToken.None))
            .Throws<InvalidOperationException>();

        await Assert.That(exception.Message).Contains("forms.googleapis.com");
    }

    [Test]
    public async Task ProviderHttpFailures_ClassifyUnauthorizedRateLimitAndServerFailures()
    {
        foreach ((HttpStatusCode status, string code) in new[]
                 {
                     (HttpStatusCode.Unauthorized, "google_forms_http_401"),
                     (HttpStatusCode.TooManyRequests, "google_forms_http_429"),
                     (HttpStatusCode.InternalServerError, "google_forms_http_500")
                 })
        {
            GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(new RecordingHandler(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            }));

            HttpRequestException exception = await Assert.That(async () =>
                    await descriptor.ReadSchemaAsync(new(TenantId, Binding(), Connection(), descriptor.Tuple), CancellationToken.None))
                .Throws<HttpRequestException>();
            await Assert.That(exception.Message).Contains(code);
        }
    }

    [Test]
    public async Task OAuthRefreshTokenBundle_UsesGenericSecretBindingWithoutNewSecretDefinition()
    {
        var checkpoints = new RecordingConnectionCheckpoint();
        RecordingHandler handler = new(request => request.RequestUri!.Host == "oauth2.googleapis.com"
            ? Json("{\"access_token\":\"exchanged-token\"}")
            : Json("{\"formId\":\"form_123\",\"items\":[]}"));
        GoogleFormsRegistrationProviderDescriptor descriptor = new(new GoogleFormsRegistrationProviderAdapter(
            new HttpClient(handler),
            new FakeSecretResolver(new Dictionary<Guid, string>
            {
                [OAuthTokenBindingId] = "{\"refresh_token\":\"refresh\",\"client_id\":\"client\",\"client_secret\":\"secret\"}"
            }),
            checkpoints,
            new AcceptingOidcValidator()));

        await descriptor.ReadSchemaAsync(new(TenantId, Binding(), Connection(), descriptor.Tuple), CancellationToken.None);

        await Assert.That(handler.Requests[0].RequestUri!.Host).IsEqualTo("oauth2.googleapis.com");
        await Assert.That(handler.Requests[1].Headers.Authorization!.Parameter).IsEqualTo("exchanged-token");
        await Assert.That(checkpoints.CredentialRefreshes).IsEqualTo(1);
        await Assert.That(checkpoints.AccessValidations).IsEqualTo(1);
    }

    [Test]
    public async Task OAuthRefreshTokenBundle_DoesNotRecordRefreshWhenTokenExchangeFails()
    {
        var checkpoints = new RecordingConnectionCheckpoint();
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        GoogleFormsRegistrationProviderDescriptor descriptor = new(new GoogleFormsRegistrationProviderAdapter(
            new HttpClient(handler),
            new FakeSecretResolver(new Dictionary<Guid, string>
            {
                [OAuthTokenBindingId] = "{\"refresh_token\":\"refresh\",\"client_id\":\"client\",\"client_secret\":\"secret\"}"
            }),
            checkpoints,
            new AcceptingOidcValidator()));

        await Assert.That(async () => await descriptor.ReadSchemaAsync(new(TenantId, Binding(), Connection(), descriptor.Tuple), CancellationToken.None))
            .Throws<HttpRequestException>();

        await Assert.That(checkpoints.CredentialRefreshes).IsEqualTo(0);
        await Assert.That(checkpoints.AccessValidations).IsEqualTo(0);
    }

    [Test]
    public async Task OAuthRefreshTokenBundle_IgnoresTenantControlledTokenUriAndDoesNotLeakSecretInErrors()
    {
        RecordingHandler handler = new(request => request.RequestUri!.Host == "oauth2.googleapis.com"
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{}", Encoding.UTF8, "application/json") }
            : Json("{}"));
        GoogleFormsRegistrationProviderDescriptor descriptor = new(new GoogleFormsRegistrationProviderAdapter(
            new HttpClient(handler),
            new FakeSecretResolver(new Dictionary<Guid, string>
            {
                [OAuthTokenBindingId] = "{\"refresh_token\":\"refresh-secret\",\"client_id\":\"client\",\"client_secret\":\"client-secret\",\"token_uri\":\"https://evil.example.test/token\"}"
            }),
            new RecordingConnectionCheckpoint(),
            new AcceptingOidcValidator()));

        HttpRequestException exception = await Assert.That(async () =>
                await descriptor.ReadSchemaAsync(new(TenantId, Binding(), Connection(), descriptor.Tuple), CancellationToken.None))
            .Throws<HttpRequestException>();

        await Assert.That(handler.Requests.Single().RequestUri!.ToString()).IsEqualTo("https://oauth2.googleapis.com/token");
        await Assert.That(exception.ToString()).DoesNotContain("refresh-secret");
        await Assert.That(exception.ToString()).DoesNotContain("client-secret");
        await Assert.That(exception.ToString()).DoesNotContain("evil.example.test");
    }

    [Test]
    public async Task SubscriptionAndCallbackVerification_CreateWatchAndAcceptAuthenticatedNotifyOnlyPush()
    {
        RecordingHandler handler = new(_ => Json("{\"watch\":{\"id\":\"watch_123\",\"expireTime\":\"2026-08-18T12:00:00Z\"}}"));
        GoogleFormsRegistrationProviderDescriptor descriptor = Descriptor(handler);

        RegistrationProviderSubscriptionResult subscription = await descriptor.EnsureSubscriptionAsync(
            new(TenantId, Binding(), Connection(), descriptor.Tuple, new Uri("https://event.example.test/api/registration-provider-callback")), CancellationToken.None);
        RegistrationProviderCallbackVerificationResult callback = await descriptor.VerifyCallbackAsync(
            new(TenantId, Binding(), Connection(), descriptor.Tuple, PubSubBody("form_123", "watch_123"), new Dictionary<string, string> { ["Authorization"] = "Bearer test" }), CancellationToken.None);
        RegistrationProviderCallbackVerificationResult realShape = await descriptor.VerifyCallbackAsync(
            new(TenantId, Binding(), Connection(), descriptor.Tuple, PubSubBodyWithoutResponse("form_123"), new Dictionary<string, string> { ["Authorization"] = "Bearer test" }), CancellationToken.None);

        await Assert.That(subscription.IsActive).IsTrue();
        await Assert.That(subscription.ProviderSubscriptionId).IsEqualTo("watch_123");
        await Assert.That(subscription.ExpiresAtUtc).IsEqualTo(new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc));
        await Assert.That(callback.IsVerified).IsTrue();
        await Assert.That(callback.EffectKind).IsEqualTo("registration.provider_response_sweep");
        await Assert.That(realShape.IsVerified).IsTrue();
        await Assert.That(realShape.ProviderSubmissionId).IsEqualTo("pubsub-message-1");
        await Assert.That(handler.RequestBodies.Single()).Contains("projects/forms-project/topics/registration-watch");
    }

    [Test]
    [Arguments("issuer")]
    [Arguments("audience")]
    [Arguments("email")]
    [Arguments("email_verified")]
    [Arguments("expiry")]
    [Arguments("signing_key")]
    public async Task PubSubOidcValidator_VerifiesSignedGoogleTokenClaimsAndSigningKey(string invalidPart)
    {
        using RSA validRsa = RSA.Create(2048);
        using RSA otherRsa = RSA.Create(2048);
        var validKey = new RsaSecurityKey(validRsa) { KeyId = "valid-key" };
        var otherKey = new RsaSecurityKey(otherRsa) { KeyId = "other-key" };
        GooglePubSubConfigurationReference reference = GooglePubSubConfigurationReference.Parse(Connection().PubSubConfigurationReference);
        GooglePubSubOidcTokenValidator validator = new(new StaticOpenIdConfigurationManager(validKey));
        string issuer = invalidPart == "issuer" ? "https://evil.example.test" : "https://accounts.google.com";
        string audience = invalidPart == "audience" ? "https://wrong.example.test/callback" : reference.Audience;
        string email = invalidPart == "email" ? "wrong@example.iam.gserviceaccount.com" : reference.ServiceAccountEmail;
        string emailVerified = invalidPart == "email_verified" ? "false" : "true";
        DateTime notBefore = invalidPart == "expiry" ? DateTime.UtcNow.AddMinutes(-20) : DateTime.UtcNow.AddMinutes(-5);
        DateTime expires = invalidPart == "expiry" ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(10);
        SecurityKey signingKey = invalidPart == "signing_key" ? otherKey : validKey;

        bool result = await validator.ValidateAsync(
            new Dictionary<string, string> { ["Authorization"] = "Bearer " + CreateGoogleOidcToken(signingKey, issuer, audience, email, emailVerified, notBefore, expires) },
            reference,
            CancellationToken.None);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task PubSubOidcValidator_AcceptsSignedGoogleTokenWithUnmappedEmailClaim()
    {
        using RSA rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "valid-key" };
        GooglePubSubConfigurationReference reference = GooglePubSubConfigurationReference.Parse(Connection().PubSubConfigurationReference);
        GooglePubSubOidcTokenValidator validator = new(new StaticOpenIdConfigurationManager(key));

        bool result = await validator.ValidateAsync(
            new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + CreateGoogleOidcToken(key, "https://accounts.google.com", reference.Audience, reference.ServiceAccountEmail, "true", DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(10))
            },
            reference,
            CancellationToken.None);

        await Assert.That(result).IsTrue();
    }

    private static GoogleFormsRegistrationProviderDescriptor Descriptor(RecordingHandler handler) =>
        new(new GoogleFormsRegistrationProviderAdapter(new HttpClient(handler), SecretResolver(), new RecordingConnectionCheckpoint(), new AcceptingOidcValidator()));

    private static FakeSecretResolver SecretResolver() => new(new Dictionary<Guid, string> { [OAuthTokenBindingId] = "access-token" });

    private static RegistrationProviderConnection Connection()
    {
        RegistrationProviderConnection connection = RegistrationProviderConnection.Create(
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000201"), TenantId, "Google Forms",
            RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.HostedSaas,
            "GOOGLE_FORMS", "GOOGLE_WORKSPACE", "v1", "ISLAMU_EVENT_GOOGLE_FORMS_PUBSUB_V1", "2026-08-11",
            "https://forms.googleapis.com/v1", "https://docs.google.com", "google-workspace", OAuthTokenBindingId, null, UtcNow);
        connection.UpdateOAuthMetadata(
            string.Join(' ', GoogleFormsRegistrationProviderDescriptor.ManagedProvisionScopes),
            "forms-owner@example.test",
            "topic=projects/forms-project/topics/registration-watch;audience=https://event.example.test/api/registration-provider-callback;serviceAccountEmail=pubsub@example.iam.gserviceaccount.com");
        return connection;
    }

    private static RegistrationProviderBinding Binding()
    {
        RegistrationProviderBinding binding = RegistrationProviderBinding.Create(
            TenantId, Connection().Id, Guid.Parse("018e4e5c-7f00-7000-8000-000000000301"), Guid.Parse("018e4e5c-7f00-7000-8000-000000000302"),
            RegistrationProviderPresentationModeEnum.Embed, RegistrationProviderCollectionModeEnum.ProviderHosted,
            RegistrationProviderCompletionModeEnum.Callback, RegistrationProviderTrustLevelEnum.CompletionOnly, null, UtcNow);
        binding.SetDraftProvisionedSurvey("form_123", "rev_123");
        return binding;
    }

    private static RegistrationProviderBinding BindingWithCorrelation(string providerFieldKey)
    {
        RegistrationProviderBinding binding = Binding();
        binding.AddFieldMapping(RegistrationProviderFieldMapping.Create(binding, "system.registration_attempt_token", providerFieldKey, true));
        return binding;
    }

    private static void AddFieldMappingUnsafe(RegistrationProviderBinding binding, RegistrationProviderFieldMapping mapping)
    {
        FieldInfo field = typeof(RegistrationProviderBinding).GetField("_fieldMappings", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((List<RegistrationProviderFieldMapping>)field.GetValue(binding)!).Add(mapping);
    }

    private static RegistrationFormVersion FormVersion(RegistrationFieldTypeEnum fieldType = RegistrationFieldTypeEnum.Email, string[]? optionLabels = null)
    {
        RegistrationForm form = RegistrationForm.Create(Guid.Parse("018e4e5c-7f00-7000-8000-000000000301"), TenantId, Guid.Parse("018e4e5c-7f00-7000-8000-000000000401"), "profile", "main", "Main", UtcNow);
        RegistrationFormVersion version = RegistrationFormVersion.Create(Guid.Parse("018e4e5c-7f00-7000-8000-000000000302"), form, 1, "en", null, null, UtcNow);
        RegistrationFormSection section = RegistrationFormSection.Create(Guid.Parse("018e4e5c-7f00-7000-8000-000000000501"), version, 1, "Profile", UtcNow);
        version.AddSection(section);
        RegistrationFormField field = RegistrationFormField.Create(Guid.Parse("018e4e5c-7f00-7000-8000-000000000601"), section, 1, "profile", "email", "Email", fieldType, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, true, UtcNow);
        version.AddField(section, field);
        int ordinal = 1;
        foreach (string label in optionLabels ?? [])
        {
            version.AddOption(field, RegistrationFormFieldOption.Create(Guid.CreateVersion7(), field, ordinal, "option-" + ordinal, label, UtcNow));
            ordinal++;
        }
        version.UpdateFieldValidation(field, true, false, null, null, null, null, null, null, null, null);
        return version;
    }

    private static byte[] PubSubBody(string formId, string watchId)
    {
        string data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { formId, watchId, eventType = "RESPONSES" }));
        return JsonSerializer.SerializeToUtf8Bytes(new { message = new { messageId = "pubsub-message-1", data } });
    }

    private static byte[] PubSubBodyWithoutResponse(string formId)
    {
        string data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { formId, eventType = "RESPONSES" }));
        return JsonSerializer.SerializeToUtf8Bytes(new { message = new { messageId = "pubsub-message-1", data, attributes = new { watchId = "watch_123" } } });
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string CreateGoogleOidcToken(SecurityKey key, string issuer, string audience, string email, string emailVerified, DateTime notBefore, DateTime expires)
    {
        JwtSecurityToken token = new(
            issuer,
            audience,
            [new Claim("email", email), new Claim("email_verified", emailVerified)],
            notBefore,
            expires,
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        token.Header[JwtHeaderParameterNames.Kid] = key.KeyId;
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

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

    private sealed class ThrowingSecretResolver : ISecretResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(string settingKey, Guid? tenantId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("secret resolver should not be reached");

        public Task<SecretResolutionResult> ResolveQualifiedAsync(string settingKey, SecretScope scope, Guid? scopeId, string qualifier, CancellationToken cancellationToken = default) => throw new InvalidOperationException("secret resolver should not be reached");

        public Task<SecretResolutionResult> ResolveTenantBindingAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("secret resolver should not be reached");

        public Task InvalidateAsync(string settingKey, SecretScope scope, Guid? scopeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingConnectionCheckpoint : IRegistrationProviderConnectionCheckpoint
    {
        public int CredentialRefreshes { get; private set; }
        public int AccessValidations { get; private set; }

        public Task RecordCredentialRefreshAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken)
        {
            CredentialRefreshes++;
            return Task.CompletedTask;
        }

        public Task RecordAccessValidatedAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken)
        {
            AccessValidations++;
            return Task.CompletedTask;
        }
    }

    private sealed class AcceptingOidcValidator : IGooglePubSubOidcTokenValidator
    {
        public Task<bool> ValidateAsync(IReadOnlyDictionary<string, string> headers, GooglePubSubConfigurationReference reference, CancellationToken cancellationToken) =>
            Task.FromResult(headers.ContainsKey("Authorization") && reference.TopicName.StartsWith("projects/", StringComparison.Ordinal));
    }

    private sealed class StaticOpenIdConfigurationManager(SecurityKey signingKey) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            OpenIdConnectConfiguration configuration = new();
            configuration.SigningKeys.Add(signingKey);
            return Task.FromResult(configuration);
        }

        public void RequestRefresh()
        {
        }
    }
}
