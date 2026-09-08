using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EmailUnsubscribe;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("EmailUnsubscribeContract")]
public sealed class EmailUnsubscribeControllerTests(ContractApiFixture fixture)
{
    [Test]
    public async Task GetWithValidTokenReturnsConfirmationRequired()
    {
        var payload = CreatePayload();
        var token = GenerateToken(payload);

        var response = await fixture.Client.GetAsync(BuildUnsubscribePath(token));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EmailUnsubscribeResponseDto>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Status).IsEqualTo("confirmation_required");
        await Assert.That(body.Category).IsEqualTo(NotificationPreferenceCategories.RegistrationConfirmations);
        await Assert.That(body.RequiresConfirmation).IsTrue();
    }

    [Test]
    public async Task PostWithValidTokenCreatesDisabledPreference()
    {
        var payload = CreatePayload();
        var token = GenerateToken(payload);

        var response = await fixture.Client.PostAsync(BuildUnsubscribePath(token), content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EmailUnsubscribeResponseDto>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Status).IsEqualTo("unsubscribed");

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var preference = await dbContext.UserNotificationPreferences
            .AsNoTracking()
            .SingleAsync(x => x.TenantId == payload.TenantId
                && x.UserId == payload.UserId
                && x.Category == NotificationPreferenceCategories.RegistrationConfirmations);

        await Assert.That(preference.IsEnabled).IsFalse();
        await Assert.That(preference.UpdatedBy).IsEqualTo(payload.UserId);
    }

    [Test]
    public async Task PostWithMalformedTokenReturnsGenericResponseAndDoesNotPersistPreference()
    {
        var beforeCount = await CountPreferencesAsync();

        var response = await fixture.Client.PostAsync(BuildUnsubscribePath("not-a-valid-token"), content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EmailUnsubscribeResponseDto>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.Status).IsEqualTo("unsubscribed");
        await Assert.That(body.Category).IsNull();
        await Assert.That(body.RequiresConfirmation).IsFalse();

        var afterCount = await CountPreferencesAsync();
        await Assert.That(afterCount).IsEqualTo(beforeCount);
    }

    [Test]
    public async Task EndpointsAllowAnonymousAndPostUsesGlobalRateLimitPolicy()
    {
        var getMethod = typeof(EmailUnsubscribeController).GetMethod(nameof(EmailUnsubscribeController.Get));
        var postMethod = typeof(EmailUnsubscribeController).GetMethod(nameof(EmailUnsubscribeController.Post));

        await Assert.That(getMethod).IsNotNull();
        await Assert.That(postMethod).IsNotNull();
        ArgumentNullException.ThrowIfNull(getMethod);
        ArgumentNullException.ThrowIfNull(postMethod);

        await Assert.That(getMethod.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();
        await Assert.That(postMethod.GetCustomAttribute<AllowAnonymousAttribute>()).IsNotNull();

        var rateLimit = postMethod.GetCustomAttribute<EnableRateLimitingAttribute>();
        await Assert.That(rateLimit).IsNotNull();
        await Assert.That(rateLimit!.PolicyName).IsEqualTo(RateLimitingExtensions.GlobalPolicy);
    }

    private static EmailUnsubscribeTokenPayload CreatePayload()
    {
        return new EmailUnsubscribeTokenPayload(
            PlatformDefaults.DefaultTenantId,
            Guid.NewGuid(),
            NotificationPreferenceCategories.RegistrationConfirmations,
            DateTime.UtcNow);
    }

    private string GenerateToken(EmailUnsubscribeTokenPayload payload)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<IEmailUnsubscribeTokenService>();
        return tokenService.GenerateToken(payload);
    }

    private async Task<int> CountPreferencesAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        return await dbContext.UserNotificationPreferences.CountAsync();
    }

    private static string BuildUnsubscribePath(string token)
    {
        return $"/api/email/unsubscribe?token={Uri.EscapeDataString(token)}";
    }
}
