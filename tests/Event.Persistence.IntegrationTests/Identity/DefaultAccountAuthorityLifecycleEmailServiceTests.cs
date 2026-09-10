
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class DefaultAccountAuthorityLifecycleEmailServiceTests
{
    [Test]
    public async Task RequestEmailVerificationAsync_ReturnsDisabledWithoutRecordingDelegation()
    {
        await using var fixture = await AccountAuthorityLifecycleEmailFixture.CreateAsync(lifecycleEnabled: false);
        var result = await fixture.Service.RequestEmailVerificationAsync(fixture.Request());

        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.Disabled);
        await Assert.That(result.Action).IsEqualTo(AccountAuthorityLifecycleEmailAction.EmailVerification);
        await Assert.That(result.AccountAuthorityKind).IsEqualTo(AccountAuthorityKind.Keycloak);
        await AssertNoDeliveryAsync(fixture);
    }

    [Test]
    public async Task RequestPasswordResetAsync_ReturnsProviderNotConfiguredWithoutRecordingDelegation()
    {
        await using var fixture = await AccountAuthorityLifecycleEmailFixture.CreateAsync(providerConfigured: false);
        var result = await fixture.Service.RequestPasswordResetAsync(fixture.Request());

        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderNotConfigured);
        await Assert.That(result.Action).IsEqualTo(AccountAuthorityLifecycleEmailAction.PasswordReset);
        await Assert.That(result.AccountAuthorityKind).IsEqualTo(AccountAuthorityKind.Keycloak);
        await AssertNoDeliveryAsync(fixture);
    }

    [Test]
    public async Task Requests_WhenConfigured_RecordSafeIdentityLifecycleDelegationDrafts()
    {
        await using var fixture = await AccountAuthorityLifecycleEmailFixture.CreateAsync();
        // This also detects Infrastructure accidentally replacing the global routing service with Keycloak again.
        await Assert.That(fixture.Service).IsTypeOf<DefaultAccountAuthorityLifecycleEmailService>();
        foreach (var action in Enum.GetValues<AccountAuthorityLifecycleEmailAction>())
        {
            var request = fixture.Request();
            var result = await InvokeAsync(fixture.Service, action, request);
            var intent = await fixture.Context.NotificationIntents.SingleAsync(row => row.Id == result.NotificationIntentId);
            var delegation = await fixture.Context.NotificationExternalDelegations.SingleAsync(row => row.Id == result.LocalDelegationId);
            await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.DelegationRecorded);
            await Assert.That(result.Action).IsEqualTo(action);
            await Assert.That(delegation.AccountAuthorityKindId).IsEqualTo((int?)AccountAuthorityKindEnum.Keycloak);
            await Assert.That(delegation.ExternalProviderId).IsEqualTo(AccountAuthorityLifecycleEmailFixture.Subject);
            await Assert.That(intent.RecipientUserId).IsEqualTo(request.UserId);
            await Assert.That(intent.TenantId).IsEqualTo(request.TenantId!.Value);
            await Assert.That(intent.SafePayloadReference).IsEqualTo($"account-authority:Keycloak:login:{request.ExternalLoginId}");
            await Assert.That(intent.CorrelationId).IsEqualTo(request.CorrelationId);
            await Assert.That(intent.SafePayloadReference).DoesNotContain(request.ProposedEmail!);
        }
        await Assert.That(await fixture.Context.EmailDispatchOutbox.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.NotificationDeliveries.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task ExactLinkedAccount_NotEmailOrGlobalDefault_SelectsEachAuthority()
    {
        await using var fixture = await AccountAuthorityLifecycleEmailFixture.CreateAsync();
        var local = await fixture.Service.RequestPasswordResetAsync(fixture.Request(fixture.LocalLoginId) with { TenantId = null });
        var atproto = await fixture.Service.RequestPasswordResetAsync(fixture.Request(fixture.AtprotoLoginId) with { TenantId = null });
        await Assert.That(local.AccountAuthorityKind).IsEqualTo(AccountAuthorityKind.LocalIdentity);
        // Local adapter is registered, but this routing fixture has no Ready native credential operation.
        await Assert.That(local.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.AccountNotLinked);
        await Assert.That(atproto.AccountAuthorityKind).IsEqualTo(AccountAuthorityKind.AtprotoPds);
        await Assert.That(atproto.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderManaged);
        await AssertNoDeliveryAsync(fixture);
        // The very same user has no mailbox/verification fact, but its exact Keycloak link remains provider-owned.
        var keycloak = await fixture.Service.RequestPasswordResetAsync(fixture.Request());
        await Assert.That(keycloak.AccountAuthorityKind).IsEqualTo(AccountAuthorityKind.Keycloak);
        await Assert.That(keycloak.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.DelegationRecorded);
    }

    [Test]
    public async Task ForeignOrMissingLinkedAccount_CannotBeSelectedByMatchingMailbox()
    {
        await using var fixture = await AccountAuthorityLifecycleEmailFixture.CreateAsync();
        var otherUser = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii { Email = string.Empty, FirstName = "Other", LastName = "Account" },
            CreatedAt = DateTime.UtcNow
        };
        fixture.Context.Users.Add(otherUser);
        await fixture.Context.SaveChangesAsync();
        var foreignLogin = await fixture.AddLoginAsync(otherUser.Id,
            PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(AccountAuthorityLifecycleEmailFixture.Issuer, "other-subject"));
        foreach (var loginId in new[] { foreignLogin, Guid.CreateVersion7() })
        {
            var result = await fixture.Service.RequestEmailVerificationAsync(fixture.Request(loginId));
            await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.AccountNotLinked);
            await Assert.That(result.AccountAuthorityKind).IsEqualTo(AccountAuthorityKind.None);
        }
        await AssertNoDeliveryAsync(fixture);
    }

    [Test]
    public async Task EventSmtpChanges_DoNotChangeExternalProviderRoutingOrVerificationFacts()
    {
        await using var fixture = await AccountAuthorityLifecycleEmailFixture.CreateAsync();
        var before = await fixture.Service.RequestPasswordResetAsync(fixture.Request());
        var nativeBefore = await fixture.Service.RequestEmailVerificationAsync(fixture.Request(fixture.AtprotoLoginId));
        await EmailDispatchSqliteFixture.SetEmailSettingAsync(fixture.Context, GovernanceSettingKeys.TenantDelegation.LockSmtp, "false");
        await EmailDispatchSqliteFixture.SetEmailSettingAsync(fixture.Context, GovernanceSettingKeys.Email.DeliveryEnabled, "false");
        await EmailDispatchSqliteFixture.SetEmailSettingAsync(fixture.Context, GovernanceSettingKeys.Email.DeliveryEnabled, "false", fixture.TenantId);
        var after = await fixture.Service.RequestPasswordResetAsync(fixture.Request());
        var nativeAfter = await fixture.Service.RequestEmailVerificationAsync(fixture.Request(fixture.AtprotoLoginId));
        await Assert.That(after.Status).IsEqualTo(before.Status);
        await Assert.That(after.AccountAuthorityKind).IsEqualTo(before.AccountAuthorityKind);
        await Assert.That(nativeAfter.Status).IsEqualTo(nativeBefore.Status);
        await Assert.That(nativeAfter.AccountAuthorityKind).IsEqualTo(nativeBefore.AccountAuthorityKind);
        await Assert.That(fixture.Http.Requests.Count).IsEqualTo(4);
        var user = await fixture.Context.Users.AsNoTracking().SingleAsync(row => row.Id == fixture.UserId);
        await Assert.That(user.EmailVerified).IsEqualTo((bool?)false);
        await Assert.That(await fixture.Context.EmailDispatchOutbox.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task TenantlessLocalAccount_DoesNotCreateTenantMembershipOrExternalDelegation()
    {
        await using var fixture = await AccountAuthorityLifecycleEmailFixture.CreateAsync();
        fixture.Context.TenantUsers.RemoveRange(await fixture.Context.TenantUsers.ToListAsync());
        await fixture.Context.SaveChangesAsync();
        foreach (var action in Enum.GetValues<AccountAuthorityLifecycleEmailAction>())
        {
            var result = await InvokeAsync(fixture.Service, action, fixture.Request(fixture.LocalLoginId) with { TenantId = null });
            await Assert.That(result.AccountAuthorityKind).IsEqualTo(AccountAuthorityKind.LocalIdentity);
            await Assert.That(result.DelegationRecorded).IsFalse();
        }
        await Assert.That(await fixture.Context.TenantUsers.CountAsync()).IsEqualTo(0);
        await AssertNoDeliveryAsync(fixture);
    }

    internal static Task<AccountAuthorityLifecycleEmailResult> InvokeAsync(IAccountAuthorityLifecycleEmailService service,
        AccountAuthorityLifecycleEmailAction action, AccountAuthorityLifecycleEmailRequest request) => action switch
        {
            AccountAuthorityLifecycleEmailAction.EmailVerification => service.RequestEmailVerificationAsync(request),
            AccountAuthorityLifecycleEmailAction.PasswordReset => service.RequestPasswordResetAsync(request),
            AccountAuthorityLifecycleEmailAction.EmailUpdateVerification => service.RequestEmailUpdateVerificationAsync(request),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

    internal static async Task AssertNoDeliveryAsync(AccountAuthorityLifecycleEmailFixture fixture)
    {
        await Assert.That(fixture.Http.Requests).IsEmpty();
        await Assert.That(await fixture.Context.NotificationIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.NotificationExternalDelegations.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.EmailDispatchOutbox.CountAsync()).IsEqualTo(0);
    }
}
