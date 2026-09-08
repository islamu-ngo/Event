// ABOUTME: RED HAL tests for tenant-governed event-reporting intake affordances.
// ABOUTME: Specifies fail-closed DTO context and asynchronous assembler enrichment without cached DTO mutation.

namespace Event.Api.IntegrationTests.Features.Hateoas;

using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

public sealed class EventReportingIntakeHateoasTests
{
    [Test]
    public async Task EventPolicies_EnabledIntakePublishesAllReportingRelationsWithoutChangingUnrelatedLinks()
    {
        var detail = CreateDetail(Guid.CreateVersion7()) with { IsReportingIntakeEnabled = true };
        var list = CreateList(detail.TenantId) with { IsReportingIntakeEnabled = true };

        LinkDefinition[] detailLinks = [.. new EventDetailLinkPolicy().GetLinks(detail, null)];
        LinkDefinition[] collectionLinks = [.. new EventCollectionLinkPolicy().GetItemLinks(list, null)];

        await AssertReportingRelations(detailLinks, generalReportExpected: true);
        await AssertReportingRelations(collectionLinks, generalReportExpected: true);
        await Assert.That(detailLinks.Any(link => link.Rel == LinkRelations.Self)).IsTrue();
        await Assert.That(detailLinks.Any(link => link.Rel == LinkRelations.Sessions)).IsTrue();
        await Assert.That(collectionLinks.Any(link => link.Rel == LinkRelations.Self)).IsTrue();
        await Assert.That(collectionLinks.Any(link => link.Rel == "actor")).IsTrue();
    }

    [Test]
    public async Task EventPolicies_DisabledOrDefaultIntakeKeepsOptionsButOmitsReportingMutations()
    {
        var disabledDetail = CreateDetail(Guid.CreateVersion7()) with { IsReportingIntakeEnabled = false };
        var disabledList = CreateList(disabledDetail.TenantId) with { IsReportingIntakeEnabled = false };
        var defaultDetail = CreateDetail(Guid.CreateVersion7());
        var defaultList = CreateList(defaultDetail.TenantId);

        foreach (LinkDefinition[] links in new List<LinkDefinition[]>
                 {
                     new EventDetailLinkPolicy().GetLinks(disabledDetail, null).ToArray(),
                     new EventCollectionLinkPolicy().GetItemLinks(disabledList, null).ToArray(),
                     new EventDetailLinkPolicy().GetLinks(defaultDetail, null).ToArray(),
                     new EventCollectionLinkPolicy().GetItemLinks(defaultList, null).ToArray()
                 })
        {
            await AssertReportingRelations(links, generalReportExpected: false);
        }

        LinkDefinition[] disabledDetailLinks = [.. new EventDetailLinkPolicy().GetLinks(disabledDetail, null)];
        LinkDefinition[] disabledCollectionLinks = [.. new EventCollectionLinkPolicy().GetItemLinks(disabledList, null)];
        await Assert.That(disabledDetailLinks.Any(link => link.Rel == LinkRelations.Self)).IsTrue();
        await Assert.That(disabledDetailLinks.Any(link => link.Rel == LinkRelations.Sessions)).IsTrue();
        await Assert.That(disabledCollectionLinks.Any(link => link.Rel == LinkRelations.Self)).IsTrue();
        await Assert.That(disabledCollectionLinks.Any(link => link.Rel == "actor")).IsTrue();
    }

    [Test]
    public async Task ReportOptionsHal_DisabledOptionsKeepNavigationButOmitSubmit()
    {
        var options = new Explore.Application.DTOs.EventReporting.EventReportOptionsDto
        {
            EventId = Guid.CreateVersion7(),
            IsReportable = false
        };

        LinkDefinition[] detailLinks = [.. new EventReportOptionsDetailLinkPolicy().GetLinks(options, null)];
        LinkDefinition[] collectionLinks = [.. new EventReportOptionsCollectionLinkPolicy().GetItemLinks(options, null)];

        foreach (LinkDefinition[] links in new List<LinkDefinition[]> { detailLinks, collectionLinks })
        {
            await Assert.That(links.Any(link => link.Rel == LinkRelations.Self)).IsTrue();
            await Assert.That(links.Any(link => link.Rel == LinkRelations.Event)).IsTrue();
            await Assert.That(links.Any(link => link.Rel == LinkRelations.ReportEvent)).IsFalse();
        }
    }

