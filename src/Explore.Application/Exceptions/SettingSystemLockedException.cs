namespace Explore.Application.Exceptions;

public sealed class SettingSystemLockedException(string settingKey)
    : InvalidOperationException($"Setting '{settingKey}' is locked at Instance scope.")
{
    public const string Code = "setting_system_locked";
}
