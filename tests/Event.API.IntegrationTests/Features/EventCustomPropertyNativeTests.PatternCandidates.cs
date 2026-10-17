using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class EventCustomPropertyNativeTests
{
    [Test]
    public async Task PatternCandidatePatch_RejectsWithoutMutation_AndRoundTripsDescriptionPresence()
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        using (var seed = Scope(factory, PlatformDefaults.DefaultTenantId))
        {
            var db = seed.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var row = await db.EventCustomPropertyDefinitions.SingleAsync(row => row.Id == data.DefinitionId);
            row.Description = "Retained";
            row.MinLength = 2;
            row.MaxLength = 8;
            row.RegexPattern = "^[a-z]+$";
            row.DefaultTextValue = "safe";
            await db.SaveChangesAsync();
        }
        var before = await DetailAsync(factory, PlatformDefaults.DefaultTenantId, data.DefinitionId);
        foreach (var invalid in new[]
        {
            """{"metadata":{"description":{"hasValue":true,"value":null},"displayName":"Changed"},"validation":{"regexPattern":{"hasValue":true,"value":"(?=private-pattern-canary)"}}}""",
            """{"metadata":{"description":{"hasValue":true,"value":null}},"validation":{"maxLength":{"hasValue":true,"value":1}}}""",
            """{"metadata":{"description":{"hasValue":true,"value":null}},"validation":{"defaultTextValue":{"hasValue":true,"value":"PRIVATE-VALUE-CANARY"}}}""",
            """{"metadata":{"description":{"hasValue":true,"value":null}},"options":{"items":null}}""",
            """{"metadata":{"description":{"hasValue":true,"value":null}},"options":{"items":[null]}}"""
        })
        {
            using var patch = new HttpRequestMessage(HttpMethod.Patch, $"{Root}/{data.DefinitionId}")
            {
                Content = new StringContent(invalid, Encoding.UTF8, "application/json")
            };
            patch.Headers.TryAddWithoutValidation("If-Match", $"\"{before.ConcurrencyStamp:D}\"");
            using var rejected = await client.SendAsync(patch);
            await ProblemAsync(rejected, HttpStatusCode.BadRequest, "validation_failed", "eventCustomPropertyDefinition");
            var failure = await rejected.Content.ReadAsStringAsync();
            await Assert.That(failure).DoesNotContain("private-pattern-canary");
            await Assert.That(failure).DoesNotContain("PRIVATE-VALUE-CANARY");
            using var fresh = Scope(factory, PlatformDefaults.DefaultTenantId);
            var stored = await fresh.ServiceProvider.GetRequiredService<IEventCustomPropertyRepository>()
                .GetDefinitionWithDetails(data.DefinitionId);
            await Assert.That(stored!.Description).IsEqualTo("Retained");
            await Assert.That(stored.DisplayName).IsEqualTo("own");
            await Assert.That(stored.MaxLength).IsEqualTo(8);
            await Assert.That(stored.RegexPattern).IsEqualTo("^[a-z]+$");
            await Assert.That(stored.DefaultTextValue).IsEqualTo("safe");
            await Assert.That(stored.ConcurrencyStamp).IsEqualTo(before.ConcurrencyStamp);
            await Assert.That(stored.Options.Count).IsEqualTo(0);

            using var native = Scope(factory, PlatformDefaults.DefaultTenantId, data.AdminId);
            var repository = native.ServiceProvider.GetRequiredService<IEventCustomPropertyRepository>();
            var tracked = await repository.GetTrackedDefinitionWithOptions(data.DefinitionId, default);
            var options = native.ServiceProvider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
            var result = await native.ServiceProvider
                .GetRequiredService<ICommandHandler<UpdateEventCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new()
                {
                    DefinitionId = data.DefinitionId,
                    ExpectedConcurrencyStamp = before.ConcurrencyStamp,
                    DefinitionDto = JsonSerializer.Deserialize<UpdateEventCustomPropertyDefinitionDto>(invalid, options)!
                }, default);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(tracked!.Description).IsEqualTo("Retained");
            await Assert.That(tracked.DisplayName).IsEqualTo("own");
            await Assert.That(tracked.RegexPattern).IsEqualTo("^[a-z]+$");
            await Assert.That(tracked.ConcurrencyStamp).IsEqualTo(before.ConcurrencyStamp);
            await Assert.That(native.ServiceProvider.GetRequiredService<ExploreDbContext>().Entry(tracked).State)
                .IsEqualTo(EntityState.Unchanged);
        }

        foreach (var (json, description) in new (string Json, string? Description)[]
        {
            ("""{"metadata":{"displayName":"Updated"}}""", "Retained"),
            ("""{"metadata":{"description":null,"displayName":"Updated again"}}""", "Retained"),
            ("""{"metadata":{"description":{"hasValue":true,"value":""}}}""", ""),
            ("""{"metadata":{"description":{"hasValue":true,"value":null}}}""", null),
            ("""{"metadata":{"description":{"hasValue":true,"value":"Restored"}}}""", "Restored")
        })
        {
            var current = await DetailAsync(factory, PlatformDefaults.DefaultTenantId, data.DefinitionId);
            using var patch = new HttpRequestMessage(HttpMethod.Patch, $"{Root}/{data.DefinitionId}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            patch.Headers.TryAddWithoutValidation("If-Match", $"\"{current.ConcurrencyStamp:D}\"");
            using var updated = await client.SendAsync(patch);
            await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var fresh = Scope(factory, PlatformDefaults.DefaultTenantId);
            var stored = await fresh.ServiceProvider.GetRequiredService<IEventCustomPropertyRepository>()
                .GetDefinitionWithDetails(data.DefinitionId);
            await Assert.That(stored!.Description).IsEqualTo(description);
            await Assert.That(stored.MinLength).IsEqualTo(2);
            await Assert.That(stored.RegexPattern).IsEqualTo("^[a-z]+$");
        }
    }

    [Test]
    public async Task PatternCandidateCreate_RejectsNullOptionsAndUnsafeSyntaxWithoutRows()
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        foreach (var fragment in new[]
        {
            "\"options\":null",
            "\"propertyType\":\"Option\",\"options\":[null]",
            "\"regexPattern\":\"[\"",
            "\"regexPattern\":\"(?=private-pattern-canary)\"",
            "\"minLength\":5,\"maxLength\":4",
            "\"regexPattern\":\"^[a-z]+$\",\"defaultTextValue\":\"PRIVATE-VALUE-CANARY\""
        })
        {
            var json = $$"""{"eventId":"{{data.EventId}}","namespace":"tenant.community","key":"rejected","displayName":"Rejected",{{fragment}}}""";
            using var rejected = await client.PostAsync(Root, new StringContent(json, Encoding.UTF8, "application/json"));
            await ProblemAsync(rejected, HttpStatusCode.BadRequest, "validation_failed", "eventCustomPropertyDefinition");
            var failure = await rejected.Content.ReadAsStringAsync();
            await Assert.That(failure).DoesNotContain("private-pattern-canary");
            await Assert.That(failure).DoesNotContain("PRIVATE-VALUE-CANARY");
            using var fresh = Scope(factory, PlatformDefaults.DefaultTenantId);
            var stored = await fresh.ServiceProvider.GetRequiredService<IEventCustomPropertyRepository>()
                .GetDefinitionsForEventPaged(data.EventId, 1, 20);
            await Assert.That(stored.TotalCount).IsEqualTo(3);
        }
    }

    [Test]
    [Arguments(false, "^[a-z]+$", "PRIVATE-VALUE-CANARY")]
    [Arguments(true, "^[a-z]+$", "PRIVATE-VALUE-CANARY")]
    [Arguments(false, "^[a-z]+$", "PVT123")]
    [Arguments(true, "^[a-z]+$", "PVT123")]
    [Arguments(false, "[", "safe")]
    [Arguments(true, "[", "safe")]
    [Arguments(false, "(?=private-pattern-canary)", "safe")]
    [Arguments(true, "(?=private-pattern-canary)", "safe")]
    [Arguments(false, "[", "overlength-private-value-canary")]
    [Arguments(true, "[", "overlength-private-value-canary")]
    public async Task BoundedPatternValues_RejectHistoricalUnsafePatternsBeforeReplacingRows(bool multiple, string pattern, string incoming)
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        using var initial = await client.PutAsJsonAsync($"{Root}/value", ValueDto(data, "retained"));
        await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (var seed = Scope(factory, PlatformDefaults.DefaultTenantId))
        {
            var db = seed.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var row = await db.EventCustomPropertyDefinitions.SingleAsync(row => row.Id == data.DefinitionId);
            row.IsMulti = multiple;
            row.RegexPattern = pattern;
            row.MaxLength = 8;
            await db.SaveChangesAsync();
        }
        using var rejected = multiple
            ? await client.PutAsJsonAsync($"{Root}/values", new SetEventCustomPropertyMultiValuesDto
            {
                DefinitionId = data.DefinitionId,
                EventId = data.EventId,
                Values = [ValueDto(data, "safe"), ValueDto(data, incoming)]
            })
            : await client.PutAsJsonAsync($"{Root}/value", ValueDto(data, incoming));
        await ProblemAsync(rejected, HttpStatusCode.BadRequest, "validation_failed", "eventCustomPropertyValue");
        var failure = await rejected.Content.ReadAsStringAsync();
        await Assert.That(failure).DoesNotContain("private-pattern-canary");
        await Assert.That(failure).DoesNotContain(incoming);
        await Assert.That((await ValuesAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId)).Single().TextValue)
            .IsEqualTo("retained");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BoundedPatternValues_AcceptSupportedPatternAtValueLengthBoundary(bool multiple)
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        using (var seed = Scope(factory, PlatformDefaults.DefaultTenantId))
        {
            var db = seed.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var row = await db.EventCustomPropertyDefinitions.SingleAsync(row => row.Id == data.DefinitionId);
            row.IsMulti = multiple;
            row.MinLength = 4;
            row.MaxLength = 4;
            row.RegexPattern = "^[a-z]+$";
            await db.SaveChangesAsync();
        }
        using var accepted = multiple
            ? await client.PutAsJsonAsync($"{Root}/values", new SetEventCustomPropertyMultiValuesDto
            {
                DefinitionId = data.DefinitionId,
                EventId = data.EventId,
                Values = [ValueDto(data, "safe")]
            })
            : await client.PutAsJsonAsync($"{Root}/value", ValueDto(data, "safe"));
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await ValuesAsync(factory, PlatformDefaults.DefaultTenantId, data.EventId)).Single().TextValue)
            .IsEqualTo("safe");
    }
}
