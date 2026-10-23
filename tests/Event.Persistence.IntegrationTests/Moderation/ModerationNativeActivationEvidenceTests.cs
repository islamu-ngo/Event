using System.Data;
using System.Collections.Immutable;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Persistence;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>
/// Exercises the native-source port contract, not deployment conformance. The controlled owner below
/// is test evidence only; passing these cases cannot supply a production registration or prerequisite.
/// </summary>
public sealed class ModerationNativeActivationEvidenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(0, true)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(4, false)]
    [Arguments(5, false)]
    [Arguments(6, false)]
    [Arguments(7, false)]
    [Arguments(8, false)]
    [Arguments(9, false)]
    [Arguments(10, false)]
    [Arguments(11, false)]
    [Arguments(12, false)]
    [Arguments(13, false)]
    [Arguments(14, false)]
    [Arguments(15, false)]
    [Arguments(16, false)]
    [Arguments(17, false)]
    [Arguments(18, false)]
    [Arguments(19, false)]
    [Arguments(20, false)]
    [Arguments(21, false)]
    [Arguments(22, false)]
    [Arguments(23, false)]
    [Arguments(24, false)]
    [Arguments(25, false)]
    [Arguments(26, false)]
    [Arguments(27, false)]
    [Arguments(28, true)]
    [Arguments(29, false)]
    [Arguments(30, false)]
    [Arguments(31, false)]
    [Arguments(32, true)]
    [Arguments(33, false)]
    [Arguments(34, false)]
    [Arguments(35, false)]
    [Arguments(36, false)]
    [Arguments(37, false)]
    [Arguments(38, true)]
    [Arguments(39, false)]
    [Arguments(40, false)]
    [Arguments(41, false)]
    [Arguments(42, false)]
    [Arguments(43, false)]
    [Arguments(44, true)]
    [Arguments(45, false)]
    [Arguments(46, false)]
    [Arguments(47, false)]
    [Arguments(48, false)]
    [Arguments(49, true)]
    [Arguments(50, false)]
    [Arguments(51, false)]
    public async Task Qualification_requires_complete_independent_current_sources_in_the_owning_transaction(
        int changedSource, bool expected)
    {
        await using var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
        var tenant = new NativeTenant();
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection).Options;
        await using var context = new ExploreDbContext(options) { TenantContext = tenant };
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var scope = AuthorityScope.Tenant(instance, tenant.TenantId);
        context.ModerationTrustDomainId = scope.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var hostId = Guid.CreateVersion7();
        var policy = CompilePolicy(scope, inventory, hostId, recognition: changedSource == 31);
        var profile = policy.Profiles.Single();
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), Guid.CreateVersion7(),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance),
            instance, scope, profile.Definition.Rule, [ModerationCapability.Publish], Now, Now.AddHours(1), null,
            profile.Definition.RequiredApprovals, Now);
        var owner = new ControlledSourceOwner(inventory, hostId, transaction.TransactionId, policy, profile, decision);
        var first = inventory.RequiredConsumers(ModerationCapability.Publish, ModerationNativeMode.Embedded)[0];
        switch (changedSource)
        {
            case 1:
                owner.CurrentSources.Remove(first);
                break;
            case 2:
                owner.Registrations[first] = owner.Registrations[first] with { Conformance = ModerationNativeConformance.NotRun };
                break;
            case 3:
                owner.CurrentSources[first] = owner.CurrentSources[first] with { Revision = 2 };
                break;
            case 4:
                owner.Registrations[first] = owner.Registrations[first] with { VerifiedAt = Now.AddTicks(-1) };
                break;
            case 5:
                owner.Prerequisites[ModerationActivationPrerequisite.MeasuredOperations] =
                    owner.Prerequisites[ModerationActivationPrerequisite.MeasuredOperations] with { ValidUntil = Now };
                break;
            case 6:
                owner.Profile = owner.Profile with { TransactionId = Guid.CreateVersion7() };
                break;
            case 7:
                owner.Profile = owner.Profile with { PublicationId = Guid.CreateVersion7() };
                break;
            case 8:
                owner.CurrentSources[first] = owner.CurrentSources[first] with { WriterGeneration = "replacement-native-writer" };
                break;
            case 9:
                owner.Registrations[first] = owner.Registrations[first] with
                {
                    Contract = owner.Registrations[first].Contract with { TerminalCheckpoint = "entry-only-is-not-terminal" }
                };
                break;
            case 10:
                owner.Prerequisites.Remove(ModerationActivationPrerequisite.ConstitutionAndReviewers);
                break;
            case 11:
                owner.Profile = owner.Profile with
                {
                    Capabilities = [ModerationCapability.Publish, (ModerationCapability)int.MaxValue]
                };
                break;
            case 12:
                owner.Profile = owner.Profile with
                {
                    Capabilities = [ModerationCapability.Publish, ModerationCapability.Publish]
                };
                break;
            case 15: owner.Profile = owner.Profile with { ProfileId = Guid.CreateVersion7() }; break;
            case 16: owner.Profile = owner.Profile with { DecisionId = Guid.CreateVersion7() }; break;
            case 17: owner.Profile = owner.Profile with { DecisionRevision = decision.Revision + 1 }; break;
            case 18:
                owner.Profile = owner.Profile with
                { Contribution = owner.Profile.Contribution with { SubjectId = Guid.CreateVersion7() } }; break;
            case 19:
                owner.Profile = owner.Profile with
                {
                    Contribution = owner.Profile.Contribution with
                    {
                        Effects = owner.Profile.Contribution.Effects.Select(effect => effect with
                        { Target = effect.Target with { Scope = instance } }).ToImmutableArray()
                    }
                }; break;
            case 20:
                owner.Profile = owner.Profile with
                { Contribution = owner.Profile.Contribution with { Effects = [] } }; break;
            case 21:
                owner.Profile = owner.Profile with
                { Contribution = owner.Profile.Contribution with { StartsAt = Now.AddTicks(1) } }; break;
            case 22: owner.Profile = owner.Profile with { Mode = ModerationNativeMode.RemoteStrict }; break;
            case 24: owner.Profile = owner.Profile with { ValidUntil = Now }; break;
            case 25: owner.Profile = owner.Profile with { ValidUntil = DateTimeOffset.MaxValue }; break;
            case 26: owner.Profile = owner.Profile with { ValidUntil = Now.AddMinutes(5).ToOffset(TimeSpan.FromHours(1)) }; break;
            case 27:
            case 28:
                owner.Profile = owner.Profile with { ValidUntil = Now.AddMinutes(5) };
                break;
            case 29:
                owner.Profile = owner.Profile with
                {
                    RequiredPrerequisites = owner.Profile.RequiredPrerequisites.Add(ModerationActivationPrerequisite.ChildAndAgePolicy)
                };
                break;
            case 30:
                owner.Profile = owner.Profile with
                {
                    RequiredPrerequisites =
                        [ModerationActivationPrerequisite.ConstitutionAndReviewers, ModerationActivationPrerequisite.MeasuredOperations,
                            ModerationActivationPrerequisite.RecognitionAndPrivacy, ModerationActivationPrerequisite.EventRightsAndPremises,
                            ModerationActivationPrerequisite.ChildAndAgePolicy]
                };
                break;
            case 31:
                owner.Profile = owner.Profile with
                {
                    RequiredPrerequisites =
                        [ModerationActivationPrerequisite.ConstitutionAndReviewers, ModerationActivationPrerequisite.MeasuredOperations]
                };
                break;
            case 32:
                owner.Profile = owner.Profile with
                {
                    RequiredPrerequisites = owner.Profile.RequiredPrerequisites.Reverse().ToImmutableArray()
                };
                break;
            case 40: owner.CurrentSources[first] = owner.CurrentSources[first] with { ValidUntil = Now }; break;
            case 45: owner.Registrations[first] = owner.Registrations[first] with { ValidUntil = Now }; break;
            case 46: owner.Registrations[first] = owner.Registrations[first] with { ValidUntil = DateTimeOffset.MaxValue }; break;
            case 47:
                owner.Registrations[first] = owner.Registrations[first] with
                { ValidUntil = Now.AddMinutes(5).ToOffset(TimeSpan.FromHours(1)) }; break;
            case 48:
            case 49:
                owner.Registrations[first] = owner.Registrations[first] with { ValidUntil = Now.AddMinutes(5) };
                break;
            case 41: owner.CurrentSources[first] = owner.CurrentSources[first] with { ValidUntil = DateTimeOffset.MaxValue }; break;
            case 42:
                owner.CurrentSources[first] = owner.CurrentSources[first] with
                { ValidUntil = Now.AddMinutes(5).ToOffset(TimeSpan.FromHours(1)) }; break;
            case 43:
            case 44:
                owner.CurrentSources[first] = owner.CurrentSources[first] with { ValidUntil = Now.AddMinutes(5) };
                break;
        }
        if (changedSource is 29 or 30)
        {
            var existing = owner.Prerequisites[ModerationActivationPrerequisite.MeasuredOperations];
            foreach (var prerequisite in owner.Profile.RequiredPrerequisites)
                owner.Prerequisites.TryAdd(prerequisite, existing with
                {
                    Prerequisite = prerequisite,
                    ApprovalId = Guid.CreateVersion7(),
                    SourceId = Guid.CreateVersion7()
                });
        }
        var clock = new FixedClock(changedSource == 13 ? Now.AddTicks(-1) : Now);
        if (changedSource is 23 or 27 or 39 or 43 or 48)
            owner.PrerequisiteObserved = prerequisite =>
            {
                if (prerequisite == ModerationActivationPrerequisite.MeasuredOperations)
                    clock.Current = changedSource == 23 ? Now.AddHours(1) : Now.AddMinutes(5);
            };
        var configuration = owner.Profile;
        configuration = changedSource switch
        {
            33 => configuration with { SourceId = Guid.CreateVersion7() },
            34 => configuration with { WriterGeneration = "withdrawn-profile-configuration" },
            35 => configuration with { PublicationId = Guid.CreateVersion7() },
            36 => configuration with
            { Contribution = configuration.Contribution with { SubjectId = Guid.CreateVersion7() } },
            37 => configuration with { ValidUntil = Now },
            38 or 39 => configuration with { ValidUntil = Now.AddMinutes(5) },
            _ => configuration
        };
        var qualification = new ModerationNativeActivationQualification(context, inventory,
            changedSource == 14 ? null : owner, owner, clock);
        using var consumerEvidence = new ComparisonEvidenceSession(transaction.TransactionId);
        if (changedSource == 50)
            owner.ProfileObserved = consumerEvidence.Dispose;
        if (changedSource == 51)
            owner.PrerequisiteObserved = prerequisite =>
            {
                if (consumerEvidence.TransactionId == Guid.Empty)
                    throw new InvalidOperationException("A lost original borrow must stop before another prerequisite read.");
                if (prerequisite == ModerationActivationPrerequisite.ConstitutionAndReviewers)
                    consumerEvidence.Dispose();
            };
        var validUntil = await qualification.QualifyAsync(consumerEvidence, hostId, policy, profile, decision, configuration, Now, default);
        await Assert.That(validUntil is not null).IsEqualTo(expected);
        if (expected) await Assert.That(validUntil).IsEqualTo(
            changedSource is 28 or 38 or 44 or 49 ? Now.AddMinutes(5) : Now.AddHours(1));
    }

    [Test]
    [Arguments(0, true)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(3, false)]
    [Arguments(4, false)]
    [Arguments(5, false)]
    [Arguments(6, false)]
    [Arguments(7, false)]
    [Arguments(8, false)]
    [Arguments(9, false)]
    [Arguments(10, false)]
    [Arguments(11, false)]
    [Arguments(12, false)]
    [Arguments(13, false)]
    [Arguments(14, false)]
    [Arguments(15, false)]
    public async Task Procedural_lifetimes_require_separate_current_case_scope_policy_approval(int change, bool expected)
    {
        await using var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
        var tenant = new NativeTenant();
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection).Options;
        await using var context = new ExploreDbContext(options) { TenantContext = tenant };
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var scope = AuthorityScope.Tenant(instance, tenant.TenantId);
        context.ModerationTrustDomainId = scope.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var hostId = Guid.CreateVersion7();
        var policy = CompilePolicy(scope, inventory, hostId);
        var profile = policy.Profiles.Single();
        var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance);
        var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), Now);
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject,
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance),
            instance, scope, profile.Definition.Rule, [ModerationCapability.Publish], Now, Now.AddHours(1), null, 1, Now);
        var owner = new ControlledSourceOwner(inventory, hostId, transaction.TransactionId, policy, profile, decision);
        var current = owner.CurrentPolicy!;
        var approval = owner.PolicyApproval!;
        switch (change)
        {
            case 1: owner.CurrentPolicy = null; break;
            case 2: owner.PolicyApproval = null; break;
            case 3: owner.CurrentPolicy = current with { TransactionId = Guid.NewGuid() }; break;
            case 4: owner.PolicyApproval = approval with { CaseId = Guid.CreateVersion7() }; break;
            case 5: owner.PolicyApproval = approval with { Scope = instance }; break;
            case 6: owner.CurrentPolicy = current with { SourceRevision = 2 }; break;
            case 7: owner.CurrentPolicy = current with { WriterGeneration = "replacement-policy-writer" }; break;
            case 8: owner.CurrentPolicy = current with { MaximumReviewerLifetime = TimeSpan.FromMinutes(30) }; break;
            case 9: owner.PolicyApproval = approval with { MaximumUrgencyLifetime = TimeSpan.FromMinutes(30) }; break;
            case 10: owner.CurrentPolicy = current with { ValidUntil = Now }; break;
            case 11: owner.PolicyApproval = approval with { ValidUntil = Now }; break;
            case 12: owner.PolicyApproval = approval with { ApprovalId = current.SourceId }; break;
            case 13: owner.CurrentPolicy = current with { HostBindingId = Guid.CreateVersion7() }; break;
            case 14: owner.PolicyApproval = approval with { AdoptedAt = Now.AddTicks(1) }; break;
            case 15:
                owner.CurrentPolicy = current with { MaximumReviewerLifetime = TimeSpan.Zero };
                owner.PolicyApproval = approval with { MaximumReviewerLifetime = TimeSpan.Zero };
                break;
        }
        var qualification = new ModerationNativeActivationQualification(context, inventory, null, owner, new FixedClock(Now));
        var observed = await qualification.AcquireQualificationPolicyAsync(hostId, allegation, Now, default);
        await Assert.That(observed is not null).IsEqualTo(expected);
        using var consumerEvidence = new ComparisonEvidenceSession(transaction.TransactionId);
        await Assert.That(await qualification.QualifyAsync(consumerEvidence, hostId, policy, profile, decision, owner.Profile, Now, default)).IsNull();
        if (expected)
        {
            await Assert.That(observed!.Current.MaximumReviewerLifetime).IsEqualTo(TimeSpan.FromHours(1));
            await Assert.That(observed.Current.MaximumUrgencyLifetime).IsEqualTo(TimeSpan.FromMinutes(15));
            await Assert.That(observed.Current.SourceId).IsNotEqualTo(observed.Approval.ApprovalId);
            var missingProcedure = new ModerationNativeActivationQualification(
                context, inventory, owner, null, new FixedClock(Now));
            await Assert.That(await missingProcedure.AcquireQualificationPolicyAsync(hostId, allegation, Now, default)).IsNull();
            await Assert.That(await missingProcedure.QualifyAsync(consumerEvidence, hostId, policy, profile, decision, owner.Profile, Now, default)).IsNull();
        }
    }

    private static ModerationPolicyCompiledDraft CompilePolicy(
        AuthorityScope scope, ModerationCapabilityRegistry inventory, Guid hostId,
        bool recognition = false)
    {
        var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG06);
        var response = reference.Responses[0];
        ModerationRuleReference Rule(string code) => ModerationRuleReference.Create(scope.Id, code, 1);
        var definition = new ModerationPolicyProfileDefinition(Guid.CreateVersion7(), Rule("native-source-contract"),
            Rule("native-source-reason"), reference.Id, reference.Revision, response.Grade,
            [ModerationScopeKind.Tenant], [ModerationSubjectKind.Human], reference.Effects.ToImmutableArray(),
            reference.Bundle, reference.ProtectionPurpose, [], new(response.Timing.Kind, TimeSpan.FromHours(1)),
            response.RequiredApprovals, 6,
            recognition ? ModerationReferenceRecognition.SeparatelyQualifiedFinalAction : ModerationReferenceRecognition.Never);
        var proposed = new ModerationPolicyProposedState(Guid.CreateVersion7(), 0, Guid.CreateVersion7(), scope,
            ModerationReferenceCatalogue.Revision, inventory.Revision, inventory.Fingerprint,
            ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
            [new(Rule("native-source-class"), "Native evidence binding witness.", reference.Ground, reference.Basis)],
            [new(definition.Reason, "Native source qualification.", Rule("native-source-class"))], [definition], [definition.Rule]);
        return ModerationPolicyProposedStateCompiler.Compile(proposed,
            new(hostId, Guid.CreateVersion7(), 1, ModerationPolicyPublicationProjection.WriterGeneration,
                ModerationPolicyPublicationProjection.AuthoringBounds(scope), null), inventory).Draft
            ?? throw new InvalidOperationException("The rich native-source contract fixture did not compile.");
    }

    private sealed class ControlledSourceOwner : IModerationActivationSourceOwner, IModerationQualificationPolicySourceOwner
    {
        public ModerationActivationProfileObservation Profile { get; set; }
        public Dictionary<string, ModerationCapabilityRegistration> Registrations { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ModerationCapabilitySourceObservation> CurrentSources { get; } = new(StringComparer.Ordinal);
        public Dictionary<ModerationActivationPrerequisite, ModerationPrerequisiteObservation> Prerequisites { get; } = [];
        public ModerationQualificationPolicyCurrentSource? CurrentPolicy { get; set; }
        public ModerationQualificationPolicyApproval? PolicyApproval { get; set; }
        public Action<ModerationActivationPrerequisite>? PrerequisiteObserved { get; set; }

        public ControlledSourceOwner(
            ModerationCapabilityRegistry inventory, Guid hostId, Guid transactionId,
            ModerationPolicyCompiledDraft policy, ModerationPolicyCompiledProfile profile, ModerationDecision decision)
        {
            var publication = policy.Publication;
            var definition = profile.Definition;
            var target = new ModerationPolicyTarget(decision.Scope, decision.Subject.Kind,
                decision.Subject.Id, Guid.CreateVersion7(), 1, "event.publish", null);
            var contribution = new ModerationPolicyContribution(decision.Id, definition.Rule,
                decision.Scope, decision.Subject.Kind, decision.Subject.Id, decision.StartsAt, null,
                definition.Effects.Select(effect => new ModerationPolicyEffectTarget(effect, target)).ToImmutableArray(),
                [new(null, target)]);
            Profile = new(transactionId, hostId, publication.PolicyId, publication.ProposedRevision,
                publication.PublicationId, publication.Jurisdiction, definition.Id, decision.Id, decision.Revision,
                definition.Rule, ModerationNativeMode.Embedded, Guid.CreateVersion7(), 1, "native-profile-generation", Now, Now.AddHours(1),
                policy.Prerequisites.Single(row => row.Rule == definition.Rule && row.Mode == ModerationNativeMode.Embedded).ActivationGates,
                [ModerationCapability.Publish], contribution);
            var binding = new ModerationActivationBinding(publication.PolicyId, publication.ProposedRevision,
                publication.PublicationId, publication.Jurisdiction, definition.Rule,
                ModerationCapability.Publish, ModerationNativeMode.Embedded);
            var policySourceId = Guid.CreateVersion7();
            CurrentPolicy = new(transactionId, hostId, decision.CaseId, decision.Scope,
                policySourceId, 1, "native-procedural-policy", TimeSpan.FromHours(1), TimeSpan.FromMinutes(15),
                Now, Now.AddHours(1));
            PolicyApproval = new(transactionId, hostId, decision.CaseId, decision.Scope, Guid.CreateVersion7(), 1,
                policySourceId, 1, "native-procedural-policy", TimeSpan.FromHours(1), TimeSpan.FromMinutes(15),
                Now, Now.AddHours(1), Now);
            foreach (var id in inventory.RequiredConsumers(binding.Capability, binding.Mode))
            {
                var sourceId = Guid.CreateVersion7();
                CurrentSources.Add(id, new(binding, id, sourceId, 1, "native-writer-generation", Now, Now.AddHours(1)));
                Registrations.Add(id, new(binding, inventory.Fingerprint, inventory.Consumers[id].Contract,
                    sourceId, 1, "native-writer-generation", ModerationNativeConformance.Passed, Now, Now.AddHours(1)));
            }
            foreach (var prerequisite in Profile.RequiredPrerequisites)
                Prerequisites.Add(prerequisite, new(transactionId, binding, prerequisite,
                    Guid.CreateVersion7(), 1, Guid.CreateVersion7(), 1, "native-prerequisite-generation",
                    1, "native-prerequisite-generation", Now, Now.AddHours(1), Now));
        }

        public Task<ModerationActivationProfileObservation?> AcquireProfileAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid hostBindingId, Guid transactionId, ModerationPolicyCompiledDraft policy,
            ModerationPolicyCompiledProfile profile, ModerationDecision decision,
            DateTimeOffset observedAt, CancellationToken cancellationToken)
        {
            ProfileObserved?.Invoke();
            return Task.FromResult<ModerationActivationProfileObservation?>(Profile);
        }
        public Action? ProfileObserved { get; set; }
        public Task<ModerationQualificationPolicyCurrentSource?> AcquireQualificationPolicyAsync(
            Guid hostBindingId, Guid transactionId, Guid caseId, AuthorityScope scope,
            DateTimeOffset observedAt, CancellationToken cancellationToken) => Task.FromResult(CurrentPolicy);
        public Task<ModerationQualificationPolicyApproval?> ReadQualificationPolicyApprovalAsync(
            Guid hostBindingId, Guid transactionId, Guid caseId, AuthorityScope scope,
            DateTimeOffset observedAt, CancellationToken cancellationToken) => Task.FromResult(PolicyApproval);
        public Task<ModerationCapabilityRegistration?> ReadConformanceAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid transactionId, ModerationActivationBinding binding, string consumerId,
            DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            Task.FromResult(Registrations.GetValueOrDefault(consumerId));
        public Task<ModerationCapabilitySourceObservation?> AcquireConsumerAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid transactionId, ModerationActivationBinding binding, string consumerId,
            DateTimeOffset observedAt, CancellationToken cancellationToken)
        {
            if (evidenceSession.TransactionId != transactionId)
                throw new InvalidOperationException("A lost original borrow must stop before consumer acquisition.");
            return Task.FromResult(CurrentSources.GetValueOrDefault(consumerId));
        }
        public Task<ModerationPrerequisiteObservation?> AcquirePrerequisiteAsync(
            IModerationConsumerEvidenceSession evidenceSession,
            Guid transactionId, ModerationActivationBinding binding, ModerationActivationPrerequisite prerequisite,
            DateTimeOffset observedAt, CancellationToken cancellationToken)
        {
            PrerequisiteObserved?.Invoke(prerequisite);
            return Task.FromResult(Prerequisites.GetValueOrDefault(prerequisite));
        }
    }

    private sealed class ComparisonEvidenceSession(Guid transactionId) : IModerationConsumerEvidenceSession
    {
        private bool disposed;
        public Guid TransactionId => !disposed ? transactionId : Guid.Empty;
        public void Dispose() => disposed = true;
        public Task<ModerationMeasurementDatabaseObservation?> AcquirePrimaryDatabaseAsync(
            DateTimeOffset observedAt, CancellationToken token) =>
            throw new InvalidOperationException("Controlled comparison owners cannot claim actual primary database facts.");
        public Task<ModerationCapabilitySourceObservation?> AcquireInstallationAsync(
            ModerationActivationBinding binding, string consumerId, DateTimeOffset observedAt, CancellationToken token) =>
            throw new InvalidOperationException("Controlled comparison owners cannot claim a native installation acquisition.");
        public Task<ModerationMeasurementReceiptObservation?> AcquireMeasurementAsync(
            ModerationActivationBinding binding, string consumerId, DateTimeOffset observedAt, CancellationToken token) =>
            throw new InvalidOperationException("Controlled comparison owners cannot certify execution or native receipt acquisition.");
    }

    private sealed class NativeTenant : ITenantContext
    {
        public Guid TenantId { get; } = Guid.CreateVersion7();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Current;
    }
}
