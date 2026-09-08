using Amazon.S3;
using Explore.Application.Models;

namespace Explore.Infrastructure.Storage;

internal sealed class S3ClientFactory : IS3ClientFactory
{
    public IAmazonS3 CreateDataClient(S3Configuration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return CreateClient(config, config.Endpoint);
    }

    public IAmazonS3 CreatePresignClient(S3Configuration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(config.PublicEndpoint) ||
            string.Equals(config.PublicEndpoint.Trim(), config.Endpoint.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return CreateDataClient(config);
        }

        return CreateClient(config, config.PublicEndpoint);
    }

    private static IAmazonS3 CreateClient(S3Configuration config, string endpoint)
    {
        var normalizedEndpoint = NormalizeEndpoint(endpoint);
        var s3Config = new AmazonS3Config
        {
            ForcePathStyle = config.ForcePathStyle,
            ServiceURL = normalizedEndpoint,
            UseHttp = normalizedEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
            AuthenticationRegion = string.IsNullOrWhiteSpace(config.Region) ? "us-east-1" : config.Region
        };

        return new AmazonS3Client(config.AccessKeyId, config.SecretAccessKey, s3Config);
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        var trimmed = endpoint.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"https://{trimmed}";
    }
}
