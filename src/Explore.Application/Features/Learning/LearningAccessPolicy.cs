using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Learning;
using FluentValidation.Results;
using ISLAMU.Wire.Contracts.Learning;
using LearningGuidanceAudience = Explore.Domain.Learning.LearningGuidanceAudience;
using LearningRequirementPolicy = Explore.Domain.Learning.LearningRequirementPolicy;

namespace Explore.Application.Features.Learning;

/// <summary>
/// Projects learning eligibility from the current internal human's fenced facts.
/// Eligibility is independent of publishing, event creation and administrator roles.
/// </summary>
public static class LearningAccessPolicy
{
    /// <summary>Practice has no tour family; only global policy and the original member's facts apply.</summary>
    public static bool CanPractice(LearningGuidancePolicy policy, bool isActiveMember, bool isTenantAdministrator) =>
        policy.Enabled && isActiveMember
        && (policy.ManualAccessAudience == LearningGuidanceAudience.ActiveMembers || isTenantAdministrator);

    /// <summary>
    /// Applies policy to an already acquired original-human intent, including retained retries.
    /// Owner cleanup and acknowledgement do not depend on interactive guidance eligibility.
    /// </summary>
    public static void RequireInteractiveAccess(
        LearningMutationIntent intent, bool isTenantAdministrator, LearningSourceFacts sources)
    {
        if (intent is LearningPreferencesIntent or LearningSpaceIntent or LearningRequirementAcknowledgementIntent
            or LearningPracticeIntent { Action: Explore.Domain.Learning.LearningPracticeAction.Clear }
            or LearningTourOutcomeIntent { ClaimToken: not null, Action:
                Explore.Domain.Learning.LearningTourAction.Skip or Explore.Domain.Learning.LearningTourAction.Interrupt
                or Explore.Domain.Learning.LearningTourAction.Snooze })
            return;

        LearningGuidancePolicy policy = LearningGuidancePolicyCodec.Decode(sources.GuidanceDocument);
        // Active membership is a prerequisite of the acquired operation, not inferred from policy.
        bool allowed = intent switch
        {
            LearningTourClaimIntent { Replay: true } claim =>
                policy.Evaluate(claim.Family, true, isTenantAdministrator).CanAccessManually,
            LearningTourClaimIntent claim =>
                policy.Evaluate(claim.Family, true, isTenantAdministrator) is (true, _) or (_, true),
            LearningTourOutcomeIntent outcome =>
                policy.Evaluate(outcome.Family, true, isTenantAdministrator) is (true, _) or (_, true),
            LearningPracticeIntent => CanPractice(policy, true, isTenantAdministrator),
            _ => throw new InvalidOperationException("The learning intent is not registered.")
        };
        if (!allowed)
            throw new AuthorizationException("The current guidance policy does not allow this interactive learning action.");
        string? expectedPolicyStamp = intent switch
        {
            LearningTourClaimIntent claim => claim.ExpectedPolicySourceStamp,
            LearningTourOutcomeIntent outcome => outcome.ExpectedPolicySourceStamp,
            _ => null
        };
        if (expectedPolicyStamp is not null
            && !string.Equals(expectedPolicyStamp, sources.PolicySourceStamp, StringComparison.Ordinal))
            throw new ConcurrencyConflictException("learning_source_changed",
                "The guidance policy sources changed. Refresh the private plan.");
    }

    public static void RequireAcknowledgement(
        LearningRequirementAcknowledgementIntent intent,
        bool isTenantAdministrator,
        LearningSourceFacts sources,
        LearningGuidancePolicy policy)
    {
        if (!isTenantAdministrator)
            throw new AuthorizationException("Current tenant administrator authority is required.");
        if (policy.RequirementPolicy != LearningRequirementPolicy.TenantAdminOrientationAcknowledgement)
            throw new ValidationException(new ValidationResult(
                [new ValidationFailure(nameof(policy.RequirementPolicy), "Administrator orientation acknowledgement is not required.")]));
        LearningRequirementDefinition definition = LearningRequirementDefinition.Current;
        if (intent.RequirementKey != definition.RequirementKey || intent.MaterialVersion != definition.MaterialVersion)
            throw new ValidationException(new ValidationResult(
                [new ValidationFailure(nameof(intent.MaterialVersion), "Only the current authored requirement may be acknowledged.")]));
        if (!string.Equals(intent.ExpectedPolicySourceStamp, sources.PolicySourceStamp, StringComparison.Ordinal))
            throw new ConcurrencyConflictException("learning_source_changed",
                "The requirement policy sources changed. Refresh the private context.");
    }

