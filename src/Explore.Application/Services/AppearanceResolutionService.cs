namespace Explore.Application.Services;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Appearance;
using Explore.Application.DTOs.Appearance.Validators;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using FluentValidation;

public class AppearanceResolutionService : IAppearanceResolutionService
{
    private readonly IUiThemePresetRepository _presetRepository;
    private readonly IUserAppearanceProfileRepository _profileRepository;
    private readonly IUserAppearancePreferenceRepository _preferenceRepository;
    private readonly IUiThemeRepository _uiThemeRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IHierarchicalSettingsResolver _settingsResolver;

    public AppearanceResolutionService(
        IUiThemePresetRepository presetRepository,
        IUserAppearanceProfileRepository profileRepository,
        IUserAppearancePreferenceRepository preferenceRepository,
        IUiThemeRepository uiThemeRepository,
        ITenantContext tenantContext,
        ICurrentUserService currentUserService,
        IHierarchicalSettingsResolver settingsResolver)
    {
        _presetRepository = presetRepository;
        _profileRepository = profileRepository;
        _preferenceRepository = preferenceRepository;
        _uiThemeRepository = uiThemeRepository;
        _tenantContext = tenantContext;
        _currentUserService = currentUserService;
        _settingsResolver = settingsResolver;
    }

    public async Task<ResolvedAppearanceDto> ResolveForCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId;
        var tenantId = _tenantContext.TenantId;

        if (userId is null)
        {
            return CreateEmergencyFallback();
        }

        // 1. Try user's active profile for the current tenant context
        var preference = await _preferenceRepository.GetByUserAndTenantAsync(userId.Value, tenantId);

        if (preference?.ActiveProfile is { } activeProfile && !activeProfile.IsArchived)
        {
            return CreateFromProfile(activeProfile, AppearanceResolutionSource.UserTenantProfile);
        }

        // 2. Try user's global profile (TenantId = null)
        var globalPreference = await _preferenceRepository.GetByUserAndTenantAsync(userId.Value, null);
        if (globalPreference?.ActiveProfile is { } globalProfile && !globalProfile.IsArchived)
        {
            return CreateFromProfile(globalProfile, AppearanceResolutionSource.UserGlobalProfile);
        }

        // 3. Try the configured tenant/instance default preset
        var configuredDefault = await ResolveConfiguredDefaultPresetAsync(tenantId, cancellationToken);
        if (configuredDefault is not null)
        {
            return CreateFromPreset(configuredDefault.Value.Preset, configuredDefault.Value.Source);
        }

        // 4. System fallback: enterprise-blue
        var systemPreset = await _presetRepository.GetByThemeKeyAsync(null, "enterprise-blue");
        if (systemPreset is not null)
        {
            return CreateFromPreset(systemPreset, AppearanceResolutionSource.SystemPresetFallback);
        }

