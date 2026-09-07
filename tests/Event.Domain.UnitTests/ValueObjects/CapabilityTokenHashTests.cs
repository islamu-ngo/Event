using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.ValueObjects;

public sealed class CapabilityTokenHashTests
{
    [Test]
    public async Task Create_RoundTripsCanonicalSha256Hash()
    {
        string value = Convert.ToBase64String(new byte[32]);

        CapabilityTokenHash hash = CapabilityTokenHash.Create(value);

        await Assert.That(hash.Value).IsEqualTo(value);
    }

    [Test]
    public async Task Create_UsesValueEqualityForCanonicalHashes()
    {
        string value = Convert.ToBase64String(new byte[32]);
        byte[] differentBytes = new byte[32];
        differentBytes[0] = 1;

        CapabilityTokenHash first = CapabilityTokenHash.Create(value);
        CapabilityTokenHash second = CapabilityTokenHash.Create(value);
        CapabilityTokenHash different = CapabilityTokenHash.Create(Convert.ToBase64String(differentBytes));

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first).IsNotEqualTo(different);
    }

    [Test]
    public async Task ToString_RedactsThePersistedHashValue()
    {
        string value = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());

        CapabilityTokenHash hash = CapabilityTokenHash.Create(value);

        await Assert.That(hash.ToString()).DoesNotContain(value);
        await Assert.That($"{hash}").DoesNotContain(value);
    }

    [Test]
    public async Task Create_RejectsNonCanonicalOrNonSha256Representations()
    {
        string[] invalidValues =
        [
            string.Empty,
            " ",
            "not-base64",
            Convert.ToBase64String(new byte[31]),
            Convert.ToBase64String(new byte[33]),
            Convert.ToBase64String(new byte[32]) + "\n"
        ];

        foreach (string invalidValue in invalidValues)
        {
            bool rejected = false;

            try
            {
                _ = CapabilityTokenHash.Create(invalidValue);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            await Assert.That(rejected).IsTrue();
        }
    }
}
