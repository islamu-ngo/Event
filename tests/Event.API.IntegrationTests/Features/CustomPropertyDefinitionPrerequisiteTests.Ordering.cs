using System.Net;
using System.Net.Http.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Queries;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class CustomPropertyDefinitionPrerequisiteTests
{
    [Test]
    [Arguments("native")]
    [Arguments("http")]
    public async Task DetailOptions_AreAscendingWithStableTiesAndRetainedDefaultAndRetiredIdentities(string surface)
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedMutationAsync(factory, client);
        var tiedOption = Option(data.Seed.OwnDefinitionId, "tie", 10, false);
        using (var seed = TenantScope(factory, PlatformDefaults.DefaultTenantId))
        {
            var db = seed.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.CustomPropertyOptions.Add(tiedOption);
            await db.SaveChangesAsync();
        }
        using var updated = await MutateAsync(client, data, "options");
        await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);

        CustomPropertyDefinitionDto detail;
        if (surface == "http")
        {
            using var response = await client.GetAsync($"{Root}/{data.Seed.OwnDefinitionId}");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            detail = (await response.Content.ReadFromJsonAsync<CustomPropertyDefinitionDto>(JsonOptions))!;
        }
        else
        {
            using var scope = TenantScope(factory, PlatformDefaults.DefaultTenantId);
            detail = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetCustomPropertyDefinitionDetailsQuery, CustomPropertyDefinitionDto>>()
                .QueryAsync(new(data.Seed.OwnDefinitionId), default);
        }

        await Assert.That(detail.Options[0].Key).IsEqualTo("new");
        await Assert.That(detail.Options.Select(option => option.SortOrder).SequenceEqual([5, 10, 10, 20])).IsTrue();
        await Assert.That(detail.Options.Select(option => option.Key).SequenceEqual(["new", "kept", "tie", "retired"])).IsTrue();
        await Assert.That(detail.Id).IsEqualTo(data.Seed.OwnDefinitionId);
        await Assert.That(detail.DefaultOptionId).IsEqualTo(data.KeptOptionId);
        await Assert.That(detail.Options.Single(option => option.IsDefault).Id).IsEqualTo(data.KeptOptionId);
        await Assert.That(detail.Options[1].Id).IsEqualTo(data.KeptOptionId);
        await Assert.That(detail.Options[2].Id).IsEqualTo(tiedOption.Id);
        await Assert.That(detail.Options[2].IsActive).IsFalse();
        await Assert.That(detail.Options[3].Id).IsEqualTo(data.RetiredOptionId);
        await Assert.That(detail.Options[3].IsActive).IsFalse();
    }
}
