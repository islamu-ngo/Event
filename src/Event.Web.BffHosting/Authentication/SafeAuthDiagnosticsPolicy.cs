using System.Diagnostics;
using System.Security.Cryptography;

namespace Event.Web.BffHosting.Authentication;

public sealed record SafeAuthDiagnostic(
    string ErrorCode,
    string CorrelationId,
    string FailureCategory);

public interface ISafeAuthDiagnosticsPolicy
{
    SafeAuthDiagnostic CreateDiagnostic(string errorCode, Exception? failure = null);

    string BuildLoginRedirectUrl(
        string returnUrl,
        string? provider,
        SafeAuthDiagnostic diagnostic);
}

public sealed class SafeAuthDiagnosticsPolicy : ISafeAuthDiagnosticsPolicy
{
    private const string DefaultErrorCode = "auth_failure";

    public SafeAuthDiagnostic CreateDiagnostic(string errorCode, Exception? failure = null)
    {
        var safeErrorCode = NormalizeToken(errorCode, DefaultErrorCode);
        var category = failure is null
            ? "unknown"
            : NormalizeToken(failure.GetType().Name, "exception");

        return new SafeAuthDiagnostic(
            safeErrorCode,
            CreateCorrelationId(),
            category);
    }

    public string BuildLoginRedirectUrl(
        string returnUrl,
        string? provider,
        SafeAuthDiagnostic diagnostic)
    {
        var safeReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
        var redirectUrl = $"/login?returnUrl={Uri.EscapeDataString(safeReturnUrl)}" +
            "&challengeError=1" +
            $"&errorCode={Uri.EscapeDataString(diagnostic.ErrorCode)}" +
            $"&correlationId={Uri.EscapeDataString(diagnostic.CorrelationId)}";

        if (!string.IsNullOrWhiteSpace(provider))
        {
            redirectUrl += $"&provider={Uri.EscapeDataString(NormalizeToken(provider, "unknown"))}";
        }

        return redirectUrl;
    }

    private static string CreateCorrelationId()
    {
        var traceId = Activity.Current?.TraceId.ToString();
        if (!string.IsNullOrWhiteSpace(traceId))
        {
            return traceId;
        }

        return RandomNumberGenerator.GetHexString(16).ToLowerInvariant();
    }

    private static string NormalizeToken(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        Span<char> buffer = stackalloc char[Math.Min(value.Length, 64)];
        var position = 0;

        foreach (var character in value)
        {
            if (position >= buffer.Length)
            {
                break;
            }

            buffer[position++] = char.IsAsciiLetterOrDigit(character) || character is '_' or '-'
                ? char.ToLowerInvariant(character)
                : '_';
        }

        return position == 0 ? fallback : new string(buffer[..position]);
    }
}
