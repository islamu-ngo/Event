using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.API.Services;

public sealed class SvixIncomingWebhookVerifier(
    IOptionsMonitor<WebhookOptions> options,
    ISecretResolver secretResolver,
    IWebhookSignatureService signatureService,
    IWebhookConsumerProviderBindingRepository bindingRepository,
    ILogger<SvixIncomingWebhookVerifier> logger) : IIncomingWebhookVerifier
{
    public string Provider => "svix";

    public async Task<IncomingWebhookVerificationResult> VerifyAsync(
        IncomingWebhookContext context,
        CancellationToken cancellationToken)
    {
        var secretRef = options.CurrentValue.Svix.OperationalWebhookSecretRef?.Trim();
        if (string.IsNullOrWhiteSpace(secretRef))
        {
            logger.LogWarning("Svix operational webhook rejected because Webhooks:Svix:OperationalWebhookSecretRef is not configured.");
            return IncomingWebhookVerificationResult.Rejected(
                "svix_operational_webhook_secret_missing",
                "The Svix operational webhook signing secret is not configured.");
        }

        var resolved = await secretResolver.ResolveAsync(secretRef, tenantId: null, cancellationToken);
        if (!resolved.IsResolved || string.IsNullOrWhiteSpace(resolved.Value))
        {
            logger.LogWarning("Svix operational webhook rejected because the signing secret could not be resolved.");
            return IncomingWebhookVerificationResult.Rejected(
                "svix_operational_webhook_secret_unresolved",
                "The Svix operational webhook signing secret could not be resolved.");
        }

        var verification = signatureService.Verify(
            context.RawPayloadBytes.Span,
            context.Headers,
            new WebhookSecretMaterial(resolved.Value, CurrentSecretVersion: 1));
        if (!verification.IsValid)
        {
            return IncomingWebhookVerificationResult.Rejected(
                $"svix_webhook_{verification.FailureCategory ?? "verification_failed"}",
                "The Svix operational webhook signature could not be verified.");
        }

        if (!TryGetProviderApplicationIdentity(
                context.RawPayloadBytes,
                out var externalApplicationId,
                out var applicationUid))
        {
            return IncomingWebhookVerificationResult.Rejected(
                "svix_webhook_application_identity_missing",
                "The signed Svix operational event does not contain a complete application identity.");
        }

        var binding = await bindingRepository.ResolveVerifiedProviderIdentityAsync(
            WebhookProviderKind.Svix,
            options.CurrentValue.Svix.Environment,
            externalApplicationId,
            applicationUid,
            cancellationToken);
        if (binding is null ||
            binding.TenantId is not { } tenantId ||
            !binding.IsVerifiedFor(tenantId, binding.WebhookConsumerId))
        {
            return IncomingWebhookVerificationResult.Rejected(
                "svix_webhook_binding_not_verified",
                "The signed Svix application identity is not bound to an enabled tenant webhook consumer.");
        }

        _ = TryGetHeader(context.Headers, "svix-id", out var providerMessageId);
        var eventType = TryGetHeader(context.Headers, "svix-event-type", out var svixEventType)
            ? svixEventType
            : null;

        return IncomingWebhookVerificationResult.VerifiedProviderBinding(
            tenantId,
            binding.Id,
            providerMessageId,
            eventType,
            providerMessageId);
    }

    private static bool TryGetProviderApplicationIdentity(
        ReadOnlyMemory<byte> rawPayloadBytes,
        out string externalApplicationId,
        out string applicationUid)
    {
        externalApplicationId = string.Empty;
        applicationUid = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(rawPayloadBytes);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   TryGetRequiredString(document.RootElement, "appId", out externalApplicationId) &&
                   TryGetRequiredString(document.RootElement, "appUid", out applicationUid);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetRequiredString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool TryGetHeader(
        IReadOnlyDictionary<string, string> headers,
        string headerName,
        out string value)
    {
        value = string.Empty;
        return headers.TryGetValue(headerName, out value!) && !string.IsNullOrWhiteSpace(value);
    }
}
