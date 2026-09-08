namespace ISLAMU.Wire.Contracts.SetupLive;

using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class SetupClientChallengeJsonConverter
    : JsonConverter<SetupClientChallenge>
{
    public override bool HandleNull => true;

    public override SetupClientChallenge Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String
            || !SetupClientChallenge.TryCreate(
                reader.GetString(),
                out SetupClientChallenge? challenge))
        {
            throw new JsonException(
                "Setup client challenge must use canonical SHA-256 Base64url syntax.");
        }

        return challenge!;
    }

    public override void Write(
        Utf8JsonWriter writer,
        SetupClientChallenge value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.EncodedValue);
    }
}

internal sealed class SetupEnrollmentScopeListJsonConverter
    : JsonConverter<IReadOnlyList<SetupEnrollmentScope>>
{
    public override bool HandleNull => true;

    public override IReadOnlyList<SetupEnrollmentScope> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Setup enrollment scopes must be an array.");

        var scopes = new List<SetupEnrollmentScope>(3);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String
                || !SetupLiveEnumWire.TryParse(
                    reader.GetString(),
                    out SetupEnrollmentScope scope)
                || scopes.Contains(scope))
            {
                throw new JsonException(
                    "Setup enrollment scopes must be unique closed string values.");
            }

            scopes.Add(scope);
            if (scopes.Count > 3)
                throw new JsonException("Too many Setup enrollment scopes.");
        }

        if (reader.TokenType != JsonTokenType.EndArray || scopes.Count == 0)
            throw new JsonException("Setup enrollment scopes cannot be empty.");

        return SetupLiveSnapshot.ScopeList(scopes);
    }

    public override void Write(
        Utf8JsonWriter writer,
        IReadOnlyList<SetupEnrollmentScope> value,
        JsonSerializerOptions options)
    {
        IReadOnlyList<SetupEnrollmentScope> scopes =
            SetupLiveSnapshot.ScopeList(value);
        writer.WriteStartArray();
        foreach (SetupEnrollmentScope scope in scopes)
            writer.WriteStringValue(SetupLiveEnumWire.Format(scope));
        writer.WriteEndArray();
    }
}
