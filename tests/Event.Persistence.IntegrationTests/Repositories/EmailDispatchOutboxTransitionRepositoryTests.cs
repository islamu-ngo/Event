// ABOUTME: PostgreSQL-backed tests for EmailDispatch operator replay and parking transitions.
// ABOUTME: Verifies durable state-machine changes that future RabbitMQ consumers and admin actions reuse.

using System.Data.Common;
using System.Diagnostics;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Models;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EmailDispatchOutboxTransitionRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task EligibilityRefreshesCurrentVerifiedAddressAndCreatesProviderFenceAtomically()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        await EmailDispatchSqliteFixture.EnableInstanceEmailAsync(context);
        var tenant = await SeedTenantAsync(context, "eligibility-address");
        var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        var user = await context.Users.Include(value => value.Pii).SingleAsync(value => value.Id == dispatch.RecipientUserId);
        user.Email = "current-verified@example.test";
        await context.SaveChangesAsync();
        var repository = new EmailDispatchOutboxRepository(context);
        var leaseToken = Guid.CreateVersion7();
        await Assert.That(await ClaimSpecificAsync(repository, dispatch, leaseToken, DateTime.UtcNow)).IsNotNull();
        context.ChangeTracker.Clear();
        var evaluator = CreateEligibilityEvaluator(context);

        var result = await evaluator.EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(tenant.Id, dispatch.Id, leaseToken),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Eligible);
        await Assert.That(result.RecipientEmail).IsEqualTo("current-verified@example.test");
        var persisted = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(value => value.Id == dispatch.Id);
        var attempt = await context.EmailDispatchAttempts.IgnoreQueryFilters().AsNoTracking().SingleAsync(value => value.EmailDispatchOutboxId == dispatch.Id);
        var receipt = await context.EmailDispatchReceipts.IgnoreQueryFilters().AsNoTracking().SingleAsync(value => value.EmailDispatchOutboxId == dispatch.Id);
        await Assert.That(persisted.RecipientEmail).IsEqualTo("current-verified@example.test");
        await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Processing);
        await Assert.That(attempt.FailureCategory).IsEqualTo("provider_handoff_started");
        await Assert.That(attempt.CompletedAt).IsNull();
        await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Processing);
    }

    [Test]
    public async Task PersistedGlobalRateAdmissionDefersWithoutAttemptOrProviderEvidence()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        await EmailDispatchSqliteFixture.EnableInstanceEmailAsync(context);
        var firstTenant = await SeedTenantAsync(context, "smtp-global-first");
        var first = await SeedDispatchAsync(context, firstTenant.Id, EmailDispatchStatus.Pending);
        var firstLease = Guid.CreateVersion7();
        var repository = new EmailDispatchOutboxRepository(context);
        await Assert.That(await ClaimSpecificAsync(repository, first, firstLease, DateTime.UtcNow)).IsNotNull();
        context.ChangeTracker.Clear();

        var admitted = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(firstTenant.Id, first.Id, firstLease, globalRateLimit: 1, tenantRateLimit: 1),
            CancellationToken.None);

        await Assert.That(admitted.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Eligible);

        var secondTenant = await SeedTenantAsync(context, "smtp-global-second");
        var second = await SeedDispatchAsync(context, secondTenant.Id, EmailDispatchStatus.Pending);
        var secondLease = Guid.CreateVersion7();
        await Assert.That(await ClaimSpecificAsync(repository, second, secondLease, DateTime.UtcNow)).IsNotNull();
        context.ChangeTracker.Clear();

        var deferred = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(secondTenant.Id, second.Id, secondLease, globalRateLimit: 1, tenantRateLimit: 1),
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var persisted = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == second.Id);
        var globalState = await context.EmailDispatchProcessorStates.AsNoTracking()
            .SingleAsync(row => row.ProcessorCode == "smtp");
        await Assert.That(deferred.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.RateDeferred);
        await Assert.That(deferred.RetryAt).IsNotNull();
        await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.RetryScheduled);
        await Assert.That(persisted.AttemptCount).IsEqualTo(0);
        await Assert.That(persisted.LastFailureCategory).IsEqualTo("smtp_rate_deferred");
        await Assert.That(persisted.ProcessingLeaseToken).IsNull();
        await Assert.That(globalState.SmtpAvailableTokens).IsEqualTo(0);
        await Assert.That(await context.EmailDispatchAttempts.IgnoreQueryFilters().CountAsync(row => row.EmailDispatchOutboxId == second.Id)).IsEqualTo(0);
        await Assert.That(await context.EmailDispatchReceipts.IgnoreQueryFilters().CountAsync(row => row.EmailDispatchOutboxId == second.Id)).IsEqualTo(0);
    }

    [Test]
    public async Task PersistedTenantRateAdmissionDoesNotBlockAnotherTenant()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        await EmailDispatchSqliteFixture.EnableInstanceEmailAsync(context);
        var firstTenant = await SeedTenantAsync(context, "smtp-tenant-first");
        var first = await SeedDispatchAsync(context, firstTenant.Id, EmailDispatchStatus.Pending);
        var firstLease = Guid.CreateVersion7();
        var repository = new EmailDispatchOutboxRepository(context);
        await Assert.That(await ClaimSpecificAsync(repository, first, firstLease, DateTime.UtcNow)).IsNotNull();
        context.ChangeTracker.Clear();
        await Assert.That((await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(firstTenant.Id, first.Id, firstLease, globalRateLimit: 3, tenantRateLimit: 1),
            CancellationToken.None)).Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Eligible);

        var sameTenant = await SeedDispatchAsync(context, firstTenant.Id, EmailDispatchStatus.Pending);
        var sameTenantLease = Guid.CreateVersion7();
        await Assert.That(await ClaimSpecificAsync(repository, sameTenant, sameTenantLease, DateTime.UtcNow)).IsNotNull();
        context.ChangeTracker.Clear();
        var tenantDeferred = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(firstTenant.Id, sameTenant.Id, sameTenantLease, globalRateLimit: 3, tenantRateLimit: 1),
            CancellationToken.None);

        var otherTenant = await SeedTenantAsync(context, "smtp-tenant-other");
        var other = await SeedDispatchAsync(context, otherTenant.Id, EmailDispatchStatus.Pending);
        var otherLease = Guid.CreateVersion7();
        await Assert.That(await ClaimSpecificAsync(repository, other, otherLease, DateTime.UtcNow)).IsNotNull();
        context.ChangeTracker.Clear();
        var otherAdmitted = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(otherTenant.Id, other.Id, otherLease, globalRateLimit: 3, tenantRateLimit: 1),
            CancellationToken.None);

        await Assert.That(tenantDeferred.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.RateDeferred);
        await Assert.That(otherAdmitted.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Eligible);
        await Assert.That(await context.EmailDispatchAttempts.IgnoreQueryFilters().CountAsync(row => row.EmailDispatchOutboxId == sameTenant.Id)).IsEqualTo(0);
        await Assert.That(await context.EmailDispatchAttempts.IgnoreQueryFilters().CountAsync(row => row.EmailDispatchOutboxId == other.Id)).IsEqualTo(1);
    }

    [Test]
    public async Task EligibilitySkipsUnverifiedRecipientAndAlignsAllDeliveryLedgersAtomically()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "eligibility-unverified");
        var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        var user = await context.Users.SingleAsync(value => value.Id == dispatch.RecipientUserId);
        user.EmailVerified = false;
        await context.SaveChangesAsync();
        var repository = new EmailDispatchOutboxRepository(context);
        var leaseToken = Guid.CreateVersion7();
        await Assert.That(await ClaimSpecificAsync(repository, dispatch, leaseToken, DateTime.UtcNow)).IsNotNull();
        context.ChangeTracker.Clear();

        var result = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(tenant.Id, dispatch.Id, leaseToken),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Skipped);
        await Assert.That(result.SkipReason).IsEqualTo("recipient_email_unverified");
        var persisted = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(value => value.Id == dispatch.Id);
        var delivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().SingleAsync(value => value.EmailDispatchOutboxId == dispatch.Id);
        var attempt = await context.EmailDispatchAttempts.IgnoreQueryFilters().AsNoTracking().SingleAsync(value => value.EmailDispatchOutboxId == dispatch.Id);
        var receipt = await context.EmailDispatchReceipts.IgnoreQueryFilters().AsNoTracking().SingleAsync(value => value.EmailDispatchOutboxId == dispatch.Id);
        await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Skipped);
        await Assert.That(persisted.LastFailureCategory).IsEqualTo("recipient_email_unverified");
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Skipped);
        await Assert.That(delivery.FailureCategory).IsEqualTo("recipient_email_unverified");
        await Assert.That(attempt.Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Skipped);
        await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Skipped);
    }

    [Test]
    public async Task EligibilitySkipsQueuedReportReceiptWhenRecipientPiiWasErasedBeforeProviderHandoff()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "eligibility-report-receipt-erased-pii");
        var dispatch = await SeedReportReceiptDispatchAsync(context, tenant);
        var repository = new EmailDispatchOutboxRepository(context);
        var leaseToken = Guid.CreateVersion7();
        await Assert.That(await ClaimSpecificAsync(repository, dispatch, leaseToken, DateTime.UtcNow)).IsNotNull();
        await context.UserPii
            .Where(value => value.UserId == dispatch.RecipientUserId)
            .ExecuteDeleteAsync();
        context.ChangeTracker.Clear();

        var result = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(tenant.Id, dispatch.Id, leaseToken),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Skipped);
        await Assert.That(result.RecipientEmail).IsNull();
        await Assert.That(result.SkipReason).IsEqualTo(RecipientEmailAddressResolver.RecipientEmailMissing);
        var persisted = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == dispatch.Id);
        var delivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.EmailDispatchOutboxId == dispatch.Id);
        var attempt = await context.EmailDispatchAttempts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.EmailDispatchOutboxId == dispatch.Id);
        var receipt = await context.EmailDispatchReceipts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.EmailDispatchOutboxId == dispatch.Id);
        await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Skipped);
        await Assert.That(persisted.LastFailureCategory).IsEqualTo(RecipientEmailAddressResolver.RecipientEmailMissing);
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Skipped);
        await Assert.That(delivery.FailureCategory).IsEqualTo(RecipientEmailAddressResolver.RecipientEmailMissing);
        await Assert.That(attempt.Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Skipped);
        await Assert.That(attempt.FailureCategory).IsEqualTo(RecipientEmailAddressResolver.RecipientEmailMissing);
        await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Skipped);
        await Assert.That(await context.EmailDispatchAttempts.IgnoreQueryFilters().AnyAsync(value =>
            value.EmailDispatchOutboxId == dispatch.Id && value.FailureCategory == "provider_handoff_started")).IsFalse();
    }

    [Test]
    public async Task EligibilityHonorsTrustSafetyOptOutForReportReceiptButNotRequiredModeration()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        await EmailDispatchSqliteFixture.EnableInstanceEmailAsync(context);
        var tenant = await SeedTenantAsync(context, "eligibility-trust-safety-policy");
        var reportReceipt = await SeedReportReceiptDispatchAsync(context, tenant);
        var requiredModeration = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        var requiredDelivery = requiredModeration.NotificationIntent!.Deliveries.Single();
        requiredModeration.Kind = EmailDispatchKind.OrganizerNotification;
        requiredModeration.NotificationIntent.CategoryId = (int)NotificationCategoryEnum.TrustSafetyModeration;
        requiredDelivery.IsRequired = true;
        requiredDelivery.DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired;
        requiredDelivery.PreferenceCategoryCode = NotificationPreferenceCategoryCodes.TrustSafety;
        context.NotificationChannelPreferences.AddRange(
            CreateUserEmailPreference(
                tenant.Id,
                reportReceipt.RecipientUserId,
                NotificationPreferenceCategoryEnum.TrustSafety,
                false),
            CreateUserEmailPreference(
                tenant.Id,
                requiredModeration.RecipientUserId,
                NotificationPreferenceCategoryEnum.TrustSafety,
                false));
        await context.SaveChangesAsync();

        var repository = new EmailDispatchOutboxRepository(context);
        var reportLeaseToken = Guid.CreateVersion7();
        await Assert.That(await ClaimSpecificAsync(
            repository,
            reportReceipt,
            reportLeaseToken,
            DateTime.UtcNow)).IsNotNull();
        context.ChangeTracker.Clear();
        var reportResult = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(tenant.Id, reportReceipt.Id, reportLeaseToken),
            CancellationToken.None);

        var moderationLeaseToken = Guid.CreateVersion7();
        await Assert.That(await ClaimSpecificAsync(
            repository,
            requiredModeration,
            moderationLeaseToken,
            DateTime.UtcNow)).IsNotNull();
        context.ChangeTracker.Clear();
        var moderationResult = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(tenant.Id, requiredModeration.Id, moderationLeaseToken),
            CancellationToken.None);

        await Assert.That(reportResult.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Skipped);
        await Assert.That(reportResult.SkipReason).IsEqualTo("recipient_notification_preference_disabled");
        await Assert.That(moderationResult.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Eligible);
        await Assert.That(moderationResult.RecipientEmail).IsEqualTo(requiredModeration.RecipientEmail);
        var reportOutbox = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == reportReceipt.Id);
        var moderationOutbox = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == requiredModeration.Id);
        await Assert.That(reportOutbox.Status).IsEqualTo(EmailDispatchStatus.Skipped);
        await Assert.That(moderationOutbox.Status).IsEqualTo(EmailDispatchStatus.Processing);
        await Assert.That(await context.EmailDispatchAttempts.IgnoreQueryFilters().AnyAsync(value =>
            value.EmailDispatchOutboxId == reportReceipt.Id && value.FailureCategory == "provider_handoff_started")).IsFalse();
        await Assert.That(await context.EmailDispatchAttempts.IgnoreQueryFilters().AnyAsync(value =>
            value.EmailDispatchOutboxId == requiredModeration.Id && value.FailureCategory == "provider_handoff_started")).IsTrue();
    }

    [Test]
    public async Task EligibilitySkipsSupersededDeliveryBeforeProviderHandoff()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "eligibility-superseded");
        var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        var delivery = await context.NotificationDeliveries.SingleAsync(value => value.EmailDispatchOutboxId == dispatch.Id);
        delivery.StatusId = (int)NotificationDeliveryStatusEnum.Superseded;
        await context.SaveChangesAsync();
        var repository = new EmailDispatchOutboxRepository(context);
        var leaseToken = Guid.CreateVersion7();
        await Assert.That(await ClaimSpecificAsync(repository, dispatch, leaseToken, DateTime.UtcNow)).IsNotNull();
        context.ChangeTracker.Clear();

        var result = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(tenant.Id, dispatch.Id, leaseToken),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Skipped);
        await Assert.That(result.SkipReason).IsEqualTo("delivery_superseded");
    }

    [Test]
    public async Task EligibilityDefersTenantPauseWithoutConsumingAttemptBudget()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "eligibility-paused");
        var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        var repository = new EmailDispatchOutboxRepository(context);
        var leaseToken = Guid.CreateVersion7();
        await Assert.That(await ClaimSpecificAsync(repository, dispatch, leaseToken, DateTime.UtcNow)).IsNotNull();
        await repository.SetTenantPauseState(tenant.Id, true, "maintenance", null, DateTime.UtcNow, CancellationToken.None);
        context.ChangeTracker.Clear();

        var result = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(tenant.Id, dispatch.Id, leaseToken),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.TenantPaused);
        var persisted = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(value => value.Id == dispatch.Id);
        await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Pending);
        await Assert.That(persisted.AttemptCount).IsEqualTo(0);
        await Assert.That(await context.EmailDispatchAttempts.IgnoreQueryFilters().CountAsync(value => value.EmailDispatchOutboxId == dispatch.Id)).IsEqualTo(0);
    }

    [Test]
    public async Task EligibilitySkipsInactiveTenantMembershipBeforeProviderHandoff()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "eligibility-membership");
        var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        var membership = await context.TenantUsers.SingleAsync(value =>
            value.TenantId == tenant.Id && value.UserId == dispatch.RecipientUserId);
        membership.StatusId = (int)TenantUserStatusEnum.Suspended;
        await context.SaveChangesAsync();
        var repository = new EmailDispatchOutboxRepository(context);
        var leaseToken = Guid.CreateVersion7();
        await Assert.That(await ClaimSpecificAsync(repository, dispatch, leaseToken, DateTime.UtcNow)).IsNotNull();
        context.ChangeTracker.Clear();

        var result = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(tenant.Id, dispatch.Id, leaseToken),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Skipped);
        await Assert.That(result.SkipReason).IsEqualTo("recipient_membership_inactive");
        await Assert.That(await context.EmailDispatchAttempts.IgnoreQueryFilters().AnyAsync(value =>
            value.EmailDispatchOutboxId == dispatch.Id && value.FailureCategory == "provider_handoff_started")).IsFalse();
    }

    [Test]
    public async Task EligibilityFailsClosedWhenPersistedPolicyVersionDrifts()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "eligibility-policy-version");
        var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        var delivery = await context.NotificationDeliveries.SingleAsync(value => value.EmailDispatchOutboxId == dispatch.Id);
        delivery.PolicyVersion = 2;
        await context.SaveChangesAsync();
        var repository = new EmailDispatchOutboxRepository(context);
        var leaseToken = Guid.CreateVersion7();
        await Assert.That(await ClaimSpecificAsync(repository, dispatch, leaseToken, DateTime.UtcNow)).IsNotNull();
        context.ChangeTracker.Clear();

        var result = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(tenant.Id, dispatch.Id, leaseToken),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Skipped);
        await Assert.That(result.SkipReason).IsEqualTo("delivery_policy_version_unsupported");
    }

    [Test]
    public async Task EligibilitySkipsOptionalDeliveryWhenRecipientUnsubscribedAfterQueueing()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "eligibility-preference");
        var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        context.UserNotificationPreferences.Add(new UserNotificationPreference
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = dispatch.RecipientUserId,
            Category = NotificationPreferenceCategories.RegistrationConfirmations,
            IsEnabled = false,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var repository = new EmailDispatchOutboxRepository(context);
        var leaseToken = Guid.CreateVersion7();
        await Assert.That(await ClaimSpecificAsync(repository, dispatch, leaseToken, DateTime.UtcNow)).IsNotNull();
        context.ChangeTracker.Clear();

        var result = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(tenant.Id, dispatch.Id, leaseToken),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Skipped);
        await Assert.That(result.SkipReason).IsEqualTo("recipient_unsubscribed");
    }

    [Test]
    public async Task EligibilitySkipsRegistrationCancellationKindsWhenRecipientDisabledEventUpdates()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "eligibility-registration-cancellation");

        foreach (var kind in new[] { EmailDispatchKind.RegistrationCancelled, EmailDispatchKind.RegistrationRevoked })
        {
            var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
            dispatch.Kind = kind;
            context.UserNotificationPreferences.Add(new UserNotificationPreference
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = null!,
                UserId = dispatch.RecipientUserId,
                Category = NotificationPreferenceCategories.EventUpdates,
                IsEnabled = false,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var repository = new EmailDispatchOutboxRepository(context);
            var leaseToken = Guid.CreateVersion7();
            await Assert.That(await ClaimSpecificAsync(repository, dispatch, leaseToken, DateTime.UtcNow)).IsNotNull();
            context.ChangeTracker.Clear();

            var result = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
                CreateEligibilityRequest(tenant.Id, dispatch.Id, leaseToken),
                CancellationToken.None);

            await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Skipped);
            await Assert.That(result.SkipReason).IsEqualTo("recipient_unsubscribed");
        }
    }

    [Test]
    public async Task EligibilitySkipsRetiredManagedInvitationDespiteAdministratorAuthority()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "eligibility-invitation");
        var invitedAddress = "authorized-invitation@example.test";
        var dispatch = await SeedDispatchAsync(
            context,
            tenant.Id,
            EmailDispatchStatus.Pending,
            managedInvitation: true,
            invitationEmail: invitedAddress);
        var repository = new EmailDispatchOutboxRepository(context);
        var leaseToken = Guid.CreateVersion7();
        await Assert.That(await ClaimSpecificAsync(repository, dispatch, leaseToken, DateTime.UtcNow)).IsNotNull();
        context.ChangeTracker.Clear();

        var result = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(tenant.Id, dispatch.Id, leaseToken),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Skipped);
        await Assert.That(result.RecipientEmail).IsNull();
        await Assert.That(result.SkipReason).IsEqualTo("managed_administrator_invitation_retired");
        var skipped = (await repository.GetByTenantAndId(tenant.Id, dispatch.Id, CancellationToken.None))!;
        await Assert.That(skipped.Status).IsEqualTo(EmailDispatchStatus.Skipped);
        await Assert.That(skipped.ProcessingLeaseToken).IsNull();
        await Assert.That(skipped.RecipientEmail).IsEqualTo(invitedAddress);
    }

    [Test]
    public async Task RetentionRedactionClearsParentAndChildContentAfterSentCutoff()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var utcNow = AtPostgresPrecision(DateTime.UtcNow);
        var settledAt = utcNow.AddDays(-181);
        var graph = await SeedAcceptedSettlementGraphAsync(context, "retention-sent", settledAt);
        var repository = new EmailDispatchOutboxRepository(context);
        await repository.SettleProviderAccepted(
            new EmailDispatchAcceptedSettlement(
                graph.Dispatch.TenantId,
                graph.Dispatch.Id,
                graph.Dispatch.ProcessingLeaseToken!.Value,
                graph.Attempt.AttemptNumber,
                settledAt,
                "provider-message-retained"),
            CancellationToken.None);

        var dispatch = await context.EmailDispatchOutbox.IgnoreQueryFilters().SingleAsync(row => row.Id == graph.Dispatch.Id);
        var attempt = await context.EmailDispatchAttempts.IgnoreQueryFilters().SingleAsync(row => row.Id == graph.Attempt.Id);
        var receipt = await context.EmailDispatchReceipts.IgnoreQueryFilters().SingleAsync(row => row.Id == graph.Receipt.Id);
        dispatch.ReplyTo = "reply@example.test";
        dispatch.PlainTextBody = "private plain content";
        dispatch.HtmlBody = "<p>private html content</p>";
        attempt.SanitizedErrorMessage = "retained attempt detail";
        receipt.FailureMessage = "retained receipt detail";
        await context.SaveChangesAsync();

        var redacted = await repository.RedactRetentionEligible(
            graph.Dispatch.TenantId,
            utcNow.AddDays(-180),
            utcNow,
            10,
            CancellationToken.None);

        context.ChangeTracker.Clear();
        dispatch = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == graph.Dispatch.Id);
        attempt = await context.EmailDispatchAttempts.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == graph.Attempt.Id);
        receipt = await context.EmailDispatchReceipts.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == graph.Receipt.Id);
        await Assert.That(redacted).IsEqualTo(1);
        await Assert.That(dispatch.RecipientEmail).IsEmpty();
        await Assert.That(dispatch.Subject).IsEmpty();
        await Assert.That(dispatch.PlainTextBody).IsNull();
        await Assert.That(dispatch.HtmlBody).IsNull();
        await Assert.That(dispatch.ReplyTo).IsNull();
        await Assert.That(dispatch.ContentRedactedAt).IsEqualTo(utcNow);
        await Assert.That(attempt.SanitizedErrorMessage).IsNull();
        await Assert.That(attempt.ProviderMessageId).IsNull();
        await Assert.That(receipt.FailureMessage).IsNull();
        await Assert.That(receipt.ProviderMessageId).IsNull();
    }

    [Test]
    public async Task RetentionRedactionWaitsForExplicitResolutionAndThenBlocksReplay()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "retention-resolution");
        var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.DeadLettered);
        var repository = new EmailDispatchOutboxRepository(context);
        var utcNow = AtPostgresPrecision(DateTime.UtcNow);

        var unresolvedCount = await repository.CountRetentionRedactionEligible(
            tenant.Id,
            utcNow.AddDays(-180),
            10,
            CancellationToken.None);
        var resolved = await repository.TryResolveWithoutReplay(
            tenant.Id,
            dispatch.Id,
            "No replay required after operator review.",
            Guid.CreateVersion7(),
            utcNow.AddDays(-181),
            CancellationToken.None);
        var redacted = await repository.RedactRetentionEligible(
            tenant.Id,
            utcNow.AddDays(-180),
            utcNow,
            10,
            CancellationToken.None);
        var replayed = await repository.TryReplayForOperator(
            tenant.Id,
            dispatch.Id,
            Guid.CreateVersion7(),
            utcNow,
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var persisted = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == dispatch.Id);
        await Assert.That(unresolvedCount).IsEqualTo(0);
        await Assert.That(resolved).IsTrue();
        await Assert.That(redacted).IsEqualTo(1);
        await Assert.That(replayed).IsFalse();
        await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Skipped);
        await Assert.That(persisted.LastFailureCategory).IsEqualTo("operator_resolved_without_replay");
        await Assert.That(persisted.ContentRedactedAt).IsEqualTo(utcNow);
    }

    [Test]
    public async Task RetentionRedactionImmediatelySuppressesPurgedTenantWork()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "retention-purged");
        var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        tenant.TenantStatusId = (int)TenantStatusEnum.Purged;
        await context.SaveChangesAsync();
        var repository = new EmailDispatchOutboxRepository(context);
        var utcNow = AtPostgresPrecision(DateTime.UtcNow);

        var redacted = await repository.SuppressAndRedactTenant(
            tenant.Id,
            Guid.CreateVersion7(),
            utcNow,
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var persisted = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == dispatch.Id);
        var delivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.EmailDispatchOutboxId == dispatch.Id);
        await Assert.That(redacted).IsEqualTo(1);
        await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Skipped);
        await Assert.That(persisted.LastFailureCategory).IsEqualTo("tenant_deleted");
        await Assert.That(persisted.ContentRedactedAt).IsEqualTo(utcNow);
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Skipped);
        await Assert.That(delivery.FailureCategory).IsEqualTo("tenant_deleted");
    }

    [Test]
    public async Task ClaimPendingBatchUsesTenantRoundsAndPrioritizesRequiredWork()
    {
        await fixture.ResetAsync();
        var baseline = new PersistenceQueryBaselineInterceptor();
        await using var context = fixture.CreateDbContext(baseline);
        var now = DateTime.UtcNow;
        var tenantA = await SeedTenantAsync(context, "fair-a");
        var tenantB = await SeedTenantAsync(context, "fair-b");
        var tenantC = await SeedTenantAsync(context, "fair-c");
        var tenantAOldest = await SeedDispatchAsync(context, tenantA.Id, EmailDispatchStatus.Pending);
        var tenantASecond = await SeedDispatchAsync(context, tenantA.Id, EmailDispatchStatus.Pending);
        var tenantBOnly = await SeedDispatchAsync(context, tenantB.Id, EmailDispatchStatus.Pending);
        var reminder = await SeedDispatchAsync(context, tenantC.Id, EmailDispatchStatus.Pending);
        var required = await SeedDispatchAsync(context, tenantC.Id, EmailDispatchStatus.Pending);

        tenantAOldest.CreatedAt = now.AddHours(-5);
        tenantASecond.CreatedAt = now.AddHours(-4);
        tenantBOnly.CreatedAt = now.AddHours(-1);
        reminder.Kind = EmailDispatchKind.EventReminder;
        reminder.CreatedAt = now.AddHours(-6);
        required.Kind = EmailDispatchKind.OrganizerNotification;
        required.CreatedAt = now.AddMinutes(-30);
        var requiredDelivery = await context.NotificationDeliveries
            .SingleAsync(row => row.EmailDispatchOutboxId == required.Id);
        requiredDelivery.IsRequired = true;
        requiredDelivery.DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired;
        await context.SaveChangesAsync();

        baseline.Reset();
        var repository = new EmailDispatchOutboxRepository(context);
        var elapsed = Stopwatch.StartNew();
        var batch = await repository.ClaimPendingBatchAsync(
            CreateBatchClaimRequest(now, batchSize: 3, maxRowsPerTenant: 1),
            CancellationToken.None);
        elapsed.Stop();
        PersistenceQueryBaselineEvidence.Record(baseline
            .Snapshot("email_claim_pending_batch", batch.Count, elapsed.Elapsed));

        await Assert.That(batch.Select(row => row.TenantId).Distinct().Count()).IsEqualTo(3);
        await Assert.That(batch.Count(row => row.TenantId == tenantA.Id)).IsEqualTo(1);
        await Assert.That(batch.Single(row => row.TenantId == tenantA.Id).Id).IsEqualTo(tenantAOldest.Id);
        await Assert.That(batch.Single(row => row.TenantId == tenantC.Id).Id).IsEqualTo(required.Id);
        await Assert.That(batch.Select(row => row.Id)).DoesNotContain(reminder.Id);
        await Assert.That(batch.All(row => row.Status == EmailDispatchStatus.Processing)).IsTrue();
    }

    [Test]
    public async Task ConcurrentClaimersReceiveDisjointRows()
    {
        await fixture.ResetAsync();
        await using (var seedContext = fixture.CreateDbContext())
        {
            var tenantA = await SeedTenantAsync(seedContext, "claim-disjoint-a");
            var tenantB = await SeedTenantAsync(seedContext, "claim-disjoint-b");
            for (var index = 0; index < 3; index++)
            {
                await SeedDispatchAsync(seedContext, tenantA.Id, EmailDispatchStatus.Pending);
                await SeedDispatchAsync(seedContext, tenantB.Id, EmailDispatchStatus.Pending);
            }
        }

        await using var nodeAContext = fixture.CreateDbContext();
        await using var nodeBContext = fixture.CreateDbContext();
        var nodeARepository = new EmailDispatchOutboxRepository(nodeAContext);
        var nodeBRepository = new EmailDispatchOutboxRepository(nodeBContext);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var now = DateTime.UtcNow;
        var nodeATask = ClaimAfterSignalAsync(nodeARepository, CreateBatchClaimRequest(now, batchSize: 3), start.Task);
        var nodeBTask = ClaimAfterSignalAsync(nodeBRepository, CreateBatchClaimRequest(now, batchSize: 3), start.Task);

        start.SetResult();
        var claims = await Task.WhenAll(nodeATask, nodeBTask);

        await Assert.That(claims[0].Count).IsEqualTo(3);
        await Assert.That(claims[1].Count).IsEqualTo(3);
        await Assert.That(claims[0].Select(row => row.Id).Intersect(claims[1].Select(row => row.Id))).IsEmpty();
    }

    [Test]
    public async Task ClaimPendingBatchHonorsPreexistingGlobalProcessingCeiling()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenantA = await SeedTenantAsync(context, "claim-global-a");
        var tenantB = await SeedTenantAsync(context, "claim-global-b");
        await SeedDispatchAsync(context, tenantA.Id, EmailDispatchStatus.Processing);
        await SeedDispatchAsync(context, tenantB.Id, EmailDispatchStatus.Processing);
        await SeedDispatchAsync(context, tenantA.Id, EmailDispatchStatus.Pending);
        await SeedDispatchAsync(context, tenantB.Id, EmailDispatchStatus.Pending);

        var claimed = await new EmailDispatchOutboxRepository(context).ClaimPendingBatchAsync(
            CreateBatchClaimRequest(DateTime.UtcNow, batchSize: 10, globalLimit: 3),
            CancellationToken.None);

        await Assert.That(claimed.Count).IsEqualTo(1);
        await Assert.That(await context.EmailDispatchOutbox.IgnoreQueryFilters()
            .CountAsync(row => row.Status == EmailDispatchStatus.Processing)).IsEqualTo(3);
    }

    [Test]
    public async Task ClaimPendingBatchHonorsPreexistingPerTenantProcessingCeiling()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenantA = await SeedTenantAsync(context, "claim-tenant-a");
        var tenantB = await SeedTenantAsync(context, "claim-tenant-b");
        await SeedDispatchAsync(context, tenantA.Id, EmailDispatchStatus.Processing);
        await SeedDispatchAsync(context, tenantA.Id, EmailDispatchStatus.Pending);
        await SeedDispatchAsync(context, tenantA.Id, EmailDispatchStatus.Pending);
        await SeedDispatchAsync(context, tenantB.Id, EmailDispatchStatus.Pending);
        await SeedDispatchAsync(context, tenantB.Id, EmailDispatchStatus.Pending);

        var claimed = await new EmailDispatchOutboxRepository(context).ClaimPendingBatchAsync(
            CreateBatchClaimRequest(DateTime.UtcNow, batchSize: 10, tenantLimit: 2),
            CancellationToken.None);

        await Assert.That(claimed.Count(row => row.TenantId == tenantA.Id)).IsEqualTo(1);
        await Assert.That(claimed.Count(row => row.TenantId == tenantB.Id)).IsEqualTo(2);
        await Assert.That(await context.EmailDispatchOutbox.IgnoreQueryFilters()
            .CountAsync(row => row.TenantId == tenantA.Id && row.Status == EmailDispatchStatus.Processing)).IsEqualTo(2);
    }

    [Test]
    public async Task TryClaimSpecificHonorsGlobalAndPerTenantProcessingCeilings()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenantA = await SeedTenantAsync(context, "claim-specific-a");
        var tenantB = await SeedTenantAsync(context, "claim-specific-b");
        await SeedDispatchAsync(context, tenantA.Id, EmailDispatchStatus.Processing);
        var tenantATarget = await SeedDispatchAsync(context, tenantA.Id, EmailDispatchStatus.Pending);
        var tenantBTarget = await SeedDispatchAsync(context, tenantB.Id, EmailDispatchStatus.Pending);
        var repository = new EmailDispatchOutboxRepository(context);
        var claimedAt = DateTime.UtcNow;

        var tenantLimited = await repository.TryClaimSpecificAsync(
            CreateSpecificClaimRequest(tenantATarget, claimedAt, globalLimit: 2, tenantLimit: 1),
            CancellationToken.None);
        var globallyLimited = await repository.TryClaimSpecificAsync(
            CreateSpecificClaimRequest(tenantBTarget, claimedAt, globalLimit: 1, tenantLimit: 1),
            CancellationToken.None);

        await Assert.That(tenantLimited).IsNull();
        await Assert.That(globallyLimited).IsNull();
        await Assert.That(await context.EmailDispatchOutbox.IgnoreQueryFilters()
            .CountAsync(row => row.Status == EmailDispatchStatus.Processing)).IsEqualTo(1);
    }

    [Test]
    public async Task OptionalReminderHysteresisPersistsAcrossRepositoryContexts()
    {
        await fixture.ResetAsync();
        Guid reminderId;
        Guid[] coreIds;
        var now = DateTime.UtcNow;
        await using (var firstContext = fixture.CreateDbContext())
        {
            var tenant = await SeedTenantAsync(firstContext, "claim-hysteresis");
            var coreA = await SeedDispatchAsync(firstContext, tenant.Id, EmailDispatchStatus.Pending);
            var coreB = await SeedDispatchAsync(firstContext, tenant.Id, EmailDispatchStatus.Pending);
            var reminder = await SeedDispatchAsync(firstContext, tenant.Id, EmailDispatchStatus.Pending);
            reminder.Kind = EmailDispatchKind.EventReminder;
            await firstContext.SaveChangesAsync();
            reminderId = reminder.Id;
            coreIds = [coreA.Id, coreB.Id];

            await new EmailDispatchOutboxRepository(firstContext).ClaimPendingBatchAsync(
                CreateBatchClaimRequest(now, batchSize: 1, highWatermark: 2, lowWatermark: 1),
                CancellationToken.None);
        }

        await using (var settlementContext = fixture.CreateDbContext())
        {
            var coreRows = await settlementContext.EmailDispatchOutbox.IgnoreQueryFilters()
                .Where(row => coreIds.Contains(row.Id))
                .ToListAsync();
            foreach (var row in coreRows)
            {
                row.Status = EmailDispatchStatus.Sent;
                row.SentAt = now;
                row.ProcessingStartedAt = null;
                row.ProcessingLeaseToken = null;
            }
            await settlementContext.SaveChangesAsync();
        }

        await using var resumedContext = fixture.CreateDbContext();
        var resumedClaim = await new EmailDispatchOutboxRepository(resumedContext).ClaimPendingBatchAsync(
            CreateBatchClaimRequest(now.AddMinutes(1), batchSize: 1, highWatermark: 2, lowWatermark: 1),
            CancellationToken.None);

        await Assert.That(resumedClaim.Single().Id).IsEqualTo(reminderId);
        await Assert.That((await resumedContext.EmailDispatchProcessorStates.AsNoTracking().SingleAsync()).OptionalRemindersDeferred).IsFalse();
    }

    [Test]
    public async Task PausedTenantBacklogDoesNotTriggerOptionalReminderSuppression()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var pausedTenant = await SeedTenantAsync(context, "claim-paused");
        var activeTenant = await SeedTenantAsync(context, "claim-active");
        for (var index = 0; index < 3; index++)
        {
            await SeedDispatchAsync(context, pausedTenant.Id, EmailDispatchStatus.Pending);
        }
        var reminder = await SeedDispatchAsync(context, activeTenant.Id, EmailDispatchStatus.Pending);
        reminder.Kind = EmailDispatchKind.EventReminder;
        await context.SaveChangesAsync();
        var repository = new EmailDispatchOutboxRepository(context);
        await repository.SetTenantPauseState(pausedTenant.Id, true, "maintenance", null, DateTime.UtcNow, CancellationToken.None);

        var claimed = await repository.ClaimPendingBatchAsync(
            CreateBatchClaimRequest(DateTime.UtcNow, batchSize: 1, highWatermark: 2, lowWatermark: 1),
            CancellationToken.None);

        await Assert.That(claimed.Single().Id).IsEqualTo(reminder.Id);
        await Assert.That((await context.EmailDispatchProcessorStates.AsNoTracking().SingleAsync()).OptionalRemindersDeferred).IsFalse();
    }

    [Test]
    public async Task RequiredReminderRemainsEligibleWhileOptionalRemindersAreDeferred()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "claim-required-reminder");
        var core = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        var requiredReminder = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        var optionalReminder = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        requiredReminder.Kind = EmailDispatchKind.EventReminder;
        optionalReminder.Kind = EmailDispatchKind.EventReminder;
        var requiredDelivery = await context.NotificationDeliveries.SingleAsync(row => row.EmailDispatchOutboxId == requiredReminder.Id);
        requiredDelivery.IsRequired = true;
        requiredDelivery.DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired;
        core.CreatedAt = DateTime.UtcNow.AddMinutes(-2);
        requiredReminder.CreatedAt = DateTime.UtcNow.AddMinutes(-1);
        optionalReminder.CreatedAt = DateTime.UtcNow.AddMinutes(-3);
        await context.SaveChangesAsync();

        var claimed = await new EmailDispatchOutboxRepository(context).ClaimPendingBatchAsync(
            CreateBatchClaimRequest(DateTime.UtcNow, batchSize: 1, highWatermark: 2, lowWatermark: 1),
            CancellationToken.None);

        await Assert.That(claimed.Single().Id).IsEqualTo(requiredReminder.Id);
        await Assert.That((await context.EmailDispatchProcessorStates.AsNoTracking().SingleAsync()).OptionalRemindersDeferred).IsTrue();
        await Assert.That((await context.EmailDispatchOutbox.IgnoreQueryFilters().SingleAsync(row => row.Id == optionalReminder.Id)).Status)
            .IsEqualTo(EmailDispatchStatus.Pending);
    }

    [Test]
    public async Task RepeatingBatchClaimWithSameLeaseTokenReturnsOriginalRowsWithoutExtraAttempts()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "claim-idempotent");
        await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        var repository = new EmailDispatchOutboxRepository(context);
        var request = CreateBatchClaimRequest(DateTime.UtcNow, batchSize: 2);

        var first = await repository.ClaimPendingBatchAsync(request, CancellationToken.None);
        var replay = await repository.ClaimPendingBatchAsync(request, CancellationToken.None);

        await Assert.That(replay.Select(row => row.Id).Order()).IsEquivalentTo(first.Select(row => row.Id).Order());
        await Assert.That(replay.All(row => row.AttemptCount == 0)).IsTrue();
    }

    [Test]
    public async Task TryParkForOperatorMarksEligibleRowAsParked()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "park");
        var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.DeadLettered);
        var actorId = Guid.NewGuid();
        var parkedAt = DateTime.UtcNow;
        var repository = new EmailDispatchOutboxRepository(context);

        var parked = await repository.TryParkForOperator(
            tenant.Id,
            dispatch.Id,
            "operator quarantine",
            actorId,
            parkedAt,
            CancellationToken.None);

        await Assert.That(parked).IsTrue();

        var row = await context.EmailDispatchOutbox
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(outbox => outbox.Id == dispatch.Id);
        await Assert.That(row.Status).IsEqualTo(EmailDispatchStatus.Parked);
        await Assert.That(row.ParkedAt).IsNotNull();
        await Assert.That(Math.Abs((row.ParkedAt.Value - parkedAt).TotalMilliseconds)).IsLessThan(5);
        await Assert.That(row.LastFailureCategory).IsEqualTo("operator_parked");
        await Assert.That(row.LastError).IsEqualTo("operator quarantine");
        await Assert.That(row.UpdatedBy).IsEqualTo(actorId);
        await Assert.That(row.NextAttemptAt).IsNull();
        await Assert.That(row.ProcessingLeaseToken).IsNull();
    }

    [Test]
    public async Task TryReplayForOperatorResetsDeferredRowToPending()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "replay");
        var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.DeadLettered);
        dispatch.RabbitMqLastPublishedAt = DateTime.UtcNow.AddMinutes(-10);
        dispatch.RabbitMqLastPublishAttemptAt = DateTime.UtcNow.AddMinutes(-10);
        dispatch.RabbitMqPublishAttemptCount = 3;
        dispatch.RabbitMqLastPublishFailureCategory = "publisher_nack";
        await context.SaveChangesAsync();
        var actorId = Guid.NewGuid();
        var replayAt = DateTime.UtcNow;
        var repository = new EmailDispatchOutboxRepository(context);

        var replayed = await repository.TryReplayForOperator(
            tenant.Id,
            dispatch.Id,
            actorId,
            replayAt,
            CancellationToken.None);

        await Assert.That(replayed).IsTrue();

        var row = await context.EmailDispatchOutbox
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(outbox => outbox.Id == dispatch.Id);
        await Assert.That(row.Status).IsEqualTo(EmailDispatchStatus.Pending);
        await Assert.That(row.NextAttemptAt).IsNull();
        await Assert.That(row.DeadLetteredAt).IsNull();
        await Assert.That(row.ParkedAt).IsNull();
        await Assert.That(row.UnknownAt).IsNull();
        await Assert.That(row.LastFailureCategory).IsNull();
        await Assert.That(row.LastError).IsNull();
        await Assert.That(row.RabbitMqLastPublishedAt).IsNull();
        await Assert.That(row.RabbitMqLastPublishAttemptAt).IsNull();
        await Assert.That(row.RabbitMqPublishAttemptCount).IsEqualTo(0);
        await Assert.That(row.RabbitMqLastPublishFailureCategory).IsNull();
        await Assert.That(row.UpdatedBy).IsEqualTo(actorId);
        var delivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.EmailDispatchOutboxId == dispatch.Id);
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Queued);
    }

    [Test]
    public async Task TryReplayForOperatorDoesNotReplaySentRow()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "sent");
        var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Sent);
        var repository = new EmailDispatchOutboxRepository(context);

        var replayed = await repository.TryReplayForOperator(
            tenant.Id,
            dispatch.Id,
            Guid.NewGuid(),
            DateTime.UtcNow,
            CancellationToken.None);

        await Assert.That(replayed).IsFalse();

        var row = await context.EmailDispatchOutbox
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(outbox => outbox.Id == dispatch.Id);
        await Assert.That(row.Status).IsEqualTo(EmailDispatchStatus.Sent);
    }

    [Test]
    public async Task TryParkForOperatorDoesNotParkSkippedRow()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "skipped-park");
        var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Skipped);
        var repository = new EmailDispatchOutboxRepository(context);

        var parked = await repository.TryParkForOperator(
            tenant.Id,
            dispatch.Id,
            "manual review",
            Guid.NewGuid(),
            DateTime.UtcNow,
            CancellationToken.None);

        await Assert.That(parked).IsFalse();

        var row = await context.EmailDispatchOutbox
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(outbox => outbox.Id == dispatch.Id);
        await Assert.That(row.Status).IsEqualTo(EmailDispatchStatus.Skipped);
        await Assert.That(row.ParkedAt).IsNull();
    }

    [Test]
    public async Task GetByTenantAndPublishEventIdReturnsOnlyMatchingTenantRow()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "lookup");
        var otherTenant = await SeedTenantAsync(context, "lookup-other");
        var dispatch = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        await SeedDispatchAsync(context, otherTenant.Id, EmailDispatchStatus.Pending);
        var repository = new EmailDispatchOutboxRepository(context);

        var found = await repository.GetByTenantAndPublishEventId(
            tenant.Id,
            dispatch.PublishEventId,
            CancellationToken.None);
        var wrongTenant = await repository.GetByTenantAndPublishEventId(
            otherTenant.Id,
            dispatch.PublishEventId,
            CancellationToken.None);

        await Assert.That(found).IsNotNull();
        await Assert.That(found!.Id).IsEqualTo(dispatch.Id);
        await Assert.That(found.TenantId).IsEqualTo(tenant.Id);
        await Assert.That(wrongTenant).IsNull();
    }

    [Test]
    public async Task GetRabbitMqPublishBatchReturnsDueUnpausedRowsOnly()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var activeTenant = await SeedTenantAsync(context, "rabbitmq-active");
        var pausedTenant = await SeedTenantAsync(context, "rabbitmq-paused");
        var now = DateTime.UtcNow;
        var eligible = await SeedDispatchAsync(context, activeTenant.Id, EmailDispatchStatus.Pending);
        var throttled = await SeedDispatchAsync(context, activeTenant.Id, EmailDispatchStatus.Pending);
        var deferred = await SeedDispatchAsync(context, activeTenant.Id, EmailDispatchStatus.RetryScheduled);
        var sent = await SeedDispatchAsync(context, activeTenant.Id, EmailDispatchStatus.Sent);
        var paused = await SeedDispatchAsync(context, pausedTenant.Id, EmailDispatchStatus.Pending);
        throttled.RabbitMqLastPublishAttemptAt = now.AddSeconds(-5);
        deferred.NextAttemptAt = now.AddMinutes(10);
        context.EmailDispatchTenantControls.Add(new EmailDispatchTenantControl
        {
            Id = Guid.NewGuid(),
            TenantId = pausedTenant.Id,
            IsPaused = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();
        var repository = new EmailDispatchOutboxRepository(context);

        IReadOnlyList<EmailDispatchOutbox> rows = await repository.GetRabbitMqPublishBatch(
            10,
            now,
            now.AddSeconds(-30),
            CancellationToken.None);

        await Assert.That(rows.Select(row => row.Id)).IsEquivalentTo([eligible.Id]);
        await Assert.That(rows.Select(row => row.Id)).DoesNotContain(throttled.Id);
        await Assert.That(rows.Select(row => row.Id)).DoesNotContain(deferred.Id);
        await Assert.That(rows.Select(row => row.Id)).DoesNotContain(sent.Id);
        await Assert.That(rows.Select(row => row.Id)).DoesNotContain(paused.Id);
    }

    [Test]
    public async Task RabbitMqPublishMarkersUpdateProducerMetadata()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "rabbitmq-markers");
        var success = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        var failure = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        var repository = new EmailDispatchOutboxRepository(context);
        var publishedAt = DateTime.UtcNow.AddMinutes(-2);
        var failedAt = DateTime.UtcNow;

        await repository.MarkRabbitMqPublishSucceeded(success.Id, publishedAt, CancellationToken.None);
        await repository.MarkRabbitMqPublishFailed(failure.Id, "mandatory_return", failedAt, CancellationToken.None);

        var rows = await context.EmailDispatchOutbox
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(outbox => outbox.Id == success.Id || outbox.Id == failure.Id)
            .ToDictionaryAsync(outbox => outbox.Id);
        await Assert.That(rows[success.Id].RabbitMqLastPublishedAt).IsNotNull();
        await Assert.That(Math.Abs((rows[success.Id].RabbitMqLastPublishedAt!.Value - publishedAt).TotalMilliseconds)).IsLessThan(10);
        await Assert.That(rows[success.Id].RabbitMqLastPublishAttemptAt).IsNotNull();
        await Assert.That(rows[success.Id].RabbitMqPublishAttemptCount).IsEqualTo(1);
        await Assert.That(rows[success.Id].RabbitMqLastPublishFailureCategory).IsNull();
        await Assert.That(rows[failure.Id].RabbitMqLastPublishedAt).IsNull();
        await Assert.That(rows[failure.Id].RabbitMqLastPublishAttemptAt).IsNotNull();
        await Assert.That(rows[failure.Id].RabbitMqPublishAttemptCount).IsEqualTo(1);
        await Assert.That(rows[failure.Id].RabbitMqLastPublishFailureCategory).IsEqualTo("mandatory_return");
    }

    [Test]
    public async Task HealthAggregatesExposeActiveStatesAndExcludePausedTenants()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "health");
        var otherTenant = await SeedTenantAsync(context, "health-other");
        var pausedTenant = await SeedTenantAsync(context, "health-paused");
        var now = AtPostgresPrecision(DateTime.UtcNow);
        var activePending = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Pending);
        var dueRetry = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.RetryScheduled);
        var futureRetry = await SeedDispatchAsync(context, otherTenant.Id, EmailDispatchStatus.RetryScheduled);
        await SeedProcessingDispatchWithReceiptAsync(context, tenant.Id, now.AddMinutes(-30));
        await SeedProcessingDispatchWithReceiptAsync(context, otherTenant.Id, now.AddMinutes(-2));
        await SeedDispatchAsync(context, otherTenant.Id, EmailDispatchStatus.DeadLettered);
        await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Unknown);
        await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Parked);
        await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Sent);
        var pausedPending = await SeedDispatchAsync(context, pausedTenant.Id, EmailDispatchStatus.Pending);
        await SeedDispatchAsync(context, pausedTenant.Id, EmailDispatchStatus.RetryScheduled);
        await SeedProcessingDispatchWithReceiptAsync(context, pausedTenant.Id, now.AddHours(-1));
        await SeedDispatchAsync(context, pausedTenant.Id, EmailDispatchStatus.DeadLettered);
        await SeedDispatchAsync(context, pausedTenant.Id, EmailDispatchStatus.Unknown);
        await SeedDispatchAsync(context, pausedTenant.Id, EmailDispatchStatus.Parked);
        activePending.CreatedAt = now.AddMinutes(-10);
        pausedPending.CreatedAt = now.AddHours(-2);
        dueRetry.NextAttemptAt = now.AddMinutes(-5);
        futureRetry.NextAttemptAt = now.AddMinutes(30);
        context.EmailDispatchTenantControls.Add(new EmailDispatchTenantControl
        {
            Id = Guid.CreateVersion7(),
            TenantId = pausedTenant.Id,
            IsPaused = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        context.EmailDispatchProcessorStates.Add(new EmailDispatchProcessorState
        {
            Id = Guid.CreateVersion7(),
            ProcessorCode = "smtp",
            OptionalRemindersDeferred = true,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();
        var repository = new EmailDispatchOutboxRepository(context);

        var dueDispatchCount = await repository.CountDueDispatchAsync(now, CancellationToken.None);
        var retryScheduledCount = await repository.CountRetryScheduledAsync(CancellationToken.None);
        var staleProcessingCount = await repository.CountStaleProcessingAsync(
            now.AddMinutes(-10),
            CancellationToken.None);
        var deadLetteredCount = await repository.CountDeadLetteredAsync(CancellationToken.None);
        var unknownCount = await repository.CountUnknownAsync(CancellationToken.None);
        var parkedCount = await repository.CountParkedAsync(CancellationToken.None);
        var oldestDue = await repository.GetOldestDueCreatedAtAsync(now, CancellationToken.None);
        var tenantBacklog = await repository.CountDueDispatchByTenantAsync(now, 10, CancellationToken.None);
        var optionalReminderDeferralActive = await repository.IsOptionalReminderDeferralActiveAsync(CancellationToken.None);

        await Assert.That(dueDispatchCount).IsEqualTo(2);
        await Assert.That(retryScheduledCount).IsEqualTo(2);
        await Assert.That(staleProcessingCount).IsEqualTo(1);
        await Assert.That(deadLetteredCount).IsEqualTo(1);
        await Assert.That(unknownCount).IsEqualTo(1);
        await Assert.That(parkedCount).IsEqualTo(1);
        await Assert.That(oldestDue).IsEqualTo(activePending.CreatedAt);
        await Assert.That(tenantBacklog).IsEquivalentTo(new Dictionary<Guid, int> { [tenant.Id] = 2 });
        await Assert.That(optionalReminderDeferralActive).IsTrue();
    }

    [Test]
    public async Task RecoverStaleProcessingRetriesOnlyExpiredUnfencedClaims()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "stale-processing");
        var staleStartedAt = DateTime.UtcNow.AddMinutes(-30);
        var freshStartedAt = DateTime.UtcNow.AddMinutes(-2);
        var stale = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Processing);
        stale.ProcessingStartedAt = staleStartedAt;
        var fresh = await SeedDispatchAsync(context, tenant.Id, EmailDispatchStatus.Processing);
        fresh.ProcessingStartedAt = freshStartedAt;
        await context.SaveChangesAsync();
        var recoveredAt = DateTime.UtcNow;
        var repository = new EmailDispatchOutboxRepository(context);

        var recovered = await repository.RecoverStaleProcessing(
            new EmailDispatchStaleRecoveryRequest(
                DateTime.UtcNow.AddMinutes(-10),
                recoveredAt,
                "processing_lease_released",
                "lease expired before provider handoff",
                "processing_lease_expired",
                "provider handoff lease expired",
                10),
            CancellationToken.None);

        await Assert.That(recovered.RetryScheduledCount).IsEqualTo(1);
        await Assert.That(recovered.UnknownCount).IsEqualTo(0);

        var rows = await context.EmailDispatchOutbox
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(outbox => outbox.Id == stale.Id || outbox.Id == fresh.Id)
            .ToDictionaryAsync(outbox => outbox.Id);
        await Assert.That(rows[stale.Id].Status).IsEqualTo(EmailDispatchStatus.RetryScheduled);
        await Assert.That(rows[stale.Id].UnknownAt).IsNull();
        await Assert.That(rows[stale.Id].NextAttemptAt).IsNotNull();
        await Assert.That(rows[stale.Id].ProcessingLeaseToken).IsNull();
        await Assert.That(rows[stale.Id].ProcessingStartedAt).IsNull();
        await Assert.That(rows[stale.Id].LastFailureCategory).IsEqualTo("processing_lease_released");
        await Assert.That(rows[fresh.Id].Status).IsEqualTo(EmailDispatchStatus.Processing);
        await Assert.That(rows[fresh.Id].ProcessingStartedAt).IsNotNull();
        await Assert.That(await context.EmailDispatchAttempts.IgnoreQueryFilters().CountAsync(row => row.EmailDispatchOutboxId == stale.Id)).IsEqualTo(0);
        await Assert.That(await context.EmailDispatchReceipts.IgnoreQueryFilters().CountAsync(row => row.EmailDispatchOutboxId == stale.Id)).IsEqualTo(0);
    }

    [Test]
    public async Task RetryableProviderFailureRequeuesDeliveryAndAllowsNextProviderHandoff()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        await EmailDispatchSqliteFixture.EnableInstanceEmailAsync(context);
        var graph = await SeedAcceptedSettlementGraphAsync(context, "retryable-settlement", DateTime.UtcNow);
        var repository = new EmailDispatchOutboxRepository(context);
        var firstLease = graph.Dispatch.ProcessingLeaseToken!.Value;
        var firstSettledAt = DateTime.UtcNow;

        var firstOutcome = await repository.SettleProviderFailure(
            new EmailDispatchFailureSettlement(
                TenantId: graph.Dispatch.TenantId,
                OutboxId: graph.Dispatch.Id,
                ProcessingLeaseToken: firstLease,
                AttemptNumber: graph.Attempt.AttemptNumber,
                Outcome: SmtpDeliveryOutcome.TransientFailure,
                RetryDelay: TimeSpan.Zero,
                MaxAttempts: 3,
                SettledAt: firstSettledAt),
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var retryOutbox = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Dispatch.Id);
        var retryDelivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Delivery.Id);
        await Assert.That(firstOutcome).IsEqualTo(EmailDispatchFailureSettlementOutcome.RetryScheduled);
        await Assert.That(retryOutbox.Status).IsEqualTo(EmailDispatchStatus.RetryScheduled);
        await Assert.That(retryOutbox.AttemptCount).IsEqualTo(1);
        await Assert.That(retryDelivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Queued);
        await Assert.That(retryDelivery.ProviderStatus).IsEqualTo("retry_scheduled");
        await Assert.That(retryDelivery.CompletedAt).IsNull();

        var secondLease = Guid.CreateVersion7();
        var claimed = await ClaimSpecificAsync(
            repository,
            graph.Dispatch,
            secondLease,
            firstSettledAt.AddSeconds(1));
        await Assert.That(claimed).IsNotNull();
        context.ChangeTracker.Clear();

        var secondEligibility = await CreateEligibilityEvaluator(context).EvaluateAndBeginProviderHandoffAsync(
            CreateEligibilityRequest(
                graph.Dispatch.TenantId,
                graph.Dispatch.Id,
                secondLease,
                attemptNumber: 1),
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var secondOutbox = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Dispatch.Id);
        var secondAttempt = await context.EmailDispatchAttempts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.EmailDispatchOutboxId == graph.Dispatch.Id && row.AttemptNumber == 2);
        var secondReceipt = await context.EmailDispatchReceipts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.EmailDispatchOutboxId == graph.Dispatch.Id);
        await Assert.That(secondEligibility.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Eligible);
        await Assert.That(secondEligibility.AttemptNumber).IsEqualTo(2);
        await Assert.That(secondOutbox.Status).IsEqualTo(EmailDispatchStatus.Processing);
        await Assert.That(secondOutbox.AttemptCount).IsEqualTo(2);
        await Assert.That(secondAttempt.FailureCategory).IsEqualTo("provider_handoff_started");
        await Assert.That(secondReceipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Processing);
    }

    [Test]
    public async Task ExhaustedProviderFailureLeavesTerminalDeliveryDeadLettered()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var graph = await SeedAcceptedSettlementGraphAsync(context, "exhausted-settlement", DateTime.UtcNow);
        var repository = new EmailDispatchOutboxRepository(context);
        var settledAt = DateTime.UtcNow;

        var outcome = await repository.SettleProviderFailure(
            new EmailDispatchFailureSettlement(
                TenantId: graph.Dispatch.TenantId,
                OutboxId: graph.Dispatch.Id,
                ProcessingLeaseToken: graph.Dispatch.ProcessingLeaseToken!.Value,
                AttemptNumber: graph.Attempt.AttemptNumber,
                Outcome: SmtpDeliveryOutcome.TransientFailure,
                RetryDelay: TimeSpan.Zero,
                MaxAttempts: 1,
                SettledAt: settledAt),
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var outbox = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Dispatch.Id);
        var delivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Delivery.Id);
        await Assert.That(outcome).IsEqualTo(EmailDispatchFailureSettlementOutcome.DeadLettered);
        await Assert.That(outbox.Status).IsEqualTo(EmailDispatchStatus.DeadLettered);
        await Assert.That(outbox.NextAttemptAt).IsNull();
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.DeadLettered);
        await Assert.That(delivery.ProviderStatus).IsEqualTo("dead_lettered");
        await Assert.That(delivery.CompletedAt).IsNotNull();
        await Assert.That(await ClaimSpecificAsync(repository, graph.Dispatch, Guid.CreateVersion7(), settledAt.AddMinutes(1))).IsNull();
    }

    [Test]
    public async Task SettleProviderAcceptedAtomicallyAlignsAttemptReceiptOutboxAndEmailDelivery()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var graph = await SeedAcceptedSettlementGraphAsync(context, "accepted-settlement", DateTime.UtcNow);
        var repository = new EmailDispatchOutboxRepository(context);
        var settledAt = DateTime.UtcNow;
        var settlement = new EmailDispatchAcceptedSettlement(
            graph.Dispatch.TenantId,
            graph.Dispatch.Id,
            graph.Dispatch.ProcessingLeaseToken!.Value,
            graph.Attempt.AttemptNumber,
            settledAt,
            "provider-message-accepted");

        await repository.SettleProviderAccepted(settlement, CancellationToken.None);

        context.ChangeTracker.Clear();
        var attempt = await context.EmailDispatchAttempts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Attempt.Id);
        var receipt = await context.EmailDispatchReceipts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Receipt.Id);
        var dispatch = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Dispatch.Id);
        var delivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Delivery.Id);
        var due = await repository.CountDueDispatchAsync(DateTime.UtcNow.AddHours(1), CancellationToken.None);

        await Assert.That(attempt.Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Succeeded);
        await Assert.That(attempt.ProviderMessageId).IsEqualTo("provider-message-accepted");
        await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Completed);
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Sent);
        await Assert.That(dispatch.NextAttemptAt).IsNull();
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Delivered);
        await Assert.That(delivery.ProviderStatus).IsEqualTo("accepted");
        await Assert.That(due).IsEqualTo(0);
    }

    [Test]
    public async Task ReconcileProviderAcceptedConvertsPartialSettlementToSanitizedUnknownWithoutRetry()
    {
        const string canary = "provider-canary attendee@example.test body-canary";
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var graph = await SeedAcceptedSettlementGraphAsync(context, "partial-settlement", DateTime.UtcNow);
        graph.Attempt.Outcome = EmailDispatchAttemptOutcome.Succeeded;
        graph.Attempt.CompletedAt = DateTime.UtcNow;
        graph.Attempt.FailureCategory = null;
        graph.Attempt.SanitizedErrorMessage = null;
        graph.Attempt.ProviderMessageId = canary;
        graph.Receipt.Status = EmailDispatchReceiptStatus.Completed;
        graph.Receipt.CompletedAt = DateTime.UtcNow;
        graph.Receipt.ProviderMessageId = canary;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new EmailDispatchOutboxRepository(context);
        var settlement = new EmailDispatchAcceptedSettlement(
            graph.Dispatch.TenantId,
            graph.Dispatch.Id,
            graph.Dispatch.ProcessingLeaseToken!.Value,
            graph.Attempt.AttemptNumber,
            DateTime.UtcNow,
            canary);

        EmailDispatchAcceptedReconciliationOutcome outcome = await repository.ReconcileProviderAccepted(
            settlement,
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var attempt = await context.EmailDispatchAttempts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Attempt.Id);
        var receipt = await context.EmailDispatchReceipts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Receipt.Id);
        var dispatch = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Dispatch.Id);
        var delivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Delivery.Id);
        var due = await repository.CountDueDispatchAsync(DateTime.UtcNow.AddHours(1), CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(EmailDispatchAcceptedReconciliationOutcome.Unknown);
        await Assert.That(attempt.Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Unknown);
        await Assert.That(attempt.SanitizedErrorMessage).DoesNotContain(canary);
        await Assert.That(attempt.ProviderMessageId).IsNull();
        await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Unknown);
        await Assert.That(receipt.FailureMessage).DoesNotContain(canary);
        await Assert.That(receipt.ProviderMessageId).IsNull();
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Unknown);
        await Assert.That(dispatch.LastError).DoesNotContain(canary);
        await Assert.That(dispatch.ProviderMessageId).IsNull();
        await Assert.That(dispatch.NextAttemptAt).IsNull();
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Unknown);
        await Assert.That(delivery.ProviderMessageId).IsNull();
        await Assert.That(due).IsEqualTo(0);
    }

    [Test]
    public async Task OperatorReconciliationAlignsUnknownGraphAsDelivered()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var graph = await SeedAcceptedSettlementGraphAsync(context, "operator-reconcile-delivered", DateTime.UtcNow);
        await MakeGraphUnknownAsync(context, graph);
        var repository = new EmailDispatchOutboxRepository(context);

        bool reconciled = await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            bool result = await repository.TryReconcileUnknown(
                graph.Dispatch.TenantId,
                graph.Dispatch.Id,
                EmailDispatchUnknownReconciliationOutcome.Delivered,
                "provider log confirms acceptance",
                "provider-confirmed-id",
                Guid.NewGuid(),
                DateTime.UtcNow,
                CancellationToken.None);
            await transaction.CommitAsync();
            return result;
        });

        context.ChangeTracker.Clear();
        await Assert.That(reconciled).IsTrue();
        await Assert.That((await context.EmailDispatchOutbox.IgnoreQueryFilters().SingleAsync(row => row.Id == graph.Dispatch.Id)).Status)
            .IsEqualTo(EmailDispatchStatus.Sent);
        await Assert.That((await context.EmailDispatchAttempts.IgnoreQueryFilters().SingleAsync(row => row.Id == graph.Attempt.Id)).Outcome)
            .IsEqualTo(EmailDispatchAttemptOutcome.Succeeded);
        await Assert.That((await context.EmailDispatchReceipts.IgnoreQueryFilters().SingleAsync(row => row.Id == graph.Receipt.Id)).Status)
            .IsEqualTo(EmailDispatchReceiptStatus.Completed);
        await Assert.That((await context.NotificationDeliveries.IgnoreQueryFilters().SingleAsync(row => row.Id == graph.Delivery.Id)).StatusId)
            .IsEqualTo((int)NotificationDeliveryStatusEnum.Delivered);
    }

    [Test]
    public async Task OperatorReconciliationAlignsUnknownGraphAsNotDeliveredAndQueued()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var graph = await SeedAcceptedSettlementGraphAsync(context, "operator-reconcile-not-delivered", DateTime.UtcNow);
        await MakeGraphUnknownAsync(context, graph);
        var repository = new EmailDispatchOutboxRepository(context);

        bool reconciled = await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            bool result = await repository.TryReconcileUnknown(
                graph.Dispatch.TenantId,
                graph.Dispatch.Id,
                EmailDispatchUnknownReconciliationOutcome.NotDelivered,
                "provider log confirms no acceptance",
                null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                CancellationToken.None);
            await transaction.CommitAsync();
            return result;
        });

        context.ChangeTracker.Clear();
        await Assert.That(reconciled).IsTrue();
        await Assert.That((await context.EmailDispatchOutbox.IgnoreQueryFilters().SingleAsync(row => row.Id == graph.Dispatch.Id)).Status)
            .IsEqualTo(EmailDispatchStatus.Pending);
        await Assert.That((await context.EmailDispatchReceipts.IgnoreQueryFilters().SingleAsync(row => row.Id == graph.Receipt.Id)).Status)
            .IsEqualTo(EmailDispatchReceiptStatus.Received);
        await Assert.That((await context.NotificationDeliveries.IgnoreQueryFilters().SingleAsync(row => row.Id == graph.Delivery.Id)).StatusId)
            .IsEqualTo((int)NotificationDeliveryStatusEnum.Queued);
    }

    [Test]
    public async Task ReconcileProviderAcceptedRecognizesAlreadyCommittedAlignedSentGraph()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var graph = await SeedAcceptedSettlementGraphAsync(context, "committed-settlement", DateTime.UtcNow);
        var repository = new EmailDispatchOutboxRepository(context);
        var settlement = new EmailDispatchAcceptedSettlement(
            graph.Dispatch.TenantId,
            graph.Dispatch.Id,
            graph.Dispatch.ProcessingLeaseToken!.Value,
            graph.Attempt.AttemptNumber,
            DateTime.UtcNow,
            "provider-message-committed");
        await repository.SettleProviderAccepted(settlement, CancellationToken.None);
        context.ChangeTracker.Clear();

        EmailDispatchAcceptedReconciliationOutcome outcome = await repository.ReconcileProviderAccepted(
            settlement,
            CancellationToken.None);

        var dispatch = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Dispatch.Id);
        var delivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Delivery.Id);
        await Assert.That(outcome).IsEqualTo(EmailDispatchAcceptedReconciliationOutcome.Sent);
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Sent);
        await Assert.That(dispatch.UnknownAt).IsNull();
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Delivered);
    }

    [Test]
    public async Task RecoverStaleProcessingAlignsFencedRecipientDeliveryGraphAsUnknown()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var processingStartedAt = DateTime.UtcNow.AddMinutes(-30);
        var graph = await SeedAcceptedSettlementGraphAsync(context, "stale-fenced", processingStartedAt);
        graph.Attempt.Outcome = EmailDispatchAttemptOutcome.Failed;
        graph.Attempt.CompletedAt = processingStartedAt.AddMinutes(-5);
        graph.Attempt.FailureCategory = "previous_attempt_failed";
        graph.Attempt.SanitizedErrorMessage = "Previous SMTP attempt failed before provider acceptance.";
        graph.Dispatch.AttemptCount = 2;
        var currentAttempt = new EmailDispatchAttempt
        {
            Id = Guid.CreateVersion7(),
            TenantId = graph.Dispatch.TenantId,
            EmailDispatchOutboxId = graph.Dispatch.Id,
            AttemptNumber = 2,
            Outcome = EmailDispatchAttemptOutcome.Unknown,
            StartedAt = processingStartedAt,
            FailureCategory = "provider_handoff_started",
            SanitizedErrorMessage = "SMTP provider handoff started; automatic resend is suppressed until settlement.",
            CreatedAt = processingStartedAt
        };
        context.EmailDispatchAttempts.Add(currentAttempt);
        await context.SaveChangesAsync();
        var repository = new EmailDispatchOutboxRepository(context);
        var recoveredAt = DateTime.UtcNow;

        var recovered = await repository.RecoverStaleProcessing(
            new EmailDispatchStaleRecoveryRequest(
                DateTime.UtcNow.AddMinutes(-10),
                recoveredAt,
                "processing_lease_released",
                "Provider handoff had not started; retry is safe.",
                "processing_lease_expired",
                "Provider handoff lease expired; automatic resend is disabled.",
                10),
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var attempts = await context.EmailDispatchAttempts.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.EmailDispatchOutboxId == graph.Dispatch.Id)
            .ToDictionaryAsync(row => row.AttemptNumber);
        var receipt = await context.EmailDispatchReceipts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Receipt.Id);
        var dispatch = await context.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Dispatch.Id);
        var delivery = await context.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Delivery.Id);
        var due = await repository.CountDueDispatchAsync(DateTime.UtcNow.AddHours(1), CancellationToken.None);

        await Assert.That(recovered.RetryScheduledCount).IsEqualTo(0);
        await Assert.That(recovered.UnknownCount).IsEqualTo(1);
        await Assert.That(attempts[1].Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Failed);
        await Assert.That(attempts[1].FailureCategory).IsEqualTo("previous_attempt_failed");
        await Assert.That(attempts[2].Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Unknown);
        await Assert.That(attempts[2].FailureCategory).IsEqualTo("processing_lease_expired");
        await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Unknown);
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Unknown);
        await Assert.That(dispatch.NextAttemptAt).IsNull();
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Unknown);
        await Assert.That(due).IsEqualTo(0);
    }

    [Test]
    [Arguments("email_dispatch_attempts")]
    [Arguments("email_dispatch_receipts")]
    [Arguments("email_dispatch_outbox")]
    [Arguments("notification_deliveries")]
    public async Task AcceptedSettlementStageFailureRollsBackThenFreshContextAlignsUnknown(string failingTable)
    {
        await fixture.ResetAsync();
        AcceptedSettlementGraph graph;
        await using (var seedContext = fixture.CreateDbContext())
        {
            graph = await SeedAcceptedSettlementGraphAsync(seedContext, $"fault-{failingTable}", DateTime.UtcNow);
        }

        var settlement = new EmailDispatchAcceptedSettlement(
            graph.Dispatch.TenantId,
            graph.Dispatch.Id,
            graph.Dispatch.ProcessingLeaseToken!.Value,
            graph.Attempt.AttemptNumber,
            DateTime.UtcNow,
            "provider-message-accepted");
        await using (var failingContext = CreateDbContext(new SettlementStageFailureInterceptor(failingTable)))
        {
            var failingRepository = new EmailDispatchOutboxRepository(failingContext);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                failingRepository.SettleProviderAccepted(settlement, CancellationToken.None));
        }

        await using var reconciliationContext = fixture.CreateDbContext();
        var reconciliationRepository = new EmailDispatchOutboxRepository(reconciliationContext);
        var outcome = await reconciliationRepository.ReconcileProviderAccepted(settlement, CancellationToken.None);

        var attempt = await reconciliationContext.EmailDispatchAttempts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Attempt.Id);
        var receipt = await reconciliationContext.EmailDispatchReceipts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Receipt.Id);
        var dispatch = await reconciliationContext.EmailDispatchOutbox.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Dispatch.Id);
        var delivery = await reconciliationContext.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(row => row.Id == graph.Delivery.Id);

        await Assert.That(outcome).IsEqualTo(EmailDispatchAcceptedReconciliationOutcome.Unknown);
        await Assert.That(attempt.Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Unknown);
        await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Unknown);
        await Assert.That(dispatch.Status).IsEqualTo(EmailDispatchStatus.Unknown);
        await Assert.That(dispatch.NextAttemptAt).IsNull();
        await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Unknown);
    }

    private static async Task<Tenant> SeedTenantAsync(ExploreDbContext context, string slugPrefix)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            FullName = $"Email Dispatch {slugPrefix}",
            Slug = $"email-dispatch-{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };

        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant;
    }

    private static EmailDispatchBatchClaimRequest CreateBatchClaimRequest(
        DateTime now,
        int batchSize,
        int maxRowsPerTenant = 5,
        int globalLimit = 20,
        int tenantLimit = 5,
        int highWatermark = 100,
        int lowWatermark = 50) =>
        new(
            Guid.CreateVersion7(),
            batchSize,
            maxRowsPerTenant,
            globalLimit,
            tenantLimit,
            highWatermark,
            lowWatermark,
            now);

    private static EmailDispatchEligibilityRequest CreateEligibilityRequest(
        Guid tenantId,
        Guid outboxId,
        Guid leaseToken,
        int attemptNumber = 0,
        int globalRateLimit = 120,
        int tenantRateLimit = 30) =>
        new(
            tenantId,
            outboxId,
            leaseToken,
            attemptNumber,
            globalRateLimit,
            tenantRateLimit,
            "test-worker",
            DateTime.UtcNow);

    private static EmailDispatchSpecificClaimRequest CreateSpecificClaimRequest(
        EmailDispatchOutbox dispatch,
        DateTime claimedAt,
        int globalLimit,
        int tenantLimit) =>
        new(
            dispatch.TenantId,
            dispatch.PublishEventId,
            Guid.CreateVersion7(),
            globalLimit,
            tenantLimit,
            100,
            50,
            claimedAt);

    private static async Task<IReadOnlyList<EmailDispatchOutbox>> ClaimAfterSignalAsync(
        EmailDispatchOutboxRepository repository,
        EmailDispatchBatchClaimRequest request,
        Task start)
    {
        await start;
        return await repository.ClaimPendingBatchAsync(request, CancellationToken.None);
    }

    private static Task<EmailDispatchOutbox?> ClaimSpecificAsync(
        EmailDispatchOutboxRepository repository,
        EmailDispatchOutbox dispatch,
        Guid leaseToken,
        DateTime claimedAt) =>
        repository.TryClaimSpecificAsync(
            new EmailDispatchSpecificClaimRequest(
                dispatch.TenantId,
                dispatch.PublishEventId,
                leaseToken,
                20,
                5,
                100,
                50,
                claimedAt),
            CancellationToken.None);

    private ExploreDbContext CreateDbContext(DbCommandInterceptor interceptor)
        => fixture.CreateDbContext(interceptor);

    private static async Task<AcceptedSettlementGraph> SeedAcceptedSettlementGraphAsync(
        ExploreDbContext context,
        string slugPrefix,
        DateTime processingStartedAt)
    {
        var tenant = await SeedTenantAsync(context, slugPrefix);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"{slugPrefix}@example.test",
                FirstName = "Email",
                LastName = "Recipient"
            },
            EmailVerified = true,
            CreatedAt = processingStartedAt
        };
        var tenantUser = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = processingStartedAt,
            CreatedAt = processingStartedAt
        };
        var intent = new NotificationIntent
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            CategoryId = (int)NotificationCategoryEnum.RegistrationLifecycle,
            OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
            RecipientKindId = (int)NotificationRecipientKindEnum.User,
            StatusId = (int)NotificationIntentStatusEnum.DispatchQueued,
            TemplateKey = "registration.confirmed",
            DeduplicationKey = $"accepted-settlement:{slugPrefix}:{Guid.CreateVersion7()}",
            RecipientUserId = user.Id,
            RecipientTenantUser = tenantUser,
            CreatedAt = processingStartedAt
        };
        var dispatch = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            PublishEventId = Guid.CreateVersion7(),
            Kind = EmailDispatchKind.RegistrationConfirmation,
            SourceType = "notification_intent",
            SourceId = intent.Id,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            RecipientUserId = user.Id,
            RecipientTenantUser = tenantUser,
            RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            RecipientEmail = user.Email,
            Subject = "Registration confirmation",
            PlainTextBody = "Registration confirmed.",
            Status = EmailDispatchStatus.Processing,
            AttemptCount = 1,
            MaxAttempts = 5,
            ProcessingStartedAt = processingStartedAt,
            ProcessingLeaseToken = Guid.CreateVersion7(),
            CreatedAt = processingStartedAt,
            UpdatedAt = processingStartedAt
        };
        var delivery = new NotificationDelivery
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            ChannelId = (int)NotificationPreferenceChannelEnum.Email,
            DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.RegistrationStatusOptional,
            IsRequired = false,
            PolicyVersion = 1,
            PreferenceCategoryCode = NotificationPreferenceCategoryCodes.RegistrationStatus,
            RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            DisclosureLevel = "standard",
            TemplateKey = intent.TemplateKey,
            TemplateVersion = 1,
            LinkAllowed = true,
            EmailDispatchOutboxId = dispatch.Id,
            EmailDispatchOutbox = dispatch,
            StatusId = (int)NotificationDeliveryStatusEnum.Queued,
            ProviderStatus = "queued",
            QueuedAt = processingStartedAt,
            CreatedAt = processingStartedAt
        };
        var attempt = new EmailDispatchAttempt
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            EmailDispatchOutboxId = dispatch.Id,
            EmailDispatchOutbox = dispatch,
            AttemptNumber = 1,
            Outcome = EmailDispatchAttemptOutcome.Unknown,
            StartedAt = processingStartedAt,
            FailureCategory = "provider_handoff_started",
            SanitizedErrorMessage = "SMTP provider handoff started; automatic resend is suppressed until settlement.",
            CreatedAt = processingStartedAt
        };
        var receipt = CreateReceipt(
            tenant.Id,
            dispatch.PublishEventId,
            dispatch.Id,
            "scheduler-node");
        receipt.Id = Guid.CreateVersion7();
        receipt.EmailDispatchOutbox = dispatch;
        receipt.FirstSeenAt = processingStartedAt;
        receipt.ProcessingStartedAt = processingStartedAt;
        receipt.CreatedAt = processingStartedAt;

        context.Users.Add(user);
        context.TenantUsers.Add(tenantUser);
        context.NotificationIntents.Add(intent);
        context.EmailDispatchOutbox.Add(dispatch);
        context.NotificationDeliveries.Add(delivery);
        context.EmailDispatchAttempts.Add(attempt);
        context.EmailDispatchReceipts.Add(receipt);
        await context.SaveChangesAsync();
        return new AcceptedSettlementGraph(dispatch, attempt, receipt, delivery);
    }

    private static async Task<EmailDispatchOutbox> SeedDispatchAsync(
        ExploreDbContext context,
        Guid tenantId,
        EmailDispatchStatus status,
        bool managedInvitation = false,
        string? invitationEmail = null)
    {
        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"recipient-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Email",
                LastName = "Recipient",
            },
            EmailVerified = true,
            CreatedAt = now,
        };
        var tenantUser = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            UserId = user.Id,
            User = user,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = now,
            CreatedAt = now,
        };
        var intent = new NotificationIntent
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            CategoryId = (int)NotificationCategoryEnum.RegistrationLifecycle,
            OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
            RecipientKindId = (int)NotificationRecipientKindEnum.User,
            StatusId = (int)NotificationIntentStatusEnum.DispatchQueued,
            TemplateKey = "registration.confirmed",
            DeduplicationKey = $"transition-seed:{Guid.CreateVersion7():N}",
            RecipientUserId = user.Id,
            RecipientTenantUser = tenantUser,
            CreatedAt = now,
        };
        ManagedTenantProvisioningOperation? operation = null;
        if (managedInvitation)
        {
            operation = new ManagedTenantProvisioningOperation
            {
                Id = Guid.CreateVersion7(),
                ManagedInstanceId = Guid.CreateVersion7(),
                ExternalRequestId = $"request-{Guid.CreateVersion7():N}",
                ExternalCustomerReference = $"customer-{Guid.CreateVersion7():N}",
                RequestHash = new string('a', 64),
                RequestJson = null,
                TenantSlug = "managed-invitation",
                CurrentOutboxMessageId = Guid.CreateVersion7(),
                Status = ManagedTenantProvisioningStatus.Succeeded,
                TenantId = tenantId,
                TenantAdministratorUserId = user.Id,
                CompletedAt = now,
                CreatedAt = now
            };
        }

        var recipientAddressSource = managedInvitation
            ? RecipientAddressSource.ManagedTenantAdministratorInvitation
            : RecipientAddressSource.TenantUserVerifiedEmail;
        var dispatch = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            PublishEventId = Guid.CreateVersion7(),
            Kind = managedInvitation
                ? EmailDispatchKind.TenantAdministratorInvitation
                : EmailDispatchKind.RegistrationConfirmation,
            SourceType = managedInvitation ? "managed_tenant_provisioning" : "notification_intent",
            SourceId = operation?.Id ?? intent.Id,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            RecipientUserId = user.Id,
            RecipientTenantUser = tenantUser,
            RecipientAddressSource = recipientAddressSource,
            ManagedTenantProvisioningOperationId = operation?.Id,
            RecipientEmail = managedInvitation ? invitationEmail! : user.Email,
            Subject = "Registration confirmation",
            PlainTextBody = "plain body",
            HtmlBody = "<p>html body</p>",
            Status = status,
            AttemptCount = status == EmailDispatchStatus.Pending ? 0 : 3,
            MaxAttempts = 5,
            NextAttemptAt = status == EmailDispatchStatus.RetryScheduled ? now.AddHours(1) : null,
            ProcessingStartedAt = status == EmailDispatchStatus.Processing ? now : null,
            ProcessingLeaseToken = status == EmailDispatchStatus.Processing ? Guid.CreateVersion7() : null,
            DeadLetteredAt = status == EmailDispatchStatus.DeadLettered ? now : null,
            ParkedAt = status == EmailDispatchStatus.Parked ? now : null,
            UnknownAt = status == EmailDispatchStatus.Unknown ? now : null,
            SentAt = status == EmailDispatchStatus.Sent ? now : null,
            LastFailureCategory = status == EmailDispatchStatus.Pending ? null : "smtp_send_failed",
            LastError = status == EmailDispatchStatus.Pending ? null : "previous failure",
            LastFailureAt = status == EmailDispatchStatus.Pending ? null : now,
            CreatedAt = now,
            UpdatedAt = now
        };
        var delivery = new NotificationDelivery
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            ChannelId = (int)NotificationPreferenceChannelEnum.Email,
            DeliveryPolicyId = managedInvitation
                ? (int)NotificationDeliveryPolicyEnum.TenantAdministrationRequired
                : (int)NotificationDeliveryPolicyEnum.RegistrationStatusOptional,
            IsRequired = managedInvitation,
            PolicyVersion = 1,
            PreferenceCategoryCode = managedInvitation ? null : NotificationPreferenceCategoryCodes.RegistrationStatus,
            RecipientAddressSource = recipientAddressSource,
            DisclosureLevel = "standard",
            TemplateKey = intent.TemplateKey,
            TemplateVersion = 1,
            LinkAllowed = false,
            EmailDispatchOutboxId = dispatch.Id,
            EmailDispatchOutbox = dispatch,
            StatusId = status switch
            {
                EmailDispatchStatus.Sent => (int)NotificationDeliveryStatusEnum.Delivered,
                EmailDispatchStatus.Skipped => (int)NotificationDeliveryStatusEnum.Skipped,
                EmailDispatchStatus.DeadLettered => (int)NotificationDeliveryStatusEnum.DeadLettered,
                EmailDispatchStatus.Parked => (int)NotificationDeliveryStatusEnum.Parked,
                EmailDispatchStatus.Unknown => (int)NotificationDeliveryStatusEnum.Unknown,
                _ => (int)NotificationDeliveryStatusEnum.Queued,
            },
            QueuedAt = now,
            CompletedAt = status is EmailDispatchStatus.Sent or EmailDispatchStatus.Skipped ? now : null,
            CreatedAt = now,
        };
        intent.Deliveries.Add(delivery);

        context.Users.Add(user);
        context.TenantUsers.Add(tenantUser);
        if (operation is not null)
        {
            context.ManagedTenantProvisioningOperations.Add(operation);
            context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                TenantUserId = tenantUser.Id,
                TenantUser = tenantUser,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = now,
                CreatedAt = now
            });
        }
        context.NotificationIntents.Add(intent);
        await context.SaveChangesAsync();
        return dispatch;
    }

    private static async Task<EmailDispatchOutbox> SeedReportReceiptDispatchAsync(
        ExploreDbContext context,
        Tenant tenant)
    {
        EmailDispatchOutbox dispatch = await SeedDispatchAsync(
            context,
            tenant.Id,
            EmailDispatchStatus.Pending);
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = dispatch.RecipientUserId,
            Pii = new ActorPii { DisplayName = "Report receipt recipient" },
            CreatedAt = DateTime.UtcNow
        };
        var @event = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            Title = "Reported event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = actor,
            TenantId = tenant.Id,
            Tenant = tenant,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!,
            CreatedAt = DateTime.UtcNow
        };
        EventReport report = EventReport.Create(
            tenant.Id,
            @event.Id,
            dispatch.RecipientUserId,
            actor.Id,
            EventReporterKind.AuthenticatedUser,
            EventReportSourceKind.UserReport,
            "spam",
            null,
            EventReportPriority.Normal,
            null,
            reportCaseUpdatesConsent: true,
            reportFollowUpContactConsent: false,
            "en",
            null,
            null);
        NotificationIntent intent = dispatch.NotificationIntent!;
        NotificationDelivery delivery = intent.Deliveries.Single();
        intent.CategoryId = (int)NotificationCategoryEnum.TrustSafetyReporting;
        intent.RecipientKindId = (int)NotificationRecipientKindEnum.Reporter;
        intent.TemplateKey = ReportReceiptNotificationFactory.TemplateKey;
        intent.EventId = @event.Id;
        intent.ReportId = report.Id;
        dispatch.Kind = EmailDispatchKind.ReportReceipt;
        dispatch.SourceType = ReportReceiptNotificationFactory.SourceType;
        dispatch.SourceId = report.Id;
        dispatch.EventId = @event.Id;
        delivery.DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.ReportCaseUpdate;
        delivery.PreferenceCategoryCode = NotificationPreferenceCategoryCodes.TrustSafety;
        delivery.ConsentPurpose = ReportEmailConsentPurposeCodes.CaseUpdates;
        delivery.ConsentVersion = 1;
        delivery.TemplateKey = ReportReceiptNotificationFactory.TemplateKey;

        context.Actors.Add(actor);
        context.Events.Add(@event);
        context.EventReports.Add(report);
        await context.SaveChangesAsync();
        return dispatch;
    }

    private static NotificationChannelPreference CreateUserEmailPreference(
        Guid tenantId,
        Guid userId,
        NotificationPreferenceCategoryEnum category,
        bool isEnabled)
    {
        return new NotificationChannelPreference
        {
            TenantId = tenantId,
            Tenant = null!,
            ScopeId = (int)ConfigurationScopeEnum.User,
            Scope = null!,
            UserId = userId,
            User = null!,
            CategoryId = (int)category,
            Category = null!,
            ChannelId = (int)NotificationPreferenceChannelEnum.Email,
            Channel = null!,
            IsEnabled = isEnabled
        };
    }

    private static EmailDispatchEligibilityEvaluator CreateEligibilityEvaluator(ExploreDbContext context) =>
        new(
            context,
            new NotificationDeliveryPolicyResolver(),
            new NotificationPreferenceResolver(context),
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));

    private static async Task MakeGraphUnknownAsync(ExploreDbContext context, AcceptedSettlementGraph graph)
    {
        graph.Dispatch.Status = EmailDispatchStatus.Unknown;
        graph.Dispatch.UnknownAt = DateTime.UtcNow;
        graph.Dispatch.ProcessingLeaseToken = null;
        graph.Dispatch.ProcessingStartedAt = null;
        graph.Attempt.Outcome = EmailDispatchAttemptOutcome.Unknown;
        graph.Attempt.CompletedAt = null;
        graph.Receipt.Status = EmailDispatchReceiptStatus.Unknown;
        graph.Delivery.StatusId = (int)NotificationDeliveryStatusEnum.Unknown;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static async Task<EmailDispatchOutbox> SeedProcessingDispatchWithReceiptAsync(
        ExploreDbContext context,
        Guid tenantId,
        DateTime processingStartedAt)
    {
        var dispatch = await SeedDispatchAsync(context, tenantId, EmailDispatchStatus.Processing);
        dispatch.AttemptCount = 1;
        dispatch.ProcessingStartedAt = processingStartedAt;
        dispatch.ProcessingLeaseToken = Guid.NewGuid();
        dispatch.UpdatedAt = processingStartedAt;
        context.EmailDispatchReceipts.Add(CreateReceipt(
            tenantId,
            dispatch.PublishEventId,
            dispatch.Id,
            "scheduler-node"));
        await context.SaveChangesAsync();
        return dispatch;
    }

    private static EmailDispatchReceipt CreateReceipt(
        Guid tenantId,
        Guid publishEventId,
        Guid outboxId,
        string consumerId)
    {
        var now = DateTime.UtcNow;
        return new EmailDispatchReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PublishEventId = publishEventId,
            EmailDispatchOutboxId = outboxId,
            Status = EmailDispatchReceiptStatus.Processing,
            ConsumerId = consumerId,
            FirstSeenAt = now,
            ProcessingStartedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static DateTime AtPostgresPrecision(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);

    private sealed record AcceptedSettlementGraph(
        EmailDispatchOutbox Dispatch,
        EmailDispatchAttempt Attempt,
        EmailDispatchReceipt Receipt,
        NotificationDelivery Delivery);

    private sealed class SettlementStageFailureInterceptor(string failingTable) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains(failingTable, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Injected accepted-settlement failure at {failingTable}.");
            }

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
