using Explore.Domain.Secrets;

namespace Explore.Domain;

/// <summary>
/// Immutable, non-public storage target. No owner relationship: cleanup outlives tenant retirement.
/// </summary>
public sealed class StorageProviderBinding
{
    private StorageProviderBinding() { }

    public Guid Id { get; private set; }
    public string Provider { get; private set; } = null!;
    public string? LocalRootPath { get; private set; }
    public string? Endpoint { get; private set; }
    public string? BucketName { get; private set; }
    public string? Region { get; private set; }
    public bool ForcePathStyle { get; private set; }
    public RetainedSecretReference? AccessKeyReference { get; private set; }
    public RetainedSecretReference? SecretKeyReference { get; private set; }

    public static StorageProviderBinding Local(string absoluteRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteRootPath);
        if (!Path.IsPathFullyQualified(absoluteRootPath))
            throw new ArgumentException("Storage binding requires an absolute root.", nameof(absoluteRootPath));
        return new StorageProviderBinding
        {
            Id = Guid.CreateVersion7(), Provider = StorageProviders.Local, LocalRootPath = absoluteRootPath
        };
    }

    public static StorageProviderBinding S3(
        string endpoint, string bucketName, string region, bool forcePathStyle,
        RetainedSecretReference accessKeyReference, RetainedSecretReference secretKeyReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        var address = endpoint.Contains("://", StringComparison.Ordinal) ? endpoint : $"https://{endpoint}";
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Storage endpoint must be a non-secret HTTP target.", nameof(endpoint));
        ArgumentNullException.ThrowIfNull(accessKeyReference);
        ArgumentNullException.ThrowIfNull(secretKeyReference);
        if (accessKeyReference.SettingKey != SecretDefinitionRegistry.Keys.Storage.AccessKeyId
            || secretKeyReference.SettingKey != SecretDefinitionRegistry.Keys.Storage.SecretAccessKey)
            throw new ArgumentException("Storage binding requires the storage credential references.");
        return new StorageProviderBinding
        {
            Id = Guid.CreateVersion7(), Provider = StorageProviders.S3Compatible,
            Endpoint = endpoint, BucketName = bucketName, Region = region, ForcePathStyle = forcePathStyle,
            AccessKeyReference = accessKeyReference, SecretKeyReference = secretKeyReference
        };
    }
}
