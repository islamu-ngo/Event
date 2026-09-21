using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Constants;
using Explore.Application.DTOs.Onboarding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Api.IntegrationTests.Features.Hateoas;

[NotInParallel]
public sealed class OperatorIdentityFormOptionsLinkPolicyTests
{
    [Test]
    public async Task Missing_authentication_omits_all_metadata_affordances()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var assembler = scope.ServiceProvider.GetRequiredService<
            IResourceAssembler<OperatorIdentityFormOptionsDto, OperatorIdentityFormOptionsDto>>();
        var resource = await assembler.ToResource(new OperatorIdentityFormOptionsDto(), context);
        await Assert.That(resource.Links).IsEmpty();
    }

    [Test]
    public async Task Unresolved_routes_omit_links_instead_of_guessing_urls()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        using var emptyRoutes = new ServiceCollection().AddLogging().AddRouting().BuildServiceProvider();
        var generator = new HateoasLinkGenerator(emptyRoutes.GetRequiredService<LinkGenerator>(),
            NullLogger<HateoasLinkGenerator>.Instance);
        var assembler = new HalResourceAssembler<OperatorIdentityFormOptionsDto, OperatorIdentityFormOptionsDto>(
            generator, new OperatorIdentityFormOptionsLinkPolicy(), new OperatorIdentityFormOptionsCollectionLinkPolicy());
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([], "test"))
        };
        var resource = await assembler.ToResource(new OperatorIdentityFormOptionsDto(), context);
        await Assert.That(resource.Links).IsEmpty();
    }

    [Test]
    public async Task Identity_document_discovers_lookup_only_for_normal_authenticated_readers()
    {
        var policy = new InstanceOperatorIdentityLinkPolicy();
        var document = new InstanceOperatorIdentityDocumentDto
        {
            PublicDisclosure = new(false, null, []),
            PaidCommerce = new(false, null, [])
        };
        var authenticated = new ClaimsPrincipal(new ClaimsIdentity([], "test"));
        var setup = new ClaimsPrincipal(new ClaimsIdentity([], ApiAuthenticationSchemeNames.SetupSecret));
        var lookup = policy.GetLinks(document, authenticated).Single(link => link.Rel == "form-options");
        await Assert.That(lookup.RouteName).IsEqualTo(RouteNames.GetOperatorIdentityFormOptions);
        await Assert.That(lookup.RequiresAuth).IsTrue();
        await Assert.That(policy.GetLinks(document, setup).Any(link => link.Rel == "form-options")).IsFalse();
        await Assert.That(policy.GetLinks(document, null).Any(link => link.Rel == "form-options")).IsFalse();
    }
}
