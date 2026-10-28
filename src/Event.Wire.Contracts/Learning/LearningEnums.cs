using System.Text.Json;
using System.Text.Json.Serialization;

namespace ISLAMU.Wire.Contracts.Learning;

[JsonConverter(typeof(LearningStringEnumJsonConverter<LearningTourFamily>))]
public enum LearningTourFamily
{
    Shell = 1,
    Studio = 2,
    EventManagement = 3,
    NormalAi = 4
}

[JsonConverter(typeof(LearningStringEnumJsonConverter<LearningTourOutcome>))]
public enum LearningTourOutcome
{
    InProgress = 1,
    Completed = 2,
    Skipped = 3,
    Interrupted = 4
}

[JsonConverter(typeof(LearningStringEnumJsonConverter<LearningPracticeAction>))]
public enum LearningPracticeAction
{
    Start = 1,
    Choose = 2,
    Confirm = 3,
    Reset = 4,
    Clear = 5
}

[JsonConverter(typeof(LearningStringEnumJsonConverter<LearningPracticeChoiceSlot>))]
public enum LearningPracticeChoiceSlot
{
    None = 0,
    Primary = 1,
    Secondary = 2,
    Tertiary = 3
}

[JsonConverter(typeof(LearningStringEnumJsonConverter<LearningSpaceAction>))]
public enum LearningSpaceAction
{
    Remove = 1,
    Recreate = 2
}

[JsonConverter(typeof(LearningStringEnumJsonConverter<LearningSpaceStatus>))]
public enum LearningSpaceStatus
{
    Absent = 0,
    Active = 1,
    Removed = 2
}

[JsonConverter(typeof(LearningStringEnumJsonConverter<LearningSimulationStatus>))]
public enum LearningSimulationStatus
{
    Choosing = 1,
    AwaitingConfirmation = 2,
    Simulated = 3,
    Cleared = 4
}

internal sealed class LearningStringEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String
            || !Enum.TryParse(reader.GetString(), ignoreCase: false, out TEnum value)
            || !Enum.IsDefined(value)
            || !string.Equals(Enum.GetName(value), reader.GetString(), StringComparison.Ordinal))
        {
            throw new JsonException("Learning enum values must be exact named strings.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Enum.GetName(value)
            ?? throw new JsonException("Unknown learning enum value."));
}
