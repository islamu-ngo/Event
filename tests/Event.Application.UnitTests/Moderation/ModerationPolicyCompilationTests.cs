using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Application.UnitTests.Moderation;

/// <summary>Pure semantic assertions. No test here establishes deployed conformance or approval.</summary>
public sealed partial class ModerationPolicyCompilationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly AuthorityScope Trust = AuthorityScope.TrustDomain(Id(10));
    private static readonly AuthorityScope Solution = AuthorityScope.Solution(Trust, Id(11));
    private static readonly AuthorityScope Instance = AuthorityScope.Instance(Solution, Id(12));
    private static readonly AuthorityScope Tenant = AuthorityScope.Tenant(Instance, Id(13));
    private static readonly AuthorityScope Community = AuthorityScope.Community(Tenant, Id(14));
    private static readonly AuthorityScope Resource = AuthorityScope.Resource(Community, Id(15));
    private static readonly ModerationCapabilityRegistry Registry =
        ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());

    [Test]
    public async Task Complete_pack_preserves_every_profile_bundle_strategy_and_thirteen_dimension_plan()
    {
        var result = Compile(State(Enum.GetValues<ModerationReferenceProfileId>()));
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyCompilationOutcome.Draft);
        var draft = result.Draft!;
        await Assert.That(draft.Profiles.Length).IsEqualTo(27);
        await Assert.That(draft.Consequences.Select(item => item.Bundle).Distinct().Count()).IsEqualTo(10);
        await Assert.That(draft.Consequences.Where(item => item.Strategy is not null)
            .Select(item => item.Strategy).Distinct().Count()).IsEqualTo(3);
        foreach (var profile in draft.Profiles)
        {
            await Assert.That(profile.Definition.Effects.ToHashSet().SetEquals(profile.Reference.Effects)).IsTrue();
            await Assert.That(profile.Consequences.Length).IsEqualTo(profile.Reference.ConsequencePlans.Count);
            for (var index = 0; index < profile.Consequences.Length; index++)
            {
                var expected = profile.Reference.ConsequencePlans[index];
                // Capture eligibility is independently narrowed by the profile. Every other mode
                // must survive compilation unchanged, not merely remain a defined enum value.
                await Assert.That(profile.Consequences[index] with { Recognition = expected.Recognition })
                    .IsEqualTo(expected);
            }
            foreach (var plan in profile.Consequences)
            {
                await Assert.That(Enum.IsDefined(plan.Content) && Enum.IsDefined(plan.Management)
                    && Enum.IsDefined(plan.Resources) && Enum.IsDefined(plan.NewAdmission)
                    && Enum.IsDefined(plan.ExistingAdmission) && Enum.IsDefined(plan.PrivateDisclosure)
                    && Enum.IsDefined(plan.Settlement) && Enum.IsDefined(plan.Capacity)
                    && Enum.IsDefined(plan.Credentials) && Enum.IsDefined(plan.Notifications)
                    && Enum.IsDefined(plan.Recognition) && Enum.IsDefined(plan.PendingWork)
                    && Enum.IsDefined(plan.Restoration)).IsTrue();
                await Assert.That(plan.Safeguards.OrdinaryPermissionRequired).IsTrue();
                await Assert.That(plan.Safeguards.AutomaticHistoricalRestoration).IsFalse();
            }
        }
    }

    [Test]
    public async Task Every_shipped_response_compiles_without_inventing_a_generic_duration_or_quorum()
    {
        foreach (var reference in ModerationReferenceCatalogue.Profiles.Values)
            foreach (var response in reference.Responses)
            {
                var state = State(reference.Id);
                var definition = state.Profiles[0] with
                {
                    Grade = response.Grade,
                    Timing = new(response.Timing.Kind, response.Timing.Duration),
                    RequiredApprovals = response.RequiredApprovals
                };
                var result = Compile(state with { Profiles = [definition] });
                await Assert.That(result.Draft is not null).IsTrue();
                await Assert.That(result.Draft!.Profiles[0].Definition.Timing.Kind).IsEqualTo(response.Timing.Kind);
                await Assert.That(result.Draft.Profiles[0].Definition.RequiredApprovals).IsEqualTo(response.RequiredApprovals);
            }
    }

    [Test]
    public async Task Active_request_rejects_the_whole_supported_set_without_dropping_unimplemented_consumers()
    {
        var result = Compile(State(ModerationReferenceProfileId.CFG06, ModerationReferenceProfileId.CFG08)
            with
        { RequestedState = ModerationPolicyRequestedState.Active });
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyCompilationOutcome.ActiveRejected);
        await Assert.That(result.Draft!.Profiles.Length).IsEqualTo(2);
        await Assert.That(result.Draft.Consumers.SelectMany(item => item.Consumers)
            .Any(item => item.Id == "queued.moderation")).IsTrue();
        await Assert.That(result.Diagnostics.Any(item => item.Kind == ModerationPolicyDiagnosticKind.UnsupportedConsumer
            && item.ConsumerId == "queued.moderation")).IsTrue();
    }

    [Test]
    public async Task Implemented_source_labels_still_require_current_native_qualification_not_passed_certification()
    {
        var registry = SourceImplementedInventory();
        var state = State(ModerationReferenceProfileId.CFG05) with
        {
            InventoryFingerprint = registry.Fingerprint,
            RequestedState = ModerationPolicyRequestedState.Active
        };
        var result = ModerationPolicyProposedStateCompiler.Compile(state, Context(), registry);
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyCompilationOutcome.RequiresNativePublicationQualification);
        await Assert.That(result.Diagnostics.Any(item => item.Kind == ModerationPolicyDiagnosticKind.NativeQualificationRequired))
            .IsTrue();
        await Assert.That(result.Draft!.Effects.Single().Effect.Mode).IsEqualTo(ModerationReferenceEffectMode.RequireReview);
        await Assert.That(result.Draft.Prerequisites.Single().NativePrerequisites
            .Contains(ModerationReferencePrerequisite.ActualPublicationReview)).IsTrue();
    }

    [Test]
    public async Task Each_selected_mode_keeps_its_exact_consumer_contracts_and_publication_binding()
    {
        var state = State(ModerationReferenceProfileId.CFG08) with
        {
            Modes = [ModerationNativeMode.RemoteStrict, ModerationNativeMode.Embedded]
        };
        var draft = Compile(state).Draft!;
        await Assert.That(draft.Consumers.Length).IsEqualTo(2);
        foreach (var required in draft.Consumers)
        {
            await Assert.That(required.Binding.PolicyRevision).IsEqualTo(1L);
            await Assert.That(required.Binding.PublicationId).IsEqualTo(state.PublicationId);
            await Assert.That(required.Consumers.Select(item => item.Id).ToHashSet(StringComparer.Ordinal)
                .SetEquals(Registry.RequiredConsumers(ModerationCapability.ReadResource, required.Binding.Mode))).IsTrue();
        }
        await Assert.That(draft.Consumers.Single(item => item.Binding.Mode == ModerationNativeMode.RemoteStrict)
            .Consumers.Any(item => item.Id == "remote.receiver")).IsTrue();
    }

    [Test]
    [Arguments(ModerationReferenceProfileId.CFG05)]
    [Arguments(ModerationReferenceProfileId.CFG09)]
    [Arguments(ModerationReferenceProfileId.CFG28)]
    [Arguments(ModerationReferenceProfileId.CFG29)]
    public async Task Nonbinary_native_controls_never_turn_into_general_capability_denials(ModerationReferenceProfileId id)
    {
        var draft = Compile(State(id)).Draft!;
        var projection = ModerationPolicyProposedStateCompiler.Compose(draft, [Contribution(draft, 0, 700)], Now);
        await Assert.That(projection.Outcome).IsEqualTo(ModerationPolicyCompositionOutcome.Preview);
        await Assert.That(projection.Operations.SelectMany(item => item.OverlayDenials).Any()).IsFalse();
        await Assert.That(draft.Effects.All(item => item.Effect.Execution == ModerationReferenceExecution.NativeOwner)).IsTrue();
        await Assert.That(draft.Effects.All(item => Enum.IsDefined(item.Owner))).IsTrue();
    }

    [Test]
    public async Task Capabilityless_form_guard_still_requires_its_native_owner_and_material_target()
    {
        var draft = Compile(State(ModerationReferenceProfileId.CFG26)).Draft!;
        var required = draft.Effects.Single();
        await Assert.That(required.Effect.Capability is null).IsTrue();
        await Assert.That(required.Owner).IsEqualTo(ModerationPolicySemanticOwner.FormAndIncident);
        await Assert.That(required.Effect.Target).IsEqualTo(ModerationReferenceTarget.ImplicatedEventFormDestinationVersions);
        await Assert.That(draft.Prerequisites.Single().NativePrerequisites
            .Contains(ModerationReferencePrerequisite.ProhibitedSecretNoStorageOrEcho)).IsTrue();
        var composition = ModerationPolicyProposedStateCompiler.Compose(draft, [Contribution(draft, 0, 700)], Now.AddDays(10));
        await Assert.That(composition.Operations.Single().Contributions.Single().Effect.Mode)
            .IsEqualTo(ModerationReferenceEffectMode.Guard);
    }

    [Test]
    [Arguments("unknown-profile")]
    [Arguments("duplicate-profile")]
    [Arguments("unknown-effect")]
    [Arguments("missing-effect")]
    [Arguments("duplicate-effect")]
    [Arguments("wrong-execution")]
    [Arguments("missing-strategy")]
    [Arguments("duplicate-strategy")]
    [Arguments("unknown-mode")]
    [Arguments("duplicate-mode")]
    [Arguments("missing-purpose")]
    [Arguments("wrong-purpose")]
    [Arguments("stale-catalogue")]
    [Arguments("stale-inventory")]
    [Arguments("unknown-support")]
    [Arguments("duplicate-support")]
    [Arguments("default-profiles")]
    public async Task Invalid_complete_proposals_never_return_a_partial_compiled_draft(string mutation)
    {
        var state = State(ModerationReferenceProfileId.CFG19, ModerationReferenceProfileId.CFG28);
        var first = state.Profiles[0];
        state = mutation switch
        {
            "unknown-profile" => state with { Profiles = [first with { ReferenceProfile = (ModerationReferenceProfileId)2 }] },
            "duplicate-profile" => state with { Profiles = [first, first] },
            "unknown-effect" => state with { Profiles = [first with { Effects = [first.Effects[0] with { Kind = (ModerationReferenceEffectKind)999 }] }] },
            "missing-effect" => state with { Profiles = [first with { Effects = [first.Effects[0]] }] },
            "duplicate-effect" => state with { Profiles = [first with { Effects = [.. first.Effects, first.Effects[0]] }] },
            "wrong-execution" => state with
            {
                Profiles = [first with { Effects = first.Effects.SetItem(0,
                first.Effects[0] with { Execution = ModerationReferenceExecution.OverlayDenial }) }]
            },
            "missing-strategy" => state with { Profiles = [first, state.Profiles[1] with { Strategies = [] }] },
            "duplicate-strategy" => state with
            {
                Profiles = [first, state.Profiles[1] with
                { Strategies = [ModerationAdditiveStrategyKind.AgeAudienceEligibility, ModerationAdditiveStrategyKind.AgeAudienceEligibility] }]
            },
            "unknown-mode" => state with { Modes = [(ModerationNativeMode)999] },
            "duplicate-mode" => state with { Modes = [ModerationNativeMode.Embedded, ModerationNativeMode.Embedded] },
            "missing-purpose" => state with { Profiles = [first with { ProtectionPurpose = null }, state.Profiles[1]] },
            "wrong-purpose" => state with { Profiles = [first with { ProtectionPurpose = ModerationProtectionPurpose.CredentialRecovery }, state.Profiles[1]] },
            "stale-catalogue" => state with { CatalogueRevision = 999 },
            "stale-inventory" => state with { InventoryFingerprint = "not-current" },
            "unknown-support" => state with { SupportedProfiles = [Ref("missing")] },
            "duplicate-support" => state with { SupportedProfiles = [first.Rule, first.Rule] },
            "default-profiles" => state with { Profiles = default },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        if (!state.Profiles.IsDefault && mutation is not ("unknown-support" or "duplicate-support"))
            state = state with { SupportedProfiles = state.Profiles.Select(item => item.Rule).Distinct().ToImmutableArray() };
        var result = Compile(state);
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyCompilationOutcome.Invalid);
        await Assert.That(result.Draft is null).IsTrue();
        var expected = mutation switch
        {
            "unknown-profile" or "unknown-support" => ModerationPolicyDiagnosticKind.UnknownProfile,
            "duplicate-profile" => ModerationPolicyDiagnosticKind.DuplicateDefinition,
            "unknown-effect" or "missing-effect" or "wrong-execution" => ModerationPolicyDiagnosticKind.UnsupportedEffect,
            "missing-strategy" => ModerationPolicyDiagnosticKind.IncompletePlan,
            "missing-purpose" or "wrong-purpose" => ModerationPolicyDiagnosticKind.InvalidProtectionPurpose,
            "stale-catalogue" or "stale-inventory" => ModerationPolicyDiagnosticKind.StaleBinding,
            _ => ModerationPolicyDiagnosticKind.InvalidShape
        };
        await Assert.That(result.Diagnostics.Any(item => item.Kind == expected)).IsTrue();
    }

    [Test]
    public async Task Protective_purposes_preserve_both_distinct_plans_instead_of_last_one_winning()
    {
        var draft = Compile(State(ModerationReferenceProfileId.CFG19, ModerationReferenceProfileId.CFG20)).Draft!;
        var integrity = draft.Consequences.Single(item => item.ProtectionPurpose == ModerationProtectionPurpose.EntitlementIntegrity);
        var recovery = draft.Consequences.Single(item => item.ProtectionPurpose == ModerationProtectionPurpose.CredentialRecovery);
        await Assert.That(integrity.Plan.ExistingAdmission).IsEqualTo(ModerationExistingAdmissionMode.InvalidateProvedBadEntitlement);
        await Assert.That(recovery.Plan.ExistingAdmission).IsEqualTo(ModerationExistingAdmissionMode.CurrentEligibility);
        await Assert.That(recovery.Plan.Credentials).IsEqualTo(ModerationCredentialMode.VerifyRecoveryBeforeRelease);
        await Assert.That(recovery.Plan.Recognition).IsEqualTo(ModerationRecognitionMode.NoCaptureFromThisControl);
    }

    [Test]
    public async Task Warning_and_object_control_never_inherit_recognition_from_the_unaffected_bundle()
    {
        var draft = Compile(State(ModerationReferenceProfileId.CFG01, ModerationReferenceProfileId.CFG09)).Draft!;
        await Assert.That(draft.Consequences.All(item => item.Plan.Recognition == ModerationRecognitionMode.NoCaptureFromThisControl))
            .IsTrue();
        await Assert.That(draft.Prerequisites.All(item => !item.ActivationGates.Contains(ModerationActivationPrerequisite.RecognitionAndPrivacy)))
            .IsTrue();
    }

    [Test]
    [Arguments("duration")]
    [Arguments("quorum")]
    [Arguments("remedy")]
    [Arguments("scope")]
    [Arguments("effect")]
    [Arguments("timing")]
    public async Task Independently_supplied_floors_and_ceilings_cannot_be_widened_by_a_profile(string bound)
    {
        var state = State(ModerationReferenceProfileId.CFG06);
        var context = Context();
        var bounds = bound switch
        {
            "duration" => context.Bounds with { MaximumFiniteDuration = TimeSpan.FromDays(1) },
            "quorum" => context.Bounds with { MinimumApprovals = 2 },
            "remedy" => context.Bounds with { MinimumContestationCalendarMonths = 12 },
            "scope" => context.Bounds with { ScopeKinds = [ModerationScopeKind.Tenant] },
            "effect" => context.Bounds with { Effects = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG01).Effects.ToImmutableArray() },
            "timing" => context.Bounds with { TimingKinds = [ModerationReferenceTimingKind.NoEnforcementInterval] },
            _ => throw new ArgumentOutOfRangeException(nameof(bound))
        };
        var result = ModerationPolicyProposedStateCompiler.Compile(state, context with { Bounds = bounds }, Registry);
        await Assert.That(result.Draft is null).IsTrue();
        await Assert.That(result.Diagnostics.Any(item => item.Kind == ModerationPolicyDiagnosticKind.OutsideAuthority)).IsTrue();
    }

    [Test]
    public async Task Reason_and_class_must_resolve_the_exact_qualified_ground_and_basis()
    {
        var state = State(ModerationReferenceProfileId.CFG06);
        var mismatched = state.Classes[0] with { Ground = ModerationReferenceGround.CredibleImminentPhysicalRisk };
        var result = Compile(state with { Classes = [mismatched] });
        await Assert.That(result.Draft is null).IsTrue();
        await Assert.That(result.Diagnostics.Any(item => item.Kind == ModerationPolicyDiagnosticKind.IncompatibleMeaning)).IsTrue();
    }

    [Test]
    [Arguments("class")]
    [Arguments("reason")]
    [Arguments("profile")]
    [Arguments("omission")]
    [Arguments("stale")]
    [Arguments("publication")]
    public async Task Historical_meaning_and_current_publication_binding_cannot_be_rewritten(string change)
    {
        var current = Compile(State(ModerationReferenceProfileId.CFG06)).Draft!;
        var next = current.Proposed with { ExpectedRevision = 1, PublicationId = Id(3) };
        next = change switch
        {
            "class" => next with { Classes = [next.Classes[0] with { Meaning = "Different meaning" }] },
            "reason" => next with { Reasons = [next.Reasons[0] with { Meaning = "Different reason" }] },
            "profile" => next with { Profiles = [next.Profiles[0] with { Timing = new(ModerationReferenceTimingKind.Finite, TimeSpan.FromDays(1)) }] },
            "omission" => State(ModerationReferenceProfileId.CFG05) with { ExpectedRevision = 1, PublicationId = Id(3) },
            "stale" => next with { ExpectedRevision = 0 },
            "publication" => next with { PublicationId = current.Proposed.PublicationId },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        var result = ModerationPolicyProposedStateCompiler.Compile(next, Context() with { Current = current }, Registry);
        await Assert.That(result.Draft is null).IsTrue();
    }

    [Test]
    public async Task Retirement_retains_historical_meaning_and_publishes_a_new_monotonic_draft_revision()
    {
        var current = Compile(State(ModerationReferenceProfileId.CFG06)).Draft!;
        var next = current.Proposed with { ExpectedRevision = 1, PublicationId = Id(3), SupportedProfiles = [] };
        var result = ModerationPolicyProposedStateCompiler.Compile(next, Context() with { Current = current }, Registry);
        await Assert.That(result.Draft!.Publication.ProposedRevision).IsEqualTo(2L);
        await Assert.That(result.Draft.Profiles.Length).IsEqualTo(1);
        await Assert.That(result.Draft.Consumers.IsEmpty).IsTrue();
        await Assert.That(current.Publication.ProposedRevision).IsEqualTo(1L);
    }

    private static ModerationPolicyCompilationResult Compile(ModerationPolicyProposedState state) =>
        ModerationPolicyProposedStateCompiler.Compile(state, Context(), Registry);

    private static ModerationPolicyProposedState State(params ModerationReferenceProfileId[] ids)
    {
        var profiles = ids.Select(id =>
        {
            var reference = ModerationReferenceCatalogue.GetProfile(id);
            var response = reference.Responses[0];
            return new ModerationPolicyProfileDefinition(Id(2000 + (int)id), Ref($"profile.{reference.Code}"),
                Ref($"reason.{reference.Code}"), id, reference.Revision, response.Grade,
                [reference.ScopeKinds.Max()], [reference.SubjectKinds.Order().First()],
                reference.Effects.ToImmutableArray(), reference.Bundle, reference.ProtectionPurpose,
                reference.Strategies.Order().ToImmutableArray(), new(response.Timing.Kind, response.Timing.Duration),
                response.RequiredApprovals, 6, reference.Recognition);
        }).ToImmutableArray();
        return new(Id(1), 0, Id(2), Trust, ModerationReferenceCatalogue.Revision, Registry.Revision,
            Registry.Fingerprint, ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
            ids.Select(id =>
            {
                var reference = ModerationReferenceCatalogue.GetProfile(id);
                return new ModerationPolicyClassDefinition(Ref($"class.{reference.Code}"),
                    $"Defined {reference.Ground}", reference.Ground, reference.Basis);
            }).ToImmutableArray(),
            ids.Select(id => new ModerationPolicyReasonDefinition(Ref($"reason.CFG{(int)id:00}"),
                $"Reason for CFG{(int)id:00}", Ref($"class.CFG{(int)id:00}"))).ToImmutableArray(),
            profiles, profiles.Select(item => item.Rule).ToImmutableArray());
    }

    private static ModerationPolicyCompilationContext Context() => new(Id(20), Id(21), 1, "generation-1",
        new(Trust, Enum.GetValues<ModerationScopeKind>().ToImmutableArray(),
            Enum.GetValues<ModerationSubjectKind>().ToImmutableArray(),
            ModerationReferenceCatalogue.Profiles.Values.SelectMany(item => item.Effects).Distinct().ToImmutableArray(),
            Enum.GetValues<ModerationReferenceTimingKind>().ToImmutableArray(),
            TimeSpan.FromDays(365), TimeSpan.FromDays(90), 0, 2, 6), null);

    private static ModerationRuleReference Ref(string code) => ModerationRuleReference.Create(Trust.Id, code, 1);
    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");

    private static ModerationCapabilityRegistry SourceImplementedInventory()
    {
        var source = JsonNode.Parse(ModerationCapabilityRegistry.ReadInventory())!.AsObject();
        foreach (var item in source["consumers"]!.AsArray())
            item!["supportState"] = nameof(ModerationSourceSupport.ImplementedUnverified);
        foreach (var item in source["modes"]!.AsArray())
            item!["supportState"] = nameof(ModerationSourceSupport.ImplementedUnverified);
        return ModerationCapabilityRegistry.Parse(source.ToJsonString());
    }

    private static ModerationPolicyContribution Contribution(
        ModerationPolicyCompiledDraft draft, int profileIndex, int id,
        DateTimeOffset? startsAt = null, DateTimeOffset? eventEnd = null)
    {
        var definition = draft.Profiles[profileIndex].Definition;
        var scope = definition.ScopeKinds.Single() switch
        {
            ModerationScopeKind.TrustDomain => Trust,
            ModerationScopeKind.Solution => Solution,
            ModerationScopeKind.Instance => Instance,
            ModerationScopeKind.Tenant => Tenant,
            ModerationScopeKind.Community => Community,
            ModerationScopeKind.Resource => Resource,
            _ => throw new InvalidOperationException()
        };
        var subject = definition.SubjectKinds.Single();
        ModerationPolicyTarget Target(string operation) => new(scope, subject, Id(600),
            Id(900), 1, operation, eventEnd);
        return new(Id(id), definition.Rule, scope, subject, Id(600), startsAt ?? Now, eventEnd,
            definition.Effects.Select(effect => new ModerationPolicyEffectTarget(effect,
                Target(effect.Capability?.ToString() ?? effect.Kind.ToString()))).ToImmutableArray(),
            new[] { new ModerationPolicyConsequenceTarget(null, Target("native.lifecycle")) }
                .Concat(definition.Strategies.Select(strategy =>
                    new ModerationPolicyConsequenceTarget(strategy, Target("native.lifecycle")))).ToImmutableArray());
    }
}
