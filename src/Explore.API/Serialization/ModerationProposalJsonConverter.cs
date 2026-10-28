using System.Text.Json;
using System.Text.Json.Serialization;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;

namespace Explore.API.Serialization;

/// <summary>Rebuilds a defensive proposal snapshot with the shared capability, quorum and interval invariants intact.</summary>
public sealed class ModerationProposalJsonConverter : JsonConverter<ModerationProposal>
{
    public override bool HandleNull => true;

    public override ModerationProposal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("A moderation proposal requires explicit terms.");
        Guid? caseId = null;
        ModerationRuleReference? rule = null;
        ModerationCapability[]? capabilities = null;
        DateTimeOffset? startsAt = null;
        DateTimeOffset? endsAt = null;
        DateTimeOffset? reviewAt = null;
        int? requiredApprovals = null;
        var endsAtSeen = false;
        var reviewAtSeen = false;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            switch (property.Name)
            {
                case "caseId" when caseId is null:
                    caseId = property.Value.Deserialize<Guid>(options);
                    break;
                case "rule" when rule is null:
                    rule = property.Value.Deserialize<ModerationRuleReference>(options)
                        ?? throw new JsonException("A moderation proposal requires its original rule.");
                    break;
                case "capabilities" when capabilities is null:
                    capabilities = property.Value.Deserialize<ModerationCapability[]>(options)
                        ?? throw new JsonException("A moderation proposal requires restrictive capabilities.");
                    break;
                case "startsAt" when startsAt is null:
                    startsAt = property.Value.Deserialize<DateTimeOffset>(options);
                    break;
                case "endsAt" when !endsAtSeen:
                    endsAtSeen = true;
                    endsAt = property.Value.Deserialize<DateTimeOffset?>(options);
                    break;
                case "reviewAt" when !reviewAtSeen:
                    reviewAtSeen = true;
                    reviewAt = property.Value.Deserialize<DateTimeOffset?>(options);
                    break;
                case "requiredApprovals" when requiredApprovals is null:
                    requiredApprovals = property.Value.Deserialize<int>(options);
                    break;
                default:
                    throw new JsonException("Moderation proposal members must be known and unique.");
            }
        }
        if (caseId is null || rule is null || capabilities is null || startsAt is null || requiredApprovals is null)
            throw new JsonException("A moderation proposal requires its case, rule, capabilities, start and quorum.");
        try
        {
            return new ModerationProposal(caseId.Value, rule, capabilities, startsAt.Value,
                endsAt, reviewAt, requiredApprovals.Value);
        }
        catch (ArgumentException)
        {
            throw new JsonException("The moderation capability, quorum or interval terms are invalid.");
        }
    }

    public override void Write(Utf8JsonWriter writer, ModerationProposal value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteString("caseId", value.CaseId);
        writer.WritePropertyName("rule");
        JsonSerializer.Serialize(writer, value.Rule, options);
        writer.WritePropertyName("capabilities");
        JsonSerializer.Serialize(writer, value.Capabilities, options);
        writer.WriteString("startsAt", value.StartsAt);
        if (value.EndsAt is { } endsAt) writer.WriteString("endsAt", endsAt);
        if (value.ReviewAt is { } reviewAt) writer.WriteString("reviewAt", reviewAt);
        writer.WriteNumber("requiredApprovals", value.RequiredApprovals);
        writer.WriteEndObject();
    }
}
