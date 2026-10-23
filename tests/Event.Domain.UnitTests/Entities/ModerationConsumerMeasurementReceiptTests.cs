using System.Security.Cryptography;
using System.Text;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationConsumerMeasurementReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    // Inert compact-JWS spelling, not an authenticated receipt or a credential.
    private const string Token = "e30.e30.AA";
    private const string TokenDigest = "4357466b02e9ee7980d03934e529bb5fa13485bae584cd2b0e2434eaf72dd161";

    [Test]
    public async Task Acceptance_RetainsExactOriginalCoordinatesAndBytesWithoutAuthenticatingThem()
    {
        var ids = Enumerable.Range(0, 8).Select(_ => Guid.CreateVersion7()).ToArray();
        var human = Guid.NewGuid();
        var receipt = ModerationConsumerMeasurementReceipt.Accept(ids[0], ids[1], ids[2], ids[3],
            ids[4], ids[5], ids[6], ids[7], Token, TokenDigest, human, 4, Now, Now.AddMinutes(1));
        receipt.ValidatePersistedState();
        await Assert.That((receipt.Id, receipt.HostBindingId, receipt.TrustDomainId, receipt.TenantId,
            receipt.ChallengeId, receipt.IssuerEnrollmentId, receipt.IssuerKeyId, receipt.RunId))
            .IsEqualTo((ids[0], ids[1], ids[2], ids[3], ids[4], ids[5], ids[6], ids[7]));
        await Assert.That(receipt.SignedToken).IsEqualTo(Token);
        await Assert.That(receipt.TokenDigest).IsEqualTo(TokenDigest);
        await Assert.That(receipt.AcceptedByHumanId).IsEqualTo(human);
        await Assert.That(receipt.WriterGeneration).IsEqualTo(4L);
        await Assert.That(receipt.Revision).IsEqualTo(1L);
        await Assert.That(receipt.AcceptedAt).IsEqualTo(Now);
        await Assert.That(receipt.ValidUntil).IsEqualTo(Now.AddMinutes(1));
        await Assert.That(receipt.ToString()).IsEqualTo(nameof(ModerationConsumerMeasurementReceipt));
    }

    [Test]
    public async Task Acceptance_NormalizesInstantsWithoutChangingTheRetainedWindow()
    {
        var receipt = Accept(acceptedAt: Now.ToOffset(TimeSpan.FromHours(2)),
            validUntil: Now.AddMinutes(1).ToOffset(TimeSpan.FromHours(-3)));
        receipt.ValidatePersistedState();
        await Assert.That(receipt.AcceptedAt.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(receipt.ValidUntil.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(receipt.AcceptedAt).IsEqualTo(Now);
        await Assert.That(receipt.ValidUntil).IsEqualTo(Now.AddMinutes(1));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task Acceptance_RequiresCanonicalUuidV7ForEveryDomainCoordinate(int coordinate)
    {
        var ids = Enumerable.Range(0, 8).Select(_ => Guid.CreateVersion7()).ToArray();
        foreach (var invalid in new[] { Guid.Empty, Guid.NewGuid(),
            Guid.Parse("019a0000-0000-7000-0000-000000000001") })
        {
            ids[coordinate] = invalid;
            await Assert.That(() => ModerationConsumerMeasurementReceipt.Accept(ids[0], ids[1], ids[2],
                ids[3], ids[4], ids[5], ids[6], ids[7], Token, TokenDigest, Guid.NewGuid(),
                4, Now, Now.AddMinutes(1))).Throws<ArgumentException>();
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("e30.e30")]
    [Arguments("e30.e30.AA.AA")]
    [Arguments(".e30.AA")]
    [Arguments("e30..AA")]
    [Arguments("e30.e30.")]
    [Arguments(" e30.e30.AA")]
    [Arguments("e30.e30.AA\n")]
    [Arguments("e30.e30.A=")]
    [Arguments("e30.e30.A+")]
    [Arguments("e30.e30.A/")]
    [Arguments("e30.e30.\u00e9A")]
    [Arguments("e30.e30.A")]
    [Arguments("e30.e30.AB")]
    [Arguments("e31.e30.AA")]
    [Arguments("e30.e31.AA")]
    public async Task Acceptance_RejectsMalformedOrAlternateCompactJwsSpelling(string? token)
    {
        await Assert.That(() => Accept(token: token!, digest: token is null ? TokenDigest : Digest(token)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Acceptance_BoundsExactAsciiTokenAtSixtyFourKibibytes()
    {
        var token = "e30." + new string('A', 65528) + ".AAA";
        var receipt = Accept(token: token, digest: Digest(token));
        await Assert.That(receipt.SignedToken.Length).IsEqualTo(65536);
        await Assert.That(receipt.SignedToken).IsEqualTo(token);
        var oversized = "e30." + new string('A', 65530) + ".AAA";
        await Assert.That(() => Accept(token: oversized, digest: Digest(oversized))).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("4357466b02e9ee7980d03934e529bb5fa13485bae584cd2b0e2434eaf72dd16")]
    [Arguments("4357466b02e9ee7980d03934e529bb5fa13485bae584cd2b0e2434eaf72dd1610")]
    [Arguments("4357466B02E9EE7980D03934E529BB5FA13485BAE584CD2B0E2434EAF72DD161")]
    [Arguments("g357466b02e9ee7980d03934e529bb5fa13485bae584cd2b0e2434eaf72dd161")]
    [Arguments("0357466b02e9ee7980d03934e529bb5fa13485bae584cd2b0e2434eaf72dd161")]
    public async Task Acceptance_RequiresMatchingCanonicalSha256OfExactToken(string? digest)
    {
        await Assert.That(() => Accept(digest: digest!)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Acceptance_CannotReuseDigestForDifferentCanonicalToken()
    {
        await Assert.That(() => Accept(token: "e30.e30.AQ")).Throws<ArgumentException>();
    }

    [Test]
    public async Task RetainedReceipt_ExposesNoPublicScalarRewrite()
    {
        await Assert.That(typeof(ModerationConsumerMeasurementReceipt).GetProperties()
            .All(property => property.SetMethod is { IsPrivate: true })).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    public async Task Acceptance_RequiresAccountableHumanFiniteEpochAndFutureFiniteWindow(int change)
    {
        await Assert.That(() => Accept(human: change == 0 ? Guid.Empty : Guid.NewGuid(),
            generation: change switch { 1 => 0, 2 => -1, 3 => long.MaxValue, _ => 4 },
            acceptedAt: change switch { 4 => DateTimeOffset.MinValue, 5 => DateTimeOffset.MaxValue, _ => Now },
            validUntil: change switch
            {
                6 => Now,
                7 => Now.AddTicks(-1),
                8 => DateTimeOffset.MaxValue,
                _ => Now.AddMinutes(1)
            })).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task PersistedState_RejectsCorruptOriginalGenerationRevisionBytesAndWindow(int change)
    {
        var receipt = Accept();
        var (property, value) = change switch
        {
            0 => (nameof(receipt.Revision), (object)2L),
            1 => (nameof(receipt.WriterGeneration), (object)long.MaxValue),
            2 => (nameof(receipt.SignedToken), (object)"e30.e30.AQ"),
            3 => (nameof(receipt.AcceptedByHumanId), (object)Guid.Empty),
            4 => (nameof(receipt.AcceptedAt), (object)Now.ToOffset(TimeSpan.FromHours(1))),
            5 => (nameof(receipt.ValidUntil), (object)Now.AddMinutes(1).ToOffset(TimeSpan.FromHours(1))),
            6 => (nameof(receipt.ValidUntil), (object)DateTimeOffset.MaxValue),
            _ => (nameof(receipt.ValidUntil), (object)Now)
        };
        // Simulate malformed scalar materialization; there is no domain transition rewriting a receipt.
        typeof(ModerationConsumerMeasurementReceipt).GetProperty(property)!.SetValue(receipt, value);
        await Assert.That(() => receipt.ValidatePersistedState()).Throws<ArgumentException>();
    }

    private static ModerationConsumerMeasurementReceipt Accept(
        string token = Token, string digest = TokenDigest, Guid? human = null, long generation = 4,
        DateTimeOffset? acceptedAt = null, DateTimeOffset? validUntil = null) =>
        ModerationConsumerMeasurementReceipt.Accept(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), token, digest, human ?? Guid.NewGuid(),
            generation, acceptedAt ?? Now, validUntil ?? Now.AddMinutes(1));

    private static string Digest(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
}
