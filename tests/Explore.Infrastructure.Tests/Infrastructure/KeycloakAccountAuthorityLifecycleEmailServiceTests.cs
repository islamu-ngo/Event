using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services.Keycloak;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NotificationCategory = Explore.Application.Notifications.NotificationCategory;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class KeycloakAccountAuthorityLifecycleEmailServiceTests
{
    [Test]
    [Arguments(AccountAuthorityLifecycleEmailAction.PasswordReset, "UPDATE_PASSWORD", false)]
    [Arguments(AccountAuthorityLifecycleEmailAction.PasswordReset, "UPDATE_PASSWORD", true)]
    [Arguments(AccountAuthorityLifecycleEmailAction.EmailVerification, "VERIFY_EMAIL", false)]
    [Arguments(AccountAuthorityLifecycleEmailAction.EmailVerification, "VERIFY_EMAIL", true)]
    [Arguments(AccountAuthorityLifecycleEmailAction.EmailUpdateVerification, "UPDATE_EMAIL", false)]
    [Arguments(AccountAuthorityLifecycleEmailAction.EmailUpdateVerification, "UPDATE_EMAIL", true)]
    public async Task Request_WhenProviderFails_LogsOnlyBoundedStatusAndAction(
        AccountAuthorityLifecycleEmailAction action, string requiredAction, bool structured)
    {
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var loginId = Guid.CreateVersion7();
        var subject = Guid.NewGuid().ToString("N");
        var proposedEmail = $"{Guid.NewGuid():N}@example.test";
        using var handler = new FailingProviderHandler(subject, requiredAction, proposedEmail);
        using var client = new HttpClient(handler);
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(KeycloakAccountAuthorityLifecycleEmailService.HttpClientName).Returns(client);
        var externalLogins = Substitute.For<IUserExternalLoginRepository>();
        externalLogins.GetById(loginId).Returns(new UserExternalLogin
        {
            Id = loginId,
            UserId = userId,
            User = null!,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
            AuthenticationProvider = null!,
            ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                "https://keycloak.example.test/auth/realms/ISLAMU", subject).Value
        });
        var tenantUsers = Substitute.For<ITenantUserRepository>();
        tenantUsers.GetByTenantAndUserAsync(tenantId, userId, Arg.Any<CancellationToken>())
            .Returns(new TenantUser { TenantId = tenantId, UserId = userId, Tenant = null!, User = null! });
        var intent = new NotificationIntent
        {
            TenantId = tenantId,
            RecipientUserId = userId,
            TemplateKey = "identity.lifecycle",
            DeduplicationKey = Guid.NewGuid().ToString("N")
        };
        var delegation = new NotificationExternalDelegation
        {
            TenantId = tenantId,
            NotificationIntentId = intent.Id,
            TemplateKey = intent.TemplateKey
        };
        var orchestrator = Substitute.For<INotificationOrchestrator>();
        orchestrator.EnqueueAsync(Arg.Is<NotificationIntentDraft>(draft =>
                draft != null && draft.TenantId == tenantId && draft.UserId == userId && draft.ExternalProviderId == subject),
                Arg.Any<CancellationToken>())
            .Returns(new NotificationOrchestrationResult(intent,
                new NotificationOwnershipDecision(NotificationCategory.IdentityLifecycle,
                    NotificationOwnership.AccountAuthority, AccountAuthorityKind.Keycloak),
                ExternalDelegation: delegation));
        var logger = new TestListLogger<KeycloakAccountAuthorityLifecycleEmailService>();
        var provider = new KeycloakAccountAuthorityLifecycleEmailService(clients, orchestrator, tenantUsers,
            Options.Create(new AccountAuthorityLifecycleEmailOptions { Enabled = true, ProviderConfigured = true }),
            Options.Create(new KeycloakLifecycleEmailOptions
            {
                Enabled = true,
                BaseUrl = "https://keycloak.example.test/auth",
                Realm = "ISLAMU",
                AdminUsername = handler.AdminUsername,
                AdminPassword = handler.AdminPassword
            }), logger);
        IAccountAuthorityLifecycleEmailService service = new DefaultAccountAuthorityLifecycleEmailService(
            externalLogins, [provider]);
        var request = new AccountAuthorityLifecycleEmailRequest(userId, loginId, tenantId, proposedEmail);

        var result = await (action switch
        {
            AccountAuthorityLifecycleEmailAction.PasswordReset => service.RequestPasswordResetAsync(request),
            AccountAuthorityLifecycleEmailAction.EmailVerification => service.RequestEmailVerificationAsync(request),
            AccountAuthorityLifecycleEmailAction.EmailUpdateVerification => service.RequestEmailUpdateVerificationAsync(request),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        });

        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderRequestFailed);
        await Assert.That(result.Action).IsEqualTo(action);
        await Assert.That(result.AccountAuthorityKind).IsEqualTo(AccountAuthorityKind.Keycloak);
        await Assert.That(result.ReasonCode).IsEqualTo("keycloak_lifecycle_email_failed");
        await Assert.That(result.NotificationIntentId).IsEqualTo(intent.Id);
        await Assert.That(result.LocalDelegationId).IsEqualTo(delegation.Id);
        await Assert.That(handler.RequestCount).IsEqualTo(2);
        await Assert.That(logger.Entries.Count).IsEqualTo(1);
        var entry = logger.Entries.Single();
        await Assert.That(entry.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(entry.Exception).IsNull();
        await Assert.That(entry.State.Single(property => property.Key == "StatusCode").Value).IsEqualTo(503);
        await Assert.That(entry.State.Single(property => property.Key == "Action").Value).IsEqualTo(action);
        await Assert.That(entry.Message).Contains("503");
        await Assert.That(entry.Message).Contains(action.ToString());

        var output = structured ? JsonSerializer.Serialize(entry.State) : entry.Message;
        foreach (var identifier in new[] { tenantId, userId, loginId })
        {
            await Assert.That(output).DoesNotContain(identifier.ToString("D"));
            await Assert.That(output).DoesNotContain(identifier.ToString("N"));
        }
        foreach (var canary in new[]
                 {
                     subject, proposedEmail, handler.AdminUsername, handler.AdminPassword,
                     handler.AdminToken, handler.ResponseBodyCanary
                 })
        {
            await Assert.That(output).DoesNotContain(canary);
        }
        await Assert.That(entry.State.Select(property => property.Key))
            .IsEquivalentTo(["StatusCode", "Action", "{OriginalFormat}"]);
    }

    private sealed class FailingProviderHandler(string subject, string requiredAction, string proposedEmail)
        : HttpMessageHandler
    {
        public string AdminUsername { get; } = Guid.NewGuid().ToString("N");
        public string AdminPassword { get; } = Guid.NewGuid().ToString("N");
        public string AdminToken { get; } = Guid.NewGuid().ToString("N");
        public string ResponseBodyCanary { get; } = Guid.NewGuid().ToString("N");
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (RequestCount == 1)
            {
                await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
                await Assert.That(request.RequestUri!.AbsolutePath)
                    .IsEqualTo("/auth/realms/master/protocol/openid-connect/token");
                await Assert.That(body).Contains($"username={AdminUsername}");
                await Assert.That(body).Contains($"password={AdminPassword}");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { access_token = AdminToken }),
                        Encoding.UTF8, "application/json")
                };
            }

            await Assert.That(RequestCount).IsEqualTo(2);
            await Assert.That(request.Method).IsEqualTo(HttpMethod.Put);
            await Assert.That(request.RequestUri!.AbsolutePath)
                .IsEqualTo($"/auth/admin/realms/ISLAMU/users/{subject}/execute-actions-email");
            await Assert.That(request.Headers.Authorization?.Scheme).IsEqualTo("Bearer");
            await Assert.That(request.Headers.Authorization?.Parameter).IsEqualTo(AdminToken);
            await Assert.That(JsonSerializer.Deserialize<string[]>(body)!).IsEquivalentTo([requiredAction]);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    error = ResponseBodyCanary,
                    email = proposedEmail,
                    password = AdminPassword,
                    access_token = AdminToken
                }), Encoding.UTF8, "application/json")
            };
        }
    }
}
