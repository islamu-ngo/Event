using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

public sealed class RegistrationConsentRecordPersistenceTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Category("Runtime")]
    public async Task PostgreSqlEnforcesTypedSubjectLineageAndRollsBackCombinedNativeEvidence()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("phase84")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await database.StartAsync();
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        await using ExploreDbContext context = CreateContext(options);
        await context.Database.EnsureCreatedAsync();
        await SeedLookupsAsync(context);
        ConsentScope orderScope = await SeedScopeAsync(context, RegistrationRequirementSubjectTypeEnum.AllOrders);
        ConsentScope participantScope = await SeedScopeAsync(context, RegistrationRequirementSubjectTypeEnum.EveryParticipant);

        RegistrationConsentRecord persisted = RegistrationConsentRecord.Grant(
            orderScope.Submission, orderScope.Requirement, orderScope.Version, orderScope.ConsentField,
            RegistrationAnswerSubjectTypeEnum.RegistrationOrder, orderScope.OrderId, null, UtcNow.AddMinutes(2));
        context.RegistrationConsentRecords.Add(persisted);
        await context.SaveChangesAsync();
        await Assert.That(persisted.EffectiveSubjectIdentity).IsEqualTo(orderScope.OrderId);
        persisted.Withdraw(UtcNow.AddMinutes(3));
        await context.SaveChangesAsync();

        RegistrationConsentRecord orphan = RegistrationConsentRecord.Grant(
            participantScope.Submission, participantScope.Requirement, participantScope.Version, participantScope.ConsentField,
            RegistrationAnswerSubjectTypeEnum.Participant, Guid.CreateVersion7(), null, UtcNow.AddMinutes(2));
        await AssertForeignKeyRejectedAsync(context, orphan);

        RegistrationConsentRecord crossTenant = RegistrationConsentRecord.Grant(
            participantScope.Submission, participantScope.Requirement, participantScope.Version, participantScope.ConsentField,
            RegistrationAnswerSubjectTypeEnum.Participant, participantScope.OtherTenantParticipantId, null, UtcNow.AddMinutes(2));
        await AssertForeignKeyRejectedAsync(context, crossTenant);

        Guid expectedAttemptStamp = orderScope.Attempt.ConcurrencyStamp;
        RegistrationSubmission accepted = orderScope.Attempt.SubmitNative(
            RegistrationEvidenceHash.Create(Hash("atomic-evidence")), UtcNow.AddMinutes(4), null);
        RegistrationAnswer answer = RegistrationAnswer.CreateText(
            accepted, orderScope.TextField, orderScope.Requirement,
            RegistrationAnswerSubjectTypeEnum.RegistrationOrder, orderScope.OrderId, 1, "Ada", UtcNow.AddMinutes(4));
        RegistrationConsentRecord firstDuplicate = RegistrationConsentRecord.Grant(
            accepted, orderScope.Requirement, orderScope.Version, orderScope.ConsentField,
            RegistrationAnswerSubjectTypeEnum.RegistrationOrder, orderScope.OrderId, null, UtcNow.AddMinutes(4));
        RegistrationConsentRecord secondDuplicate = RegistrationConsentRecord.Grant(
            accepted, orderScope.Requirement, orderScope.Version, orderScope.ConsentField,
            RegistrationAnswerSubjectTypeEnum.RegistrationOrder, orderScope.OrderId, null, UtcNow.AddMinutes(4));
        RegistrationSubmissionIssue issue = RegistrationSubmissionIssue.Create(
            accepted, "FORCED_ROLLBACK", UtcNow.AddMinutes(4), orderScope.TextField.Id);
        Exception failure = (await Assert.That(async () => await new RegistrationSubmissionRepository(context)
            .PersistAcceptedWithNormalizationAsync(orderScope.Attempt, accepted, expectedAttemptStamp,
                [answer], [firstDuplicate, secondDuplicate], [issue], [], CancellationToken.None))
            .Throws<Exception>())!;
        PostgresException postgresFailure = FindPostgresException(failure);
        await Assert.That(postgresFailure.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
        await Assert.That(postgresFailure.ConstraintName).IsEqualTo(
            RelationalConstraintDescriptorResolver.UniqueIndex<RegistrationConsentRecord>(
                context,
                nameof(RegistrationConsentRecord.TenantId),
                nameof(RegistrationConsentRecord.RegistrationSubmissionId),
                nameof(RegistrationConsentRecord.RegistrationFormFieldId),
                nameof(RegistrationConsentRecord.AnswerSubjectTypeId),
                nameof(RegistrationConsentRecord.EffectiveSubjectIdentity)).Name);
        context.ChangeTracker.Clear();

        await Assert.That(await context.RegistrationSubmissions.CountAsync(candidate => candidate.Id == accepted.Id)).IsEqualTo(0);
        await Assert.That(await context.RegistrationAnswers.CountAsync(candidate => candidate.RegistrationSubmissionId == accepted.Id)).IsEqualTo(0);
        await Assert.That(await context.RegistrationConsentRecords.CountAsync(candidate => candidate.RegistrationSubmissionId == accepted.Id)).IsEqualTo(0);
        await Assert.That(await context.RegistrationSubmissionIssues.CountAsync(candidate => candidate.RegistrationSubmissionId == accepted.Id)).IsEqualTo(0);
        RegistrationAttempt restoredAttempt = await context.RegistrationAttempts.SingleAsync(candidate => candidate.Id == orderScope.Attempt.Id);
        await Assert.That(restoredAttempt.StatusId).IsEqualTo((int)RegistrationAttemptStatusEnum.Active);
    }

    private static ExploreDbContext CreateContext(DbContextOptions<ExploreDbContext> options)
    {
        var context = new ExploreDbContext(options);
        context.EnableTenantFilterBypass("Phase 8.4 PostgreSQL constraint and rollback verification.");
        return context;
    }

    private static async Task SeedLookupsAsync(ExploreDbContext context)
    {
        context.RegistrationAnswerSubjectTypes.AddRange(
            new RegistrationAnswerSubjectType { Id = (int)RegistrationAnswerSubjectTypeEnum.RegistrationOrder, MasterCode = "REGISTRATION_ORDER", FullName = "Registration order" },
            new RegistrationAnswerSubjectType { Id = (int)RegistrationAnswerSubjectTypeEnum.Participant, MasterCode = "PARTICIPANT", FullName = "Participant" });
        context.Set<RegistrationAttemptStatus>().AddRange(
            new RegistrationAttemptStatus { Id = (int)RegistrationAttemptStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active" },
            new RegistrationAttemptStatus { Id = (int)RegistrationAttemptStatusEnum.Consumed, MasterCode = "CONSUMED", FullName = "Consumed" });
        context.Set<RegistrationSubmissionStatus>().AddRange(
            new RegistrationSubmissionStatus { Id = (int)RegistrationSubmissionStatusEnum.EvidenceOnly, MasterCode = "EVIDENCE_ONLY", FullName = "Evidence only" },
            new RegistrationSubmissionStatus { Id = (int)RegistrationSubmissionStatusEnum.Received, MasterCode = "RECEIVED", FullName = "Received" });
        await context.SaveChangesAsync();
    }

    private static async Task<ConsentScope> SeedScopeAsync(
        ExploreDbContext context,
        RegistrationRequirementSubjectTypeEnum subjectType)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        try
        {
            Tenant tenant = new() { FullName = "Consent", Slug = $"consent-{Guid.NewGuid():N}", TenantStatusId = 2, TenantStatus = null! };
            Tenant otherTenant = new() { FullName = "Other", Slug = $"other-{Guid.NewGuid():N}", TenantStatusId = 2, TenantStatus = null! };
            context.Tenants.AddRange(tenant, otherTenant);
            await context.SaveChangesAsync();
            Guid eventId = Guid.CreateVersion7();
            Explore.Domain.Event @event = new(EventStatusEnum.Draft)
            {
                Id = eventId,
                Title = "Consent",
                ActorId = Guid.CreateVersion7(),
                Actor = null!,
                TenantId = tenant.Id,
                Tenant = null!,
                EventStatus = null!,
                EventFormatId = 1,
                EventFormat = null!,
                EventProvenanceTypeId = 1,
                VisibilityTypeId = 1,
                VisibilityType = null!,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            Guid orderId = Guid.CreateVersion7();
            RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenant.Id, eventId, $"CONSENT_{Guid.NewGuid():N}", UtcNow);
            RegistrationRequirement requirement = RegistrationRequirement.Create(
                workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
                RegistrationRequirementCompletionEffectEnum.BlocksRegistration, RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
                subjectType, null, UtcNow);
            RegistrationChannel channel = RegistrationChannel.Create(requirement, 1, true, null, UtcNow);
            requirement.AddChannel(channel);
            workflow.AddRequirement(requirement);
            RegistrationForm form = RegistrationForm.Create(tenant.Id, eventId, "platform.registration", $"consent-{Guid.NewGuid():N}", "Consent", UtcNow);
            RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, UtcNow);
            RegistrationFormSection section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", UtcNow);
            RegistrationFormField consentField = RegistrationFormField.Create(Guid.CreateVersion7(), section, 1, "registration",
                "marketing_consent", "Send me event updates", RegistrationFieldTypeEnum.Consent, 1,
                RegistrationOrganizerVisibilityEnum.Hidden, true, false, UtcNow, "EVENT_UPDATES", "2026-08",
                "I agree to receive event updates by email.");
            RegistrationFormField textField = RegistrationFormField.Create(Guid.CreateVersion7(), section, 2, "registration",
                "name", "Name", RegistrationFieldTypeEnum.ShortText, 1,
                RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, false, UtcNow);
            version.AddSection(section);
            version.AddField(section, consentField);
            version.AddField(section, textField);
            form.AddVersion(version);
            RegistrationAttempt attempt = RegistrationAttempt.Create(tenant.Id, eventId, orderId, workflow.Id, requirement.Id,
                channel.Id, form.Id, version.Id, CapabilityTokenHash.Create(Hash("capability")), null, null, UtcNow,
                UtcNow.AddMinutes(10));
            attempt.ConcurrencyStamp = Guid.CreateVersion7();
            RegistrationParticipant participant = RegistrationParticipant.Create(
                tenant.Id, orderId, null, ParticipantTypeEnum.Adult, null);
            Guid otherOrderId = Guid.CreateVersion7();
            RegistrationParticipant otherParticipant = RegistrationParticipant.Create(
                otherTenant.Id, otherOrderId, null, ParticipantTypeEnum.Adult, null);
            context.AddRange(@event, workflow, requirement, channel, form, version, section, consentField, textField, attempt,
                participant, otherParticipant);
            await context.SaveChangesAsync();
            RegistrationSubmission submission = RegistrationSubmission.CreateNativeEvidenceOnly(
                attempt, RegistrationEvidenceHash.Create(Hash("evidence")), UtcNow.AddMinutes(1), null);
            context.RegistrationSubmissions.Add(submission);
            await context.SaveChangesAsync();
            return new(tenant.Id, orderId, requirement, version, consentField, textField, attempt, submission, otherParticipant.Id);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("SET session_replication_role = origin");
            context.ChangeTracker.Clear();
        }
    }

    private static async Task AssertForeignKeyRejectedAsync(ExploreDbContext context, RegistrationConsentRecord record)
    {
        context.RegistrationConsentRecords.Add(record);
        Exception failure = (await Assert.That(() => context.SaveChangesAsync()).Throws<Exception>())!;
        await Assert.That(FindPostgresException(failure).SqlState).IsEqualTo(PostgresErrorCodes.ForeignKeyViolation);
        context.ChangeTracker.Clear();
    }

    private static PostgresException FindPostgresException(Exception exception) =>
        exception is PostgresException postgres
            ? postgres
            : exception.InnerException is not null
                ? FindPostgresException(exception.InnerException)
                : throw new InvalidOperationException("Expected PostgreSQL failure.", exception);

    private static string Hash(string value) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record ConsentScope(
        Guid TenantId,
        Guid OrderId,
        RegistrationRequirement Requirement,
        RegistrationFormVersion Version,
        RegistrationFormField ConsentField,
        RegistrationFormField TextField,
        RegistrationAttempt Attempt,
        RegistrationSubmission Submission,
        Guid OtherTenantParticipantId);
}
