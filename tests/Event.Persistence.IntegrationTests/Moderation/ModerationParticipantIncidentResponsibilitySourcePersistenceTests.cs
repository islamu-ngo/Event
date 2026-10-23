using System.Data;
using System.Runtime.InteropServices;
using System.Text;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>
/// Storage witnesses reuse the native proof fixture, including real issuance and original narrative custody.
/// Authored before the mapping; programme assurance owns execution.
/// </summary>
public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments("missing-trust")]
    [Arguments("foreign-trust")]
    [Arguments("foreign-tenant")]
    [Arguments("foreign-tenant-ignore-general-filter")]
    public async Task AssessmentCustodyCannotReadThroughForeignScopeOrGeneralFilterBypass(string scope)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await PersistIncidentAssessmentAsync(database);
        await using var context = database.Context();
        if (scope == "missing-trust") context.ModerationTrustDomainId = null;
        if (scope == "foreign-trust") context.ModerationTrustDomainId = Guid.CreateVersion7();
        if (scope.StartsWith("foreign-tenant", StringComparison.Ordinal))
            context.TenantContext = new SelectedTenant(Guid.CreateVersion7());
        var query = context.Set<ModerationParticipantIncidentResponsibilitySource>().AsNoTracking();
        if (scope == "foreign-tenant-ignore-general-filter")
            query = query.IgnoreQueryFilters(["Tenant"]);
        await Assert.That(await query.AnyAsync(row => row.Id == source.Id)).IsFalse();
    }

    [Test]
    public async Task IncidentAssessmentRoundtripRetainsOpaqueGenerationsExactDeadlinesAndOwnedCiphertext()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await PersistIncidentAssessmentAsync(database);
        await using var context = database.Context();
        var retained = await context.Set<ModerationParticipantIncidentResponsibilitySource>()
            .AsNoTracking().SingleAsync(row => row.Id == source.Id);
        retained.ValidatePersistedState();
        await Assert.That(retained.EvidenceSourceVersion).IsEqualTo(source.EvidenceSourceVersion);
        await Assert.That(retained.EventVersion).IsEqualTo(source.EventVersion);
        await Assert.That(retained.SessionGeneration).IsEqualTo(source.SessionGeneration);
        await Assert.That(retained.OrderGeneration).IsEqualTo(source.OrderGeneration);
        await Assert.That(retained.ParticipantGeneration).IsEqualTo(source.ParticipantGeneration);
        await Assert.That(retained.EvidenceSourceVersion.Version).IsEqualTo(4);
        await Assert.That(retained.EventVersion.Version).IsEqualTo(4);
        await Assert.That(retained.SessionGeneration.Version).IsEqualTo(4);
        await Assert.That(retained.OrderGeneration.Version).IsEqualTo(4);
        await Assert.That(retained.ParticipantGeneration.Version).IsEqualTo(4);
        await Assert.That(retained.QualifiedAt).IsEqualTo(source.QualifiedAt);
        await Assert.That(retained.ValidUntil).IsEqualTo(source.ValidUntil);
        await Assert.That(retained.PurgeAt).IsEqualTo(source.PurgeAt);
        await Assert.That(retained.OriginalNarrativePurgeAt).IsEqualTo(source.OriginalNarrativePurgeAt);
        await Assert.That(retained.ReadGrantExpiresAt).IsEqualTo(source.ReadGrantExpiresAt);
        await Assert.That(retained.CustodySourceValidUntil).IsEqualTo(source.CustodySourceValidUntil);
        await Assert.That(retained.CustodyAdoptionValidUntil).IsEqualTo(source.CustodyAdoptionValidUntil);
        await Assert.That(retained.IncidentWindowFrom).IsEqualTo(source.IncidentWindowFrom);
        await Assert.That(retained.IncidentWindowUntil).IsEqualTo(source.IncidentWindowUntil);
        await Assert.That(retained.AssessmentDigest).IsEqualTo(source.AssessmentDigest);
        await Assert.That(retained.ProtectedAssessment.Span.SequenceEqual(source.ProtectedAssessment.Span)).IsTrue();
        var exposed = retained.ProtectedAssessment;
        await Assert.That(MemoryMarshal.TryGetArray(exposed, out var copy)).IsTrue();
        copy.Array![copy.Offset] ^= 0xff;
        await Assert.That(retained.ProtectedAssessment.Span.SequenceEqual(source.ProtectedAssessment.Span)).IsTrue();
        // Native read issuance may be performed by the assessor; independence concerns the alleged subject.
        await Assert.That(retained.AssessorHumanId).IsEqualTo(retained.ReadIssuerHumanId);
        await Assert.That(retained.SubjectId).IsNotEqualTo(retained.AssessorHumanId);
        await Assert.That(retained.SubjectKind).IsEqualTo(ModerationSubjectKind.Human);
        await Assert.That(retained.Purpose).IsEqualTo(ModerationCaseNarrativePurpose.ParticipantInvestigation);
        await Assert.That(retained.TargetKind).IsEqualTo(ModerationCaseTargetKind.ParticipantIncident);
        await Assert.That(retained.Revision).IsEqualTo(1L);
        await Assert.That(await context.ModerationFindings.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationSanctions.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments(nameof(ModerationParticipantIncidentResponsibilitySource.ValidUntil))]
    [Arguments(nameof(ModerationParticipantIncidentResponsibilitySource.PurgeAt))]
    [Arguments(nameof(ModerationParticipantIncidentResponsibilitySource.OriginalNarrativePurgeAt))]
    [Arguments(nameof(ModerationParticipantIncidentResponsibilitySource.ReadGrantExpiresAt))]
    [Arguments(nameof(ModerationParticipantIncidentResponsibilitySource.CustodySourceValidUntil))]
    [Arguments(nameof(ModerationParticipantIncidentResponsibilitySource.CustodyAdoptionValidUntil))]
    [Arguments(nameof(ModerationParticipantIncidentResponsibilitySource.ParticipantGeneration))]
    [Arguments("protectedAssessment")]
    public async Task PersistedIncidentAssessmentCannotRewriteOriginOrRenewCustody(string property)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await PersistIncidentAssessmentAsync(database);
        await using var context = database.Context();
        var retained = await context.Set<ModerationParticipantIncidentResponsibilitySource>().SingleAsync();
        var entry = context.Entry(retained).Property(property);
        entry.CurrentValue = entry.CurrentValue switch
        {
            DateTimeOffset deadline => deadline.AddTicks(1),
            Guid => Guid.NewGuid(),
            byte[] => database.NarrativeProtection.CreateProtector("incident-assessment-storage")
                .Protect(Encoding.UTF8.GetBytes("A different encrypted investigator assessment.")),
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
        await Assert.That(async () => await context.SaveChangesAsync()).Throws<InvalidOperationException>();
        await using var verify = database.Context();
        var unchanged = await verify.Set<ModerationParticipantIncidentResponsibilitySource>().AsNoTracking().SingleAsync();
        await Assert.That(unchanged.PurgeAt).IsEqualTo(source.PurgeAt);
        await Assert.That(unchanged.ValidUntil).IsEqualTo(source.ValidUntil);
        await Assert.That(unchanged.ProtectedAssessment.Span.SequenceEqual(source.ProtectedAssessment.Span)).IsTrue();
    }

    [Test]
    [Arguments("purpose")]
    [Arguments("target")]
    [Arguments("subject-kind")]
    [Arguments("self-assessor")]
    [Arguments("self-issuer")]
    [Arguments("evidence-revision")]
    [Arguments("read-revision")]
    [Arguments("custody-generation")]
    [Arguments("qualification-limit")]
    [Arguments("retention-limit")]
    [Arguments("validity")]
    [Arguments("purge-before-validity")]
    [Arguments("original-deadline")]
    [Arguments("read-deadline")]
    [Arguments("adoption-deadline")]
    [Arguments("source-deadline")]
    [Arguments("infinite-deadline")]
    [Arguments("unrecorded-assessment")]
    [Arguments("unrecorded-window")]
    [Arguments("incident-window")]
    [Arguments("incident-after-assessment")]
    [Arguments("partial-withdrawal")]
    [Arguments("null-withdrawal")]
    [Arguments("regressed-withdrawal")]
    [Arguments("infinite-withdrawal")]
    public async Task NativeDatabaseRejectsMalformedIncidentAssessmentWithoutDomainValidation(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await PersistIncidentAssessmentAsync(database);
        await using var context = database.Context();
        // Deliberately bypass the entity's private setters to prove the actual relational boundary.
        var assignment = defect switch
        {
            "purpose" => "purpose = 1",
            "target" => "target_kind = 1",
            "subject-kind" => "subject_kind = 2",
            "self-assessor" => "subject_id = assessor_human_id",
            "self-issuer" => "subject_id = read_issuer_human_id",
            "evidence-revision" => "evidence_case_revision = occurrence_case_revision + 1",
            "read-revision" => "read_grant_revision = 2",
            "custody-generation" => "custody_adoption_writer_generation = custody_source_writer_generation",
            "qualification-limit" => "maximum_qualification_lifetime_ticks = 1",
            "retention-limit" => "maximum_retention_ticks = 1",
            "validity" => "valid_until = qualified_at",
            "purge-before-validity" => "purge_at = valid_until - 1",
            "original-deadline" => "original_narrative_purge_at = purge_at - 1",
            "read-deadline" => "read_grant_expires_at = purge_at - 1",
            "adoption-deadline" => "custody_adoption_valid_until = purge_at - 1",
            "source-deadline" => "custody_source_valid_until = custody_adoption_valid_until - 1",
            "infinite-deadline" => "custody_source_valid_until = 3155378975999999999",
            "unrecorded-assessment" => "qualified_at = 0",
            "unrecorded-window" => "incident_window_from = 0",
            "incident-window" => "incident_window_from = incident_window_until",
            "incident-after-assessment" => "incident_window_until = qualified_at + 1",
            "partial-withdrawal" => "withdrawn_by_human_id = assessor_human_id",
            "null-withdrawal" => "revision = 2, withdrawn_by_human_id = assessor_human_id, withdrawal_operation_id = id",
            "regressed-withdrawal" => "revision = 2, withdrawn_at = qualified_at - 1, withdrawn_by_human_id = assessor_human_id, withdrawal_operation_id = id",
            "infinite-withdrawal" => "revision = 2, withdrawn_at = 3155378975999999999, withdrawn_by_human_id = assessor_human_id, withdrawal_operation_id = id",
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        await Assert.That(async () => await context.Database.ExecuteSqlRawAsync(
            $"UPDATE moderation_participant_incident_responsibility_sources SET {assignment} WHERE id = {{0}}",
            source.Id)).Throws<Microsoft.Data.Sqlite.SqliteException>();
        var retained = await context.Set<ModerationParticipantIncidentResponsibilitySource>().AsNoTracking().SingleAsync();
        retained.ValidatePersistedState();
        await Assert.That(retained.Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments("narrative")]
    [Arguments("source")]
    [Arguments("case")]
    [Arguments("read")]
    [Arguments("policy")]
    [Arguments("session")]
    [Arguments("order")]
    [Arguments("participant")]
    [Arguments("subject")]
    [Arguments("custody-tenant")]
    [Arguments("custody-trust")]
    [Arguments("custody-source")]
    public async Task IncidentAssessmentCannotSubstituteMissingParentsOrForeignCustody(string parent)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await PersistIncidentAssessmentAsync(database);
        await using var context = database.Context();
        var column = parent switch
        {
            "narrative" => "original_narrative_id",
            "source" => "evidence_source_id",
            "case" => "occurrence_case_id",
            "read" => "read_grant_id",
            "policy" => "qualification_policy_id",
            "session" => "session_id",
            "order" => "order_id",
            "participant" => "participant_id",
            "subject" => "subject_id",
            "custody-tenant" => "tenant_id",
            "custody-trust" => "trust_domain_id",
            "custody-source" => "custody_source_id",
            _ => throw new ArgumentOutOfRangeException(nameof(parent))
        };
        await Assert.That(async () => await context.Database.ExecuteSqlRawAsync(
            $"UPDATE moderation_participant_incident_responsibility_sources SET {column} = {{0}} WHERE id = {{1}}",
            Guid.CreateVersion7(), source.Id)).Throws<Microsoft.Data.Sqlite.SqliteException>();
    }

    [Test]
    [Arguments("narrative")]
    [Arguments("read")]
    [Arguments("policy")]
    [Arguments("session")]
    [Arguments("custody-adoption")]
    public async Task RetainedAssessmentRestrictsDeletionOfItsActualParents(string parent)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await PersistIncidentAssessmentAsync(database);
        await using var context = database.Context();
        await Assert.That(async () =>
        {
            _ = parent switch
            {
                "narrative" => await context.Set<ModerationCaseNarrative>().Where(row => row.Id == source.OriginalNarrativeId).ExecuteDeleteAsync(),
                "read" => await context.Set<ModerationCaseReadGrant>().Where(row => row.Id == source.ReadGrantId).ExecuteDeleteAsync(),
                "policy" => await context.Set<ModerationCaseTargetPolicy>().Where(row => row.Id == source.QualificationPolicyId).ExecuteDeleteAsync(),
                "session" => await context.EventSessions.Where(row => row.Id == source.SessionId).ExecuteDeleteAsync(),
                "custody-adoption" => await context.Set<ModerationCaseNarrativeAdoption>().Where(row => row.Id == source.CustodyAdoptionId).ExecuteDeleteAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(parent))
            };
        }).Throws<Microsoft.Data.Sqlite.SqliteException>();
        await Assert.That(await context.Set<ModerationParticipantIncidentResponsibilitySource>().CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task CompetingAssessmentWithdrawalsRejectStaleRevisionAndPreserveOriginalDeadlines()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.ParticipantIncident);
        var source = await PersistIncidentAssessmentAsync(database);
        await using var first = database.Context();
        await using var competing = database.Context();
        var winner = await first.Set<ModerationParticipantIncidentResponsibilitySource>().SingleAsync();
        var stale = await competing.Set<ModerationParticipantIncidentResponsibilitySource>().SingleAsync();
        var operationId = Guid.CreateVersion7();
        winner.Withdraw(operationId, 1, source.AssessorHumanId, source.QualifiedAt.AddTicks(1));
        stale.Withdraw(Guid.CreateVersion7(), 1, source.AssessorHumanId, source.QualifiedAt.AddTicks(2));
        await first.SaveChangesAsync();
        await Assert.That(async () => await competing.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
        await using var verify = database.Context();
        var retained = await verify.Set<ModerationParticipantIncidentResponsibilitySource>().AsNoTracking().SingleAsync();
        await Assert.That(retained.WithdrawalOperationId).IsEqualTo(operationId);
        await Assert.That(retained.WithdrawnAt).IsEqualTo(source.QualifiedAt.AddTicks(1));
        await Assert.That(retained.Revision).IsEqualTo(2L);
        await Assert.That(retained.ValidUntil).IsEqualTo(source.ValidUntil);
        await Assert.That(retained.PurgeAt).IsEqualTo(source.PurgeAt);
        await Assert.That(retained.IsCurrentAt(source.QualifiedAt)).IsFalse();
    }

    private static async Task<ModerationParticipantIncidentResponsibilitySource> PersistIncidentAssessmentAsync(
        NativeDatabase database)
    {
        await using (var native = database.Context())
        {
            await native.Events.Where(row => row.Id == database.EventId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid()));
            await native.RegistrationOrders.Where(row => row.Id == database.OrderId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid()));
            await native.RegistrationParticipants.Where(row => row.Id == database.ParticipantId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.ConcurrencyStamp, Guid.NewGuid()));
        }
        var issued = await database.IssueAsync();
        await using (var evidence = database.Context())
            await evidence.ModerationCaseTargetSources.Where(row => row.Id == issued.Receipt.SourceId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Version, Guid.NewGuid()));
        issued = await database.IssueAsync();
        Guid caseId;
        await using (var intake = database.Context())
        {
            var accepted = await Committer(intake, database).SubmitAsync(
                new(ModerationCaseAccessPurpose.SubmitAllegation, Guid.Empty, Guid.CreateVersion7(), 0,
                    issued.Receipt.Id, issued.Token) { Narrative = database.Request().Material }, default);
            await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
            caseId = accepted.Acknowledgement!.CaseId;
        }
        var qualifiedAt = database.Clock.Now.AddTicks(7);
        Guid readGrantId;
        await using (var read = database.Context())
        {
            await using var transaction = await read.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = database.Owner(read);
            var grant = await owner.GrantReadAsync(new(Guid.CreateVersion7(), caseId, 1,
                issued.Receipt.SourceId, database.CallerId, database.Clock.Now.AddMinutes(2)), default);
            await Assert.That(grant).IsNotNull();
            readGrantId = grant!.Id;
            await read.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
        }
        await using var context = database.Context();
        var host = await context.ModerationHostBindings.SingleAsync();
        var allegationPolicy = await context.Set<ModerationCaseNarrativePolicySource>().SingleAsync();
        var custody = ModerationCaseNarrativePolicySource.Record(Guid.CreateVersion7(), host.Id,
            host.TrustDomainId, database.TenantId, ModerationCaseTargetKind.ParticipantIncident,
            new(ModerationCaseNarrativePurpose.ParticipantInvestigation, TimeSpan.FromMinutes(30), 4096),
            allegationPolicy.Ownership, allegationPolicy.RecordedByHumanId, 4, qualifiedAt,
            qualifiedAt.AddHours(1).AddTicks(3));
        var adoption = ModerationCaseNarrativeAdoption.Adopt(Guid.CreateVersion7(), custody,
            custody.PrivacyOwnerHumanId, 5, qualifiedAt, qualifiedAt.AddMinutes(30).AddTicks(2));
        var subjectId = Guid.CreateVersion7();
        var subject = new User
        {
            Id = subjectId, Pii = new UserPii
            {
                UserId = subjectId, Email = $"{subjectId:N}@example.invalid", FirstName = "Alleged", LastName = "Subject"
            }
        };
        var parent = await context.Events.SingleAsync(row => row.Id == database.EventId);
        var session = new EventSession
        {
            Id = Guid.CreateVersion7(), EventId = parent.Id, Event = parent,
            TenantId = database.TenantId, Tenant = null!, ConcurrencyStamp = Guid.NewGuid()
        };
        context.AddRange(subject, session, custody, adoption);
        await context.SaveChangesAsync();
        var occurrence = await context.ModerationCases.Include(row => row.Evidence).SingleAsync(row => row.Id == caseId);
        var ciphertext = database.NarrativeProtection.CreateProtector("incident-assessment-storage")
            .Protect(Encoding.UTF8.GetBytes("Authenticated incident-specific investigator assessment."));
        var expectedCiphertext = ciphertext.ToArray();
        var source = ModerationParticipantIncidentResponsibilitySource.Record(Guid.CreateVersion7(), host,
            occurrence, 1, await context.ModerationCaseTargetSources.SingleAsync(row => row.Id == issued.Receipt.SourceId),
            await context.Set<ModerationCaseNarrative>().SingleAsync(row => row.CaseId == caseId),
            session, await context.RegistrationOrders.SingleAsync(row => row.Id == database.OrderId),
            await context.RegistrationParticipants.SingleAsync(row => row.Id == database.ParticipantId),
            SubjectReference.Create(ModerationSubjectKind.Human, subjectId, occurrence.Scope.Parent!),
            database.AdminId, await context.Set<ModerationCaseTargetPolicy>().SingleAsync(), custody, adoption,
            await context.ModerationCaseReadGrants.SingleAsync(row => row.Id == readGrantId),
            qualifiedAt.AddMinutes(-1).AddTicks(1), qualifiedAt.AddTicks(-1), qualifiedAt,
            qualifiedAt.AddMinutes(1).AddTicks(1), qualifiedAt.AddSeconds(90).AddTicks(2),
            ciphertext, new string('a', 64));
        Array.Fill(ciphertext, (byte)0);
        await Assert.That(source.ProtectedAssessment.Span.SequenceEqual(expectedCiphertext)).IsTrue();
        context.Add(source);
        await context.SaveChangesAsync();
        return source;
    }
}
