using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Options;

using ApplicationAccountAuthorityKind = Explore.Application.Notifications.AccountAuthorityKind;
using ApplicationNotificationCategory = Explore.Application.Notifications.NotificationCategory;
using ApplicationNotificationOwnership = Explore.Application.Notifications.NotificationOwnership;

namespace Event.Application.UnitTests.Services;

public sealed class DefaultAccountAuthorityLifecycleEmailServiceTests
{
    [Test]
    public async Task RequestEmailVerificationAsync_ReturnsDisabledWithoutRecordingDelegation()
    {
        var orchestrator = new CapturingNotificationOrchestrator();
        var service = CreateService(orchestrator, new AccountAuthorityLifecycleEmailOptions
        {
            Enabled = false,
            ProviderConfigured = true,
            AccountAuthorityKind = ApplicationAccountAuthorityKind.Keycloak
        });

        var result = await service.RequestEmailVerificationAsync(CreateRequest());

        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.Disabled);
        await Assert.That(result.Action).IsEqualTo(AccountAuthorityLifecycleEmailAction.EmailVerification);
        await Assert.That(result.AccountAuthorityKind).IsEqualTo(ApplicationAccountAuthorityKind.Keycloak);
        await Assert.That(result.DelegationRecorded).IsFalse();
        await Assert.That(result.NotificationIntentId).IsNull();
        await Assert.That(result.LocalDelegationId).IsNull();
        await Assert.That(result.ReasonCode).IsEqualTo("account_authority_lifecycle_email_disabled");
        await Assert.That(orchestrator.EnqueueCount).IsEqualTo(0);
        await Assert.That(orchestrator.LastDraft).IsNull();
    }

    [Test]
    public async Task RequestPasswordResetAsync_ReturnsProviderNotConfiguredWithoutRecordingDelegation()
    {
        var orchestrator = new CapturingNotificationOrchestrator();
        var service = CreateService(orchestrator, new AccountAuthorityLifecycleEmailOptions
        {
            Enabled = true,
            ProviderConfigured = false,
            AccountAuthorityKind = ApplicationAccountAuthorityKind.Keycloak
        });

        var result = await service.RequestPasswordResetAsync(CreateRequest());

        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.ProviderNotConfigured);
        await Assert.That(result.Action).IsEqualTo(AccountAuthorityLifecycleEmailAction.PasswordReset);
        await Assert.That(result.AccountAuthorityKind).IsEqualTo(ApplicationAccountAuthorityKind.Keycloak);
        await Assert.That(result.DelegationRecorded).IsFalse();
        await Assert.That(result.NotificationIntentId).IsNull();
        await Assert.That(result.LocalDelegationId).IsNull();
        await Assert.That(result.ReasonCode).IsEqualTo("account_authority_provider_not_configured");
        await Assert.That(orchestrator.EnqueueCount).IsEqualTo(0);
        await Assert.That(orchestrator.LastDraft).IsNull();
    }

    [Test]
    public async Task Requests_WhenConfigured_RecordSafeIdentityLifecycleDelegationDrafts()
    {
        var cases = new[]
        {
            new LifecycleEmailCase(
                AccountAuthorityLifecycleEmailAction.EmailVerification,
                "identity.email.verify",
                "email-verification",
                (service, request) => service.RequestEmailVerificationAsync(request)),
            new LifecycleEmailCase(
                AccountAuthorityLifecycleEmailAction.PasswordReset,
                "identity.password.reset",
                "password-reset",
                (service, request) => service.RequestPasswordResetAsync(request)),
            new LifecycleEmailCase(
                AccountAuthorityLifecycleEmailAction.EmailUpdateVerification,
                "identity.email-update.verify",
                "email-update-verification",
                (service, request) => service.RequestEmailUpdateVerificationAsync(request))
        };

        foreach (var testCase in cases)
        {
            var orchestrator = new CapturingNotificationOrchestrator();
            var service = CreateService(orchestrator, new AccountAuthorityLifecycleEmailOptions
            {
                Enabled = true,
                ProviderConfigured = true,
                AccountAuthorityKind = ApplicationAccountAuthorityKind.Keycloak
            });
            var request = CreateRequest(correlationId: $"{testCase.Action}-correlation");

            var result = await testCase.Invoke(service, request);

            var draft = orchestrator.LastDraft!;
            await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.DelegationRecorded);
            await Assert.That(result.Action).IsEqualTo(testCase.Action);
            await Assert.That(result.AccountAuthorityKind).IsEqualTo(ApplicationAccountAuthorityKind.Keycloak);
            await Assert.That(result.DelegationRecorded).IsTrue();
            await Assert.That(result.NotificationIntentId).IsEqualTo(orchestrator.LastIntentId);
            await Assert.That(result.LocalDelegationId).IsEqualTo(orchestrator.LastDelegationId);
            await Assert.That(result.ReasonCode).IsEqualTo("delegation_recorded");
            await Assert.That(orchestrator.EnqueueCount).IsEqualTo(1);
            await Assert.That(draft.Category).IsEqualTo(ApplicationNotificationCategory.IdentityLifecycle);
            await Assert.That(draft.TenantId).IsEqualTo(request.TenantId);
            await Assert.That(draft.RecipientKind).IsEqualTo("User");
            await Assert.That(draft.TemplateKey).IsEqualTo(testCase.TemplateKey);
            await Assert.That(draft.SafePayloadReference).IsEqualTo("account-authority:Keycloak:user:keycloak-user-123");
            await Assert.That(draft.DeduplicationKey).IsEqualTo(
                $"identity-lifecycle:{testCase.ActionKey}:{request.UserId}:{request.CorrelationId}");
            await Assert.That(draft.CorrelationId).IsEqualTo(request.CorrelationId);
            await Assert.That(draft.UserId).IsEqualTo(request.UserId);
            await Assert.That(draft.ExternalProviderId).IsEqualTo("keycloak-user-123");
            await Assert.That(draft.ExternalCorrelationId).IsEqualTo(request.CorrelationId);
            await Assert.That(result.ToString()).DoesNotContain("new@example.test");
            await Assert.That(result.ToString()).DoesNotContain("old@example.test");
        }
    }

    private static DefaultAccountAuthorityLifecycleEmailService CreateService(
        INotificationOrchestrator orchestrator,
        AccountAuthorityLifecycleEmailOptions options)
    {
        return new DefaultAccountAuthorityLifecycleEmailService(orchestrator, Options.Create(options));
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
            LifespanSeconds: 900,
            CorrelationId: correlationId ?? Guid.CreateVersion7().ToString("N"));
    }

    private sealed record LifecycleEmailCase(
        AccountAuthorityLifecycleEmailAction Action,
        string TemplateKey,
        string ActionKey,
        Func<DefaultAccountAuthorityLifecycleEmailService, AccountAuthorityLifecycleEmailRequest, Task<AccountAuthorityLifecycleEmailResult>> Invoke);

    private sealed class CapturingNotificationOrchestrator : INotificationOrchestrator
    {
        public int EnqueueCount { get; private set; }
        public Guid? LastIntentId { get; private set; }
        public Guid? LastDelegationId { get; private set; }
        public NotificationIntentDraft? LastDraft { get; private set; }

        public Task<NotificationOrchestrationResult> EnqueueAsync(
            NotificationIntentDraft draft,
            CancellationToken cancellationToken = default)
        {
            EnqueueCount++;
            LastDraft = draft;
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
                SafePayloadReference = draft.SafePayloadReference,
                SafePayloadHash = draft.SafePayloadHash,
                CorrelationId = draft.CorrelationId,
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
                SafePayloadHash = draft.SafePayloadHash,
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
}
