using System.Text.Json;
using Explore.Application.DTOs.AudienceAge;
using Explore.Application.DTOs.AudienceGender;
using Explore.Application.DTOs.EventType;
using Explore.Application.Mappings;
using Explore.Domain;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.AudienceAges.Handlers.Queries;
using Explore.Application.Features.AudienceAges.Requests.Queries;
using NSubstitute;

namespace Event.Application.UnitTests.Profiles;

public sealed class EventCatalogMapperTests
{
    [Test]
    public async Task AgeHandlers_PreserveMissingDetailAndListOrder()
    {
        var repository = Substitute.For<IAudienceAgeRepository>();
        var sources = new List<AudienceAge>
        {
            new() { Id = 8, MasterCode = "ADULT", FullName = "Adults", MinAge = 18 },
            new() { Id = 2, MasterCode = "ALL", FullName = "All" }
        };
        repository.GetAll().Returns(sources);
        var list = await new GetAudienceAgeListRequestHandler(repository).Handle(new GetAudienceAgeListRequest(), CancellationToken.None);
        var missing = await new GetAudienceAgeDetailsRequestHandler(repository).Handle(new GetAudienceAgeDetailsRequest { Id = 999 }, CancellationToken.None);
        sources.Clear();
        await Assert.That(list.Select(item => item.Id).ToArray()).IsEquivalentTo(new[] { 8, 2 });
        await Assert.That(list[0].MinAge).IsEqualTo(18);
        await Assert.That(list[1].MaxAge).IsNull();
        await Assert.That(missing).IsNull();
    }

    [Test]
    public async Task CatalogProjections_PreserveBoundsNullsAndIndependentValues()
    {
        var age = new AudienceAge { Id = 7, MasterCode = "YOUTH", FullName = "Youth", MinAge = 12, MaxAge = 18, Description = null };
        var gender = new AudienceGender { Id = 3, MasterCode = "ALL", FullName = "All", Description = "" };
        var type = new EventType { Id = 9, MasterCode = "LECTURE", FullName = "Lecture", Description = "Talk", TenantId = Guid.Parse("01900000-0000-7000-8000-000000000001") };
        var detail = EventMapper.ToDetail(age);
        var list = EventMapper.ToListItem(age);
        var genderDetail = EventMapper.ToDetail(gender);
        var genderList = EventMapper.ToListItem(gender);
        var typeDto = EventMapper.ToListItem(type);
        age.MaxAge = null;
        gender.FullName = "Changed";
        type.Description = "Changed";

        await Assert.That(detail).IsEqualTo(new AudienceAgeDto { Id = 7, MasterCode = "YOUTH", FullName = "Youth", MinAge = 12, MaxAge = 18 });
        await Assert.That(list).IsEqualTo(new AudienceAgeListDto { Id = 7, MasterCode = "YOUTH", FullName = "Youth", MinAge = 12, MaxAge = 18 });
        await Assert.That(genderDetail).IsEqualTo(new AudienceGenderDto { Id = 3, MasterCode = "ALL", FullName = "All", Description = "" });
        await Assert.That(genderList).IsEqualTo(new AudienceGenderListDto { Id = 3, MasterCode = "ALL", FullName = "All", Description = "" });
        await Assert.That(typeDto).IsEqualTo(new EventTypeListDto { Id = 9, MasterCode = "LECTURE", FullName = "Lecture", Description = "Talk" });
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(typeDto));
        await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(4);
        await Assert.That(json.RootElement.TryGetProperty("TenantId", out _)).IsFalse();
    }
}
