namespace ISLAMU.Wire.Contracts.SetupLive;

using System.Text.Json;
using System.Text.Json.Serialization;

internal abstract class SetupLiveStringEnumJsonConverter<TEnum>
    : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public sealed override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Setup live enum values must be strings.");

        string? wireValue = reader.GetString();
        if (wireValue is null || !TryParse(wireValue, out TEnum value))
            throw new JsonException("Unknown Setup live enum value.");

        return value;
    }

    public sealed override void Write(
        Utf8JsonWriter writer,
        TEnum value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(Format(value));

    protected abstract bool TryParse(string wireValue, out TEnum value);

    protected abstract string Format(TEnum value);
}

internal sealed class SetupEnrollmentScopeJsonConverter
    : SetupLiveStringEnumJsonConverter<SetupEnrollmentScope>
{
    protected override bool TryParse(
        string wireValue,
        out SetupEnrollmentScope value) =>
        SetupLiveEnumWire.TryParse(wireValue, out value);

    protected override string Format(SetupEnrollmentScope value) =>
        SetupLiveEnumWire.Format(value);
}

internal sealed class SetupEnrollmentStateJsonConverter
    : SetupLiveStringEnumJsonConverter<SetupEnrollmentState>
{
    protected override bool TryParse(
        string wireValue,
        out SetupEnrollmentState value) =>
        SetupLiveEnumWire.TryParse(wireValue, out value);

    protected override string Format(SetupEnrollmentState value) =>
        SetupLiveEnumWire.Format(value);
}

internal sealed class SetupEnrollmentIssuanceJsonConverter
    : SetupLiveStringEnumJsonConverter<SetupEnrollmentIssuance>
{
    protected override bool TryParse(
        string wireValue,
        out SetupEnrollmentIssuance value) =>
        SetupLiveEnumWire.TryParse(wireValue, out value);

    protected override string Format(SetupEnrollmentIssuance value) =>
        SetupLiveEnumWire.Format(value);
}

internal sealed class SetupSecretBindingReadinessStateJsonConverter
    : SetupLiveStringEnumJsonConverter<SetupSecretBindingReadinessState>
{
    protected override bool TryParse(
        string wireValue,
        out SetupSecretBindingReadinessState value) =>
        SetupLiveEnumWire.TryParse(wireValue, out value);

    protected override string Format(SetupSecretBindingReadinessState value) =>
        SetupLiveEnumWire.Format(value);
}

internal sealed class SetupSecretBindingOperationStateJsonConverter
    : SetupLiveStringEnumJsonConverter<SetupSecretBindingOperationState>
{
    protected override bool TryParse(
        string wireValue,
        out SetupSecretBindingOperationState value) =>
        SetupLiveEnumWire.TryParse(wireValue, out value);

    protected override string Format(SetupSecretBindingOperationState value) =>
        SetupLiveEnumWire.Format(value);
}

internal sealed class SetupSecretBindingOperationOutcomeJsonConverter
    : SetupLiveStringEnumJsonConverter<SetupSecretBindingOperationOutcome>
{
    protected override bool TryParse(
        string wireValue,
        out SetupSecretBindingOperationOutcome value) =>
        SetupLiveEnumWire.TryParse(wireValue, out value);

    protected override string Format(SetupSecretBindingOperationOutcome value) =>
        SetupLiveEnumWire.Format(value);
}
