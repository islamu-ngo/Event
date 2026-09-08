using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.ValueObjects;

public sealed class AtprotoDidTests
{
    [Test]
    [Arguments("did:plc:z72i7hdynmk6r22z27h6tvur")]
    [Arguments("did:web:example.com")]
    [Arguments("did:web:sub.example.com:user:alice")]
    [Arguments("did:custom:validIdentifier-123.456_789")]
    public async Task Parse_ValidDid_ReturnsValueObjectWithExactValue(string rawDid)
    {
        var did = AtprotoDid.Parse(rawDid);

        await Assert.That(did.Value).IsEqualTo(rawDid);
        await Assert.That(did.Method).IsEqualTo(rawDid.Split(':')[1]);
        await Assert.That(did.ToString()).IsEqualTo("[AT Protocol DID]");

        var success = AtprotoDid.TryParse(rawDid, out var parsed);
        await Assert.That(success).IsTrue();
        await Assert.That(parsed.Value).IsEqualTo(rawDid);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("not-a-did")]
    [Arguments("did:")]
    [Arguments("did::")]
    [Arguments("did:PLC:z72i7hdynmk6r22z27h6tvur")] // uppercase method is forbidden
    [Arguments("DID:plc:z72i7hdynmk6r22z27h6tvur")] // uppercase prefix is forbidden
    [Arguments("did:plc:")]                          // empty method-specific identifier
    [Arguments("did:plc:abc:")]                      // trailing colon
    [Arguments("did:plc:abc def")]                   // whitespace
    [Arguments("did:plc:abc\ndef")]                  // newline
    [Arguments("did:plc:abc?query=1")]               // query forbidden
    [Arguments("did:plc:abc#fragment")]              // fragment forbidden
    [Arguments("did:plc:abc%20def")]                 // percent encoding forbidden
    [Arguments("erased-did:deleted-user-123")]       // privacy erasure tombstone is not a live DID
    public async Task Parse_InvalidDid_ThrowsArgumentException(string? invalidDid)
    {
        await Assert.That(() => AtprotoDid.Parse(invalidDid!)).Throws<ArgumentException>();
        await Assert.That(AtprotoDid.TryParse(invalidDid, out _)).IsFalse();
    }

    [Test]
    public async Task Parse_OversizedDid_ThrowsArgumentException()
    {
        var oversizedDid = "did:plc:" + new string('a', 2050);
        await Assert.That(() => AtprotoDid.Parse(oversizedDid)).Throws<ArgumentException>();
        await Assert.That(AtprotoDid.TryParse(oversizedDid, out _)).IsFalse();
    }

    [Test]
    public async Task Equality_IsCaseSensitiveAndExact()
    {
        var did1 = AtprotoDid.Parse("did:plc:z72i7hdynmk6r22z27h6tvur");
        var did2 = AtprotoDid.Parse("did:plc:z72i7hdynmk6r22z27h6tvur");
        var did3 = AtprotoDid.Parse("did:plc:Z72I7HDYNMK6R22Z27H6TVUR");

        await Assert.That(did1).IsEqualTo(did2);
        await Assert.That(did1 == did2).IsTrue();
        await Assert.That(did1.Equals(did2)).IsTrue();

        await Assert.That(did1).IsNotEqualTo(did3);
        await Assert.That(did1 == did3).IsFalse();
        await Assert.That(did1.Equals(did3)).IsFalse();
    }

    [Test]
    public async Task PublicSurface_HasNoImplicitStringConversion()
    {
        bool hasImplicitConversion = typeof(AtprotoDid).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Any(method => method.Name == "op_Implicit");

        await Assert.That(hasImplicitConversion).IsFalse();
    }

    [Test]
    public async Task Parse_InvalidSentinelAndOversizedValuesHaveBoundedValueFreeDiagnostics()
    {
        const string sentinel = "did:plc:diagnostic-sentinel?raw";
        string oversized = "did:plc:" + new string('z', 2050);

        foreach (string invalid in new[] { sentinel, oversized })
        {
            ArgumentException? exception = await Assert.That(() => AtprotoDid.Parse(invalid))
                .Throws<ArgumentException>();

            await Assert.That(exception!.Message).DoesNotContain(invalid);
            await Assert.That(exception.Message).DoesNotContain("diagnostic-sentinel");
            await Assert.That(exception.Message.Length).IsLessThan(160);
        }
    }
}
