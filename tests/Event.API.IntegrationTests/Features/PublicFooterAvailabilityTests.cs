using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Notifications;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class PublicFooterAvailabilityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingDirectoryIdentityPreservesSafeFooterWithoutPublishingPrivateContent(bool privateTenant)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using (var database = factory.CreateDatabase())
        {
            database.BindTenantLease();
            foreach (var pair in new Dictionary<string, string>
            {
                [GovernanceSettingKeys.PublicExperience.VisitorAccessMode] = privateTenant
                    ? "\"AuthenticatedOnly\"" : "\"FullRegistrationAndAuth\"",
                [GovernanceSettingKeys.Footer.DescriptionText] = "\"Tenant footer marker\""
            })
            {
                var row = await database.SystemSettings.SingleOrDefaultAsync(setting => setting.SettingKey == pair.Key);
                if (row is null)
                {
                    var definition = SettingRegistry.Get(pair.Key)!;
                    row = new SystemSetting
                    {
                        Id = Guid.CreateVersion7(),
                        SettingKey = pair.Key,
                        Value = pair.Value,
                        ValueType = definition.ValueType,
                        Category = definition.Category,
                        CreatedAt = DateTime.UtcNow
                    };
                    database.SystemSettings.Add(row);
                }
                row.Value = pair.Value;
            }
            await database.SaveChangesAsync();
        }
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        using var response = await client.GetAsync("/api/footer/config");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("revision").GetString()).IsNotNullOrWhiteSpace();
        await Assert.That(body.RootElement.TryGetProperty("directoryOperator", out var directory)
            && directory.ValueKind != JsonValueKind.Null).IsFalse();
        await Assert.That(body.RootElement.GetProperty("settings").GetProperty("showCookieSettingsLink").GetBoolean()).IsTrue();
        if (privateTenant)
        {
            await Assert.That(body.RootElement.GetProperty("linkGroups").GetArrayLength()).IsEqualTo(0);
            await Assert.That(body.RootElement.GetProperty("settings").GetProperty("descriptionText").GetString()).IsEqualTo(string.Empty);
        }
        else
            await Assert.That(body.RootElement.GetProperty("settings").GetProperty("descriptionText").GetString()).IsEqualTo("Tenant footer marker");

        string? revision = body.RootElement.GetProperty("revision").GetString();
        await using (var database = factory.CreateDatabase())
        {
            database.BindTenantLease();
            var description = await database.SystemSettings.SingleAsync(
                setting => setting.SettingKey == GovernanceSettingKeys.Footer.DescriptionText);
            description.Value = "\"Changed tenant footer marker\"";
            await database.SaveChangesAsync();
        }
        // Fixture storage writes must publish the same post-commit invalidation as setting commands.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var notification = new SettingChangedNotification(GovernanceSettingKeys.Footer.DescriptionText,
                "\"Tenant footer marker\"", "\"Changed tenant footer marker\"",
                SettingSource.SystemDefault, null, null, DateTime.UtcNow);
            foreach (var handler in scope.ServiceProvider.GetServices<INotificationHandler<SettingChangedNotification>>())
                await handler.HandleAsync(notification, CancellationToken.None);
        }
        using var changedResponse = await client.GetAsync("/api/footer/config");
        await Assert.That(changedResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var changed = JsonDocument.Parse(await changedResponse.Content.ReadAsStringAsync());
        string? changedRevision = changed.RootElement.GetProperty("revision").GetString();
        if (privateTenant)
            await Assert.That(changedRevision).IsEqualTo(revision);
        else
            await Assert.That(changedRevision).IsNotEqualTo(revision);
    }
}
