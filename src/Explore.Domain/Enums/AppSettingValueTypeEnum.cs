// Used for validation and UI rendering in the secret management system.

namespace Explore.Domain.Enums;

/// <summary>
/// Data type hint for an AppSetting value after decryption.
/// Used for validation and UI rendering.
/// </summary>
public enum AppSettingValueTypeEnum
{
    /// <summary>Plain text string.</summary>
    String = 0,

    /// <summary>Integer number.</summary>
    Integer = 1,

    /// <summary>Boolean (true/false).</summary>
    Boolean = 2,

    /// <summary>Decimal number.</summary>
    Decimal = 3,

    /// <summary>JSON object or array.</summary>
    Json = 4,

    /// <summary>Connection string (sensitive).</summary>
    ConnectionString = 5,

    /// <summary>URL.</summary>
    Url = 6,

    /// <summary>Email address.</summary>
    Email = 7,

    /// <summary>Secret/API key (always sensitive).</summary>
    Secret = 8
}
