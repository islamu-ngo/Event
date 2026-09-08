namespace Explore.Blazor.Extensions;

using Explore.Blazor.Services.Preferences;
using Api = Explore.Blazor.Client.Clients;

public static class BffPreferenceEndpoints
{
    public static WebApplication MapPreferenceEndpoints(this WebApplication app)
    {
        app.MapPost("/bff/theme", HandleThemePreference)
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapGet("/bff/theme", HandleGetThemePreferenceAsync)
            .ExcludeFromDescription();

        app.MapPost("/bff/language", HandleLanguagePreference)
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapPost("/bff/direction", HandleDirectionPreference)
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapGet("/bff/ui-themes", HandleGetAvailableThemesAsync)
            .ExcludeFromDescription();

        app.MapGet("/bff/me", HandleGetCurrentUser);

        app.MapGet("/bff/appearance", HandleGetResolvedAppearanceAsync)
            .ExcludeFromDescription();

        app.MapGet("/bff/appearance/presets", HandleGetPresetsAsync)
            .ExcludeFromDescription();

        app.MapGet("/bff/appearance/profiles", HandleGetProfilesAsync)
            .ExcludeFromDescription();

        app.MapPut("/bff/appearance/active-profile", HandleSetActiveProfileAsync)
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapPost("/bff/appearance/profiles/from-preset/{presetId:guid}", HandleClonePresetAsync)
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapPost("/bff/appearance/profiles", HandleCreateProfileAsync)
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapPatch("/bff/appearance/profiles/{profileId:guid}", HandleUpdateProfileAsync)
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapPut("/bff/appearance/mode", HandleSetThemeModeAsync)
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapGet("/bff/appearance/generate-palette", HandleGeneratePaletteAsync)
            .ExcludeFromDescription();

        app.MapPut("/bff/appearance/profiles/{profileId:guid}/archive", HandleArchiveProfileAsync)
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        app.MapPost("/bff/appearance/profiles/{profileId:guid}/duplicate", HandleDuplicateProfileAsync)
            .ValidateAntiforgery()
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<IResult> HandleGetResolvedAppearanceAsync(HttpContext ctx, CancellationToken cancellationToken)
    {
        var fallback = GetPreferenceCookies(ctx).BuildDefaultResolvedAppearance(ctx);

        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            return Results.Ok(fallback);
        }

        try
        {
            return Results.Ok(await GetPreferenceForwarding(ctx).GetAppearanceAsync(cancellationToken));
        }
        catch (Api.ApiException)
        {
            return Results.Ok(fallback);
        }
    }