    [Test]
    public async Task EventAssembler_ResolvesDistinctTenantsWithPolicyCopiesWithoutAlteringCachedDtos()
    {
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        EventListDto cachedA = CreateList(tenantA);
        EventListDto cachedB = CreateList(tenantB);
        IEventReportingIntakeGuard guard = Substitute.For<IEventReportingIntakeGuard>();
        guard.ResolveAsync(tenantA, Arg.Any<CancellationToken>()).Returns(Task.FromResult(DisabledDecision()));
        guard.ResolveAsync(tenantB, Arg.Any<CancellationToken>()).Returns(Task.FromResult(EnabledDecision()));
        var assembler = new EventResourceAssembler(
            CreateLinkGenerator(),
            new EventDetailLinkPolicy(),
            new EventCollectionLinkPolicy(),
            guard,
            Substitute.For<IVisitorAccessCapabilityResolver>(),
            Substitute.For<IEventRepository>(), TimeProvider.System);

        HalCollectionResource<EventListDto> resource = await assembler.ToCollectionResource(
            [cachedA, cachedB, cachedA],
            RouteNames.GetEvents,
            CreateHttpContext());
        HalCollectionEmbedded<EventListDto> embedded = resource.Embedded ?? throw new InvalidOperationException("Collection embedding is required.");
        EventListDto[] assembled = embedded.Items.Select(item => item.Data).ToArray();

        await guard.Received(1).ResolveAsync(tenantA, Arg.Any<CancellationToken>());
        await guard.Received(1).ResolveAsync(tenantB, Arg.Any<CancellationToken>());
        await Assert.That(assembled.Select(item => item.IsReportingIntakeEnabled).SequenceEqual([false, false, false])).IsTrue();
        await Assert.That(cachedA.IsReportingIntakeEnabled).IsFalse();
        await Assert.That(cachedB.IsReportingIntakeEnabled).IsFalse();
        await Assert.That(ReferenceEquals(assembled[0], cachedA)).IsTrue();
        await Assert.That(ReferenceEquals(assembled[1], cachedB)).IsTrue();
        await Assert.That(embedded.Items[0].Links.ContainsKey(LinkRelations.ReportEvent)).IsFalse();
        await Assert.That(embedded.Items[1].Links.ContainsKey(LinkRelations.ReportEvent)).IsTrue();
        await Assert.That(embedded.Items[2].Links.ContainsKey(LinkRelations.ReportEvent)).IsFalse();
    }

