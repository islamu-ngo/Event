using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Maps normalized native entities to compiler values within the owning Application assembly.
/// It restores saved plans rather than recompiling consequences against today's catalogue.
/// A different catalogue revision requires its real historical catalogue reader, never reinterpretation.
/// </summary>
public static class ModerationPolicyPublicationProjection
{
    public const string WriterGeneration = "native-moderation-publication-v1";

    /// <summary>
    /// Finite authoring envelope for an already qualified native administrator. These are not
    /// deployed/legal approvals; active publication separately qualifies the entire envelope.
    /// </summary>
    public static ModerationPolicyBounds AuthoringBounds(AuthorityScope jurisdiction) => new(
        jurisdiction, jurisdiction.Kind switch
        {
            ModerationScopeKind.TrustDomain => [ModerationScopeKind.TrustDomain, ModerationScopeKind.Solution,
                ModerationScopeKind.Instance, ModerationScopeKind.Tenant, ModerationScopeKind.Community,
                ModerationScopeKind.Resource, ModerationScopeKind.Event],
            ModerationScopeKind.Solution => [ModerationScopeKind.Solution, ModerationScopeKind.Instance,
                ModerationScopeKind.Tenant, ModerationScopeKind.Community, ModerationScopeKind.Resource,
                ModerationScopeKind.Event],
            ModerationScopeKind.Instance => [ModerationScopeKind.Instance, ModerationScopeKind.Tenant,
                ModerationScopeKind.Community, ModerationScopeKind.Resource, ModerationScopeKind.Event],
            ModerationScopeKind.Tenant => [ModerationScopeKind.Tenant, ModerationScopeKind.Community,
                ModerationScopeKind.Resource, ModerationScopeKind.Event],
            ModerationScopeKind.Community => [ModerationScopeKind.Community, ModerationScopeKind.Resource],
            ModerationScopeKind.Resource => [ModerationScopeKind.Resource],
            ModerationScopeKind.Event => [ModerationScopeKind.Event],
            _ => throw new ArgumentOutOfRangeException(nameof(jurisdiction))
        },
        Enum.GetValues<ModerationSubjectKind>().ToImmutableArray(),
        ModerationReferenceCatalogue.Profiles.Values.SelectMany(profile => profile.Effects).Distinct()
            .OrderBy(effect => effect.Kind).ThenBy(effect => effect.Capability).ToImmutableArray(),
        Enum.GetValues<ModerationReferenceTimingKind>().ToImmutableArray(),
        TimeSpan.FromDays(36600), TimeSpan.FromDays(36600), 0, 2, 6);

    public static ModerationPolicyCompiledDraft? Restore(
        ModerationPolicyPublication publication, ModerationCapabilityRegistry inventory) =>
        RestoreCore(publication, inventory);

    /// <summary>
    /// Historical declarations for the compiler's monotonic history comparison. A new inventory
    /// revision cannot erase old meanings or prevent authoring its replacement. This path never
    /// qualifies an active selection, and its retained consumer observations are not current truth.
    /// </summary>
    public static ModerationPolicyCompiledDraft? RestoreHistory(ModerationPolicyPublication publication) =>
        RestoreCore(publication, null);

    /// <summary>Rebinds the immutable definition to its reserved future identity; no selection or activation is implied.</summary>
    public static ModerationPolicyCompiledDraft? RestoreCandidate(
        ModerationPolicyPublicationCandidate candidate, ModerationCapabilityRegistry? inventory)
    {
        candidate.ValidatePersistedState();
        if (candidate.Definition is not { } definition || definition.AdoptedRevision is not null)
            return null;
        var restored = RestoreCore(definition, inventory);
        if (restored is null) return null;
        var proposed = restored.Proposed with
        {
            PublicationId = candidate.Id, RequestedState = ModerationPolicyRequestedState.Active
        };
        if (ProposedDigest(proposed) != candidate.ProposedStateDigest) return null;
        return new(proposed, restored.Publication with { PublicationId = candidate.Id },
            restored.Bounds, restored.Profiles,
            restored.Consumers.Select(row => row with
            {
                Binding = row.Binding with { PublicationId = candidate.Id }
            }).ToImmutableArray(), restored.Effects, restored.Consequences, restored.Prerequisites);
    }

