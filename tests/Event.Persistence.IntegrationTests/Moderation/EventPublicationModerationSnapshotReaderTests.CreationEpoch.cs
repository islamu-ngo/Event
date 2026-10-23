using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>Creation may account for its own epoch advancement, never a changed authority snapshot.</summary>
public sealed class EventPublicationModerationSnapshotReaderCreationEpochTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(2, false)]
    [Arguments(2, true)]
    public async Task Only_the_known_single_creation_advance_matches_otherwise_unchanged_installed_facts(
        int advances, bool adopted)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        if (adopted) await PublishAsync(database);
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var original = await ReadAsync(context, database);
        var repeated = await ReadAsync(context, database);
        var epoch = await context.ModerationQualificationEpochs.SingleAsync();
        var beforeRevision = epoch.Revision;

        await Assert.That(original.IsAdopted).IsEqualTo(adopted);
        await Assert.That(original.PermitsAt(Now)).IsTrue();
        await Assert.That(original == repeated).IsTrue();
        await Assert.That(original.QualificationEpochId).IsEqualTo((Guid?)epoch.Id);
        await Assert.That(original.QualificationRevision).IsEqualTo(beforeRevision);
        for (var advance = 0; advance < advances; advance++) epoch.Advance(epoch.Revision);
        await context.SaveChangesAsync();

        var current = await ReadAsync(context, database);
        var expectedOwnAdvance = original with { QualificationRevision = checked(beforeRevision + 1) };

        await Assert.That(current.QualificationEpochId).IsEqualTo(original.QualificationEpochId);
        await Assert.That(current.QualificationRevision).IsEqualTo(epoch.Revision);
        await Assert.That(current.Generation).IsEqualTo(original.Generation);
        await Assert.That(current == original).IsFalse();
        await Assert.That(current == expectedOwnAdvance).IsEqualTo(advances == 1);
        await transaction.CommitAsync();

        await using var committedContext = database.Context();
        await using var committedRead = await committedContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await ReadAsync(committedContext, database) == current).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_new_native_sanction_or_protection_cannot_hide_behind_the_creation_advance(bool protective)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var publication = await PublishAsync(database);
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var original = await ReadAsync(context, database);
        await Assert.That(original.PermitsAt(Now)).IsTrue();
        var epoch = await context.ModerationQualificationEpochs.SingleAsync();
        epoch.Advance(original.QualificationRevision);
        AddContribution(context, database, publication, protective, Now);
        await context.SaveChangesAsync();

        var current = await ReadAsync(context, database);

        await Assert.That(current.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(current.PermitsAt(Now)).IsFalse();
        await Assert.That(current.Generation).IsNotEqualTo(original.Generation);
        await Assert.That(current == (original with { QualificationRevision = epoch.Revision })).IsFalse();
    }

    [Test]
    public async Task Accounting_for_creation_does_not_renew_the_original_scheduled_restriction_deadline()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var publication = await PublishAsync(database);
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var startsAt = Now.AddMinutes(1);
        AddContribution(context, database, publication, protective: true, startsAt: startsAt);
        await context.SaveChangesAsync();
        var original = await ReadAsync(context, database);
        var epoch = await context.ModerationQualificationEpochs.SingleAsync();
        epoch.Advance(original.QualificationRevision);
        await context.SaveChangesAsync();

        var adjusted = original with { QualificationRevision = epoch.Revision };
        var current = await ReadAsync(context, database, startsAt.AddTicks(-1));
        var effective = await ReadAsync(context, database, startsAt);

        await Assert.That(original.CurrentUntil).IsEqualTo((DateTimeOffset?)startsAt);
        await Assert.That(current == adjusted).IsTrue();
        await Assert.That(adjusted.PermitsAt(startsAt.AddTicks(-1))).IsTrue();
        await Assert.That(adjusted.PermitsAt(startsAt)).IsFalse();
        await Assert.That(adjusted.PermitsAt(Now.ToOffset(TimeSpan.FromHours(1)))).IsFalse();
        await Assert.That((adjusted with { CurrentUntil = startsAt.AddMinutes(1) }) == current).IsFalse();
        await Assert.That(effective.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(effective == adjusted).IsFalse();
    }

    [Test]
    public async Task A_changed_installed_host_coordinate_cannot_hide_behind_the_creation_advance()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var original = await ReadAsync(context, database);
        await Assert.That(original.PermitsAt(Now)).IsTrue();
        var epoch = await context.ModerationQualificationEpochs.SingleAsync();
        epoch.Advance(original.QualificationRevision);
        var principal = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(), Code = $"replacement-{database.TenantId:N}", DisplayName = "Replacement maintenance"
        };
        context.ServicePrincipals.Add(principal);
        await context.SaveChangesAsync();
        await context.ModerationHostBindings.Where(row => row.Id == database.HostId).ExecuteUpdateAsync(update =>
            update.SetProperty(row => row.MaintenanceServicePrincipalId, principal.Id));

        var current = await ReadAsync(context, database);

        await Assert.That(current.PermitsAt(Now)).IsTrue();
        await Assert.That(current.QualificationEpochId).IsEqualTo(original.QualificationEpochId);
        await Assert.That(current.Generation).IsNotEqualTo(original.Generation);
        await Assert.That(current == (original with { QualificationRevision = epoch.Revision })).IsFalse();
    }

    [Test]
    public async Task A_replacement_epoch_is_not_the_original_creation_epoch_even_at_the_expected_revision()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var original = await ReadAsync(context, database);
        var epoch = await context.ModerationQualificationEpochs.SingleAsync();
        context.ModerationQualificationEpochs.Remove(epoch);
        await context.SaveChangesAsync();
        var replacement = ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(),
            await context.ModerationHostBindings.SingleAsync(),
            await context.Tenants.SingleAsync(row => row.Id == database.TenantId));
        replacement.Advance(original.QualificationRevision);
        context.ModerationQualificationEpochs.Add(replacement);
        await context.SaveChangesAsync();

        var current = await ReadAsync(context, database);

        await Assert.That(current.PermitsAt(Now)).IsTrue();
        await Assert.That(current.QualificationEpochId).IsEqualTo((Guid?)replacement.Id);
        await Assert.That(current.QualificationEpochId).IsNotEqualTo(original.QualificationEpochId);
        await Assert.That(current.QualificationRevision).IsEqualTo(checked(original.QualificationRevision + 1));
        await Assert.That(current == (original with { QualificationRevision = replacement.Revision })).IsFalse();
    }

    [Test]
    public async Task A_missing_installed_epoch_stays_unavailable_after_an_original_positive_snapshot()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var original = await ReadAsync(context, database);
        await Assert.That(original.PermitsAt(Now)).IsTrue();
        await context.ModerationQualificationEpochs.Where(row => row.Id == database.EpochId).ExecuteDeleteAsync();

        var current = await ReadAsync(context, database);

        await Assert.That(current.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unavailable);
        await Assert.That(current.PermitsAt(Now)).IsFalse();
        await Assert.That(current == (original with { QualificationRevision = checked(original.QualificationRevision + 1) }))
            .IsFalse();
    }

    [Test]
    public async Task Positive_missing_host_nonadoption_has_no_epoch_coordinates()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var installed = await ReadAsync(context, database);
        context.ModerationQualificationEpochs.RemoveRange(await context.ModerationQualificationEpochs.ToArrayAsync());
        context.ModerationHostBindings.RemoveRange(await context.ModerationHostBindings.ToArrayAsync());
        await context.SaveChangesAsync();

        var missingHost = await ReadAsync(context, database);

        await Assert.That(missingHost == EventPublicationModerationFacts.NotAdopted).IsTrue();
        await Assert.That(missingHost.QualificationEpochId).IsNull();
        await Assert.That(missingHost.QualificationRevision).IsEqualTo(0L);
        await Assert.That(missingHost.PermitsAt(Now)).IsTrue();
        await Assert.That(missingHost == installed).IsFalse();
    }

    private static async Task<EventPublicationModerationFacts> ReadAsync(
        ExploreDbContext context, ModerationQualificationWriterTests.SourceDatabase database, DateTimeOffset? at = null)
    {
        var facts = await new EventPublicationModerationSnapshotReader(context,
            ModerationRecognitionResourceFixture.Reader(context)).ReadAsync(database.SubjectId, at ?? Now, CancellationToken.None);
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        return facts;
    }

    private static async Task<ModerationPolicyPublication> PublishAsync(
        ModerationQualificationWriterTests.SourceDatabase database)
    {
        var result = await database.PublishAsync(database.AdministratorId,
            database.Publication(1, ModerationReferenceProfileId.CFG06));
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        return result.Publication!;
    }

    private static void AddContribution(ExploreDbContext context,
        ModerationQualificationWriterTests.SourceDatabase database, ModerationPolicyPublication publication,
        bool protective, DateTimeOffset startsAt)
    {
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, database.SubjectId, database.Instance);
        var issuer = SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.Instance);
        var scope = database.Scope;
        if (protective)
        {
            context.ModerationProtectiveMeasures.Add(ModerationProtectiveMeasure.Create(Guid.CreateVersion7(),
                subject, issuer, scope, scope, [ModerationCapability.Publish], "Bounded creation publication protection.",
                startsAt, Now.AddHours(1), Now.AddMinutes(30)));
            return;
        }

        var controller = SubjectReference.Create(ModerationSubjectKind.Human, database.ReviewerId, database.Instance);
        var profile = publication.Profiles.Single();
        var rule = ModerationRuleReference.Create(publication.Jurisdiction.Id, profile.RuleCode, profile.RuleRevision);
        var reason = ModerationRuleReference.Create(publication.Jurisdiction.Id, profile.ReasonCode, profile.ReasonRevision);
        var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), Now);
        var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, 1, scope, null, null,
            rule, rule, reason, reason, controller,
            ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), allegation.Id, 1, Now),
            "Persisted creation publication finding.", Now);
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, issuer,
            scope, scope, rule, [ModerationCapability.Publish], startsAt, Now.AddHours(1), null, 1, Now);
        decision.RecordResponse(decision.Revision, Now);
        decision.AcceptFinding(finding.Id, decision.Revision, Now);
        decision.Approve(controller, scope, decision.Revision, Now);
        var sanction = decision.Activate(Guid.CreateVersion7(), decision.Revision, Now);
        context.AddRange(allegation, finding, decision, sanction);
    }
}
