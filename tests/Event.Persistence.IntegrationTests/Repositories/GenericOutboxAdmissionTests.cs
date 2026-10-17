using Explore.Persistence.Database.ProviderPrimitives;
using System.Text.Json;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Event.Persistence.IntegrationTests.ConfigurationManifest;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class GenericOutboxAdmissionTests(PostgreSqlContainerFixture fixture)
{
    [Before(Test)]
    public async Task InstallAdmissionContract()
    {
        await using var context = fixture.CreateDbContext();
        await PostgresGenericOutboxAdmissionContract.ApplyAsync(context);
    }

    [Test]
    [Arguments("original")]
    [Arguments("current-case-version")]
    [Arguments("outbox-id")]
    [Arguments("envelope")]
    [Arguments("native-report")]
    [Arguments("tenant")]
    [Arguments("event")]
    [Arguments("case")]
    [Arguments("retained-version")]
    [Arguments("retained-time")]
    [Arguments("message-time")]
    [Arguments("digest")]
    [Arguments("lowercase-digest")]
    [Arguments("payload")]
    public async Task ReportAdmission_RequiresExactRetainedSubmissionReceipt(string variant)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Report receipt tenant",
            Slug = "receipt-" + Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Pii = new UserPii { Email = $"receipt-{Guid.CreateVersion7():N}@example.test", FirstName = "Receipt", LastName = "Owner" }
        };
        seed.AddRange(tenant, user);
        await seed.SaveChangesAsync();
        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Report source" },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id
        };
        seed.Actors.Add(actor);
        await seed.SaveChangesAsync();
        var source = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            Title = "Report source",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!
        };
        seed.Events.Add(source);
        var report = EventReport.Create(tenant.Id, source.Id, user.Id, null,
            EventReporterKind.AuthenticatedUser, EventReportSourceKind.UserReport, "spam", null,
            EventReportPriority.Normal, null, false, false, "en", null, null, DateTime.UtcNow);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "safety", EventReportPriority.Normal, null, report.CreatedAt);
        var message = EventReportOutboxMessageFactory.CreateProviderSyncRequestedMessage(report, reportCase, null);
        Guid originalVersion = reportCase.ConcurrencyStamp;
        string originalDigest = report.ProviderSyncPayloadDigest!;
        seed.AddRange(report, reportCase);
        var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(message.Payload!)!;
        switch (variant)
        {
            case "current-case-version": reportCase.ConcurrencyStamp = Guid.CreateVersion7(); break;
            case "outbox-id": message.Id = Guid.CreateVersion7(); break;
            case "envelope": message.AggregateType = "Event"; break;
            case "native-report": message.AggregateId = Guid.CreateVersion7(); break;
            case "tenant": fields["TenantId"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()); break;
            case "event": fields["EventId"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()); break;
            case "case": fields["CaseId"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()); break;
            case "retained-version":
                seed.Entry(report).Property(row => row.ProviderSyncCaseConcurrencyStamp).CurrentValue = Guid.CreateVersion7();
                break;
            case "retained-time":
                seed.Entry(report).Property(row => row.ProviderSyncSubmittedAtUtc).CurrentValue = report.CreatedAt.AddTicks(10);
                break;
            case "message-time": message.CreatedAt = message.CreatedAt.AddTicks(10); break;
            case "digest":
                seed.Entry(report).Property(row => row.ProviderSyncPayloadDigest).CurrentValue = new string('0', 64);
                break;
            case "lowercase-digest":
                seed.Entry(report).Property(row => row.ProviderSyncPayloadDigest).CurrentValue = new string('a', 64);
                break;
            case "payload": fields["CorrelationId"] = JsonSerializer.SerializeToElement("changed"); break;
        }
        if (variant is "tenant" or "event" or "case" or "payload")
            message.Payload = JsonSerializer.Serialize(fields);
        seed.OutboxMessages.Add(message);
        await seed.SaveChangesAsync();
        var admission = new GenericOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        var claims = await admission.ClaimDueAsync(10, DateTime.UtcNow, CancellationToken.None);
        if (variant is "original" or "current-case-version")
        {
            // This is a required production round-trip, not a test-normalized JSON receipt.
            // Exact-byte storage must preserve the factory digest; JSONB reformatting is not accepted.
            var claim = claims.Single();
            await Assert.That(claim.TenantId).IsEqualTo(tenant.Id);
            await Assert.That(claim.SourceIdentity).IsEqualTo(
                $"{report.Id:D}:{source.Id:D}:{reportCase.Id:D}:{originalVersion:D}:{originalDigest}");
            await using var runtime = fixture.CreateEnrolledTenantDbContext(new SelectedTenant(tenant.Id));
            var authority = new GenericOutboxExecutionAuthority(runtime, admission);
            await authority.BindAsync(claim, CancellationToken.None);
            var persisted = await new OutboxRepository(runtime, fixture.NativeQueueEnrollment)
                .GetClaimedGenericAsync(message.Id, claim.LeaseExpiresAt, claim.Status, DateTime.UtcNow);
            await authority.RequireAsync(persisted!, CancellationToken.None);
            await seed.Set<EventReport>().Where(row => row.Id == report.Id).ExecuteUpdateAsync(setters =>
                setters.SetProperty(row => row.ProviderSyncCaseConcurrencyStamp, Guid.CreateVersion7()));
            await Assert.ThrowsAsync<InvalidOperationException>(() => authority.RequireAsync(persisted!, CancellationToken.None));
        }
        else
        {
            await Assert.That(claims).IsEmpty();
            await Assert.That((await seed.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id)).Status)
                .IsEqualTo(OutboxMessageStatus.Failed);
        }
    }

    [Test]
    [Arguments("manifest")]
    [Arguments("manifest-wrong-id")]
    [Arguments("manifest-unbound")]
    [Arguments("import-null")]
    [Arguments("import-array")]
    [Arguments("import-payload")]
    [Arguments("import-unbound")]
    [Arguments("import-wrong-id")]
    [Arguments("import-lowercase")]
    public async Task ConfigurationAdmission_RequiresNativeEffectIdentityAndDigest(string variant)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        await GenericOutboxConfigurationReceiptAssertions.VerifyAsync(
            context, new GenericOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System), variant);
    }

    [Test]
    public async Task ExactPayloadRepresentation_SurvivesStorageAndReceipt_AndReplacementUsesOriginalBytes()
    {
        await fixture.ResetAsync();
        const string original = """
            [
              {
                "NewValue" : "\u00E9\u0633",
                "ChangedAt" : "2026-10-10T12:00:00Z",
                "Key" : "instance.display_name",
                "Scope" : 0,
                "OldValue" : null,
                "ActorUserId" : null,
                "TenantId" : null,
                "IsSensitive" : false
              }
            ]
            """;
        const string equivalent = """[{"Key":"instance.display_name","OldValue":null,"NewValue":"\u00e9\u0633","Scope":0,"TenantId":null,"ActorUserId":null,"ChangedAt":"2026-10-10T12:00:00Z","IsSensitive":false}]""";
        using var originalJson = JsonDocument.Parse(original);
        using var equivalentJson = JsonDocument.Parse(equivalent);
        await Assert.That(JsonElement.DeepEquals(originalJson.RootElement, equivalentJson.RootElement)).IsTrue();
        await using var seed = fixture.CreateDbContext();
        var operation = ConfigurationImportOperationTestData.Applied(Guid.CreateVersion7());
        var message = ConfigurationImportEffectOutbox.Create(
            operation.EffectOutboxId!.Value, operation.Id, DateTime.UtcNow);
        message.Payload = original;
        operation.BindEffectPayload(original);
        string retainedDigest = operation.EffectPayloadDigest!;
        seed.AddRange(operation, message);
        await seed.SaveChangesAsync();

        var admission = new GenericOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        var claim = (await admission.ClaimDueAsync(10, DateTime.UtcNow, CancellationToken.None)).Single();
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        var authority = new GenericOutboxExecutionAuthority(runtime, admission);
        var repository = new OutboxRepository(runtime, fixture.NativeQueueEnrollment);
        await authority.BindAsync(claim, CancellationToken.None);
        var persisted = await repository.GetClaimedGenericAsync(message.Id, claim.LeaseExpiresAt, claim.Status, DateTime.UtcNow);
        await Assert.That(persisted!.Payload).IsEqualTo(original);
        var receipt = await seed.Set<ConfigurationImportOperation>().AsNoTracking().SingleAsync(row => row.Id == operation.Id);
        await Assert.That(receipt.EffectPayloadDigest).IsEqualTo(retainedDigest);
        await Assert.That(claim.SourceIdentity).IsEqualTo($"{operation.Id:D}:{message.Id:D}:{retainedDigest}");
        await authority.RequireAsync(persisted, CancellationToken.None);

        // JSON-semantic equality is not authority to replace different original bytes.
        await Assert.That(await repository.TryReplaceProcessingPayloadAsync(
            message.Id, claim.LeaseExpiresAt, equivalent, equivalent)).IsFalse();
        await Assert.That(await repository.TryReplaceProcessingPayloadAsync(
            message.Id, claim.LeaseExpiresAt, original, equivalent)).IsTrue();
        var replaced = await repository.GetClaimedGenericAsync(message.Id, claim.LeaseExpiresAt, claim.Status, DateTime.UtcNow);
        await Assert.That(replaced).IsNull();
        var changed = await seed.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id);
        await Assert.That(changed.Payload).IsEqualTo(equivalent);
        await Assert.That(await admission.ReadClaimAsync(message.Id, claim.LeaseExpiresAt, claim.Status, CancellationToken.None)).IsNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.RequireAsync(persisted, CancellationToken.None));
        var unchangedReceipt = await seed.Set<ConfigurationImportOperation>().AsNoTracking().SingleAsync(row => row.Id == operation.Id);
        await Assert.That(unchangedReceipt.EffectPayloadDigest).IsEqualTo(retainedDigest);
        await Assert.That(changed.Status).IsEqualTo(OutboxMessageStatus.Processing);
    }

    [Test]
    [Arguments("single")]
    [Arguments("identical")]
    [Arguments("case-variant")]
    public async Task RawTextDuplicateProperties_CannotCollapseIntoAdmittedCustody(string variant)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        Guid locationId = Guid.CreateVersion7();
        Guid intentId = Guid.CreateVersion7();
        string duplicate = variant == "single" ? string.Empty
            : $", \"{(variant == "identical" ? "LocationId" : "locationid")}\" : \"{locationId:D}\"";
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = nameof(Location),
            AggregateId = locationId,
            EventType = "LocationPiiErased",
            Payload = $$"""{"LocationId":"{{locationId:D}}","IntentId":"{{intentId:D}}"{{duplicate}}}""",
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            MaxRetries = 10
        };
        await new OutboxRepository(context).Create(message);
        var admission = new GenericOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        var claims = await admission.ClaimDueAsync(10, DateTime.UtcNow, CancellationToken.None);
        if (variant == "single")
            await Assert.That(claims.Single().MessageId).IsEqualTo(message.Id);
        else
        {
            await Assert.That(claims).IsEmpty();
            var rejected = await context.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id);
            await Assert.That(rejected.Status).IsEqualTo(OutboxMessageStatus.Failed);
            await Assert.That(rejected.LastError).IsEqualTo("generic_source_not_admitted");
        }
    }

    [Test]
    public async Task PersistedSubjectClaim_DoesNotInventTenant_AndSettlesOnce()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var message = PrivacyErasureCacheInvalidationOutboxMessageFactory.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), DateTime.UtcNow);
        await new OutboxRepository(seed).Create(message);
        var admission = new GenericOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        var claim = (await admission.ClaimDueAsync(10, DateTime.UtcNow, CancellationToken.None)).Single();
        await Assert.That(claim.TenantId).IsNull();
        await Assert.That(claim.Ownership).IsEqualTo(GenericOutboxOwnership.Subject);
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        var authority = new GenericOutboxExecutionAuthority(runtime, admission);
        var repository = new OutboxRepository(runtime, fixture.NativeQueueEnrollment);
        await authority.BindAsync(claim, CancellationToken.None);
        var persisted = await repository.GetClaimedGenericAsync(
            claim.MessageId, claim.LeaseExpiresAt, claim.Status, DateTime.UtcNow);
        await authority.RequireAsync(persisted!, CancellationToken.None);
        await Assert.That(await repository.MarkAsCompleted(claim.MessageId, claim.LeaseExpiresAt)).IsTrue();
        await Assert.That(await repository.MarkAsCompleted(claim.MessageId, claim.LeaseExpiresAt)).IsFalse();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.RequireAsync(persisted!, CancellationToken.None));
    }

    [Test]
    [Arguments("unclaimed", false)]
    [Arguments("replaced", false)]
    [Arguments("payload", false)]
    [Arguments("envelope", false)]
    [Arguments("foreign", false)]
    [Arguments("unclaimed", true)]
    [Arguments("replaced", true)]
    [Arguments("payload", true)]
    [Arguments("envelope", true)]
    [Arguments("foreign", true)]
    public async Task ChangedCustody_CannotExecuteOrAcknowledgeOriginal(string change, bool deadLetter)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var message = PrivacyErasureCacheInvalidationOutboxMessageFactory.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), DateTime.UtcNow);
        if (deadLetter)
        {
            message.Status = OutboxMessageStatus.DeadLettered;
            message.NextRetryAt = DateTime.UtcNow.AddMinutes(-1);
        }
        var repository = new OutboxRepository(seed);
        await repository.Create(message);
        var admission = new GenericOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        if (change == "unclaimed")
        {
            await Assert.That(await admission.ReadClaimAsync(message.Id,
                DateTime.UtcNow.AddMinutes(5), deadLetter ? OutboxMessageStatus.DeadLettered : OutboxMessageStatus.Processing,
                CancellationToken.None)).IsNull();
            return;
        }
        var claim = (await admission.ClaimDueAsync(10, DateTime.UtcNow, CancellationToken.None)).Single();
        await using var runtime = change == "foreign"
            ? fixture.CreateEnrolledTenantDbContext(new SelectedTenant(Guid.CreateVersion7()))
            : fixture.CreateEnrolledTenantlessDbContext();
        var authority = new GenericOutboxExecutionAuthority(runtime, admission);
        if (change == "foreign")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                authority.BindAsync(claim, CancellationToken.None));
            return;
        }
        await authority.BindAsync(claim, CancellationToken.None);
        var executionRepository = new OutboxRepository(runtime, fixture.NativeQueueEnrollment);
        if (change == "replaced")
            await seed.OutboxMessages.Where(row => row.Id == message.Id).ExecuteUpdateAsync(setters =>
                setters.SetProperty(row => row.NextRetryAt, claim.LeaseExpiresAt.AddMinutes(1)));
        else if (change == "payload")
            await seed.OutboxMessages.Where(row => row.Id == message.Id).ExecuteUpdateAsync(setters =>
                setters.SetProperty(row => row.Payload, "{}"));
        else
            await seed.OutboxMessages.Where(row => row.Id == message.Id).ExecuteUpdateAsync(setters =>
                setters.SetProperty(row => row.AggregateId, Guid.CreateVersion7()));
        message.Status = claim.Status;
        message.NextRetryAt = claim.LeaseExpiresAt;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            deadLetter ? authority.RequireDeadLetterAsync(message, CancellationToken.None)
                : authority.RequireAsync(message, CancellationToken.None));
        if (change == "replaced")
            await Assert.That(deadLetter
                ? await executionRepository.MarkDeadLetterReconciled(message.Id, claim.LeaseExpiresAt)
                : await executionRepository.MarkAsCompleted(message.Id, claim.LeaseExpiresAt)).IsFalse();
        var stored = await seed.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id);
        await Assert.That(stored.Status).IsEqualTo(claim.Status);
    }

    [Test]
    [Arguments("registration.refund_campaign.process_requested", nameof(RefundCampaign))]
    [Arguments("registration.refund.dispatch_requested", nameof(RefundAttempt))]
    [Arguments("registration.refund.reconciliation_requested", nameof(RefundAttempt))]
    [Arguments("registration.payment.cancellation_requested", nameof(PaymentAttempt))]
    public async Task MissingFinancialSource_IsFailed_NotCompletedOrReconciled(string route, string aggregateType)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var id = Guid.CreateVersion7();
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = aggregateType,
            AggregateId = id,
            EventType = route,
            Payload = JsonSerializer.Serialize(new
            {
                TenantId = Guid.CreateVersion7(),
                RefundAttemptId = id,
                CampaignId = id,
                PaymentAttemptId = id
            }),
            CreatedAt = DateTime.UtcNow,
            Status = OutboxMessageStatus.Pending,
            MaxRetries = 10
        };
        await new OutboxRepository(seed).Create(message);
        var admission = new GenericOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        await Assert.That((await admission.ClaimDueAsync(10, DateTime.UtcNow, CancellationToken.None)).Count).IsEqualTo(0);
        var stored = await seed.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id);
        await Assert.That(stored.Status).IsEqualTo(OutboxMessageStatus.Failed);
        await Assert.That(stored.LastError).IsEqualTo("generic_source_not_admitted");
        await Assert.That(stored.ProcessedAt).IsNull();
    }

    [Test]
    public async Task TerminalTransition_PreservesOriginalCustody_AndAcknowledgesOnlyOnce()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var message = PrivacyErasureCacheInvalidationOutboxMessageFactory.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), DateTime.UtcNow);
        message.MaxRetries = 1;
        var repository = new OutboxRepository(seed);
        await repository.Create(message);
        var admission = new GenericOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        var claim = (await admission.ClaimDueAsync(10, DateTime.UtcNow, CancellationToken.None)).Single();
        await using var runtime = fixture.CreateEnrolledTenantlessDbContext();
        var authority = new GenericOutboxExecutionAuthority(runtime, admission);
        await authority.BindAsync(claim, CancellationToken.None);
        var executionRepository = new OutboxRepository(runtime, fixture.NativeQueueEnrollment);
        await Assert.That(await executionRepository.MarkAsFailed(message.Id, claim.LeaseExpiresAt,
            "dispatch_failed", true, 1, DateTime.UtcNow)).IsEqualTo(OutboxFailureTransition.DeadLettered);
        var terminal = await executionRepository.GetClaimedGenericAsync(
            message.Id, claim.LeaseExpiresAt, OutboxMessageStatus.DeadLettered, DateTime.UtcNow);
        await authority.RequireDeadLetterAsync(terminal!, CancellationToken.None);
        await Assert.That(await executionRepository.MarkDeadLetterReconciled(message.Id, claim.LeaseExpiresAt)).IsTrue();
        await Assert.That(await executionRepository.MarkDeadLetterReconciled(message.Id, claim.LeaseExpiresAt)).IsFalse();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.RequireDeadLetterAsync(terminal!, CancellationToken.None));
    }

    [Test]
    [Arguments("original")]
    [Arguments("wrong-source")]
    [Arguments("foreign-pointer")]
    [Arguments("foreign-runtime")]
    [Arguments("source-changed")]
    public async Task PersistedIntent_SelectsOnlyItsNativeTenantAndIdentity(string variant)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Generic admission owner",
            Slug = "generic-" + Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var other = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Unrelated owner",
            Slug = "generic-" + Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        seed.Tenants.AddRange(tenant, other);
        var id = Guid.CreateVersion7();
        var intent = new AdmissionRecoveryRequestIntent(id, tenant.Id, "protected-test-identity", 1, DateTime.UtcNow);
        seed.AdmissionRecoveryRequestIntents.Add(intent);
        var message = new OutboxMessage
        {
            Id = id,
            AggregateId = variant == "wrong-source" ? Guid.CreateVersion7() : id,
            AggregateType = nameof(AdmissionRecoveryRequestIntent),
            EventType = "AdmissionRecoveryRequestProcessingRequested",
            Payload = JsonSerializer.Serialize(new
            {
                TenantId = variant == "foreign-pointer" ? other.Id : tenant.Id,
                RequestIntentId = id
            }),
            CreatedAt = intent.CreatedAt,
            Status = OutboxMessageStatus.Pending,
            MaxRetries = 10
        };
        seed.OutboxMessages.Add(message);
        await seed.SaveChangesAsync();
        var admission = new GenericOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        var claims = await admission.ClaimDueAsync(10, DateTime.UtcNow, CancellationToken.None);
        if (variant is "wrong-source" or "foreign-pointer")
        {
            await Assert.That(claims).IsEmpty();
            var rejected = await seed.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == id);
            await Assert.That(rejected.Status).IsEqualTo(OutboxMessageStatus.Failed);
            return;
        }
        var claim = claims.Single();
        await Assert.That(claim.TenantId).IsEqualTo(tenant.Id);
        await Assert.That(claim.SourceId).IsEqualTo(intent.Id);
        await using var runtime = fixture.CreateEnrolledTenantDbContext(
            new SelectedTenant(variant == "foreign-runtime" ? other.Id : tenant.Id));
        var authority = new GenericOutboxExecutionAuthority(runtime, admission);
        if (variant == "foreign-runtime")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => authority.BindAsync(claim, CancellationToken.None));
            var denied = await Assert.ThrowsAsync<PostgresException>(() =>
                runtime.AdmissionRecoveryRequestIntents.AnyAsync(row => row.Id == id));
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
            await Assert.That((await seed.AdmissionRecoveryRequestIntents.AsNoTracking()
                .SingleAsync(row => row.Id == id)).TenantId).IsEqualTo(tenant.Id);
            return;
        }
        await authority.BindAsync(claim, CancellationToken.None);
        var reloaded = await new OutboxRepository(runtime, fixture.NativeQueueEnrollment)
            .GetClaimedGenericAsync(id, claim.LeaseExpiresAt, claim.Status, DateTime.UtcNow);
        if (variant == "source-changed")
        {
            await seed.AdmissionRecoveryRequestIntents.Where(row => row.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.TenantId, other.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => authority.RequireAsync(reloaded!, CancellationToken.None));
        }
        else
        {
            await authority.RequireAsync(reloaded!, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => authority.BindAsync(claim, CancellationToken.None));
        }
        await Assert.That((await seed.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == id)).Status)
            .IsEqualTo(OutboxMessageStatus.Processing);
        await Assert.That(await seed.AdmissionRecoveryDeliveryIntents.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments("EventReportProviderSyncRequested")]
    [Arguments("LocationPiiErased")]
    [Arguments("LocationPrivacyCorrectionRequested")]
    [Arguments("location.privacy.corrected")]
    [Arguments("PrivacyErasureCacheInvalidationRequested")]
    [Arguments("ConfigurationManifestEffectsRequested")]
    [Arguments("ConfigurationImportEffectsRequested")]
    [Arguments("OperatorIdentityImported")]
    [Arguments("RegistrationOrderRejected")]
    [Arguments("RegistrationOrderConfirmed")]
    [Arguments("RegistrationOrderCancelled")]
    [Arguments("admission.event_cancellation.requested")]
    [Arguments("AdmissionCredentialDeliveryRequested")]
    [Arguments("AdmissionRecoveryRequestProcessingRequested")]
    [Arguments("AdmissionRecoveryDeliveryRequested")]
    [Arguments("registration.refund_campaign.process_requested")]
    [Arguments("registration.refund.dispatch_requested")]
    [Arguments("registration.refund.reconciliation_requested")]
    [Arguments("registration.payment.cancellation_requested")]
    [Arguments("ManagedTenantProvisioningProcessRequested")]
    public async Task EveryGenericRoute_RejectsWrongPersistedEnvelope_IncludingTerminalCustody(string route)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var processing = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateId = Guid.CreateVersion7(),
            AggregateType = "WrongEnvelope",
            EventType = route,
            Payload = null,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            MaxRetries = 1
        };
        var terminal = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateId = processing.AggregateId,
            AggregateType = "WrongEnvelope",
            EventType = route,
            Payload = null,
            Status = OutboxMessageStatus.DeadLettered,
            CreatedAt = processing.CreatedAt,
            MaxRetries = 1,
            NextRetryAt = DateTime.UtcNow.AddMinutes(-1)
        };
        seed.OutboxMessages.AddRange(processing, terminal);
        await seed.SaveChangesAsync();
        var admission = new GenericOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        await Assert.That(await admission.ClaimDueAsync(10, DateTime.UtcNow, CancellationToken.None)).IsEmpty();
        var stored = await seed.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == processing.Id);
        await Assert.That(stored.Status).IsEqualTo(OutboxMessageStatus.Failed);
        var retained = await seed.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == terminal.Id);
        await Assert.That(retained.Status).IsEqualTo(OutboxMessageStatus.DeadLettered);
        await Assert.That(retained.NextRetryAt).IsNotNull();
        await Assert.That(retained.ProcessedAt).IsNull();
        if (GenericOutboxRoutes.SupportsDeadLetter(route))
            await Assert.That(retained.LastError).IsEqualTo("generic_source_not_admitted");
    }

    [Test]
    public async Task GlobalCapability_ReturnsCustodyButCannotReadPayloadOrProtectedIntent()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var message = PrivacyErasureCacheInvalidationOutboxMessageFactory.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), DateTime.UtcNow);
        await new OutboxRepository(context).Create(message);
        var sql = context.GetService<ISqlGenerationHelper>();
        string schema = context.GetService<IDbContextOptions>()
            .FindExtension<RelationalNamespaceOptionsExtension>()!.TargetSchema;
        string Table<T>() => sql.DelimitIdentifier(context.Model.FindEntityType(typeof(T))!.GetTableName()!, schema);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SET LOCAL ROLE {PostgresGenericOutboxAdmissionContract.GlobalRole}";
            await command.ExecuteNonQueryAsync();
            command.CommandText = $"""
                SELECT rolsuper OR rolbypassrls
                    OR pg_has_role(current_user, '{PostgresGenericOutboxAdmissionContract.OwnerRole}', 'MEMBER')
                    OR pg_has_role(current_user, '{PostgresGenericOutboxAdmissionContract.MigratorRole}', 'MEMBER')
                FROM pg_catalog.pg_roles WHERE rolname = current_user
                """;
            await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsFalse();
            command.CommandText = $"SELECT * FROM {PostgresGenericOutboxAdmissionContract.ClaimFunctionSql(context)}(10, statement_timestamp())";
            await using (var reader = await command.ExecuteReaderAsync())
            {
                await Assert.That(await reader.ReadAsync()).IsTrue();
                await Assert.That(reader.GetGuid(reader.GetOrdinal("MessageId"))).IsEqualTo(message.Id);
                await Assert.That(reader.IsDBNull(reader.GetOrdinal("TenantId"))).IsTrue();
                await Assert.That(reader.GetInt32(reader.GetOrdinal("Ownership"))).IsEqualTo((int)GenericOutboxOwnership.Subject);
                await Assert.That(reader.FieldCount).IsEqualTo(12);
                await Assert.That(await reader.ReadAsync()).IsFalse();
            }
            await transaction.CommitAsync();
        }
        foreach (string query in new[]
        {
            $"SELECT payload FROM {Table<OutboxMessage>()}",
            $"SELECT protected_identity FROM {Table<AdmissionRecoveryRequestIntent>()}",
            $"SELECT external_account_id FROM {Table<RefundAttempt>()}"
        })
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SET LOCAL ROLE {PostgresGenericOutboxAdmissionContract.GlobalRole}";
            await command.ExecuteNonQueryAsync();
            command.CommandText = query;
            var failure = await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteScalarAsync());
            await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            await transaction.RollbackAsync();
        }
    }

    [Test]
    [Arguments("EventPublishedNotificationFanoutRequested")]
    [Arguments("EventLightModeratedNotificationFanoutRequested")]
    [Arguments("EventHeavyRedactedNotificationFanoutRequested")]
    [Arguments("NotificationFanoutOccurrenceRequested")]
    [Arguments("EventDiscoveryIdentityCorrectionRequested")]
    public async Task NotificationPurpose_CannotBeClaimedByGenericAdmission(string route)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateId = Guid.CreateVersion7(),
            AggregateType = "Event",
            EventType = route,
            Payload = "{}",
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            MaxRetries = 5
        };
        await new OutboxRepository(context).Create(message);
        var admission = new GenericOutboxAdmission(new AdmissionFactory(fixture), TimeProvider.System);
        await Assert.That(await admission.ClaimDueAsync(10, DateTime.UtcNow, CancellationToken.None)).IsEmpty();
        var retained = await context.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id);
        await Assert.That(retained.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(retained.NextRetryAt).IsNull();
        await Assert.That(retained.LastError).IsNull();
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;

    private sealed class AdmissionFactory(PostgreSqlContainerFixture database)
        : IGenericOutboxAdmissionDbContextFactory
    {
        private readonly GlobalExploreDbContextFactory _global = new(
            database.GetNativeQueueOptions(Explore.Secrets.Database.PrimaryDatabaseRole.Global));

        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            _global.CreateDbContextAsync(cancellationToken);
    }
}

