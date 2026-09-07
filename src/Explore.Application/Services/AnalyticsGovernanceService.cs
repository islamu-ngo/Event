using System.Security.Cryptography;
using System.Text;
using Explore.Application.Analytics;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public class AnalyticsGovernanceService : IAnalyticsGovernanceService
{
    private static readonly HashSet<string> SensitivePropertyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "email",
        "user_email",
        "full_name",
        "display_name",
        "phone",
        "address",
        "password",
        "token",
        "secret",
        "api_key",
        "personal_api_key"
    };

    public bool AllowsIdentify(AnalyticsProviderEnum provider, AnalyticsConsentMode consentMode)
    {
        return consentMode == AnalyticsConsentMode.Identified && provider is AnalyticsProviderEnum.Posthog or AnalyticsProviderEnum.RudderStack;
    }

    public bool AllowsGroupIdentify(AnalyticsProviderEnum provider, AnalyticsConsentMode consentMode)
    {
        return consentMode == AnalyticsConsentMode.Identified && provider is AnalyticsProviderEnum.Posthog or AnalyticsProviderEnum.RudderStack;
    }

    public SanitizedAnalyticsTrackPayload? CreateTrackRequest(
        AnalyticsConfiguration configuration,
        string distinctId,
        AnalyticsEventDefinition definition,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (!configuration.IsEnabled || configuration.Provider == AnalyticsProviderEnum.None)
        {
            return null;
        }

        if (definition.RequiresIdentifiedTracking && configuration.ConsentMode != AnalyticsConsentMode.Identified)
        {
            return null;
        }

        var sanitizedProperties = SanitizeProperties(definition.AllowedPropertyKeys, properties);
        var resolvedDistinctId = ResolveDistinctId(configuration.ConsentMode, distinctId);

        return new SanitizedAnalyticsTrackPayload(
            resolvedDistinctId,
            definition.EventName,
            sanitizedProperties);
    }

    public SanitizedAnalyticsPageViewPayload? CreatePageViewRequest(
        AnalyticsConfiguration configuration,
        string distinctId,
        string pagePath,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (!configuration.IsEnabled || configuration.Provider == AnalyticsProviderEnum.None || string.IsNullOrWhiteSpace(pagePath))
        {
            return null;
        }

        var sanitizedProperties = SanitizeProperties(AnalyticsEvents.PublicExperience.PageViewed.AllowedPropertyKeys, properties);
        var resolvedDistinctId = ResolveDistinctId(configuration.ConsentMode, distinctId);

        return new SanitizedAnalyticsPageViewPayload(
            resolvedDistinctId,
            pagePath,
            sanitizedProperties);
    }

    private static string ResolveDistinctId(AnalyticsConsentMode consentMode, string distinctId)
    {
        return consentMode switch
        {
            AnalyticsConsentMode.Identified => distinctId,
            AnalyticsConsentMode.Anonymous => $"anonymous-{Guid.CreateVersion7():N}",
            _ => $"pseudo-{HashDistinctId(distinctId)}"
        };
    }

    private static string HashDistinctId(string distinctId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(distinctId));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, object> SanitizeProperties(
        IReadOnlySet<string> allowedPropertyKeys,
        IReadOnlyDictionary<string, object?>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return new Dictionary<string, object>();
        }

        var sanitized = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach (var (key, value) in properties)
        {
            if (!allowedPropertyKeys.Contains(key) || SensitivePropertyKeys.Contains(key) || value is null)
            {
                continue;
            }

            var normalized = NormalizeValue(value);
            if (normalized is not null)
            {
                sanitized[key] = normalized;
            }
        }

        return sanitized;
    }

    private static object? NormalizeValue(object value)
    {
        return value switch
        {
            string text when string.IsNullOrWhiteSpace(text) => null,
            string text => text,
            Guid guid => guid.ToString(),
            DateTime dateTime => dateTime.ToUniversalTime().ToString("O"),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O"),
            Enum @enum => @enum.ToString(),
            bool boolean => boolean,
            byte number => number,
            short number => number,
            int number => number,
            long number => number,
            float number => number,
            double number => number,
            decimal number => number,
            string[] stringArray => stringArray.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
            IEnumerable<string> stringValues => stringValues.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray(),
            _ => null
        };
    }
}
