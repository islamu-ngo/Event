using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Explore.Application.Features.Moderation.Narratives;

namespace Event.Application.UnitTests.Moderation;

public sealed class ReadModerationCaseNarrativeQueryTests
{
    [Test]
    public async Task ReadCoordinatesOwnBearerMemoryWithoutChangingCallerInput()
    {
        var input = RandomNumberGenerator.GetBytes(32);
        var expected = input.ToArray();
        var query = Request(input);
        input[0] ^= 1;
        await Assert.That(query.PresentedToken.ToArray()).IsEquivalentTo(expected);
        if (MemoryMarshal.TryGetArray(query.PresentedToken, out var exposed))
            CryptographicOperations.ZeroMemory(exposed.Array!);
        await Assert.That(query.PresentedToken.ToArray()).IsEquivalentTo(expected);
        await Assert.That((await new ReadModerationCaseNarrativeQueryValidator().ValidateAsync(query)).IsValid).IsTrue();
    }

    [Test]
    [Arguments("case")]
    [Arguments("receipt")]
    [Arguments("revision")]
    [Arguments("token")]
    public async Task InvalidReadCoordinatesAreRejected(string defect)
    {
        var query = Request(new byte[32]);
        query = defect switch
        {
            "case" => query with { CaseId = Guid.Empty },
            "receipt" => query with { ReceiptId = Guid.Empty },
            "revision" => query with { ExpectedCaseRevision = 0 },
            _ => query with { PresentedToken = new byte[31] }
        };
        await Assert.That((await new ReadModerationCaseNarrativeQueryValidator().ValidateAsync(query)).IsValid).IsFalse();
    }

    private static ReadModerationCaseNarrativeQuery Request(ReadOnlyMemory<byte> token) => new()
    {
        CaseId = Guid.CreateVersion7(), ReceiptId = Guid.CreateVersion7(),
        ExpectedCaseRevision = 1, PresentedToken = token
    };
}
