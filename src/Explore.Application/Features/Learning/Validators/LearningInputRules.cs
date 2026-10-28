namespace Explore.Application.Features.Learning.Validators;

internal static class LearningInputRules
{
    internal static bool IsIdentity(Guid value) =>
        value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;

    internal static bool IsOptionalIdentity(Guid? value) =>
        value is null || IsIdentity(value.Value);

    internal static bool IsContext(string? value) =>
        value == "self" || value is not null
        && value.StartsWith("actor:", StringComparison.Ordinal)
        && Guid.TryParseExact(value.AsSpan(6), "D", out Guid actorId)
        && IsIdentity(actorId) && value == $"actor:{actorId:D}";

    internal static bool IsScopeStamp(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');
}
