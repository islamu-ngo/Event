using Explore.Application.Notifications;
using Microsoft.Extensions.Options;

namespace Event.Application.UnitTests.Notifications;

public sealed class DefaultNotificationOwnershipResolverTests
{
    [Test]
    public async Task ResolveAsync_RejectsIdentityLifecycleWithoutResolvedLinkedAuthority()
    {
        var resolver = CreateResolver();

        await Assert.That(async () => await resolver.ResolveAsync(
            new NotificationIntentDraft(NotificationCategory.IdentityLifecycle)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ResolveAsync_RoutesProductCategoriesToIslamuEventByDefault()
    {
        var resolver = CreateResolver();

        foreach (var category in new[]
        {
            NotificationCategory.ProductLifecycle,
            NotificationCategory.EventLifecycle,
            NotificationCategory.RegistrationLifecycle,
            NotificationCategory.TrustSafetyReporting,
            NotificationCategory.TrustSafetyModeration,
            NotificationCategory.PlatformOperations,
            NotificationCategory.Marketing
        })
        {
            var decision = await resolver.ResolveAsync(new NotificationIntentDraft(category));

            await Assert.That(decision.Ownership).IsEqualTo(NotificationOwnership.IslamuEvent);
            await Assert.That(decision.IsLocalIslamuDelivery).IsTrue();
            await Assert.That(decision.AccountAuthorityKind).IsEqualTo(AccountAuthorityKind.None);
        }
    }

    [Test]
    public async Task ResolveAsync_RoutesProviderInternalToExternalWorkflowProvider()
    {
        var resolver = CreateResolver(new NotificationRoutingOptions
        {
            ProviderInternalProvider = ExternalWorkflowProviderKind.Coop
        });

        var decision = await resolver.ResolveAsync(new NotificationIntentDraft(
            NotificationCategory.ProviderInternal,
            IsUserFacing: false));

        await Assert.That(decision.Ownership).IsEqualTo(NotificationOwnership.ExternalWorkflowProvider);
        await Assert.That(decision.ExternalWorkflowProviderKind).IsEqualTo(ExternalWorkflowProviderKind.Coop);
        await Assert.That(decision.RequiresLocalAudit).IsFalse();
    }

    [Test]
    public async Task Constructor_RejectsExternalTrustSafetyRoutingWithoutExplicitDelegation()
    {
        var options = new NotificationRoutingOptions
        {
            TrustSafetyModerationOwner = NotificationOwnership.ExternalWorkflowProvider,
            ExternalUserFacingModerationProvider = ExternalWorkflowProviderKind.Coop
        };

        await Assert.That(() => CreateResolver(options)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ResolveAsync_AllowsExplicitExternalTrustSafetyDelegationWithAudit()
    {
        var resolver = CreateResolver(new NotificationRoutingOptions
        {
            TrustSafetyModerationOwner = NotificationOwnership.ExternalWorkflowProvider,
            AllowExternalUserFacingModerationEmails = true,
            ExternalUserFacingModerationProvider = ExternalWorkflowProviderKind.Coop
        });

        var decision = await resolver.ResolveAsync(new NotificationIntentDraft(NotificationCategory.TrustSafetyModeration));

        await Assert.That(decision.Ownership).IsEqualTo(NotificationOwnership.ExternalWorkflowProvider);
        await Assert.That(decision.ExternalWorkflowProviderKind).IsEqualTo(ExternalWorkflowProviderKind.Coop);
        await Assert.That(decision.RequiresLocalAudit).IsTrue();
    }

    [Test]
    public async Task ResolveAsync_HonorsCancellationBeforeReturningDecision()
    {
        var resolver = CreateResolver();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.That(async () => await resolver.ResolveAsync(
            new NotificationIntentDraft(NotificationCategory.EventLifecycle),
            source.Token)).Throws<OperationCanceledException>();
    }

    private static DefaultNotificationOwnershipResolver CreateResolver(
        NotificationRoutingOptions? options = null)
    {
        return new DefaultNotificationOwnershipResolver(Options.Create(options ?? new NotificationRoutingOptions()));
    }
}
