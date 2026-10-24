using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Application.Features.Moderation.Policy;

/// <summary>
/// Compiles bounded proposed meaning, never native truth. Actual publication must join current
/// authority, deployment, conformance, prerequisite and consequence owners under one transaction.
/// Inventory support metadata can disprove support; it cannot prove measured conformance.
/// </summary>
public static partial class ModerationPolicyProposedStateCompiler
{
    private const int MaximumDefinitions = 256;

    public static ModerationPolicyCompilationResult Compile(
        ModerationPolicyProposedState proposed, ModerationPolicyCompilationContext context,
        ModerationCapabilityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var issues = ImmutableArray.CreateBuilder<ModerationPolicyDiagnostic>();
        void Error(ModerationPolicyDiagnosticKind kind, ModerationRuleReference? rule = null) =>
            issues.Add(new(kind, ModerationPolicyDiagnosticSeverity.Error, rule));

        if (!ValidShape(proposed, context))
        {
            Error(ModerationPolicyDiagnosticKind.InvalidShape);
            return new(ModerationPolicyCompilationOutcome.Invalid, null, issues.ToImmutable());
        }

        var current = context.Current;
        if (proposed.CatalogueRevision != ModerationReferenceCatalogue.Revision
            || proposed.InventoryRevision != registry.Revision
            || proposed.InventoryFingerprint != registry.Fingerprint
            || proposed.Jurisdiction != context.Bounds.Jurisdiction
            || proposed.ExpectedRevision != (current?.Publication.ProposedRevision ?? 0)
            || current is not null && (current.Proposed.PolicyId != proposed.PolicyId
                || current.Proposed.Jurisdiction != proposed.Jurisdiction
                || current.Proposed.PublicationId == proposed.PublicationId))
            Error(ModerationPolicyDiagnosticKind.StaleBinding);

        var classes = new Dictionary<ModerationRuleReference, ModerationPolicyClassDefinition>();
        foreach (var definition in proposed.Classes)
        {
            if (!Owned(definition.Reference, proposed.Jurisdiction) || !Text(definition.Meaning, 4000)
                || !Enum.IsDefined(definition.Ground) || !Enum.IsDefined(definition.Basis))
                Error(ModerationPolicyDiagnosticKind.InvalidShape, definition.Reference);
            else if (!classes.TryAdd(definition.Reference, definition))
                Error(ModerationPolicyDiagnosticKind.DuplicateDefinition, definition.Reference);
        }

        var reasons = new Dictionary<ModerationRuleReference, ModerationPolicyReasonDefinition>();
        foreach (var reason in proposed.Reasons)
        {
            if (!Owned(reason.Reference, proposed.Jurisdiction) || !Text(reason.Meaning, 4000)
                || reason.Class is null || !classes.ContainsKey(reason.Class))
                Error(ModerationPolicyDiagnosticKind.IncompatibleMeaning, reason.Reference);
            else if (!reasons.TryAdd(reason.Reference, reason))
                Error(ModerationPolicyDiagnosticKind.DuplicateDefinition, reason.Reference);
        }

        var profiles = ImmutableArray.CreateBuilder<ModerationPolicyCompiledProfile>();
        var references = new HashSet<ModerationRuleReference>();
        var identities = new HashSet<Guid>();
        foreach (var definition in proposed.Profiles.OrderBy(item => item.Rule?.Code, StringComparer.Ordinal)
                     .ThenBy(item => item.Rule?.Revision))
        {
            var start = issues.Count;
            if (!Owned(definition.Rule, proposed.Jurisdiction) || !Version7(definition.Id)
                || definition.Reason is null || !reasons.ContainsKey(definition.Reason)
                || !UniqueEnums(definition.ScopeKinds) || !UniqueEnums(definition.SubjectKinds)
                || !Unique(definition.Effects) || !UniqueEnums(definition.Strategies, allowEmpty: true)
                || definition.Timing is null || !Enum.IsDefined(definition.Recognition)
                || definition.ContestationCalendarMonths is < 0 or > 120)
            {
                Error(ModerationPolicyDiagnosticKind.InvalidShape, definition.Rule);
                continue;
            }
            if (!references.Add(definition.Rule) || !identities.Add(definition.Id))
                Error(ModerationPolicyDiagnosticKind.DuplicateDefinition, definition.Rule);
            if (!ModerationReferenceCatalogue.Profiles.TryGetValue(definition.ReferenceProfile, out var reference))
            {
                Error(ModerationPolicyDiagnosticKind.UnknownProfile, definition.Rule);
                continue;
            }
            if (definition.ReferenceRevision != reference.Revision)
                Error(ModerationPolicyDiagnosticKind.StaleBinding, definition.Rule);
            var reason = reasons[definition.Reason];
            var classification = classes[reason.Class];
            if (classification.Ground != reference.Ground || classification.Basis != reference.Basis)
                Error(ModerationPolicyDiagnosticKind.IncompatibleMeaning, definition.Rule);
            if (!SameSet(definition.Effects, reference.Effects))
                Error(ModerationPolicyDiagnosticKind.UnsupportedEffect, definition.Rule);
            if (definition.Bundle != reference.Bundle || !SameSet(definition.Strategies, reference.Strategies))
                Error(ModerationPolicyDiagnosticKind.IncompletePlan, definition.Rule);
            if (definition.ProtectionPurpose != reference.ProtectionPurpose)
                Error(ModerationPolicyDiagnosticKind.InvalidProtectionPurpose, definition.Rule);
            if (!definition.ScopeKinds.All(reference.ScopeKinds.Contains)
                || !definition.SubjectKinds.All(reference.SubjectKinds.Contains))
                Error(ModerationPolicyDiagnosticKind.OutsideAuthority, definition.Rule);
            if (definition.Recognition != ModerationReferenceRecognition.Never
                && definition.Recognition != reference.Recognition)
                Error(ModerationPolicyDiagnosticKind.IncompatibleMeaning, definition.Rule);

            var response = reference.Responses.SingleOrDefault(item => item.Grade == definition.Grade);
            if (response is null || !ValidTiming(definition.Timing, response.Timing)
                || definition.RequiredApprovals < (response?.RequiredApprovals ?? 0)
                || definition.RequiredApprovals > 2
                || definition.Timing.Kind == ModerationReferenceTimingKind.ReviewedIndefinite
                    && definition.RequiredApprovals != 2
                || definition.ScopeKinds.Any(kind => kind is ModerationScopeKind.Solution or ModerationScopeKind.TrustDomain)
                    && definition.Grade != ModerationReferenceGrade.G0 && definition.RequiredApprovals != 2)
                Error(ModerationPolicyDiagnosticKind.InvalidResponse, definition.Rule);

            if (proposed.SupportedProfiles.Contains(definition.Rule) && !WithinBounds(definition, context.Bounds))
                Error(ModerationPolicyDiagnosticKind.OutsideAuthority, definition.Rule);
            if (issues.Count == start)
                profiles.Add(new(definition, reference,
                    new[] { PlanFor(definition, null) }.Concat(definition.Strategies.Order()
                        .Select(strategy => PlanFor(definition, strategy))).ToImmutableArray()));
        }

        foreach (var supported in proposed.SupportedProfiles)
            if (!references.Contains(supported))
                Error(ModerationPolicyDiagnosticKind.UnknownProfile, supported);
        if (current is not null && issues.Count == 0)
            ValidateHistory(proposed, current.Proposed, issues);
        if (issues.Any(issue => issue.Severity == ModerationPolicyDiagnosticSeverity.Error))
            return new(ModerationPolicyCompilationOutcome.Invalid, null, issues.ToImmutable());

        var revision = proposed.ExpectedRevision + 1;
        var consumers = ImmutableArray.CreateBuilder<ModerationPolicyConsumerRequirement>();
        var effects = ImmutableArray.CreateBuilder<ModerationPolicyEffectRequirement>();
        var consequences = ImmutableArray.CreateBuilder<ModerationPolicyConsequenceRequirement>();
        var prerequisites = ImmutableArray.CreateBuilder<ModerationPolicyPrerequisiteRequirement>();
        foreach (var profile in profiles.Where(item => proposed.SupportedProfiles.Contains(item.Definition.Rule)))
        {
            var definition = profile.Definition;
            foreach (var mode in proposed.Modes.Order())
            {
                if (registry.Modes[mode].SupportState != ModerationSourceSupport.ImplementedUnverified)
                    issues.Add(new(ModerationPolicyDiagnosticKind.UnsupportedMode,
                        ModerationPolicyDiagnosticSeverity.ActivationBlocker, definition.Rule, Mode: mode));
                foreach (var capability in profile.Reference.CapabilityEnvelope.Order())
                {
                    var required = registry.RequiredConsumers(capability, mode).Order(StringComparer.Ordinal)
                        .Select(id => registry.Consumers[id]).ToArray();
                    var binding = new ModerationActivationBinding(proposed.PolicyId, revision, proposed.PublicationId,
                        proposed.Jurisdiction, definition.Rule, capability, mode);
                    consumers.Add(new(binding, required.Select(item => item.Contract).ToImmutableArray()));
                    foreach (var consumer in required.Where(item =>
                                 item.SupportState != ModerationSourceSupport.ImplementedUnverified))
                        issues.Add(new(ModerationPolicyDiagnosticKind.UnsupportedConsumer,
                            ModerationPolicyDiagnosticSeverity.ActivationBlocker, definition.Rule, consumer.Contract.Id, mode));
                }
                foreach (var effect in definition.Effects.OrderBy(item => item.Kind).ThenBy(item => item.Capability))
                {
                    var required = effect.Capability is { } capability
                        ? registry.RequiredConsumers(capability, mode).Order(StringComparer.Ordinal)
                            .Select(id => registry.Consumers[id].Contract).ToImmutableArray()
                        : ImmutableArray<ModerationConsumerContract>.Empty;
                    effects.Add(new(definition.Rule, mode, effect, Owner(effect.Kind), required));
                }
                consequences.Add(new(definition.Rule, mode, definition.Bundle, definition.ProtectionPurpose,
                    null, PlanFor(definition, null)));
                foreach (var strategy in definition.Strategies.Order())
                    consequences.Add(new(definition.Rule, mode, definition.Bundle, definition.ProtectionPurpose,
                        strategy, PlanFor(definition, strategy)));
                prerequisites.Add(new(definition.Rule, mode, Gates(profile),
                    profile.Reference.Prerequisites
                        .Concat(definition.Recognition == ModerationReferenceRecognition.Never
                            ? [] : profile.Reference.RecognitionPrerequisites)
                        .Distinct().Order().ToImmutableArray()));
            }
            issues.Add(new(ModerationPolicyDiagnosticKind.NativeQualificationRequired,
                ModerationPolicyDiagnosticSeverity.PublicationObligation, definition.Rule));
        }

        var publication = new ModerationPolicyPublicationRequirement(
            context.HostBindingId, context.AuthoritySourceId, context.AuthorityRevision, context.WriterGeneration,
            proposed.PolicyId, proposed.ExpectedRevision, revision, proposed.PublicationId, proposed.Jurisdiction,
            proposed.CatalogueRevision, registry.Revision, registry.Fingerprint);
        var draft = new ModerationPolicyCompiledDraft(proposed, publication, context.Bounds, profiles.ToImmutable(),
            consumers.ToImmutable(), effects.ToImmutable(), consequences.ToImmutable(), prerequisites.ToImmutable());
        var diagnostics = issues.Distinct().OrderBy(item => item.Kind)
            .ThenBy(item => item.Rule?.Code, StringComparer.Ordinal).ThenBy(item => item.Rule?.Revision)
            .ThenBy(item => item.Mode).ThenBy(item => item.ConsumerId, StringComparer.Ordinal).ToImmutableArray();
        var outcome = proposed.RequestedState == ModerationPolicyRequestedState.Draft
            ? ModerationPolicyCompilationOutcome.Draft
            : diagnostics.Any(item => item.Severity == ModerationPolicyDiagnosticSeverity.ActivationBlocker)
                ? ModerationPolicyCompilationOutcome.ActiveRejected
                : ModerationPolicyCompilationOutcome.RequiresNativePublicationQualification;
        return new(outcome, draft, diagnostics);
    }

