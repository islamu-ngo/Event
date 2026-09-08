namespace Explore.Application.Features.Appearance.Common;

using Explore.Application.DTOs.Appearance;
using Explore.Domain;
using Explore.Domain.ValueObjects;

internal static class UiThemeMapper
{
    internal static UiTheme CreateEntity(CreateUiThemeDto dto, Guid? tenantId) => new()
    {
        TenantId = tenantId,
        ThemeKey = UiThemeInputRules.NormalizeThemeKey(dto.ThemeKey),
        DisplayName = dto.DisplayName.Trim(),
        Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
        IsActive = dto.IsActive,
        IsDefault = dto.IsDefault,
        SortOrder = dto.SortOrder,
        LightPalette = ToPalette(dto.LightPalette),
        DarkPalette = ToPalette(dto.DarkPalette)
    };

    internal static void Apply(UpdateUiThemeDto dto, UiTheme entity)
    {
        if (dto.Metadata is { } metadata)
        {
            if (metadata.ThemeKey is not null)
            {
                entity.ThemeKey = UiThemeInputRules.NormalizeThemeKey(metadata.ThemeKey);
            }

            if (metadata.DisplayName is not null)
            {
                entity.DisplayName = metadata.DisplayName.Trim();
            }

            if (metadata.Description.HasValue)
            {
                entity.Description = string.IsNullOrWhiteSpace(metadata.Description.Value)
                    ? null
                    : metadata.Description.Value.Trim();
            }
        }

        if (dto.State is { } state)
        {
            entity.IsActive = state.IsActive ?? entity.IsActive;
            entity.IsDefault = state.IsDefault ?? entity.IsDefault;
            entity.SortOrder = state.SortOrder ?? entity.SortOrder;
        }

        if (dto.Palettes is { } palettes)
        {
            entity.LightPalette = palettes.Light is null ? entity.LightPalette : ToPalette(palettes.Light);
            entity.DarkPalette = palettes.Dark is null ? entity.DarkPalette : ToPalette(palettes.Dark);
        }
    }

    internal static UiThemeListItemDto ToListItem(UiTheme theme) => new()
    {
        Id = theme.Id,
        ThemeKey = theme.ThemeKey,
        DisplayName = theme.DisplayName,
        Description = theme.Description,
        IsActive = theme.IsActive,
        IsDefault = theme.IsDefault,
        IsPlatformTheme = !theme.TenantId.HasValue,
        SortOrder = theme.SortOrder,
        RowVersion = theme.RowVersion
    };

    internal static UiThemeDetailsDto ToDetails(UiTheme theme) => new()
    {
        Id = theme.Id,
        ThemeKey = theme.ThemeKey,
        DisplayName = theme.DisplayName,
        Description = theme.Description,
        IsActive = theme.IsActive,
        IsDefault = theme.IsDefault,
        IsPlatformTheme = !theme.TenantId.HasValue,
        SortOrder = theme.SortOrder,
        RowVersion = theme.RowVersion,
        LightPalette = ToPaletteDto(theme.LightPalette),
        DarkPalette = ToPaletteDto(theme.DarkPalette)
    };

    private static UiThemePalette ToPalette(UiThemePaletteDto palette) => new()
    {
        Primary = UiThemeInputRules.NormalizeHex(palette.Primary),
        PrimaryContrastText = UiThemeInputRules.NormalizeHex(palette.PrimaryContrastText ?? "#FFFFFF"),
        Secondary = UiThemeInputRules.NormalizeHex(palette.Secondary),
        SecondaryContrastText = UiThemeInputRules.NormalizeHex(palette.SecondaryContrastText ?? "#FFFFFF"),
        Background = UiThemeInputRules.NormalizeHex(palette.Background),
        Surface = UiThemeInputRules.NormalizeHex(palette.Surface),
        AppbarBackground = UiThemeInputRules.NormalizeFlexibleColor(palette.AppbarBackground),
        AppbarText = UiThemeInputRules.NormalizeHex(palette.AppbarText),
        DrawerBackground = UiThemeInputRules.NormalizeFlexibleColor(palette.DrawerBackground),
        DrawerText = UiThemeInputRules.NormalizeHex(palette.DrawerText),
        DrawerIcon = UiThemeInputRules.NormalizeHex(palette.DrawerIcon),
        TextPrimary = UiThemeInputRules.NormalizeHex(palette.TextPrimary),
        TextSecondary = UiThemeInputRules.NormalizeHex(palette.TextSecondary),
        Info = UiThemeInputRules.NormalizeHex(palette.Info),
        Success = UiThemeInputRules.NormalizeHex(palette.Success),
        Warning = UiThemeInputRules.NormalizeHex(palette.Warning),
        Error = UiThemeInputRules.NormalizeHex(palette.Error),
        LinesDefault = UiThemeInputRules.NormalizeHex(palette.LinesDefault),
        Divider = UiThemeInputRules.NormalizeFlexibleColor(palette.Divider)
    };

    internal static UiThemePaletteDto ToPaletteDto(UiThemePalette palette) => new()
    {
        Primary = palette.Primary,
        PrimaryContrastText = palette.PrimaryContrastText,
        Secondary = palette.Secondary,
        SecondaryContrastText = palette.SecondaryContrastText,
        Background = palette.Background,
        Surface = palette.Surface,
        AppbarBackground = palette.AppbarBackground,
        AppbarText = palette.AppbarText,
        DrawerBackground = palette.DrawerBackground,
        DrawerText = palette.DrawerText,
        DrawerIcon = palette.DrawerIcon,
        TextPrimary = palette.TextPrimary,
        TextSecondary = palette.TextSecondary,
        Info = palette.Info,
        Success = palette.Success,
        Warning = palette.Warning,
        Error = palette.Error,
        LinesDefault = palette.LinesDefault,
        Divider = palette.Divider
    };
}
