using System.Globalization;

namespace Event.Testing;

/// <summary>
/// Categorizes container workloads so Testcontainers fixtures enforce bounded host memory and safe reuse semantics.
/// </summary>
public enum ContainerServiceRole
{
    Generic = 0,
    PostgreSql = 1,
    PostGis = 2,
    MySql = 3,
    MariaDb = 4,
    Redis = 5,
    Mailpit = 6,
    Cerbos = 7,
    RabbitMq = 8,
    Keycloak = 9,
    SqlServer = 10,
    Svix = 11,
}

/// <summary>
/// Resolved container memory ceiling, optional JVM heap ceiling, and isolated reuse policy.
/// </summary>
public readonly record struct ContainerResourcePolicy(
    ContainerServiceRole Role,
    long MemoryLimitBytes,
    int? KeycloakHeapLimitMegabytes,
    bool EnableReuse);

/// <summary>
/// Resolves per-service Testcontainers memory ceilings, Keycloak JVM heap limits, and safe opt-in reuse rules.
/// </summary>
public static class ContainerResourceBounds
{
    public const string ContainerMemoryLimitEnvVar = "ISLAMU_CONTAINER_MEMORY_MB";
    public const string KeycloakHeapLimitEnvVar = "ISLAMU_KEYCLOAK_HEAP_MB";
    public const string ContainerReuseEnvVar = "ISLAMU_TEST_CONTAINER_REUSE";

    private const long BytesPerMegabyte = 1024L * 1024L;
    private const int MinimumAllowedMemoryMegabytes = 64;
    private const int DefaultKeycloakHeapMegabytes = 512;
    private const int MinimumKeycloakHeapMegabytes = 128;

    public static int GetDefaultMemoryMegabytes(ContainerServiceRole role) => role switch
    {
        ContainerServiceRole.Redis or ContainerServiceRole.Mailpit => 128,
        ContainerServiceRole.Cerbos or ContainerServiceRole.Svix => 256,
        ContainerServiceRole.RabbitMq or ContainerServiceRole.Generic => 512,
        ContainerServiceRole.MySql or ContainerServiceRole.MariaDb => 768,
        ContainerServiceRole.PostgreSql or ContainerServiceRole.PostGis or ContainerServiceRole.Keycloak => 1024,
        ContainerServiceRole.SqlServer => 2048,
        _ => 512,
    };

    public static long ResolveMemoryLimitBytes(ContainerServiceRole role, Func<string, string?>? getEnv = null)
    {
        int memoryMegabytes = ResolveMemoryMegabytes(role, getEnv ?? Environment.GetEnvironmentVariable);
        return checked(memoryMegabytes * BytesPerMegabyte);
    }

    public static int ResolveKeycloakHeapMegabytes(int containerMemoryMegabytes, Func<string, string?>? getEnv = null)
    {
        Func<string, string?> env = getEnv ?? Environment.GetEnvironmentVariable;
        int? explicitHeap = TryParsePositiveAtLeast(env(KeycloakHeapLimitEnvVar), MinimumKeycloakHeapMegabytes);
        if (explicitHeap is int parsedHeap)
        {
            return parsedHeap;
        }

        int boundedDefault = Math.Max(MinimumKeycloakHeapMegabytes, (containerMemoryMegabytes * 3) / 4);
        return Math.Min(DefaultKeycloakHeapMegabytes, boundedDefault);
    }

    public static bool ShouldEnableReuse(
        bool hasAssuranceOwner,
        bool hasExplicitContainerName,
        Func<string, string?>? getEnv = null)
    {
        // Named containers and assurance-owned runs require strict lifecycle isolation; never reuse them.
        if (hasAssuranceOwner || hasExplicitContainerName)
        {
            return false;
        }

        Func<string, string?> env = getEnv ?? Environment.GetEnvironmentVariable;
        string? raw = env(ContainerReuseEnvVar)?.Trim();
        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "1", StringComparison.Ordinal);
    }

    public static ContainerResourcePolicy Resolve(
        ContainerServiceRole role,
        bool hasAssuranceOwner = false,
        bool hasExplicitContainerName = false,
        Func<string, string?>? getEnv = null)
    {
        Func<string, string?> env = getEnv ?? Environment.GetEnvironmentVariable;
        int memoryMegabytes = ResolveMemoryMegabytes(role, env);
        long memoryBytes = checked(memoryMegabytes * BytesPerMegabyte);
        int? keycloakHeapMegabytes = role == ContainerServiceRole.Keycloak
            ? ResolveKeycloakHeapMegabytes(memoryMegabytes, env)
            : null;
        bool enableReuse = ShouldEnableReuse(hasAssuranceOwner, hasExplicitContainerName, env);

        return new ContainerResourcePolicy(role, memoryBytes, keycloakHeapMegabytes, enableReuse);
    }

    public static string FormatKeycloakJavaOpts(string? existingJavaOpts, int heapMegabytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(heapMegabytes, MinimumKeycloakHeapMegabytes);

        string heapFlags = string.Create(
            CultureInfo.InvariantCulture,
            $"-Xms64m -Xmx{heapMegabytes}m");

        return string.IsNullOrWhiteSpace(existingJavaOpts)
            ? heapFlags
            : $"{existingJavaOpts.Trim()} {heapFlags}";
    }

    private static int ResolveMemoryMegabytes(ContainerServiceRole role, Func<string, string?> env)
    {
        int? explicitMemory = TryParsePositiveAtLeast(env(ContainerMemoryLimitEnvVar), MinimumAllowedMemoryMegabytes);
        return explicitMemory ?? GetDefaultMemoryMegabytes(role);
    }

    private static int? TryParsePositiveAtLeast(string? raw, int minimum)
    {
        if (!int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return null;
        }

        return parsed >= minimum ? parsed : null;
    }
}
