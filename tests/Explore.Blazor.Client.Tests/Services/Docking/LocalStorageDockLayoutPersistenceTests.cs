using System.Text.Json;
using Explore.Blazor.Client.Services.Docking;

using LocalStorageDockLayoutPersistence = Explore.Blazor.Client.Services.Interop.LocalStorageDockLayoutPersistence;

namespace Explore.Blazor.Client.Tests.Services.Docking;

public sealed class LocalStorageDockLayoutPersistenceTests
{
    private static readonly DockPanelId CustomizeId = new("workspace.events.customize");

    [Test]
    public async Task SerializeDeserialize_RoundTripsSchemaVersionedSnapshot()
    {
        var snapshot = CreateSnapshot("events");
        var logger = Substitute.For<ILogger<LocalStorageDockLayoutPersistence>>();

        var json = LocalStorageDockLayoutPersistence.Serialize(snapshot);
        var restored = LocalStorageDockLayoutPersistence.Deserialize("events", json, logger);

        await Assert.That(json).Contains("schemaVersion");
        await Assert.That(json).Contains("layoutKey");
        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.LayoutKey).IsEqualTo("events");
        await Assert.That(restored.Panels.Count).IsEqualTo(1);
        await Assert.That(restored.Panels[0].Id).IsEqualTo(CustomizeId);
        await Assert.That(restored.Panels[0].Mode).IsEqualTo(DockMode.Docked);
        await Assert.That(restored.Panels[0].Width).IsEqualTo(420);
    }

    [Test]
    public async Task Deserialize_CorruptJson_ReturnsNull()
    {
        var logger = Substitute.For<ILogger<LocalStorageDockLayoutPersistence>>();

        var restored = LocalStorageDockLayoutPersistence.Deserialize("events", "{not valid json", logger);

        await Assert.That(restored).IsNull();
    }

    [Test]
    public async Task Deserialize_MismatchedLayoutKey_ReturnsNull()
    {
        var logger = Substitute.For<ILogger<LocalStorageDockLayoutPersistence>>();
        var json = LocalStorageDockLayoutPersistence.Serialize(CreateSnapshot("events"));

        var restored = LocalStorageDockLayoutPersistence.Deserialize("other-layout", json, logger);

        await Assert.That(restored).IsNull();
    }

    [Test]
    public async Task Deserialize_UnsupportedSchemaVersion_ReturnsNull()
    {
        var logger = Substitute.For<ILogger<LocalStorageDockLayoutPersistence>>();
        var unsupportedEnvelope = new
        {
            SchemaVersion = 999,
            LayoutKey = "events",
            Snapshot = CreateSnapshot("events")
        };
        var json = JsonSerializer.Serialize(unsupportedEnvelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var restored = LocalStorageDockLayoutPersistence.Deserialize("events", json, logger);

        await Assert.That(restored).IsNull();
    }

    [Test]
    public async Task LoadSaveDelete_ReturnServerSafeResultsOutsideBrowser()
    {
        var jsRuntime = Substitute.For<Microsoft.JSInterop.IJSRuntime>();
        var logger = Substitute.For<ILogger<LocalStorageDockLayoutPersistence>>();
        await using var persistence = new LocalStorageDockLayoutPersistence(
            jsRuntime,
            Substitute.For<IPublicExperienceService>(),
            logger);

        var loaded = await persistence.LoadAsync("events");
        var saved = await persistence.SaveAsync(CreateSnapshot("events"));
        var deleted = await persistence.DeleteAsync("events");

        await Assert.That(loaded).IsNull();
        await Assert.That(saved).IsFalse();
        await Assert.That(deleted).IsFalse();
    }

    [Test]
    public async Task BuildStorageKey_IncludesTenantDiscriminatorAndHasNoLegacyFallback()
    {
        var key = LocalStorageDockLayoutPersistence.BuildStorageKey("community", "shell");

        await Assert.That(key).IsEqualTo("community:shell");
        await Assert.That(key).IsNotEqualTo("shell");
    }

    private static DockLayoutSnapshot CreateSnapshot(string layoutKey)
    {
        return new DockLayoutSnapshot(
            layoutKey,
            [new DockPanelState(CustomizeId, IsOpen: true, DockMode.Docked, Width: 420, Order: 10, IsActive: true)],
            new DateTimeOffset(2026, 04, 30, 12, 0, 0, TimeSpan.Zero));
    }
}
