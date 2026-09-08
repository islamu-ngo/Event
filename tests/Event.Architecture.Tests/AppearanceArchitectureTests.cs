namespace Event.Architecture.Tests;

using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Appearance;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using TUnit.Assertions;
using TUnit.Core;

public class AppearanceArchitectureTests
{
    [Test]
    public async Task AppearanceThemeMode_Contains_All_Required_Values()
    {
        var enumValues = Enum.GetValues<AppearanceThemeMode>();

        await Assert.That(enumValues).Contains(AppearanceThemeMode.Light);
        await Assert.That(enumValues).Contains(AppearanceThemeMode.Dark);
        await Assert.That(enumValues).Contains(AppearanceThemeMode.System);
        await Assert.That(enumValues).Contains(AppearanceThemeMode.LightHighContrast);
        await Assert.That(enumValues).Contains(AppearanceThemeMode.DarkHighContrast);
        await Assert.That(enumValues).Contains(AppearanceThemeMode.Custom);
        await Assert.That(enumValues.Length).IsEqualTo(6);
    }

    [Test]
    public async Task AppearanceResolutionSource_Contains_All_Fallback_Levels()
    {
        var enumValues = Enum.GetValues<AppearanceResolutionSource>();

        await Assert.That(enumValues.Length).IsEqualTo(6);
        await Assert.That(enumValues).Contains(AppearanceResolutionSource.UserTenantProfile);
        await Assert.That(enumValues).Contains(AppearanceResolutionSource.EmergencyFallback);
    }

    [Test]
    public async Task AppearanceThemeOrigin_Contains_All_Origin_Types()
    {
        var enumValues = Enum.GetValues<AppearanceThemeOrigin>();

        await Assert.That(enumValues).Contains(AppearanceThemeOrigin.SystemPreset);
        await Assert.That(enumValues).Contains(AppearanceThemeOrigin.TenantPreset);
        await Assert.That(enumValues).Contains(AppearanceThemeOrigin.UserCustom);
        await Assert.That(enumValues).Contains(AppearanceThemeOrigin.Fallback);
    }

    [Test]
    public async Task IAppearanceResolutionService_Has_All_Required_Methods()
    {
        var interfaceType = typeof(IAppearanceResolutionService);
        var methodNames = interfaceType.GetMethods().Select(m => m.Name).ToList();

        await Assert.That(methodNames).Contains("ResolveForCurrentUserAsync");
        await Assert.That(methodNames).Contains("GetAvailablePresetsAsync");
        await Assert.That(methodNames).Contains("GetUserProfilesAsync");
        await Assert.That(methodNames).Contains("ClonePresetAsync");
        await Assert.That(methodNames).Contains("CreateCustomProfileAsync");
        await Assert.That(methodNames).Contains("SetActiveProfileAsync");
        await Assert.That(methodNames).Contains("SetThemeModeAsync");
        await Assert.That(methodNames).Contains("UpdateProfileAsync");
        await Assert.That(methodNames).Contains("GeneratePalette");
        await Assert.That(methodNames).Contains("ArchiveProfileAsync");
        await Assert.That(methodNames).Contains("DuplicateProfileAsync");
    }

    [Test]
    public async Task UserAppearanceController_Has_Archive_And_Duplicate_Endpoints()
    {
        var controllerType = typeof(UserAppearanceController);
        var methodNames = controllerType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(m => m.Name).ToList();

        await Assert.That(methodNames).Contains("ArchiveProfile");
        await Assert.That(methodNames).Contains("DuplicateProfile");
    }

    [Test]
    public async Task ResolvedAppearanceDto_Has_All_Required_Properties()
    {
        var dtoType = typeof(ResolvedAppearanceDto);
        var propertyNames = dtoType.GetProperties().Select(p => p.Name).ToList();

        await Assert.That(propertyNames).Contains("ActiveProfileId");
        await Assert.That(propertyNames).Contains("SourcePresetId");
        await Assert.That(propertyNames).Contains("SourcePresetKey");
        await Assert.That(propertyNames).Contains("ResolutionSource");
        await Assert.That(propertyNames).Contains("ThemeMode");
        await Assert.That(propertyNames).Contains("ServerEffectiveDarkMode");
        await Assert.That(propertyNames).Contains("Direction");
        await Assert.That(propertyNames).Contains("Language");
        await Assert.That(propertyNames).Contains("Theme");
        await Assert.That(propertyNames).Contains("Capabilities");
    }

    [Test]
    public async Task RouteNames_Contains_Appearance_Routes()
    {
        var routeNamesType = typeof(RouteNames);
        var fields = routeNamesType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        var fieldNames = fields.Select(f => f.Name).ToList();

        await Assert.That(fieldNames).Contains("GetCurrentUserAppearancePreferences");
        await Assert.That(fieldNames).Contains("GetAvailableThemes");
        await Assert.That(fieldNames).Contains("ArchiveAppearanceProfile");
        await Assert.That(fieldNames).Contains("DuplicateAppearanceProfile");
        await Assert.That(fieldNames).Contains("SetAppearanceThemeMode");
        await Assert.That(fieldNames).Contains("GenerateAppearancePalette");
    }

    [Test]
    public async Task AppearanceEntityUpdatesUsePatchWhileFocusedActionsRetainPut()
    {
        var userController = typeof(UserAppearanceController);
        var themeController = typeof(UiThemeAdminController);

        await Assert.That(userController.GetMethod("UpdatePreferences")!.GetCustomAttributes(typeof(HttpPatchAttribute), false)).IsNotEmpty();
        await Assert.That(userController.GetMethod("UpdateProfile")!.GetCustomAttributes(typeof(HttpPatchAttribute), false)).IsNotEmpty();
        await Assert.That(themeController.GetMethod("Update")!.GetCustomAttributes(typeof(HttpPatchAttribute), false)).IsNotEmpty();
        await Assert.That(userController.GetMethod("SetActiveProfile")!.GetCustomAttributes(typeof(HttpPutAttribute), false)).IsNotEmpty();
        await Assert.That(userController.GetMethod("SetThemeMode")!.GetCustomAttributes(typeof(HttpPutAttribute), false)).IsNotEmpty();
        await Assert.That(userController.GetMethod("ArchiveProfile")!.GetCustomAttributes(typeof(HttpPutAttribute), false)).IsNotEmpty();
    }
}
