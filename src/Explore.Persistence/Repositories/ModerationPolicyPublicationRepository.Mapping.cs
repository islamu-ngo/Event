using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationPolicyPublicationRepository
{
    private static ModerationPolicyPublication Snapshot(
        ModerationPolicyPublicationRequest request, ModerationPolicyCompiledDraft draft,
        ModerationSourceAuthority authority, string digest, DateTimeOffset now,
        IReadOnlyList<ModerationPolicyPublicationEvidence> evidence, bool definitionOnly = false,
        DateTimeOffset? preparationValidUntil = null)
    {
        var proposed = draft.Proposed;
        var id = proposed.PublicationId;
        return new()
        {
            Id = id, PolicyId = proposed.PolicyId, Revision = draft.Publication.ProposedRevision,
            AdoptedRevision = definitionOnly ? null : draft.Publication.ProposedRevision,
            PreviousPublicationId = request.ExpectedPublicationId,
            HostBindingId = authority.Binding.Id, TrustDomainId = authority.Binding.TrustDomainId,
            SolutionId = authority.Binding.SolutionId, InstanceId = authority.Binding.InstanceId,
            JurisdictionTenantId = proposed.Jurisdiction.Kind == ISLAMU.Moderation.Domain.ModerationScopeKind.Tenant
                ? proposed.Jurisdiction.Id : null, TenantId = authority.Tenant.Id,
            PrincipalId = authority.UserId, OperationId = request.OperationId, RequestDigest = digest,
            DeclaredStateDigest = ModerationPolicyPublicationProjection.ProposedDigest(proposed),
            AuthoritySourceId = authority.Epoch.Id, AuthorityRevision = authority.Epoch.Revision,
            QualificationRevision = checked(authority.Epoch.Revision + 1),
            WriterGeneration = ModerationPolicyPublicationProjection.WriterGeneration,
            CatalogueRevision = proposed.CatalogueRevision, InventoryRevision = proposed.InventoryRevision,
            InventoryFingerprint = proposed.InventoryFingerprint,
            PublishedActive = proposed.RequestedState == ModerationPolicyRequestedState.Active,
            PublishedAt = now, EvidenceValidUntil = evidence.Count == 0 ? null
                : preparationValidUntil is { } bound
                    ? new[] { bound, evidence.Min(row => row.ValidUntil) }.Min()
                    : evidence.Min(row => row.ValidUntil),
            MaximumFiniteDurationTicks = draft.Bounds.MaximumFiniteDuration.Ticks,
            MaximumReviewDelayTicks = draft.Bounds.MaximumReviewDelay.Ticks,
            MinimumApprovals = draft.Bounds.MinimumApprovals, MaximumApprovals = draft.Bounds.MaximumApprovals,
            MinimumContestationCalendarMonths = draft.Bounds.MinimumContestationCalendarMonths,
            Classes = proposed.Classes.Select(row => new ModerationPolicyPublicationClass
            {
                PublicationId = id, Code = row.Reference.Code, Revision = row.Reference.Revision,
                Meaning = row.Meaning, Ground = row.Ground, Basis = row.Basis
            }).ToArray(),
            Reasons = proposed.Reasons.Select(row => new ModerationPolicyPublicationReason
            {
                PublicationId = id, Code = row.Reference.Code, Revision = row.Reference.Revision,
                Meaning = row.Meaning, ClassCode = row.Class.Code, ClassRevision = row.Class.Revision
            }).ToArray(),
            Modes = proposed.Modes.Select(mode => new ModerationPolicyPublicationMode { PublicationId = id, Mode = (int)mode }).ToArray(),
            Evidence = evidence,
            Profiles = draft.Profiles.Select(compiled =>
            {
                var profile = compiled.Definition;
                var strategies = new[] { 0 }.Concat(profile.Strategies.Order().Select(strategy => (int)strategy)).ToArray();
                return new ModerationPolicyPublicationProfile
                {
                    PublicationId = id, ProfileId = profile.Id,
                    RuleCode = profile.Rule.Code, RuleRevision = profile.Rule.Revision,
                    ReasonCode = profile.Reason.Code, ReasonRevision = profile.Reason.Revision,
                    ReferenceProfile = profile.ReferenceProfile, ReferenceRevision = profile.ReferenceRevision,
                    Grade = profile.Grade, Bundle = profile.Bundle, ProtectionPurpose = profile.ProtectionPurpose,
                    TimingKind = profile.Timing.Kind, DurationTicks = profile.Timing.Duration.Ticks,
                    RequiredApprovals = profile.RequiredApprovals, ContestationCalendarMonths = profile.ContestationCalendarMonths,
                    Recognition = profile.Recognition, Supported = proposed.SupportedProfiles.Contains(profile.Rule),
                    Scopes = profile.ScopeKinds.Select(kind => new ModerationPolicyPublicationScope
                        { PublicationId = id, ProfileId = profile.Id, Kind = kind }).ToArray(),
                    Subjects = profile.SubjectKinds.Select(kind => new ModerationPolicyPublicationSubject
                        { PublicationId = id, ProfileId = profile.Id, Kind = kind }).ToArray(),
                    Effects = profile.Effects.Select((effect, ordinal) => new ModerationPolicyPublicationEffect
                    {
                        PublicationId = id, ProfileId = profile.Id, Ordinal = ordinal,
                        Kind = effect.Kind, Mode = effect.Mode, Target = effect.Target,
                        Capability = effect.Capability, Execution = effect.Execution,
                        SemanticOwner = (int?)draft.Effects.FirstOrDefault(row => row.Rule == profile.Rule && row.Effect == effect)?.Owner ?? 0
                    }).ToArray(),
                    Plans = compiled.Consequences.Select((plan, ordinal) => new ModerationPolicyPublicationPlan
                    {
                        PublicationId = id, ProfileId = profile.Id, Strategy = strategies[ordinal],
                        Content = plan.Content, Management = plan.Management, Resources = plan.Resources,
                        NewAdmission = plan.NewAdmission, ExistingAdmission = plan.ExistingAdmission,
                        PrivateDisclosure = plan.PrivateDisclosure, Settlement = plan.Settlement,
                        Capacity = plan.Capacity, Credentials = plan.Credentials, Notifications = plan.Notifications,
                        Recognition = plan.Recognition, PendingWork = plan.PendingWork, Restoration = plan.Restoration
                    }).ToArray(),
                    Consumers = draft.Consumers.Where(requirement => requirement.Binding.Rule == profile.Rule)
                        .SelectMany(requirement => requirement.Consumers.Select(contract => new ModerationPolicyPublicationConsumer
                        {
                            PublicationId = id, ProfileId = profile.Id, Mode = (int)requirement.Binding.Mode,
                            Capability = requirement.Binding.Capability, ConsumerId = contract.Id,
                            SubjectResolver = contract.SubjectResolver, TargetResolver = contract.TargetResolver,
                            AuthorizationOwner = contract.AuthorizationOwner, EntryCheckpoint = contract.EntryCheckpoint,
                            TerminalCheckpoint = contract.TerminalCheckpoint, WriterFence = contract.WriterFence,
                            FreshnessClass = (int)contract.FreshnessClass, OwningPacket = contract.OwningPacket,
                            ConformanceTest = contract.ConformanceTest
                        })).ToArray(),
                    Prerequisites = draft.Prerequisites.Where(requirement => requirement.Rule == profile.Rule)
                        .SelectMany(requirement => requirement.ActivationGates.Select((gate, ordinal) => new ModerationPolicyPublicationPrerequisite
                        {
                            PublicationId = id, ProfileId = profile.Id, Mode = (int)requirement.Mode,
                            Ordinal = ordinal, ActivationGate = (int)gate, NativePrerequisite = null
                        }).Concat(requirement.NativePrerequisites.Select((prerequisite, ordinal) => new ModerationPolicyPublicationPrerequisite
                        {
                            PublicationId = id, ProfileId = profile.Id, Mode = (int)requirement.Mode,
                            Ordinal = requirement.ActivationGates.Length + ordinal, ActivationGate = null,
                            NativePrerequisite = prerequisite
                        }))).ToArray()
                };
            }).ToArray()
        };
    }
}
