using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Services.Registration.Providers.SubmissionSinks;
using Explore.Infrastructure.Webhooks;
using Explore.Infrastructure.Tests.Infrastructure;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Registration.SubmissionSinks;

public sealed class RegistrationProviderSubmissionSinkAdapterTests
{
    [Test]
    public async Task CsvSinkStoresStableSubmissionFileAndMetadata()
    {
        await using var environment = await StorageTestEnvironment.CreateAsync();
        var request = ManagedStorageBindingTests.CsvRequest(environment.Database.TenantId);
        var sink = new CsvRegistrationProviderSubmissionSink(environment.Producer, environment.Objects, environment.Unit);
        await sink.AcceptAsync(request, CancellationToken.None);
        var stored = (await environment.Objects.GetAll()).Single();
        await Assert.That(stored.OwningResourceId).IsEqualTo(request.RegistrationSubmissionId);
        await Assert.That(stored.Purpose).IsEqualTo(StorageObjectPurposes.Document);
        var provider = await environment.Bindings.ResolveTargetAsync(stored.StorageProviderBindingId, stored.Provider, default);
        await Assert.That(await provider.ExistsAsync(new(stored.ObjectKey!), default)).IsTrue();
    }

    [Test]
    public async Task GoogleSheetsSinkPostsRawBoundedValuesWithSubmissionIdempotency()
    {
        Guid submissionId = Guid.CreateVersion7();
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveTenantBindingAsync(TenantId, ApiSecretBindingId, Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret("google", "token", SecretSourceType.EnvironmentVariable, SecretScope.Tenant, TenantId, DateTimeOffset.UtcNow)));
        var sink = new GoogleSheetsRegistrationProviderSubmissionSink(new HttpClient(handler), secrets);

        await sink.AcceptAsync(Request(GoogleSheetsRegistrationProviderSubmissionSink.SupportedTuple, submissionId), CancellationToken.None);

        await Assert.That(handler.Request!.Headers.Authorization!.Parameter).IsEqualTo("token");
        await Assert.That(handler.Request.Headers.GetValues("Idempotency-Key").Single()).IsEqualTo(submissionId.ToString("N"));
        string body = handler.Body!;
        using JsonDocument document = JsonDocument.Parse(body);
        await Assert.That(document.RootElement.GetProperty("values")[0][0].GetString()).IsEqualTo("'=SUM(1,1)");
        await Assert.That(body).DoesNotContain("blocked@example.test");
    }

    [Test]
    public async Task WebhookSinkUsesSafeEndpointSecretSignatureAndNoUrlInPayload()
    {
        Guid submissionId = Guid.CreateVersion7();
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Accepted));
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveTenantBindingAsync(TenantId, WebhookSecretBindingId, Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret("webhook", "secret", SecretSourceType.EnvironmentVariable, SecretScope.Tenant, TenantId, DateTimeOffset.UtcNow)));
        var sink = new WebhookRegistrationProviderSubmissionSink(
            new HttpClient(handler),
            secrets,
            new WebhookEndpointSafetyPolicy(Options.Create(new WebhookOptions { Local = { BlockPrivateNetworks = false } }).ToMonitor()),
            Options.Create(new WebhookOptions()).ToMonitor());

        await sink.AcceptAsync(Request(WebhookRegistrationProviderSubmissionSink.SupportedTuple, submissionId), CancellationToken.None);

        string body = handler.Body!;
        await Assert.That(handler.Request.Headers.GetValues("Idempotency-Key").Single()).IsEqualTo(submissionId.ToString("N"));
        await Assert.That(handler.Request.Headers.GetValues("X-Islamu-Signature").Single()).StartsWith("sha256=");
        await Assert.That(body).Contains("approved.email");
        await Assert.That(body).DoesNotContain("https://webhook.example.test");
        await Assert.That(body).DoesNotContain("blocked@example.test");
    }

    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid ApiSecretBindingId = Guid.CreateVersion7();
    private static readonly Guid WebhookSecretBindingId = Guid.CreateVersion7();

    private static RegistrationProviderSubmissionSinkRequest Request(RegistrationProviderTuple tuple, Guid submissionId)
    {
        RegistrationProviderConnection connection = RegistrationProviderConnection.Create(
            TenantId,
            "sink",
            RegistrationProviderKindEnum.ExternalApi,
            RegistrationProviderDeploymentKindEnum.HostedSaas,
            tuple.ProviderCode,
            tuple.ProviderDeploymentCode,
            tuple.ApiVersion,
            tuple.AdapterPolicyVersion,
            tuple.ConformanceEvidenceRevision,
            tuple.ProviderCode == "GOOGLE_SHEETS" ? "https://sheets.googleapis.com/v4" : "https://webhook.example.test",
            "https://webhook.example.test/registration",
            tuple.ProviderCode == "EXCEL_COMPATIBLE" ? StorageProviders.Local : "Sheet1!A:ZZ",
            ApiSecretBindingId,
            WebhookSecretBindingId,
            DateTime.UtcNow);
        RegistrationProviderBinding binding = RegistrationProviderBinding.Create(
            TenantId,
            connection.Id,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            RegistrationProviderPresentationModeEnum.Manual,
            RegistrationProviderCollectionModeEnum.MirrorOnly,
            RegistrationProviderCompletionModeEnum.Callback,
            RegistrationProviderTrustLevelEnum.SelectedFields,
            WebhookSecretBindingId,
            DateTime.UtcNow);
        binding.SetDraftProvisionedSurvey("spreadsheet", null);

        return new RegistrationProviderSubmissionSinkRequest(
            TenantId,
            binding,
            connection,
            tuple,
            Guid.CreateVersion7(),
            submissionId,
            new Dictionary<string, string>
            {
                ["approved.email"] = "'=SUM(1,1)",
                ["approved.name"] = "Amina"
            },
            null);
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}

file static class OptionsMonitorTestExtensions
{
    public static IOptionsMonitor<T> ToMonitor<T>(this IOptions<T> options) where T : class
    {
        var monitor = Substitute.For<IOptionsMonitor<T>>();
        monitor.CurrentValue.Returns(options.Value);
        return monitor;
    }
}
