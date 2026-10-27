using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Authentication;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.RecognitionRecovery;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/privacy-erasure/recognition-corrections")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting("public_transactional")]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class ModerationRecognitionRecoveryCorrectionController(
    IQueryHandler<GetRecognitionCorrectionProofsQuery, RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionCorrectionProofDto>>> read,
    IResourceAssembler<RecognitionCorrectionProofDto, RecognitionCorrectionProofDto> proofs) : ControllerBase
{
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("receipt", "Invalid correction receipt", "Correction receipt authentication is required."))
        .AuthenticationRequired(FailureCodes.AuthenticationRequired);

    /// <summary>Reads only sanitized protected correction facts using the original receipt's dedicated purpose.</summary>
    [AllowAnonymous]
    [HttpGet("", Name = RouteNames.GetRecognitionCorrectionProofs)]
    [ProducesResponseType<HalCollectionResource<RecognitionCorrectionProofDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HalCollectionResource<RecognitionCorrectionProofDto>>> Read(CancellationToken cancellationToken)
    {
        // GET stays AllowAnonymous by repository convention; the explicit scheme invocation is mandatory.
        var authentication = await HttpContext.AuthenticateAsync(
            ModerationRecognitionCorrectionReceiptAuthenticationHandler.SchemeName);
        string? claim = authentication.Principal?.FindFirst(
            ModerationRecognitionCorrectionReceiptAuthenticationHandler.IntentIdClaim)?.Value;
        if (!authentication.Succeeded || !Guid.TryParse(claim, out var intentId))
            return Failures.Map(this, BaseCommandResponse.Authentication<Guid>());
        string authorization = Request.Headers.Authorization.ToString();
        var result = await read.QueryAsync(new(intentId,
            authorization[ModerationRecognitionCorrectionReceiptAuthenticationHandler.CredentialPrefix.Length..]),
            cancellationToken);
        if (!result.IsSuccess) return Failures.Map(this, result);
        return Ok(await proofs.ToCollectionResource(result.Data!, RouteNames.GetRecognitionCorrectionProofs, HttpContext));
    }
}
