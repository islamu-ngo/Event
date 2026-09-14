using System.Net;
using System.Net.Http.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class SessionCustomPropertyNativeTests
{
    [Test]
    public async Task MultiValueClear_RemovesSourceAndProjection_WhileMetadataPatchRefreshesExposure()
    {
        await using var factory = await SessionFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        using var scope = Scope(factory, PlatformDefaults.DefaultTenantId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var definition = await db.EventSessionCustomPropertyDefinitions.SingleAsync(row => row.Id == data.DefinitionId);
        definition.IsMulti = true;
        await db.SaveChangesAsync();
        using var set = await client.PutAsJsonAsync($"{Root}/values", new SetEventSessionCustomPropertyMultiValuesDto
        {
            DefinitionId = data.DefinitionId, EventSessionId = data.SessionId, Values = [ValueDto(data, "First"), ValueDto(data, "Second")]
        });
        await Assert.That(set.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var detail = await DetailAsync(factory, PlatformDefaults.DefaultTenantId, data.DefinitionId);
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"{Root}/{data.DefinitionId}")
        {
            Content = JsonContent.Create(new UpdateEventSessionCustomPropertyDefinitionDto { Metadata = new() { ExposureLevel = ExposureLevel.Public } })
        };
        patch.Headers.TryAddWithoutValidation("If-Match", $"\"{detail.ConcurrencyStamp:D}\"");
        using var updated = await client.SendAsync(patch);
        await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (var verify = Scope(factory, PlatformDefaults.DefaultTenantId))
        {
            var rows = await verify.ServiceProvider.GetRequiredService<IEventSessionCustomPropertyProjectionRepository>()
                .GetForSessionAsync(data.SessionId, ExposureLevel.Public, default);
            await Assert.That(rows.Select(row => row.TextValue).ToArray()).IsEquivalentTo(new string?[] { "First", "Second" });
        }
        using var clear = await client.PutAsJsonAsync($"{Root}/values", new SetEventSessionCustomPropertyMultiValuesDto
        {
            DefinitionId = data.DefinitionId, EventSessionId = data.SessionId, Values = []
        });
        await Assert.That(clear.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await ValuesAsync(factory, PlatformDefaults.DefaultTenantId, data.SessionId)).Count).IsEqualTo(0);
        using var cleared = Scope(factory, PlatformDefaults.DefaultTenantId);
        await Assert.That((await cleared.ServiceProvider.GetRequiredService<IEventSessionCustomPropertyProjectionRepository>()
            .GetForSessionAsync(data.SessionId, ExposureLevel.Internal, default)).Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task Values_CommitOrRollbackSourceAndProjectionThroughHttp(bool multiple, bool failCommit)
    {
        await using var factory = await SessionFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        if (multiple)
        {
            using var scope = Scope(factory, PlatformDefaults.DefaultTenantId);
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            (await db.EventSessionCustomPropertyDefinitions.SingleAsync(row => row.Id == data.DefinitionId)).IsMulti = true;
            await db.SaveChangesAsync();
        }
        using var initial = await client.PutAsJsonAsync($"{Root}/value", ValueDto(data, "Initial"));
        await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var initialId = (await initial.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!.Id;
        using var warmed = await client.GetAsync($"{Root}/values?eventSessionId={data.SessionId}");
        await Assert.That(warmed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await warmed.Content.ReadFromJsonAsync<List<EventSessionCustomPropertyValueDto>>())!.Single().TextValue).IsEqualTo("Initial");
        factory.Commits.Fail = failCommit;
        using var response = multiple
            ? await client.PutAsJsonAsync($"{Root}/values", new SetEventSessionCustomPropertyMultiValuesDto
            {
                DefinitionId = data.DefinitionId, EventSessionId = data.SessionId,
                Values = [ValueDto(data, "Replaced"), ValueDto(data, "Second")]
            })
            : await client.PutAsJsonAsync($"{Root}/value", ValueDto(data, "Replaced"));
        factory.Commits.Fail = false;
        await Assert.That(response.StatusCode).IsEqualTo(failCommit ? HttpStatusCode.InternalServerError : HttpStatusCode.OK);
        using var valuesResponse = await client.GetAsync($"{Root}/values?eventSessionId={data.SessionId}");
        await Assert.That(valuesResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var values = (await valuesResponse.Content.ReadFromJsonAsync<List<EventSessionCustomPropertyValueDto>>())!;
        await Assert.That(values.Select(value => value.TextValue).ToArray()).IsEquivalentTo(
            failCommit ? new string?[] { "Initial" } : multiple ? new string?[] { "Replaced", "Second" } : new string?[] { "Replaced" });
        await Assert.That(values.Select(value => value.Ordinal).ToArray()).IsEquivalentTo(!failCommit && multiple ? new[] { 0, 1 } : new[] { 0 });
        if (!multiple || failCommit) await Assert.That(values.Single().Id).IsEqualTo(initialId);
        using var fresh = Scope(factory, PlatformDefaults.DefaultTenantId);
        var projected = await fresh.ServiceProvider.GetRequiredService<IEventSessionCustomPropertyProjectionRepository>()
            .GetForSessionAsync(data.SessionId, ExposureLevel.Internal, default);
        await Assert.That(projected.Select(value => value.TextValue).ToArray()).IsEquivalentTo(values.Select(value => value.TextValue).ToArray());
        await Assert.That((await ValuesAsync(factory, data.ForeignTenantId, data.SessionId)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task ForeignTargets_InvalidValuesAndDuplicateMultiValues_DoNotMutate()
    {
        await using var factory = await SessionFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        using var initial = await client.PutAsJsonAsync($"{Root}/value", ValueDto(data, "Retained"));
        await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK);
        foreach (var invalid in new[]
        {
            ValueDto(data, "Foreign") with { EventSessionCustomPropertyDefinitionId = data.ForeignDefinitionId, EventSessionId = data.ForeignSessionId },
            ValueDto(data, "Wrong session") with { EventSessionId = data.ForeignSessionId },
            ValueDto(data, "Wrong ordinal") with { Ordinal = 1 },
            ValueDto(data, "Wrong shape") with { NumberValue = 5 }
        })
        {
            using var response = await client.PutAsJsonAsync($"{Root}/value", invalid);
            await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed", "eventSessionCustomPropertyValue");
        }
        using var foreignMulti = await client.PutAsJsonAsync($"{Root}/values", new SetEventSessionCustomPropertyMultiValuesDto
        {
            DefinitionId = data.ForeignDefinitionId, EventSessionId = data.ForeignSessionId,
            Values = [ValueDto(data, "Foreign")]
        });
        await ProblemAsync(foreignMulti, HttpStatusCode.BadRequest, "validation_failed", "eventSessionCustomPropertyValue");
        using (var scope = Scope(factory, PlatformDefaults.DefaultTenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            (await db.EventSessionCustomPropertyDefinitions.SingleAsync(row => row.Id == data.DefinitionId)).IsMulti = true;
            await db.SaveChangesAsync();
        }
        using var duplicate = await client.PutAsJsonAsync($"{Root}/values", new SetEventSessionCustomPropertyMultiValuesDto
        {
            DefinitionId = data.DefinitionId, EventSessionId = data.SessionId,
            Values = [ValueDto(data, "Alpha"), ValueDto(data, " alpha ")]
        });
        await ProblemAsync(duplicate, HttpStatusCode.BadRequest, "validation_failed", "eventSessionCustomPropertyValue");
        await Assert.That((await ValuesAsync(factory, PlatformDefaults.DefaultTenantId, data.SessionId)).Single().TextValue).IsEqualTo("Retained");
        await Assert.That((await ValuesAsync(factory, data.ForeignTenantId, data.ForeignSessionId)).Count).IsEqualTo(0);
    }
}
