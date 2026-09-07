using Explore.Domain.Enums;

namespace Explore.Application.Features.EventReporting.Policies;

public static class EventReportReasonCodePolicy
{
    public const int MaxReasonCodeLength = 100;
    public const int MaxSubcategoryCodeLength = 100;
    public const int MaxCorrelationIdLength = 100;
    public const string InvalidReasonCodeFailureCode = "event_report_reason_code_invalid";
    public const string EventCorrectionSuggestionSubcategory = "event_correction_suggestion";
    public const string UnsafeExternalLinkSubcategory = "unsafe_external_link";
    public const string LegalOrCopyrightComplaintSubcategory =
        "legal_or_copyright_complaint";

    private static readonly ReasonCodeMetadata[] ReasonCodes =
    [
        new(EventReportReasonCode.Spam, "spam", "Spam", "Unwanted promotional, repetitive, or irrelevant event content."),
        new(EventReportReasonCode.ScamOrFraud, "scam_or_fraud", "Scam or fraud", "Suspicious financial, impersonation, phishing, or fraudulent activity."),
        new(EventReportReasonCode.HateOrHarassment, "hate_or_harassment", "Hate or harassment", "Abusive, hateful, bullying, or targeted harassment concerns."),
        new(EventReportReasonCode.ViolenceOrThreats, "violence_or_threats", "Violence or threats", "Threats, incitement, or credible risks of physical harm."),
        new(EventReportReasonCode.IllegalContent, "illegal_content", "Illegal content", "Potentially illegal event content, activity, or instructions."),
        new(EventReportReasonCode.SexualContent, "sexual_content", "Sexual content", "Sexual content or conduct that appears inappropriate for the platform."),
        new(EventReportReasonCode.PrivacyViolation, "privacy_violation", "Privacy violation", "Private information, doxxing, or consent-related privacy concerns."),
        new(EventReportReasonCode.Misinformation, "misinformation", "Misinformation", "Potentially false or misleading claims with safety or community impact."),
        new(EventReportReasonCode.SafetyConcern, "safety_concern", "Safety concern", "Venue, organizer, attendee, or community safety concerns."),
        new(EventReportReasonCode.Other, "other", "Other", "A concern that does not fit the other categories.")
    ];

    private static readonly IReadOnlyDictionary<EventReportReasonCode, string> Codes =
        ReasonCodes.ToDictionary(metadata => metadata.ReasonCode, metadata => metadata.Code);

    public static IReadOnlyCollection<string> AllowedReasonCodes { get; } = Codes.Values.ToArray();

    public static string ToCode(EventReportReasonCode reasonCode) => Codes[reasonCode];

    public static IReadOnlyList<(int Id, string Code, string DisplayName, string Description)> GetReasonOptions()
    {
        return ReasonCodes
            .Select(metadata => ((int)metadata.ReasonCode, metadata.Code, metadata.DisplayName, metadata.Description))
            .ToArray();
    }

    public static (int Id, string Code, string DisplayName, string Description)? FindReasonOption(string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return null;
        }

        var normalized = reasonCode.Trim().ToLowerInvariant();
        var match = ReasonCodes.FirstOrDefault(metadata => metadata.Code == normalized);
        return match is null
            ? null
            : ((int)match.ReasonCode, match.Code, match.DisplayName, match.Description);
    }

    public static bool IsReservedRemedySubcategory(string? subcategoryCode)
    {
        if (string.IsNullOrWhiteSpace(subcategoryCode))
            return false;

        string normalized = subcategoryCode.Trim();
        return string.Equals(
                normalized,
                EventCorrectionSuggestionSubcategory,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                normalized,
                UnsafeExternalLinkSubcategory,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                normalized,
                LegalOrCopyrightComplaintSubcategory,
                StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryNormalize(
        string? reasonCode,
        out string normalizedReasonCode,
        out string? error)
    {
        normalizedReasonCode = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            error = "ReasonCode is required.";
            return false;
        }

        var normalized = reasonCode.Trim().ToLowerInvariant();
        if (normalized.Length > MaxReasonCodeLength || !AllowedReasonCodes.Contains(normalized, StringComparer.Ordinal))
        {
            error = "ReasonCode must be one of the supported event report reason codes.";
            return false;
        }

        normalizedReasonCode = normalized;
        return true;
    }

    private sealed record ReasonCodeMetadata(
        EventReportReasonCode ReasonCode,
        string Code,
        string DisplayName,
        string Description);
}
