using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Appearance;
using Explore.Application.Exceptions;
using Explore.Application.Features.Appearance.Requests.Commands;
using Explore.Application.Features.Appearance.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeAppearanceHttpTests
{
    [Test]
    public async Task Create_NormalizesInputAndReplacesOnlyTheOwningCatalogDefault()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.TenantAdmin.Id);
        using (var rejected = await client.PostAsJsonAsync("/api/admin/ui-themes",
            Input("invalid-default") with { IsDefault = true, IsActive = false }))
        {
            await ProblemAsync(rejected, HttpStatusCode.BadRequest);
        }
        using var created = await client.PostAsJsonAsync("/api/admin/ui-themes",
            Input("  new_theme  ") with { DisplayName = "  New theme  ", IsDefault = true });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var body = await JsonAsync(created);
        await Assert.That(body.GetProperty("success").GetBoolean()).IsTrue();
        var id = body.GetProperty("id").GetGuid();
        await Assert.That(created.Headers.Location?.AbsolutePath).IsEqualTo($"/api/admin/ui-themes/{id}");
        var detail = await DetailAsync(client, id);
        await Assert.That(detail.ThemeKey).IsEqualTo("new_theme");
        await Assert.That(detail.DisplayName).IsEqualTo("New theme");
        await Assert.That(detail.IsPlatformTheme).IsFalse();
        await Assert.That(detail.LightPalette.Primary).IsEqualTo("#AABBCC");
        await Assert.That(detail.IsDefault).IsTrue();
        var catalog = await CatalogAsync(client);
        await Assert.That(catalog.Where(theme => theme.IsDefault).Select(theme => theme.Id)).IsEquivalentTo(new[] { id });
        await Assert.That(catalog.Any(theme => theme.ThemeKey == "invalid-default")).IsFalse();
        using var platformAdmin = Client(factory, data.PlatformAdmin.Id);
        await Assert.That((await DetailAsync(platformAdmin, data.Platform.Id)).IsDefault).IsTrue();
        await Assert.That((await DetailAsync(platformAdmin, data.Foreign.Id)).IsDefault).IsTrue();
        using var duplicate = await client.PostAsJsonAsync("/api/admin/ui-themes", Input("new_theme"));
        await ProblemAsync(duplicate, HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UpdateAndDelete_PreserveDefaultProtectionPartialGroupsAndStaleVersionRejection()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.TenantAdmin.Id);
        var original = await DetailAsync(client, data.Local.Id);
        using (var stale = await client.PatchAsJsonAsync($"/api/admin/ui-themes/{data.Local.Id}",
            new UpdateUiThemeDto { RowVersion = original.RowVersion + 1, Metadata = new() { DisplayName = "Stale overwrite" } }))
        {
            await ProblemAsync(stale, HttpStatusCode.BadRequest);
        }
        using (var unset = await client.PatchAsJsonAsync($"/api/admin/ui-themes/{data.Local.Id}",
            new UpdateUiThemeDto { RowVersion = original.RowVersion, State = new() { IsDefault = false } }))
        {
            await ProblemAsync(unset, HttpStatusCode.BadRequest);
        }
        using (var deletedDefault = await client.DeleteAsync($"/api/admin/ui-themes/{data.Local.Id}"))
        {
            await ProblemAsync(deletedDefault, HttpStatusCode.BadRequest);
        }
        await Assert.That((await DetailAsync(client, data.Local.Id)).DisplayName).IsEqualTo(original.DisplayName);
        using (var promote = await client.PatchAsJsonAsync($"/api/admin/ui-themes/{data.Alternative.Id}",
            new UpdateUiThemeDto { RowVersion = data.Alternative.RowVersion, State = new() { IsDefault = true } }))
        {
            await Assert.That(promote.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        var demoted = await DetailAsync(client, data.Local.Id);
        await Assert.That(demoted.IsDefault).IsFalse();
        using (var updated = await client.PatchAsJsonAsync($"/api/admin/ui-themes/{data.Local.Id}",
            new { rowVersion = demoted.RowVersion, metadata = new { displayName = "  Renamed  ", description = new { hasValue = true, value = (string?)null } } }))
        {
            await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        var changed = await DetailAsync(client, data.Local.Id);
        await Assert.That(changed.DisplayName).IsEqualTo("Renamed");
        await Assert.That(changed.Description).IsNull();
        await Assert.That(changed.LightPalette).IsEqualTo(original.LightPalette);
        await Assert.That(changed.DarkPalette).IsEqualTo(original.DarkPalette);
        await Assert.That(changed.ThemeKey).IsEqualTo(original.ThemeKey);
        using (var deleted = await client.DeleteAsync($"/api/admin/ui-themes/{data.Local.Id}"))
        {
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }
        using var missing = await client.GetAsync($"/api/admin/ui-themes/{data.Local.Id}");
        await ProblemAsync(missing, HttpStatusCode.NotFound);
        using var missingDelete = await client.DeleteAsync($"/api/admin/ui-themes/{data.Local.Id}");
        await ProblemAsync(missingDelete, HttpStatusCode.NotFound);
        using var missingUpdate = await client.PatchAsJsonAsync($"/api/admin/ui-themes/{data.Local.Id}",
            new UpdateUiThemeDto { RowVersion = 1, State = new() { IsActive = false } });
        await ProblemAsync(missingUpdate, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Authority_ComesFromDatabaseGrantsAndPersistedThemeScopeNotClaims()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        var data = await SeedAsync(factory);
        using var anonymous = factory.CreateClient();
        using (var denied = await anonymous.GetAsync("/api/admin/ui-themes"))
        {
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }
        using var forgedAdmin = factory.CreateClient();
        forgedAdmin.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateInstanceAdminHeaderValue(data.User.Id));
        await Assert.That(await CatalogAsync(forgedAdmin)).IsEmpty();
        using (var createDenied = await forgedAdmin.PostAsJsonAsync("/api/admin/ui-themes", Input("denied")))
        {
            await ProblemAsync(createDenied, HttpStatusCode.BadRequest);
        }
        using var tenantAdmin = Client(factory, data.TenantAdmin.Id);
        foreach (var client in new[] { forgedAdmin, tenantAdmin })
        {
            foreach (var theme in new[] { data.Platform, data.Foreign })
            {
                using var hidden = await client.GetAsync($"/api/admin/ui-themes/{theme.Id}");
                await ProblemAsync(hidden, HttpStatusCode.NotFound);
                using var updateDenied = await client.PatchAsJsonAsync($"/api/admin/ui-themes/{theme.Id}",
                    new UpdateUiThemeDto { RowVersion = theme.RowVersion, Metadata = new() { DisplayName = "Denied" } });
                await ProblemAsync(updateDenied, HttpStatusCode.Forbidden);
                using var deleteDenied = await client.DeleteAsync($"/api/admin/ui-themes/{theme.Id}");
                await ProblemAsync(deleteDenied, HttpStatusCode.Forbidden);
            }
        }
        await Assert.That(await CatalogAsync(tenantAdmin, platform: true)).IsEmpty();
        using (var platformCreate = await tenantAdmin.PostAsJsonAsync("/api/admin/ui-themes",
            Input("denied-platform") with { IsPlatformTheme = true }))
        {
            await ProblemAsync(platformCreate, HttpStatusCode.BadRequest);
        }
        using var platformAdmin = Client(factory, data.PlatformAdmin.Id);
        await Assert.That((await DetailAsync(platformAdmin, data.Foreign.Id)).DisplayName).IsEqualTo(data.Foreign.DisplayName);
        await Assert.That((await CatalogAsync(platformAdmin, platform: true)).Select(theme => theme.Id))
            .IsEquivalentTo(new[] { data.Platform.Id });
        using var platformCreated = await platformAdmin.PostAsJsonAsync("/api/admin/ui-themes",
            Input("platform-created") with { IsPlatformTheme = true });
        await Assert.That(platformCreated.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var id = (await JsonAsync(platformCreated)).GetProperty("id").GetGuid();
        await Assert.That((await DetailAsync(platformAdmin, id)).IsPlatformTheme).IsTrue();
        await Assert.That((await DetailAsync(platformAdmin, id)).IsDefault).IsFalse();
    }

    [Test]
    public async Task ScopedNativeReads_FilterAvailableThemesWithoutProvisioningDefaultsAndEnforceAuthority()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        var data = await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var available = await services.GetRequiredService<IQueryHandler<GetAvailableThemesQuery, IReadOnlyList<AvailableThemeDto>>>()
            .QueryAsync(new(), default);
        await Assert.That(available.Select(theme => theme.Id))
            .IsEquivalentTo(new[] { data.Local.Id, data.Alternative.Id, data.Platform.Id });
        await Assert.That(available[0].Id).IsEqualTo(data.Local.Id);
        var catalog = services.GetRequiredService<IQueryHandler<GetUiThemeCatalogQuery, IReadOnlyList<UiThemeListItemDto>>>();
        await Assert.That(await catalog.QueryAsync(new(), default)).IsEmpty();
        await Assert.That(await services.GetRequiredService<IQueryHandler<GetUiThemeDetailsQuery, UiThemeDetailsDto?>>()
            .QueryAsync(new(data.Local.Id), default)).IsNull();
        var create = await services.GetRequiredService<ICommandHandler<CreateUiThemeCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { UiThemeDto = Input("no-authority") }, default);
        await Assert.That(create.IsSuccess).IsFalse();
        await Assert.That(create.Errors).IsNotEmpty();
        var update = await services.GetRequiredService<ICommandHandler<UpdateUiThemeCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { Id = data.Local.Id, UiThemeDto = new() { RowVersion = 1, State = new() { IsDefault = false } } }, default);
        await Assert.That(update.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await Assert.That(async () => await services.GetRequiredService<ICommandHandler<DeleteUiThemeCommand, bool>>()
            .ExecuteAsync(new(data.Local.Id), default)).Throws<AuthorizationException>();
        var preferences = await services.GetRequiredService<ICommandHandler<UpdateCurrentUserAppearancePreferencesCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { Preferences = new() { Localization = new() { Language = "fr" } } }, default);
        await Assert.That(preferences.IsSuccess).IsFalse();
        var anonymousPreferences = await services.GetRequiredService<IQueryHandler<GetCurrentUserAppearancePreferencesQuery, UserAppearancePreferencesDto>>()
            .QueryAsync(new(), default);
        await Assert.That(anonymousPreferences).IsEqualTo(new UserAppearancePreferencesDto());
        using var admin = Client(factory, data.TenantAdmin.Id);
        await Assert.That((await CatalogAsync(admin)).Select(theme => theme.Id))
            .IsEquivalentTo(new[] { data.Local.Id, data.Alternative.Id, data.Inactive.Id });
        await Assert.That((await CatalogAsync(admin, activeOnly: true)).Select(theme => theme.Id))
            .IsEquivalentTo(new[] { data.Local.Id, data.Alternative.Id });
        await Assert.That((await DetailAsync(admin, data.Local.Id)).IsDefault).IsTrue();
    }

    [Test]
    public async Task PreferencePatch_BindsCurrentUserInvalidatesCacheAndRemovesParentEquivalentOverrides()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.User.Id);
        var before = await PreferencesAsync(factory, data.User.Id);
        await Assert.That(before.Language).IsEqualTo("en");
        await Assert.That(before.Direction).IsEqualTo("ltr");
        using (var invalid = await client.PatchAsJsonAsync("/api/user/appearance", new { localization = new { language = "unsupported" } }))
        {
            await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        }
        using (var forged = await client.PatchAsJsonAsync("/api/user/appearance", new
        {
            userId = data.TenantAdmin.Id,
            tenantId = data.Foreign.TenantId,
            localization = new { language = "fr", direction = "rtl" },
            themeMode = "dark",
            activeProfileId = Guid.CreateVersion7()
        }))
        {
            await ProblemAsync(forged, HttpStatusCode.BadRequest);
        }
        await Assert.That(await PreferencesAsync(factory, data.User.Id)).IsEqualTo(before);
        using (var updated = await client.PatchAsJsonAsync("/api/user/appearance",
            new UpdateUserAppearancePreferencesDto { Localization = new() { Language = "fr", Direction = "rtl" } }))
        {
            await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var body = await JsonAsync(updated);
            await Assert.That(body.GetProperty("success").GetBoolean()).IsTrue();
            await Assert.That(body.GetProperty("id").GetGuid()).IsEqualTo(data.User.Id);
        }
        var changed = await PreferencesAsync(factory, data.User.Id);
        await Assert.That(changed.Language).IsEqualTo("fr");
        await Assert.That(changed.Direction).IsEqualTo("rtl");
        await Assert.That(changed.ThemeMode).IsEqualTo(before.ThemeMode);
        await Assert.That(changed.DefaultThemeId).IsEqualTo(before.DefaultThemeId);
        await Assert.That((await PreferencesAsync(factory, data.TenantAdmin.Id)).Language).IsEqualTo("en");
        await Assert.That((await PreferencesAsync(factory, data.User.Id, data.Foreign.TenantId)).Language).IsEqualTo("en");
        using (var inherited = await client.PatchAsJsonAsync("/api/user/appearance", new { localization = new { language = "en" } }))
        {
            await Assert.That(inherited.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        var restored = await PreferencesAsync(factory, data.User.Id);
        await Assert.That(restored.Language).IsEqualTo("en");
        await Assert.That(restored.Direction).IsEqualTo("rtl");
        using var scope = factory.Services.CreateScope();
        var setting = await scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>()
            .ResolveWithMetadataAsync(GovernanceSettingKeys.Appearance.Language,
                new SettingContext(TenantId: PlatformDefaults.DefaultTenantId, UserId: data.User.Id));
        await Assert.That(setting!.Source).IsEqualTo(SettingSource.TenantOverride);
        using var resolved = await client.GetAsync("/api/user/appearance");
        await Assert.That(resolved.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await JsonAsync(resolved)).TryGetProperty("resolutionSource", out _)).IsTrue();
    }

    [Test]
    public async Task ErasureFence_RejectsPreferenceMutationWithoutChangingEffectiveValues()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        var data = await SeedAsync(factory);
        var before = await PreferencesAsync(factory, data.User.Id);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var intent = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
                data.User.Id, PrivacyErasureReasonCode.AccountDeletion, 1, now, now);
            db.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(intent, 1, SHA256.HashData([1]), now.AddHours(1), now));
            await db.SaveChangesAsync();
        }
        using var client = Client(factory, data.User.Id);
        foreach (var language in new[] { "fr", "unsupported" })
        {
            using var rejected = await client.PatchAsJsonAsync("/api/user/appearance", new { localization = new { language } });
            await ProblemAsync(rejected, HttpStatusCode.BadRequest);
        }
        await Assert.That(await PreferencesAsync(factory, data.User.Id)).IsEqualTo(before);
    }

    private static async Task<SeedData> SeedAsync(AuthenticatedWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var status = await db.TenantStatuses.SingleAsync(item => item.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant { Id = PlatformDefaults.DefaultTenantId, Slug = "appearance", FullName = "Appearance", TenantStatusId = status.Id, TenantStatus = status };
        var foreignTenant = new Tenant { Id = Guid.CreateVersion7(), Slug = "foreign-appearance", FullName = "Foreign", TenantStatusId = status.Id, TenantStatus = status };
        var user = NewUser("member");
        var tenantAdmin = NewUser("tenant-admin");
        var platformAdmin = NewUser("platform-admin");
        db.Users.AddRange(user, tenantAdmin, platformAdmin);
        var role = await db.Set<Role>().SingleAsync(item => item.MasterCode == "platform.admin");
        db.PlatformUserRoles.Add(new PlatformUserRole { Id = Guid.CreateVersion7(), UserId = platformAdmin.Id, User = platformAdmin, RoleId = role.Id, Role = role });
        var membership = new TenantUser { Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant, UserId = tenantAdmin.Id, User = tenantAdmin, StatusId = (int)TenantUserStatusEnum.Active };
        db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            TenantUserId = membership.Id,
            TenantUser = membership,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = await db.Set<Role>().SingleAsync(item => item.Id == (int)RoleEnum.TenantAdmin),
            RoleScopeId = (int)RoleScopeEnum.Tenant
        });
        var local = Theme(tenant, "local", isDefault: true);
        var alternative = Theme(tenant, "alternative");
        var inactive = Theme(tenant, "inactive", active: false);
        var platform = Theme(null, "platform", isDefault: true);
        var foreign = Theme(foreignTenant, "foreign", isDefault: true);
        db.UiThemes.AddRange(local, alternative, inactive, platform, foreign);
        db.Set<TenantSetting>().AddRange(
            new TenantSetting { TenantId = tenant.Id, Tenant = tenant, SettingKey = GovernanceSettingKeys.Appearance.Language, Value = "\"en\"" },
            new TenantSetting { TenantId = tenant.Id, Tenant = tenant, SettingKey = GovernanceSettingKeys.Appearance.Direction, Value = "\"ltr\"" });
        await db.SaveChangesAsync();
        return new(user, tenantAdmin, platformAdmin, local, alternative, inactive, platform, foreign);
    }

    private static User NewUser(string name) => new()
    {
        Id = Guid.CreateVersion7(),
        Pii = new() { Email = $"{name}@example.test", FirstName = name, LastName = "User" }
    };

    private static UiTheme Theme(Tenant? tenant, string key, bool isDefault = false, bool active = true) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenant?.Id,
        Tenant = tenant,
        ThemeKey = key,
        DisplayName = key,
        Description = "Original description",
        IsDefault = isDefault,
        IsActive = active,
        RowVersion = 1,
        LightPalette = EmergencyFallbackPalettes.FallbackLight,
        DarkPalette = EmergencyFallbackPalettes.FallbackDark
    };

    private static CreateUiThemeDto Input(string key) => new()
    {
        ThemeKey = key,
        DisplayName = key,
        LightPalette = Palette(),
        DarkPalette = Palette()
    };

    private static UiThemePaletteDto Palette() => new()
    {
        Primary = "#aabbcc",
        Secondary = "#112233",
        Background = "#FFFFFF",
        Surface = "#FFFFFF",
        AppbarBackground = "#112233",
        AppbarText = "#FFFFFF",
        DrawerBackground = "#FFFFFF",
        DrawerText = "#112233",
        DrawerIcon = "#112233",
        TextPrimary = "#112233",
        TextSecondary = "#112233",
        Info = "#112233",
        Success = "#112233",
        Warning = "#112233",
        Error = "#112233",
        LinesDefault = "#112233",
        Divider = "rgba(0,0,0,0.12)"
    };

    private static HttpClient Client(AuthenticatedWebApplicationFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId));
        return client;
    }

    private static async Task<UiThemeDetailsDto> DetailAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"/api/admin/ui-themes/{id}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<UiThemeDetailsDto>())!;
    }

    private static async Task<IReadOnlyList<UiThemeListItemDto>> CatalogAsync(HttpClient client, bool platform = false, bool activeOnly = false)
    {
        using var response = await client.GetAsync($"/api/admin/ui-themes?isPlatformCatalog={platform}&activeOnly={activeOnly}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<UiThemeListItemDto>>())!;
    }

    private static async Task<UserAppearancePreferencesDto> PreferencesAsync(AuthenticatedWebApplicationFactory factory, Guid userId, Guid? tenantId = null)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenantId ?? PlatformDefaults.DefaultTenantId);
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "Test")) };
        try
        {
            return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetCurrentUserAppearancePreferencesQuery, UserAppearancePreferencesDto>>()
                .QueryAsync(new(), default);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        await Assert.That((await JsonAsync(response)).GetProperty("status").GetInt32()).IsEqualTo((int)expected);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private sealed record SeedData(User User, User TenantAdmin, User PlatformAdmin, UiTheme Local, UiTheme Alternative, UiTheme Inactive, UiTheme Platform, UiTheme Foreign);
}
