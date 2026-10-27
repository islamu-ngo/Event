using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Category;
using Explore.Application.DTOs.Tag;
using Explore.Application.DTOs.User;
using Explore.Application.Hateoas;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features.Hateoas;

/// <summary>
/// Executes the production policy/evaluator/assembler pipeline against controlled PDP responses.
/// Named test routes exercise materialization; these are adapter contracts, not HTTP or policy-engine proof.
/// </summary>
[Category(TestCategories.Fast)]
public sealed class PermissionActionContractTests
{
    private static readonly Guid ResourceId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task CategoryLinksUseCanonicalActionsAndIndependentDecisions(bool update, bool delete) =>
        AssertPipeline(new CategoryDto
        {
            Id = ResourceId,
            TenantId = TenantId,
            MasterCode = "contract",
            FullName = "Contract"
        }, new CategoryDetailLinkPolicy(), new CategoryCollectionLinkPolicy(),
            "islamuevent_category", [("edit", "update", update), ("delete", "delete", delete)]);

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task TagLinksUseCanonicalActionsAndIndependentDecisions(bool update, bool delete) =>
        AssertPipeline(new TagDto
        {
            Id = ResourceId,
            TenantId = TenantId,
            MasterCode = "contract",
            FullName = "Contract"
        }, new TagDetailLinkPolicy(), new TagCollectionLinkPolicy(),
            "islamuevent_tag", [("edit", "update", update), ("delete", "delete", delete)]);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task UserProfileLinkUsesCanonicalActionAndDecision(bool update) =>
        AssertPipeline(new UserDto
        {
            Id = ResourceId,
            Email = "contract@example.invalid",
            FirstName = "Contract",
            LastName = "Subject"
        }, new UserDetailLinkPolicy(), new UserCollectionLinkPolicy(),
            "islamuevent_user", [("edit", "update", update)]);

    private static async Task AssertPipeline<TDto, TListDto>(
        TDto dto,
        ILinkPolicy<TDto> detailPolicy,
        ICollectionLinkPolicy<TListDto> collectionPolicy,
        string expectedKind,
        (string Relation, string Action, bool Allowed)[] expected)
        where TDto : class
        where TListDto : class
    {
        List<AuthorizationRequest> observed = [];
        var provider = Substitute.For<IAuthorizationProvider>();
        provider.AuthorizeBatchAsync(Arg.Any<IReadOnlyList<AuthorizationRequest>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                IReadOnlyList<AuthorizationRequest> requests = call.Arg<IReadOnlyList<AuthorizationRequest>>();
                observed.AddRange(requests);
                return requests.Select(request => expected.Single(item => item.Action == request.Action).Allowed
                    ? AuthorizationDecision.Allow(new AuthorizationProviderMetadata("controlled-pdp-adapter"))
                    : AuthorizationDecision.Deny(new AuthorizationProviderMetadata("controlled-pdp-adapter"))).ToArray();
            });

        using var database = new ExploreDbContext(new DbContextOptions<ExploreDbContext>());
        var evaluator = new HateoasAuthorizationEvaluator(provider, new EventRepository(database),
            new SelectedTenant(), NullLogger<HateoasAuthorizationEvaluator>.Instance);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, ResourceId.ToString("D"))], "contract"));
        Endpoint[] endpoints = detailPolicy.GetLinks(dto, user).Select(link => link.RouteName).Distinct()
            .Select(name =>
            {
                var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask,
                    RoutePatternFactory.Parse($"/contract/{name}/{{id?}}"), 0);
                endpoint.Metadata.Add(new RouteNameMetadata(name));
                return endpoint.Build();
            }).ToArray();
        var services = new ServiceCollection();
        services.AddLogging().AddRouting();
        services.AddSingleton<EndpointDataSource>(new DefaultEndpointDataSource(endpoints));
        services.AddSingleton<IHateoasAuthorizationEvaluator>(evaluator);
        using ServiceProvider serviceProvider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = serviceProvider, User = user };
        var generator = new HateoasLinkGenerator(serviceProvider.GetRequiredService<LinkGenerator>(),
            NullLogger<HateoasLinkGenerator>.Instance);
        var assembler = new HalResourceAssembler<TDto, TListDto>(generator, detailPolicy, collectionPolicy);

        HalResource<TDto> result = await assembler.ToResource(dto, context);

        await Assert.That(observed.Select(request => (request.ResourceKind, request.Action, request.ResourceId)))
            .IsEquivalentTo(expected.Select(item => (expectedKind, item.Action, ResourceId.ToString("D"))));
        foreach (var item in expected)
            await Assert.That(result.Links.ContainsKey(item.Relation)).IsEqualTo(item.Allowed);
        await Assert.That(result.Links.ContainsKey("self")).IsTrue();
    }

    private sealed class SelectedTenant : ITenantContext
    {
        public Guid TenantId => PermissionActionContractTests.TenantId;
    }
}
