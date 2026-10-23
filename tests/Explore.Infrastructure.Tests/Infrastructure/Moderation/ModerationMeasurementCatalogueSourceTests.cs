using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Services.Moderation;
using Explore.Infrastructure.Services.Moderation;

namespace Explore.Infrastructure.Tests.Infrastructure.Moderation;

public sealed class ModerationMeasurementCatalogueSourceTests
{
    [Test]
    public async Task ExactApprovedSidecarReturnsOnlyImmutableMembership()
    {
        using var fixture = new Fixture();
        await fixture.PublishAsync(fixture.Bytes);
        var artifact = await new ModerationMeasurementCatalogueSource().ReadAsync(fixture.Digest);
        await Assert.That(artifact).IsNotNull();
        await Assert.That(artifact!.Digest).IsEqualTo(fixture.Digest);
        await Assert.That(artifact.Catalogue.CaseIds.Length).IsEqualTo(3);
        await Assert.That(artifact.Catalogue.ConsumerId).IsEqualTo("resource.payload");
    }

    [Test]
    public async Task MissingOrChangedSidecarCannotSupplyApprovedMembership()
    {
        using var fixture = new Fixture();
        var source = new ModerationMeasurementCatalogueSource();
        await Assert.That(await source.ReadAsync(fixture.Digest)).IsNull();
        await fixture.PublishAsync([.. fixture.Bytes, (byte)' ']);
        await Assert.That(await source.ReadAsync(fixture.Digest)).IsNull();
    }

    [Test]
    public async Task OversizedSidecarIsUnavailableEvenWhenItsPathIsApproved()
    {
        using var fixture = new Fixture();
        await fixture.PublishAsync(new byte[ModerationMeasurementCatalogue.MaximumBytes + 1]);
        await Assert.That(await new ModerationMeasurementCatalogueSource().ReadAsync(fixture.Digest)).IsNull();
    }

    [Test]
    [Arguments("../catalogue")]
    [Arguments("latest")]
    [Arguments("")]
    public async Task CallerCannotChooseAPathInsteadOfAnEnrolledDigest(string digest)
    {
        await Assert.That(await new ModerationMeasurementCatalogueSource().ReadAsync(digest)).IsNull();
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            var identity = Guid.CreateVersion7().ToString("N");
            var catalogue = new ModerationMeasurementCatalogue(1, "resource.payload", "10.0.0",
                "Sqlite", "3.49.0", new string('a', 64), new string('b', 64),
                ModerationMeasurementCatalogue.Bundles.Select((bundle, index) =>
                    new ModerationMeasurementCatalogueJob(bundle, "/*/*/MeasuredCase/*",
                        ImmutableArray.Create(new ModerationMeasurementCatalogueCase(
                            $"case.{index}", $"journal.{index}", $"node.{index}", "MeasuredCase",
                            "Outcome", "Outcome", "Tests.MeasuredCase", "Outcome", identity))))
                    .ToImmutableArray());
            Bytes = JsonSerializer.SerializeToUtf8Bytes(catalogue,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            Digest = Convert.ToHexStringLower(SHA256.HashData(Bytes));
            DirectoryPath = Path.Combine(AppContext.BaseDirectory, "moderation", "catalogues", Digest);
            FilePath = Path.Combine(DirectoryPath, "catalogue.v1.json");
            if (Directory.Exists(DirectoryPath))
                throw new InvalidOperationException("This invocation's catalogue identity already exists.");
        }

        public byte[] Bytes { get; }
        public string Digest { get; }
        private string DirectoryPath { get; }
        private string FilePath { get; }

        public async Task PublishAsync(byte[] bytes)
        {
            Directory.CreateDirectory(DirectoryPath);
            await using var file = new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.WriteAsync(bytes);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
        }
    }
}
