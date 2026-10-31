using System.Text.Json;
using static ISLAMU.ReleaseEngineering.SecurityExecutionEvidence;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class SecurityExecutionEvidenceImageReferenceTests
{
    [Test]
    [Arguments("postgres:18-alpine", "docker.io/library/postgres:18-alpine", true)]
    [Arguments("minio/minio:latest", "docker.io/minio/minio:latest", true)]
    [Arguments("docker.io/postgres", "postgres:latest", true)]
    [Arguments("postgres:18-alpine", "quay.io/library/postgres:18-alpine", false)]
    [Arguments("postgres:18-alpine", "postgres:18-Alpine", false)]
    public async Task ReferenceKeysPreserveRegistryRepositoryAndTagIdentity(string left, string right, bool equal)
    {
        await Assert.That(LocalImageReferenceKey(left) == LocalImageReferenceKey(right)).IsEqualTo(equal);
    }

    [Test]
    public async Task DigestSelectionDoesNotDependOnAnOptionalTag()
    {
        string digest = "sha256:" + new string('a', 64);
        await Assert.That(LocalImageReferenceKey("docker.io/library/redis:7-alpine@" + digest))
            .IsEqualTo("docker.io/library/redis@" + digest);
        await Assert.That(LocalImageReferenceKey("redis@" + digest))
            .IsEqualTo("docker.io/library/redis@" + digest);
        await Assert.That(LocalImageReferenceKey("foreign/redis@" + digest))
            .IsNotEqualTo(LocalImageReferenceKey("redis@" + digest));
        await Assert.That(LocalImageReferenceKey("redis@sha256:" + new string('b', 64)))
            .IsNotEqualTo(LocalImageReferenceKey("redis@" + digest));
        await Assert.That(LocalImageReferenceKey(digest)).IsEqualTo(digest);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EquivalentCachedReferencesCannotClaimDifferentImageIds(bool conflict)
    {
        string first = "sha256:" + new string('a', 64);
        string second = conflict ? "sha256:" + new string('b', 64) : first;
        using var inventory = JsonDocument.Parse(JsonSerializer.Serialize(new[]
        {
            new { Id = first, RepoTags = new[] { "postgres:18-alpine" }, RepoDigests = Array.Empty<string>() },
            new { Id = second, RepoTags = new[] { "docker.io/library/postgres:18-alpine" }, RepoDigests = Array.Empty<string>() }
        }));
        if (conflict)
            await Assert.That(() => ReadCachedImageReferences(inventory.RootElement)).Throws<InvalidDataException>();
        else
            await Assert.That(ReadCachedImageReferences(inventory.RootElement)[LocalImageReferenceKey("postgres:18-alpine")])
                .IsEqualTo(first);
    }
}
