using System.Text.Json;
using System.Text.Json.Nodes;
using ISLAMU.Moderation.Domain;

namespace Explore.Persistence.EntityConfigurations;

/// <summary>
/// Consent-owner storage encoding only. Explicit coordinates reconstruct Domain factories,
/// without CLR deserialization or reflection materialization. Structural validation does not
/// authenticate applicability or grant collection; no private body or evidence is encoded.
/// </summary>
internal static class ModerationProfileSubjectConsentSnapshot
{
    internal static string EncodeBinding(ModerationIndividualBinding binding) => BindingRow(binding).ToJsonString();

    internal static ModerationIndividualBinding DecodeBinding(string encoded)
    {
        if (encoded.Length > 4096) throw new JsonException("Consent binding exceeds its owner column bound.");
        using var document = JsonDocument.Parse(encoded, new JsonDocumentOptions { MaxDepth = 8 });
        return RestoreBinding(document.RootElement);
    }

    internal static string EncodeAssessment(ModerationCollectionPrerequisiteAssessment assessment) =>
        new JsonObject
        {
            ["Binding"] = BindingRow(assessment.Binding),
            ["Id"] = assessment.Reference.Id,
            ["Revision"] = assessment.Reference.Revision,
            ["Generation"] = assessment.Reference.Generation,
            ["ObservedAt"] = assessment.Reference.ObservedAt,
            ["ValidUntil"] = assessment.Reference.ValidUntil,
            ["Required"] = new JsonArray(assessment.Required.Select(kind => (JsonNode?)JsonValue.Create((int)kind)).ToArray())
        }.ToJsonString();

    internal static ModerationCollectionPrerequisiteAssessment DecodeAssessment(string encoded)
    {
        if (encoded.Length > 8192) throw new JsonException("Consent assessment exceeds its owner column bound.");
        using var document = JsonDocument.Parse(encoded, new JsonDocumentOptions { MaxDepth = 8 });
        var row = document.RootElement;
        RequireFields(row, ["Binding", "Id", "Revision", "Generation", "ObservedAt", "ValidUntil", "Required"]);
        var requiredRow = row.GetProperty("Required");
        if (requiredRow.ValueKind != JsonValueKind.Array || requiredRow.GetArrayLength() is < 1 or > 3)
            throw new ArgumentException("Consent assessment must preserve the complete required set.");
        var required = requiredRow.EnumerateArray()
            .Select(value => (ModerationCollectionPrerequisiteKind)value.GetInt32()).ToArray();
        if (!required.Contains(ModerationCollectionPrerequisiteKind.SubjectConsent))
            throw new ArgumentException("Consent assessment must retain the subject prerequisite.");
        return new(RestoreBinding(row.GetProperty("Binding")),
            new(row.GetProperty("Id").GetGuid(), row.GetProperty("Revision").GetInt64(),
                row.GetProperty("Generation").GetInt64(), row.GetProperty("ObservedAt").GetDateTimeOffset(),
                row.GetProperty("ValidUntil").GetDateTimeOffset()), required);
    }

