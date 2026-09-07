using System.Security.Cryptography;
using System.Text.Json;
using Explore.API.Controllers;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Domain;

namespace Explore.API.Services;

public sealed class RegistrationProviderIncomingWebhookVerifier(
    IRegistrationProviderCallbackBindingResolver bindingResolver,
    IRegistrationProviderRegistry providerRegistry,
    IRegistrationProviderCallbackReceiptProtector receiptProtector) : IIncomingWebhookVerifier
{
    public const string IntakeProvider = "registration-provider";

    public string Provider => IntakeProvider;

    public async Task<IncomingWebhookVerificationResult> VerifyAsync(
        IncomingWebhookContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Headers.TryGetValue(RegistrationProviderCallbackController.ProviderHeader, out string? provider) ||
            !context.Headers.TryGetValue(RegistrationProviderCallbackController.BindingHeader, out string? bindingValue) ||
            !Guid.TryParse(bindingValue, out Guid bindingId))
        {
            return IncomingWebhookVerificationResult.Rejected("registration_callback_route_invalid", "The callback route is invalid.");
        }

        RegistrationProviderBinding? binding = await bindingResolver.ResolveForCallbackAsync(provider, bindingId, cancellationToken);
        if (binding is null)
        {
            return IncomingWebhookVerificationResult.Rejected("registration_callback_binding_unknown", "The registration callback could not be verified.");
        }

        RegistrationProviderCallbackVerificationResult verified;
        RegistrationProviderTuple tuple;
        try
        {
            tuple = ResolveTuple(binding, provider);
            if (binding.Connection is null || tuple.ProviderCode.Length == 0)
            {
                return IncomingWebhookVerificationResult.Rejected("registration_callback_binding_unknown", "The registration callback could not be verified.");
            }

            if (providerRegistry.TryResolve(tuple) is not IRegistrationProviderCallbackVerifier callbackVerifier)
            {
                return IncomingWebhookVerificationResult.Rejected("registration_callback_provider_unsupported", "The registration callback could not be verified.");
            }

            verified = await callbackVerifier.VerifyCallbackAsync(
                new RegistrationProviderCallbackVerificationRequest(binding.TenantId, binding, binding.Connection, tuple,
                    context.RawPayloadBytes, context.Headers), cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or ArgumentException or CryptographicException)
        {
            return IncomingWebhookVerificationResult.Rejected("registration_callback_format_invalid", "The registration callback could not be verified.");
        }

        string providerSubmissionId = verified.ProviderSubmissionId?.Trim() ?? string.Empty;
        if (!verified.IsVerified || string.IsNullOrWhiteSpace(verified.Receipt) ||
            providerSubmissionId.Length is 0 or > 200 || tuple.ProviderCode.Length == 0)
        {
            return IncomingWebhookVerificationResult.Rejected("registration_callback_unverified", "The registration callback could not be verified.");
        }

        string protectedReceipt = receiptProtector.Protect(new RegistrationProviderCallbackReceipt(
            binding.TenantId,
            binding.RegistrationProviderConnectionId,
            binding.Id,
            provider.Trim(),
            tuple.Key,
            ComputePayloadHash(context.RawPayloadBytes.Span),
            providerSubmissionId,
            context.ReceivedAt,
            Guid.CreateVersion7().ToString("N")));

        string effectKind = string.IsNullOrWhiteSpace(verified.EffectKind)
            ? ProcessProviderSubmissionEffectCommandHandler.StableEffectKind
            : verified.EffectKind.Trim();
        IncomingWebhookVerificationResult result = IncomingWebhookVerificationResult.VerifiedTenantCredential(
            binding.TenantId,
            $"{binding.Id:N}:{providerSubmissionId}",
            effectKind,
            $"{binding.Id:N}:{providerSubmissionId}");
        return result with { Receipt = protectedReceipt };
    }

    private static RegistrationProviderTuple ResolveTuple(RegistrationProviderBinding binding, string provider)
    {
        return binding.Connection is not null && string.Equals(binding.Connection.ProviderCode, provider, StringComparison.OrdinalIgnoreCase) &&
               binding.Capabilities.Any(capability => !capability.IsDeleted &&
                   string.Equals(capability.CapabilityCode, RegistrationProviderCapabilityCodes.CallbackVerification, StringComparison.OrdinalIgnoreCase))
            ? new RegistrationProviderTuple(binding.Connection.ProviderCode, binding.Connection.ProviderDeploymentCode, binding.Connection.ApiVersion,
                binding.Connection.AdapterPolicyVersion, binding.Connection.ConformanceEvidenceRevision)
            : RegistrationProviderTuple.Empty;
    }

    private static string ComputePayloadHash(ReadOnlySpan<byte> bodyBytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bodyBytes)).ToLowerInvariant();
}
