using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class EmailDispatchOutboxRepositoryBypassTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task WorkerQueueBypasses_WithAmbientTenant_ReturnOnlyEligibleRowsAndMutateExactDispatch()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("email-worker-a");
        var tenantB = CreateTenant("email-worker-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        await seedContext.SaveChangesAsync();

        var now = new DateTime(2026, 1, 4, 12, 0, 0, DateTimeKind.Utc);
        var retryAttemptsBefore = now.AddSeconds(-30);
        var eligible = CreateDispatch(tenantA.Id, "eligible", EmailDispatchStatus.Pending, now.AddMinutes(-8));
        var retryDue = CreateDispatch(tenantA.Id, "retry-due", EmailDispatchStatus.RetryScheduled, now.AddMinutes(-7));
        retryDue.NextAttemptAt = now.AddMinutes(-1);
        var retryFuture = CreateDispatch(tenantA.Id, "retry-future", EmailDispatchStatus.RetryScheduled, now.AddMinutes(-6));
        retryFuture.NextAttemptAt = now.AddMinutes(15);
        var throttled = CreateDispatch(tenantA.Id, "throttled", EmailDispatchStatus.Pending, now.AddMinutes(-5));
        throttled.RabbitMqLastPublishAttemptAt = now.AddSeconds(-5);
        var sent = CreateDispatch(tenantA.Id, "sent", EmailDispatchStatus.Sent, now.AddMinutes(-4));
        var ambientPaused = CreateDispatch(tenantB.Id, "paused", EmailDispatchStatus.Pending, now.AddMinutes(-3));

        seedContext.EmailDispatchOutbox.AddRange(eligible, retryDue, retryFuture, throttled, sent, ambientPaused);
        seedContext.EmailDispatchTenantControls.Add(CreateTenantControl(tenantB.Id, isPaused: true, now));
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var visibleWithoutBypass = await tenantBContext.EmailDispatchOutbox
            .AsNoTracking()
            .Select(dispatch => dispatch.Id)
            .ToListAsync();

        var repository = new EmailDispatchOutboxRepository(tenantBContext);
        var rabbitMqBatch = await repository.GetRabbitMqPublishBatch(
            10,
            now,
            retryAttemptsBefore,
            CancellationToken.None);
        var leaseToken = Guid.CreateVersion7();
        var claimed = await repository.TryClaimSpecificAsync(
            CreateSpecificClaimRequest(eligible, leaseToken, now),
            CancellationToken.None);
        var futureClaimed = await repository.TryClaimSpecificAsync(
            CreateSpecificClaimRequest(retryFuture, Guid.CreateVersion7(), now),
            CancellationToken.None);

        await using var verifyContext = fixture.CreateDbContext();
        var rows = await verifyContext.EmailDispatchOutbox
            .AsNoTracking()
            .Where(dispatch => dispatch.Id == eligible.Id
                || dispatch.Id == retryDue.Id
                || dispatch.Id == retryFuture.Id
                || dispatch.Id == throttled.Id
                || dispatch.Id == sent.Id
                || dispatch.Id == ambientPaused.Id)
            .ToDictionaryAsync(dispatch => dispatch.Id);

        await Assert.That(visibleWithoutBypass).IsEquivalentTo([ambientPaused.Id]);
        await Assert.That(rabbitMqBatch.Select(dispatch => dispatch.Id))
            .IsEquivalentTo([eligible.Id, retryDue.Id]);

        await Assert.That(claimed).IsNotNull();
        await Assert.That(futureClaimed).IsNull();
        await Assert.That(rows[eligible.Id].TenantId).IsEqualTo(tenantA.Id);
        await Assert.That(rows[eligible.Id].Status).IsEqualTo(EmailDispatchStatus.Processing);
        await Assert.That(rows[eligible.Id].ProcessingLeaseToken).IsEqualTo(leaseToken);
        await Assert.That(rows[eligible.Id].AttemptCount).IsEqualTo(0);
        await Assert.That(rows[retryFuture.Id].Status).IsEqualTo(EmailDispatchStatus.RetryScheduled);
        await Assert.That(rows[ambientPaused.Id].Status).IsEqualTo(EmailDispatchStatus.Pending);
    }

    private static EmailDispatchSpecificClaimRequest CreateSpecificClaimRequest(
        EmailDispatchOutbox dispatch,
        Guid leaseToken,
        DateTime claimedAt) =>
        new(
            dispatch.TenantId,
            dispatch.PublishEventId,
            leaseToken,
            20,
            5,
            100,
            50,
            claimedAt);

    [Test]
    public async Task TenantOperationBypasses_WithAmbientTenant_ReturnAndUpdateOnlyExplicitTenantRows()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("email-tenant-a");
        var tenantB = CreateTenant("email-tenant-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        await seedContext.SaveChangesAsync();

        var now = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
        var tenantAStatus = CreateDispatch(tenantA.Id, "status-a", EmailDispatchStatus.Pending, now.AddMinutes(-4));
        var tenantAParkable = CreateDispatch(tenantA.Id, "park-a", EmailDispatchStatus.DeadLettered, now.AddMinutes(-3));
        var tenantAReplayable = CreateDispatch(tenantA.Id, "replay-a", EmailDispatchStatus.DeadLettered, now.AddMinutes(-2));
        var tenantBStatus = CreateDispatch(tenantB.Id, "status-b", EmailDispatchStatus.Pending, now.AddMinutes(-1));
        seedContext.EmailDispatchOutbox.AddRange(tenantAStatus, tenantAParkable, tenantAReplayable, tenantBStatus);
        seedContext.EmailDispatchTenantControls.Add(CreateTenantControl(tenantB.Id, isPaused: true, now));
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var visibleOutboxWithoutBypass = await tenantBContext.EmailDispatchOutbox
            .AsNoTracking()
            .Select(dispatch => dispatch.Id)
            .ToListAsync();
        var visibleControlsWithoutBypass = await tenantBContext.EmailDispatchTenantControls
            .AsNoTracking()
            .Select(control => control.TenantId)
            .ToListAsync();

        var repository = new EmailDispatchOutboxRepository(tenantBContext);
        var tenantAStatusRows = await repository.GetStatusRows(tenantA.Id, 10, CancellationToken.None);
        var tenantAById = await repository.GetByTenantAndId(tenantA.Id, tenantAStatus.Id, CancellationToken.None);
        var wrongTenantById = await repository.GetByTenantAndId(tenantB.Id, tenantAStatus.Id, CancellationToken.None);
        var tenantAByPublishEvent = await repository.GetByTenantAndPublishEventId(
            tenantA.Id,
            tenantAStatus.PublishEventId,
            CancellationToken.None);
        var tenantAPausedBefore = await repository.IsTenantPaused(tenantA.Id, CancellationToken.None);
        var tenantBPaused = await repository.IsTenantPaused(tenantB.Id, CancellationToken.None);
        var actorId = Guid.CreateVersion7();
        var changedAt = now.AddMinutes(1);
        var tenantAControl = await repository.SetTenantPauseState(
            tenantA.Id,
            isPaused: true,
            "operator maintenance",
            actorId,
            changedAt,
            CancellationToken.None);
        var parked = await repository.TryParkForOperator(
            tenantA.Id,
            tenantAParkable.Id,
            "operator quarantine",
            actorId,
            changedAt,
            CancellationToken.None);
        var wrongTenantParked = await repository.TryParkForOperator(
            tenantB.Id,
            tenantAStatus.Id,
            "wrong tenant",
            actorId,
            changedAt,
            CancellationToken.None);
        var replayed = await repository.TryReplayForOperator(
            tenantA.Id,
            tenantAReplayable.Id,
            actorId,
            changedAt,
            CancellationToken.None);

        await using var verifyContext = fixture.CreateDbContext();
        var rows = await verifyContext.EmailDispatchOutbox
            .AsNoTracking()
            .Where(dispatch => dispatch.Id == tenantAStatus.Id
                || dispatch.Id == tenantAParkable.Id
                || dispatch.Id == tenantAReplayable.Id
                || dispatch.Id == tenantBStatus.Id)
            .ToDictionaryAsync(dispatch => dispatch.Id);
        var controls = await verifyContext.EmailDispatchTenantControls
            .AsNoTracking()
            .Where(control => control.TenantId == tenantA.Id || control.TenantId == tenantB.Id)
            .ToDictionaryAsync(control => control.TenantId);

        await Assert.That(visibleOutboxWithoutBypass).IsEquivalentTo([tenantBStatus.Id]);
        await Assert.That(visibleControlsWithoutBypass).IsEquivalentTo([tenantB.Id]);

        await Assert.That(tenantAStatusRows.Select(dispatch => dispatch.TenantId))
            .IsEquivalentTo([tenantA.Id, tenantA.Id, tenantA.Id]);
        await Assert.That(tenantAStatusRows.Select(dispatch => dispatch.Id))
            .DoesNotContain(tenantBStatus.Id);
        await Assert.That(tenantAById).IsNotNull();
        await Assert.That(tenantAById!.Id).IsEqualTo(tenantAStatus.Id);
        await Assert.That(wrongTenantById).IsNull();
        await Assert.That(tenantAByPublishEvent).IsNotNull();
        await Assert.That(tenantAByPublishEvent!.Id).IsEqualTo(tenantAStatus.Id);

        await Assert.That(tenantAPausedBefore).IsFalse();
        await Assert.That(tenantBPaused).IsTrue();
        await Assert.That(tenantAControl.TenantId).IsEqualTo(tenantA.Id);
        await Assert.That(controls[tenantA.Id].IsPaused).IsTrue();
        await Assert.That(controls[tenantA.Id].PausedBy).IsEqualTo(actorId);
        await Assert.That(controls[tenantB.Id].IsPaused).IsTrue();

        await Assert.That(parked).IsTrue();
        await Assert.That(wrongTenantParked).IsFalse();
        await Assert.That(replayed).IsTrue();
        await Assert.That(rows[tenantAStatus.Id].Status).IsEqualTo(EmailDispatchStatus.Pending);
        await Assert.That(rows[tenantAParkable.Id].Status).IsEqualTo(EmailDispatchStatus.Parked);
        await Assert.That(rows[tenantAParkable.Id].LastFailureCategory).IsEqualTo("operator_parked");
        await Assert.That(rows[tenantAReplayable.Id].Status).IsEqualTo(EmailDispatchStatus.Pending);
        await Assert.That(rows[tenantAReplayable.Id].DeadLetteredAt).IsNull();
        await Assert.That(rows[tenantBStatus.Id].Status).IsEqualTo(EmailDispatchStatus.Pending);
    }

    private static Tenant CreateTenant(string slugPrefix)
    {
        return new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"Email Dispatch Bypass {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
    }

    private static EmailDispatchOutbox CreateDispatch(
        Guid tenantId,
        string key,
        EmailDispatchStatus status,
        DateTime createdAt)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"{key}@example.test",
                FirstName = "Email",
                LastName = "Recipient",
            },
            EmailVerified = true,
            CreatedAt = createdAt,
        };
        var tenantUser = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            UserId = user.Id,
            User = user,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = createdAt,
            CreatedAt = createdAt,
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
            DeduplicationKey = $"email-dispatch-bypass:{key}:{Guid.CreateVersion7():N}",
            RecipientUserId = user.Id,
            RecipientTenantUser = tenantUser,
            CreatedAt = createdAt,
        };
        var dispatch = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
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
            Subject = $"Email dispatch {key}",
            PlainTextBody = "plain body",
            HtmlBody = "<p>html body</p>",
            Status = status,
            AttemptCount = status == EmailDispatchStatus.Pending ? 0 : 1,
            MaxAttempts = 5,
            SentAt = status == EmailDispatchStatus.Sent ? createdAt : null,
            DeadLetteredAt = status == EmailDispatchStatus.DeadLettered ? createdAt : null,
            ParkedAt = status == EmailDispatchStatus.Parked ? createdAt : null,
            UnknownAt = status == EmailDispatchStatus.Unknown ? createdAt : null,
            LastFailureCategory = status == EmailDispatchStatus.Pending ? null : "smtp_send_failed",
            LastError = status == EmailDispatchStatus.Pending ? null : "previous failure",
            LastFailureAt = status == EmailDispatchStatus.Pending ? null : createdAt,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
        intent.Deliveries.Add(new NotificationDelivery
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            NotificationIntentId = intent.Id,
            NotificationIntent = intent,
            ChannelId = (int)NotificationPreferenceChannelEnum.Email,
            DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.RegistrationStatusOptional,
            IsRequired = false,
            PolicyVersion = 1,
            RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            DisclosureLevel = "standard",
            TemplateKey = intent.TemplateKey,
            TemplateVersion = 1,
            LinkAllowed = false,
            EmailDispatchOutboxId = dispatch.Id,
            EmailDispatchOutbox = dispatch,
            StatusId = status switch
            {
                EmailDispatchStatus.Sent => (int)NotificationDeliveryStatusEnum.Delivered,
                EmailDispatchStatus.DeadLettered => (int)NotificationDeliveryStatusEnum.DeadLettered,
                EmailDispatchStatus.Parked => (int)NotificationDeliveryStatusEnum.Parked,
                EmailDispatchStatus.Unknown => (int)NotificationDeliveryStatusEnum.Unknown,
                EmailDispatchStatus.Skipped => (int)NotificationDeliveryStatusEnum.Skipped,
                _ => (int)NotificationDeliveryStatusEnum.Queued,
            },
            QueuedAt = createdAt,
            CompletedAt = status is EmailDispatchStatus.Sent or EmailDispatchStatus.Skipped
                ? createdAt
                : null,
            CreatedAt = createdAt,
        });

        return dispatch;
    }

    private static EmailDispatchTenantControl CreateTenantControl(Guid tenantId, bool isPaused, DateTime now)
    {
        return new EmailDispatchTenantControl
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            IsPaused = isPaused,
            PauseReason = isPaused ? "paused for maintenance" : null,
            PausedAt = isPaused ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
