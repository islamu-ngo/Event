namespace Explore.Domain.Enums;

public enum SecretSourceType
{
    /// <summary>Value lives in Infisical; DB stores environment + path + key reference.</summary>
    Infisical = 0,

    /// <summary>Value lives in an environment variable; DB stores the variable name.</summary>
    EnvironmentVariable = 1,
}
