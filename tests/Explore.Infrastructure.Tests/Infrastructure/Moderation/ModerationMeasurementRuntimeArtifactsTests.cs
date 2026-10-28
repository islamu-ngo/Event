using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Explore.Application.Contracts.Infrastructure;
using Explore.Infrastructure.Services.Moderation;

namespace Explore.Infrastructure.Tests.Infrastructure.Moderation;

public sealed class ModerationMeasurementRuntimeArtifactsTests
{
    [Test]
    public async Task RealDeployedBytesAgreeWithTheirLoadedModule()
    {
        using var fixture = new Fixture();
        var artifact = await ModerationMeasurementRuntimeArtifacts.ReadArtifactAsync(
            typeof(ModerationMeasurementRuntimeArtifacts).Assembly, fixture.Path, default);

        await Assert.That(artifact).IsNotNull();
        await Assert.That(artifact!.Assembly).IsEqualTo("Explore.Infrastructure");
        await Assert.That(artifact.ModuleVersionId).IsEqualTo(
            typeof(ModerationMeasurementRuntimeArtifacts).Module.ModuleVersionId);
        await Assert.That(artifact.Sha256).IsEqualTo(
            Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(fixture.Path))));
    }

    [Test]
    public async Task DifferentModuleBytesCannotDescribeTheLoadedProduct()
    {
        using var fixture = new Fixture();
        File.Copy(typeof(object).Assembly.Location, fixture.Path, overwrite: true);
        await Assert.That(await ModerationMeasurementRuntimeArtifacts.ReadArtifactAsync(
            typeof(ModerationMeasurementRuntimeArtifacts).Assembly, fixture.Path, default)).IsNull();
    }

    [Test]
    public async Task MatchingAssemblyNameWithDifferentMvidCannotDescribeTheLoadedModule()
    {
        using var fixture = new Fixture();
        var assembly = typeof(ModerationMeasurementRuntimeArtifacts).Assembly;
        var bytes = await File.ReadAllBytesAsync(fixture.Path);
        var offset = bytes.AsSpan().IndexOf(assembly.ManifestModule.ModuleVersionId.ToByteArray());
        await Assert.That(offset).IsGreaterThanOrEqualTo(0);
        bytes[offset] ^= 1;
        using (var image = new MemoryStream(bytes, writable: false))
        using (var pe = new PEReader(image))
        {
            var metadata = pe.GetMetadataReader();
            await Assert.That(metadata.GetString(metadata.GetAssemblyDefinition().Name))
                .IsEqualTo(assembly.GetName().Name);
            await Assert.That(metadata.GetGuid(metadata.GetModuleDefinition().Mvid))
                .IsNotEqualTo(assembly.ManifestModule.ModuleVersionId);
        }
        await File.WriteAllBytesAsync(fixture.Path, bytes);
        await Assert.That(await ModerationMeasurementRuntimeArtifacts.ReadArtifactAsync(
            assembly, fixture.Path, default)).IsNull();
    }

    [Test]
    public async Task MissingAndTruncatedSourcesCannotSupplyAnObservation()
    {
        using var fixture = new Fixture();
        File.Delete(fixture.Path);
        await Assert.That(await ModerationMeasurementRuntimeArtifacts.ReadArtifactAsync(
            typeof(ModerationMeasurementRuntimeArtifacts).Assembly, fixture.Path, default)).IsNull();
        await File.WriteAllBytesAsync(fixture.Path, [0, 1, 2]);
        await Assert.That(await ModerationMeasurementRuntimeArtifacts.ReadArtifactAsync(
            typeof(ModerationMeasurementRuntimeArtifacts).Assembly, fixture.Path, default)).IsNull();
    }

    [Test]
    public async Task ReparseSourceCannotBorrowARealLoadedModuleIdentity()
    {
        using var fixture = new Fixture();
        File.Delete(fixture.Path);
        File.CreateSymbolicLink(fixture.Path, typeof(ModerationMeasurementRuntimeArtifacts).Assembly.Location);
        await Assert.That(await ModerationMeasurementRuntimeArtifacts.ReadArtifactAsync(
            typeof(ModerationMeasurementRuntimeArtifacts).Assembly, fixture.Path, default)).IsNull();
    }

    [Test]
    public async Task UnavailableSourceCannotSupplyAnObservation()
    {
        using var fixture = new Fixture();
        await using var exclusive = new FileStream(fixture.Path, FileMode.Open, FileAccess.ReadWrite,
            FileShare.None);
        await Assert.That(await ModerationMeasurementRuntimeArtifacts.ReadArtifactAsync(
            typeof(ModerationMeasurementRuntimeArtifacts).Assembly, fixture.Path, default)).IsNull();
    }

    [Test]
    public async Task RawFileDigestIncludesBytesOutsideThePeModule()
    {
        using var fixture = new Fixture();
        var assembly = typeof(ModerationMeasurementRuntimeArtifacts).Assembly;
        var original = await ModerationMeasurementRuntimeArtifacts.ReadArtifactAsync(assembly, fixture.Path, default);
        await using (var stream = new FileStream(fixture.Path, FileMode.Append, FileAccess.Write, FileShare.None))
            await stream.WriteAsync(new byte[] { 1, 2, 3, 4 });
        var changed = await ModerationMeasurementRuntimeArtifacts.ReadArtifactAsync(assembly, fixture.Path, default);

        await Assert.That(original).IsNotNull();
        await Assert.That(changed).IsNotNull();
        await Assert.That(changed!.ModuleVersionId).IsEqualTo(original!.ModuleVersionId);
        await Assert.That(changed.Sha256).IsNotEqualTo(original.Sha256);
    }

    [Test]
    public async Task ProductManifestMatchesThePublishedOrderedWireFormat()
    {
        ImmutableArray<ModerationMeasurementRuntimeModule> modules =
        [
            new("Explore.API", Guid.Empty, new string('a', 64)),
            new("Explore.Application", Guid.Empty, new string('b', 64)),
            new("Explore.Domain", Guid.Empty, new string('c', 64)),
            new("Explore.Persistence", Guid.Empty, new string('d', 64)),
            new("Explore.Infrastructure", Guid.Empty, new string('e', 64)),
            new("Explore.ServiceDefaults", Guid.Empty, new string('f', 64)),
            new("ISLAMU.Moderation.Application", Guid.Empty, new string('0', 64)),
            new("ISLAMU.Moderation.Domain", Guid.Empty, new string('1', 64))
        ];
        await Assert.That(ModerationMeasurementRuntimeArtifacts.ProductDigest(modules)).IsEqualTo(
            "3169f83cfbcf765834006249367035bf8aad0b9fe82963160d3dfeb48c3c337e");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("TMPDIR") ?? System.IO.Path.GetTempPath(),
            "moderation-artifacts-" + Guid.CreateVersion7().ToString("N"));

        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "Explore.Infrastructure.dll");
            File.Copy(typeof(ModerationMeasurementRuntimeArtifacts).Assembly.Location, Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