public sealed class GenericOutboxConfigurationPortableAdmissionTests
{
    [Test]
    [Arguments("admission", false)]
    [Arguments("admission", true)]
    [Arguments("bind", false)]
    [Arguments("bind", true)]
    [Arguments("dispatch", false)]
    [Arguments("dispatch", true)]
    public async Task OriginalClaimMustRemainLiveAfterAwaitedSourceRead(string boundary, bool expire)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var factory = fixture.Services.GetRequiredService<IDbContextFactory<ExploreDbContext>>();
        var clock = new ClaimClock(DateTimeOffset.UtcNow);
        var operation = ConfigurationImportOperationTestData.Applied(Guid.CreateVersion7());
        var message = ConfigurationImportEffectOutbox.Create(
            operation.EffectOutboxId!.Value, operation.Id, clock.GetUtcNow().UtcDateTime);
        operation.BindEffectPayload(message.Payload);
        fixture.Context.AddRange(operation, message);
        await fixture.Context.SaveChangesAsync();
        var sql = fixture.Context.GetService<ISqlGenerationHelper>();
        var entity = fixture.Context.Model.FindEntityType(typeof(ConfigurationImportOperation))!;
        var observed = new SourceReadObservation(sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema()));
        await using var original = await factory.CreateDbContextAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>(
            (DbContextOptions<ExploreDbContext>)original.GetService<IDbContextOptions>())
            .AddInterceptors(observed).Options;
        var admission = new GenericOutboxAdmission(new ObservedAdmissionFactory(options), clock);
        var claim = (await admission.ClaimDueAsync(1000, clock.GetUtcNow().UtcDateTime, CancellationToken.None))
            .Single(row => row.MessageId == message.Id);
        await using var runtime = await factory.CreateDbContextAsync();
        var authority = new GenericOutboxExecutionAuthority(runtime, admission);
        var persisted = await new OutboxRepository(runtime).GetClaimedGenericAsync(
            claim.MessageId, claim.LeaseExpiresAt, claim.Status, clock.GetUtcNow().UtcDateTime);
        await Assert.That(persisted).IsNotNull();
        if (boundary == "dispatch")
            await authority.BindAsync(claim, CancellationToken.None);
        observed.Armed = true;
        Task<GenericOutboxClaim?> attempt = boundary switch
        {
            "admission" => admission.ReadClaimAsync(claim.MessageId, claim.LeaseExpiresAt, claim.Status, CancellationToken.None),
            "bind" => BindAsync(),
            "dispatch" => RequireAsync(),
            _ => throw new InvalidOperationException("Unknown claim boundary.")
        };
        try
        {
            await Task.WhenAny(observed.Read.Task, attempt).WaitAsync(TimeSpan.FromSeconds(30));
            if (!observed.Read.Task.IsCompleted)
                await attempt;
            await observed.Read.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (expire)
                clock.Set(claim.LeaseExpiresAt);
        }
        finally { observed.Release.TrySetResult(); }
        if (boundary == "admission")
            await Assert.That(await attempt).IsEqualTo(expire ? null : claim);
        else if (expire)
        {
            var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => attempt);
            await Assert.That(denied!.Message).IsEqualTo("generic_outbox_execution_not_admitted");
        }
        else
            await Assert.That(await attempt).IsEqualTo(claim);
        await using var independent = await factory.CreateDbContextAsync();
        var unchanged = await independent.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id);
        await Assert.That(unchanged.Status).IsEqualTo(claim.Status);
        await Assert.That(unchanged.NextRetryAt).IsEqualTo(claim.LeaseExpiresAt);
        await Assert.That(unchanged.ProcessedAt).IsNull();

        async Task<GenericOutboxClaim?> BindAsync()
        {
            await authority.BindAsync(claim, CancellationToken.None);
            return claim;
        }
        async Task<GenericOutboxClaim?> RequireAsync()
        {
            await authority.RequireAsync(persisted!, CancellationToken.None);
            return claim;
        }
    }

    private sealed class ClaimClock(DateTimeOffset now) : TimeProvider
    {
        private long ticks = now.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        internal void Set(DateTime value) => Interlocked.Exchange(ref ticks, value.Ticks);
    }

    private sealed class SourceReadObservation(string sourceTable) : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains(sourceTable, StringComparison.Ordinal))
            {
                Read.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }

    private sealed class ObservedAdmissionFactory(DbContextOptions<ExploreDbContext> options)
        : IGenericOutboxAdmissionDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ExploreDbContext(options));
        }
    }

    [Test]
    [Arguments("manifest")]
    [Arguments("manifest-wrong-id")]
    [Arguments("manifest-unbound")]
    [Arguments("import-null")]
    [Arguments("import-array")]
    [Arguments("import-payload")]
    [Arguments("import-unbound")]
    [Arguments("import-wrong-id")]
    [Arguments("import-lowercase")]
    public async Task ConfigurationAdmission_RequiresNativeEffectIdentityAndDigest(string variant)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var factory = fixture.Services.GetRequiredService<IDbContextFactory<ExploreDbContext>>();
        await GenericOutboxConfigurationReceiptAssertions.VerifyAsync(
            fixture.Context, new GenericOutboxAdmission(new AdmissionFactory(factory), TimeProvider.System), variant);
    }

    private sealed class AdmissionFactory(IDbContextFactory<ExploreDbContext> factory) : IGenericOutboxAdmissionDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            factory.CreateDbContextAsync(cancellationToken);
    }
}

