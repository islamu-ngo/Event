using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.Features.Localization.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public class TranslationController(IMediator mediator) : ControllerBase
{

    /// <summary>
    /// Get all translations for a language.
    /// </summary>
    [HttpGet("{languageCode}", Name = RouteNames.GetTranslationByLanguage)]
    [AllowAnonymous]
    [EndpointSummary("Get Translations")]
    [EndpointDescription("Returns all translation key-value pairs for the specified language code.")]
    [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Dictionary<string, string>>> GetTranslations(
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        var translations = await mediator.Send(
            new GetTranslationsQuery { LanguageCode = languageCode },
            cancellationToken);
        return Ok(translations);
    }

    /// <summary>
    /// Get available translation languages.
    /// </summary>
    [HttpGet("languages", Name = RouteNames.GetAvailableTranslationLanguages)]
    [AllowAnonymous]
    [EndpointSummary("Get Available Languages")]
    [EndpointDescription("Returns the list of language codes that have translations available.")]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<string>>> GetLanguages(CancellationToken cancellationToken = default)
    {
        var languages = await mediator.Send(new GetAvailableLanguagesQuery(), cancellationToken);
        return Ok(languages);
    }
}
