// ABOUTME: Defines real PostgreSQL acceptance for typed atomic registration-answer storage.
// ABOUTME: Requires relational value, subject, lineage, durable identity, sensitive-shape, and tenant constraints.

using System.Security.Cryptography;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

public sealed class RegistrationAnswerPersistenceContractTests
{
    private static readonly DateTime AnalyticsNow = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ModelDeclaresTypedAnswerIdentitySensitiveShapeAndNamedFilters()
    {
        await using ExploreDbContext context = new(
            new DbContextOptionsBuilder<ExploreDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
                .UseSnakeCaseNamingConvention()
                .Options);

        IEntityType answer = context.Model.FindEntityType(typeof(RegistrationAnswer))!;
        IEntityType sensitive = context.Model.FindEntityType(typeof(RegistrationSensitiveAnswerValue))!;
        IIndex identity = answer.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
            [
                nameof(RegistrationAnswer.TenantId),
                nameof(RegistrationAnswer.RegistrationSubmissionId),
                nameof(RegistrationAnswer.RegistrationFormFieldId),
                nameof(RegistrationAnswer.AnswerSubjectTypeId),
                nameof(RegistrationAnswer.EffectiveSubjectIdentity),
                nameof(RegistrationAnswer.Ordinal)
            ]));

        await Assert.That(answer.GetDeclaredQueryFilters().Count()).IsEqualTo(2);
        await Assert.That(sensitive.GetDeclaredQueryFilters().Count()).IsEqualTo(2);
        await Assert.That(identity.IsUnique).IsTrue();
        await Assert.That(identity.GetFilter()).IsNull();
        await Assert.That(sensitive.FindProperty("Plaintext")).IsNull();
        await Assert.That(sensitive.FindProperty(nameof(RegistrationSensitiveAnswerValue.Ciphertext))).IsNotNull();
        await Assert.That(sensitive.FindProperty(nameof(RegistrationSensitiveAnswerValue.KeyVersion))).IsNotNull();
    }

    [Test]
    public async Task AnalyticsRepositoryAppliesExactScopeGovernanceSuppressionAndBoundedOutput()
    {
        await using ExploreDbContext context = CreateInMemoryContext();
        AnalyticsScope scope = SeedAnalyticsScope(context);
        context.TenantContext = new TestTenantContext(scope.TenantId);
        var repository = new RegistrationAnswerAnalyticsRepository(context);

        RegistrationAnswerAnalyticsProjection result = (await repository.GetEventFormVersionAnalyticsAsync(
            scope.TenantId, scope.EventId, scope.FormId, scope.VersionId, 3, CancellationToken.None))!;

        await Assert.That(result.Fields.Select(field => field.Key)).IsEquivalentTo(["age", "month", "attendance"]);
        RegistrationAnswerFieldAggregateProjection age = result.Fields.Single(field => field.Key == "age");
        await Assert.That(age.Numeric).IsNotNull();
        await Assert.That((age.ResponseCount, age.Numeric!.Min, age.Numeric.Max, age.Numeric.Average)).IsEqualTo((6, 18m, 42m, 30m));
        RegistrationAnswerFieldAggregateProjection month = result.Fields.Single(field => field.Key == "month");
        await Assert.That(month.Cells.Select(cell => cell.Value)).IsEquivalentTo(["2026-08"]);
        await Assert.That(month.Cells.Any(cell => cell.Value.Contains("-02", StringComparison.Ordinal))).IsFalse();
        RegistrationAnswerFieldAggregateProjection attendance = result.Fields.Single(field => field.Key == "attendance");
        await Assert.That(attendance.Cells.Select(cell => (cell.Value, cell.Count))).IsEquivalentTo([("yes", 3L)]);
    }

    private static ExploreDbContext CreateInMemoryContext() => new(
        new DbContextOptionsBuilder<ExploreDbContext>()
            .UseInMemoryDatabase($"analytics-{Guid.NewGuid():N}")
            .Options);

    private static AnalyticsScope SeedAnalyticsScope(ExploreDbContext context)
    {
        Tenant tenant = new() { FullName = "Analytics", Slug = $"analytics-{Guid.NewGuid():N}", TenantStatusId = 2, TenantStatus = null! };
        User user = new() { Pii = new UserPii { Email = $"{Guid.NewGuid():N}@example.com", FirstName = "A", LastName = "B" } };
        context.AddRange(tenant, user);
        context.SaveChanges();
        Actor actor = new() { Pii = new ActorPii { DisplayName = "Analytics" }, ActorTypeId = 1, ActorType = null!, UserId = user.Id };
        context.Actors.Add(actor);
        context.SaveChanges();
        Explore.Domain.Event @event = new(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = "Analytics",
            ActorId = actor.Id,
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
        context.Events.Add(@event);
        context.SaveChanges();

        RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenant.Id, @event.Id, "ANALYTICS", AnalyticsNow);
        RegistrationRequirement requirement = RegistrationRequirement.Create(workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration, RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, AnalyticsNow);
        RegistrationChannel channel = RegistrationChannel.Create(requirement, 1, true, null, AnalyticsNow);
        requirement.AddChannel(channel);
        workflow.AddRequirement(requirement);
        RegistrationForm form = RegistrationForm.Create(tenant.Id, @event.Id, "platform.registration", "analytics", "Analytics", AnalyticsNow);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, AnalyticsNow);
        RegistrationFormSection section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", AnalyticsNow);
        version.AddSection(section);
        RegistrationFormField age = Field(section, 1, "age", RegistrationFieldTypeEnum.Integer, analytics: true);
        RegistrationFormField month = Field(section, 2, "month", RegistrationFieldTypeEnum.Date, analytics: true);
        RegistrationFormField attendance = Field(section, 3, "attendance", RegistrationFieldTypeEnum.SingleChoice, analytics: true);
        RegistrationFormField note = Field(section, 4, "note", RegistrationFieldTypeEnum.ShortText, analytics: false);
        RegistrationFormField consent = Field(section, 5, "consent", RegistrationFieldTypeEnum.Boolean, analytics: false, explicitConsent: true);
        version.AddField(section, age);
        version.AddField(section, month);
        version.AddField(section, attendance);
        version.AddField(section, note);
        version.AddField(section, consent);
        RegistrationFormFieldOption yes = RegistrationFormFieldOption.Create(Guid.CreateVersion7(), attendance, 1, "yes", "Yes", AnalyticsNow);
        RegistrationFormFieldOption no = RegistrationFormFieldOption.Create(Guid.CreateVersion7(), attendance, 2, "no", "No", AnalyticsNow);
        version.AddOption(attendance, yes);
        version.AddOption(attendance, no);
        form.AddVersion(version);
        context.AddRange(workflow, form);
        context.SaveChanges();

        for (int i = 0; i < 6; i++)
        {
            (RegistrationSubmission submission, Guid orderId) = AddSubmission(context, tenant.Id, @event.Id, workflow.Id, requirement.Id, channel.Id, form.Id, version.Id, user.Id, i);
            context.RegistrationAnswers.Add(RegistrationAnswer.CreateInteger(submission, age, requirement, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, orderId, 1, i < 3 ? 18 : 42, AnalyticsNow));
            context.RegistrationAnswers.Add(RegistrationAnswer.CreateDate(submission, month, requirement, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, orderId, 1, i < 4 ? new DateOnly(2026, 8, 2 + i) : new DateOnly(2026, 9, 1), AnalyticsNow));
            if (i < 5)
            {
                context.RegistrationAnswers.Add(RegistrationAnswer.CreateOption(submission, attendance, requirement, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, orderId, 1, i < 3 ? yes : no, AnalyticsNow));
            }
            context.RegistrationAnswers.Add(RegistrationAnswer.CreateText(submission, note, requirement, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, orderId, 1, "raw pii", AnalyticsNow));
            context.RegistrationAnswers.Add(RegistrationAnswer.CreateBoolean(submission, consent, requirement, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, orderId, 1, true, AnalyticsNow));
        }
        context.SaveChanges();
        return new(tenant.Id, @event.Id, form.Id, version.Id);
    }

    private static RegistrationFormField Field(RegistrationFormSection section, int ordinal, string key, RegistrationFieldTypeEnum type, bool analytics, bool explicitConsent = false) =>
        RegistrationFormField.Create(Guid.CreateVersion7(), section, ordinal, "platform.registration", key, key, type, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, explicitConsent, true, false, null, analytics, analytics, AnalyticsNow,
            explicitConsent ? "EVENT_TERMS" : null, explicitConsent ? "v1" : null, explicitConsent ? "I agree." : null);

    private static (RegistrationSubmission Submission, Guid OrderId) AddSubmission(ExploreDbContext context, Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, Guid channelId, Guid formId, Guid versionId, Guid userId, int index)
    {
        RegistrationOrder order = RegistrationOrder.Create(tenantId, eventId, userId, null, BookingPartyTypeEnum.Individual, Guid.CreateVersion7(),
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired), workflowId, null, "EUR", AnalyticsNow, AnalyticsNow.AddHours(1));
        context.RegistrationOrders.Add(order);
        context.SaveChanges();
        RegistrationAttempt attempt = RegistrationAttempt.Create(tenantId, eventId, order.Id, workflowId, requirementId, channelId, formId, versionId,
            CapabilityTokenHash.Create(Hash($"capability-{index}")), null, null, AnalyticsNow, AnalyticsNow.AddMinutes(10));
        RegistrationSubmission submission = RegistrationSubmission.Create(attempt, RegistrationEvidenceHash.Create(Hash($"evidence-{index}")), AnalyticsNow.AddMinutes(1), null, null, null, null);
        context.AddRange(attempt, submission);
        context.SaveChanges();
        return (submission, order.Id);
    }

    private static string Hash(string value) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record AnalyticsScope(Guid TenantId, Guid EventId, Guid FormId, Guid VersionId);
    private sealed record TestTenantContext(Guid TenantId) : Explore.Application.Contracts.Infrastructure.ITenantContext;
}

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class RegistrationAnswerPostgreSqlPersistenceTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime UtcNow = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);
    private const string AnswerTable = RelationalModelNamespace.Name + ".registration_answers";
    private const string AnswerSubjectTypeTable = RelationalModelNamespace.Name + ".registration_answer_subject_types";

    [Test]
    [Category("Runtime")]
    public async Task PostgreSqlCatalogContainsNamedTypedValueSubjectAndIdentityConstraints()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        IIndex durableIdentity = DurableIdentityIndex(context);
        string[] expected =
        [
            "ck_registration_answers_exactly_one_value",
            "ck_registration_answers_value_matches_field_type",
            "ck_registration_answers_subject_shape",
            "ck_registration_answers_positive_ordinal",
            durableIdentity.GetDatabaseName()
                ?? throw new InvalidOperationException("Registration answer identity index has no database name.")
        ];

        string[] actual = await context.Database.SqlQueryRaw<string>(
            $"SELECT conname AS value FROM pg_constraint WHERE conrelid = '{AnswerTable}'::regclass " +
            $"UNION ALL SELECT indexname AS value FROM pg_indexes WHERE schemaname = '{RelationalModelNamespace.Name}' AND tablename = 'registration_answers'")
            .ToArrayAsync();
        string[] subjectTypes = await context.Database.SqlQueryRaw<string>(
            $"SELECT id || ':' || master_code AS value FROM {AnswerSubjectTypeTable} ORDER BY id")
            .ToArrayAsync();

        await Assert.That(expected.Except(actual)).IsEmpty();
        await Assert.That(subjectTypes).IsEquivalentTo(
        [
            "1:REGISTRATION_ORDER",
            "2:PURCHASER",
            "3:PARTICIPANT",
            "4:TICKET_ASSIGNMENT",
            "5:SESSION_SELECTION"
        ]);
    }

    [Test]
    [Category("Runtime")]
    public async Task TextAnswerRoundTripsAndTenantFilterHidesIt()
    {
        await fixture.ResetAsync();
        AnswerScope scope = await SeedAnswerAsync();

        await using ExploreDbContext tenant = fixture.CreateTenantFilteredDbContext(new TestTenantContext(scope.TenantId));
        RegistrationAnswer persisted = await tenant.RegistrationAnswers.AsNoTracking().SingleAsync();
        await Assert.That(persisted.TextValue).IsEqualTo("Ada");

        await using ExploreDbContext otherTenant = fixture.CreateTenantFilteredDbContext(new TestTenantContext(Guid.CreateVersion7()));
        await Assert.That(await otherTenant.RegistrationAnswers.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Category("Runtime")]
    public async Task DatabaseRejectsTwoValuesWrongTypeSubjectShapeAndDuplicateIdentity()
    {
        await fixture.ResetAsync();
        AnswerScope scope = await SeedAnswerAsync();

        await AssertRejectedUpdateAsync(scope.AnswerId,
            "integer_value = 1", "ck_registration_answers_exactly_one_value");
        await AssertRejectedUpdateAsync(scope.AnswerId,
            "text_value = NULL, integer_value = 1", "ck_registration_answers_value_matches_field_type");
        await AssertRejectedUpdateAsync(scope.AnswerId, Guid.CreateVersion7(),
            "ck_registration_answers_subject_shape");

        await using ExploreDbContext context = fixture.CreateDbContext();
        IIndex durableIdentity = DurableIdentityIndex(context);
        DbUpdateException duplicate = await AssertDatabaseFailureAsync(() => context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {AnswerTable} (id, tenant_id, event_id, registration_order_id, registration_attempt_id, registration_submission_id, registration_workflow_id, registration_requirement_id, registration_form_id, registration_form_version_id, registration_form_section_id, registration_form_field_id, field_type_id, requirement_subject_type_id, requirement_subject_id, answer_subject_type_id, order_subject_id, purchaser_subject_id, participant_subject_id, ticket_assignment_subject_id, session_selection_subject_id, ordinal, text_value, integer_value, decimal_value, boolean_value, date_value, time_value, instant_value, selected_option_id, sensitive_answer_value_id, created_at, created_by, updated_at, updated_by, is_deleted, deleted_at, deleted_by) " +
            $"SELECT gen_random_uuid(), tenant_id, event_id, registration_order_id, registration_attempt_id, registration_submission_id, registration_workflow_id, registration_requirement_id, registration_form_id, registration_form_version_id, registration_form_section_id, registration_form_field_id, field_type_id, requirement_subject_type_id, requirement_subject_id, answer_subject_type_id, order_subject_id, purchaser_subject_id, participant_subject_id, ticket_assignment_subject_id, session_selection_subject_id, ordinal, text_value, integer_value, decimal_value, boolean_value, date_value, time_value, instant_value, selected_option_id, sensitive_answer_value_id, created_at, created_by, updated_at, updated_by, is_deleted, deleted_at, deleted_by FROM {AnswerTable} WHERE id = {{0}}",
            scope.AnswerId));
        PostgresException duplicatePostgres = FindPostgresException(duplicate);
        await Assert.That(duplicatePostgres.SqlState).IsEqualTo(PostgresErrorCodes.UniqueViolation);
        await Assert.That(duplicatePostgres.ConstraintName).IsEqualTo(durableIdentity.GetDatabaseName());
    }

    [Test]
    [Category("Runtime")]
    public async Task DatabaseRejectsTicketAssignmentFromWrongTargetedTicketType()
    {
        await fixture.ResetAsync();

        await Assert.That(() => SeedAnswerAsync(useWrongTicketTypeAssignment: true))
            .Throws<DbUpdateException>();
    }

    [Test]
    [Category("Runtime")]
    public async Task SensitiveAnswerForeignKeyRejectsCrossTenantCiphertext()
    {
        await fixture.ResetAsync();
        AnswerScope scope = await SeedAnswerAsync();
        Guid otherTenantId;
        Guid sensitiveId;
        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            Tenant otherTenant = new() { FullName = "Other", Slug = $"other-{Guid.NewGuid():N}", TenantStatusId = 2, TenantStatus = null! };
            setup.Tenants.Add(otherTenant);
            await setup.SaveChangesAsync();
            RegistrationSensitiveAnswerValue sensitive = RegistrationSensitiveAnswerValue.Create(
                otherTenant.Id, Convert.ToBase64String(new byte[29]), 1, UtcNow);
            setup.RegistrationSensitiveAnswerValues.Add(sensitive);
            await setup.SaveChangesAsync();
            otherTenantId = otherTenant.Id;
            sensitiveId = sensitive.Id;
        }

        await using ExploreDbContext context = fixture.CreateDbContext();
        DbUpdateException rejected = await AssertDatabaseFailureAsync(() => context.Database.ExecuteSqlRawAsync(
            $"UPDATE {AnswerTable} SET text_value = NULL, sensitive_answer_value_id = {{0}} WHERE id = {{1}}",
            sensitiveId, scope.AnswerId));
        PostgresException postgres = FindPostgresException(rejected);
        await Assert.That(postgres.SqlState).IsEqualTo(PostgresErrorCodes.ForeignKeyViolation);
        await Assert.That(otherTenantId).IsNotEqualTo(scope.TenantId);
    }

    private static IIndex DurableIdentityIndex(ExploreDbContext context) =>
        context.Model.FindEntityType(typeof(RegistrationAnswer))!
            .GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name).SequenceEqual(
            [
                nameof(RegistrationAnswer.TenantId),
                nameof(RegistrationAnswer.RegistrationSubmissionId),
                nameof(RegistrationAnswer.RegistrationFormFieldId),
                nameof(RegistrationAnswer.AnswerSubjectTypeId),
                nameof(RegistrationAnswer.EffectiveSubjectIdentity),
                nameof(RegistrationAnswer.Ordinal)
            ]));

    [Test]
    [Category("Runtime")]
    public async Task SensitiveAnswerPersistsOnlyOpaqueCiphertextAndKeyMetadata()
    {
        await fixture.ResetAsync();
        AnswerScope scope = await SeedAnswerAsync();
        string ciphertext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            RegistrationSensitiveAnswerValue sensitive = RegistrationSensitiveAnswerValue.Create(
                scope.TenantId, ciphertext, 7, UtcNow);
            setup.RegistrationSensitiveAnswerValues.Add(sensitive);
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlRawAsync(
                $"UPDATE {AnswerTable} SET text_value = NULL, sensitive_answer_value_id = {{0}} WHERE id = {{1}}",
                sensitive.Id, scope.AnswerId);
        }

        await using ExploreDbContext context = fixture.CreateDbContext();
        RegistrationAnswer answer = await context.RegistrationAnswers
            .Include(candidate => candidate.SensitiveAnswerValue)
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == scope.AnswerId);

        await Assert.That(answer.TextValue).IsNull();
        await Assert.That(answer.IntegerValue).IsNull();
        await Assert.That(answer.DecimalValue).IsNull();
        await Assert.That(answer.BooleanValue).IsNull();
        await Assert.That(answer.DateValue).IsNull();
        await Assert.That(answer.TimeValue).IsNull();
        await Assert.That(answer.InstantValue).IsNull();
        await Assert.That(answer.SelectedOptionId).IsNull();
        await Assert.That(answer.SensitiveAnswerValue!.Ciphertext).IsEqualTo(ciphertext);
        await Assert.That(answer.SensitiveAnswerValue.KeyVersion).IsEqualTo(7);
    }

    [Test]
    [Category("Runtime")]
    public async Task RetentionCleanupDeletesExpiredRegistrationDataAndPreservesConsentAuditEvidence()
    {
        await fixture.ResetAsync();
        AnswerScope scope = await SeedAnswerAsync();
        DateTime retainedAt = UtcNow.AddYears(-8);
        Guid sensitiveId;
        Guid participantId;
        Guid consentId;
        Guid historyId;
        Guid exportId;

        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            RegistrationSensitiveAnswerValue sensitive = RegistrationSensitiveAnswerValue.Create(
                scope.TenantId, Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)), 1,
                (int)RegistrationRetentionPolicyEnum.SensitiveShort, retainedAt);
            sensitiveId = sensitive.Id;
            setup.RegistrationSensitiveAnswerValues.Add(sensitive);
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlRawAsync(
                $"UPDATE {AnswerTable} SET text_value = NULL, sensitive_answer_value_id = {{0}}, retention_until = {{1}} WHERE id = {{2}}",
                sensitive.Id, retainedAt.AddDays(90), scope.AnswerId);

            RegistrationOrder order = await setup.RegistrationOrders.SingleAsync(candidate => candidate.Id == scope.OrderId);
            order.SetPii(RegistrationOrderPii.Create(
                order.Id, scope.TenantId, "Purchaser", "purchaser@example.com", null, null,
                (int)RegistrationRetentionPolicyEnum.StandardOperational, retainedAt));
            RegistrationParticipant participant = RegistrationParticipant.Create(
                scope.TenantId, order.Id, scope.UserId, ParticipantTypeEnum.Adult, null);
            participant.SetPii(RegistrationParticipantPii.Create(
                participant.Id, scope.TenantId, "Participant", "participant@example.com", null,
                (int)RegistrationRetentionPolicyEnum.StandardOperational, retainedAt));
            participantId = participant.Id;

            EventContactShareConsent consent = EventContactShareConsent.Grant(
                scope.TenantId, ContactShareConsentSubjectTypeEnum.User, scope.UserId, scope.ActorId,
                ConsentPurposeCodes.OrganizerFutureCommunications, "retained@example.com",
                "I agree", "v1", UtcNow);
            EventContactShareConsentHistory history = consent.CreateGrantHistory(
                scope.EventId, scope.OrderId, scope.ActorId, scope.UserId, UtcNow);
            EventContactShareExport export = EventContactShareExport.Request(
                scope.TenantId, scope.ActorId, scope.EventId, scope.UserId, "csv",
                "ATTENDEE_EXPORT", "[\"Email\"]", "v1", UtcNow);
            export.Complete("[\"Email\"]", new string('a', 64), 1, UtcNow);
            export.AddItem(EventContactShareExportItem.Create(export.Id, consent.Id, "{\"Email\":\"retained@example.com\"}"));
            consentId = consent.Id;
            historyId = history.Id;
            exportId = export.Id;

            setup.AddRange(participant, consent, history, export);
            await setup.SaveChangesAsync();
        }

        await using (ExploreDbContext cleanupContext = fixture.CreateDbContext())
        {
            RegistrationRetentionCleanupResult result = await new RegistrationRetentionCleanupRepository(
                    cleanupContext, new EfCoreUnitOfWork(cleanupContext))
                .CleanupTenantAsync(scope.TenantId, UtcNow, 100, CancellationToken.None);

            await Assert.That(result.AnswersDeleted).IsEqualTo(1);
            await Assert.That(result.SensitiveValuesDeleted).IsEqualTo(1);
            await Assert.That(result.OrderPiiDeleted).IsEqualTo(1);
            await Assert.That(result.ParticipantPiiDeleted).IsEqualTo(1);
        }

        await using ExploreDbContext verification = fixture.CreateDbContext();
        await Assert.That(await verification.RegistrationAnswers.IgnoreQueryFilters().AnyAsync(answer => answer.Id == scope.AnswerId)).IsFalse();
        await Assert.That(await verification.RegistrationSensitiveAnswerValues.IgnoreQueryFilters().AnyAsync(value => value.Id == sensitiveId)).IsFalse();
        await Assert.That(await verification.RegistrationOrderPii.AnyAsync(pii => pii.RegistrationOrderId == scope.OrderId)).IsFalse();
        await Assert.That(await verification.RegistrationParticipantPii.AnyAsync(pii => pii.RegistrationParticipantId == participantId)).IsFalse();
        await Assert.That(await verification.EventContactShareConsents.AnyAsync(consent => consent.Id == consentId)).IsTrue();
        await Assert.That(await verification.EventContactShareConsentHistory.AnyAsync(history => history.Id == historyId)).IsTrue();
        await Assert.That(await verification.EventContactShareExports.AnyAsync(export => export.Id == exportId)).IsTrue();
        await Assert.That(await verification.EventContactShareExportItems.AnyAsync(item => item.ExportId == exportId && item.ConsentId == consentId)).IsTrue();
    }

    [Test]
    [Category("Runtime")]
    public async Task UserErasureDeletesSubjectConsentHistoryBeforeCurrentConsent()
    {
        await fixture.ResetAsync();
        AnswerScope scope = await SeedAnswerAsync();
        Guid consentId;
        Guid historyId;
        Guid exportId;

        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            EventContactShareConsent consent = EventContactShareConsent.Grant(
                scope.TenantId, ContactShareConsentSubjectTypeEnum.User, scope.UserId, scope.ActorId,
                ConsentPurposeCodes.OrganizerFutureCommunications, "erase@example.com",
                "I agree", "v1", UtcNow);
            EventContactShareConsentHistory history = consent.CreateGrantHistory(
                scope.EventId, scope.OrderId, scope.ActorId, scope.UserId, UtcNow);
            EventContactShareExport export = EventContactShareExport.Request(
                scope.TenantId, scope.ActorId, scope.EventId, scope.UserId, "csv",
                "ATTENDEE_EXPORT", "[\"Email\"]", "v1", UtcNow);
            export.Complete("[\"Email\"]", new string('a', 64), 1, UtcNow);
            export.AddItem(EventContactShareExportItem.Create(
                export.Id, consent.Id, "{\"Email\":\"erase@example.com\"}"));
            consentId = consent.Id;
            historyId = history.Id;
            exportId = export.Id;
            setup.AddRange(consent, history, export);
            await setup.SaveChangesAsync();
        }

        await using (ExploreDbContext erasureContext = fixture.CreateDbContext())
        {
            await new UserLocationPrivacyErasureRepository(erasureContext)
                .AnonymizeRetainedAuditEvidenceAsync(scope.UserId, CancellationToken.None);
            await new UserLocationPrivacyErasureRepository(erasureContext)
                .EraseRegistrationAndLocalNotificationsAsync(scope.UserId, CancellationToken.None);
        }

        await using ExploreDbContext verification = fixture.CreateDbContext();
        await Assert.That(await verification.EventContactShareConsents.AnyAsync(consent => consent.Id == consentId)).IsFalse();
        await Assert.That(await verification.EventContactShareConsentHistory.AnyAsync(history => history.Id == historyId)).IsFalse();
        await Assert.That(await verification.EventContactShareExports.AnyAsync(export => export.Id == exportId)).IsTrue();
        EventContactShareExport retainedExport = await verification.EventContactShareExports.SingleAsync(export => export.Id == exportId);
        await Assert.That(retainedExport.ExportedByUserId).IsNull();
        await Assert.That(await verification.EventContactShareExportItems.AnyAsync(item => item.ExportId == exportId)).IsFalse();
    }

    [Test]
    [Category("Runtime")]
    public async Task ContactReadEventFilterUsesConsentHistoryProvenance()
    {
        await fixture.ResetAsync();
        AnswerScope scope = await SeedAnswerAsync();

        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            EventContactShareConsent consent = EventContactShareConsent.Grant(
                scope.TenantId, ContactShareConsentSubjectTypeEnum.User, scope.UserId, scope.ActorId,
                ConsentPurposeCodes.OrganizerFutureCommunications, "provenance@example.com",
                "I agree", "v1", UtcNow);
            setup.AddRange(consent, consent.CreateGrantHistory(
                scope.EventId, scope.OrderId, scope.ActorId, scope.UserId, UtcNow));
            await setup.SaveChangesAsync();
        }

        await using ExploreDbContext queryContext = fixture.CreateDbContext();
        var repository = new EventContactShareConsentRepository(queryContext);
        var matching = await repository.GetGrantedForRecipient(
            scope.TenantId, scope.ActorId, scope.EventId, null, 1, 20);
        var unrelated = await repository.GetGrantedForRecipient(
            scope.TenantId, scope.ActorId, Guid.CreateVersion7(), null, 1, 20);

        await Assert.That(matching.TotalCount).IsEqualTo(1);
        await Assert.That(unrelated.TotalCount).IsEqualTo(0);
    }

    private async Task<AnswerScope> SeedAnswerAsync(bool useWrongTicketTypeAssignment = false)
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        Tenant tenant = new() { FullName = "Answers", Slug = $"answers-{Guid.NewGuid():N}", TenantStatusId = 2, TenantStatus = null! };
        User user = new() { Pii = new UserPii { Email = $"{Guid.NewGuid():N}@example.com", FirstName = "Answer", LastName = "Owner" } };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        Actor actor = new() { Pii = new ActorPii { DisplayName = "Answers" }, ActorTypeId = 1, ActorType = null!, UserId = user.Id };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();
        Explore.Domain.Event @event = new(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = "Answers",
            ActorId = actor.Id,
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
        context.Events.Add(@event);
        await context.SaveChangesAsync();

        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenant.Id, @event.Id, "EUR", 1);
        EventTicketType? targetedTicketType = null;
        EventTicketType? wrongTicketType = null;
        if (useWrongTicketTypeAssignment)
        {
            targetedTicketType = FreeTicketType(tenant.Id, catalog.Id, "Targeted");
            wrongTicketType = FreeTicketType(tenant.Id, catalog.Id, "Wrong");
            catalog.AddTicketType(targetedTicketType, null);
            catalog.AddTicketType(wrongTicketType, null);
            catalog.AddEntitlement(targetedTicketType,
                TicketTypeEntitlement.CreateForEvent(targetedTicketType.Id, tenant.Id, @event.Id, 1));
            catalog.AddEntitlement(wrongTicketType,
                TicketTypeEntitlement.CreateForEvent(wrongTicketType.Id, tenant.Id, @event.Id, 1));
            catalog.Publish();
        }
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenant.Id, @event.Id, "ANSWERS", UtcNow);
        RegistrationRequirement requirement = RegistrationRequirement.Create(
            workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            useWrongTicketTypeAssignment
                ? RegistrationRequirementSubjectTypeEnum.SpecificTicketType
                : RegistrationRequirementSubjectTypeEnum.AllOrders,
            targetedTicketType?.Id, UtcNow);
        RegistrationChannel channel = RegistrationChannel.Create(requirement, 1, true, null, UtcNow);
        requirement.AddChannel(channel);
        workflow.AddRequirement(requirement);
        RegistrationForm form = RegistrationForm.Create(tenant.Id, @event.Id, "platform.registration", "answers", "Answers", UtcNow);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, UtcNow);
        RegistrationFormSection section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", UtcNow);
        RegistrationFormField field = RegistrationFormField.Create(
            Guid.CreateVersion7(), section, 1, "platform.registration", "name", "Name",
            RegistrationFieldTypeEnum.ShortText, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, true, UtcNow);
        version.AddSection(section);
        version.AddField(section, field);
        form.AddVersion(version);
        context.AddRange(catalog, workflow, form);
        await context.SaveChangesAsync();

        RegistrationOrder order = RegistrationOrder.Create(
            tenant.Id, @event.Id, user.Id, null, BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            workflow.Id, null, "EUR", UtcNow, UtcNow.AddHours(1));
        RegistrationTicketAssignment? wrongAssignment = null;
        Guid? wrongAssignmentOrderLineId = null;
        if (useWrongTicketTypeAssignment)
        {
            RegistrationOrderLine targetedLine = RegistrationOrderLine.Create(
                catalog, targetedTicketType!, order.Id, 1, null, null);
            RegistrationOrderLine wrongLine = RegistrationOrderLine.Create(
                catalog, wrongTicketType!, order.Id, 1, null, null);
            order.AddLine(targetedLine);
            order.AddLine(wrongLine);
            wrongAssignment = RegistrationTicketAssignment.Create(
                tenant.Id, order.Id, wrongLine.Id, 1, null, AssignmentStatusEnum.Unassigned, null, UtcNow);
            wrongAssignmentOrderLineId = wrongLine.Id;
        }
        context.RegistrationOrders.Add(order);
        if (wrongAssignment is not null)
        {
            context.RegistrationTicketAssignments.Add(wrongAssignment);
        }
        await context.SaveChangesAsync();
        RegistrationAttempt attempt = RegistrationAttempt.Create(
            tenant.Id, @event.Id, order.Id, workflow.Id, requirement.Id, channel.Id, form.Id, version.Id,
            CapabilityTokenHash.Create(Hash("capability")), null, null, UtcNow, UtcNow.AddMinutes(10));
        RegistrationSubmission submission = RegistrationSubmission.Create(
            attempt, RegistrationEvidenceHash.Create(Hash("evidence")), UtcNow.AddMinutes(1), null, null, null, null);
        context.AddRange(attempt, submission);
        await context.SaveChangesAsync();
        RegistrationAnswer answer = RegistrationAnswer.CreateText(
            submission, field, requirement,
            useWrongTicketTypeAssignment
                ? RegistrationAnswerSubjectTypeEnum.TicketAssignment
                : RegistrationAnswerSubjectTypeEnum.RegistrationOrder,
            wrongAssignment?.Id ?? order.Id, 1, "Ada", UtcNow.AddMinutes(2), wrongAssignmentOrderLineId);
        context.RegistrationAnswers.Add(answer);
        await context.SaveChangesAsync();
        return new(tenant.Id, user.Id, actor.Id, @event.Id, order.Id, answer.Id);
    }

    private async Task AssertRejectedUpdateAsync(Guid answerId, string assignment, string constraint)
    {
        string updateSql = assignment switch
        {
            "integer_value = 1" =>
                "UPDATE " + AnswerTable + " SET integer_value = 1 WHERE id = {0}",
            "text_value = NULL, integer_value = 1" =>
                "UPDATE " + AnswerTable + " SET text_value = NULL, integer_value = 1 WHERE id = {0}",
            _ => throw new ArgumentOutOfRangeException(nameof(assignment), assignment, "Unhandled assignment fixture.")
        };

        await using ExploreDbContext context = fixture.CreateDbContext();
        DbUpdateException rejected = await AssertDatabaseFailureAsync(() => context.Database.ExecuteSqlRawAsync(updateSql, answerId));
        await Assert.That(FindPostgresException(rejected).ConstraintName).IsEqualTo(constraint);
    }

    private async Task AssertRejectedUpdateAsync(Guid answerId, Guid participantSubjectId, string constraint)
    {
        const string subjectShapeSql = "UPDATE " + AnswerTable +
            " SET order_subject_id = NULL, participant_subject_id = {0}, answer_subject_type_id = 3 WHERE id = {1}";

        await using ExploreDbContext context = fixture.CreateDbContext();
        DbUpdateException rejected = await AssertDatabaseFailureAsync(() =>
            context.Database.ExecuteSqlRawAsync(subjectShapeSql, participantSubjectId, answerId));
        await Assert.That(FindPostgresException(rejected).ConstraintName).IsEqualTo(constraint);
    }

    private static async Task<DbUpdateException> AssertDatabaseFailureAsync(Func<Task<int>> action)
    {
        Exception exception = (await Assert.That(action).Throws<Exception>())!;
        return exception as DbUpdateException ?? new DbUpdateException("PostgreSQL rejected the row.", exception);
    }

    private static PostgresException FindPostgresException(Exception exception) =>
        exception is PostgresException postgres
            ? postgres
            : exception.InnerException is not null
                ? FindPostgresException(exception.InnerException)
                : throw new InvalidOperationException("Expected PostgreSQL failure.", exception);

    private static string Hash(string value) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static EventTicketType FreeTicketType(Guid tenantId, Guid catalogId, string name) => EventTicketType.Create(
        Guid.CreateVersion7(), tenantId, catalogId, name, "EUR", TicketPricingModeEnum.Free,
        null, null, null, ParticipantDataCollectionModeEnum.None,
        null, null, null, false, false, null, null, null, null);

    private sealed record AnswerScope(Guid TenantId, Guid UserId, Guid ActorId, Guid EventId, Guid OrderId, Guid AnswerId);
    private sealed record TestTenantContext(Guid TenantId) : Explore.Application.Contracts.Infrastructure.ITenantContext;
}