    private static ModerationPolicyCompiledDraft? RestoreCore(
        ModerationPolicyPublication publication, ModerationCapabilityRegistry? inventory)
    {
        if (publication.CatalogueRevision != ModerationReferenceCatalogue.Revision
            || publication.WriterGeneration != WriterGeneration
            || publication.Profiles.Any(profile =>
                !ModerationReferenceCatalogue.Profiles.TryGetValue(profile.ReferenceProfile, out var reference)
                || reference.Revision != profile.ReferenceRevision))
            return null;

        var scope = publication.Jurisdiction;
        ModerationRuleReference Rule(string code, long revision) => ModerationRuleReference.Create(scope.Id, code, revision);
        var profiles = publication.Profiles.OrderBy(profile => profile.RuleCode, StringComparer.Ordinal)
            .ThenBy(profile => profile.RuleRevision).Select(profile => new ModerationPolicyProfileDefinition(
                profile.ProfileId, Rule(profile.RuleCode, profile.RuleRevision), Rule(profile.ReasonCode, profile.ReasonRevision),
                profile.ReferenceProfile, profile.ReferenceRevision, profile.Grade,
                profile.Scopes.OrderBy(row => row.Kind).Select(row => row.Kind).ToImmutableArray(),
                profile.Subjects.OrderBy(row => row.Kind).Select(row => row.Kind).ToImmutableArray(),
                profile.Effects.OrderBy(row => row.Ordinal).Select(row => row.Meaning).ToImmutableArray(),
                profile.Bundle, profile.ProtectionPurpose,
                profile.Plans.Where(row => row.Strategy != 0).OrderBy(row => row.Strategy)
                    .Select(row => (ModerationAdditiveStrategyKind)row.Strategy).ToImmutableArray(),
                new(profile.TimingKind, TimeSpan.FromTicks(profile.DurationTicks)), profile.RequiredApprovals,
                profile.ContestationCalendarMonths, profile.Recognition)).ToImmutableArray();
        var proposed = new ModerationPolicyProposedState(publication.PolicyId, publication.Revision - 1,
            publication.Id, scope, publication.CatalogueRevision, publication.InventoryRevision,
            publication.InventoryFingerprint,
            publication.PublishedActive ? ModerationPolicyRequestedState.Active : ModerationPolicyRequestedState.Draft,
            publication.Modes.OrderBy(row => row.Mode).Select(row => (ModerationNativeMode)row.Mode).ToImmutableArray(),
            publication.Classes.OrderBy(row => row.Code, StringComparer.Ordinal).ThenBy(row => row.Revision)
                .Select(row => new ModerationPolicyClassDefinition(Rule(row.Code, row.Revision), row.Meaning, row.Ground, row.Basis)).ToImmutableArray(),
            publication.Reasons.OrderBy(row => row.Code, StringComparer.Ordinal).ThenBy(row => row.Revision)
                .Select(row => new ModerationPolicyReasonDefinition(Rule(row.Code, row.Revision), row.Meaning,
                    Rule(row.ClassCode, row.ClassRevision))).ToImmutableArray(),
            profiles, profiles.Where(profile => publication.Profiles.Single(row => row.ProfileId == profile.Id).Supported)
                .Select(profile => profile.Rule).ToImmutableArray());
        var requirement = new ModerationPolicyPublicationRequirement(publication.HostBindingId,
            publication.AuthoritySourceId, publication.AuthorityRevision, publication.WriterGeneration,
            publication.PolicyId, publication.Revision - 1, publication.Revision, publication.Id, scope,
            publication.CatalogueRevision, publication.InventoryRevision, publication.InventoryFingerprint);
        var bounds = AuthoringBounds(scope) with
        {
            MaximumFiniteDuration = TimeSpan.FromTicks(publication.MaximumFiniteDurationTicks),
            MaximumReviewDelay = TimeSpan.FromTicks(publication.MaximumReviewDelayTicks),
            MinimumApprovals = publication.MinimumApprovals, MaximumApprovals = publication.MaximumApprovals,
            MinimumContestationCalendarMonths = publication.MinimumContestationCalendarMonths
        };
        var compiled = profiles.Select(profile => new ModerationPolicyCompiledProfile(profile,
            ModerationReferenceCatalogue.GetProfile(profile.ReferenceProfile),
            publication.Profiles.Single(row => row.ProfileId == profile.Id).Plans.OrderBy(row => row.Strategy)
                .Select(row => row.Meaning).ToImmutableArray())).ToImmutableArray();
        var consumers = ImmutableArray.CreateBuilder<ModerationPolicyConsumerRequirement>();
        var effects = ImmutableArray.CreateBuilder<ModerationPolicyEffectRequirement>();
        var consequences = ImmutableArray.CreateBuilder<ModerationPolicyConsequenceRequirement>();
        var prerequisites = ImmutableArray.CreateBuilder<ModerationPolicyPrerequisiteRequirement>();
        foreach (var profile in profiles.Where(profile => proposed.SupportedProfiles.Contains(profile.Rule)))
        {
            var row = publication.Profiles.Single(row => row.ProfileId == profile.Id);
            foreach (var mode in proposed.Modes)
            {
                foreach (var group in row.Consumers.Where(item => item.Mode == (int)mode).GroupBy(item => item.Capability).OrderBy(group => group.Key))
                    consumers.Add(new(new(publication.PolicyId, publication.Revision, publication.Id, scope, profile.Rule, group.Key, mode),
                        group.OrderBy(item => item.ConsumerId, StringComparer.Ordinal).Select(Contract).ToImmutableArray()));
                foreach (var effect in row.Effects.OrderBy(item => item.Kind).ThenBy(item => item.Capability))
                    effects.Add(new(profile.Rule, mode, effect.Meaning, (ModerationPolicySemanticOwner)effect.SemanticOwner,
                        row.Consumers.Where(item => item.Mode == (int)mode && item.Capability == effect.Capability)
                            .OrderBy(item => item.ConsumerId, StringComparer.Ordinal).Select(Contract).ToImmutableArray()));
                foreach (var plan in row.Plans.OrderBy(item => item.Strategy))
                    consequences.Add(new(profile.Rule, mode, profile.Bundle, profile.ProtectionPurpose,
                        plan.Strategy == 0 ? null : (ModerationAdditiveStrategyKind)plan.Strategy, plan.Meaning));
                prerequisites.Add(new(profile.Rule, mode,
                    row.Prerequisites.Where(item => item.Mode == (int)mode && item.ActivationGate != null)
                        .OrderBy(item => item.Ordinal).Select(item => (ModerationActivationPrerequisite)item.ActivationGate!.Value).ToImmutableArray(),
                    row.Prerequisites.Where(item => item.Mode == (int)mode && item.NativePrerequisite != null)
                        .OrderBy(item => item.Ordinal).Select(item => item.NativePrerequisite!.Value).ToImmutableArray()));
            }
        }
        var restored = new ModerationPolicyCompiledDraft(proposed, requirement, bounds, compiled,
            consumers.ToImmutable(), effects.ToImmutable(), consequences.ToImmutable(), prerequisites.ToImmutable());
        if (ProposedDigest(proposed) != publication.DeclaredStateDigest) return null;
        // History still requires every retained base/additive plan. The declaration digest alone
        // cannot detect a deleted base plan because base plans do not add a strategy identifier.
        foreach (var profile in compiled)
        {
            var definition = profile.Definition;
            var expected = new[] { ModerationReferenceCatalogue.GetBundle(definition.Bundle)
                .Resolve(definition.ProtectionPurpose) }
                .Concat(definition.Strategies.Order().Select(ModerationReferenceCatalogue.GetStrategy))
                .Select(plan => definition.Recognition == ModerationReferenceRecognition.Never
                    ? plan with { Recognition = ModerationRecognitionMode.NoCaptureFromThisControl } : plan);
            if (!profile.Consequences.SequenceEqual(expected)) return null;
        }
        if (inventory is null) return restored;
        // A missing relational child must not turn the complete inventory into a passing subset.
        // Compile only as an integrity oracle under the exact retained catalogue/inventory revision,
        // then return the persisted plans. The artificial initial revision is never published.
        var integrity = ModerationPolicyProposedStateCompiler.Compile(proposed with { ExpectedRevision = 0 },
            new(publication.HostBindingId, publication.AuthoritySourceId, publication.AuthorityRevision,
                publication.WriterGeneration, bounds, null), inventory).Draft;
        if (integrity is null
            || integrity.Profiles.Length != compiled.Length
            || integrity.Profiles.Where((profile, index) => !profile.Consequences.SequenceEqual(compiled[index].Consequences)).Any()
            || Digest(integrity.Consumers.Select(item => item with
                { Binding = item.Binding with { PolicyRevision = publication.Revision } }).ToImmutableArray()) != Digest(restored.Consumers)
            || Digest(integrity.Effects) != Digest(restored.Effects)
            || Digest(integrity.Consequences) != Digest(restored.Consequences)
            || Digest(integrity.Prerequisites) != Digest(restored.Prerequisites))
            return null;
        return restored;
    }