    private static bool ValidShape(ModerationPolicyProposedState proposed, ModerationPolicyCompilationContext context) =>
        proposed is not null && context?.Bounds is { } bounds
        && Version7(proposed.PolicyId) && Version7(proposed.PublicationId)
        && proposed.ExpectedRevision is >= 0 and < long.MaxValue
        && proposed.Jurisdiction is not null && Enum.IsDefined(proposed.RequestedState)
        && UniqueEnums(proposed.Modes) && Entries(proposed.Classes) && Entries(proposed.Reasons)
        && Entries(proposed.Profiles) && Unique(proposed.SupportedProfiles, allowEmpty: true)
        && (proposed.RequestedState != ModerationPolicyRequestedState.Active || !proposed.SupportedProfiles.IsEmpty)
        && Version7(context.HostBindingId) && Version7(context.AuthoritySourceId)
        && context.AuthorityRevision > 0 && Text(context.WriterGeneration, 256)
        && !context.WriterGeneration.Any(char.IsControl)
        && bounds.Jurisdiction is not null && UniqueEnums(bounds.ScopeKinds) && UniqueEnums(bounds.SubjectKinds)
        && Unique(bounds.Effects) && bounds.Effects.All(ValidEffect) && UniqueEnums(bounds.TimingKinds)
        && bounds.MaximumFiniteDuration > TimeSpan.Zero && bounds.MaximumFiniteDuration <= TimeSpan.FromDays(36600)
        && bounds.MaximumReviewDelay > TimeSpan.Zero && bounds.MaximumReviewDelay <= TimeSpan.FromDays(36600)
        && bounds.MinimumApprovals is >= 0 and <= 2 && bounds.MaximumApprovals >= bounds.MinimumApprovals
        && bounds.MaximumApprovals <= 2 && bounds.MinimumContestationCalendarMonths is >= 0 and <= 120;

