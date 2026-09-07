using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Integrations.Listmonk;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Listmonk;

public sealed class ListmonkSyncServiceTests
{
    [Test]
    public async Task SyncSubscriberAsync_WhenListmonkAcceptsSubscriber_PostsGeneratedPayloadWithBasicAuth()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse(HttpStatusCode.OK, "{\"data\":{\"id\":123}}"));
        var fixture = new Fixture(handler);
        var outbox = CreateOutbox();

        ListmonkSyncResult result = await fixture.Service.SyncSubscriberAsync(outbox, BeginHandoff, CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(handler.CallCount).IsEqualTo(1);
        await Assert.That(handler.RequestUri!.AbsolutePath).IsEqualTo("/api/subscribers");
        await Assert.That(handler.Authorization!.Scheme).IsEqualTo("Basic");
        await Assert.That(DecodeBasicAuth(handler.Authorization)).IsEqualTo($"{fixture.Username}:{fixture.ApiKey}");

        using var body = JsonDocument.Parse(handler.Body!);
        JsonElement root = body.RootElement;
        await Assert.That(root.GetProperty("email").GetString()).IsEqualTo(outbox.SubscriberEmail);
        await Assert.That(root.GetProperty("name").GetString()).IsEqualTo(outbox.SubscriberName);
        await Assert.That(root.GetProperty("status").GetString()).IsEqualTo("enabled");
        await Assert.That(root.GetProperty("preconfirm_subscriptions").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("lists")[0].GetInt32()).IsEqualTo(outbox.ListmonkListId);
    }

    [Test]
    public async Task SyncSubscriberAsync_WhenListmonkReturnsServerError_IsAmbiguous()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse(HttpStatusCode.InternalServerError, "{\"error\":\"down\"}"));
        var fixture = new Fixture(handler);

        ListmonkSyncResult result = await fixture.Service.SyncSubscriberAsync(CreateOutbox(), BeginHandoff, CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Outcome).IsEqualTo(ListmonkSyncOutcome.Ambiguous);
        await Assert.That(handler.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SyncSubscriberAsync_WhenListmonkRejectsCredentials_FailsAsNonRetryable()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse(HttpStatusCode.Unauthorized, "{\"error\":\"unauthorized\"}"));
        var fixture = new Fixture(handler);

        ListmonkSyncResult result = await fixture.Service.SyncSubscriberAsync(CreateOutbox(), BeginHandoff, CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.IsRetryable).IsFalse();
    }

    [Test]
    public async Task SyncSubscriberAsync_WhenPayloadJsonIsInvalid_FailsAsNonRetryableWithoutHttpSend()
    {
        var handler = new RecordingMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        var fixture = new Fixture(handler);

        ListmonkSyncResult result = await fixture.Service.SyncSubscriberAsync(CreateOutbox("{"), BeginHandoff, CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.IsRetryable).IsFalse();
        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task SyncSubscriberAsync_WhenCredentialsAreMissing_FailsAsRetryableWithoutHttpSend()
    {
        var handler = new RecordingMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        var fixture = new Fixture(handler, configureSecrets: false);

        ListmonkSyncResult result = await fixture.Service.SyncSubscriberAsync(CreateOutbox(), BeginHandoff, CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.IsRetryable).IsTrue();
        await Assert.That(handler.CallCount).IsEqualTo(0);
    }

    private static IntegrationSyncOutbox CreateOutbox(string? payloadJson = null)
    {
        return new IntegrationSyncOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            Kind = IntegrationKind.Listmonk,
            SourceType = "registration_order",
            SourceId = Guid.CreateVersion7(),
            SubscriberEmail = "attendee@example.test",
            SubscriberName = "Attendee Example",
            SubscriberPayloadJson = payloadJson ?? "{\"email\":\"attendee@example.test\",\"attribs\":{\"eventId\":\"evt-1\"}}",
            ListmonkListId = 42,
            PreconfirmSubscriptions = true
        };
    }

    private static Task<bool> BeginHandoff(CancellationToken cancellationToken) => Task.FromResult(true);

    private static string DecodeBasicAuth(AuthenticationHeaderValue header)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter!));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class Fixture
    {
        public string Username { get; } = $"user-{Guid.NewGuid():N}";
        public string ApiKey { get; } = Guid.NewGuid().ToString("N");

        public Fixture(RecordingMessageHandler handler, bool configureSecrets = true)
        {
            SettingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
            SecretResolver = Substitute.For<ISecretResolver>();
            HttpClientFactory = Substitute.For<IHttpClientFactory>();

            SettingsResolver.ResolveAsync<string>(
                    GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl,
                    Arg.Any<SettingContext>(),
                    Arg.Any<CancellationToken>())
                .Returns("https://listmonk.example.test");

            if (configureSecrets)
            {
                SecretResolver.ResolveAsync(
                        SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiUsername,
                        Arg.Any<Guid?>(),
                        Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiUsername,
                Username,
                SecretSourceType.EnvironmentVariable,
                SecretScope.Tenant,
                Guid.CreateVersion7(),
                DateTimeOffset.UtcNow)));
                SecretResolver.ResolveAsync(
                        SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiKey,
                        Arg.Any<Guid?>(),
                        Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiKey,
                ApiKey,
                SecretSourceType.EnvironmentVariable,
                SecretScope.Tenant,
                Guid.CreateVersion7(),
                DateTimeOffset.UtcNow)));
            }
            else
            {
                SecretResolver.ResolveAsync(
                        Arg.Any<string>(),
                        Arg.Any<Guid?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(SecretResolutionResult.Unconfigured);
            }

            HttpClientFactory.CreateClient(ListmonkSyncService.HttpClientName)
                .Returns(_ => new HttpClient(handler));

            Service = new ListmonkSyncService(
                HttpClientFactory,
                SettingsResolver,
                SecretResolver,
                NullLogger<ListmonkSyncService>.Instance);
        }

        private IHierarchicalSettingsResolver SettingsResolver { get; }
        private ISecretResolver SecretResolver { get; }
        private IHttpClientFactory HttpClientFactory { get; }
        public ListmonkSyncService Service { get; }
    }

    private sealed class RecordingMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
