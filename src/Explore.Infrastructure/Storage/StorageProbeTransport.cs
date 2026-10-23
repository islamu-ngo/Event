using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Explore.Application.Models.Storage;
using Explore.Application.Models;
using Explore.Domain;
using OpenTelemetry;

namespace Explore.Infrastructure.Storage;

/// <summary>
/// One retained target and credential, one conditional create, one exact-version delete.
/// A failed or ambiguous put never authorizes deletion by key.
/// </summary>
internal static class StorageProbeTransport
{
    internal static async Task<FileStorageProviderStatus> TestAsync(string provider,
        S3Configuration? configuration, IStorageProbeClientFactory factory,
        Func<CancellationToken, Task> handoff, Action finalTime, CancellationToken token)
    {
        if (provider != StorageProviders.S3Compatible)
            return Result(provider, "probe_exact_cleanup_unsupported");
        if (configuration is null) return Result(provider, "probe_not_configured");
        using var instrumentation = SuppressInstrumentationScope.Begin();
        string bucket = configuration.BucketName;
        string key = $".system/preflight-probe-{Guid.CreateVersion7():N}";
        string? version = null;
        string phase = "connection";
        try
        {
            IAmazonS3 transport;
            try
            {
                transport = factory.Create(new S3Configuration
                {
                    Endpoint = configuration.Endpoint,
                    BucketName = bucket,
                    Region = configuration.Region,
                    ForcePathStyle = configuration.ForcePathStyle,
                    AccessKeyId = configuration.AccessKeyId,
                    SecretAccessKey = configuration.SecretAccessKey
                });
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Result(provider, "probe_configuration_invalid");
            }
            using var client = transport;
            await handoff(token);
            finalTime();
            var versioning = await client.GetBucketVersioningAsync(new GetBucketVersioningRequest { BucketName = bucket }, token);
            if (versioning.VersioningConfig.Status != VersionStatus.Enabled)
                return Result(provider, "probe_exact_cleanup_unsupported");
            var put = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                IfNoneMatch = "*",
                InputStream = Stream.Null,
                AutoCloseStream = false,
                ContentType = "application/octet-stream"
            };
            put.Headers.ContentLength = 0;
            phase = "write";
            await handoff(token);
            finalTime();
            var created = await client.PutObjectAsync(put, token);
            version = created.VersionId;
            if (string.IsNullOrWhiteSpace(version) || version == "null")
                return Result(provider, "probe_cleanup_ownership_unconfirmed");
            // Cleanup has no independent timeout/grant that can outlive the original
            // credential or support horizon. Revocation leaves the exact version intact.
            phase = "cleanup";
            await handoff(token);
            finalTime();
            var deleted = await client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = bucket,
                Key = key,
                VersionId = version
            }, token);
            if (deleted.VersionId != version
                || string.Equals(deleted.DeleteMarker, "true", StringComparison.OrdinalIgnoreCase))
                return Result(provider, "probe_cleanup_unconfirmed");
            return Result(provider, null);
        }
        catch (Exception exception) when (exception is AmazonServiceException or HttpRequestException
            or IOException or TimeoutException)
        {
            // No raw provider exception, endpoint, key, version or credential escapes.
            return Result(provider, phase switch
            {
                "write" => "probe_write_unconfirmed",
                "cleanup" => "probe_cleanup_failed",
                _ => "probe_connection_failed"
            });
        }
    }

    private static FileStorageProviderStatus Result(string provider, string? code) => new(
        provider,
        code is null,
        true,
        provider == StorageProviders.S3Compatible,
        code,
        code is null ? "The original probe object was created and its exact version removed."
            : "The storage probe could not confirm safe completion.");
}

/// <summary>A probe-specific external boundary; ordinary storage clients retain their own retry policy.</summary>
public interface IStorageProbeClientFactory
{
    IAmazonS3 Create(S3Configuration configuration);
}

internal sealed class StorageProbeClientFactory : IStorageProbeClientFactory
{
    public IAmazonS3 Create(S3Configuration configuration)
    {
        string endpoint = configuration.Endpoint.Trim();
        if (!endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            endpoint = $"https://{endpoint}";
        return new AmazonS3Client(configuration.AccessKeyId, configuration.SecretAccessKey, new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = configuration.ForcePathStyle,
            UseHttp = endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
            AuthenticationRegion = configuration.Region,
            MaxErrorRetry = 0,
            RetryMode = RequestRetryMode.Standard,
            AllowAutoRedirect = false
        });
    }
}
