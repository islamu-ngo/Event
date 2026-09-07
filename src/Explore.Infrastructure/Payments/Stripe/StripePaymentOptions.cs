using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Payments.Stripe;

public sealed class StripePaymentOptions
{
    public const string SectionName = "Payments:Stripe";
    public const string TestMode = "Test";
    public const string LiveMode = "Live";

    public string Mode { get; set; } = TestMode;

    public string[]? AllowedCheckoutHosts { get; set; } = ["checkout.stripe.com"];

    public bool ExpectsLiveMode => string.Equals(Mode, LiveMode, StringComparison.Ordinal);

    public string ExpectedSecretKeyPrefix => ExpectsLiveMode ? "sk_live_" : "sk_test_";
}

public sealed class StripePaymentOptionsValidator : IValidateOptions<StripePaymentOptions>
{
    public ValidateOptionsResult Validate(string? name, StripePaymentOptions options) => options.Mode switch
    {
        StripePaymentOptions.TestMode or StripePaymentOptions.LiveMode when NormalizeAllowedCheckoutHosts(options.AllowedCheckoutHosts) is not null => ValidateOptionsResult.Success,
        _ => ValidateOptionsResult.Fail("Payments:Stripe requires Test or Live mode and at least one valid Checkout host.")
    };

    internal static HashSet<string>? NormalizeAllowedCheckoutHosts(IEnumerable<string>? hosts)
    {
        if (hosts is null)
        {
            return null;
        }

        var normalizedHosts = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? host in hosts)
        {
            string normalized = host?.Trim().ToLowerInvariant() ?? string.Empty;
            if (normalized.Length is 0 or > 253 ||
                normalized.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')))
            {
                return null;
            }

            normalizedHosts.Add(normalized);
        }

        return normalizedHosts.Count == 0 ? null : normalizedHosts;
    }
}

internal static class StripeModeEvidence
{
    public static bool Matches(StripePaymentOptions options, bool livemode) => livemode == options.ExpectsLiveMode;

    public static bool TryReadLivemode(JsonElement? element, out bool livemode)
    {
        livemode = false;
        if (element is not { ValueKind: JsonValueKind.Object } json
            || !json.TryGetProperty("livemode", out JsonElement livemodeElement)
            || livemodeElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        livemode = livemodeElement.GetBoolean();
        return true;
    }

    public static bool TryReadEventLivemode(string payload, out bool livemode) =>
        TryReadPayloadBoolean(payload, static root => root, out livemode);

    public static bool TryReadAccountObjectLivemode(string payload, out bool livemode) =>
        TryReadPayloadBoolean(payload, static root => root.GetProperty("data").GetProperty("object"), out livemode);

    private static bool TryReadPayloadBoolean(
        string payload,
        Func<JsonElement, JsonElement> selectObject,
        out bool livemode)
    {
        livemode = false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement target = selectObject(document.RootElement);
            if (target.ValueKind != JsonValueKind.Object
                || !target.TryGetProperty("livemode", out JsonElement livemodeElement)
                || livemodeElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            livemode = livemodeElement.GetBoolean();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }
}
