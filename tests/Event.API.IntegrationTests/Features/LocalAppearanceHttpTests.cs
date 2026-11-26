using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Appearance;
using Explore.Application.DTOs.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Event.Api.IntegrationTests.Features;

public sealed class LocalAppearanceHttpTests
{
    [Test]
    public async Task SignedInLocalMemberCanChangeLanguageWithoutChangingOtherAppearancePreferences()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await JoinTenantAsync(fixture);
        fixture.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await fixture.SignInAsync());
        var before = await ReadAppearanceAsync(fixture);
        await Assert.That(before.Language).IsNotEqualTo("ar");

        using var response = await fixture.Client.PatchAsJsonAsync("/api/user/appearance",
            new UpdateUserAppearancePreferencesDto { Localization = new() { Language = "ar" } },
            LocalIdentityLifecycleHttpFixture.CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(fixture.FailureMetadata);
        var after = await ReadAppearanceAsync(fixture);
        await Assert.That(after.Language).IsEqualTo("ar");
        await Assert.That(after.Direction).IsEqualTo(before.Direction);
        await Assert.That(after.ThemeMode).IsEqualTo(before.ThemeMode);
        await Assert.That(after.ActiveProfileId).IsEqualTo(before.ActiveProfileId);
        await Assert.That(await ReadLanguageProvenanceAsync(fixture)).IsEqualTo(SettingSource.UserPreference);

        using var inherited = await fixture.Client.PatchAsJsonAsync("/api/user/appearance",
            new UpdateUserAppearancePreferencesDto { Localization = new() { Language = before.Language } },
            LocalIdentityLifecycleHttpFixture.CancellationToken);
        await Assert.That(inherited.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(fixture.FailureMetadata);
        var restored = await ReadAppearanceAsync(fixture);
        await Assert.That(restored.Language).IsEqualTo(before.Language);
        await Assert.That(restored.Direction).IsEqualTo(before.Direction);
        await Assert.That(restored.ThemeMode).IsEqualTo(before.ThemeMode);
        await Assert.That(restored.ActiveProfileId).IsEqualTo(before.ActiveProfileId);
        await Assert.That(await ReadLanguageProvenanceAsync(fixture)).IsNotEqualTo(SettingSource.UserPreference);
    }

    [Test]
    [Arguments("ar")]
    [Arguments("en")]
    public async Task LockedParentRejectsLanguageChangesIncludingRemovalCandidate(string language)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await JoinTenantAsync(fixture);
        await using (var database = fixture.Native.CreateDatabase())
        {
            database.BindTenantLease();
            var definition = SettingRegistry.Get(GovernanceSettingKeys.Appearance.Language)!;
            database.SystemSettings.Add(new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = definition.Key,
                Value = "\"en\"",
                SettingValueTypeId = (int)definition.ValueType,
                IsLocked = true,
                Category = definition.Category,
                Description = definition.Description,
                CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            await database.SaveChangesAsync(LocalIdentityLifecycleHttpFixture.CancellationToken);
        }
        fixture.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await fixture.SignInAsync());
        var before = await ReadAppearanceAsync(fixture);

        using var response = await fixture.Client.PatchAsJsonAsync("/api/user/appearance",
            new UpdateUserAppearancePreferencesDto { Localization = new() { Language = language } },
            LocalIdentityLifecycleHttpFixture.CancellationToken);

        await Assert.That(response.IsSuccessStatusCode).IsFalse();
        await Assert.That(fixture.RequestFailure is UnauthorizedAccessException).IsTrue().Because(fixture.FailureMetadata);
        var after = await ReadAppearanceAsync(fixture);
        await Assert.That(after.Language).IsEqualTo(before.Language);
        await Assert.That(after.Direction).IsEqualTo(before.Direction);
        await Assert.That(after.ThemeMode).IsEqualTo(before.ThemeMode);
        await Assert.That(after.ActiveProfileId).IsEqualTo(before.ActiveProfileId);
    }

    [Test]
    [Arguments("userId")]
    [Arguments("tenantId")]
    public async Task LocalizationRejectsBodyAuthorityWithoutChangingCurrentPreferences(string authorityMember)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await JoinTenantAsync(fixture);
        fixture.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await fixture.SignInAsync());
        var before = await ReadAppearanceAsync(fixture);
        var body = new Dictionary<string, object?>
        {
            [authorityMember] = Guid.CreateVersion7(),
            ["localization"] = new { language = "ar" }
        };

        using var response = await fixture.Client.PatchAsJsonAsync("/api/user/appearance", body,
            LocalIdentityLifecycleHttpFixture.CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest).Because(fixture.FailureMetadata);
        var after = await ReadAppearanceAsync(fixture);
        await Assert.That(after.Language).IsEqualTo(before.Language);
        await Assert.That(after.Direction).IsEqualTo(before.Direction);
        await Assert.That(after.ThemeMode).IsEqualTo(before.ThemeMode);
        await Assert.That(after.ActiveProfileId).IsEqualTo(before.ActiveProfileId);
    }

    private static async Task<ResolvedAppearanceDto> ReadAppearanceAsync(
        LocalIdentityLifecycleHttpFixture fixture)
    {
        using var response = await fixture.Client.GetAsync("/api/user/appearance",
            LocalIdentityLifecycleHttpFixture.CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(fixture.FailureMetadata);
        var appearance = await response.Content.ReadFromJsonAsync<ResolvedAppearanceDto>(
            cancellationToken: LocalIdentityLifecycleHttpFixture.CancellationToken);
        await Assert.That(appearance).IsNotNull();
        return appearance!;
    }

    private static async Task<SettingSource> ReadLanguageProvenanceAsync(
        LocalIdentityLifecycleHttpFixture fixture)
    {
        using var response = await fixture.Client.GetAsync("/api/settings/user/Appearance",
            LocalIdentityLifecycleHttpFixture.CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(fixture.FailureMetadata);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        var group = await response.Content.ReadFromJsonAsync<SettingGroupResponseDto>(options,
            LocalIdentityLifecycleHttpFixture.CancellationToken);
        await Assert.That(group).IsNotNull();
        return group!.Settings.Single(setting => setting.Key == GovernanceSettingKeys.Appearance.Language).Source;
    }

    private static async Task JoinTenantAsync(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var database = fixture.Native.CreateDatabase();
        database.BindTenantLease();
        Guid userId = fixture.Binding.LocalSubjectId;
        Guid actorId = await database.Actors.Where(actor => actor.UserId == userId)
            .Select(actor => actor.Id).SingleAsync(LocalIdentityLifecycleHttpFixture.CancellationToken);
        database.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            UserId = userId,
            User = null!,
            ActorId = actorId,
            Actor = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = fixture.Clock.GetUtcNow().UtcDateTime,
            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        await database.SaveChangesAsync(LocalIdentityLifecycleHttpFixture.CancellationToken);
    }
}
