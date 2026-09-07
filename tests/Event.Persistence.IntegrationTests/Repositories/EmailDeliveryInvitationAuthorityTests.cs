// ABOUTME: Verifies retired managed-administrator invitations fail closed through real SQLite eligibility and drainage.
// ABOUTME: Preserves terminal audit/redaction policy across re-enable, stale recovery, and explicit replay.

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Telemetry;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Infrastructure.Mail.Unsubscribe;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliveryInvitationAuthorityTests
{
    public enum GrantScenario { Active, Missing, Revoked, WrongUser, WrongTenant, Moderator }

    [Test]
    [Arguments(GrantScenario.Active)]
    [Arguments(GrantScenario.Missing)]
    [Arguments(GrantScenario.Revoked)]
    [Arguments(GrantScenario.WrongUser)]
    [Arguments(GrantScenario.WrongTenant)]
    [Arguments(GrantScenario.Moderator)]
    public async Task RetiredInvitationHandoff_IsSkippedEvenWithCurrentTenantAdminGrant(GrantScenario scenario)
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"email-invitation-authority-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            SeededDispatch dispatch;
            Guid? grantId;
            string invitationDestination = $"invited-admin-{Guid.CreateVersion7():N}@example.test";
            await using (var seed = CreateContext(databasePath))
            {
                (dispatch, grantId) = await SeedInvitationAsync(seed, scenario, invitationDestination);
            }

            await using var context = CreateContext(databasePath);
            var evaluator = CreateEvaluator(context);
            if (scenario == GrantScenario.Revoked)
            {
                // A worker opened before revocation must not trust a previously tracked valid grant.
                var originalGrant = await context.TenantUserRoleGrants
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .SingleAsync(row => row.TenantId == dispatch.TenantId && row.Id == grantId);
                await Assert.That(originalGrant.RevokedAt).IsNull();
                await using var revocation = CreateContext(databasePath);
                var persistedGrant = await revocation.TenantUserRoleGrants
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .SingleAsync(row => row.TenantId == dispatch.TenantId && row.Id == grantId);
                persistedGrant.RevokedAt = DateTime.UtcNow;
                persistedGrant.RevocationReason = "Tenant administrator authority removed";
                await revocation.SaveChangesAsync();
            }

            var result = await evaluator.EvaluateAndBeginProviderHandoffAsync(new EmailDispatchEligibilityRequest(
                TenantId: dispatch.TenantId, OutboxId: dispatch.OutboxId, ProcessingLeaseToken: dispatch.LeaseToken,
                AttemptNumber: 0, GlobalSmtpRateLimitPerMinute: 60, TenantSmtpRateLimitPerMinute: 60,
                ConsumerId: "invitation-authority-test", EvaluatedAt: DateTime.UtcNow));

            await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Skipped);
            await Assert.That(result.RecipientEmail).IsNull();
            await using var observer = CreateContext(databasePath);
            var persisted = (await new EmailDispatchOutboxRepository(observer)
                .GetByTenantAndId(dispatch.TenantId, dispatch.OutboxId, CancellationToken.None))!;
            await Assert.That(persisted.RecipientEmail).IsEqualTo(invitationDestination);
            await Assert.That(persisted.AttemptCount).IsEqualTo(1);
            var attempt = await observer.EmailDispatchAttempts
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .SingleAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId);
            var receipt = await observer.EmailDispatchReceipts
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .SingleAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId);
            await Assert.That(attempt.Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Skipped);
            await Assert.That(attempt.FailureCategory).IsEqualTo("managed_administrator_invitation_retired");
            await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Skipped);
            await Assert.That(result.ReceiptId).IsEqualTo(receipt.Id);
            await Assert.That(result.AttemptNumber).IsEqualTo(1);
            await Assert.That(receipt.ProcessingStartedAt).IsNull();
            await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Skipped);
            await Assert.That(persisted.ProcessingLeaseToken).IsNull();
            await Assert.That(persisted.LastFailureCategory).IsEqualTo("managed_administrator_invitation_retired");
            var delivery = await observer.NotificationDeliveries
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .SingleAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId);
            await Assert.That(delivery.StatusId).IsEqualTo((int)NotificationDeliveryStatusEnum.Skipped);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
        }
    }

    [Test]
    [Arguments(EmailDispatchStatus.Pending, false)]
    [Arguments(EmailDispatchStatus.Pending, true)]
    [Arguments(EmailDispatchStatus.RetryScheduled, true)]
    [Arguments(EmailDispatchStatus.Processing, true)]
    [Arguments(EmailDispatchStatus.Parked, true)]
    public async Task RetiredQueuedInvitation_CannotReachTransportOrReviveAfterRecovery(
        EmailDispatchStatus initialStatus, bool restoreBeforeDrain)
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"email-retired-invitation-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            SeededDispatch dispatch;
            Guid publishEventId;
            await using (var seed = CreateContext(databasePath))
            {
                (dispatch, _) = await SeedInvitationAsync(seed, GrantScenario.Active, "retired-admin@example.test");
                var row = await seed.EmailDispatchOutbox
                    .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .SingleAsync(value => value.TenantId == dispatch.TenantId && value.Id == dispatch.OutboxId);
                publishEventId = row.PublishEventId;
                row.Status = initialStatus;
                row.ProcessingStartedAt = initialStatus == EmailDispatchStatus.Processing ? DateTime.UtcNow.AddHours(-1) : null;
                row.ProcessingLeaseToken = initialStatus == EmailDispatchStatus.Processing ? dispatch.LeaseToken : null;
                row.NextAttemptAt = initialStatus == EmailDispatchStatus.RetryScheduled ? DateTime.UtcNow.AddHours(-1) : null;
                row.ParkedAt = initialStatus == EmailDispatchStatus.Parked ? DateTime.UtcNow : null;
                row.ParkReason = initialStatus == EmailDispatchStatus.Parked ? EmailDispatchParkReason.CapabilityUnavailable : null;
                if (initialStatus == EmailDispatchStatus.Parked)
                {
                    var delivery = await seed.NotificationDeliveries
                        .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                        .SingleAsync(value => value.TenantId == dispatch.TenantId && value.EmailDispatchOutboxId == dispatch.OutboxId);
                    delivery.StatusId = (int)NotificationDeliveryStatusEnum.Parked;
                }
                await seed.SaveChangesAsync();
                await ConfirmEmailDisableAsync(seed);
                if (restoreBeforeDrain)
                    await EnableInstanceEmailAsync(seed);
                if (initialStatus == EmailDispatchStatus.Parked)
                {
                    var repository = new EmailDispatchOutboxRepository(seed);
                    await Assert.That(await new EfCoreUnitOfWork(seed).ExecuteInTransactionAsync(token =>
                        repository.TryReplayForOperator(dispatch.TenantId, dispatch.OutboxId, null, DateTime.UtcNow, token))).IsTrue();
                }
            }

            var sentMessages = new List<EmailMessage>();
            var transport = Substitute.For<IEmailService>();
            transport.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var message = call.Arg<EmailMessage>();
                ArgumentNullException.ThrowIfNull(message);
                sentMessages.Add(message);
                return EmailResult.Ok();
            });
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddMetrics();
            services.AddSingleton<BusinessMetrics>();
            services.AddScoped(_ => CreateContext(databasePath));
            services.AddScoped<IEmailDispatchOutboxRepository, EmailDispatchOutboxRepository>();
            services.AddScoped<IEmailDispatchEligibilityEvaluator>(provider => CreateEvaluator(provider.GetRequiredService<ExploreDbContext>()));
            services.AddScoped<ITenantContextAccessor, TenantContextAccessor>();
            services.AddSingleton<IEmailService>(transport);
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddSingleton<IEmailUnsubscribeTokenService>(new EmailUnsubscribeTokenService(new EphemeralDataProtectionProvider()));
            services.Configure<EmailDispatchProcessorSettings>(settings =>
            {
                settings.MaxConcurrentDispatches = 1;
                settings.MaxConcurrentDispatchesPerTenant = 1;
            });
            services.AddSingleton<EmailDispatchDrainService>();
            await using var provider = services.BuildServiceProvider();
            var drain = provider.GetRequiredService<EmailDispatchDrainService>();
            var recovered = await drain.RecoverStaleProcessingAsync(CancellationToken.None);
            await Assert.That(recovered.RecoveredCount).IsEqualTo(initialStatus == EmailDispatchStatus.Processing ? 1 : 0);

            var result = await drain.ProcessBatchAsync(CancellationToken.None);

            await Assert.That(sentMessages.Count).IsEqualTo(0);
            await Assert.That(result.SkippedCount).IsEqualTo(1);
            await Assert.That(result.SentCount).IsEqualTo(0);
            await using var observer = CreateContext(databasePath);
            var repositoryAfter = new EmailDispatchOutboxRepository(observer);
            var skipped = (await repositoryAfter.GetByTenantAndId(dispatch.TenantId, dispatch.OutboxId, CancellationToken.None))!;
            await Assert.That(skipped.Status).IsEqualTo(EmailDispatchStatus.Skipped);
            await Assert.That(skipped.ParkReason).IsNull();
            await Assert.That(skipped.LastFailureCategory).IsEqualTo("managed_administrator_invitation_retired");
            await Assert.That(skipped.ProcessingLeaseToken).IsNull();
            await EnableInstanceEmailAsync(observer);
            await Assert.That(await new EfCoreUnitOfWork(observer).ExecuteInTransactionAsync(token =>
                repositoryAfter.TryReplayForOperator(dispatch.TenantId, dispatch.OutboxId, null, DateTime.UtcNow, token))).IsFalse();
            await Assert.That((await drain.RecoverStaleProcessingAsync(CancellationToken.None)).RecoveredCount).IsEqualTo(0);
            await Assert.That((await drain.ProcessSingleAsync(dispatch.TenantId, publishEventId, "retired-pointer", CancellationToken.None)).Outcome)
                .IsEqualTo(EmailDispatchDrainOutcome.AlreadySettled);

            DateTime redactedAt = DateTime.UtcNow;
            await Assert.That(await new EfCoreUnitOfWork(observer).ExecuteInTransactionAsync(token =>
                repositoryAfter.RedactRetentionEligible(dispatch.TenantId, redactedAt, redactedAt, 10, token))).IsEqualTo(1);
            var redacted = (await repositoryAfter.GetByTenantAndId(dispatch.TenantId, dispatch.OutboxId, CancellationToken.None))!;
            await Assert.That(redacted.ContentRedactedAt).IsEqualTo(redactedAt);
            await Assert.That(redacted.RecipientEmail).IsEqualTo(string.Empty);
            await Assert.That(redacted.Subject).IsEqualTo(string.Empty);
            await Assert.That(redacted.PlainTextBody).IsNull();
            await Assert.That(redacted.LastError).IsNull();
            await Assert.That((await drain.ProcessSingleAsync(dispatch.TenantId, publishEventId, "redacted-pointer", CancellationToken.None)).Outcome)
                .IsEqualTo(EmailDispatchDrainOutcome.AlreadySettled);
            await Assert.That(sentMessages.Count).IsEqualTo(0);

            // A fresh active kind uses the very same real drain and transport boundary successfully.
            var active = await SeedProcessingDispatchAsync(observer, "active-after-retirement");
            var activeRow = await observer.EmailDispatchOutbox
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .SingleAsync(value => value.TenantId == active.TenantId && value.Id == active.OutboxId);
            activeRow.Status = EmailDispatchStatus.Pending;
            activeRow.ProcessingStartedAt = null;
            activeRow.ProcessingLeaseToken = null;
            await observer.SaveChangesAsync();
            await Assert.That((await drain.ProcessBatchAsync(CancellationToken.None)).SentCount).IsEqualTo(1);
            await Assert.That(sentMessages.Single().To).IsEqualTo(activeRow.RecipientEmail);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            File.Delete(databasePath + "-wal");
            File.Delete(databasePath + "-shm");
        }
    }

    private static async Task<(SeededDispatch Dispatch, Guid? GrantId)> SeedInvitationAsync(
        ExploreDbContext context, GrantScenario scenario, string invitationDestination)
    {
        DateTime now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(), FullName = "Invitation authority tenant",
            Slug = $"invitation-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        };
        var recipient = new User
        {
            Id = Guid.CreateVersion7(), EmailVerified = true, CreatedAt = now,
            Pii = new UserPii { Email = $"recipient-{Guid.CreateVersion7():N}@example.test", FirstName = "Invited", LastName = "Admin" }
        };
        var recipientMembership = new TenantUser
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
            UserId = recipient.Id, User = recipient, StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = now, CreatedAt = now
        };
        var intent = new NotificationIntent
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id,
            CategoryId = (int)NotificationCategoryEnum.RegistrationLifecycle,
            OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
            RecipientKindId = (int)NotificationRecipientKindEnum.User,
            StatusId = (int)NotificationIntentStatusEnum.DispatchQueued,
            TemplateKey = "tenant.administrator.invitation", DeduplicationKey = $"invitation:{Guid.CreateVersion7():N}",
            RecipientUserId = recipient.Id, RecipientTenantUser = recipientMembership, CreatedAt = now
        };
        var operation = new ManagedTenantProvisioningOperation
        {
            Id = Guid.CreateVersion7(), ManagedInstanceId = Guid.CreateVersion7(),
            ExternalRequestId = $"request-{Guid.CreateVersion7():N}",
            ExternalCustomerReference = $"customer-{Guid.CreateVersion7():N}",
            RequestHash = new string('a', 64), TenantSlug = "invitation-authority",
            CurrentOutboxMessageId = Guid.CreateVersion7(), Status = ManagedTenantProvisioningStatus.Succeeded,
            TenantId = tenant.Id, TenantAdministratorUserId = recipient.Id,
            CompletedAt = now, CreatedAt = now
        };
        Guid leaseToken = Guid.CreateVersion7();
        var outbox = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, PublishEventId = Guid.CreateVersion7(),
            Kind = EmailDispatchKind.TenantAdministratorInvitation, SourceType = "managed_tenant_provisioning",
            SourceId = operation.Id, ManagedTenantProvisioningOperationId = operation.Id,
            NotificationIntentId = intent.Id, NotificationIntent = intent,
            RecipientUserId = recipient.Id, RecipientTenantUser = recipientMembership,
            RecipientAddressSource = RecipientAddressSource.ManagedTenantAdministratorInvitation,
            RecipientEmail = invitationDestination, Subject = "Tenant administrator invitation", PlainTextBody = "Invitation ready.",
            Status = EmailDispatchStatus.Processing, AttemptCount = 0, MaxAttempts = 5,
            ProcessingStartedAt = now, ProcessingLeaseToken = leaseToken, CreatedAt = now, UpdatedAt = now
        };
        var delivery = new NotificationDelivery
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, NotificationIntentId = intent.Id, NotificationIntent = intent,
            ChannelId = (int)NotificationPreferenceChannelEnum.Email,
            DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.TenantAdministrationRequired,
            IsRequired = true, PolicyVersion = 1, RecipientAddressSource = RecipientAddressSource.ManagedTenantAdministratorInvitation,
            DisclosureLevel = "standard", TemplateKey = intent.TemplateKey, TemplateVersion = 1,
            EmailDispatchOutboxId = outbox.Id, EmailDispatchOutbox = outbox,
            StatusId = (int)NotificationDeliveryStatusEnum.Queued, QueuedAt = now, CreatedAt = now
        };
        context.Tenants.Add(tenant);
        context.Users.Add(recipient);
        context.TenantUsers.Add(recipientMembership);
        context.NotificationIntents.Add(intent);
        context.NotificationDeliveries.Add(delivery);

        TenantUser membership = recipientMembership;
        if (scenario is GrantScenario.WrongUser or GrantScenario.WrongTenant)
        {
            Guid tenantId = tenant.Id;
            User user = membership.User;
            if (scenario == GrantScenario.WrongTenant)
            {
                var otherTenant = new Tenant
                {
                    Id = Guid.CreateVersion7(), FullName = "Unrelated invitation tenant",
                    Slug = $"other-invitation-{Guid.CreateVersion7():N}",
                    TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
                };
                context.Tenants.Add(otherTenant);
                tenantId = otherTenant.Id;
            }
            else
            {
                user = new User
                {
                    Id = Guid.CreateVersion7(), EmailVerified = true, CreatedAt = now,
                    Pii = new UserPii { Email = $"other-admin-{Guid.CreateVersion7():N}@example.test", FirstName = "Other", LastName = "Admin" }
                };
                context.Users.Add(user);
            }
            membership = new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = null!, UserId = user.Id, User = user,
                StatusId = (int)TenantUserStatusEnum.Active, JoinedAt = now, CreatedAt = now
            };
            context.TenantUsers.Add(membership);
        }

        TenantUserRoleGrant? grant = null;
        if (scenario != GrantScenario.Missing)
        {
            grant = new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(), TenantId = membership.TenantId, Tenant = null!,
                TenantUserId = membership.Id, TenantUser = membership,
                RoleId = (int)(scenario == GrantScenario.Moderator ? RoleEnum.TenantModerator : RoleEnum.TenantAdmin),
                Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant, GrantedAt = now, CreatedAt = now
            };
            context.TenantUserRoleGrants.Add(grant);
        }
        // SQLite does not generate PostgreSQL's xmin; seed this persisted authority with an explicit token.
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ie_managed_tenant_provisioning_operations
                (id, managed_instance_id, external_request_id, external_customer_reference, request_hash,
                 tenant_slug, current_outbox_message_id, status, tenant_id, tenant_administrator_user_id,
                 completed_at, created_at, xmin)
            VALUES ({operation.Id}, {operation.ManagedInstanceId}, {operation.ExternalRequestId},
                {operation.ExternalCustomerReference}, {operation.RequestHash}, {operation.TenantSlug},
                {operation.CurrentOutboxMessageId}, {operation.Status.ToString()}, {operation.TenantId},
                {operation.TenantAdministratorUserId}, {operation.CompletedAt}, {operation.CreatedAt}, 0)
            """);
        await context.SaveChangesAsync();
        return (new SeededDispatch(TenantId: tenant.Id, OutboxId: outbox.Id, LeaseToken: leaseToken), grant?.Id);
    }
}