    private static bool ValidEffect(ModerationReferenceEffect effect) =>
        Enum.IsDefined(effect.Kind) && Enum.IsDefined(effect.Mode) && Enum.IsDefined(effect.Target)
        && Enum.IsDefined(effect.Execution)
        && (effect.Capability is null || Enum.IsDefined(effect.Capability.Value)
            && !EffectiveRestrictions.IsProtectedRemedy(effect.Capability.Value));

    private static ModerationConsequenceModes PlanFor(
        ModerationPolicyProfileDefinition definition, ModerationAdditiveStrategyKind? strategy)
    {
        var plan = strategy is { } value ? ModerationReferenceCatalogue.GetStrategy(value)
            : ModerationReferenceCatalogue.GetBundle(definition.Bundle).Resolve(definition.ProtectionPurpose);
        return definition.Recognition == ModerationReferenceRecognition.Never
            ? plan with { Recognition = ModerationRecognitionMode.NoCaptureFromThisControl }
            : plan;
    }

    private static bool ValidTiming(ModerationPolicyTiming proposed, ModerationReferenceTiming reference)
    {
        if (proposed.Kind != reference.Kind) return false;
        return proposed.Kind switch
        {
            ModerationReferenceTimingKind.NoEnforcementInterval
                or ModerationReferenceTimingKind.FixedEventEnd
                or ModerationReferenceTimingKind.CurrentControlUntilQualifiedResolution => proposed.Duration == TimeSpan.Zero,
            _ => proposed.Duration > TimeSpan.Zero && proposed.Duration <= reference.Duration
        };
    }

