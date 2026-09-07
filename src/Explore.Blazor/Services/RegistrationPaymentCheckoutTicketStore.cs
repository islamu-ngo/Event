using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace Explore.Blazor.Services;

public sealed record RegistrationPaymentCheckoutTicketIssue(
    string ProtectedCookie,
    DateTimeOffset ExpiresAt);

public sealed class RegistrationPaymentCheckoutTicketStore
{
    private const string Purpose = "registration-payment-checkout-cookie-v2";
    private const int MaximumProtectedCookieLength = 3072;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector;
    private readonly TimeProvider _timeProvider;

    public RegistrationPaymentCheckoutTicketStore(
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(typeof(RegistrationPaymentCheckoutTicketStore).FullName!, Purpose);
        _timeProvider = timeProvider;
    }

    public RegistrationPaymentCheckoutTicketIssue? PrepareIssue(
        Uri target,
        Guid eventId,
        Guid orderId,
        HttpRequest request,
        string tenantSlug,
        string checkoutSession)
    {
        if (target.AbsoluteUri.Length > 2048 || string.IsNullOrWhiteSpace(checkoutSession))
        {
            return null;
        }

        DateTimeOffset expiresAt = _timeProvider.GetUtcNow() + Lifetime;
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new TicketPayload(
                BuildAudience(request, tenantSlug, eventId, orderId),
                Digest(checkoutSession),
                target.AbsoluteUri,
                eventId,
                orderId,
                expiresAt),
            JsonOptions);
        string protectedCookie = WebEncoders.Base64UrlEncode(_protector.Protect(payload));
        return protectedCookie.Length <= MaximumProtectedCookieLength
            ? new(protectedCookie, expiresAt)
            : null;
    }

    public Uri? ValidateAndExtractTarget(
        string protectedCookie,
        HttpRequest request,
        string tenantSlug,
        string checkoutSession)
    {
        if (string.IsNullOrWhiteSpace(checkoutSession)
            || string.IsNullOrWhiteSpace(protectedCookie)
            || protectedCookie.Length > MaximumProtectedCookieLength)
        {
            return null;
        }

        try
        {
            TicketPayload? payload = JsonSerializer.Deserialize<TicketPayload>(
                _protector.Unprotect(WebEncoders.Base64UrlDecode(protectedCookie)),
                JsonOptions);
            if (payload is null
                || payload.ExpiresAt <= _timeProvider.GetUtcNow()
                || !FixedEquals(payload.SessionDigest, Digest(checkoutSession))
                || !string.Equals(
                    payload.Audience,
                    BuildAudience(request, tenantSlug, payload.EventId, payload.OrderId),
                    StringComparison.Ordinal))
            {
                return null;
            }

            return Uri.TryCreate(payload.TargetUrl, UriKind.Absolute, out Uri? target) ? target : null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static string BuildAudience(HttpRequest request, string tenantSlug, Guid eventId, Guid orderId) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}|{tenantSlug}|{eventId:D}|{orderId:D}";

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private sealed record TicketPayload(
        string Audience,
        string SessionDigest,
        string TargetUrl,
        Guid EventId,
        Guid OrderId,
        DateTimeOffset ExpiresAt);
}
