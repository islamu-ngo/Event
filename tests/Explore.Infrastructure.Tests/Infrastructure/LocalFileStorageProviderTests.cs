using System.Text;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class LocalFileStorageProviderTests
{
    [Test]
    [Arguments("root")]
    [Arguments("ancestor")]
    [Arguments("object")]
    public async Task LinkedCustodyPathsCannotReadDeleteOrWriteOutsideRoot(string location)
    {
        string parent = Directory.CreateTempSubdirectory("storage-custody-").FullName;
        string outside = Directory.CreateTempSubdirectory("storage-outside-").FullName;
        Guid tenant = Guid.CreateVersion7();
        string root = Path.Combine(parent, "storage");
        string key = $"tenants/{tenant:N}/file.pdf";
        string outsideFile = Path.Combine(outside, "file.pdf");
        byte[] bytes = "%PDF-1.7\ncustody canary"u8.ToArray();
        await File.WriteAllBytesAsync(outsideFile, bytes);
        try
        {
            if (location == "root")
                Directory.CreateSymbolicLink(root, outside);
            else
            {
                Directory.CreateDirectory(root);
                if (location == "ancestor")
                    Directory.CreateSymbolicLink(Path.Combine(root, "tenants"), outside);
                else
                {
                    string directory = Directory.CreateDirectory(Path.Combine(root, "tenants", tenant.ToString("N"))).FullName;
                    File.CreateSymbolicLink(Path.Combine(directory, "file.pdf"), outsideFile);
                }
            }
            var provider = new LocalFileStorageProvider(
                Options.Create(new LocalFileStorageOptions { RootPath = root }),
                NullLogger<LocalFileStorageProvider>.Instance);
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.OpenReadAsync(new(key, null), default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ExistsAsync(new(key), default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.DeleteAsync(new(key), default));
            await using var custody = await LocalQuarantineClaimFixture.CreateAsync(
                root, key, bytes.Length, File.GetLastWriteTimeUtc(outsideFile));
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.QuarantineAsync(
                custody.Input, custody.Owner, TimeProvider.System, default));
            using var content = new MemoryStream(bytes);
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.WriteAsync(
                new(tenant, content, "application/pdf", "file.pdf", "pdf", bytes.Length, bytes.Length, key), default));
            await Assert.That(await File.ReadAllBytesAsync(outsideFile)).IsEquivalentTo(bytes);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Test]
    public async Task InventoryNeverDescendsIntoLinkedDirectories()
    {
        string root = Directory.CreateTempSubdirectory("storage-inventory-custody-").FullName;
        string outside = Directory.CreateTempSubdirectory("storage-inventory-outside-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(outside, "private.txt"), "outside");
            Directory.CreateSymbolicLink(Path.Combine(root, "linked"), outside);
            var provider = new LocalFileStorageProvider(
                Options.Create(new LocalFileStorageOptions { RootPath = root }),
                NullLogger<LocalFileStorageProvider>.Instance);
            var inventory = new List<FileStorageInventoryObject>();
            await foreach (var item in provider.ListObjectsAsync(100, default)) inventory.Add(item);
            await Assert.That(inventory).IsEmpty();
            await Assert.That((await provider.TestAsync(default)).IsAvailable).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BoundWriteInitializesItsCapturedRootButAbsenceChecksNeverRecreateIt(bool rootExists)
    {
        string root = Path.Combine(Path.GetTempPath(), $"resource-first-write-{Guid.CreateVersion7():N}");
        var provider = new LocalFileStorageProvider(
            Options.Create(new LocalFileStorageOptions { RootPath = root, CreateRootIfMissing = false }),
            NullLogger<LocalFileStorageProvider>.Instance, boundTarget: true);
        try
        {
            if (rootExists) Directory.CreateDirectory(root);
            byte[] bytes = "bound resource"u8.ToArray();
            Guid tenantId = Guid.CreateVersion7();
            string key = $"tenants/{tenantId:N}/uploads/{Guid.CreateVersion7():N}.pdf";
            using var content = new MemoryStream(bytes);
            var written = await provider.WriteAsync(new(tenantId, content, "application/pdf", "file.pdf", "pdf",
                bytes.Length, bytes.Length, key), default);
            await Assert.That(written.ObjectKey).IsEqualTo(key);
            await Assert.That(await File.ReadAllBytesAsync(provider.ResolveObjectPath(key))).IsEquivalentTo(bytes);
            Directory.Delete(root, recursive: true);
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => provider.ExistsAsync(new(key), default));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => provider.DeleteAsync(new(key), default));
            await Assert.That(Directory.Exists(root)).IsFalse();
        }
        finally
        {
            DeleteRootIfExists(root);
        }
    }

    [Test]
    public async Task WriteAsync_GeneratesTenantScopedObjectKeyAndChecksum()
    {
        var provider = CreateProvider(out var root);

        try
        {
            var tenantId = Guid.CreateVersion7();
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("storage payload"));

            var result = await provider.WriteAsync(
                new FileStorageWriteInput(
                    tenantId,
                    content,
                    "text/plain",
                    "payload.txt",
                    ".txt",
                    ExpectedSizeBytes: 15,
                    MaxSizeBytes: 1024),
                CancellationToken.None);

            await Assert.That(result.Provider).IsEqualTo(StorageProviders.Local);
            await Assert.That(result.ObjectKey).StartsWith($"tenants/{tenantId:N}/");
            await Assert.That(result.ObjectKey).EndsWith(".txt");
            await Assert.That(result.SizeBytes).IsEqualTo(15);
            await Assert.That(result.Sha256Checksum).IsEqualTo("5e1c7766758f09dc15399c4c444a9c5734cf49bda9797f31c2f42af3be2fbbaa");
            await Assert.That(File.Exists(provider.ResolveObjectPath(result.ObjectKey))).IsTrue();
        }
        finally
        {
            DeleteRootIfExists(root);
        }
    }

    [Test]
    public async Task WriteAsync_WithReservedTenantObjectKey_UsesExactKey()
    {
        var provider = CreateProvider(out var root);

        try
        {
            var tenantId = Guid.CreateVersion7();
            var objectKey = $"tenants/{tenantId:N}/uploads/{Guid.CreateVersion7():N}.txt";
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("reserved"));

            FileStorageWriteResult result = await provider.WriteAsync(
                new FileStorageWriteInput(
                    tenantId,
                    content,
                    "text/plain",
                    "reserved.txt",
                    ".txt",
                    8,
                    8,
                    objectKey),
                CancellationToken.None);

            await Assert.That(result.ObjectKey).IsEqualTo(objectKey);
            await Assert.That(File.Exists(provider.ResolveObjectPath(objectKey))).IsTrue();
        }
        finally
        {
            DeleteRootIfExists(root);
        }
    }

    [Test]
    public async Task WriteAsync_WithAnotherTenantObjectKey_RejectsBeforeWrite()
    {
        var provider = CreateProvider(out var root);

        try
        {
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("rejected"));

            await Assert.ThrowsAsync<ArgumentException>(() => provider.WriteAsync(
                new FileStorageWriteInput(
                    Guid.CreateVersion7(),
                    content,
                    "text/plain",
                    "rejected.txt",
                    ".txt",
                    8,
                    8,
                    $"tenants/{Guid.CreateVersion7():N}/uploads/rejected.txt"),
                CancellationToken.None));

            await Assert.That(Directory.Exists(root)).IsFalse();
        }
        finally
        {
            DeleteRootIfExists(root);
        }
    }

    [Test]
    public async Task OpenReadAsync_ReturnsStoredBytesAndContentType()
    {
        var provider = CreateProvider(out var root);

        try
        {
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("read me"));
            var writeResult = await provider.WriteAsync(
                new FileStorageWriteInput(Guid.CreateVersion7(), content, "text/plain", "read.txt", ".txt", 7, 1024),
                CancellationToken.None);

            var readResult = await provider.OpenReadAsync(
                new FileStorageReadInput(writeResult.ObjectKey, "text/plain"),
                CancellationToken.None);

            await using (readResult.Content)
            {
                using var reader = new StreamReader(readResult.Content, Encoding.UTF8);
                await Assert.That(await reader.ReadToEndAsync()).IsEqualTo("read me");
            }

            await Assert.That(readResult.ContentType).IsEqualTo("text/plain");
            await Assert.That(readResult.Length).IsEqualTo(7);
        }
        finally
        {
            DeleteRootIfExists(root);
        }
    }

    [Test]
    public async Task ExistsAsync_ReturnsFalseWhenObjectIsMissingFromReachableRoot()
    {
        var provider = CreateProvider(out var root);

        try
        {
            Directory.CreateDirectory(root);
            var exists = await provider.ExistsAsync(
                new FileStorageExistsInput("tenants/missing/2026/06/02/missing.txt"),
                CancellationToken.None);

            await Assert.That(exists).IsFalse();
        }
        finally
        {
            DeleteRootIfExists(root);
        }
    }

    [Test]
    public async Task ExistsAsync_ReturnsTrueForStoredObject()
    {
        var provider = CreateProvider(out var root);

        try
        {
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("exists"));
            var writeResult = await provider.WriteAsync(
                new FileStorageWriteInput(Guid.CreateVersion7(), content, "text/plain", "exists.txt", ".txt", 6, 1024),
                CancellationToken.None);

            var exists = await provider.ExistsAsync(new FileStorageExistsInput(writeResult.ObjectKey), CancellationToken.None);

            await Assert.That(exists).IsTrue();
        }
        finally
        {
            DeleteRootIfExists(root);
        }
    }

    [Test]
    public async Task DeleteAsync_WhenCalledTwice_IsIdempotent()
    {
        var provider = CreateProvider(out var root);

        try
        {
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("delete me"));
            var writeResult = await provider.WriteAsync(
                new FileStorageWriteInput(Guid.CreateVersion7(), content, "text/plain", "delete.txt", ".txt", 9, 1024),
                CancellationToken.None);

            var firstDelete = await provider.DeleteAsync(new FileStorageDeleteInput(writeResult.ObjectKey), CancellationToken.None);
            var secondDelete = await provider.DeleteAsync(new FileStorageDeleteInput(writeResult.ObjectKey), CancellationToken.None);

            await Assert.That(firstDelete.Deleted).IsTrue();
            await Assert.That(secondDelete.Deleted).IsFalse();
        }
        finally
        {
            DeleteRootIfExists(root);
        }
    }

    [Test]
    public async Task ListObjectsAsync_ExcludesQuarantineAndTemporaryFiles()
    {
        var provider = CreateProvider(out var root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "tenants", "a"));
            Directory.CreateDirectory(Path.Combine(root, ".quarantine", "20260602"));
            await File.WriteAllTextAsync(Path.Combine(root, "tenants", "a", "stored.txt"), "stored");
            await File.WriteAllTextAsync(Path.Combine(root, "tenants", "a", "stored.txt.tmp-test"), "temp");
            await File.WriteAllTextAsync(Path.Combine(root, ".health-test"), "ok");
            await File.WriteAllTextAsync(Path.Combine(root, ".quarantine", "20260602", "stored.txt"), "quarantined");

            var objects = new List<FileStorageInventoryObject>();
            await foreach (var item in provider.ListObjectsAsync(10, CancellationToken.None))
            {
                objects.Add(item);
            }

            await Assert.That(objects.Count).IsEqualTo(1);
            await Assert.That(objects[0].ObjectKey).IsEqualTo("tenants/a/stored.txt");
        }
        finally
        {
            DeleteRootIfExists(root);
        }
    }

    [Test]
    public async Task QuarantineAsync_MovesObjectOutOfInventory()
    {
        var provider = CreateProvider(out var root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "tenants", "a"));
            var objectPath = Path.Combine(root, "tenants", "a", "orphan.txt");
            await File.WriteAllTextAsync(objectPath, "orphan");

            await using var custody = await LocalQuarantineClaimFixture.CreateAsync(
                root, "tenants/a/orphan.txt", new FileInfo(objectPath).Length, File.GetLastWriteTimeUtc(objectPath));
            var result = await provider.QuarantineAsync(
                custody.Input, custody.Owner, TimeProvider.System, CancellationToken.None);

            var objects = new List<FileStorageInventoryObject>();
            await foreach (var item in provider.ListObjectsAsync(10, CancellationToken.None))
            {
                objects.Add(item);
            }

            await Assert.That(result.Quarantined).IsTrue();
            await Assert.That(File.Exists(objectPath)).IsFalse();
            await Assert.That(Directory.Exists(Path.Combine(root, ".quarantine", "reconciliation"))).IsTrue();
            await Assert.That(objects).IsEmpty();
        }
        finally
        {
            DeleteRootIfExists(root);
        }
    }

    [Test]
    public async Task ResolveObjectPath_WithTraversalKey_ThrowsArgumentException()
    {
        var provider = CreateProvider(out var root);

        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
            {
                _ = provider.ResolveObjectPath("tenants/../../secret.txt");
                return Task.CompletedTask;
            });
        }
        finally
        {
            DeleteRootIfExists(root);
        }
    }

    [Test]
    public async Task TestAsync_WithReadableRoot_DoesNotProbeWrites()
    {
        var provider = CreateProvider(out var root);

        try
        {
            Directory.CreateDirectory(root);
            var status = await provider.TestAsync(CancellationToken.None);

            await Assert.That(status.Provider).IsEqualTo(StorageProviders.Local);
            await Assert.That(status.IsAvailable).IsTrue();
            await Assert.That(status.SupportsServerSideStreaming).IsTrue();
            await Assert.That(status.SupportsBrowserDirectUpload).IsFalse();
            await Assert.That(Directory.EnumerateFileSystemEntries(root).ToArray()).IsEmpty();
        }
        finally
        {
            DeleteRootIfExists(root);
        }
    }

    [Test]
    public async Task TestAsync_CannotCreateRootOrAcceptBooleanWriteAuthority()
    {
        var provider = CreateProvider(out var root);
        var status = await provider.TestAsync(CancellationToken.None);
        await Assert.That(status.IsAvailable).IsFalse();
        await Assert.That(Directory.Exists(root)).IsFalse();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.TestAsync(CancellationToken.None, true));
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    [Test]
    public async Task TestAsync_WithUnwritableRoot_LogsFailureTypeWithoutRawFilesystemPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"islamu-local-storage-tests-blocked-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(root, "not a directory");
        var logger = new TestListLogger<LocalFileStorageProvider>();
        var provider = new LocalFileStorageProvider(
            Options.Create(new LocalFileStorageOptions
            {
                RootPath = root
            }),
            logger);

        try
        {
            var status = await provider.TestAsync(CancellationToken.None);

            await Assert.That(status.IsAvailable).IsFalse();
            await Assert.That(status.FailureCode).IsEqualTo("local_storage_unavailable");

            var log = logger.Entries.Single(entry => entry.Level == LogLevel.Warning);
            await Assert.That(log.Exception).IsNull();
            await Assert.That(log.Message).Contains("FailureType=storage_io");
            await Assert.That(log.Message).DoesNotContain(root);
            await Assert.That(log.Message).DoesNotContain("not a directory");
        }
        finally
        {
            if (File.Exists(root))
            {
                File.Delete(root);
            }
        }
    }

    private static LocalFileStorageProvider CreateProvider(
        out string root,
        ILogger<LocalFileStorageProvider>? logger = null)
    {
        root = Path.Combine(Path.GetTempPath(), $"islamu-local-storage-tests-{Guid.NewGuid():N}");
        return new LocalFileStorageProvider(
            Options.Create(new LocalFileStorageOptions
            {
                RootPath = root
            }),
            logger ?? NullLogger<LocalFileStorageProvider>.Instance);
    }

    private static void DeleteRootIfExists(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
