using System.Collections.Immutable;
using Explore.Application.Services.Moderation;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationMeasurementProductManifestTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task ChangedBytesInEveryRequiredNativeAssemblyInvalidateMeasurement(int index)
    {
        var original = Products();
        var changed = original.SetItem(index, original[index] with { Digest = new string('b', 64) });
        await Assert.That(ModerationMeasurementProductManifest.Digest(original))
            .IsNotEqualTo(ModerationMeasurementProductManifest.Digest(changed));
    }

    [Test]
    [Arguments("consumer")]
    [Arguments("mode")]
    [Arguments("inventory")]
    [Arguments("product")]
    public async Task RuntimeIdentityCannotCrossConsumerModeInventoryOrProductBytes(string change)
    {
        var original = ModerationMeasurementProductManifest.RuntimeGeneration(
            "resource.payload", ModerationNativeMode.Embedded, new string('a', 64), new string('b', 64));
        var changed = ModerationMeasurementProductManifest.RuntimeGeneration(
            change == "consumer" ? "resource.redirect" : "resource.payload",
            change == "mode" ? ModerationNativeMode.RemoteStrict : ModerationNativeMode.Embedded,
            new string(change == "inventory" ? 'c' : 'a', 64),
            new string(change == "product" ? 'c' : 'b', 64));
        await Assert.That(changed).IsNotEqualTo(original);
    }

    [Test]
    [Arguments("missing")]
    [Arguments("extra")]
    [Arguments("reordered")]
    [Arguments("duplicate")]
    [Arguments("unknown")]
    [Arguments("invalid-digest")]
    [Arguments("default")]
    public async Task IncompleteOrSubstitutedProductRecipeCannotIdentifyTheDeployedRuntime(string mutation)
    {
        var original = Products();
        var changed = mutation switch
        {
            "missing" => original.RemoveAt(0),
            "extra" => original.Add(new("unapproved", new string('a', 64))),
            "reordered" => original.Reverse().ToImmutableArray(),
            "duplicate" => original.SetItem(1, original[0]),
            "unknown" => original.SetItem(0, original[0] with { AssemblyName = "unapproved" }),
            "invalid-digest" => original.SetItem(0, original[0] with { Digest = new string('z', 64) }),
            "default" => default,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        await Assert.ThrowsAsync<ArgumentException>(() => Task.FromResult(ModerationMeasurementProductManifest.Digest(changed)));
    }

    private static ImmutableArray<ModerationMeasurementProductArtifact> Products() =>
    [
        new("Explore.API", new string('a', 64)), new("Explore.Application", new string('a', 64)),
        new("Explore.Domain", new string('a', 64)), new("Explore.Persistence", new string('a', 64)),
        new("Explore.Infrastructure", new string('a', 64)), new("Explore.Secrets", new string('a', 64)),
        new("ISLAMU.Moderation.Application", new string('a', 64)), new("ISLAMU.Moderation.Domain", new string('a', 64))
    ];
}