    // Explicit UTC ticks make finite windows and relational ordering provider-neutral.
    // Reject offsets rather than silently normalizing a malformed original.
    internal static long EncodeInstant(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero || value == DateTimeOffset.MinValue || value == DateTimeOffset.MaxValue)
            throw new ArgumentException("Consent instants must be finite UTC originals.");
        return value.Ticks;
    }

    internal static DateTimeOffset DecodeInstant(long ticks)
    {
        if (ticks <= 0 || ticks >= DateTimeOffset.MaxValue.Ticks)
            throw new ArgumentException("Consent storage contains a nonfinite instant.");
        return new(ticks, TimeSpan.Zero);
    }

    private static JsonObject BindingRow(ModerationIndividualBinding binding)
    {
        var subject = binding.Target.HumanSubject;
        if (binding.Target.Kind != ModerationIndividualTargetKind.Human
            || subject is not { Kind: ModerationSubjectKind.Human }
            || subject.Origin.Kind != ModerationScopeKind.Instance
            || binding.Purpose != ModerationIndividualPurpose.ProfileCompletion)
            throw new ArgumentException("Consent snapshot requires original Human ProfileCompletion metadata.");
        return new()
        {
            ["TargetKind"] = (int)binding.Target.Kind,
            ["SubjectKind"] = (int)subject.Kind,
            ["SubjectId"] = subject.Id,
            ["Origin"] = ScopeRow(subject.Origin),
            ["Jurisdiction"] = ScopeRow(binding.Jurisdiction),
            ["PolicyId"] = binding.PolicyId,
            ["PolicyRevision"] = binding.PolicyRevision,
            ["Purpose"] = (int)binding.Purpose,
            ["SourceId"] = binding.SourceId,
            ["SourceRevision"] = binding.SourceRevision,
            ["SourceGeneration"] = binding.SourceGeneration,
            ["ObservationStamp"] = binding.ObservationStamp,
            ["ObservationSequence"] = binding.ObservationSequence,
            ["ObservedAt"] = binding.ObservedAt,
            ["ValidUntil"] = binding.ValidUntil
        };
    }

    private static ModerationIndividualBinding RestoreBinding(JsonElement row)
    {
        RequireFields(row, ["TargetKind", "SubjectKind", "SubjectId", "Origin", "Jurisdiction", "PolicyId",
            "PolicyRevision", "Purpose", "SourceId", "SourceRevision", "SourceGeneration",
            "ObservationStamp", "ObservationSequence", "ObservedAt", "ValidUntil"]);
        if (row.GetProperty("TargetKind").GetInt32() != (int)ModerationIndividualTargetKind.Human
            || row.GetProperty("SubjectKind").GetInt32() != (int)ModerationSubjectKind.Human
            || row.GetProperty("Purpose").GetInt32() != (int)ModerationIndividualPurpose.ProfileCompletion)
            throw new ArgumentException("Consent storage cannot alias another subject, target or purpose.");
        var origin = RestoreScope(row.GetProperty("Origin"));
        if (origin.Kind != ModerationScopeKind.Instance)
            throw new ArgumentException("Consent Human requires its original instance origin.");
        return new(ModerationIndividualTarget.Human(SubjectReference.Create(ModerationSubjectKind.Human,
                row.GetProperty("SubjectId").GetGuid(), origin)),
            new ModerationAgePolicy(row.GetProperty("PolicyId").GetGuid(), row.GetProperty("PolicyRevision").GetInt64(),
                RestoreScope(row.GetProperty("Jurisdiction"))), ModerationIndividualPurpose.ProfileCompletion,
            row.GetProperty("SourceId").GetGuid(), row.GetProperty("SourceRevision").GetInt64(),
            row.GetProperty("SourceGeneration").GetInt64(), row.GetProperty("ObservationStamp").GetGuid(),
            row.GetProperty("ObservationSequence").GetInt64(), row.GetProperty("ObservedAt").GetDateTimeOffset(),
            row.GetProperty("ValidUntil").GetDateTimeOffset());
    }

    private static JsonObject ScopeRow(AuthorityScope scope)
    {
        var coordinates = scope.Coordinates;
        if (scope.Kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant))
            throw new ArgumentException("Consent scopes must be exact native instance or tenant coordinates.");
        return new()
        {
            ["Kind"] = (int)scope.Kind,
            ["TrustDomainId"] = coordinates.TrustDomainId,
            ["SolutionId"] = coordinates.SolutionId,
            ["InstanceId"] = coordinates.InstanceId,
            ["TenantId"] = coordinates.TenantId
        };
    }

    private static AuthorityScope RestoreScope(JsonElement row)
    {
        RequireFields(row, ["Kind", "TrustDomainId", "SolutionId", "InstanceId", "TenantId"]);
        var kind = (ModerationScopeKind)row.GetProperty("Kind").GetInt32();
        if (kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant))
            throw new ArgumentException("Consent storage contains an unsupported jurisdiction kind.");
        return AuthorityScope.FromCoordinates(kind, row.GetProperty("TrustDomainId").GetGuid(),
            row.GetProperty("SolutionId").GetGuid(), row.GetProperty("InstanceId").GetGuid(),
            row.GetProperty("TenantId").ValueKind == JsonValueKind.Null ? null : row.GetProperty("TenantId").GetGuid(),
            null, null, null);
    }

    // Exact owner schemas reject missing, unknown, case-aliased and duplicate coordinates.
    private static void RequireFields(JsonElement row, string[] fields)
    {
        if (row.ValueKind != JsonValueKind.Object) throw new JsonException("Consent snapshot must be an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in row.EnumerateObject())
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw new JsonException("Consent snapshot contains an unknown or duplicate coordinate.");
        if (seen.Count != fields.Length) throw new JsonException("Consent snapshot is missing original coordinates.");
    }
}
