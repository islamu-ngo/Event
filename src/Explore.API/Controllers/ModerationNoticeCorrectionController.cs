using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Notices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/moderation/notice-corrections")]
[EndpointClassification(EndpointClass.Authenticated)]
[PrivateNoStore]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("authenticated")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
public sealed class ModerationNoticeCorrectionController(
    IQueryHandler<ReadModerationNoticeCorrectionQuery, ModerationNoticeCorrectionReadResult> read) : ControllerBase
{
    /// <summary>Discloses approved correction text only to the currently qualified original recipient.</summary>
    [AllowAnonymous]
    [HttpGet("{originalNoticeId:guid}", Name = RouteNames.GetModerationNoticeCorrection)]
    [ProducesResponseType<ModerationCorrectionExplanation>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Read(Guid originalNoticeId, [FromQuery] Guid pendingEffectId,
        [FromQuery] string expectedPendingSnapshotHash, [FromQuery] string expectedPendingEffectBindingHash,
        [FromQuery] Guid approvalId, [FromQuery] Guid grantId, [FromQuery] Guid caseId,
        [FromQuery] long expectedCaseRevision, CancellationToken cancellationToken) =>
        new ModerationNoticeCorrectionHttpResult(await read.QueryAsync(
            new()
            {
                OriginalNoticeId = originalNoticeId,
                PendingEffectId = pendingEffectId,
                ExpectedPendingSnapshotHash = expectedPendingSnapshotHash,
                ExpectedPendingEffectBindingHash = expectedPendingEffectBindingHash,
                ApprovalId = approvalId,
                GrantId = grantId,
                CaseId = caseId,
                ExpectedCaseRevision = expectedCaseRevision
            }, cancellationToken));
}
