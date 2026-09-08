
using System.Net;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Notifications;
using Microsoft.EntityFrameworkCore;
using static Event.Persistence.IntegrationTests.Identity.DefaultAccountAuthorityLifecycleEmailServiceTests;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class KeycloakAccountAuthorityLifecycleEmailServiceTests
{
    [Test]
    [Arguments(AccountAuthorityLifecycleEmailAction.EmailVerification, "VERIFY_EMAIL")]
    [Arguments(AccountAuthorityLifecycleEmailAction.PasswordReset, "UPDATE_PASSWORD")]
    [Arguments(AccountAuthorityLifecycleEmailAction.EmailUpdateVerification, "UPDATE_EMAIL")]
    public async Task Requests_WhenConfigured_CallExecuteActionsEmailWithProviderOwnedRequiredActions(
        AccountAuthorityLifecycleEmailAction action, string requiredAction)
    {
        await using var fixture = await AccountAuthorityLifecycleEmailFixture.CreateAsync();
        var result = await InvokeAsync(fixture.Service, action, fixture.Request());
        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.DelegationRecorded);
        await Assert.That(result.AccountAuthorityKind).IsEqualTo(AccountAuthorityKind.Keycloak);
        await Assert.That(fixture.Http.Requests.Count).IsEqualTo(2);
        var token = fixture.Http.Requests[0];
        await Assert.That(token.Body).Contains("grant_type=password");
        await Assert.That(token.Body).Contains("client_id=admin-cli");
        await Assert.That(token.Body).Contains($"password={fixture.Http.AdminPassword}");
        var email = fixture.Http.Requests[1];
        await Assert.That(email.Uri.AbsolutePath).IsEqualTo("/auth/admin/realms/ISLAMU/users/keycloak-user-123/execute-actions-email");
        await Assert.That(email.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(email.Authorization.Parameter).IsEqualTo(fixture.Http.AdminToken);
        await Assert.That(email.Uri.Query).Contains("clientId=event-client");
        await Assert.That(email.Uri.Query).Contains("lifespan=300");
        await Assert.That(email.Uri.Query).Contains("redirectUri=");
        await Assert.That(JsonSerializer.Deserialize<string[]>(email.Body)!).IsEquivalentTo([requiredAction]);
        await AssertPersistedSafeOutcomeAsync(fixture, result);
    }

    [Test]
    public async Task RequestPasswordResetAsync_WithUnsafeUrl_ReturnsProviderNotConfiguredWithoutAuditOrHttp()
    {
        await using var fixture = await AccountAuthorityLifecycleEmailFixture.CreateAsync(baseUrl: "http://127.0.0.1:8080/auth");
        var result = await fixture.Service.RequestPasswordResetAsync(fixture.Request());
        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderNotConfigured);
        await Assert.That(result.ReasonCode).IsEqualTo("keycloak_lifecycle_unsafe_host");
        await AssertNoDeliveryAsync(fixture);
    }

    [Test]
    public async Task RequestEmailVerificationAsync_WhenProviderFails_ReturnsSafeFailureWithLocalDelegationIds()
    {
        await using var fixture = await AccountAuthorityLifecycleEmailFixture.CreateAsync();
        fixture.Http.EmailStatus = HttpStatusCode.InternalServerError;
        var result = await fixture.Service.RequestEmailVerificationAsync(fixture.Request());
        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderRequestFailed);
        await Assert.That(result.ReasonCode).IsEqualTo("keycloak_lifecycle_email_failed");
        await AssertPersistedSafeOutcomeAsync(fixture, result);
    }

    [Test]
    public async Task RequestEmailVerificationAsync_WhenEmailRequestTransportFails_ReturnsUnreachableFailure()
    {
        await using var fixture = await AccountAuthorityLifecycleEmailFixture.CreateAsync();
        fixture.Http.FailEmailTransport = true;
        var result = await fixture.Service.RequestEmailVerificationAsync(fixture.Request());
        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderRequestFailed);
        await Assert.That(result.ReasonCode).IsEqualTo("keycloak_lifecycle_unreachable");
        await Assert.That(fixture.Http.Requests.Count).IsEqualTo(2);
        await AssertPersistedSafeOutcomeAsync(fixture, result);
    }

    [Test]
    public async Task RequestEmailVerificationAsync_WhenAdminTokenTransportFails_ReturnsUnreachableFailure()
    {
        await using var fixture = await AccountAuthorityLifecycleEmailFixture.CreateAsync();
        fixture.Http.FailTokenTransport = true;
        var result = await fixture.Service.RequestEmailVerificationAsync(fixture.Request());
        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderRequestFailed);
        await Assert.That(result.ReasonCode).IsEqualTo("keycloak_lifecycle_unreachable");
        await Assert.That(fixture.Http.Requests.Count).IsEqualTo(1);
        await AssertPersistedSafeOutcomeAsync(fixture, result);
    }

    [Test]
    public async Task SameSubjectInAnotherIssuer_DoesNotReachConfiguredKeycloakRealm()
    {
        await using var fixture = await AccountAuthorityLifecycleEmailFixture.CreateAsync();
        var foreignLogin = await fixture.AddLoginAsync(fixture.UserId,
            PlatformIdentityPrincipalExtensions.CreateOidcAccountKey("https://other.example.test/realms/ISLAMU",
                AccountAuthorityLifecycleEmailFixture.Subject));
        var result = await fixture.Service.RequestEmailVerificationAsync(fixture.Request(foreignLogin));
        await Assert.That(result.AccountAuthorityKind).IsEqualTo(AccountAuthorityKind.Keycloak);
        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderNotConfigured);
        await Assert.That(result.ReasonCode).IsEqualTo("keycloak_lifecycle_authority_mismatch");
        await AssertNoDeliveryAsync(fixture);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOrForeignTenant_DoesNotFabricateAuditMembership(bool foreignTenant)
    {
        await using var fixture = await AccountAuthorityLifecycleEmailFixture.CreateAsync();
        var request = fixture.Request() with { TenantId = foreignTenant ? Guid.CreateVersion7() : null };
        var result = await fixture.Service.RequestEmailVerificationAsync(request);
        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ScopeUnavailable);
        await Assert.That(result.AccountAuthorityKind).IsEqualTo(AccountAuthorityKind.Keycloak);
        await Assert.That(await fixture.Context.TenantUsers.CountAsync()).IsEqualTo(1);
        await AssertNoDeliveryAsync(fixture);
    }

    private static async Task AssertPersistedSafeOutcomeAsync(AccountAuthorityLifecycleEmailFixture fixture,
        AccountAuthorityLifecycleEmailResult result)
    {
        var intent = await fixture.Context.NotificationIntents.AsNoTracking().SingleAsync(row => row.Id == result.NotificationIntentId);
        var delegation = await fixture.Context.NotificationExternalDelegations.AsNoTracking().SingleAsync(row => row.Id == result.LocalDelegationId);
        await Assert.That(delegation.NotificationIntentId).IsEqualTo(intent.Id);
        await Assert.That(delegation.TenantId).IsEqualTo(fixture.TenantId);
        await Assert.That(delegation.AccountAuthorityKindId).IsEqualTo((int?)Explore.Domain.Enums.AccountAuthorityKindEnum.Keycloak);
        var safeData = JsonSerializer.Serialize(new { result, intent.SafePayloadReference, delegation.SafePayloadHash });
        await Assert.That(safeData).DoesNotContain(fixture.Http.AdminPassword);
        await Assert.That(safeData).DoesNotContain(fixture.Http.AdminToken);
        await Assert.That(await fixture.Context.EmailDispatchOutbox.CountAsync()).IsEqualTo(0);
    }
}
