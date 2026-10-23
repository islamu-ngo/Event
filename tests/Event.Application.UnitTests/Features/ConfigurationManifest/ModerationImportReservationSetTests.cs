using Explore.Application.Features.ConfigurationManifest.Moderation;

namespace Event.Application.UnitTests.Features.ConfigurationManifest;

public sealed class ModerationImportReservationSetTests
{
    [Test]
    public async Task Complete_reservation_snapshots_entries_and_roundtrips_operation_and_target_identity()
    {
        var entry = Entry();
        var entries = new List<ModerationConfigurationReservedTarget> { entry };
        var reservation = Set(entries);
        entries.Clear();
        var restored = ModerationConfigurationImportReservationSet.Parse(reservation.Serialize());
        await Assert.That(restored.Digest).IsEqualTo(reservation.Digest);
        await Assert.That(restored.Entries.Single().OperationId).IsEqualTo(entry.OperationId);
        await Assert.That(restored.Entries.Single().TargetTenantId).IsEqualTo(entry.TargetTenantId);
    }

    [Test]
    [Arguments("target")]
    [Arguments("operation")]
    [Arguments("publication")]
    [Arguments("profile")]
    [Arguments("empty")]
    [Arguments("oversized")]
    public async Task Complete_reservation_rejects_aliases_and_unbounded_or_empty_sets(string attack)
    {
        var first = Entry();
        var second = Entry() with { SourceName = "source-b", TargetName = "target-b" };
        second = attack switch
        {
            "target" => second with { TargetTenantId = first.TargetTenantId },
            "operation" => second with { OperationId = first.OperationId },
            "publication" => second with
            {
                Definition = new(null, second.Definition.PolicyId,
                first.Definition.PublicationId, new string('a', 64), second.Definition.Profiles)
            },
            "profile" => second with
            {
                Definition = new(null, second.Definition.PolicyId,
                second.Definition.PublicationId, new string('a', 64), first.Definition.Profiles)
            },
            _ => second
        };
        IReadOnlyList<ModerationConfigurationReservedTarget> entries = attack switch
        {
            "empty" => [],
            "oversized" => Enumerable.Range(0, 258).Select(index =>
                Entry() with { SourceName = $"source-{index}", TargetName = $"target-{index}" }).ToArray(),
            _ => [first, second]
        };
        await Assert.That(() => Set(entries)).Throws<ArgumentException>();
    }

    private static ModerationConfigurationImportReservationSet Set(
        IReadOnlyList<ModerationConfigurationReservedTarget> entries) =>
        new("instance", new string('a', 64), new string('b', 64), new string('c', 64), entries);

    private static ModerationConfigurationReservedTarget Entry() =>
        new(ModerationConfigurationImportSections.Tenant, "source-a", "target-a", Guid.CreateVersion7(),
            Guid.CreateVersion7(), new(null, Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64),
                [new("profile.a", 1, Guid.CreateVersion7())]));
}
