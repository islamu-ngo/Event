using System.Net;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class CustomPropertyDefinitionPrerequisiteTests
{
    [Test]
    public async Task PatternCandidatePatch_RejectsWithoutMutation_AndRoundTripsDescriptionPresence()
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(data.UserId, PlatformDefaults.DefaultTenantId));
        Guid stamp;
        using (var seed = TenantScope(factory, PlatformDefaults.DefaultTenantId))
        {
            var db = seed.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var row = await db.CustomPropertyDefinitions.SingleAsync(row => row.Id == data.OwnDefinitionId);
            row.Description = "Retained";
            row.MinLength = 2;
            row.MaxLength = 8;
            row.RegexPattern = "^[a-z]+$";
            row.DefaultTextValue = "safe";
            await db.SaveChangesAsync();
            stamp = row.ConcurrencyStamp;
        }
        foreach (var invalid in new[]
        {
            """{"metadata":{"description":{"hasValue":true,"value":null},"displayName":"Changed"},"validation":{"regexPattern":{"hasValue":true,"value":"(?=private-pattern-canary)"}}}""",
            """{"metadata":{"description":{"hasValue":true,"value":null}},"validation":{"maxLength":{"hasValue":true,"value":1}}}""",
            """{"metadata":{"description":{"hasValue":true,"value":null}},"validation":{"defaultTextValue":{"hasValue":true,"value":"PRIVATE-VALUE-CANARY"}}}""",
            """{"metadata":{"description":{"hasValue":true,"value":null}},"options":{"items":null}}""",
            """{"metadata":{"description":{"hasValue":true,"value":null}},"options":{"items":[null]}}"""
        })
        {
            using var patch = new HttpRequestMessage(HttpMethod.Patch, $"{Root}/{data.OwnDefinitionId}")
            {
                Content = new StringContent(invalid, Encoding.UTF8, "application/json")
            };
            patch.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
            using var rejected = await client.SendAsync(patch);
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            var failure = await rejected.Content.ReadAsStringAsync();
            using var problem = JsonDocument.Parse(failure);
            await Assert.That(problem.RootElement.GetProperty("code").GetString()).IsEqualTo("validation_failed");
            await Assert.That(failure).DoesNotContain("private-pattern-canary");
            await Assert.That(failure).DoesNotContain("PRIVATE-VALUE-CANARY");
            using var fresh = TenantScope(factory, PlatformDefaults.DefaultTenantId);
            var stored = await fresh.ServiceProvider.GetRequiredService<ICustomPropertyDefinitionRepository>()
                .GetDefinitionWithDetails(data.OwnDefinitionId);
            await Assert.That(stored!.Description).IsEqualTo("Retained");
            await Assert.That(stored.DisplayName).IsEqualTo("own_definition");
            await Assert.That(stored.MaxLength).IsEqualTo(8);
            await Assert.That(stored.RegexPattern).IsEqualTo("^[a-z]+$");
            await Assert.That(stored.DefaultTextValue).IsEqualTo("safe");
            await Assert.That(stored.ConcurrencyStamp).IsEqualTo(stamp);
            await Assert.That(stored.Options.Count).IsEqualTo(0);

            using var native = TenantScope(factory, PlatformDefaults.DefaultTenantId);
            SetNativePrincipal(native, data.UserId);
            var repository = native.ServiceProvider.GetRequiredService<ICustomPropertyDefinitionRepository>();
            var tracked = await repository.GetTrackedDefinitionWithOptions(data.OwnDefinitionId, default);
            var options = native.ServiceProvider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
            var result = await native.ServiceProvider
                .GetRequiredService<ICommandHandler<UpdateCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new()
                {
                    DefinitionId = data.OwnDefinitionId,
                    ExpectedConcurrencyStamp = stamp,
                    DefinitionDto = JsonSerializer.Deserialize<UpdateCustomPropertyDefinitionDto>(invalid, options)!
                }, default);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(tracked!.Description).IsEqualTo("Retained");
            await Assert.That(tracked.DisplayName).IsEqualTo("own_definition");
            await Assert.That(tracked.RegexPattern).IsEqualTo("^[a-z]+$");
            await Assert.That(tracked.ConcurrencyStamp).IsEqualTo(stamp);
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
            using var patch = new HttpRequestMessage(HttpMethod.Patch, $"{Root}/{data.OwnDefinitionId}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            patch.Headers.TryAddWithoutValidation("If-Match", $"\"{stamp:D}\"");
            using var updated = await client.SendAsync(patch);
            await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var fresh = TenantScope(factory, PlatformDefaults.DefaultTenantId);
            var stored = await fresh.ServiceProvider.GetRequiredService<ICustomPropertyDefinitionRepository>()
                .GetDefinitionWithDetails(data.OwnDefinitionId);
            await Assert.That(stored!.Description).IsEqualTo(description);
            await Assert.That(stored.MinLength).IsEqualTo(2);
            await Assert.That(stored.RegexPattern).IsEqualTo("^[a-z]+$");
            stamp = stored.ConcurrencyStamp;
        }
    }

    [Test]
    public async Task PatternCandidateCreate_RejectsNullOptionsAndUnsafeSyntaxWithoutRows()
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(data.UserId, PlatformDefaults.DefaultTenantId));
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
            var json = """{"entityTypeName":"Organization","namespace":"tenant.community","key":"rejected","displayName":"Rejected",""" +
                fragment + "}";
            using var rejected = await client.PostAsync(Root, new StringContent(json, Encoding.UTF8, "application/json"));
            await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            var failure = await rejected.Content.ReadAsStringAsync();
            using var problem = JsonDocument.Parse(failure);
            await Assert.That(problem.RootElement.GetProperty("code").GetString()).IsEqualTo("validation_failed");
            await Assert.That(failure).DoesNotContain("private-pattern-canary");
            await Assert.That(failure).DoesNotContain("PRIVATE-VALUE-CANARY");
            using var fresh = TenantScope(factory, PlatformDefaults.DefaultTenantId);
            var stored = await fresh.ServiceProvider.GetRequiredService<ICustomPropertyDefinitionRepository>()
                .GetDefinitionsWithDetailsPaged(EntityTypeName.Organization, 1, 20);
            await Assert.That(stored.TotalCount).IsEqualTo(1);
        }
    }
}
