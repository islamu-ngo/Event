using System.Text.Json;
using Explore.Application;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Operations.Decorators;
using Microsoft.Extensions.DependencyInjection;
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
        repository.GetById(8).Returns(sources[0]);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(repository);
        services.AddSingleton(Substitute.For<IAuthorizationProvider>());
        services.AddNativeOperations(
        [
            typeof(GetAudienceAgeListRequest), typeof(GetAudienceAgeListRequestHandler),
            typeof(GetAudienceAgeDetailsRequest), typeof(GetAudienceAgeDetailsRequestHandler)
        ]);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var listQuery = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetAudienceAgeListRequest, List<AudienceAgeListDto>>>();
        var detailQuery = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetAudienceAgeDetailsRequest, AudienceAgeDto?>>();
        await Assert.That(listQuery).IsTypeOf<AuthorizationQueryHandlerDecorator<GetAudienceAgeListRequest, List<AudienceAgeListDto>>>();
        await Assert.That(detailQuery).IsTypeOf<AuthorizationQueryHandlerDecorator<GetAudienceAgeDetailsRequest, AudienceAgeDto?>>();
        var list = await listQuery.QueryAsync(new GetAudienceAgeListRequest(), CancellationToken.None);
        var detail = await detailQuery.QueryAsync(new GetAudienceAgeDetailsRequest { Id = 8 }, CancellationToken.None);
        var missing = await detailQuery.QueryAsync(new GetAudienceAgeDetailsRequest { Id = 999 }, CancellationToken.None);
        sources[0].MinAge = 21;
        sources.Clear();
        await Assert.That(detail).IsEqualTo(new AudienceAgeDto { Id = 8, MasterCode = "ADULT", FullName = "Adults", MinAge = 18 });
        await Assert.That(list.Select(item => item.Id).SequenceEqual([8, 2])).IsTrue();
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
