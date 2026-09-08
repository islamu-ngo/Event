using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationProviderSubscriptionStateTests
{
    private static readonly DateTime Now = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task BindingSubscriptionBehaviorStoresOnlyProviderWebhookIdAndSecretReference()
    {
        RegistrationProviderBinding binding = Binding();
        Guid secretId = Guid.CreateVersion7();

        binding.SetDraftProvisionedSubscription("watch-1", secretId);

        await Assert.That(binding.ProviderWebhookId).IsEqualTo("watch-1");
        await Assert.That(binding.WebhookSecretBindingId).IsEqualTo(secretId);
        await Assert.That(typeof(RegistrationProviderBinding).GetProperties()
            .Any(property => property.Name.Contains("Expiration", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Checkpoint", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Lease", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    [Test]
    public async Task ClaimSettleRenewAndExpiredLeaseRecoveryUseGenerationFence()
    {
        RegistrationProviderSubscriptionState state = State();
        Guid staleToken = Guid.CreateVersion7();
        state.Claim(staleToken, Now.AddMinutes(1), Now);
        long staleGeneration = state.ProcessingGeneration;

        await Assert.That(() => state.Claim(Guid.CreateVersion7(), Now.AddMinutes(2), Now.AddSeconds(30)))
            .Throws<InvalidOperationException>();
        state.Claim(Guid.CreateVersion7(), Now.AddMinutes(3), Now.AddMinutes(2));
        Guid activeToken = state.LeaseToken!.Value;
        long activeGeneration = state.ProcessingGeneration;

        await Assert.That(() => state.SettleCheckpoint(staleToken, staleGeneration, "sync-2", Now.AddMinutes(2).AddSeconds(1)))
            .Throws<InvalidOperationException>();
        state.RecordNotification(activeToken, activeGeneration, Now.AddMinutes(2).AddSeconds(2));
        state.SettleCheckpoint(activeToken, activeGeneration, "sync-2", Now.AddMinutes(2).AddSeconds(3));

        state.Claim(Guid.CreateVersion7(), Now.AddMinutes(4), Now.AddMinutes(3));
        Guid renewalToken = state.LeaseToken!.Value;
        long renewalGeneration = state.ProcessingGeneration;
        state.MarkRenewalAttempt(renewalToken, renewalGeneration, Now.AddMinutes(3).AddSeconds(1));
        state.MarkRenewalSuccess(renewalToken, renewalGeneration, "watch-2", Now.AddDays(6), Now.AddMinutes(3).AddSeconds(2));

        await Assert.That(state.ResponseCheckpoint).IsEqualTo("sync-2");
        await Assert.That(state.WatchId).IsEqualTo("watch-2");
        await Assert.That(state.LastNotificationAt).IsEqualTo(Now.AddMinutes(2).AddSeconds(2));
        await Assert.That(state.LastRenewalSuccessAt).IsEqualTo(Now.AddMinutes(3).AddSeconds(2));
        await Assert.That(state.LeaseToken).IsNull();
    }

    [Test]
    public async Task FailureCategoryIsBoundedCompactAndClearsLease()
    {
        RegistrationProviderSubscriptionState state = State();
        state.Claim(Guid.CreateVersion7(), Now.AddMinutes(1), Now);
        Guid token = state.LeaseToken!.Value;
        long generation = state.ProcessingGeneration;

        await Assert.That(() => state.Fail(token, generation, "provider timeout", Now.AddSeconds(1)))
            .Throws<ArgumentException>();
        state.Fail(token, generation, "provider_timeout", Now.AddSeconds(2));

        await Assert.That(state.FailureCategory).IsEqualTo("provider_timeout");
        await Assert.That(state.LeaseToken).IsNull();
    }

    [Test]
    public async Task SettledSweepClearsPendingNotificationAndNewNotificationReopensWork()
    {
        RegistrationProviderSubscriptionState state = State();

        state.ReceiveNotification(Now.AddSeconds(1));
        state.Claim(Guid.CreateVersion7(), Now.AddMinutes(1), Now.AddSeconds(2));
        Guid token = state.LeaseToken!.Value;
        long generation = state.ProcessingGeneration;
        state.SettleCheckpoint(token, generation, "sync-2", Now.AddSeconds(3));

        await Assert.That(state.PendingNotificationAt).IsNull();
        await Assert.That(state.LastSweepSuccessAt).IsEqualTo(Now.AddSeconds(3));

        state.ReceiveNotification(Now.AddSeconds(4));

        await Assert.That(state.PendingNotificationAt).IsEqualTo(Now.AddSeconds(4));
    }

    [Test]
    public async Task FailureSetsRetryBackoffAndStaleFenceStillRejects()
    {
        RegistrationProviderSubscriptionState state = State();
        state.ReceiveNotification(Now.AddSeconds(1));
        Guid token = Guid.CreateVersion7();
        state.Claim(token, Now.AddMinutes(1), Now.AddSeconds(2));
        long generation = state.ProcessingGeneration;

        state.Fail(RegistrationProviderSubscriptionOperation.Sweep, token, generation, "provider_timeout", Now.AddMinutes(5), Now.AddSeconds(3));

        await Assert.That(state.NextSweepAttemptAt).IsEqualTo(Now.AddMinutes(5));
        await Assert.That(state.NextRenewalAttemptAt).IsNull();
        await Assert.That(state.LeaseToken).IsNull();
        await Assert.That(() => state.SettleCheckpoint(token, generation, "sync-stale", Now.AddSeconds(4)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SweepAndRenewalFailuresBackOffOnlyTheirOwnLane()
    {
        RegistrationProviderSubscriptionState state = State();
        state.ReceiveNotification(Now.AddSeconds(1));
        state.Claim(Guid.CreateVersion7(), Now.AddMinutes(1), Now.AddSeconds(2));
        Guid sweepToken = state.LeaseToken!.Value;
        long sweepGeneration = state.ProcessingGeneration;

        state.Fail(RegistrationProviderSubscriptionOperation.Sweep, sweepToken, sweepGeneration, "sweep_timeout", Now.AddMinutes(10), Now.AddSeconds(3));

        await Assert.That(state.NextSweepAttemptAt).IsEqualTo(Now.AddMinutes(10));
        await Assert.That(state.NextRenewalAttemptAt).IsNull();

        state.Claim(Guid.CreateVersion7(), Now.AddMinutes(2), Now.AddSeconds(4));
        Guid renewalToken = state.LeaseToken!.Value;
        long renewalGeneration = state.ProcessingGeneration;
        state.Fail(RegistrationProviderSubscriptionOperation.Renewal, renewalToken, renewalGeneration, "renewal_timeout", Now.AddMinutes(20), Now.AddSeconds(5));

        await Assert.That(state.NextSweepAttemptAt).IsEqualTo(Now.AddMinutes(10));
        await Assert.That(state.NextRenewalAttemptAt).IsEqualTo(Now.AddMinutes(20));
    }

    private static RegistrationProviderSubscriptionState State() => RegistrationProviderSubscriptionState.Create(
        Guid.CreateVersion7(), Guid.CreateVersion7(), "google.forms.responses", "watch-1", Now.AddDays(6), "sync-1", Now);

    private static RegistrationProviderBinding Binding() => RegistrationProviderBinding.Create(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        RegistrationProviderPresentationModeEnum.Embed, RegistrationProviderCollectionModeEnum.ProviderHosted,
        RegistrationProviderCompletionModeEnum.Callback, RegistrationProviderTrustLevelEnum.SelectedFields, null, Now);
}
