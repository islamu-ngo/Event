using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventAspects;
using Explore.Application.Features.EventAspects.Handlers.Commands;
using Explore.Application.Features.EventAspects.Requests.Commands;
using Explore.Application.Mappings;
using Explore.Domain;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Application.UnitTests.Profiles;

public sealed class EventAspectMapperTests
{
    private static readonly Guid EventId = Guid.Parse("01900000-0000-7000-8000-000000000002");

    [Test]
    public async Task IslamicCreation_PreservesSettingsAndServerOwnedSharedKey()
    {
        var events = Substitute.For<IEventRepository>();
        events.GetById(EventId).Returns(Parent());
        var aspects = Substitute.For<IEventIslamicAspectRepository>();
        EventIslamicAspect? stored = null;
        aspects.Create(Arg.Any<EventIslamicAspect>()).Returns(call =>
        {
            var entity = call.Arg<EventIslamicAspect>();
            stored = entity;
            return entity;
        });
        var madhabs = Substitute.For<IMadhabRepository>();
        madhabs.Exists(3).Returns(true);
        var languages = Substitute.For<ILanguageRepository>();
        languages.Exists(5).Returns(true);
        var services = new ServiceCollection();
        services.AddHybridCache();
        using var provider = services.BuildServiceProvider();
        var handler = new CreateEventIslamicAspectCommandHandler(
            events, aspects, madhabs, languages, provider.GetRequiredService<HybridCache>());

        var result = await handler.ExecuteAsync(new CreateEventIslamicAspectCommand
        {
            EventId = EventId,
            AspectDto = new CreateUpdateIslamicAspectDto
            {
                MadhabId = 3,
                PrimaryLanguageId = 5,
                ReferencePrayer = PrayerTime.Fajr,
                PrayerTimeOffset = -15,
                GenderMode = GenderSegregationMode.Family,
                IncludesQuranRecitation = true
            }
        }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.Id).IsEqualTo(EventId);
        await Assert.That(stored.Event).IsNull();
        await Assert.That(stored.Madhab).IsNull();
        await Assert.That(stored.PrimaryLanguage).IsNull();
        await Assert.That(stored.MadhabId).IsEqualTo(3);
        await Assert.That(stored.PrimaryLanguageId).IsEqualTo(5);
        await Assert.That(stored.ReferencePrayer).IsEqualTo(PrayerTime.Fajr);
        await Assert.That(stored.PrayerTimeOffset).IsEqualTo(-15);
        await Assert.That(stored.GenderMode).IsEqualTo(GenderSegregationMode.Family);
        await Assert.That(stored.IncludesQuranRecitation).IsTrue();
        var response = EventMapper.ToDetail(stored);
        stored.PrayerTimeOffset = 60;
        await Assert.That(response!.PrayerTimeOffset).IsEqualTo(-15);
        await Assert.That(response.MadhabName).IsNull();
    }

    [Test]
    public async Task TechCreation_PreservesSettingsAndServerOwnedSharedKey()
    {
        var events = Substitute.For<IEventRepository>();
        events.GetById(EventId).Returns(Parent());
        var aspects = Substitute.For<IEventTechAspectRepository>();
        EventTechAspect? stored = null;
        aspects.Create(Arg.Any<EventTechAspect>()).Returns(call =>
        {
            var entity = call.Arg<EventTechAspect>();
            stored = entity;
            return entity;
        });
        var services = new ServiceCollection();
        services.AddHybridCache();
        using var provider = services.BuildServiceProvider();
        var handler = new CreateEventTechAspectCommandHandler(
            events, aspects, provider.GetRequiredService<HybridCache>());

        var result = await handler.ExecuteAsync(new CreateEventTechAspectCommand
        {
            EventId = EventId,
            AspectDto = new CreateUpdateTechAspectDto
            {
                GithubRepoUrl = "https://code.example.test/project",
                HackathonTrack = "Tools",
                SkillLevel = SkillLevel.Intermediate,
                TechStackTags = "CSharp",
                RequiresLaptop = true,
                IsCodingCompetition = true,
                MaxTeamSize = 4,
                PrizePool = 125.5m,
                PrizeCurrencyCode = "EUR"
            }
        }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.Id).IsEqualTo(EventId);
        await Assert.That(stored.Event).IsNull();
        await Assert.That(stored.GithubRepoUrl).IsEqualTo("https://code.example.test/project");
        await Assert.That(stored.HackathonTrack).IsEqualTo("Tools");
        await Assert.That(stored.SkillLevel).IsEqualTo(SkillLevel.Intermediate);
        await Assert.That(stored.TechStackTags).IsEqualTo("CSharp");
        await Assert.That(stored.RequiresLaptop).IsTrue();
        await Assert.That(stored.IsCodingCompetition).IsTrue();
        await Assert.That(stored.MaxTeamSize).IsEqualTo(4);
        await Assert.That(stored.PrizePool).IsEqualTo(125.5m);
        await Assert.That(stored.PrizeCurrencyCode).IsEqualTo("EUR");
        var response = EventMapper.ToDetail(stored);
        stored.PrizePool = 0;
        await Assert.That(response!.PrizePool).IsEqualTo(125.5m);
    }

    private static Explore.Domain.Event Parent() => new()
    {
        Id = EventId,
        Title = "Workshop",
        Actor = null!,
        Tenant = null!,
        EventStatus = null!,
        VisibilityType = null!,
        EventFormat = null!
    };
}