    private static bool WithinBounds(ModerationPolicyProfileDefinition definition, ModerationPolicyBounds bounds) =>
        definition.ScopeKinds.All(bounds.ScopeKinds.Contains) && definition.SubjectKinds.All(bounds.SubjectKinds.Contains)
        && definition.Effects.All(bounds.Effects.Contains) && bounds.TimingKinds.Contains(definition.Timing.Kind)
        && definition.RequiredApprovals >= bounds.MinimumApprovals && definition.RequiredApprovals <= bounds.MaximumApprovals
        && definition.ContestationCalendarMonths >= bounds.MinimumContestationCalendarMonths
        && (definition.Timing.Kind is ModerationReferenceTimingKind.ReviewedIndefinite
                or ModerationReferenceTimingKind.ObjectReviewWithinDay
            ? definition.Timing.Duration <= bounds.MaximumReviewDelay
            : definition.Timing.Duration <= bounds.MaximumFiniteDuration)
        && (definition.Timing.Kind is not (ModerationReferenceTimingKind.ProtectiveDayWithReview
                or ModerationReferenceTimingKind.RecoveryOrDayReviewBoundary
                or ModerationReferenceTimingKind.EarlierOfDayOrEventEnd)
            || definition.Timing.Duration <= bounds.MaximumReviewDelay);

    private static ImmutableArray<ModerationActivationPrerequisite> Gates(ModerationPolicyCompiledProfile profile)
    {
        var gates = new HashSet<ModerationActivationPrerequisite>
        {
            ModerationActivationPrerequisite.ConstitutionAndReviewers,
            ModerationActivationPrerequisite.MeasuredOperations
        };
        if (profile.Definition.Recognition != ModerationReferenceRecognition.Never)
            gates.Add(ModerationActivationPrerequisite.RecognitionAndPrivacy);
        if (profile.Reference.Prerequisites.Contains(ModerationReferencePrerequisite.NativeSettlementOwnerAndRightsMapping)
            || profile.Reference.Prerequisites.Contains(ModerationReferencePrerequisite.EssentialOperationFallbackSteward))
            gates.Add(ModerationActivationPrerequisite.EventRightsAndPremises);
        if (profile.Definition.Strategies.Any(strategy => strategy is ModerationAdditiveStrategyKind.AgeAudienceEligibility
                or ModerationAdditiveStrategyKind.ClassificationIntegrity))
            gates.Add(ModerationActivationPrerequisite.ChildAndAgePolicy);
        return gates.Order().ToImmutableArray();
    }

