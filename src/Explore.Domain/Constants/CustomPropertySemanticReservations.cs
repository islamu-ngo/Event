namespace Explore.Domain.Constants;

public static class CustomPropertySemanticReservations
{
    public static readonly IReadOnlyCollection<ReservedSemanticKey> Layer2EventSemantics =
    [
        new("sector.islamic", "madhab"),
        new("sector.islamic", "madhab_id"),
        new("sector.islamic", "prayer_time"),
        new("sector.islamic", "reference_prayer"),
        new("sector.islamic", "prayer_time_offset"),
        new("sector.islamic", "gender"),
        new("sector.islamic", "gender_mode"),
        new("sector.islamic", "includes_quran_recitation"),
        new("sector.islamic", "primary_language"),
        new("sector.islamic", "primary_language_id"),
        new("sector.tech", "github_repo"),
        new("sector.tech", "github_repo_url"),
        new("sector.tech", "github_repository_url"),
        new("sector.tech", "hackathon_track"),
        new("sector.tech", "skill_level"),
        new("sector.tech", "tech_stack"),
        new("sector.tech", "tech_stack_tags"),
        new("sector.tech", "requires_laptop"),
        new("sector.tech", "is_coding_competition"),
        new("sector.tech", "coding_competition"),
        new("sector.tech", "max_team_size"),
        new("sector.tech", "prize_pool"),
        new("sector.tech", "prize_currency_code"),
    ];

    public static readonly IReadOnlyCollection<ReservedSemanticKey> Layer2SessionSemantics =
    [
        new("sector.islamic.session", "start_time_type"),
        new("sector.islamic.session", "session_start_time_type"),
        new("sector.islamic.session", "reference_prayer"),
        new("sector.islamic.session", "offset_minutes"),
        new("sector.islamic.session", "requires_wudu"),
        new("sector.islamic.session", "ritual_requirements"),
        new("sector.islamic.session", "ritual_requirements_json"),
    ];

    public static bool IsReservedLayer2Semantic(string? namespaceValue, string? key)
    {
        if (string.IsNullOrWhiteSpace(namespaceValue) || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return IsExactLayer2Semantic(namespaceValue, key)
            || IsReservedLayer2Key(key);
    }

    private static bool IsExactLayer2Semantic(string namespaceValue, string key)
        => Layer2EventSemantics.Concat(Layer2SessionSemantics).Any(reservation =>
            reservation.Namespace.Equals(namespaceValue, StringComparison.OrdinalIgnoreCase)
            && reservation.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    private static bool IsReservedLayer2Key(string key)
        => Layer2EventSemantics.Concat(Layer2SessionSemantics).Any(reservation =>
            reservation.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public readonly record struct ReservedSemanticKey(string Namespace, string Key);
}
