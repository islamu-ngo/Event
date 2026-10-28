using System.Security.Cryptography;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationMeasurementIssuerEnrollmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Revocation_IsAccountableTerminalAndPreservesOriginalTrustMetadata()
    {
        var source = Enroll();
        var original = (source.Id, source.HostBindingId, source.TrustDomainId, source.TenantId, source.KeyId,
            source.Issuer, source.PublicKeySpkiBase64, source.RunnerDigest, source.CatalogueDigest,
            source.TestBundleDigest, source.ApprovedByHumanId, source.WriterGeneration, source.ApprovedAt, source.ValidUntil);
        await Assert.That(source.Revision).IsEqualTo(1L);
        await Assert.That(source.RevokedAt).IsNull();
        await Assert.That(source.RevokedByHumanId).IsNull();
        var human = Guid.NewGuid();
        source.Revoke(1, human, Now.AddMinutes(1));
        source.ValidatePersistedState();
        await Assert.That(source.Revision).IsEqualTo(2L);
        await Assert.That(source.RevokedByHumanId).IsEqualTo(human);
        await Assert.That(source.RevokedAt).IsEqualTo(Now.AddMinutes(1));
        await Assert.That((source.Id, source.HostBindingId, source.TrustDomainId, source.TenantId, source.KeyId,
            source.Issuer, source.PublicKeySpkiBase64, source.RunnerDigest, source.CatalogueDigest,
            source.TestBundleDigest, source.ApprovedByHumanId, source.WriterGeneration, source.ApprovedAt, source.ValidUntil))
            .IsEqualTo(original);
        await Assert.That(() => source.Revoke(2, human, Now.AddMinutes(2))).Throws<InvalidOperationException>();
        await Assert.That(() => source.Revoke(1, human, Now.AddMinutes(2))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Lifecycle_NormalizesInstantsToUtcWithoutChangingTheirMeaning()
    {
        var source = Enroll(approvedAt: Now.ToOffset(TimeSpan.FromHours(2)),
            validUntil: Now.AddMinutes(5).ToOffset(TimeSpan.FromHours(-3)));
        source.Revoke(1, Guid.NewGuid(), Now.ToOffset(TimeSpan.FromHours(4)));
        source.ValidatePersistedState();
        await Assert.That(source.ApprovedAt.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(source.ValidUntil.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(source.RevokedAt!.Value.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(source.ApprovedAt).IsEqualTo(Now);
        await Assert.That(source.ValidUntil).IsEqualTo(Now.AddMinutes(5));
        await Assert.That(source.RevokedAt).IsEqualTo(Now);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" issuer")]
    [Arguments("issuer ")]
    [Arguments("issuer name")]
    [Arguments("issuer\tname")]
    [Arguments("issuer\nname")]
    [Arguments("issuer\u007f")]
    [Arguments("iss\u00fcer")]
    [Arguments("issuer/name")]
    public async Task Enrollment_RejectsNoncanonicalIssuerCodes(string? issuer)
    {
        await Assert.That(() => Enroll(issuer: issuer!)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Enrollment_BoundsIssuerCodeWithoutSilentlyTrimming()
    {
        await Assert.That(Enroll(issuer: new string('a', 128)).Issuer).IsEqualTo(new string('a', 128));
        await Assert.That(() => Enroll(issuer: new string('a', 129))).Throws<ArgumentException>();
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
    public async Task Enrollment_RejectsEmptyMalformedOversizedOrNoncanonicalPublicKeyEncoding(int change)
    {
        var publicKey = PublicKey();
        var invalid = change switch
        {
            0 => null,
            1 => string.Empty,
            2 => " " + publicKey,
            3 => publicKey + "\n",
            4 => publicKey[..^1],
            5 => "!" + publicKey[1..],
            6 => publicKey[..^3] + "B==",
            _ => Convert.ToBase64String(new byte[769])
        };
        await Assert.That(() => ModerationMeasurementIssuerEnrollment.Enroll(Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "measurement.runner-v1", invalid!, new string('a', 64), new string('b', 64),
            new string('c', 64), Guid.NewGuid(), 2, Now, Now.AddMinutes(5))).Throws<ArgumentException>();
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
    [Arguments(9)]
    [Arguments(10)]
    public async Task Enrollment_RequiresUuidV7CoordinatesAndSeparateKeyIdentity(int change)
    {
        var id = Guid.CreateVersion7();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToArray();
        ids[0] = id;
        if (change < 10)
            ids[change % 5] = change < 5 ? Guid.Empty : Guid.NewGuid();
        else
            ids[4] = id;
        await Assert.That(() => ModerationMeasurementIssuerEnrollment.Enroll(ids[0], ids[1], ids[2], ids[3],
            ids[4], "measurement.runner-v1", PublicKey(), new string('a', 64), new string('b', 64),
            new string('c', 64), Guid.NewGuid(), 2, Now, Now.AddMinutes(5))).Throws<ArgumentException>();
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
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    [Arguments(14)]
    public async Task Enrollment_RequiresExactLowercaseHexForEveryExecutableDigest(int change)
    {
        var digests = new[] { new string('a', 64), new string('b', 64), new string('c', 64) };
        digests[change / 5] = (change % 5) switch
        {
            0 => null!,
            1 => new string('a', 63),
            2 => new string('a', 65),
            3 => new string('A', 64),
            _ => new string('g', 64)
        };
        await Assert.That(() => Enroll(runnerDigest: digests[0], catalogueDigest: digests[1],
            testBundleDigest: digests[2])).Throws<ArgumentException>();
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
    public async Task Enrollment_RejectsUnaccountableInvalidEpochAndUnboundedWindows(int change)
    {
        await Assert.That(() => Enroll(human: change == 0 ? Guid.Empty : Guid.NewGuid(),
            writerGeneration: change switch { 1 => 0, 2 => -1, 3 => long.MaxValue, _ => 2 },
            approvedAt: change == 4 ? DateTimeOffset.MinValue : Now,
            validUntil: change switch { 5 => Now, 6 => Now.AddTicks(-1), 7 => DateTimeOffset.MaxValue, _ => Now.AddMinutes(5) }))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task Revocation_RejectsStaleRevisionUnaccountableHumanAndInvalidTimeWithoutMutation(int change)
    {
        var source = Enroll();
        Action revoke = () => source.Revoke(change switch { 0 => 0, 1 => 2, _ => 1 },
            change == 2 ? Guid.Empty : Guid.NewGuid(),
            change switch { 3 => Now.AddTicks(-1), 4 => DateTimeOffset.MaxValue, _ => Now });
        if (change < 2)
            await Assert.That(revoke).Throws<InvalidOperationException>();
        else
            await Assert.That(revoke).Throws<ArgumentException>();
        source.ValidatePersistedState();
        await Assert.That(source.Revision).IsEqualTo(1L);
        await Assert.That(source.RevokedAt).IsNull();
        await Assert.That(source.RevokedByHumanId).IsNull();
    }

    [Test]
    public async Task ExpiredEnrollment_CanStillBeAccountablyRevoked()
    {
        var source = Enroll();
        source.Revoke(1, Guid.NewGuid(), Now.AddHours(1));
        source.ValidatePersistedState();
        await Assert.That(source.Revision).IsEqualTo(2L);
        await Assert.That(source.RevokedAt).IsEqualTo(Now.AddHours(1));
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
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    public async Task PersistedState_RejectsCorruptRevisionAndPartialOrInvalidRevocation(int change)
    {
        var source = Enroll();
        if (change >= 4)
            source.Revoke(1, Guid.NewGuid(), Now.AddMinutes(1));
        var (property, value) = change switch
        {
            0 => (nameof(source.Revision), (object)0L),
            1 => (nameof(source.Revision), (object)3L),
            2 => (nameof(source.RevokedAt), (object)Now),
            3 => (nameof(source.RevokedByHumanId), (object)Guid.NewGuid()),
            4 => (nameof(source.RevokedAt), null),
            5 => (nameof(source.RevokedByHumanId), null),
            6 => (nameof(source.RevokedByHumanId), (object)Guid.Empty),
            7 => (nameof(source.RevokedAt), (object)Now.AddTicks(-1)),
            8 => (nameof(source.RevokedAt), (object)DateTimeOffset.MaxValue),
            9 => (nameof(source.RevokedAt), (object)Now.ToOffset(TimeSpan.FromHours(1))),
            10 => (nameof(source.ApprovedAt), (object)Now.ToOffset(TimeSpan.FromHours(1))),
            _ => (nameof(source.ValidUntil), (object)Now.AddMinutes(5).ToOffset(TimeSpan.FromHours(1)))
        };
        // Simulate scalar materialization of a malformed stored row, then exercise the public validator.
        typeof(ModerationMeasurementIssuerEnrollment).GetProperty(property)!.SetValue(source, value);
        await Assert.That(() => source.ValidatePersistedState()).Throws<ArgumentException>();
    }

    private static ModerationMeasurementIssuerEnrollment Enroll(
        string issuer = "measurement.runner-v1", string? publicKey = null,
        string runnerDigest = "", string catalogueDigest = "", string testBundleDigest = "",
        Guid? human = null, long writerGeneration = 2,
        DateTimeOffset? approvedAt = null, DateTimeOffset? validUntil = null) =>
        ModerationMeasurementIssuerEnrollment.Enroll(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), issuer, publicKey ?? PublicKey(),
            runnerDigest == "" ? new string('a', 64) : runnerDigest,
            catalogueDigest == "" ? new string('b', 64) : catalogueDigest,
            testBundleDigest == "" ? new string('c', 64) : testBundleDigest,
            human ?? Guid.NewGuid(), writerGeneration, approvedAt ?? Now, validUntil ?? Now.AddMinutes(5));

    private static string PublicKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }
}