    public static LearningRequirementDto Requirement(
        LearningOperationState operation, LearningGuidancePolicy policy)
    {
        LearningSourceFacts sources = operation.Sources
            ?? throw new InvalidOperationException("Requirement projection requires its retained source image.");
        if (sources.TenantId != operation.TenantId
            || operation.State is { } owner && (owner.TenantId != operation.TenantId || owner.UserId != operation.UserId))
            throw new UnauthorizedAccessException("Requirement evidence must belong to the original private scope.");
        LearningRequirementDefinition definition = LearningRequirementDefinition.Current;
        bool applicable = operation.CanManageSpace
            && policy.RequirementPolicy == LearningRequirementPolicy.TenantAdminOrientationAcknowledgement;
        LearningRequirementAcknowledgement? acknowledgement = applicable
            ? operation.State?.RequirementAcknowledgements.SingleOrDefault(value =>
                value.RequirementKey == definition.RequirementKey && value.MaterialVersion == definition.MaterialVersion)
            : null;
        return new()
        {
            RequirementKey = definition.RequirementKey,
            MaterialVersion = definition.MaterialVersion,
            PolicySourceStamp = sources.PolicySourceStamp,
            Status = !applicable ? LearningRequirementStatus.NotApplicable
                : acknowledgement is null ? LearningRequirementStatus.Pending : LearningRequirementStatus.Satisfied,
            AcknowledgedAtUtc = acknowledgement is null ? null
                : new DateTimeOffset(DateTime.SpecifyKind(acknowledgement.AcknowledgedAtUtc, DateTimeKind.Utc))
        };
    }

    public static bool IsActiveLearner(HumanPrincipalSourceFacts facts, Guid tenantId)
    {
        if (tenantId == Guid.Empty || facts.PrincipalId == Guid.Empty
            || facts.SelectedTenantId != tenantId || facts.Users.Length != 1
            || facts.Users[0].Id != facts.PrincipalId || facts.Users[0].IsDeleted
            || facts.Tenants.Count(value => value.Id == tenantId && value.IsActiveState) != 1)
            return false;

        var memberships = facts.TenantMemberships
            .Where(value => value.TenantId == tenantId && !value.IsDeleted).ToArray();
        return memberships.Length == 1
            && memberships[0].StatusId == (int)TenantUserStatusEnum.Active;
    }

    public static bool IsActorMember(HumanPrincipalSourceFacts facts, Actor actor)
    {
        if (facts.SelectedTenantId is not Guid tenantId
            || !IsActiveLearner(facts, tenantId) || actor.IsSuspended)
            return false;

        return (ActorTypeEnum)actor.ActorTypeId switch
        {
            ActorTypeEnum.User => actor.UserId == facts.PrincipalId,
            ActorTypeEnum.Organization when actor.OrganizationId is Guid organizationId =>
                facts.Organizations.Any(value => value.OwnerId == organizationId && Eligible(value, tenantId)),
            ActorTypeEnum.Group when actor.GroupId is Guid groupId =>
                facts.Groups.Any(value => value.OwnerId == groupId && Eligible(value, tenantId)),
            _ => false
        };
    }

    public static string ScopeStamp(Guid tenantId, Guid userId) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"learning-scope:v1:{tenantId:N}:{userId:N}")));

    private static bool Eligible(HumanPrincipalOwnerMembershipFact membership, Guid tenantId) =>
        membership.TenantId == tenantId && !membership.IsDeleted
        && !membership.PlacementIsDeleted && !membership.PlacementIsSuspended && !membership.OwnerIsDeleted
        && membership.ApprovalStatusId == (int)ApprovalStatusEnum.Approved;
}
