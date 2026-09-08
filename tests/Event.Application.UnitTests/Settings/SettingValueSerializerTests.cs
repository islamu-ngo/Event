namespace Event.Application.UnitTests.Settings;

using Explore.Application.Settings;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public class SettingValueSerializerTests
{
    // --- String deserialization ---

    [Test]
    public async Task DeserializeString_WithJsonString_ReturnsValue()
    {
        var result = SettingValueSerializer.DeserializeString("\"hello\"");
        await Assert.That(result).IsEqualTo("hello");
    }

    [Test]
    public async Task DeserializeString_WithNull_ReturnsDefault()
    {
        var result = SettingValueSerializer.DeserializeString(null, "fallback");
        await Assert.That(result).IsEqualTo("fallback");
    }

    [Test]
    public async Task DeserializeString_WithEmpty_ReturnsDefault()
    {
        var result = SettingValueSerializer.DeserializeString("", "fallback");
        await Assert.That(result).IsEqualTo("fallback");
    }

    [Test]
    public async Task DeserializeString_WithWhitespace_ReturnsDefault()
    {
        var result = SettingValueSerializer.DeserializeString("   ", "fallback");
        await Assert.That(result).IsEqualTo("fallback");
    }

    [Test]
    public async Task DeserializeString_WithUnquotedText_TrimsQuotes()
    {
        var result = SettingValueSerializer.DeserializeString("plain text");
        await Assert.That(result).IsEqualTo("plain text");
    }

    [Test]
    public async Task DeserializeString_WithEmptyJsonString_ReturnsDefault()
    {
        var result = SettingValueSerializer.DeserializeString("\"\"", "fallback");
        await Assert.That(result).IsEqualTo("fallback");
    }

    // --- Int deserialization ---

    [Test]
    public async Task DeserializeInt_WithJsonNumber_ReturnsValue()
    {
        var result = SettingValueSerializer.DeserializeInt("587");
        await Assert.That(result).IsEqualTo(587);
    }

    [Test]
    public async Task DeserializeInt_WithNull_ReturnsDefault()
    {
        var result = SettingValueSerializer.DeserializeInt(null, 42);
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task DeserializeInt_WithMalformedJson_FallsBackToParse()
    {
        var result = SettingValueSerializer.DeserializeInt("not_a_number", 99);
        await Assert.That(result).IsEqualTo(99);
    }

    // --- Bool deserialization ---

    [Test]
    public async Task DeserializeBool_WithJsonTrue_ReturnsTrue()
    {
        var result = SettingValueSerializer.DeserializeBool("true");
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task DeserializeBool_WithJsonFalse_ReturnsFalse()
    {
        var result = SettingValueSerializer.DeserializeBool("false");
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task DeserializeBool_WithNull_ReturnsDefault()
    {
        var result = SettingValueSerializer.DeserializeBool(null, true);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task DeserializeBool_WithMalformed_ReturnsDefault()
    {
        var result = SettingValueSerializer.DeserializeBool("maybe", false);
        await Assert.That(result).IsFalse();
    }

    // --- Decimal deserialization ---

    [Test]
    public async Task DeserializeDecimal_WithJsonNumber_ReturnsValue()
    {
        var result = SettingValueSerializer.DeserializeDecimal("3.14");
        await Assert.That(result).IsEqualTo(3.14m);
    }

    [Test]
    public async Task DeserializeDecimal_WithNull_ReturnsDefault()
    {
        var result = SettingValueSerializer.DeserializeDecimal(null, 1.0m);
        await Assert.That(result).IsEqualTo(1.0m);
    }

    // --- Generic Deserialize<T> ---

    [Test]
    public async Task Deserialize_GenericInt_Works()
    {
        var result = SettingValueSerializer.Deserialize("100", 0);
        await Assert.That(result).IsEqualTo(100);
    }

    [Test]
    public async Task Deserialize_GenericBool_Works()
    {
        var result = SettingValueSerializer.Deserialize("true", false);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Deserialize_GenericString_Works()
    {
        var result = SettingValueSerializer.Deserialize("\"test\"", "default");
        await Assert.That(result).IsEqualTo("test");
    }

    // --- Serialize ---

    [Test]
    public async Task Serialize_String_ProducesJsonString()
    {
        var result = SettingValueSerializer.Serialize("hello");
        await Assert.That(result).IsEqualTo("\"hello\"");
    }

    [Test]
    public async Task Serialize_Int_ProducesJsonNumber()
    {
        var result = SettingValueSerializer.Serialize(42);
        await Assert.That(result).IsEqualTo("42");
    }

    [Test]
    public async Task Serialize_Bool_ProducesJsonBool()
    {
        var result = SettingValueSerializer.Serialize(true);
        await Assert.That(result).IsEqualTo("true");
    }

    // --- Roundtrip ---

    [Test]
    public async Task Roundtrip_StringValue_Preserves()
    {
        var original = "Hello World";
        var serialized = SettingValueSerializer.Serialize(original);
        var deserialized = SettingValueSerializer.DeserializeString(serialized);
        await Assert.That(deserialized).IsEqualTo(original);
    }

    [Test]
    public async Task Roundtrip_IntValue_Preserves()
    {
        var original = 9999;
        var serialized = SettingValueSerializer.Serialize(original);
        var deserialized = SettingValueSerializer.DeserializeInt(serialized);
        await Assert.That(deserialized).IsEqualTo(original);
    }

    [Test]
    public async Task Roundtrip_BoolValue_Preserves()
    {
        var original = true;
        var serialized = SettingValueSerializer.Serialize(original);
        var deserialized = SettingValueSerializer.DeserializeBool(serialized);
        await Assert.That(deserialized).IsEqualTo(original);
    }
}
