// Provides structured error information for troubleshooting.

namespace Explore.Secrets.Abstractions;

/// <summary>
/// Exception thrown when a secret provider operation fails.
/// </summary>
public class SecretProviderException : Exception
{
    /// <summary>
    /// The provider type that encountered the error.
    /// </summary>
    public SecretProviderType ProviderType { get; }

    /// <summary>
    /// The operation that failed (e.g., "Initialize", "GetSecret", "Refresh").
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Whether this is a transient error that may succeed on retry.
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>
    /// Creates a new SecretProviderException.
    /// </summary>
    public SecretProviderException(
        string message,
        SecretProviderType providerType,
        string operation,
        bool isTransient = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderType = providerType;
        Operation = operation;
        IsTransient = isTransient;
    }

    /// <summary>
    /// Creates a transient exception (network issues, timeouts).
    /// </summary>
    public static SecretProviderException Transient(
        string message,
        SecretProviderType providerType,
        string operation,
        Exception? innerException = null)
        => new(message, providerType, operation, isTransient: true, innerException);

    /// <summary>
    /// Creates a permanent exception (auth failure, missing secret).
    /// </summary>
    public static SecretProviderException Permanent(
        string message,
        SecretProviderType providerType,
        string operation,
        Exception? innerException = null)
        => new(message, providerType, operation, isTransient: false, innerException);
}
