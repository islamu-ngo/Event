namespace Explore.API.Controllers;

using System.Globalization;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.DTOs.LegalDocuments;
using Explore.Application.Features.LegalDocuments.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

[ApiVersion("0.1")]
[Route("api/legal-documents")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public sealed class LegalDocumentsController(IMediator mediator) : ControllerBase
{
    [HttpGet("{kindCode}", Name = RouteNames.GetPublicLegalDocument)]
    [AllowAnonymous]
    [EndpointSummary("Get a published legal document")]
    [EndpointDescription(
        "Returns the current role-labeled immutable public legal document in the negotiated locale.")]
    [ProducesResponseType(typeof(PublicLegalDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [OutputCache(PolicyName = "PublicLegalDocuments")]
    public async Task<ActionResult<PublicLegalDocumentDto>> Get(
        string kindCode,
        CancellationToken cancellationToken = default)
    {
        string languageTag = CultureInfo.CurrentUICulture.Name;
        if (string.IsNullOrWhiteSpace(languageTag))
            languageTag = "en";
        PublicLegalDocumentQueryResult result = await mediator.Send(
            new GetPublicLegalDocumentQuery(
                kindCode,
                languageTag),
            cancellationToken);
        if (result.Document is not null)
            return Ok(result.Document);

        Response.Headers.CacheControl = "no-store";
        return Problem(
            statusCode: result.IsNotFound
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status503ServiceUnavailable,
            title: result.IsNotFound
                ? "The legal document is not published."
                : "The legal document is unavailable.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = result.FailureCode
            });
    }
}
