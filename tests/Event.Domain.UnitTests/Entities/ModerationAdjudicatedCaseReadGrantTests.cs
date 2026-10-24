using Explore.Domain.Interfaces;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationAdjudicatedCaseReadGrantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GrantRetainsExactCaseRecipientIssuerAndOriginalEffectWithoutConveyingAuthority(bool platform)
    {
        var ids = Enumerable.Range(0, 9).Select(_ => Guid.CreateVersion7()).ToArray();
        var grant = ModerationAdjudicatedCaseReadGrant.Grant(ids[0], ids[1], ids[2], ids[3], ids[4],
            7, ids[5], ids[6], ids[7], platform, ids[8], new string('a', 64), new string('b', 64),
            Now.AddDays(1), Now, Now.AddHours(1));

        grant.ValidatePersistedState();
        await Assert.That((grant.Id, grant.HostBindingId, grant.TrustDomainId, grant.TenantId,
            grant.CaseId, grant.RecipientUserId, grant.IssuerUserId, grant.IssuerRoleGrantId,
            grant.OriginalEffectId)).IsEqualTo((ids[0], ids[1], ids[2], ids[3], ids[4], ids[5], ids[6], ids[7], ids[8]));
        await Assert.That(grant.CaseRevision).IsEqualTo(7L);
        await Assert.That(grant.IssuerPlatformGrant).IsEqualTo(platform);
        await Assert.That(grant.OriginalSnapshotHash).IsEqualTo(new string('a', 64));
        await Assert.That(grant.OriginalEffectBindingHash).IsEqualTo(new string('b', 64));
        await Assert.That(grant.OriginalRetainUntil).IsEqualTo(Now.AddDays(1));
        await Assert.That(grant.IssuedAt).IsEqualTo(Now);
        await Assert.That(grant.ExpiresAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(grant.Revision).IsEqualTo(1L);
        await AssertUnrevoked(grant);
        await Assert.That(grant.ToString()).IsEqualTo(nameof(ModerationAdjudicatedCaseReadGrant));
    }

    [Test]
    [Arguments("Id")]
    [Arguments("OriginalEffectId")]
    public async Task GrantAndRetainedStateRejectNonUuidV7EffectAndGrantIdentifiers(string property)
    {
        foreach (var invalid in new[] { Guid.Empty, Guid.NewGuid(),
            Guid.Parse("019a0000-0000-7000-0000-000000000001") })
        {
            var grant = Grant();
            Materialize(grant, property, invalid);
            await Assert.That(() => Reissue(grant)).Throws<ArgumentException>();
            await Assert.That(() => grant.ValidatePersistedState()).Throws<ArgumentException>();
        }
    }

    [Test]
    [Arguments("HostBindingId")]
    [Arguments("TrustDomainId")]
    [Arguments("TenantId")]
    [Arguments("CaseId")]
    [Arguments("RecipientUserId")]
    [Arguments("IssuerUserId")]
    [Arguments("IssuerRoleGrantId")]
    public async Task GrantAndRetainedStateRequireEveryNativeCoordinate(string property)
    {
        var grant = Grant();
        Materialize(grant, property, Guid.Empty);
        await Assert.That(() => Reissue(grant)).Throws<ArgumentException>();
        await Assert.That(() => grant.ValidatePersistedState()).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(-1L)]
    [Arguments(0L)]
    [Arguments(long.MaxValue)]
    public async Task GrantAndRetainedStateRequireBoundedPositiveCaseRevision(long revision)
    {
        var grant = Grant();
        Materialize(grant, nameof(grant.CaseRevision), revision);
        await Assert.That(() => Reissue(grant)).Throws<ArgumentException>();
        await Assert.That(() => grant.ValidatePersistedState()).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("OriginalSnapshotHash")]
    [Arguments("OriginalEffectBindingHash")]
    public async Task GrantAndRetainedStateRequireCanonicalLowercaseSha256Bindings(string property)
    {
        foreach (var invalid in new[] { null, "", new string('a', 63), new string('a', 65),
            new string('A', 64), new string('g', 64), new string('\u00e9', 64) })
        {
            var grant = Grant();
            Materialize(grant, property, invalid);
            await Assert.That(() => Reissue(grant)).Throws<ArgumentException>();
            await Assert.That(() => grant.ValidatePersistedState()).Throws<ArgumentException>();
        }
    }

    [Test]
    [Arguments("IssuedAt")]
    [Arguments("ExpiresAt")]
    public async Task GrantAndRetainedStateRejectNonFiniteNonUtcOrSubMicrosecondInstants(string property)
    {
        foreach (var invalid in InvalidInstants())
        {
            var grant = Grant();
            Materialize(grant, property, invalid);
            await Assert.That(() => Reissue(grant)).Throws<ArgumentException>();
            await Assert.That(() => grant.ValidatePersistedState()).Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task OriginalRetentionKeepsItsAuthenticatedSubMicrosecondCeiling()
    {
        var ceiling = Now.AddHours(1).AddTicks(1);
        var grant = Grant(expiresAt: Now.AddHours(1), originalRetainUntil: ceiling);
        grant.ValidatePersistedState();
        await Assert.That(grant.OriginalRetainUntil).IsEqualTo(ceiling);
        await Assert.That(grant.IsCurrentAt(Now.AddHours(1))).IsFalse();
        foreach (var invalid in InvalidInstants().Take(3))
        {
            Materialize(grant, nameof(grant.OriginalRetainUntil), invalid);
            await Assert.That(() => Reissue(grant)).Throws<ArgumentException>();
        }
    }

    [Test]
    [Arguments("expiry-at-issuance")]
    [Arguments("expiry-before-issuance")]
    [Arguments("expiry-beyond-original")]
    [Arguments("original-at-issuance")]
    [Arguments("original-before-issuance")]
    public async Task GrantAndRetainedStateCannotWidenOrExhaustOriginalRetention(string invalid)
    {
        var grant = Grant();
        var property = invalid.StartsWith("original", StringComparison.Ordinal)
            ? nameof(grant.OriginalRetainUntil) : nameof(grant.ExpiresAt);
        var instant = invalid switch
        {
            "expiry-at-issuance" or "original-at-issuance" => Now,
            "expiry-before-issuance" or "original-before-issuance" => Now.AddTicks(-10),
            _ => grant.OriginalRetainUntil.AddTicks(10)
        };
        Materialize(grant, property, instant);
        await Assert.That(() => Reissue(grant)).Throws<ArgumentException>();
        await Assert.That(() => grant.ValidatePersistedState()).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CurrentWindowIncludesIssuanceButExcludesExpiryAndOriginalCeiling(bool expiryAtOriginalCeiling)
    {
        var ceiling = Now.AddHours(2);
        var expiry = expiryAtOriginalCeiling ? ceiling : Now.AddHours(1);
        var grant = Grant(expiresAt: expiry, originalRetainUntil: ceiling);
        await Assert.That(grant.IsCurrentAt(Now.AddTicks(-10))).IsFalse();
        await Assert.That(grant.IsCurrentAt(Now)).IsTrue();
        await Assert.That(grant.IsCurrentAt(expiry.AddTicks(-10))).IsTrue();
        await Assert.That(grant.IsCurrentAt(expiry)).IsFalse();
        await Assert.That(grant.IsCurrentAt(ceiling)).IsFalse();
        await Assert.That(grant.IsCurrentAt(ceiling.AddTicks(10))).IsFalse();
    }

    [Test]
    public async Task CurrentChecksRejectMalformedClockInstants()
    {
        var grant = Grant();
        foreach (var invalid in InvalidInstants())
            await Assert.That(() => grant.IsCurrentAt(invalid)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task RevocationRetainsAccountableAdminAndNeverRenewsEvenAnExpiredGrant(bool platform, bool expired)
    {
        var grant = Grant();
        var operationId = Guid.CreateVersion7();
        var adminId = Guid.NewGuid();
        var roleGrantId = Guid.NewGuid();
        var revokedAt = expired ? grant.OriginalRetainUntil.AddDays(1) : Now;
        grant.Revoke(operationId, 1, adminId, roleGrantId, platform, revokedAt);

        grant.ValidatePersistedState();
        await Assert.That(grant.Revision).IsEqualTo(2L);
        await Assert.That(grant.RevocationOperationId).IsEqualTo((Guid?)operationId);
        await Assert.That(grant.RevokedAt).IsEqualTo((DateTimeOffset?)revokedAt);
        await Assert.That(grant.RevokedByUserId).IsEqualTo((Guid?)adminId);
        await Assert.That(grant.RevokedByRoleGrantId).IsEqualTo((Guid?)roleGrantId);
        await Assert.That(grant.RevokedByPlatformGrant).IsEqualTo((bool?)platform);
        await Assert.That(grant.ExpiresAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(grant.OriginalRetainUntil).IsEqualTo(Now.AddDays(1));
        await Assert.That(grant.IsCurrentAt(Now)).IsFalse();
        await Assert.That(grant.IsCurrentAt(revokedAt)).IsFalse();
        foreach (var revision in new[] { 1L, 2L })
            await Assert.That(() => grant.Revoke(Guid.CreateVersion7(), revision,
                Guid.NewGuid(), Guid.NewGuid(), !platform, revokedAt.AddMinutes(1)))
                .Throws<InvalidOperationException>();
        await Assert.That(grant.RevocationOperationId).IsEqualTo((Guid?)operationId);
        await Assert.That(grant.RevokedAt).IsEqualTo((DateTimeOffset?)revokedAt);
        await Assert.That(grant.RevokedByUserId).IsEqualTo((Guid?)adminId);
        await Assert.That(grant.RevokedByRoleGrantId).IsEqualTo((Guid?)roleGrantId);
        await Assert.That(grant.RevokedByPlatformGrant).IsEqualTo((bool?)platform);
        await Assert.That(grant.Revision).IsEqualTo(2L);
    }

    [Test]
    [Arguments("empty-operation")]
    [Arguments("v4-operation")]
    [Arguments("variant-operation")]
    [Arguments("same-operation")]
    [Arguments("empty-admin")]
    [Arguments("empty-role")]
    [Arguments("before-issuance")]
    [Arguments("minimum-time")]
    [Arguments("maximum-time")]
    [Arguments("non-utc-time")]
    [Arguments("sub-microsecond-time")]
    public async Task RejectedRevocationCannotPartiallyMutateTheGrant(string invalid)
    {
        var grant = Grant();
        var operation = invalid switch
        {
            "empty-operation" => Guid.Empty,
            "v4-operation" => Guid.NewGuid(),
            "variant-operation" => Guid.Parse("019a0000-0000-7000-0000-000000000001"),
            "same-operation" => grant.Id,
            _ => Guid.CreateVersion7()
        };
        var now = invalid switch
        {
            "before-issuance" => Now.AddTicks(-10),
            "minimum-time" => DateTimeOffset.MinValue,
            "maximum-time" => DateTimeOffset.MaxValue,
            "non-utc-time" => Now.ToOffset(TimeSpan.FromHours(1)),
            "sub-microsecond-time" => Now.AddTicks(1),
            _ => Now
        };
        await Assert.That(() => grant.Revoke(operation, 1,
            invalid == "empty-admin" ? Guid.Empty : Guid.NewGuid(),
            invalid == "empty-role" ? Guid.Empty : Guid.NewGuid(), true, now)).Throws<ArgumentException>();
        await AssertUnrevoked(grant);
        await Assert.That(grant.Revision).IsEqualTo(1L);
        await Assert.That(grant.IsCurrentAt(Now)).IsTrue();
        grant.ValidatePersistedState();
    }

    [Test]
    [Arguments(-1L)]
    [Arguments(0L)]
    [Arguments(2L)]
    [Arguments(long.MaxValue)]
    public async Task StaleRevocationCannotChangeAnyRevocationField(long revision)
    {
        var grant = Grant();
        await Assert.That(() => grant.Revoke(Guid.CreateVersion7(), revision,
            Guid.NewGuid(), Guid.NewGuid(), false, Now)).Throws<InvalidOperationException>();
        await AssertUnrevoked(grant);
        await Assert.That(grant.Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments("RevokedAt")]
    [Arguments("RevocationOperationId")]
    [Arguments("RevokedByUserId")]
    [Arguments("RevokedByRoleGrantId")]
    [Arguments("RevokedByPlatformGrant")]
    public async Task RetainedStateRejectsEveryIncompleteRevocationAndStrayUnrevokedField(string property)
    {
        var revoked = Grant();
        revoked.Revoke(Guid.CreateVersion7(), 1, Guid.NewGuid(), Guid.NewGuid(), false, Now);
        var retainedValue = typeof(ModerationAdjudicatedCaseReadGrant).GetProperty(property)!.GetValue(revoked);
        var unrevoked = Grant();
        Materialize(unrevoked, property, retainedValue);
        Materialize(revoked, property, null);
        await Assert.That(() => unrevoked.ValidatePersistedState()).Throws<ArgumentException>();
        await Assert.That(() => revoked.ValidatePersistedState()).Throws<ArgumentException>();
        await Assert.That(() => unrevoked.IsCurrentAt(Now)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false, 0L)]
    [Arguments(false, 2L)]
    [Arguments(false, long.MaxValue)]
    [Arguments(true, 0L)]
    [Arguments(true, 1L)]
    [Arguments(true, 3L)]
    public async Task RetainedStateRequiresTheExactUnrevokedOrRevokedGeneration(bool revoked, long revision)
    {
        var grant = Grant();
        if (revoked) grant.Revoke(Guid.CreateVersion7(), 1, Guid.NewGuid(), Guid.NewGuid(), true, Now);
        Materialize(grant, nameof(grant.Revision), revision);
        await Assert.That(() => grant.ValidatePersistedState()).Throws<ArgumentException>();
        await Assert.That(() => grant.Revoke(Guid.CreateVersion7(), revision,
            Guid.NewGuid(), Guid.NewGuid(), false, Now)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("empty-operation")]
    [Arguments("same-operation")]
    [Arguments("v4-operation")]
    [Arguments("empty-admin")]
    [Arguments("empty-role")]
    [Arguments("before-issuance")]
    [Arguments("minimum-time")]
    [Arguments("maximum-time")]
    [Arguments("non-utc-time")]
    [Arguments("sub-microsecond-time")]
    public async Task RetainedStateRejectsMalformedCompleteRevocations(string invalid)
    {
        var grant = Grant();
        grant.Revoke(Guid.CreateVersion7(), 1, Guid.NewGuid(), Guid.NewGuid(), true, Now);
        var property = invalid switch
        {
            "empty-operation" or "same-operation" or "v4-operation" => nameof(grant.RevocationOperationId),
            "empty-admin" => nameof(grant.RevokedByUserId),
            "empty-role" => nameof(grant.RevokedByRoleGrantId),
            _ => nameof(grant.RevokedAt)
        };
        object value = invalid switch
        {
            "empty-operation" or "empty-admin" or "empty-role" => Guid.Empty,
            "same-operation" => grant.Id,
            "v4-operation" => Guid.NewGuid(),
            "before-issuance" => Now.AddTicks(-10),
            "minimum-time" => DateTimeOffset.MinValue,
            "maximum-time" => DateTimeOffset.MaxValue,
            "non-utc-time" => Now.ToOffset(TimeSpan.FromHours(1)),
            _ => Now.AddTicks(1)
        };
        Materialize(grant, property, value);
        await Assert.That(() => grant.ValidatePersistedState()).Throws<ArgumentException>();
    }

    [Test]
    public async Task TenantInfrastructureCannotRehomeTheGrantAndNoPublicSetterCanRewriteItsPurpose()
    {
        var grant = Grant();
        ITenantEntity tenantEntity = grant;
        tenantEntity.TenantId = grant.TenantId;
        var originalTenant = grant.TenantId;
        await Assert.That(() => tenantEntity.TenantId = Guid.NewGuid()).Throws<InvalidOperationException>();
        await Assert.That(() => tenantEntity.TenantId = Guid.Empty).Throws<InvalidOperationException>();
        await Assert.That(grant.TenantId).IsEqualTo(originalTenant);
        await Assert.That(typeof(ModerationAdjudicatedCaseReadGrant).GetProperties()
            .All(property => property.SetMethod is { IsPrivate: true })).IsTrue();
    }

    private static ModerationAdjudicatedCaseReadGrant Grant(
        DateTimeOffset? expiresAt = null, DateTimeOffset? originalRetainUntil = null) =>
        ModerationAdjudicatedCaseReadGrant.Grant(Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), 7, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false,
            Guid.CreateVersion7(), new string('a', 64), new string('b', 64),
            originalRetainUntil ?? Now.AddDays(1), Now, expiresAt ?? Now.AddHours(1));

    private static ModerationAdjudicatedCaseReadGrant Reissue(ModerationAdjudicatedCaseReadGrant grant) =>
        ModerationAdjudicatedCaseReadGrant.Grant(grant.Id, grant.HostBindingId, grant.TrustDomainId,
            grant.TenantId, grant.CaseId, grant.CaseRevision, grant.RecipientUserId, grant.IssuerUserId,
            grant.IssuerRoleGrantId, grant.IssuerPlatformGrant, grant.OriginalEffectId,
            grant.OriginalSnapshotHash, grant.OriginalEffectBindingHash, grant.OriginalRetainUntil,
            grant.IssuedAt, grant.ExpiresAt);

    private static DateTimeOffset[] InvalidInstants() =>
        [DateTimeOffset.MinValue, DateTimeOffset.MaxValue, Now.ToOffset(TimeSpan.FromHours(1)), Now.AddTicks(1)];

    // Simulate malformed storage materialization; callers cannot use the private setters.
    private static void Materialize(ModerationAdjudicatedCaseReadGrant grant, string property, object? value) =>
        typeof(ModerationAdjudicatedCaseReadGrant).GetProperty(property)!.SetValue(grant, value);

    private static async Task AssertUnrevoked(ModerationAdjudicatedCaseReadGrant grant)
    {
        await Assert.That(grant.RevokedAt).IsNull();
        await Assert.That(grant.RevocationOperationId).IsNull();
        await Assert.That(grant.RevokedByUserId).IsNull();
        await Assert.That(grant.RevokedByRoleGrantId).IsNull();
        await Assert.That(grant.RevokedByPlatformGrant).IsNull();
    }
}