        // 5. Emergency hardcoded fallback
        return CreateEmergencyFallback();
    }

    private async Task<(UiThemePreset Preset, AppearanceResolutionSource Source)?> ResolveConfiguredDefaultPresetAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var setting = await _settingsResolver.ResolveWithMetadataAsync(
            GovernanceSettingKeys.Appearance.DefaultPresetId,
            new SettingContext(TenantId: tenantId),
            cancellationToken);

        if (setting is null
            || !Guid.TryParse(SettingValueSerializer.DeserializeString(setting.Value), out var presetId))
        {
            return null;
        }

        var isTenantSetting = setting.Source is SettingSource.TenantOverride or SettingSource.TenantLocked;
        var preset = await _presetRepository.GetById(presetId);
        if (preset is not { IsActive: true }
            || preset.TenantId.HasValue && (!isTenantSetting || preset.TenantId != tenantId))
        {
            return null;
        }

        var source = isTenantSetting
            ? AppearanceResolutionSource.TenantDefaultPreset
            : AppearanceResolutionSource.InstanceDefaultPreset;

        return (preset, source);
    }

    public async Task<IReadOnlyList<AvailablePresetDto>> GetAvailablePresetsAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var presets = await _presetRepository.GetAvailablePresetsForTenantAsync(tenantId);

        return presets.Select(MapToPresetDto).ToList();
    }

    public async Task<IReadOnlyList<UserAppearanceProfileDto>> GetUserProfilesAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId;
        if (userId is null)
        {
            return Array.Empty<UserAppearanceProfileDto>();
        }

        var tenantId = _tenantContext.TenantId;
        var profiles = await _profileRepository.GetProfilesForUserAsync(userId.Value, tenantId);

        return profiles.Select(MapToProfileDto).ToList();
    }

    public async Task<UserAppearanceProfileDto> ClonePresetAsync(Guid presetId, string? name, bool activate, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException("User not authenticated.");
        var tenantId = _tenantContext.TenantId;

        var preset = await _presetRepository.GetById(presetId)
            ?? throw new KeyNotFoundException($"Preset with ID {presetId} not found.");

        // Check for existing clone to avoid duplicates
        var existingClone = await _profileRepository.GetExistingCloneAsync(userId, tenantId, presetId);
        if (existingClone is not null)
        {
            if (activate)
            {
                await ActivateProfileInternalAsync(userId, tenantId, existingClone.Id);
            }

            return MapToProfileDto(existingClone);
        }

        var profile = new UserAppearanceProfile
        {
            UserId = userId,
            TenantId = tenantId,
            Name = name ?? preset.DisplayName,
            ThemeMode = AppearanceThemeMode.System,
            LightPaletteSnapshot = preset.LightPalette.Normalized(),
            DarkPaletteSnapshot = preset.DarkPalette.Normalized(),
            SourcePresetKey = preset.ThemeKey,
            SourcePresetId = preset.Id,
            SourcePresetSeedVersion = preset.SeedVersion,
            IsUserEditable = true,
            IsDefault = false,
            IsArchived = false,
            ClonedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };

        var created = await _profileRepository.Create(profile);

        if (activate)
        {
            await ActivateProfileInternalAsync(userId, tenantId, created.Id);
        }

        return MapToProfileDto(created);
    }

    public async Task<UserAppearanceProfileDto> CreateCustomProfileAsync(CreateCustomProfileRequestDto request, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException("User not authenticated.");
        var tenantId = _tenantContext.TenantId;

        var lightPalette = AppearancePaletteGenerator.GenerateLightPalette(request.NaturalColor, request.BrandColor);
        var darkPalette = AppearancePaletteGenerator.GenerateDarkPalette(request.NaturalColor, request.BrandColor);

        var profile = new UserAppearanceProfile
        {
            UserId = userId,
            TenantId = tenantId,
            Name = request.Name,
            ThemeMode = Enum.Parse<AppearanceThemeMode>(request.ThemeMode, true),
            LightPaletteSnapshot = MapDtoToPalette(lightPalette),
            DarkPaletteSnapshot = MapDtoToPalette(darkPalette),
            SourcePresetKey = null,
            SourcePresetId = null,
            SourcePresetSeedVersion = null,
            IsUserEditable = true,
            IsDefault = false,
            IsArchived = false,
            ClonedAt = null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };

        var created = await _profileRepository.Create(profile);
        return MapToProfileDto(created);
    }

    public async Task SetActiveProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException("User not authenticated.");
        var tenantId = _tenantContext.TenantId;

        var profile = await _profileRepository.GetById(profileId)
            ?? throw new KeyNotFoundException($"Profile with ID {profileId} not found.");

        if (profile.UserId != userId)
        {
            throw new UnauthorizedAccessException("Cannot activate a profile that does not belong to the current user.");
        }

        if (profile.IsArchived)
        {
            throw new InvalidOperationException("Cannot activate an archived profile.");
        }

        await ActivateProfileInternalAsync(userId, tenantId, profileId);
    }

    public async Task SetThemeModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException("User not authenticated.");
        var tenantId = _tenantContext.TenantId;

        var parsedMode = mode.ToLowerInvariant() switch
        {
            "light" => AppearanceThemeMode.Light,
            "dark" => AppearanceThemeMode.Dark,
            "lighthighcontrast" => AppearanceThemeMode.LightHighContrast,
            "darkhighcontrast" => AppearanceThemeMode.DarkHighContrast,
            "custom" => AppearanceThemeMode.Custom,
            _ => AppearanceThemeMode.System
        };

        var preference = await _preferenceRepository.GetByUserAndTenantAsync(userId, tenantId);

        if (preference is not null)
        {
            preference.ThemeMode = parsedMode;
            await _preferenceRepository.Update(preference);
        }
    }

    public async Task<UserAppearanceProfileDto> UpdateProfileAsync(Guid profileId, UpdateAppearanceProfileRequestDto request, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException("User not authenticated.");

        var validator = new UpdateAppearanceProfileRequestDtoValidator();
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var profile = await _profileRepository.GetById(profileId)
            ?? throw new KeyNotFoundException($"Profile with ID {profileId} not found.");

        if (profile.UserId != userId)
        {
            throw new UnauthorizedAccessException("Cannot update a profile that does not belong to the current user.");
        }

        if (!profile.IsUserEditable)
        {
            throw new InvalidOperationException("This profile is not editable.");
        }

        if (request.Metadata?.Name is not null)
        {
            profile.Name = request.Metadata.Name.Trim();
        }

        if (request.Metadata?.ThemeMode is not null)
        {
            profile.ThemeMode = Enum.Parse<AppearanceThemeMode>(request.Metadata.ThemeMode, true);
        }

        if (request.Palettes?.Light is not null)
        {
            profile.LightPaletteSnapshot = MapDtoToPalette(request.Palettes.Light);
        }

        if (request.Palettes?.Dark is not null)
        {
            profile.DarkPaletteSnapshot = MapDtoToPalette(request.Palettes.Dark);
        }

        profile.UpdatedAt = DateTime.UtcNow;
        profile.UpdatedBy = userId;

        await _profileRepository.Update(profile);
        return MapToProfileDto(profile);
    }

    public UiThemePaletteDto GeneratePalette(string naturalColor, string brandColor, bool isDark)
    {
        return isDark
            ? AppearancePaletteGenerator.GenerateDarkPalette(naturalColor, brandColor)
            : AppearancePaletteGenerator.GenerateLightPalette(naturalColor, brandColor);
    }

    public async Task ArchiveProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException("User not authenticated.");
        var profile = await _profileRepository.GetById(profileId)
            ?? throw new KeyNotFoundException($"Profile with ID {profileId} not found.");

        if (profile.UserId != userId)
        {
            throw new UnauthorizedAccessException("Cannot archive a profile that does not belong to the current user.");
        }

        if (profile.IsDefault)
        {
            throw new InvalidOperationException("Cannot archive the active profile. Set a different profile as active first.");
        }

        profile.IsArchived = true;
        profile.UpdatedAt = DateTime.UtcNow;
        profile.UpdatedBy = userId;
        await _profileRepository.Update(profile);
    }

    public async Task<UserAppearanceProfileDto> DuplicateProfileAsync(Guid profileId, string? name, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException("User not authenticated.");
        var tenantId = _tenantContext.TenantId;

        var source = await _profileRepository.GetById(profileId)
            ?? throw new KeyNotFoundException($"Profile with ID {profileId} not found.");

        if (source.UserId != userId)
        {
            throw new UnauthorizedAccessException("Cannot duplicate a profile that does not belong to the current user.");
        }

        var duplicate = new UserAppearanceProfile
        {
            UserId = userId,
            TenantId = tenantId,
            Name = name ?? $"{source.Name} (Copy)",
            ThemeMode = source.ThemeMode,
            LightPaletteSnapshot = source.LightPaletteSnapshot.Normalized(),
            DarkPaletteSnapshot = source.DarkPaletteSnapshot.Normalized(),
            SourcePresetKey = source.SourcePresetKey,
            SourcePresetId = source.SourcePresetId,
            SourcePresetSeedVersion = source.SourcePresetSeedVersion,
            IsUserEditable = true,
            IsDefault = false,
            IsArchived = false,
            ClonedAt = source.ClonedAt,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };

        var created = await _profileRepository.Create(duplicate);
        return MapToProfileDto(created);
    }

    private async Task ActivateProfileInternalAsync(Guid userId, Guid tenantId, Guid profileId)
    {
        await _profileRepository.ClearDefaultAsync(userId, tenantId, profileId);

        var preference = await _preferenceRepository.GetOrCreateAsync(userId, tenantId, profileId);
        preference.ActiveProfileId = profileId;
        await _preferenceRepository.Update(preference);

        var profile = await _profileRepository.GetById(profileId);
        if (profile is not null)
        {
            profile.IsDefault = true;
            profile.UpdatedAt = DateTime.UtcNow;
            await _profileRepository.Update(profile);
        }
    }

    private ResolvedAppearanceDto CreateFromProfile(UserAppearanceProfile profile, AppearanceResolutionSource source)
    {
        return new ResolvedAppearanceDto
        {
            ActiveProfileId = profile.Id,
            SourcePresetId = profile.SourcePresetId,
            SourcePresetKey = profile.SourcePresetKey,
            ResolutionSource = source.ToString(),
            ThemeMode = profile.ThemeMode.ToString().ToLowerInvariant(),
            ServerEffectiveDarkMode = ResolveEffectiveDarkMode(profile.ThemeMode),
            Direction = "auto",
            Language = "en",
            Theme = new ResolvedThemeDto
            {
                DisplayName = profile.Name,
                LightPalette = AppearanceMapper.ToPaletteDto(profile.LightPaletteSnapshot),
                DarkPalette = AppearanceMapper.ToPaletteDto(profile.DarkPaletteSnapshot),
                IsSnapshot = true,
                IsUserEditable = profile.IsUserEditable,
                Origin = profile.SourcePresetId.HasValue ? AppearanceThemeOrigin.TenantPreset.ToString() : AppearanceThemeOrigin.UserCustom.ToString()
            },
            Capabilities = new AppearanceCapabilitiesDto
            {
                CanEditProfile = profile.IsUserEditable,
                CanCreateCustomProfile = true,
                CanClonePreset = true,
                CanDeleteProfile = !profile.IsDefault
            }
        };
    }

    private ResolvedAppearanceDto CreateFromPreset(UiThemePreset preset, AppearanceResolutionSource source)
    {
        var themeMode = AppearanceThemeMode.System;

        return new ResolvedAppearanceDto
        {
            ActiveProfileId = null,
            SourcePresetId = preset.Id,
            SourcePresetKey = preset.ThemeKey,
            ResolutionSource = source.ToString(),
            ThemeMode = themeMode.ToString().ToLowerInvariant(),
            ServerEffectiveDarkMode = null,
            Direction = "auto",
            Language = "en",
            Theme = new ResolvedThemeDto
            {
                DisplayName = preset.DisplayName,
                LightPalette = AppearanceMapper.ToPaletteDto(preset.LightPalette),
                DarkPalette = AppearanceMapper.ToPaletteDto(preset.DarkPalette),
                IsSnapshot = false,
                IsUserEditable = preset.IsEditable,
                Origin = preset.IsSystem ? AppearanceThemeOrigin.SystemPreset.ToString() : AppearanceThemeOrigin.TenantPreset.ToString()
            },
            Capabilities = new AppearanceCapabilitiesDto
            {
                CanEditProfile = false,
                CanCreateCustomProfile = true,
                CanClonePreset = true,
                CanDeleteProfile = false
            }
        };
    }

    private static ResolvedAppearanceDto CreateEmergencyFallback()
    {
        var fallbackLight = EmergencyFallbackPalettes.FallbackLight;
        var fallbackDark = EmergencyFallbackPalettes.FallbackDark;

        return new ResolvedAppearanceDto
        {
            ActiveProfileId = null,
            SourcePresetId = null,
            SourcePresetKey = "emergency-fallback",
            ResolutionSource = AppearanceResolutionSource.EmergencyFallback.ToString(),
            ThemeMode = "system",
            ServerEffectiveDarkMode = null,
            Direction = "auto",
            Language = "en",
            Theme = new ResolvedThemeDto
            {
                DisplayName = "Enterprise Blue",
                LightPalette = AppearanceMapper.ToPaletteDto(fallbackLight),
                DarkPalette = AppearanceMapper.ToPaletteDto(fallbackDark),
                IsSnapshot = false,
                IsUserEditable = false,
                Origin = AppearanceThemeOrigin.Fallback.ToString()
            },
            Capabilities = new AppearanceCapabilitiesDto
            {
                CanEditProfile = false,
                CanCreateCustomProfile = true,
                CanClonePreset = true,
                CanDeleteProfile = false
            }
        };
    }

    private static bool? ResolveEffectiveDarkMode(AppearanceThemeMode mode) => mode switch
    {
        AppearanceThemeMode.Dark => true,
        AppearanceThemeMode.Light => false,
        AppearanceThemeMode.DarkHighContrast => true,
        AppearanceThemeMode.LightHighContrast => false,
        _ => null
    };

    private static AvailablePresetDto MapToPresetDto(UiThemePreset preset) => new()
    {
        Id = preset.Id,
        ThemeKey = preset.ThemeKey,
        DisplayName = preset.DisplayName,
        Description = preset.Description,
        IsSystem = preset.IsSystem,
        IsEditable = preset.IsEditable,
        IsDefault = false,
        SortOrder = 0,
        LightPalette = AppearanceMapper.ToPaletteDto(preset.LightPalette),
        DarkPalette = AppearanceMapper.ToPaletteDto(preset.DarkPalette),
        DeprecatedAt = preset.DeprecatedAt
    };

    private static UserAppearanceProfileDto MapToProfileDto(UserAppearanceProfile profile) => new()
    {
        Id = profile.Id,
        TenantId = profile.TenantId,
        Name = profile.Name,
        ThemeMode = profile.ThemeMode.ToString().ToLowerInvariant(),
        LightPaletteSnapshot = AppearanceMapper.ToPaletteDto(profile.LightPaletteSnapshot),
        DarkPaletteSnapshot = AppearanceMapper.ToPaletteDto(profile.DarkPaletteSnapshot),
        SourcePresetKey = profile.SourcePresetKey,
        SourcePresetId = profile.SourcePresetId,
        SourcePresetSeedVersion = profile.SourcePresetSeedVersion,
        IsUserEditable = profile.IsUserEditable,
        IsDefault = profile.IsDefault,
        IsArchived = profile.IsArchived,
        ClonedAt = profile.ClonedAt
    };

    private static Domain.ValueObjects.UiThemePalette MapDtoToPalette(UiThemePaletteDto dto) => new()
    {
        Primary = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.Primary),
        PrimaryContrastText = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.PrimaryContrastText),
        Secondary = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.Secondary),
        SecondaryContrastText = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.SecondaryContrastText),
        Background = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.Background),
        Surface = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.Surface),
        AppbarBackground = UiThemeInputRules.NormalizeFlexibleColor(dto.AppbarBackground),
        AppbarText = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.AppbarText),
        DrawerBackground = UiThemeInputRules.NormalizeFlexibleColor(dto.DrawerBackground),
        DrawerText = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.DrawerText),
        DrawerIcon = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.DrawerIcon),
        TextPrimary = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.TextPrimary),
        TextSecondary = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.TextSecondary),
        Info = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.Info),
        Success = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.Success),
        Warning = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.Warning),
        Error = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.Error),
        LinesDefault = Domain.ValueObjects.UiThemePalette.NormalizeHex(dto.LinesDefault),
        Divider = UiThemeInputRules.NormalizeFlexibleColor(dto.Divider)
    };
}
