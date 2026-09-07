using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services.Keycloak;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

using ApplicationAccountAuthorityKind = Explore.Application.Notifications.AccountAuthorityKind;
using ApplicationNotificationCategory = Explore.Application.Notifications.NotificationCategory;
using ApplicationNotificationOwnership = Explore.Application.Notifications.NotificationOwnership;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class KeycloakAccountAuthorityLifecycleEmailServiceTests
{
    [Test]
    public async Task Requests_WhenConfigured_CallExecuteActionsEmailWithProviderOwnedRequiredActions()
    {
        var cases = new[]
        {
            new LifecycleEmailCase(
                AccountAuthorityLifecycleEmailAction.EmailVerification,
                "VERIFY_EMAIL",
                (service, request) => service.RequestEmailVerificationAsync(request)),
            new LifecycleEmailCase(
                AccountAuthorityLifecycleEmailAction.PasswordReset,
                "UPDATE_PASSWORD",
                (service, request) => service.RequestPasswordResetAsync(request)),
            new LifecycleEmailCase(
                AccountAuthorityLifecycleEmailAction.EmailUpdateVerification,
                "UPDATE_EMAIL",
                (service, request) => service.RequestEmailUpdateVerificationAsync(request))
        };

        foreach (var testCase in cases)
        {
            var handler = new OrderedMessageHandler(
                Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", request =>
                {
                    AssertTokenRequest(request);
                    return JsonResponse("""
                        { "access_token": "admin-token" }
                        """);
                }),
                Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/users/keycloak-user-123/execute-actions-email", request =>
                {
                    AssertExecuteActionsRequest(request, testCase.RequiredAction);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }));
            var orchestrator = new CapturingNotificationOrchestrator();
            var service = CreateService(handler, orchestrator);
            var lifecycleRequest = CreateRequest(correlationId: $"{testCase.RequiredAction}-correlation");

            var result = await testCase.Invoke(service, lifecycleRequest);

            await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.DelegationRecorded);
            await Assert.That(result.Action).IsEqualTo(testCase.Action);
            await Assert.That(result.AccountAuthorityKind).IsEqualTo(ApplicationAccountAuthorityKind.Keycloak);
            await Assert.That(result.NotificationIntentId).IsEqualTo(orchestrator.LastIntentId);
            await Assert.That(result.LocalDelegationId).IsEqualTo(orchestrator.LastDelegationId);
            await Assert.That(result.ReasonCode).IsEqualTo("keycloak_required_action_email_requested");
            await Assert.That(orchestrator.EnqueueCount).IsEqualTo(1);
            await Assert.That(handler.Requests.Count).IsEqualTo(2);

            var serializedResult = JsonSerializer.Serialize(result);
            await Assert.That(serializedResult).DoesNotContain("runtime-admin-secret");
            await Assert.That(serializedResult).DoesNotContain("admin-token");
            await Assert.That(serializedResult).DoesNotContain("provider raw failure");
        }
    }

    [Test]
    public async Task RequestPasswordResetAsync_WithUnsafeUrl_ReturnsProviderNotConfiguredWithoutAuditOrHttp()
    {
        var handler = new OrderedMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        var orchestrator = new CapturingNotificationOrchestrator();
        var service = CreateService(
            handler,
            orchestrator,
            keycloakOptions: CreateKeycloakOptions(baseUrl: "http://127.0.0.1:8080/auth"));

        var result = await service.RequestPasswordResetAsync(CreateRequest());

        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderNotConfigured);
        await Assert.That(result.ReasonCode).IsEqualTo("keycloak_lifecycle_unsafe_host");
        await Assert.That(result.NotificationIntentId).IsNull();
        await Assert.That(result.LocalDelegationId).IsNull();
        await Assert.That(orchestrator.EnqueueCount).IsEqualTo(0);
        await Assert.That(handler.Requests).IsEmpty();
    }

    [Test]
    public async Task RequestEmailVerificationAsync_WhenProviderFails_ReturnsSafeFailureWithLocalDelegationIds()
    {
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ => JsonResponse("""
                { "access_token": "admin-token" }
                """)),
            Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/users/keycloak-user-123/execute-actions-email", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("provider raw failure includes runtime-admin-secret")
            }));
        var orchestrator = new CapturingNotificationOrchestrator();
        var service = CreateService(handler, orchestrator);

        var result = await service.RequestEmailVerificationAsync(CreateRequest());

        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderRequestFailed);
        await Assert.That(result.NotificationIntentId).IsEqualTo(orchestrator.LastIntentId);
        await Assert.That(result.LocalDelegationId).IsEqualTo(orchestrator.LastDelegationId);
        await Assert.That(result.ReasonCode).IsEqualTo("keycloak_lifecycle_email_failed");
        await Assert.That(orchestrator.EnqueueCount).IsEqualTo(1);

        var serializedResult = JsonSerializer.Serialize(result);
        await Assert.That(serializedResult).DoesNotContain("runtime-admin-secret");
        await Assert.That(serializedResult).DoesNotContain("admin-token");
        await Assert.That(serializedResult).DoesNotContain("provider raw failure");
    }

    [Test]
    public async Task RequestEmailVerificationAsync_WhenEmailRequestTransportFails_ReturnsUnreachableFailure()
    {
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ => JsonResponse("""
                { "access_token": "admin-token" }
                """)),
            Expect(HttpMethod.Put, "/auth/admin/realms/ISLAMU/users/keycloak-user-123/execute-actions-email", _ =>
                throw new HttpRequestException("runtime-admin-secret transport failure")));
        var orchestrator = new CapturingNotificationOrchestrator();
        var service = CreateService(handler, orchestrator);

        var result = await service.RequestEmailVerificationAsync(CreateRequest());

        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderRequestFailed);
        await Assert.That(result.ReasonCode).IsEqualTo("keycloak_lifecycle_unreachable");
        await Assert.That(result.NotificationIntentId).IsEqualTo(orchestrator.LastIntentId);
        await Assert.That(result.LocalDelegationId).IsEqualTo(orchestrator.LastDelegationId);
        await Assert.That(JsonSerializer.Serialize(result)).DoesNotContain("runtime-admin-secret");
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RequestEmailVerificationAsync_WhenAdminTokenTransportFails_ReturnsUnreachableFailure()
    {
        var handler = new OrderedMessageHandler(
            Expect(HttpMethod.Post, "/auth/realms/master/protocol/openid-connect/token", _ =>
                throw new HttpRequestException("runtime-admin-secret token transport failure")));
        var orchestrator = new CapturingNotificationOrchestrator();
        var service = CreateService(handler, orchestrator);

        var result = await service.RequestEmailVerificationAsync(CreateRequest());

        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderRequestFailed);
        await Assert.That(result.ReasonCode).IsEqualTo("keycloak_lifecycle_unreachable");
        await Assert.That(result.NotificationIntentId).IsEqualTo(orchestrator.LastIntentId);
        await Assert.That(result.LocalDelegationId).IsEqualTo(orchestrator.LastDelegationId);
        await Assert.That(JsonSerializer.Serialize(result)).DoesNotContain("runtime-admin-secret");
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    private static KeycloakAccountAuthorityLifecycleEmailService CreateService(
        OrderedMessageHandler handler,
        CapturingNotificationOrchestrator orchestrator,
        AccountAuthorityLifecycleEmailOptions? lifecycleOptions = null,
        KeycloakLifecycleEmailOptions? keycloakOptions = null)
    {
        return new KeycloakAccountAuthorityLifecycleEmailService(
            new StaticHttpClientFactory(new HttpClient(handler)),
            orchestrator,
            Options.Create(lifecycleOptions ?? CreateLifecycleOptions()),
            Options.Create(keycloakOptions ?? CreateKeycloakOptions()),
            Substitute.For<ILogger<KeycloakAccountAuthorityLifecycleEmailService>>());
    }

    private static AccountAuthorityLifecycleEmailOptions CreateLifecycleOptions()
    {
        return new AccountAuthorityLifecycleEmailOptions
        {
            Enabled = true,
            ProviderConfigured = true,
            AccountAuthorityKind = ApplicationAccountAuthorityKind.Keycloak
        };
    }

    private static KeycloakLifecycleEmailOptions CreateKeycloakOptions(string baseUrl = "https://keycloak.example.com/auth")
    {
        return new KeycloakLifecycleEmailOptions
        {
            Enabled = true,
            BaseUrl = baseUrl,
            Realm = "ISLAMU",
            AdminUsername = "runtime-admin",
            AdminPassword = "runtime-admin-secret",
            DefaultClientId = "islamu-event-blazor",
            DefaultLifespanSeconds = 900,
            AccountAuthorityKind = ApplicationAccountAuthorityKind.Keycloak
        };
    }

    private static AccountAuthorityLifecycleEmailRequest CreateRequest(string? correlationId = null)
    {
        return new AccountAuthorityLifecycleEmailRequest(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "keycloak-user-123",
            CurrentEmail: "old@example.test",
            ProposedEmail: "new@example.test",
            ClientId: "islamu-event-blazor",
            RedirectUri: "https://event.example.test/account",
            LifespanSeconds: 300,
            CorrelationId: correlationId ?? Guid.CreateVersion7().ToString("N"));
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Expect(
        HttpMethod method,
        string path,
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        return request =>
        {
            if (request.Method != method || request.RequestUri?.AbsolutePath != path)
            {
                throw new InvalidOperationException(
                    $"Expected {method} {path}, got {request.Method} {request.RequestUri?.PathAndQuery}.");
            }

            return responseFactory(request);
        };
    }

    private static void AssertTokenRequest(HttpRequestMessage request)
    {
        var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
        if (!body.Contains("grant_type=password", StringComparison.Ordinal)
            || !body.Contains("client_id=admin-cli", StringComparison.Ordinal)
            || !body.Contains("username=runtime-admin", StringComparison.Ordinal)
            || !body.Contains("password=runtime-admin-secret", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected token request body: {body}");
        }
    }

    private static void AssertExecuteActionsRequest(HttpRequestMessage request, string requiredAction)
    {
        if (request.Headers.Authorization?.Scheme != "Bearer" || request.Headers.Authorization.Parameter != "admin-token")
            throw new InvalidOperationException("Expected bearer authorization header.");

        var query = request.RequestUri?.Query ?? string.Empty;
        if (!query.Contains("redirectUri=", StringComparison.Ordinal)
            || !query.Contains("clientId=islamu-event-blazor", StringComparison.Ordinal)
            || !query.Contains("lifespan=300", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected execute-actions-email query: {query}");
        }

        var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
        if (!body.Contains(requiredAction, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected required action {requiredAction}, got body {body}.");
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed record LifecycleEmailCase(
        AccountAuthorityLifecycleEmailAction Action,
        string RequiredAction,
        Func<KeycloakAccountAuthorityLifecycleEmailService, AccountAuthorityLifecycleEmailRequest, Task<AccountAuthorityLifecycleEmailResult>> Invoke);

    private sealed class CapturingNotificationOrchestrator : INotificationOrchestrator
    {
        public int EnqueueCount { get; private set; }
        public Guid? LastIntentId { get; private set; }
        public Guid? LastDelegationId { get; private set; }

        public Task<NotificationOrchestrationResult> EnqueueAsync(
            NotificationIntentDraft draft,
            CancellationToken cancellationToken = default)
        {
            EnqueueCount++;
            LastIntentId = Guid.CreateVersion7();
            LastDelegationId = Guid.CreateVersion7();
            var intent = new NotificationIntent
            {
                Id = LastIntentId.Value,
                TenantId = draft.TenantId ?? Guid.CreateVersion7(),
                Tenant = null!,
                CategoryId = (int)NotificationCategoryEnum.IdentityLifecycle,
                Category = null!,
                OwnershipTypeId = (int)NotificationOwnershipTypeEnum.AccountAuthority,
                OwnershipType = null!,
                RecipientKindId = (int)NotificationRecipientKindEnum.User,
                RecipientKind = null!,
                StatusId = (int)NotificationIntentStatusEnum.Delegated,
                Status = null!,
                TemplateKey = draft.TemplateKey ?? string.Empty,
                DeduplicationKey = draft.DeduplicationKey ?? string.Empty,
                RecipientUserId = draft.UserId!.Value
            };
            var delegation = new NotificationExternalDelegation
            {
                Id = LastDelegationId.Value,
                TenantId = intent.TenantId,
                Tenant = null,
                NotificationIntentId = intent.Id,
                NotificationIntent = intent,
                ProviderKindId = (int)ExternalWorkflowProviderKindEnum.None,
                ProviderKind = null,
                AccountAuthorityKindId = (int)AccountAuthorityKindEnum.Keycloak,
                AccountAuthorityKind = null,
                StatusId = (int)NotificationExternalDelegationStatusEnum.Requested,
                Status = null,
                RecipientKindId = (int)NotificationRecipientKindEnum.User,
                RecipientKind = null,
                TemplateKey = draft.TemplateKey ?? string.Empty,
                ExternalProviderId = draft.ExternalProviderId,
                ExternalCorrelationId = draft.ExternalCorrelationId
            };

            return Task.FromResult(new NotificationOrchestrationResult(
                intent,
                new NotificationOwnershipDecision(
                    ApplicationNotificationCategory.IdentityLifecycle,
                    ApplicationNotificationOwnership.AccountAuthority,
                    ApplicationAccountAuthorityKind.Keycloak),
                ExternalDelegation: delegation));
        }
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class OrderedMessageHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;

        public OrderedMessageHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            _responses = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));

            if (_responses.Count == 0)
                throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri?.PathAndQuery}.");

            return _responses.Dequeue()(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        AuthenticationHeaderValue? Authorization,
        string Body);
}
