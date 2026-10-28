using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Hateoas;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.API.Hateoas.Policies;

/// <summary>Context actions use only the original human's server-evaluated learning capabilities.</summary>
public sealed class LearningContextLinkPolicy : ILinkPolicy<LearningContextDto>
{
    public IEnumerable<LinkDefinition> GetLinks(LearningContextDto dto, ClaimsPrincipal? user)
    {
        yield return new(LinkRelations.Self, RouteNames.GetLearningContext,
            new { contextKey = dto.ContextKey, eventContextId = dto.EventContextId }, "GET", RequiresAuth: true);
        if (!dto.Capabilities.CanLearn)
            yield break;

        yield return new("preferences", RouteNames.UpdateLearningPreferences, null, "PATCH", RequiresAuth: true);
        if (dto.Capabilities.CanClaimTours && dto.Tours.Any(tour => tour.IsAvailable))
        {
            yield return new("claim-tour", RouteNames.ClaimLearningTour, null, "POST", RequiresAuth: true);
            yield return new("record-tour-outcome", RouteNames.RecordLearningTourOutcome, null, "POST", RequiresAuth: true);
        }
        else if (dto.Claim is not null)
        {
            yield return new("record-tour-outcome", RouteNames.RecordLearningTourOutcome, null, "POST", RequiresAuth: true);
        }

        if (dto.Capabilities.CanSimulate)
            yield return new("start-simulation", RouteNames.ApplyLearningPracticeAction, null, "POST", RequiresAuth: true);
        if (dto.Capabilities.CanClearPractice)
            yield return new("clear-practice", RouteNames.ApplyLearningPracticeAction, null, "POST", RequiresAuth: true);
        if (dto.Capabilities.CanManageSpace)
        {
            if (dto.SpaceStatus is LearningSpaceStatus.Active or LearningSpaceStatus.Absent)
                yield return new("remove-learning-space", RouteNames.ManageTenantLearningSpace, null, "POST", RequiresAuth: true);
            else if (dto.SpaceStatus == LearningSpaceStatus.Removed)
                yield return new("recreate-learning-space", RouteNames.ManageTenantLearningSpace, null, "POST", RequiresAuth: true);
        }
    }
}

/// <summary>Detail actions cannot inherit another context's or recipe's simulation eligibility.</summary>
public sealed class LearningPracticeLinkPolicy : ILinkPolicy<LearningPracticeDto>
{
    public IEnumerable<LinkDefinition> GetLinks(LearningPracticeDto dto, ClaimsPrincipal? user)
    {
        yield return new(LinkRelations.Self, RouteNames.GetLearningPractice,
            new { id = dto.Id }, "GET", RequiresAuth: true);
        yield return new("learning-context", RouteNames.GetLearningContext,
            new { contextKey = dto.ContextKey }, "GET", RequiresAuth: true);
        if (dto.CanSimulate)
        {
            if (dto.Simulation.Status == LearningSimulationStatus.Choosing)
                yield return new("simulate-choice", RouteNames.ApplyLearningPracticeAction, null, "POST", RequiresAuth: true);
            if (dto.Simulation.Status == LearningSimulationStatus.AwaitingConfirmation)
                yield return new("simulate-confirm", RouteNames.ApplyLearningPracticeAction, null, "POST", RequiresAuth: true);
            yield return new("reset-simulation", RouteNames.ApplyLearningPracticeAction, null, "POST", RequiresAuth: true);
        }
        if (dto.CanClearPractice)
            yield return new("clear-practice", RouteNames.ApplyLearningPracticeAction, null, "POST", RequiresAuth: true);
    }
}

/// <summary>A settled receipt offers navigation, not a new reusable mutation authorization.</summary>
public sealed class LearningMutationReceiptLinkPolicy : ILinkPolicy<LearningMutationReceiptDto>
{
    public IEnumerable<LinkDefinition> GetLinks(LearningMutationReceiptDto dto, ClaimsPrincipal? user)
    {
        yield return new("learning-context", RouteNames.GetLearningContext,
            new { contextKey = dto.State?.ContextKey ?? dto.Practice?.ContextKey ?? "self",
                eventContextId = dto.State?.EventContextId }, "GET", RequiresAuth: true);
        if (dto.Practice is { } practice)
            yield return new("practice", RouteNames.GetLearningPractice,
                new { id = practice.Id }, "GET", RequiresAuth: true);
    }
}

public sealed class LearningContextCollectionLinkPolicy : ICollectionLinkPolicy<LearningContextDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(LearningContextDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

public sealed class LearningPracticeCollectionLinkPolicy : ICollectionLinkPolicy<LearningPracticeDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(LearningPracticeDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}

public sealed class LearningMutationReceiptCollectionLinkPolicy : ICollectionLinkPolicy<LearningMutationReceiptDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(LearningMutationReceiptDto dto, ClaimsPrincipal? user) => [];
    public IEnumerable<LinkDefinition> GetCollectionLinks(ClaimsPrincipal? user) => [];
}
