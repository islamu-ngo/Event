using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookEndpointSecretResolver
{
    private const string ConfigurationPrefix = "configuration:";
    private readonly IConfiguration _configuration;

    public WebhookEndpointSecretResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public WebhookSecretMaterial? Resolve(WebhookEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var currentSecret = ResolveSecret(endpoint.SecretRef);
        if (string.IsNullOrWhiteSpace(currentSecret))
        {
            return null;
        }

        var previousSecret = ResolveSecret(endpoint.PreviousSecretRef);
        var previousValidUntil = ConvertPreviousSecretValidUntil(endpoint.PreviousSecretValidUntil);

        return new WebhookSecretMaterial(
            currentSecret,
            endpoint.SecretVersion,
            previousSecret,
            previousValidUntil);
    }

    public WebhookSecretMaterial? Resolve(string credentialReference, int credentialVersion)
    {
        if (credentialVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(credentialVersion));
        }

        var currentSecret = ResolveSecret(credentialReference);
        return string.IsNullOrWhiteSpace(currentSecret)
            ? null
            : new WebhookSecretMaterial(currentSecret, credentialVersion, null, null);
    }

    private string? ResolveSecret(string? secretRef)
    {
        if (string.IsNullOrWhiteSpace(secretRef))
        {
            return null;
        }

        var trimmed = secretRef.Trim();
        if (trimmed.StartsWith(ConfigurationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var key = trimmed[ConfigurationPrefix.Length..];
            return NormalizeSecret(_configuration[key]);
        }

        var webhookSecret = _configuration[$"{WebhookOptions.SectionName}:EndpointSecrets:{trimmed}"];
        if (!string.IsNullOrWhiteSpace(webhookSecret))
        {
            return NormalizeSecret(webhookSecret);
        }

        return NormalizeSecret(_configuration[trimmed]);
    }

    private static DateTimeOffset? ConvertPreviousSecretValidUntil(DateTime? validUntil)
    {
        if (validUntil is null)
        {
            return null;
        }

        var utc = validUntil.Value.Kind == DateTimeKind.Utc
            ? validUntil.Value
            : DateTime.SpecifyKind(validUntil.Value, DateTimeKind.Utc);
        return new DateTimeOffset(utc);
    }

    private static string? NormalizeSecret(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
