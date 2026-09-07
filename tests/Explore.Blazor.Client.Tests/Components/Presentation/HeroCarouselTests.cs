using Explore.Blazor.Client.Components.Presentation;

namespace Explore.Blazor.Client.Tests.Components.Presentation;

public sealed class HeroCarouselTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task HeroCarouselCapsSlidesAtTen()
    {
        var cut = Render(12);

        await Assert.That(cut.FindAll("[data-testid='hero-slide']").Count).IsEqualTo(10);
    }

    [Test]
    public async Task HeroCarouselManualControlsAdvanceAndWrap()
    {
        var cut = Render(3);

        await Assert.That(cut.Find("[data-testid='hero-counter']").TextContent.Trim()).IsEqualTo("NO. 1");

        await cut.Find("button[aria-label='Next featured event']")
            .TriggerEventAsync("onclick", new MouseEventArgs());
        await Assert.That(cut.Find("[data-testid='hero-counter']").TextContent.Trim()).IsEqualTo("NO. 2");

        await cut.Find("button[aria-label='Previous featured event']")
            .TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find("button[aria-label='Previous featured event']")
            .TriggerEventAsync("onclick", new MouseEventArgs());
        await Assert.That(cut.Find("[data-testid='hero-counter']").TextContent.Trim()).IsEqualTo("NO. 3");
    }

    [Test]
    public async Task HeroCarouselAutomaticallyAdvancesAfterNineSeconds()
    {
        var cut = Render(2);

        cut.WaitForState(
            () => cut.Find("[data-testid='hero-counter']").TextContent.Trim() == "NO. 2",
            TimeSpan.FromSeconds(10));

        await Assert.That(cut.Find("[data-testid='hero-counter']").TextContent.Trim()).IsEqualTo("NO. 2");
    }

    [Test]
    public async Task HeroCarouselPointerSwipeChangesSlide()
    {
        var cut = Render(3);
        var hero = cut.Find("[data-testid='hero-carousel']");

        await hero.TriggerEventAsync("onpointerdown", new PointerEventArgs { ClientX = 240, PointerId = 1, IsPrimary = true });
        await hero.TriggerEventAsync("onpointerup", new PointerEventArgs { ClientX = 120, PointerId = 1, IsPrimary = true });

        await Assert.That(cut.Find("[data-testid='hero-counter']").TextContent.Trim()).IsEqualTo("NO. 2");
    }

    [Test]
    public async Task HeroCarouselRendersBackdropAndInsetPosterForActiveSlide()
    {
        var cut = Render(1);
        var slide = cut.Find("[data-testid='hero-slide']:not([hidden])");

        await Assert.That(slide.QuerySelectorAll(".hero-carousel__backdrop").Length).IsEqualTo(1);
        await Assert.That(slide.QuerySelectorAll(".hero-carousel__poster-image").Length).IsEqualTo(1);
        await Assert.That(slide.QuerySelector(".hero-carousel__backdrop")!.GetAttribute("alt")).IsEqualTo(string.Empty);
        await Assert.That(slide.QuerySelector(".hero-carousel__poster-image")!.GetAttribute("alt"))
            .IsEqualTo("Poster for Featured event 1");
    }

    [Test]
    public async Task HeroCarouselKeepsContextHeaderOutsideAnimatedSlides()
    {
        var cut = _ctx.RenderMudComponent<HeroCarousel>(parameters => parameters
            .Add(component => component.Events, CreateEvents(2))
            .Add(
                component => component.HeaderContent,
                (RenderFragment)(builder => builder.AddContent(0, "Browsing events in"))));

        var header = cut.Find(".hero-carousel__persistent-header");

        await Assert.That(header.TextContent.Trim()).IsEqualTo("Browsing events in");
        await Assert.That(cut.FindAll(".hero-carousel__slide .hero-carousel__persistent-header").Count).IsEqualTo(0);

        await cut.Find("button[aria-label='Next featured event']")
            .TriggerEventAsync("onclick", new MouseEventArgs());

        await Assert.That(cut.FindAll(".hero-carousel__persistent-header").Count).IsEqualTo(1);
    }

    [Test]
    public async Task HeroCarouselRendersTitleBeforeCompactClassificationBadges()
    {
        var events = CreateEvents(1);
        events[0] = events[0] with { EventTypeFullName = "Community gathering", EventFormatFullName = "In-Person" };

        var cut = _ctx.RenderMudComponent<HeroCarousel>(parameters => parameters
            .Add(component => component.Events, events));
        var content = cut.Find(".hero-carousel__content");
        var badges = content.QuerySelectorAll(".hero-carousel__badge");

        await Assert.That(badges.Length).IsEqualTo(2);
        await Assert.That(badges[0].TextContent).IsEqualTo("COMMUNITY GATHERING");
        await Assert.That(badges[1].TextContent).IsEqualTo("IN-PERSON");
        await Assert.That(content.InnerHtml.IndexOf("hero-carousel__title", StringComparison.Ordinal))
            .IsLessThan(content.InnerHtml.IndexOf("hero-carousel__badges", StringComparison.Ordinal));
    }

    [Test]
    public async Task HeroCarouselRendersLinkedActorIdentityBelowDescription()
    {
        var actorId = Guid.NewGuid();
        var events = CreateEvents(1);
        events[0] = events[0] with { ActorId = actorId, ActorTypeId = 2, ActorDisplayName = "Community organizer", ActorProfilePictureUri = "https://example.test/actors/community-organizer.webp" };

        var cut = _ctx.RenderMudComponent<HeroCarousel>(parameters => parameters
            .Add(component => component.Events, events));
        var content = cut.Find(".hero-carousel__content");
        var actorLink = cut.Find("a.hero-carousel__actor-link");
        var actorImage = actorLink.QuerySelector("img")!;

        await Assert.That(actorLink.GetAttribute("href")).IsEqualTo($"/organization/profile/{actorId}");
        await Assert.That(actorLink.GetAttribute("aria-label")).IsEqualTo("View Community organizer's profile");
        await Assert.That(actorLink.TextContent.Trim()).IsEqualTo("Community organizer");
        await Assert.That(actorImage.GetAttribute("src")).IsEqualTo(events[0].ActorProfilePictureUri);
        await Assert.That(actorImage.GetAttribute("alt")).IsEqualTo(string.Empty);
        await Assert.That(content.InnerHtml.IndexOf("hero-carousel__description", StringComparison.Ordinal))
            .IsLessThan(content.InnerHtml.IndexOf("hero-carousel__actor-link", StringComparison.Ordinal));
    }

    [Test]
    public async Task HeroCarouselRendersOnlySafeExternalEventAction()
    {
        var events = CreateEvents(2);
        const string sourceHref = "/api/event/source/featured";
        SetFederatedSource(events[0], sourceHref);
        SetFederatedSource(events[1], "javascript:alert('unsafe')");

        var cut = _ctx.RenderMudComponent<HeroCarousel>(parameters => parameters
            .Add(component => component.Events, events));
        var externalLink = cut.Find("a.hero-carousel__external-link");

        await Assert.That(externalLink.GetAttribute("href")).IsEqualTo(sourceHref);
        await Assert.That(externalLink.GetAttribute("target")).IsEqualTo("_blank");
        await Assert.That(externalLink.GetAttribute("rel")).IsEqualTo("noopener noreferrer");
        await Assert.That(externalLink.GetAttribute("aria-label"))
            .IsEqualTo("Open Featured event 1 on its external platform in a new tab");
        await Assert.That(cut.FindAll("a.hero-carousel__external-link").Count).IsEqualTo(1);
    }

    [Test]
    public async Task ActiveSlideKeepsPrimaryEventLinkIndependentFromSecondaryActions()
    {
        var cut = Render(1);
        var slide = cut.Find("[data-testid='hero-slide']:not([hidden])");
        var link = slide.QuerySelector("a.hero-carousel__slide-link")!;

        await Assert.That(link.GetAttribute("href")).IsEqualTo("/events/featured-event-1-EVT001");
        await Assert.That(link.GetAttribute("aria-label")).IsEqualTo("View event: Featured event 1");
        await Assert.That(slide.QuerySelectorAll(".hero-carousel__link").Length).IsEqualTo(0);
    }

    [Test]
    public async Task HeroCarouselPrioritizesOnlyActiveBackdropAndPoster()
    {
        var cut = Render(3);

        await Assert.That(cut.FindAll("img[fetchpriority='high'][loading='eager']").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll("img[fetchpriority='low'][loading='lazy']").Count).IsEqualTo(4);
    }

    [Test]
    public async Task HeroCarouselUsesImageHelperFallbackForMissingImage()
    {
        var events = CreateEvents(1);
        events[0] = events[0] with { FeaturedImageUri = null };

        var cut = _ctx.RenderMudComponent<HeroCarousel>(parameters => parameters
            .Add(component => component.Events, events));

        await Assert.That(cut.FindAll("img").All(image =>
                image.GetAttribute("src")?.StartsWith("data:image/svg+xml;utf8,", StringComparison.Ordinal) == true))
            .IsTrue();
    }

    [Test]
    public async Task HeroCarouselUsesImageHelperFallbackForCanonicalPlaceholder()
    {
        var events = CreateEvents(1);
        events[0] = events[0] with { FeaturedImageUri = "https://placeholder.islamu.org/event-default.jpg" };

        var cut = _ctx.RenderMudComponent<HeroCarousel>(parameters => parameters
            .Add(component => component.Events, events));

        await Assert.That(cut.FindAll("img").All(image =>
                image.GetAttribute("src")?.StartsWith("data:image/svg+xml;utf8,", StringComparison.Ordinal) == true))
            .IsTrue();
    }

    [Test]
    public async Task HeaderOnlyHeroUsesOverflowSafeEmptyVariant()
    {
        var cut = _ctx.RenderMudComponent<HeroCarousel>(parameters => parameters
            .Add(component => component.Events, Array.Empty<EventListDto>())
            .Add(
                component => component.HeaderContent,
                (RenderFragment)(builder => builder.AddContent(0, "Browsing events in"))));

        await Assert.That(cut.Find("[data-testid='hero-carousel']").ClassList)
            .Contains("hero-carousel--empty");
    }

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    private IRenderedComponent<HeroCarousel> Render(int count)
    {
        return _ctx.RenderMudComponent<HeroCarousel>(parameters => parameters
            .Add(component => component.Events, CreateEvents(count)));
    }

    private static List<EventListDto> CreateEvents(int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => new EventListDto
            {
                Id = Guid.NewGuid(),
                Title = $"Featured event {index}",
                Slug = $"featured-event-{index}",
                PublicCode = $"EVT{index:D3}",
                Description = $"Description {index}",
                FeaturedImageUri = $"https://example.test/events/{index}.webp",
                ActorDisplayName = "Community organizer",
                EventTypeFullName = "Community event",
                EventFormatFullName = "In-Person",
                FirstSessionDate = new DateTimeOffset(2026, 8, index, 18, 0, 0, TimeSpan.Zero)
            })
            .ToList();
    }

    private static void SetFederatedSource(EventListDto eventDto, string href)
    {
        eventDto.AdditionalProperties["eventDiscoverySource"] = "atproto";
        eventDto.AdditionalProperties["_links"] = System.Text.Json.JsonSerializer.SerializeToElement(
            new Dictionary<string, HalLink>
            {
                ["source"] = new() { Href = href, Method = "GET" }
            });
    }
}
