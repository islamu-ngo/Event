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
        if (sources.TenantId != operation.TenantId)
            throw new UnauthorizedAccessException("The learning plan requires its original tenant sources.");
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));
        bool sourceAvailable = operation.Space is null
            || operation.Space is { IsRemoved: false, PackKey: LearningLessonCatalog.PackKey,
                PackVersion: LearningLessonCatalog.MaterialVersion };
        bool available = sourceAvailable && (family switch
        {
            WireFamily.Shell => true,
            WireFamily.Studio => operation.StudioAvailable,
            WireFamily.EventManagement => operation.CanManageEvent,
            WireFamily.NormalAi => operation.NormalAiAvailable,
            _ => false
        });
        LearningTourStepDto[] steps = available
            ? LearningExperienceCatalog.GetSteps((DomainFamily)family)
                .Where(step => step.StepKey != "shell.studio" || operation.StudioAvailable).ToArray()
            : [];
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
            CanClaim = available && !(operation.State?.ActiveClaimId is not null
                && operation.State.ClaimExpiresAtUtc > operation.ObservedAtUtc),
            Steps = steps,
            UnavailableReason = available ? null
                : new("Learning.Availability.context-unavailable",
                    "Guidance is unavailable in the current workspace context.")
        };
        return (plan, available ? new LearningTourDefinition((DomainFamily)family,
            LearningExperienceCatalog.MaterialVersion,
            steps.Select(step => new LearningTourStep(step.StepKey, step.StepVersion)).ToArray(),
            LearningExperienceCatalog.GetDefinition((DomainFamily)family).Steps) : null);
    }
}
