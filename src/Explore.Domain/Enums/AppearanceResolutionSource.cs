namespace Explore.Domain.Enums;

public enum AppearanceResolutionSource
{
    UserTenantProfile,
    UserGlobalProfile,
    TenantDefaultPreset,
    InstanceDefaultPreset,
    SystemPresetFallback,
    EmergencyFallback
}
