using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Learning;
using ISLAMU.Wire.Contracts.Learning;
using DomainFamily = Explore.Domain.Learning.LearningTourFamily;
using WireFamily = ISLAMU.Wire.Contracts.Learning.LearningTourFamily;

namespace Explore.Application.Features.Learning;

/// <summary>
/// Projects authored semantics from an already authorized operation and retained source image.
/// This resolver creates no state and issues no authority or source lease.
/// </summary>
public static class LearningTourPlanResolver
{
    public static (LearningTourPlanDto Plan, LearningTourDefinition? Definition) Resolve(
        LearningOperationState operation, LearningSourceFacts sources, WireFamily family)
    {
        if (operation.Sources is not { } acquired || !ReferenceEquals(acquired, sources)
            || sources.TenantId != operation.TenantId)
            throw new UnauthorizedAccessException("The learning plan requires its original tenant sources.");
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));
        LearningGuidancePolicy policy = LearningGuidancePolicyCodec.Decode(acquired.GuidanceDocument);
        var eligibility = policy.Evaluate((DomainFamily)family, true, operation.CanManageSpace);
        bool sourceAvailable = operation.Space is null
            || operation.Space is
            {
                IsRemoved: false, PackKey: LearningLessonCatalog.PackKey,
                PackVersion: LearningLessonCatalog.MaterialVersion
            };
        bool contextAvailable = sourceAvailable && (family switch
        {
            WireFamily.Shell => true,
            WireFamily.Studio => operation.StudioAvailable,
            WireFamily.EventManagement => operation.CanManageEvent,
            WireFamily.NormalAi => operation.NormalAiAvailable,
            _ => false
        });
        LearningTourStepDto[] steps = contextAvailable
            ? LearningExperienceCatalog.GetSteps((DomainFamily)family)
                .Where(step => step.StepKey != "shell.studio" || operation.StudioAvailable).ToArray()
            : [];
        var definition = contextAvailable ? new LearningTourDefinition((DomainFamily)family,
            LearningExperienceCatalog.MaterialVersion,
            steps.Select(step => new LearningTourStep(step.StepKey, step.StepVersion)).ToArray(),
            LearningExperienceCatalog.GetDefinition((DomainFamily)family).Steps) : null;
        bool available = contextAvailable && (eligibility.CanInvite || eligibility.CanAccessManually);
        bool hasLiveClaim = operation.State?.ActiveClaimId is not null
            && (operation.ObservedAtUtc == default || operation.State.ClaimExpiresAtUtc > operation.ObservedAtUtc);
        LearningTourProgress? progress = operation.State?.Tours.SingleOrDefault(tour =>
            tour.Family == (DomainFamily)family && tour.MaterialVersion == LearningExperienceCatalog.MaterialVersion);
        bool canInvite = contextAvailable && eligibility.CanInvite && !hasLiveClaim
            && operation.State?.SuppressAutomaticTours != true && operation.State?.IsHidden != true
            && !(progress?.SnoozedUntilUtc > operation.ObservedAtUtc)
            && progress?.Outcome != Explore.Domain.Learning.LearningTourOutcome.Skipped
            && (definition is null || operation.State?.HasTourCoverage(definition) != true);
        string fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Family = family,
            LearningExperienceCatalog.MaterialVersion,
            operation.ContextKey,
            operation.EventContextId,
            sources.WorkspaceSourceStamp,
            sources.PolicySourceStamp,
            Available = available,
            Steps = steps.Select(step => new { step.StepKey, step.StepVersion }).ToArray()
        })));
        var plan = new LearningTourPlanDto
        {
            Family = family,
            MaterialVersion = LearningExperienceCatalog.MaterialVersion,
            ScopeStamp = LearningAccessPolicy.ScopeStamp(operation.TenantId, operation.UserId),
            PolicySourceStamp = sources.PolicySourceStamp,
            PlanFingerprint = fingerprint,
            LearnerRevision = operation.State?.Revision ?? 0,
            ContextKey = operation.ContextKey,
            EventContextId = operation.EventContextId,
            IsAvailable = available,
            CanClaim = available && !hasLiveClaim,
            CanInvite = canInvite,
            Steps = available ? steps : [],
            UnavailableReason = available ? null
                : !sourceAvailable ? new("Learning.Availability.source-unavailable",
                    "The shared learning source is removed or its material is unavailable.")
                : !contextAvailable && family == WireFamily.NormalAi
                    ? new("Learning.Availability.normal-ai-unavailable", "Normal AI is unavailable in the current context.")
                : !contextAvailable ? new("Learning.Availability.context-unavailable",
                    "Guidance is unavailable in the current workspace context.")
                : !policy.Enabled ? new("Learning.Availability.policy-disabled",
                    "Interactive guidance is disabled by the tenant policy.")
                : policy.FamilyOverrides.Any(value => value.Family == (DomainFamily)family && !value.Enabled)
                    ? new("Learning.Availability.family-disabled", "This tour is disabled by the tenant policy.")
                : new("Learning.Availability.audience-unavailable",
                    "The tenant policy does not allow tour access for the current member.")
        };
        return (plan, available ? definition : null);
    }
}
