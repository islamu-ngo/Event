using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Event.Application.UnitTests.Features.ConfigurationManifest;

public sealed class ModerationImportReservationTests
{
    [Test]
    public async Task Reservation_snapshots_mapping_and_roundtrips_without_a_compilation_graph()
    {
        var profileId = Guid.CreateVersion7();
        var profiles = new List<ModerationConfigurationReservedProfile> { new("profile.a", 1, profileId) };
        var reservation = new ModerationConfigurationImportReservation(null, Guid.CreateVersion7(),
            Guid.CreateVersion7(), new string('a', 64), profiles);
        profiles.Clear();
        var restored = ModerationConfigurationImportReservation.Parse(reservation.Serialize());
        await Assert.That(restored.Profiles.Single().ProfileId).IsEqualTo(profileId);
        await Assert.That(restored.Digest).IsEqualTo(reservation.Digest);
        await Assert.That(restored.Binding).IsEqualTo(reservation.Binding);
    }

    [Test]
    [Arguments("duplicate-key")]
    [Arguments("duplicate-id")]
    [Arguments("non-version-seven")]
    [Arguments("invalid-variant")]
    [Arguments("oversized")]
    public async Task Reservation_rejects_ambiguous_or_unbounded_native_mapping(string attack)
    {
        var first = new ModerationConfigurationReservedProfile("profile.a", 1, Guid.CreateVersion7());
        IReadOnlyList<ModerationConfigurationReservedProfile> profiles = attack switch
        {
            "duplicate-key" => [first, first with { ProfileId = Guid.CreateVersion7() }],
            "duplicate-id" => [first, first with { Code = "profile.b" }],
            "non-version-seven" => [first with { ProfileId = Guid.NewGuid() }],
            "invalid-variant" => [first with { ProfileId = Guid.Parse("00000000-0000-7000-0000-000000000001") }],
            _ => Enumerable.Range(0, 257).Select(index =>
                new ModerationConfigurationReservedProfile($"profile.{index}", 1, Guid.CreateVersion7())).ToArray()
        };
        await Assert.That(() => new ModerationConfigurationImportReservation(null, Guid.CreateVersion7(),
            Guid.CreateVersion7(), new string('a', 64), profiles)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Session_cannot_authorize_reservation_that_differs_from_approved_binding()
    {
        var now = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        var target = ConfigurationImportTarget.ForTenant(Guid.CreateVersion7());
        var digest = new string('a', 64);
        var session = ConfigurationImportSession.Create(Guid.CreateVersion7(), target,
            new(new(Guid.CreateVersion7()), digest, 1, now.AddHours(1)), digest, now, TimeSpan.FromMinutes(30));
        var definition = new ModerationConfigurationImportReservation(null, Guid.CreateVersion7(),
            Guid.CreateVersion7(), digest, [new("profile.a", 1, Guid.CreateVersion7())]);
        var reservation = new ModerationConfigurationImportReservationSet(target.AuthorityKey, digest, digest, digest,
            [new(ModerationConfigurationImportSections.Tenant, "source", target.TenantId!.Value.ToString("N"),
                target.TenantId, Guid.CreateVersion7(), definition)]);
        var binding = new ConfigurationImportPreviewBinding(target, digest, digest, digest, digest,
            ConfigurationImportApplyMode.ApplySelected, digest, now.AddMinutes(30), new string('b', 64));
        await Assert.That(() => session.MarkPreviewReady(binding, now, reservation))
            .Throws<ConfigurationImportSessionException>();
        await Assert.That(session.State).IsEqualTo(ConfigurationImportSessionState.Uploaded);
        await Assert.That(session.ModerationReservation).IsNull();
    }
}