internal static class GenericOutboxConfigurationReceiptAssertions
{
    internal static async Task VerifyAsync(ExploreDbContext context, IGenericOutboxAdmission admission, string variant)
    {
        DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        OutboxMessage message;
        string expectedIdentity;
        if (variant.StartsWith("manifest", StringComparison.Ordinal))
        {
            var operation = ConfigurationManifestOperation.Create(
                ConfigurationManifestAuditMode.Bootstrap, "v1", "TenantBootstrap", "receipt",
                new string('a', 64), ConfigurationManifestOperationStatus.Applied,
                0, 0, 0, 0, null, null, now, now, new string('b', 64), 1);
            message = ConfigurationManifestEffectOutbox.Create(Guid.CreateVersion7(), operation.Id, now);
            if (variant != "manifest-unbound")
                operation.BindEffectOutbox(message.Id);
            if (variant == "manifest-wrong-id")
                message.Id = Guid.CreateVersion7();
            expectedIdentity = $"{operation.Id:D}:{message.Id:D}";
            context.Add(operation);
        }
        else
        {
            var operation = ConfigurationImportOperationTestData.Applied(Guid.CreateVersion7());
            message = ConfigurationImportEffectOutbox.Create(operation.EffectOutboxId!.Value, operation.Id, now);
            if (variant == "import-array")
                message.Payload = "[]";
            if (variant != "import-unbound")
                operation.BindEffectPayload(message.Payload);
            if (variant == "import-null")
                await Assert.That(operation.EffectPayloadDigest).IsEqualTo(
                    "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");
            expectedIdentity = $"{operation.Id:D}:{message.Id:D}:{operation.EffectPayloadDigest}";
            context.Add(operation);
            if (variant == "import-payload")
                message.Payload = "[]";
            if (variant == "import-wrong-id")
                message.Id = Guid.CreateVersion7();
            if (variant == "import-lowercase")
                context.Entry(operation).Property(row => row.EffectPayloadDigest).CurrentValue =
                    operation.EffectPayloadDigest!.ToLowerInvariant();
        }
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var claims = await admission.ClaimDueAsync(1000, DateTime.UtcNow, CancellationToken.None);
        if (variant is "manifest" or "import-null" or "import-array")
        {
            var claim = claims.Single(row => row.MessageId == message.Id);
            await Assert.That(claim.TenantId).IsNull();
            await Assert.That(claim.SourceIdentity).IsEqualTo(expectedIdentity);
            await Assert.That(await admission.ReadClaimAsync(message.Id, claim.LeaseExpiresAt, claim.Status, CancellationToken.None))
                .IsEqualTo(claim);
        }
        else
        {
            await Assert.That(claims.Any(row => row.MessageId == message.Id)).IsFalse();
            var rejected = await context.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id);
            await Assert.That(rejected.Status).IsEqualTo(OutboxMessageStatus.Failed);
            await Assert.That(rejected.LastError).IsEqualTo("generic_source_not_admitted");
        }
    }
}
