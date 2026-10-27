using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Moderation;

public class ModerationMachineSubjectBindingTests
{
    private static readonly DateTimeOffset RevocationTime = new(2026, 10, 9, 4, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ExplicitCoordinatesArePreservedWithoutDerivingPrincipalIdentity()
    {
        var ids = Ids();
        var binding = Create(ids, keyId: "  Opaque-Key  ");

        await Assert.That(binding.Id).IsEqualTo(ids[0]);
        await Assert.That(binding.HostBindingId).IsEqualTo(ids[1]);
        await Assert.That(binding.CredentialSource).IsEqualTo(ModerationMachineCredentialSource.Persisted);
        await Assert.That(binding.KeyId).IsEqualTo("  Opaque-Key  ");
        await Assert.That(binding.AuthorityTenantId).IsEqualTo((Guid?)ids[2]);
        await Assert.That(binding.OwnerType).IsEqualTo(ExternalApiKeyOwnerType.Organization);
        await Assert.That(binding.OwnerId).IsEqualTo(ids[3]);
        await Assert.That(binding.ServicePrincipalId).IsEqualTo(ids[4]);
        await Assert.That(binding.Revision).IsEqualTo(1L);
        await Assert.That(binding.RevokedAt).IsNull();
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task ConflictingIdentifiersCannotRewriteHistoricalCoordinates(int coordinate)
    {
        var ids = Ids();
        var binding = Create(ids);
        var changed = ids.ToArray();
        changed[coordinate] = Guid.CreateVersion7();

        await Assert.That(() => RequireMatching(binding, changed)).Throws<InvalidOperationException>();
        RequireMatching(binding, ids);
        await Assert.That(binding.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task ExactSourceTenantOwnerAndOrdinalKeyAreRequiredForMatching()
    {
        var ids = Ids();
        var binding = Create(ids);

        await Assert.That(() => RequireMatching(binding, ids, source: ModerationMachineCredentialSource.Configured))
            .Throws<InvalidOperationException>();
        await Assert.That(() => RequireMatching(binding, ids, ownerType: ExternalApiKeyOwnerType.User))
            .Throws<InvalidOperationException>();
        await Assert.That(() => RequireMatching(binding, ids, keyId: "opaque-key"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => binding.RequireMatchingCoordinates(
            ids[1], ModerationMachineCredentialSource.Persisted, "Opaque-Key", null,
            ExternalApiKeyOwnerType.Organization, ids[3], ids[4])).Throws<InvalidOperationException>();
        RequireMatching(binding, ids);
    }

    [Test]
    public async Task ReplacementCredentialsCanRetainTheSameCanonicalPrincipal()
    {
        var ids = Ids();
        var original = Create(ids);
        var replacementIds = ids.ToArray();
        replacementIds[0] = Guid.CreateVersion7();
        var replacement = Create(replacementIds, keyId: "Replacement-Key",
            source: ModerationMachineCredentialSource.Configured);
        original.Revoke(1, RevocationTime);

        await Assert.That(replacement.ServicePrincipalId).IsEqualTo(original.ServicePrincipalId);
        await Assert.That(replacement.Id).IsNotEqualTo(original.Id);
        await Assert.That(original.RevokedAt).IsEqualTo((DateTimeOffset?)RevocationTime);
        await Assert.That(replacement.RevokedAt).IsNull();
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task AbsentAuthorityTenantAcceptsKnownOwnersWithoutTenantFallback(int ownerType)
    {
        var ids = Ids();
        var binding = ModerationMachineSubjectBinding.Establish(
            ids[0], ids[1], ModerationMachineCredentialSource.Configured, "Instance-Key", null,
            (ExternalApiKeyOwnerType)ownerType, ids[3], ids[4]);

        await Assert.That(binding.AuthorityTenantId).IsNull();
        await Assert.That(binding.OwnerType).IsEqualTo((ExternalApiKeyOwnerType)ownerType);
        binding.RequireMatchingCoordinates(ids[1], ModerationMachineCredentialSource.Configured,
            "Instance-Key", null, (ExternalApiKeyOwnerType)ownerType, ids[3], ids[4]);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task EmptyNonVersionSevenAndInvalidVariantIdentifiersAreRejected(int coordinate)
    {
        foreach (var invalid in new[]
        {
            Guid.Empty,
            Guid.Parse("12345678-1234-4234-8234-123456789abc"),
            Guid.Parse("12345678-1234-7234-0234-123456789abc")
        })
        {
            var ids = Ids();
            ids[coordinate] = invalid;
            await Assert.That(() => Create(ids)).Throws<ArgumentException>();
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(3)]
    [Arguments(6)]
    public async Task UnknownCredentialSourcesAreRejected(int source)
    {
        await Assert.That(() => Create(Ids(), source: (ModerationMachineCredentialSource)source))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(6)]
    public async Task UnknownOwnerTypesAreRejected(int ownerType)
    {
        await Assert.That(() => Create(Ids(), ownerType: (ExternalApiKeyOwnerType)ownerType))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task KeyIdentifierMustBeNonemptyAndWithinNativeLength()
    {
        foreach (var invalid in new string?[] { null, "", " \r\n\t ", new('x', 65) })
        {
            await Assert.That(() => Create(Ids(), keyId: invalid!)).Throws<ArgumentException>();
        }
        var binding = Create(Ids(), keyId: new string('x', 64));
        await Assert.That(binding.KeyId.Length).IsEqualTo(64);
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    [Arguments(2L)]
    public async Task StaleRevocationCannotChangeActiveBinding(long expectedRevision)
    {
        var binding = Create(Ids());

        await Assert.That(() => binding.Revoke(expectedRevision, RevocationTime)).Throws<InvalidOperationException>();
        await Assert.That(binding.RevokedAt).IsNull();
        await Assert.That(binding.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task RevocationIsTerminalAndMatchingCannotRestorePermission()
    {
        var ids = Ids();
        var binding = Create(ids);
        binding.Revoke(1, RevocationTime);

        await Assert.That(binding.RevokedAt).IsEqualTo((DateTimeOffset?)RevocationTime);
        await Assert.That(binding.Revision).IsEqualTo(2L);
        RequireMatching(binding, ids);
        await Assert.That(() => binding.Revoke(1, RevocationTime.AddMinutes(1))).Throws<InvalidOperationException>();
        await Assert.That(() => binding.Revoke(2, RevocationTime.AddMinutes(1))).Throws<InvalidOperationException>();
        await Assert.That(binding.RevokedAt).IsEqualTo((DateTimeOffset?)RevocationTime);
        await Assert.That(binding.Revision).IsEqualTo(2L);
        await Assert.That(binding.ServicePrincipalId).IsEqualTo(ids[4]);
    }

    [Test]
    public async Task RevisionOverflowCannotPartiallyRevokeHydratedBinding()
    {
        var binding = Create(Ids());
        typeof(ModerationMachineSubjectBinding).GetProperty(nameof(ModerationMachineSubjectBinding.Revision))!
            .SetValue(binding, long.MaxValue);

        await Assert.That(() => binding.Revoke(long.MaxValue, RevocationTime)).Throws<OverflowException>();
        await Assert.That(binding.RevokedAt).IsNull();
        await Assert.That(binding.Revision).IsEqualTo(long.MaxValue);
    }

    private static Guid[] Ids() =>
        [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];

    private static ModerationMachineSubjectBinding Create(
        Guid[] ids,
        string keyId = "Opaque-Key",
        ModerationMachineCredentialSource source = ModerationMachineCredentialSource.Persisted,
        ExternalApiKeyOwnerType ownerType = ExternalApiKeyOwnerType.Organization) =>
        ModerationMachineSubjectBinding.Establish(ids[0], ids[1], source, keyId, ids[2], ownerType, ids[3], ids[4]);

    private static void RequireMatching(
        ModerationMachineSubjectBinding binding,
        Guid[] ids,
        string keyId = "Opaque-Key",
        ModerationMachineCredentialSource source = ModerationMachineCredentialSource.Persisted,
        ExternalApiKeyOwnerType ownerType = ExternalApiKeyOwnerType.Organization) =>
        binding.RequireMatchingCoordinates(ids[1], source, keyId, ids[2], ownerType, ids[3], ids[4]);
}
