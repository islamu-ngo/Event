using System.Collections.Immutable;
using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>Native persisted publication restrictions; no runtime-conformance assertion or mock reader.</summary>
public sealed class EventPublicationModerationSnapshotReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Installation_without_applicable_adoption_preserves_existing_permissions()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();

        var facts = await ReadAsync(database);

        await Assert.That(facts.IsAdopted).IsFalse();
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(facts.PermitsAt(Now)).IsTrue();
    }

    [Test]
    public async Task Draft_HumanPublish_adoption_is_not_itself_a_denial_or_permission_grant()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        await PublishAsync(database);

        var facts = await ReadAsync(database);

        await Assert.That(facts.IsAdopted).IsTrue();
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(facts.PermitsAt(Now)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Service_only_or_Manage_only_declarations_do_not_adopt_Human_publication(bool serviceOnly)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var request = database.Publication(1,
            serviceOnly ? ModerationReferenceProfileId.CFG06 : ModerationReferenceProfileId.CFG11);
        var profile = request.Proposed.Profiles.Single();
        request = request with
        {
            Proposed = request.Proposed with
            {
                Profiles = [profile with
                {
                    SubjectKinds = serviceOnly ? [ModerationSubjectKind.Service] : [ModerationSubjectKind.Human]
                }],
                SupportedProfiles = []
            }
        };
        var published = await database.PublishAsync(database.AdministratorId, request);
        await Assert.That(published.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);

        var facts = await ReadAsync(database);

        await Assert.That(facts.IsAdopted).IsFalse();
        await Assert.That(facts.PermitsAt(Now)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Instance_declaration_adopts_the_actual_Tenant_only_when_its_profile_selects_Tenant(bool tenantProfile)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var request = database.Publication(1);
        ModerationRuleReference Rebase(ModerationRuleReference rule) =>
            ModerationRuleReference.Create(database.Instance.Id, rule.Code, rule.Revision);
        request = request with
        {
            Proposed = request.Proposed with
            {
                Jurisdiction = database.Instance,
                Classes = request.Proposed.Classes.Select(row => row with { Reference = Rebase(row.Reference) }).ToImmutableArray(),
                Reasons = request.Proposed.Reasons.Select(row => row with
                {
                    Reference = Rebase(row.Reference),
                    Class = Rebase(row.Class)
                }).ToImmutableArray(),
                Profiles = request.Proposed.Profiles.Select(row => row with
                {
                    Rule = Rebase(row.Rule),
                    Reason = Rebase(row.Reason),
                    ScopeKinds = tenantProfile ? [ModerationScopeKind.Tenant] : [ModerationScopeKind.Instance]
                }).ToImmutableArray(),
                SupportedProfiles = []
            }
        };
        var published = await database.PublishAsync(database.AdministratorId, request);
        await Assert.That(published.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);

        var facts = await ReadAsync(database);

        await Assert.That(facts.IsAdopted).IsEqualTo(tenantProfile);
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Actual_Human_Tenant_publication_is_denied_by_tenant_or_instance_contributions(
        bool protective, bool instanceScope)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var publication = await PublishAsync(database);
        await StoreContributionAsync(database, publication, protective,
            scope: instanceScope ? database.Instance : database.Scope);

        var facts = await ReadAsync(database);

        await Assert.That(facts.IsAdopted).IsTrue();
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(facts.PermitsAt(Now)).IsFalse();
    }

    [Test]
    [Arguments("caller", false)]
    [Arguments("scope", false)]
    [Arguments("capability", false)]
    [Arguments("origin", false)]
    [Arguments("caller", true)]
    [Arguments("scope", true)]
    [Arguments("capability", true)]
    [Arguments("origin", true)]
    public async Task Unrelated_canonical_contribution_does_not_deny_or_adopt_the_actual_caller(
        string coordinate, bool keepDeclaration)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var publication = await PublishAsync(database);
        var subject = SubjectReference.Create(ModerationSubjectKind.Human,
            coordinate == "caller" ? database.AdministratorId : database.SubjectId,
            coordinate == "origin" ? database.Scope : database.Instance);
        var scope = coordinate == "scope"
            ? AuthorityScope.Tenant(database.Instance, Guid.CreateVersion7()) : database.Scope;
        await StoreContributionAsync(database, publication, subject: subject, scope: scope,
            capability: coordinate == "capability" ? ModerationCapability.Upload : ModerationCapability.Publish);
        if (!keepDeclaration) await RemoveSelectionAsync(database, publication.PolicyId);

        var facts = await ReadAsync(database);

        await Assert.That(facts.IsAdopted).IsEqualTo(keepDeclaration);
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Retiring_support_cannot_erase_a_retained_effect(bool removeSelection, bool protective)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var publication = await PublishAsync(database);
        await StoreContributionAsync(database, publication, protective);
        var before = await ReadAsync(database);
        var declared = ModerationPolicyPublicationProjection.RestoreHistory(publication)!.Proposed;
        var request = database.Publication(2) with
        {
            ExpectedPublicationId = publication.Id,
            Proposed = declared with
            {
                PublicationId = Guid.CreateVersion7(),
                ExpectedRevision = publication.Revision,
                SupportedProfiles = []
            }
        };
        var retired = await database.PublishAsync(database.AdministratorId, request);
        await Assert.That(retired.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        if (removeSelection)
            await RemoveSelectionAsync(database, publication.PolicyId);

        var after = await ReadAsync(database);

        await Assert.That(before.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(after.IsAdopted).IsTrue();
        await Assert.That(after.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(after.Generation).IsNotEqualTo(before.Generation);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Scheduled_retained_effect_preserves_adoption_and_expires_the_positive_snapshot_at_its_start(
        bool protective)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var publication = await PublishAsync(database);
        var startsAt = Now.AddMinutes(1);
        await StoreContributionAsync(database, publication, protective, startsAt: startsAt);
        await RemoveSelectionAsync(database, publication.PolicyId);

        var before = await ReadAsync(database);
        var repeated = await ReadAsync(database, Now.AddSeconds(1));
        var effective = await ReadAsync(database, startsAt);
        var expired = await ReadAsync(database, Now.AddHours(1));

        await Assert.That(before.IsAdopted).IsTrue();
        await Assert.That(before.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(before.CurrentUntil).IsEqualTo((DateTimeOffset?)startsAt);
        await Assert.That(before.Generation).IsEqualTo(repeated.Generation);
        await Assert.That(before.PermitsAt(startsAt.AddTicks(-1))).IsTrue();
        await Assert.That(before.PermitsAt(startsAt)).IsFalse();
        await Assert.That(effective.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(effective.CurrentUntil).IsEqualTo((DateTimeOffset?)Now.AddHours(1));
        await Assert.That(expired.IsAdopted).IsFalse();
        await Assert.That(expired.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
    }

    [Test]
    [Arguments("scope")]
    [Arguments("effect")]
    [Arguments("base-plan")]
    public async Task Malformed_selected_history_is_unknown_not_nonadoption(string missing)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var publication = await PublishAsync(database);
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Instance.TrustDomainId;
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            switch (missing)
            {
                case "scope":
                    await writer.Set<ModerationPolicyPublicationScope>()
                        .Where(row => row.PublicationId == publication.Id).ExecuteDeleteAsync();
                    break;
                case "effect":
                    await writer.Set<ModerationPolicyPublicationEffect>()
                        .Where(row => row.PublicationId == publication.Id).ExecuteDeleteAsync();
                    break;
                case "base-plan":
                    await writer.Set<ModerationPolicyPublicationPlan>()
                        .Where(row => row.PublicationId == publication.Id).ExecuteDeleteAsync();
                    break;
            }
            await transaction.CommitAsync();
        }

        await AssertUnavailableAsync(await ReadAsync(database));
    }

    [Test]
    public async Task No_transaction_cannot_claim_positive_nonadoption()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        await using var context = database.Context();

        await AssertUnavailableAsync(await Reader(context).ReadAsync(database.SubjectId, Now, CancellationToken.None));
        await Assert.That(context.ModerationTrustDomainId).IsNull();
    }

    [Test]
    public async Task Missing_qualification_epoch_cannot_claim_positive_nonadoption()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        await using (var writer = database.Context())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await writer.ModerationQualificationEpochs.Where(row => row.Id == database.EpochId).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }

        await AssertUnavailableAsync(await ReadAsync(database));
    }

    [Test]
    public async Task A_missing_native_Human_cannot_claim_positive_nonadoption()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        await AssertUnavailableAsync(await Reader(context).ReadAsync(Guid.CreateVersion7(), Now, CancellationToken.None));
    }

    [Test]
    public async Task Positive_facts_reject_non_UTC_evaluation_instants()
    {
        await Assert.That(EventPublicationModerationFacts.NotAdopted.PermitsAt(Now.ToOffset(TimeSpan.FromHours(1))))
            .IsFalse();
    }

    private static EventPublicationModerationSnapshotReader Reader(ExploreDbContext context) =>
        new(context, ModerationRecognitionResourceFixture.Reader(context));

    private static async Task RemoveSelectionAsync(
        ModerationQualificationWriterTests.SourceDatabase database, Guid policyId)
    {
        await using var writer = database.Context();
        writer.ModerationTrustDomainId = database.Instance.TrustDomainId;
        await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await writer.ModerationPolicySelections.Where(row => row.Id == policyId).ExecuteDeleteAsync();
        await transaction.CommitAsync();
    }

    private static async Task<EventPublicationModerationFacts> ReadAsync(
        ModerationQualificationWriterTests.SourceDatabase database, DateTimeOffset? at = null)
    {
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var facts = await Reader(context).ReadAsync(database.SubjectId, at ?? Now, CancellationToken.None);
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await transaction.CommitAsync();
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

    private static async Task StoreContributionAsync(
        ModerationQualificationWriterTests.SourceDatabase database, ModerationPolicyPublication publication,
        bool protective = false, SubjectReference? subject = null, AuthorityScope? scope = null,
        ModerationCapability capability = ModerationCapability.Publish, DateTimeOffset? startsAt = null)
    {
        subject ??= SubjectReference.Create(ModerationSubjectKind.Human, database.SubjectId, database.Instance);
        scope ??= database.Scope;
        var issuer = SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.Instance);
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (protective)
        {
            context.ModerationProtectiveMeasures.Add(ModerationProtectiveMeasure.Create(Guid.CreateVersion7(),
                subject, issuer, scope, scope, [capability], "Bounded publication protection.",
                startsAt ?? Now, Now.AddHours(1), Now.AddMinutes(30)));
        }
        else
        {
            var controller = SubjectReference.Create(ModerationSubjectKind.Human, database.ReviewerId, database.Instance);
            var profile = publication.Profiles.Single();
            var rule = ModerationRuleReference.Create(publication.Jurisdiction.Id, profile.RuleCode, profile.RuleRevision);
            var reason = ModerationRuleReference.Create(publication.Jurisdiction.Id, profile.ReasonCode, profile.ReasonRevision);
            var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), Now);
            // Historical canonical state, not an assertion of current activation support.
            var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, 1, scope, null, null,
                rule, rule, reason, reason, controller,
                ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), allegation.Id, 1, Now),
                "Persisted publication finding.", Now);
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, issuer,
                scope, scope, rule, [capability], startsAt ?? Now, Now.AddHours(1), null, 1, Now);
            decision.RecordResponse(decision.Revision, Now);
            decision.AcceptFinding(finding.Id, decision.Revision, Now);
            decision.Approve(controller, scope, decision.Revision, Now);
            var sanction = decision.Activate(Guid.CreateVersion7(), decision.Revision, Now);
            decision.ValidatePersistedState();
            sanction.ValidatePersistedState();
            context.AddRange(allegation, finding, decision, sanction);
        }
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static async Task AssertUnavailableAsync(EventPublicationModerationFacts facts)
    {
        await Assert.That(facts.IsAdopted).IsTrue();
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unavailable);
        await Assert.That(facts.PermitsAt(Now)).IsFalse();
    }
}