    private static async Task<IResult> HandleGetPresetsAsync(HttpContext ctx, CancellationToken cancellationToken)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            return Results.Ok(Array.Empty<Api.AvailablePresetDto>());
        }

        return await BffForwardingResults.ApiOrFallbackAsync<ICollection<Api.AvailablePresetDto>>(
            () => GetPreferenceForwarding(ctx).GetPresetsAsync(cancellationToken),
            Array.Empty<Api.AvailablePresetDto>());
    }

    private static async Task<IResult> HandleGetProfilesAsync(HttpContext ctx, CancellationToken cancellationToken)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            return Results.Ok(Array.Empty<Api.UserAppearanceProfileDto>());
        }

        return await BffForwardingResults.ApiOrFallbackAsync<ICollection<Api.UserAppearanceProfileDto>>(
            () => GetPreferenceForwarding(ctx).GetProfilesAsync(cancellationToken),
            Array.Empty<Api.UserAppearanceProfileDto>());
    }

    private static async Task<IResult> HandleSetActiveProfileAsync(HttpContext ctx, Api.SetActiveProfileRequestDto request, CancellationToken cancellationToken)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        if (request.ProfileId is not { } profileId || profileId == Guid.Empty)
        {
            return Results.Problem(
                detail: "Profile ID is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid active profile");
        }

        return await BffForwardingResults.ApiOrProblemAsync(
            () => GetPreferenceForwarding(ctx).SetActiveProfileAsync(profileId, cancellationToken),
            "Could not set active profile.",
            "Active profile update failed");
    }

    private static async Task<IResult> HandleClonePresetAsync(HttpContext ctx, Guid presetId, CancellationToken cancellationToken)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        return await BffForwardingResults.ApiOrProblemAsync(
            () => GetPreferenceForwarding(ctx).ClonePresetAsync(presetId, cancellationToken),
            "Could not clone preset.",
            "Preset clone failed");
    }

    private static async Task<IResult> HandleCreateProfileAsync(HttpContext ctx, Api.CreateCustomProfileRequestDto request, CancellationToken cancellationToken)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        return await BffForwardingResults.ApiOrProblemAsync(
            () => GetPreferenceForwarding(ctx).CreateProfileAsync(request, cancellationToken),
            "Could not create custom profile.",
            "Profile creation failed");
    }

    private static async Task<IResult> HandleUpdateProfileAsync(HttpContext ctx, Guid profileId, Api.UpdateAppearanceProfileRequestDto request, CancellationToken cancellationToken)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        return await BffForwardingResults.ApiOrProblemAsync(
            () => GetPreferenceForwarding(ctx).UpdateProfileAsync(profileId, request, cancellationToken),
            "Could not update profile.",
            "Profile update failed");
    }

    private static async Task<IResult> HandleSetThemeModeAsync(HttpContext ctx, Api.SetThemeModeRequestDto request, CancellationToken cancellationToken)
    {
        var preferenceValidation = GetPreferenceValidation(ctx);
        var mode = preferenceValidation.NormalizeThemeMode(request.ThemeMode);
        if (mode is null)
        {
            return Results.Problem(
                detail: preferenceValidation.ThemeModeValidationMessage,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid theme mode");
        }

        var current = await ReadCurrentPreferencesAsync(ctx, cancellationToken);
        var updated = current with { ThemeMode = mode };

        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            try
            {
                await GetPreferenceForwarding(ctx).SetThemeModeAsync(mode, cancellationToken);
            }
            catch (Api.ApiException ex)
            {
                return BffForwardingResults.Problem(
                    ex,
                    "Authenticated theme mode could not be persisted.",
                    "Theme mode update failed");
            }
        }

        GetPreferenceCookies(ctx).PersistThemeCookie(ctx, mode);
        return Results.Ok(updated);
    }

    private static async Task<IResult> HandleGeneratePaletteAsync(HttpContext ctx, string naturalColor, string brandColor, bool isDark, CancellationToken cancellationToken)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        return await BffForwardingResults.ApiOrProblemAsync(
            () => GetPreferenceForwarding(ctx).GeneratePaletteAsync(naturalColor, brandColor, isDark, cancellationToken),
            "Could not generate palette.",
            "Palette generation failed");
    }

    private static async Task<IResult> HandleArchiveProfileAsync(HttpContext ctx, Guid profileId, CancellationToken cancellationToken)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        return await BffForwardingResults.ApiOrProblemAsync(
            () => GetPreferenceForwarding(ctx).ArchiveProfileAsync(profileId, cancellationToken),
            "Could not archive profile.",
            "Archive profile failed");
    }

    private static async Task<IResult> HandleDuplicateProfileAsync(HttpContext ctx, Guid profileId, CancellationToken cancellationToken)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        return await BffForwardingResults.ApiOrProblemAsync(
            () => GetPreferenceForwarding(ctx).DuplicateProfileAsync(profileId, cancellationToken),
            "Could not duplicate profile.",
            "Duplicate profile failed");
    }

    private static async Task<IResult> HandleThemePreference(HttpContext ctx, CancellationToken cancellationToken)
    {
        var preferenceValidation = GetPreferenceValidation(ctx);
        var themeMode = preferenceValidation.NormalizeThemeMode(ctx.Request.Query["theme"].ToString());

        if (themeMode is null)
        {
            return Results.Problem(
                detail: preferenceValidation.ThemeModeValidationMessage,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid theme preference");
        }

        var current = await ReadCurrentPreferencesAsync(ctx, cancellationToken);
        var updated = current with { ThemeMode = themeMode };

        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            try
            {
                await GetPreferenceForwarding(ctx).SetThemeModeAsync(themeMode, cancellationToken);
            }
            catch (Api.ApiException ex)
            {
                return BffForwardingResults.Problem(
                    ex,
                    "Authenticated theme mode could not be persisted.",
                    "Theme preference update failed");
            }
        }

        GetPreferenceCookies(ctx).PersistThemeCookie(ctx, themeMode);
        return Results.Ok(updated);
    }

    private static async Task<IResult> HandleGetThemePreferenceAsync(HttpContext ctx, CancellationToken cancellationToken)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var preferences = await ReadAuthenticatedAsync(ctx, cancellationToken);
            if (preferences is not null)
            {
                return Results.Ok(preferences);
            }
        }

        return Results.Ok(GetPreferenceCookies(ctx).ReadCookiePreferences(ctx));
    }

    private static async Task<IResult> HandleLanguagePreference(HttpContext ctx, CancellationToken cancellationToken)
    {
        var normalizedLang = GetPreferenceValidation(ctx).NormalizeLanguage(ctx.Request.Query["lang"].ToString());

        if (normalizedLang is null)
        {
            return Results.Problem(
                detail: "Language must be a supported culture code registered in CultureRegistry.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid language preference");
        }

        var current = await ReadCurrentPreferencesAsync(ctx, cancellationToken);
        var updated = current with { Language = normalizedLang };

        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var persistResult = await PersistAuthenticatedAsync(
                ctx,
                direction: null,
                language: normalizedLang,
                "Language preference update failed",
                cancellationToken);
            if (persistResult is { } problem)
            {
                return problem;
            }
        }

        var preferenceCookies = GetPreferenceCookies(ctx);
        preferenceCookies.PersistLanguageCookie(ctx, normalizedLang);
        preferenceCookies.PersistAspNetCoreCultureCookie(ctx, normalizedLang);
        return Results.Ok(updated);
    }

    private static async Task<IResult> HandleDirectionPreference(HttpContext ctx, CancellationToken cancellationToken)
    {
        var direction = GetPreferenceValidation(ctx).NormalizeDirection(ctx.Request.Query["dir"].ToString());

        if (direction is null)
        {
            return Results.Problem(
                detail: "Direction must be 'auto', 'ltr', or 'rtl'.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid direction preference");
        }

        var current = await ReadCurrentPreferencesAsync(ctx, cancellationToken);
        var updated = current with { Direction = direction };

        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var persistResult = await PersistAuthenticatedAsync(
                ctx,
                direction,
                language: null,
                "Direction preference update failed",
                cancellationToken);
            if (persistResult is { } problem)
            {
                return problem;
            }
        }

        GetPreferenceCookies(ctx).PersistDirectionCookie(ctx, direction);
        return Results.Ok(updated);
    }

    private static async Task<IResult> HandleGetAvailableThemesAsync(HttpContext ctx, CancellationToken cancellationToken)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            return Results.Problem(
                detail: "Authentication is required to list available themes.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication required");
        }

        return await BffForwardingResults.ApiOrProblemAsync(
            () => GetPreferenceForwarding(ctx).GetAvailableThemesAsync(cancellationToken),
            "Available themes could not be fetched from the API.",
            "Theme catalog unavailable");
    }

    private static async Task<BffAppearancePreferences> ReadCurrentPreferencesAsync(HttpContext ctx, CancellationToken cancellationToken)
    {
        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var authenticated = await ReadAuthenticatedAsync(ctx, cancellationToken);
            if (authenticated is not null)
            {
                return authenticated;
            }
        }

        return GetPreferenceCookies(ctx).ReadCookiePreferences(ctx);
    }

    private static async Task<BffAppearancePreferences?> ReadAuthenticatedAsync(HttpContext ctx, CancellationToken cancellationToken)
    {
        try
        {
            var response = await GetPreferenceForwarding(ctx).GetAppearanceAsync(cancellationToken);
            return new BffAppearancePreferences(
                response.ThemeMode ?? "system",
                response.Direction ?? "auto",
                response.Language ?? "en",
                response.ActiveProfileId);
        }
        catch (Api.ApiException)
        {
            return null;
        }
    }

    private static async Task<IResult?> PersistAuthenticatedAsync(
        HttpContext ctx,
        string? direction,
        string? language,
        string failureTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            await GetPreferenceForwarding(ctx).PersistLocalizationAsync(direction, language, cancellationToken);
            return null;
        }
        catch (Api.ApiException ex)
        {
            return BffForwardingResults.Problem(
                ex,
                "Authenticated preference could not be persisted.",
                failureTitle);
        }
    }

    private static IResult HandleGetCurrentUser(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            return Results.Problem(
                detail: "Authentication is required to access the current-user BFF endpoint.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication required");
        }

        var safeClaims = new[]
        {
            "preferred_username", "email", "name", "given_name", "family_name", "sub"
        };

        return Results.Ok(new
        {
            Name = ctx.User.Identity?.Name,
            Claims = ctx.User.Claims
                .Where(c => safeClaims.Contains(c.Type, StringComparer.OrdinalIgnoreCase))
                .Select(c => new { c.Type, c.Value })
        });
    }

    private static IBffPreferenceCookieService GetPreferenceCookies(HttpContext ctx)
    {
        return ctx.RequestServices.GetRequiredService<IBffPreferenceCookieService>();
    }

    private static IBffPreferenceValidationService GetPreferenceValidation(HttpContext ctx)
    {
        return ctx.RequestServices.GetRequiredService<IBffPreferenceValidationService>();
    }

    private static IBffPreferenceForwardingService GetPreferenceForwarding(HttpContext ctx)
    {
        return ctx.RequestServices.GetRequiredService<IBffPreferenceForwardingService>();
    }
}