    public static string Digest<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    /// <summary>Set order is not meaning; relational hydration must retain the same approval binding.</summary>
    public static string ProposedDigest(ModerationPolicyProposedState proposed) => Digest(CanonicalProposed(proposed));

    public static ModerationPolicyProposedState CanonicalProposed(ModerationPolicyProposedState proposed) => proposed with
    {
        Modes = proposed.Modes.Order().ToImmutableArray(),
        Classes = proposed.Classes.OrderBy(row => row.Reference.Code, StringComparer.Ordinal)
            .ThenBy(row => row.Reference.Revision).ToImmutableArray(),
        Reasons = proposed.Reasons.OrderBy(row => row.Reference.Code, StringComparer.Ordinal)
            .ThenBy(row => row.Reference.Revision).ToImmutableArray(),
        SupportedProfiles = proposed.SupportedProfiles.OrderBy(row => row.Code, StringComparer.Ordinal)
            .ThenBy(row => row.Revision).ToImmutableArray(),
        Profiles = proposed.Profiles.OrderBy(row => row.Rule.Code, StringComparer.Ordinal)
            .ThenBy(row => row.Rule.Revision).Select(row => row with
            {
                ScopeKinds = row.ScopeKinds.Order().ToImmutableArray(),
                SubjectKinds = row.SubjectKinds.Order().ToImmutableArray(),
                Effects = row.Effects.OrderBy(effect => effect.Kind).ThenBy(effect => effect.Capability)
                    .ThenBy(effect => effect.Mode).ThenBy(effect => effect.Target).ThenBy(effect => effect.Execution).ToImmutableArray(),
                Strategies = row.Strategies.Order().ToImmutableArray()
            }).ToImmutableArray()
    };

    private static ModerationConsumerContract Contract(ModerationPolicyPublicationConsumer row) => new(
        row.ConsumerId, row.SubjectResolver, row.TargetResolver, row.AuthorizationOwner, row.EntryCheckpoint,
        row.TerminalCheckpoint, row.WriterFence, (ModerationConsumerFreshness)row.FreshnessClass, row.OwningPacket, row.ConformanceTest);
}
