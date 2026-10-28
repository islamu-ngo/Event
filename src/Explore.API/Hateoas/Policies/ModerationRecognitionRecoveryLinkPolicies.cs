using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Moderation.RecognitionRecovery;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Policies;

public sealed class RecognitionRecoveryEscalationDetailLinkPolicy(IUserContext currentUser)
    : ILinkPolicy<RecognitionRecoveryEscalationDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RecognitionRecoveryEscalationDto dto, ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true || !currentUser.IsAuthenticated
            || currentUser.UserId != dto.QualifiedOwnerHumanId) yield break;
        yield return new(LinkRelations.Self, RouteNames.GetRecognitionRecoveryEscalation, new { id = dto.Id }, RequiresAuth: true);
        yield return new("recovery-context", RouteNames.GetRecognitionRecoveryContext, new { id = dto.Id }, RequiresAuth: true);
        if (dto.DeliveredAt is not null && dto.AcknowledgedAt is null)
            yield return new("acknowledge", RouteNames.AcknowledgeRecognitionRecovery, new { id = dto.Id }, "POST", RequiresAuth: true);
    }
}

public sealed class RecognitionRecoveryEscalationCollectionLinkPolicy(IUserContext currentUser)
    : ICollectionLinkPolicy<RecognitionRecoveryEscalationDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(RecognitionRecoveryEscalationDto dto, ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true || !currentUser.IsAuthenticated
            || currentUser.UserId != dto.QualifiedOwnerHumanId) yield break;
        yield return new(LinkRelations.Self, RouteNames.GetRecognitionRecoveryEscalation, new { id = dto.Id }, RequiresAuth: true);
        yield return new("recovery-context", RouteNames.GetRecognitionRecoveryContext, new { id = dto.Id }, RequiresAuth: true);
    }
}

public sealed class RecognitionRecoveryContextDetailLinkPolicy(IUserContext currentUser)
    : ILinkPolicy<RecognitionRecoveryContextDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RecognitionRecoveryContextDto dto, ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true || !currentUser.IsAuthenticated
            || currentUser.UserId != dto.QualifiedOwnerHumanId) yield break;
        yield return new(LinkRelations.Self, RouteNames.GetRecognitionRecoveryContext, new { id = dto.EscalationId }, RequiresAuth: true);
        if (dto.CanAssess)
            yield return new("assess", RouteNames.AssessRecognitionRecovery,
                new { intentId = dto.IntentId, sourceId = dto.SourceId }, "POST", RequiresAuth: true);
        if (dto.CanAuthorize && dto.DispositionId is { } id)
            yield return new("authorize", RouteNames.AuthorizeRecognitionRecovery, new { id }, "POST", RequiresAuth: true);
    }
}

public sealed class RecognitionRecoveryContextCollectionLinkPolicy(IUserContext currentUser)
    : ICollectionLinkPolicy<RecognitionRecoveryContextDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(RecognitionRecoveryContextDto dto, ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true || !currentUser.IsAuthenticated
            || currentUser.UserId != dto.QualifiedOwnerHumanId) yield break;
        yield return new(LinkRelations.Self, RouteNames.GetRecognitionRecoveryContext, new { id = dto.EscalationId }, RequiresAuth: true);
    }
}

public sealed class RecognitionCorrectionProofDetailLinkPolicy : ILinkPolicy<RecognitionCorrectionProofDto>
{
    public IEnumerable<LinkDefinition> GetLinks(RecognitionCorrectionProofDto dto, ClaimsPrincipal? user)
    {
        yield return new(LinkRelations.Collection, RouteNames.GetRecognitionCorrectionProofs);
    }
}

public sealed class RecognitionCorrectionProofCollectionLinkPolicy : ICollectionLinkPolicy<RecognitionCorrectionProofDto>
{
    public IEnumerable<LinkDefinition> GetItemLinks(RecognitionCorrectionProofDto dto, ClaimsPrincipal? user)
    {
        yield return new(LinkRelations.Collection, RouteNames.GetRecognitionCorrectionProofs);
    }
}