    [Test]
    public async Task EventAssembler_UsesRequestCancellationForIntakeResolution()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        IEventReportingIntakeGuard guard = Substitute.For<IEventReportingIntakeGuard>();
        guard.ResolveAsync(Arg.Any<Guid>(), cancellation.Token)
            .Returns(Task.FromCanceled<EventReportingIntakeDecision>(cancellation.Token));
        var visitorResolver = Substitute.For<IVisitorAccessCapabilityResolver>();
        visitorResolver.ResolveAsync(Arg.Any<Guid>(), cancellation.Token).Returns(
            Task.FromResult(Explore.Application.Services.VisitorAccessCapabilityResolver.EvaluateProposedState(
                new Explore.Application.Models.VisitorAccessPolicyState(VisitorAccessMode.FullRegistrationAndAuth, []))));
        var assembler = new EventResourceAssembler(
            CreateLinkGenerator(),
            new EventDetailLinkPolicy(),
            new EventCollectionLinkPolicy(),
            guard,
            visitorResolver,
            Substitute.For<IEventRepository>(), TimeProvider.System);
        HttpContext context = CreateHttpContext(cancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => assembler.ToResource(CreateDetail(Guid.CreateVersion7()), context));
    }

    [Test]
    public async Task DiscoveryAssembler_EnrichesLocalItemsWithoutTouchingFederatedItems()
    {
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();
        Guid tenantRemote = Guid.CreateVersion7();
        EventListDto cachedA = CreateList(tenantA);
        EventListDto cachedB = CreateList(tenantB);
        EventListDto remoteEvent = CreateList(tenantRemote) with { IsReportingIntakeEnabled = true };
        IEventReportingIntakeGuard guard = Substitute.For<IEventReportingIntakeGuard>();
        guard.ResolveAsync(tenantA, Arg.Any<CancellationToken>()).Returns(Task.FromResult(DisabledDecision()));
        guard.ResolveAsync(tenantB, Arg.Any<CancellationToken>()).Returns(Task.FromResult(EnabledDecision()));
        var assembler = new EventDiscoveryResourceAssembler(
            CreateLinkGenerator(),
            new EventDiscoveryLinkPolicy(new EventCollectionLinkPolicy()),
            new EventDiscoveryLinkPolicy(new EventCollectionLinkPolicy()),
            guard);

        var localA = new EventDiscoveryItemDto { Source = "local", Event = cachedA };
        var federated = new EventDiscoveryItemDto
        {
            Source = "atproto",
            Event = remoteEvent,
            FederatedEvent = new FederatedEventDto { Id = Guid.CreateVersion7(), Name = "Federated" }
        };
        var localB = new EventDiscoveryItemDto { Source = "local", Event = cachedB };
        HalCollectionResource<EventDiscoveryItemDto> resource = await assembler.ToCollectionResource(
            [localA, federated, localB],
            RouteNames.GetEvents,
            CreateHttpContext());
        HalCollectionEmbedded<EventDiscoveryItemDto> embedded = resource.Embedded ?? throw new InvalidOperationException("Collection embedding is required.");
        EventDiscoveryItemDto[] assembled = embedded.Items.Select(item => item.Data).ToArray();

        await guard.Received(1).ResolveAsync(tenantA, Arg.Any<CancellationToken>());
        await guard.Received(1).ResolveAsync(tenantB, Arg.Any<CancellationToken>());
        await Assert.That(assembled[0].Event!.IsReportingIntakeEnabled).IsFalse();
        await Assert.That(assembled[1].Event).IsSameReferenceAs(remoteEvent);
        await Assert.That(assembled[1].Event!.IsReportingIntakeEnabled).IsTrue();
        await Assert.That(assembled[2].Event!.IsReportingIntakeEnabled).IsFalse();
        await Assert.That(cachedA.IsReportingIntakeEnabled).IsFalse();
        await Assert.That(cachedB.IsReportingIntakeEnabled).IsFalse();
        await Assert.That(ReferenceEquals(assembled[0].Event, cachedA)).IsTrue();
        await Assert.That(ReferenceEquals(assembled[2].Event, cachedB)).IsTrue();
        await Assert.That(embedded.Items[0].Links.ContainsKey(LinkRelations.ReportEvent)).IsFalse();
        await Assert.That(embedded.Items[1].Links.ContainsKey(LinkRelations.ReportEvent)).IsFalse();
        await Assert.That(embedded.Items[2].Links.ContainsKey(LinkRelations.ReportEvent)).IsTrue();
        await guard.DidNotReceive().ResolveAsync(tenantRemote, Arg.Any<CancellationToken>());
    }

    private static EventReportingIntakeDecision EnabledDecision() => new(
        TenantResolved: true,
        IntakeEnabled: true,
        ReasonCode: "event_reporting_intake_enabled",
        Message: "Event reporting intake is enabled.");

    private static EventReportingIntakeDecision DisabledDecision() => new(
        TenantResolved: true,
        IntakeEnabled: false,
        ReasonCode: "event_reporting_intake_disabled",
        Message: "Event reporting intake is disabled for this tenant.");

    private static async Task AssertReportingRelations(
        IEnumerable<LinkDefinition> links,
        bool generalReportExpected)
    {
        LinkDefinition[] materialized = [.. links];
        await Assert.That(materialized.Any(link => link.Rel == LinkRelations.EventReportOptions)).IsTrue();
        await Assert.That(materialized.Any(link => link.Rel == LinkRelations.ReportEvent))
            .IsEqualTo(generalReportExpected);
        foreach ((string Relation, string RouteName) remedy in new[]
                 {
                     (LinkRelations.SuggestCorrection, "SubmitEventCorrection"),
                     (LinkRelations.ReportExternalLink, "SubmitUnsafeExternalLinkReport"),
                     ("report-legal-or-copyright", "SubmitLegalOrCopyrightComplaint")
                 })
        {
            LinkDefinition? link = materialized.SingleOrDefault(
                candidate => candidate.Rel == remedy.Relation);
            await Assert.That(link).IsNotNull();
            if (link is not null)
            {
                await Assert.That(link.RouteName).IsEqualTo(remedy.RouteName);
                await Assert.That(link.RequiresAuth).IsTrue();
                await Assert.That(link.AdvertiseWhenAnonymous).IsTrue();
            }
        }
    }

    private static IHateoasLinkGenerator CreateLinkGenerator()
    {
        IHateoasLinkGenerator generator = Substitute.For<IHateoasLinkGenerator>();
        generator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>())
            .Returns(call => new HalLink { Href = $"/{call.Arg<LinkDefinition>()!.Rel}" });
        return generator;
    }

    private static HttpContext CreateHttpContext(CancellationToken cancellationToken = default)
    {
        IHateoasAuthorizationEvaluator evaluator = Substitute.For<IHateoasAuthorizationEvaluator>();
        evaluator.AreLinksAllowedAsync(
                Arg.Any<IReadOnlyList<LinkDefinition>>(),
                Arg.Any<ClaimsPrincipal?>(),
                Arg.Any<HttpContext>())
            .Returns(call =>
            {
                IReadOnlyList<LinkDefinition> definitions = call.Arg<IReadOnlyList<LinkDefinition>>() ?? [];
                return definitions.Select(_ => true).ToArray();
            });
        return new DefaultHttpContext
        {
            RequestAborted = cancellationToken,
            RequestServices = new ServiceCollection().AddSingleton(evaluator).BuildServiceProvider()
        };
    }

    private static EventDto CreateDetail(Guid tenantId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Title = "Event",
        ActorId = Guid.CreateVersion7(),
        ActorDisplayName = "Organizer",
        ActorTypeFullName = "Organization",
        EventStatusId = (int)EventStatusEnum.Published,
        EventStatusFullName = "Published",
        EventStatusMasterCode = "PUBLISHED",
        VisibilityTypeId = (int)VisibilityTypeEnum.Public,
        VisibilityTypeFullName = "Public",
        VisibilityTypeMasterCode = "PUBLIC",
        EventFormatId = (int)EventFormatEnum.Local,
        EventFormatFullName = "In person",
        EventFormatMasterCode = "IN_PERSON",
        IsPubliclyEligible = true,
        SessionCount = 1
    };

    private static EventListDto CreateList(Guid tenantId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Title = "Event",
        EventTypeFullName = "Conference",
        AudienceGenderFullName = "All",
        AudienceAgeFullName = "All",
        ActorId = Guid.CreateVersion7(),
        ActorDisplayName = "Organizer",
        ActorTypeFullName = "Organization",
        EventStatusId = (int)EventStatusEnum.Published,
        EventStatusFullName = "Published",
        VisibilityTypeId = (int)VisibilityTypeEnum.Public,
        VisibilityTypeFullName = "Public",
        EventFormatFullName = "In person",
        SessionCount = 1
    };
}
