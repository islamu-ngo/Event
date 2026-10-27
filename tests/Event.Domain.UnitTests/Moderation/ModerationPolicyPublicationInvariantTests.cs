using System.Reflection;
using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public class ModerationPolicyPublicationInvariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PolicyId = Id(1);
    private static readonly Guid PublicationId = Id(2);

    [Test]
    public async Task AdoptionWithoutNativeEvidenceNeverEnablesAnEffect()
    {
        var scope = Scope();
        var terms = Terms(scope);
        var policy = ModerationPolicy.Adopt(PolicyId, scope, [terms], PublicationId, Now);

        await Assert.That(terms.Permits(ModerationPolicyOperationPurpose.Activate,
            [ModerationCapability.Publish], Now, Now.AddDays(1), null, 1)).IsTrue();
        await Assert.That(Permits(policy, terms.Rule)).IsFalse();
        await Assert.That(policy.Rules[0].ActiveCapabilities.Count).IsEqualTo(0);
        await Assert.That(policy.IsActive).IsFalse();
        await Reject<ArgumentException>(() => policy.Activate(scope, 1, [], Id(3), Now));
        var active = policy.Activate(scope, 1,
            [Evidence(scope, terms.Rule, revision: 2, publicationId: Id(3))], Id(3), Now);
        await Assert.That(active.IsActive).IsTrue();
        await Assert.That(active.Revision).IsEqualTo(2);
        await Assert.That(Permits(active, terms.Rule)).IsTrue();
        await Assert.That(policy.IsActive).IsFalse();
        await Reject<InvalidOperationException>(() =>
            active.Activate(scope, 2, [Evidence(scope, terms.Rule, revision: 3, publicationId: Id(4))], Id(4), Now));
        await Reject<ArgumentException>(() =>
            ModerationPolicy.Publish(PolicyId, scope, [terms], [], PublicationId, Now));
    }

    [Test]
    public async Task CompleteNativeEvidenceEnablesOnlyItsExactAdoptedEffect()
    {
        var policy = Active();

        await Assert.That(Permits(policy, policy.Rules[0].Rule)).IsTrue();
        await Assert.That(Permits(policy, policy.Rules[0].Rule, ModerationCapability.Upload)).IsFalse();
        await Assert.That(Permits(policy, ModerationRuleReference.Create(Scope().Id, "other", 1))).IsFalse();
        await Assert.That(Permits(policy, policy.Rules[0].Rule,
            purpose: ModerationPolicyOperationPurpose.Protect)).IsFalse();
    }

    [Test]
    public async Task EvidenceRejectsAbsentPartialExcessAndDuplicateNativeFacts()
    {
        foreach (var consumers in new[]
        {
            Array.Empty<string>(), new[] { "entry" }, new[] { "entry", "terminal", "unowned" },
            new[] { "entry", "terminal", "terminal" }
        })
        {
            await Reject<ArgumentException>(() => Evidence(Scope(), Terms(Scope()).Rule, consumers: consumers));
        }

        await Reject<ArgumentException>(() => Evidence(Scope(), Terms(Scope()).Rule, prerequisites: []));
        await Reject<ArgumentException>(() => Evidence(Scope(), Terms(Scope()).Rule, prerequisites: ["other"]));
    }

    [Test]
    public async Task NativeEvidenceRequiresPositiveSourceRevisionsAndExactCurrentScopeAndInstant()
    {
        var scope = Scope();
        await Reject<ArgumentException>(() => _ = new ModerationPolicyNativeEvidence("entry", Id(100), 0, scope, Now));
        await Reject<ArgumentException>(() => _ = new ModerationPolicyNativeEvidence("entry", Guid.Empty, 1, scope, Now));
        await Reject<ArgumentException>(() => _ = new ModerationPolicyNativeEvidence("", Id(100), 1, scope, Now));
        foreach (var fact in new[]
        {
            new ModerationPolicyNativeEvidence("entry", Id(100), 1, Scope(99), Now),
            new ModerationPolicyNativeEvidence("entry", Id(100), 1, scope, Now.AddTicks(-1)),
            new ModerationPolicyNativeEvidence("entry", Id(100), 1, scope, Now.AddTicks(1))
        })
        {
            await Reject<ArgumentException>(() => _ = new ModerationPolicyEffectEvidence(
                PolicyId, 1, scope, Terms(scope).Rule, ModerationCapability.Publish, PublicationId, Now,
                ["adoption"], [new ModerationPolicyNativeEvidence("adoption", Id(200), 1, scope, Now)],
                ["entry", "terminal"], [fact, new ModerationPolicyNativeEvidence("terminal", Id(101), 1, scope, Now)]));
            await Reject<ArgumentException>(() => _ = new ModerationPolicyEffectEvidence(
                PolicyId, 1, scope, Terms(scope).Rule, ModerationCapability.Publish, PublicationId, Now,
                ["entry"], [fact], ["terminal"],
                [new ModerationPolicyNativeEvidence("terminal", Id(101), 1, scope, Now)]));
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task MismatchedEvidenceCannotPublishAnyPartialState(int mismatch)
    {
        var scope = Scope();
        var terms = Terms(scope);
        var proofScope = mismatch == 2 ? Scope(99) : scope;
        var proofRule = mismatch == 2 ? Terms(proofScope).Rule
            : mismatch == 3 ? ModerationRuleReference.Create(scope.Id, "danger", 2) : terms.Rule;
        var proof = Evidence(proofScope, proofRule,
            policyId: mismatch == 0 ? Id(98) : PolicyId,
            revision: mismatch == 1 ? 2 : 1,
            publicationId: mismatch == 4 ? Id(97) : PublicationId,
            verifiedAt: mismatch == 5 ? Now.AddTicks(-1) : Now);

        await Reject<ArgumentException>(() =>
            ModerationPolicy.Publish(PolicyId, scope, [terms], [proof], PublicationId, Now));
    }

    [Test]
    public async Task UnknownCapabilitiesAndRepeatedEvidenceCannotBecomeActive()
    {
        var scope = Scope();
        var terms = Terms(scope);
        var proof = Evidence(scope, terms.Rule);
        await Reject<ArgumentException>(() =>
            ModerationPolicy.Publish(PolicyId, scope, [terms], [proof, proof], PublicationId, Now));
        var unsupported = Evidence(scope, terms.Rule, capability: ModerationCapability.Admission);
        await Reject<ArgumentException>(() =>
            ModerationPolicy.Publish(PolicyId, scope, [terms], [unsupported], PublicationId, Now));
        await Reject<ArgumentException>(() => Evidence(scope, terms.Rule, capability: ModerationCapability.PrivacyErasure));
        await Reject<ArgumentException>(() => Evidence(scope, terms.Rule, capability: (ModerationCapability)99));
    }

    [Test]
    public async Task RuleOwnershipUsesExactJurisdictionNotParentOrSameLeafIdentifier()
    {
        var scope = Scope();
        var terms = Terms(scope.Parent!);
        await Reject<ArgumentException>(() =>
            ModerationPolicy.Adopt(PolicyId, scope, [terms], PublicationId, Now));
        var policy = Active();
        var foreignAncestry = AuthorityScope.Tenant(
            AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Id(80)), Id(81)), Id(82)), scope.Id);

        await Assert.That(policy.Permits(foreignAncestry, policy.Rules[0].Rule,
            ModerationPolicyOperationPurpose.Activate, [ModerationCapability.Publish],
            Now, Now.AddDays(1), null, 1)).IsFalse();
        await Reject<InvalidOperationException>(() =>
            policy.ReplaceAdoption(foreignAncestry, 1, policy.Rules, Id(3), Now));
    }

    [Test]
    public async Task CompleteRuleSetRejectsDuplicateIdentityVersionAndEmptyDefinitions()
    {
        var scope = Scope();
        var terms = Terms(scope);
        await Reject<ArgumentException>(() => ModerationPolicy.Adopt(PolicyId, scope, [], PublicationId, Now));
        await Reject<ArgumentException>(() => ModerationPolicy.Adopt(PolicyId, scope, [terms, terms], PublicationId, Now));
        await Reject<ArgumentException>(() => ModerationPolicy.Adopt(PolicyId, scope,
            [terms, Terms(scope, id: Id(21))], PublicationId, Now));
        await Reject<ArgumentException>(() => ModerationPolicy.Adopt(PolicyId, scope,
            [terms, Terms(scope, version: 2)], PublicationId, Now));
    }

    [Test]
    public async Task ReplacementAdvancesRevisionAndRetiresSupportWithoutMutatingEarlierSnapshot()
    {
        var source = Active();
        var earlierRules = source.Rules;
        var replacement = source.ReplaceAdoption(source.Jurisdiction, 1, source.Rules, Id(3), Now.AddHours(1));

        await Assert.That(replacement.Revision).IsEqualTo(2);
        await Assert.That(replacement.CreatedAt).IsEqualTo(Now);
        await Assert.That(replacement.UpdatedAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(replacement.Rules[0].PolicyRevision).IsEqualTo(2);
        await Assert.That(Permits(replacement, source.Rules[0].Rule)).IsFalse();
        await Assert.That(Permits(source, source.Rules[0].Rule)).IsTrue();
        await Assert.That(earlierRules[0].PolicyRevision).IsEqualTo(1);
        await Assert.That(source.Revision).IsEqualTo(1);
    }

    [Test]
    public async Task ReplacementRetainsImmutableMeaningAndRequiresNewRuleVersionForChanges()
    {
        var source = Active();
        foreach (var changed in new[]
        {
            Terms(source.Jurisdiction, meaning: "changed meaning"),
            Terms(source.Jurisdiction, id: Id(21)),
            Terms(source.Jurisdiction, duration: TimeSpan.FromDays(8)),
            Terms(source.Jurisdiction, capabilities: [ModerationCapability.Admission]),
            Terms(source.Jurisdiction, purposes: [ModerationPolicyOperationPurpose.Propose])
        })
        {
            await Reject<ArgumentException>(() =>
                source.ReplaceAdoption(source.Jurisdiction, 1, [changed], Id(3), Now));
        }

        await Reject<ArgumentException>(() => source.ReplaceAdoption(source.Jurisdiction, 1,
            [Terms(source.Jurisdiction, version: 2, id: Id(21))], Id(3), Now));
        var next = source.ReplaceAdoption(source.Jurisdiction, 1,
            [source.Rules[0], Terms(source.Jurisdiction, version: 2, id: Id(21), meaning: "new meaning")],
            Id(3), Now);
        await Assert.That(next.Rules.Count).IsEqualTo(2);
        await Assert.That(next.Rules[0].Meaning).IsEqualTo("Confirmed dangerous publication.");
        await Assert.That(next.Rules[1].Meaning).IsEqualTo("new meaning");
    }

    [Test]
    public async Task InvalidStaleAndOverflowingReplacementPreserveEveryPriorFact()
    {
        var source = Active();
        var priorRule = source.Rules[0];
        await Reject<InvalidOperationException>(() => source.ReplaceAdoption(source.Jurisdiction, 0, source.Rules, Id(3), Now));
        await Reject<InvalidOperationException>(() => source.ReplaceAdoption(source.Jurisdiction, 1, source.Rules, Id(3), Now.AddTicks(-1)));
        await Reject<InvalidOperationException>(() => source.ReplaceAdoption(source.Jurisdiction, 1, source.Rules, PublicationId, Now));
        await Reject<ArgumentException>(() => source.Replace(source.Jurisdiction, 1, source.Rules,
            [Evidence(source.Jurisdiction, priorRule.Rule)], Id(3), Now));
        await Assert.That(source.Rules[0]).IsSameReferenceAs(priorRule);
        await Assert.That(source.Revision).IsEqualTo(1);
        await Assert.That(source.PublicationId).IsEqualTo(PublicationId);
        await Assert.That(Permits(source, priorRule.Rule)).IsTrue();

        var overflowing = Hydrate(source);
        Set(overflowing, nameof(ModerationPolicy.Revision), long.MaxValue);
        Set(overflowing.Rules[0], nameof(ModerationPolicyRule.PolicyRevision), long.MaxValue);
        overflowing.ValidatePersistedState();
        await Reject<OverflowException>(() => overflowing.ReplaceAdoption(
            overflowing.Jurisdiction, long.MaxValue, overflowing.Rules, Id(3), Now));
        await Assert.That(overflowing.Revision).IsEqualTo(long.MaxValue);
        await Assert.That(overflowing.PublicationId).IsEqualTo(PublicationId);
        await Assert.That(Permits(overflowing, priorRule.Rule)).IsTrue();
    }

    [Test]
    public async Task NewRevisionRequiresFreshMatchingEvidenceForEveryEnabledEffect()
    {
        var source = Active();
        var time = Now.AddHours(1);
        var nextProof = Evidence(source.Jurisdiction, source.Rules[0].Rule,
            revision: 2, publicationId: Id(3), verifiedAt: time);
        var next = source.Replace(source.Jurisdiction, 1, source.Rules, [nextProof], Id(3), time);
        await Assert.That(next.Revision).IsEqualTo(2);
        await Assert.That(Permits(next, next.Rules[0].Rule)).IsTrue();
    }

    [Test]
    public async Task CallerCollectionsCannotMutateDefinitionsEvidenceOrPublishedState()
    {
        var scope = Scope();
        var caps = new List<ModerationCapability> { ModerationCapability.Publish };
        var purposes = new List<ModerationPolicyOperationPurpose> { ModerationPolicyOperationPurpose.Activate };
        var terms = Terms(scope, capabilities: caps, purposes: purposes);
        caps.Clear();
        purposes.Clear();
        var consumers = new List<string> { "entry", "terminal" };
        var proof = Evidence(scope, terms.Rule, consumers: consumers);
        consumers.Clear();
        var rules = new List<ModerationPolicyRule> { terms };
        var proofs = new List<ModerationPolicyEffectEvidence> { proof };
        var policy = ModerationPolicy.Publish(PolicyId, scope, rules, proofs, PublicationId, Now);
        rules.Clear();
        proofs.Clear();
        await Assert.That(Permits(policy, terms.Rule)).IsTrue();
        await Assert.That(proof.RegisteredConsumers.Count).IsEqualTo(2);
        await Reject<NotSupportedException>(() => ((IList<ModerationPolicyRule>)policy.Rules).Clear());
    }

    [Test]
    public async Task FiniteIndefiniteReviewAndQuorumCeilingsAreEnforcedAtTheirBoundaries()
    {
        var terms = Terms(Scope(), indefinite: true);
        bool Allowed(DateTimeOffset? end, DateTimeOffset? review, int approvals) =>
            terms.Permits(ModerationPolicyOperationPurpose.Activate, [ModerationCapability.Publish],
                Now, end, review, approvals);
        await Assert.That(Allowed(Now.AddDays(7), Now.AddDays(1), 1)).IsTrue();
        await Assert.That(Allowed(Now.AddDays(7).AddTicks(1), null, 1)).IsFalse();
        await Assert.That(Allowed(Now, null, 1)).IsFalse();
        await Assert.That(Allowed(null, Now.AddDays(1), 2)).IsTrue();
        await Assert.That(Allowed(null, Now.AddDays(1), 1)).IsFalse();
        await Assert.That(Allowed(null, null, 2)).IsFalse();
        await Assert.That(Allowed(null, Now.AddDays(1).AddTicks(1), 2)).IsFalse();
        await Assert.That(Allowed(Now.AddHours(1), Now.AddHours(2), 1)).IsFalse();
        await Assert.That(Allowed(Now.AddDays(1), Now, 1)).IsFalse();
        await Assert.That(Allowed(Now.AddDays(1), null, 0)).IsFalse();
        await Assert.That(Allowed(Now.AddDays(1), null, 3)).IsFalse();
        await Assert.That(terms.Permits(ModerationPolicyOperationPurpose.Activate, [],
            Now, Now.AddDays(1), null, 1)).IsFalse();
        await Assert.That(Terms(Scope()).Permits(ModerationPolicyOperationPurpose.Activate,
            [ModerationCapability.Publish], Now, null, Now.AddDays(1), 2)).IsFalse();
    }

    [Test]
    public async Task BroadJurisdictionsCannotUseASingleController()
    {
        foreach (var scope in new[] { Scope().Parent!.Parent!, Scope().Parent!.Parent!.Parent! })
        {
            var terms = Terms(scope);
            var policy = ModerationPolicy.Publish(PolicyId, scope, [terms],
                [Evidence(scope, terms.Rule)], PublicationId, Now);
            await Assert.That(Permits(policy, terms.Rule)).IsFalse();
            await Assert.That(Permits(policy, terms.Rule, approvals: 2)).IsTrue();
        }
    }

    [Test]
    public async Task ProtectivePurposeRequiresFiniteReviewedTermsAndHonorsMinimumQuorum()
    {
        var terms = Terms(Scope(), indefinite: true, min: 2,
            purposes: [ModerationPolicyOperationPurpose.Protect]);
        bool Allowed(DateTimeOffset? end, DateTimeOffset? review, int approvals) =>
            terms.Permits(ModerationPolicyOperationPurpose.Protect, [ModerationCapability.Publish],
                Now, end, review, approvals);
        await Assert.That(Allowed(Now.AddDays(1), Now.AddHours(1), 2)).IsTrue();
        await Assert.That(Allowed(Now.AddDays(1), Now.AddHours(1), 1)).IsFalse();
        await Assert.That(Allowed(Now.AddDays(1), null, 2)).IsFalse();
        await Assert.That(Allowed(null, Now.AddHours(1), 2)).IsFalse();
    }

    [Test]
    public async Task InvalidDefinitionsCannotEnterAnAdoptedPolicy()
    {
        await Reject<ArgumentException>(() => Terms(Scope(), meaning: ""));
        await Reject<ArgumentException>(() => Terms(Scope(), duration: TimeSpan.Zero));
        await Reject<ArgumentException>(() => Terms(Scope(), review: TimeSpan.Zero));
        await Reject<ArgumentException>(() => Terms(Scope(), min: 0));
        await Reject<ArgumentException>(() => Terms(Scope(), max: 3));
        await Reject<ArgumentException>(() => Terms(Scope(), min: 2, max: 1));
        await Reject<ArgumentException>(() => Terms(Scope(), indefinite: true, max: 1));
        await Reject<ArgumentException>(() => Terms(Scope(), purposes: []));
        await Reject<ArgumentException>(() => Terms(Scope(), purposes: [(ModerationPolicyOperationPurpose)99]));
        await Reject<ArgumentException>(() => Terms(Scope(), capabilities: []));
        await Reject<ArgumentException>(() => Terms(Scope(), capabilities: [ModerationCapability.Support]));
        await Reject<ArgumentException>(() => Terms(Scope(), capabilities: [(ModerationCapability)99]));
    }

    [Test]
    public async Task AggregateRuleAndPublicationIdentitiesMustBeUuidVersionSeven()
    {
        foreach (var invalid in new[] { Guid.Empty, Guid.Parse("00000000-0000-4000-8000-000000000001"),
            Guid.Parse("00000000-0000-7000-0000-000000000001") })
        {
            await Reject<ArgumentException>(() => Terms(Scope(), id: invalid));
            await Reject<ArgumentException>(() => ModerationPolicy.Adopt(invalid, Scope(),
                [Terms(Scope())], PublicationId, Now));
            await Reject<ArgumentException>(() => ModerationPolicy.Adopt(PolicyId, Scope(),
                [Terms(Scope())], invalid, Now));
        }
    }

    [Test]
    public async Task ScalarAndChildHydrationPreservesCompleteStateWithoutReplayingPublication()
    {
        var source = Active();
        var row = Hydrate(source);
        row.ValidatePersistedState();
        row.ValidatePersistedState();
        await Assert.That(row.Jurisdiction).IsEqualTo(source.Jurisdiction);
        await Assert.That(row.Rules[0].Rule).IsEqualTo(source.Rules[0].Rule);
        await Assert.That(row.Rules[0].Meaning).IsEqualTo(source.Rules[0].Meaning);
        await Assert.That(Permits(row, row.Rules[0].Rule)).IsTrue();
        var next = row.ReplaceAdoption(row.Jurisdiction, row.Revision, row.Rules, Id(3), Now);
        await Assert.That(next.Revision).IsEqualTo(2);
        await Assert.That(Permits(row, row.Rules[0].Rule)).IsTrue();
    }

    [Test]
    public async Task PartialForeignAndContradictoryHydrationCannotPassStructuralValidation()
    {
        foreach (var (property, value) in new (string, object?)[]
        {
            ("Id", Guid.Empty), ("Revision", 0L), ("PublicationId", Guid.Empty), ("RuleCount", 2),
            ("JurisdictionId", Id(99)),
            ("JurisdictionSolutionId", null), ("JurisdictionKind", ModerationScopeKind.TrustDomain),
            ("UpdatedAt", Now.AddTicks(-1))
        })
        {
            var row = Hydrate(Active());
            Set(row, property, value);
            await Reject<ArgumentException>(row.ValidatePersistedState);
        }

        foreach (var (property, value) in new (string, object?)[]
        {
            ("PolicyId", Id(99)), ("PolicyRevision", 2L), ("RuleOwnerId", Id(99)),
            ("ActiveCapabilityMask", 64), ("CapabilityMask", 4096), ("PurposeMask", 1),
            ("Meaning", ""), ("MaximumReviewDelayTicks", 0L), ("MaximumApprovals", 3)
        })
        {
            var row = Hydrate(Active());
            Set(row.Rules[0], property, value);
            await Reject<ArgumentException>(row.ValidatePersistedState);
        }

        var missing = Hydrate(Active());
        Children(missing).Clear();
        await Reject<ArgumentException>(missing.ValidatePersistedState);
    }

    private static Guid Id(int suffix) => Guid.Parse($"019a0000-0000-7000-8000-{suffix:x12}");

    private static AuthorityScope Scope(int tenant = 13) => AuthorityScope.Tenant(
        AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Id(10)), Id(11)), Id(12)), Id(tenant));

    private static ModerationPolicyRule Terms(
        AuthorityScope scope, long version = 1, Guid? id = null,
        string meaning = "Confirmed dangerous publication.", TimeSpan? duration = null, TimeSpan? review = null,
        bool indefinite = false, int min = 1, int max = 2,
        IEnumerable<ModerationCapability>? capabilities = null,
        IEnumerable<ModerationPolicyOperationPurpose>? purposes = null) =>
        ModerationPolicyRule.Define(id ?? Id(20), ModerationRuleReference.Create(scope.Id, "danger", version),
            meaning, purposes ?? [ModerationPolicyOperationPurpose.Propose, ModerationPolicyOperationPurpose.Activate],
            capabilities ?? [ModerationCapability.Publish, ModerationCapability.Upload],
            duration ?? TimeSpan.FromDays(7), indefinite, review ?? TimeSpan.FromDays(1), min, max);

    private static ModerationPolicyEffectEvidence Evidence(
        AuthorityScope scope, ModerationRuleReference rule, Guid? policyId = null, long revision = 1,
        Guid? publicationId = null, DateTimeOffset? verifiedAt = null,
        ModerationCapability capability = ModerationCapability.Publish,
        IEnumerable<string>? consumers = null, IEnumerable<string>? prerequisites = null) =>
        new(policyId ?? PolicyId, revision, scope, rule, capability, publicationId ?? PublicationId,
            verifiedAt ?? Now, ["adoption"],
            (prerequisites ?? ["adoption"]).Select((code, index) =>
                new ModerationPolicyNativeEvidence(code, Id(200 + index), 1, scope, verifiedAt ?? Now)),
            ["entry", "terminal"],
            (consumers ?? ["entry", "terminal"]).Select((code, index) =>
                new ModerationPolicyNativeEvidence(code, Id(100 + index), 1, scope, verifiedAt ?? Now)));

    private static ModerationPolicy Active()
    {
        var scope = Scope();
        var terms = Terms(scope);
        return ModerationPolicy.Publish(PolicyId, scope, [terms], [Evidence(scope, terms.Rule)], PublicationId, Now);
    }

    private static bool Permits(
        ModerationPolicy policy, ModerationRuleReference rule,
        ModerationCapability capability = ModerationCapability.Publish,
        ModerationPolicyOperationPurpose purpose = ModerationPolicyOperationPurpose.Activate, int approvals = 1) =>
        policy.Permits(policy.Jurisdiction, rule, purpose, [capability], Now, Now.AddDays(1), null, approvals);

    private static async Task Reject<TException>(Action action) where TException : Exception =>
        await Assert.ThrowsAsync<TException>(() =>
        {
            action();
            return Task.CompletedTask;
        });

    private static T HydrateScalars<T>(T source) where T : class
    {
        var type = source.GetType();
        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)
            ?? throw new InvalidOperationException("Missing scalar hydration constructor.");
        var row = (T)constructor.Invoke(null);
        foreach (var property in type.GetProperties().Where(property =>
            property.SetMethod is { IsPrivate: true }
            && (property.PropertyType.IsValueType || property.PropertyType == typeof(string))))
        {
            property.SetValue(row, property.GetValue(source));
        }

        return row;
    }

    private static ModerationPolicy Hydrate(ModerationPolicy source)
    {
        var row = HydrateScalars(source);
        Children(row).AddRange(source.Rules.Select(HydrateScalars));
        return row;
    }

    private static List<ModerationPolicyRule> Children(ModerationPolicy policy) =>
        (List<ModerationPolicyRule>)(typeof(ModerationPolicy).GetField("_rules", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(policy) ?? throw new InvalidOperationException("Missing native child hydration field."));

    private static void Set(object row, string property, object? value) =>
        (row.GetType().GetProperty(property) ?? throw new InvalidOperationException("Missing scalar property."))
        .SetValue(row, value);
}
