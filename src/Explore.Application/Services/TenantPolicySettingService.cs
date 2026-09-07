using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;

namespace Explore.Application.Services;

public partial class TenantPolicySettingService : ITenantPolicySettingService
{
    private const string DefaultBrandDisplayName = "";
    private const string DefaultPublicHomePage = "EventList";
    private const string DefaultCommunityGuidelinesContent =
        "# Community Guidelines\n\n" +
        "## Our Community\n\n" +
        "This platform is a space for sharing events with our community. To ensure a positive experience for everyone, we ask all event organizers and participants to follow these guidelines.\n\n" +
        "## Event Posting Standards\n\n" +
        "**Accuracy and Transparency**\n" +
        "- Provide complete and accurate event information including date, time, location, and organizer details.\n" +
        "- Clearly describe what attendees can expect from your event.\n" +
        "- Notify attendees promptly of any changes, cancellations, or updates.\n\n" +
        "**Appropriate Content**\n" +
        "- Events must be relevant to the community and align with the platform's purpose.\n" +
        "- Event titles and descriptions must be truthful and not misleading.\n" +
        "- Do not post duplicate or spam events.\n\n" +
        "**Inclusive and Respectful Language**\n" +
        "- Use welcoming, inclusive language in event descriptions and communications.\n" +
        "- Events must not promote discrimination based on race, ethnicity, religion, gender, disability, or any other protected characteristic.\n" +
        "- Maintain respectful communication with attendees and other organizers.\n\n" +
        "## Prohibited Content\n\n" +
        "The following types of events and content are not permitted on this platform:\n\n" +
        "- Events that promote illegal activities or violate applicable laws\n" +
        "- Hateful, discriminatory, or violent content\n" +
        "- Harassment or targeted abuse of individuals or groups\n" +
        "- Deceptive, fraudulent, or misleading events\n" +
        "- Spam or commercially exploitative content\n\n" +
        "## Participation Guidelines\n\n" +
        "**As an Attendee**\n" +
        "- Respect the organizer's event rules and code of conduct.\n" +
        "- Be courteous to other attendees and event staff.\n" +
        "- Cancel your registration if your plans change.\n\n" +
        "**As an Organizer**\n" +
        "- Respond to attendee inquiries in a timely manner.\n" +
        "- Enforce a safe and welcoming environment at your events.\n" +
        "- Honor the commitments made in your event listing.\n\n" +
        "## Reporting Violations\n\n" +
        "If you encounter content or behavior that violates these guidelines, please report it to the platform administrators. All reports are taken seriously.\n\n" +
        "## Consequences\n\n" +
        "Violations of these guidelines may result in removal of the event listing, a warning, temporary suspension, or permanent removal from the platform for serious or repeated violations.";

    private readonly ISystemSettingRepository _systemSettingRepository;
    private readonly ITenantSettingRepository _tenantSettingRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ISettingMutationLock _mutationLock;
    private readonly IPublicationPolicyMutationBoundary _publicationPolicyMutationBoundary;

    public TenantPolicySettingService(
        ISystemSettingRepository systemSettingRepository,
        ITenantSettingRepository tenantSettingRepository,
        ITenantRepository tenantRepository,
        ISettingMutationLock mutationLock,
        IPublicationPolicyMutationBoundary publicationPolicyMutationBoundary)
    {
        _systemSettingRepository = systemSettingRepository;
        _tenantSettingRepository = tenantSettingRepository;
        _tenantRepository = tenantRepository;
        _mutationLock = mutationLock;
        _publicationPolicyMutationBoundary = publicationPolicyMutationBoundary;
    }

    private static bool ResolveBoolean(string? tenantOverrideValue, string? systemValue, bool fallback, bool allowTenantOverride)
    {
        if (allowTenantOverride && !string.IsNullOrWhiteSpace(tenantOverrideValue))
        {
            return DeserializeBoolean(tenantOverrideValue, fallback);
        }

        return DeserializeBoolean(systemValue, fallback);
    }

    private static string ResolveString(string? tenantOverrideValue, string? systemValue, string fallback, bool allowTenantOverride)
    {
        if (allowTenantOverride && !string.IsNullOrWhiteSpace(tenantOverrideValue))
        {
            return DeserializeString(tenantOverrideValue, fallback);
        }

        return DeserializeString(systemValue, fallback);
    }

    private static IReadOnlyList<string> ResolveStringList(
        string? tenantOverrideValue,
        string? systemValue,
        IReadOnlyList<string> fallback,
        bool allowTenantOverride)
    {
        if (allowTenantOverride && !string.IsNullOrWhiteSpace(tenantOverrideValue))
        {
            return DeserializeStringList(tenantOverrideValue, fallback);
        }

        return DeserializeStringList(systemValue, fallback);
    }

    private static bool DeserializeBoolean(string? rawValue, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<bool>(rawValue);
        }
        catch
        {
            return bool.TryParse(rawValue, out var parsed) ? parsed : fallback;
        }
    }

    private static string DeserializeString(string? rawValue, string fallback)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<string>(rawValue);
            return deserialized ?? fallback;
        }
        catch
        {
            return rawValue.Trim('"');
        }
    }

    private static IReadOnlyList<string> DeserializeStringList(string? rawValue, IReadOnlyList<string> fallback)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<IReadOnlyList<string>>(rawValue);
            return NormalizeAiModelIds(deserialized ?? fallback);
        }
        catch
        {
            return NormalizeAiModelIds(rawValue.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));
        }
    }

    private static IReadOnlyList<string> NormalizeAiModelIds(params IEnumerable<string?>[] modelIdGroups)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in modelIdGroups)
        {
            foreach (var modelId in group)
            {
                var trimmed = modelId?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || !seen.Add(trimmed))
                {
                    continue;
                }

                normalized.Add(trimmed);
            }
        }

        return normalized;
    }

    private static int DeserializeInteger(string? rawValue, int fallback)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<int>(rawValue);
        }
        catch
        {
            return int.TryParse(rawValue.Trim('"'), out var parsed) ? parsed : fallback;
        }
    }

    private static string NormalizeHomePage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultPublicHomePage;
        }

        return value.Equals("LandingPage", StringComparison.OrdinalIgnoreCase)
            ? "LandingPage"
            : "EventList";
    }

    private static string NormalizeOptionalHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        normalized = normalized.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase);
        return normalized.Trim('/').Trim();
    }

    private static string? NormalizeSubdomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        normalized = normalized.Replace(" ", "-");
        normalized = new string(normalized.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