    private static ModerationPolicySemanticOwner Owner(ModerationReferenceEffectKind effect) => effect switch
    {
        ModerationReferenceEffectKind.ReasonedNotice => ModerationPolicySemanticOwner.Notice,
        ModerationReferenceEffectKind.NewAndMaterialPublication or ModerationReferenceEffectKind.ImplicatedPublication
            => ModerationPolicySemanticOwner.Publication,
        ModerationReferenceEffectKind.NewUploadAndResourcePublication
            or ModerationReferenceEffectKind.SelectedResourceMetadataPayloadRedirect
            or ModerationReferenceEffectKind.UnsafeResourceVersion
            or ModerationReferenceEffectKind.ImplicatedResourceAndRedirect
            or ModerationReferenceEffectKind.VenueSensitiveResourceReads => ModerationPolicySemanticOwner.Resource,
        ModerationReferenceEffectKind.SelectedEventManagement or ModerationReferenceEffectKind.SelectedTenantAdministration
            or ModerationReferenceEffectKind.VenueSensitiveManagement => ModerationPolicySemanticOwner.Management,
        ModerationReferenceEffectKind.DigitalJoinAndNewOnlineRegistration or ModerationReferenceEffectKind.ProductEnrollment
            => ModerationPolicySemanticOwner.Enrollment,
        ModerationReferenceEffectKind.ParticipantAdmission or ModerationReferenceEffectKind.SelectedPhysicalRegistration
            or ModerationReferenceEffectKind.ProvedEntitlementInvalidation
            or ModerationReferenceEffectKind.ImplicatedRegistration => ModerationPolicySemanticOwner.Participant,
        ModerationReferenceEffectKind.PrivateVenueAndDerivedDisclosure => ModerationPolicySemanticOwner.Disclosure,
        ModerationReferenceEffectKind.RiskyCredentialMutationAndDelegation => ModerationPolicySemanticOwner.CredentialRecovery,
        ModerationReferenceEffectKind.UnsafeFormDefinitionAndSubmission
            or ModerationReferenceEffectKind.CollectedSecretIncidentAccess
            or ModerationReferenceEffectKind.ImplicatedCollection => ModerationPolicySemanticOwner.FormAndIncident,
        ModerationReferenceEffectKind.AudienceDisclosureAndDerivatives
            or ModerationReferenceEffectKind.ActualParticipantAgeEligibility => ModerationPolicySemanticOwner.AgeEligibility,
        ModerationReferenceEffectKind.ClassificationAndMaterialApproval => ModerationPolicySemanticOwner.Classification,
        ModerationReferenceEffectKind.NamedAutomationCreation => ModerationPolicySemanticOwner.Automation,
        _ => throw new ArgumentOutOfRangeException(nameof(effect))
    };

    private static bool Owned(ModerationRuleReference reference, AuthorityScope jurisdiction) =>
        reference is not null && reference.OwnerId == jurisdiction.Id;
    private static bool Text(string value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && value == value.Trim()
        && !value.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t');
    private static bool Version7(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;
    private static bool Entries<T>(ImmutableArray<T> values) =>
        !values.IsDefaultOrEmpty && values.Length <= MaximumDefinitions && values.All(value => value is not null);
    private static bool Unique<T>(ImmutableArray<T> values, bool allowEmpty = false) =>
        !values.IsDefault && (allowEmpty || !values.IsEmpty) && values.Length <= MaximumDefinitions
        && values.All(value => value is not null) && values.Distinct().Count() == values.Length;
    private static bool UniqueEnums<T>(ImmutableArray<T> values, bool allowEmpty = false) where T : struct, Enum =>
        Unique(values, allowEmpty) && values.All(value => Enum.IsDefined(value));
    private static bool SameSet<T>(IEnumerable<T> left, IEnumerable<T> right) =>
        left.ToHashSet().SetEquals(right);
}
