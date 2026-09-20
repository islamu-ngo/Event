using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Controllers;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Category;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed class RecordTrustBoundaryTests
{
    private static readonly (Type Controller, string Action)[] AffectedWrites =
    [
        (typeof(CategoryController), nameof(CategoryController.Create)),
        (typeof(TagController), nameof(TagController.Create)),
        (typeof(LocationController), nameof(LocationController.Create)),
        (typeof(EventSessionController), nameof(EventSessionController.Create)),
        (typeof(EventSessionAgendaItemController), nameof(EventSessionAgendaItemController.Create)),
        (typeof(EventSessionLanguageController), nameof(EventSessionLanguageController.Create)),
        (typeof(EventSessionSpeakerController), nameof(EventSessionSpeakerController.Create)),
        (typeof(EventLifecycleController), nameof(EventLifecycleController.Import)),
    ];

    [Test]
    public async Task SaveTenantOnboardingStepDto_CopiesCompletedStepsAndPreservesArrayJson()
    {
        var source = new List<string> { "welcome", "branding" };
        var dto = new TenantOnboardingController.SaveTenantOnboardingStepDto(2, 4, source);

        source[0] = "forged";
        source.Clear();

        await Assert.That(dto.CompletedSteps).IsEquivalentTo(["welcome", "branding"]);
        Assert.Throws<NotSupportedException>(() => ((ICollection<string>)dto.CompletedSteps).Clear());

        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var json = System.Text.Json.JsonSerializer.Serialize(dto, options);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<TenantOnboardingController.SaveTenantOnboardingStepDto>(json, options);

        await Assert.That(json).Contains("\"completedSteps\":[\"welcome\",\"branding\"]");
        await Assert.That(roundTripped).IsNotNull();
        await Assert.That(roundTripped!.CompletedSteps).IsEquivalentTo(["welcome", "branding"]);
    }

    [Test]
    public async Task CreateCategory_ForgedBodyTenantIdFailsClosedBeforeDispatch()
    {
        var forgedTenantId = Guid.CreateVersion7();
        var authenticatedUserId = Guid.CreateVersion7();
        var categoryChecks = new ConcurrentQueue<AuthorizationRequest>();
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider
            {
                CheckPredicate = check =>
                {
                    if (check.ResourceKind == ResourceKinds.Category)
                    {
                        categoryChecks.Enqueue(check);
                    }
                    return true;
                }
            }
        };
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var categoryCount = await db.Categories.IgnoreQueryFilters().CountAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/category")
        {
            Content = JsonContent.Create(new
            {
                masterCode = "TRUST_BOUNDARY",
                fullName = "Trust Boundary",
                tenantId = forgedTenantId
            })
        };
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(authenticatedUserId));

        using var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest, "Validation failed");
        using var problem = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        await Assert.That(problem.RootElement.GetProperty("code").GetString()).IsEqualTo("validation_failed");
        await Assert.That(problem.RootElement.TryGetProperty("errors", out var errors)).IsTrue();
        await Assert.That(errors.GetProperty("body")[0].GetString())
            .IsEqualTo("Request body is invalid or contains unsupported fields.");
        var responseBody = await response.Content.ReadAsStringAsync();
        await Assert.That(responseBody).DoesNotContain(forgedTenantId.ToString("D"));
        await Assert.That(responseBody).DoesNotContain(authenticatedUserId.ToString("D"));
        await Assert.That(categoryChecks).IsEmpty();
        await Assert.That(await db.Categories.IgnoreQueryFilters().CountAsync()).IsEqualTo(categoryCount);
        await Assert.That(typeof(CreateCategoryDto).GetProperty("TenantId")).IsNull();
    }

    [Test]
    public async Task AffectedWritesRemainAuthorizedWithRfc7807FailureMetadata()
    {
        foreach (var (controller, actionName) in AffectedWrites)
        {
            var action = controller.GetMethod(actionName)!;
            var responses = action.GetCustomAttributes(typeof(ProducesResponseTypeAttribute), inherit: true)
                .Cast<ProducesResponseTypeAttribute>()
                .ToArray();

            await Assert.That(action.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)).IsNotEmpty()
                .Because($"{controller.Name}.{actionName} must remain authenticated.");
            await Assert.That(responses.Any(response => response.StatusCode == StatusCodes.Status400BadRequest)).IsTrue();
            await Assert.That(responses.Any(response => response.StatusCode == StatusCodes.Status401Unauthorized && response.Type == typeof(ProblemDetails))).IsTrue();
            await Assert.That(responses.Any(response => response.StatusCode == StatusCodes.Status403Forbidden && response.Type == typeof(ProblemDetails))).IsTrue();
        }
    }

}
