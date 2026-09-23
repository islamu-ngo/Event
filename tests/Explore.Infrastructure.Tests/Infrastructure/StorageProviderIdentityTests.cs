using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Amazon.S3;
using Amazon.S3.Model;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Application.Models;
using Explore.Application.Models.Storage;
using Explore.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class StorageProviderIdentityTests
{
    [Test]
    public async Task MissingLocalRoot_IsUnavailableNotObjectAbsence()
    {
        var provider = new LocalFileStorageProvider(
            Options.Create(new LocalFileStorageOptions
            {
                RootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                CreateRootIfMissing = false
            }), NullLogger<LocalFileStorageProvider>.Instance);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => provider.ExistsAsync(
            new FileStorageExistsInput("missing.txt"), CancellationToken.None));
    }

    [Test]
    public async Task MissingBucket_IsUnavailableNotObjectAbsence()
    {
        var client = Substitute.For<IAmazonS3>();
        client.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<GetObjectMetadataResponse>(new AmazonS3Exception("unavailable")
            { StatusCode = HttpStatusCode.NotFound, ErrorCode = "NoSuchBucket" }));
        await Assert.ThrowsAsync<AmazonS3Exception>(() => CreateS3(client).ExistsAsync(
            new FileStorageExistsInput("key"), CancellationToken.None));
    }

    [Test]
    public async Task LocalRootReplacedByFile_IsUnavailableNotAbsence()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(path, "not-a-directory");
        try
        {
            var provider = new LocalFileStorageProvider(Options.Create(new LocalFileStorageOptions
                { RootPath = path, CreateRootIfMissing = false }), NullLogger<LocalFileStorageProvider>.Instance);
            await Assert.ThrowsAsync<IOException>(() => provider.ExistsAsync(new FileStorageExistsInput("key"), CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public async Task NativeHeadNotFound_RequiresReachableBucketBeforeAbsence()
    {
        var client = Substitute.For<IAmazonS3>();
        client.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<GetObjectMetadataResponse>(new AmazonS3Exception("absent")
            { StatusCode = HttpStatusCode.NotFound, ErrorCode = "NotFound" }));
        client.HeadBucketAsync(Arg.Any<HeadBucketRequest>(), Arg.Any<CancellationToken>()).Returns(new HeadBucketResponse());
        var provider = CreateS3(client, boundTarget: true);
        await Assert.That(await provider.ExistsAsync(new FileStorageExistsInput("key", "exact-version"), CancellationToken.None)).IsFalse();
        client.HeadBucketAsync(Arg.Any<HeadBucketRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<HeadBucketResponse>(new AmazonS3Exception("unavailable") { StatusCode = HttpStatusCode.Forbidden }));
        await Assert.ThrowsAsync<AmazonS3Exception>(() => provider.ExistsAsync(new FileStorageExistsInput("key", "exact-version"), CancellationToken.None));
    }

    [Test]
    public async Task DeleteMarker_DoesNotReportBytesDeleted()
    {
        var client = Substitute.For<IAmazonS3>();
        client.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteObjectResponse { DeleteMarker = "true" });
        var result = await CreateS3(client).DeleteAsync(new FileStorageDeleteInput("key"), CancellationToken.None);
        await Assert.That(result.Deleted).IsFalse();
    }

    [Test]
    public async Task LocalBinding_SurvivesRootPolicyChange_AndDeletesOnlyOriginalBytes()
    {
        var original = Directory.CreateTempSubdirectory("binding-original-");
        var replacement = Directory.CreateTempSubdirectory("binding-replacement-");
        try
        {
            var options = new LocalFileStorageOptions { RootPath = original.FullName };
            var repository = new BindingRepository();
            var service = new StorageProviderBindingService(repository, Substitute.For<IHierarchicalSettingsResolver>(),
                Substitute.For<IRetainedSecretResolver>(), Options.Create(options), Substitute.For<IS3ClientFactory>(), NullLoggerFactory.Instance);
            var tenant = Guid.CreateVersion7();
            var binding = await service.CaptureAsync(StorageProviders.Local, tenant, CancellationToken.None);
            var provider = await service.ResolveAsync(binding.Id, CancellationToken.None);
            await using var content = new MemoryStream([1, 2, 3]);
            var written = await provider.WriteAsync(new FileStorageWriteInput(tenant, content, "application/pdf", "resource.pdf", ".pdf", null, null), CancellationToken.None);
            options.RootPath = replacement.FullName;
            var replacementPath = Path.Combine(replacement.FullName, written.ObjectKey);
            Directory.CreateDirectory(Path.GetDirectoryName(replacementPath)!);
            await File.WriteAllBytesAsync(replacementPath, [9]);
            var retiredTenantProvider = await service.ResolveAsync(binding.Id, CancellationToken.None);
            var read = await retiredTenantProvider.OpenReadAsync(new FileStorageReadInput(written.ObjectKey, null), CancellationToken.None);
            await using (read.Content)
                await Assert.That(read.Content.ReadByte()).IsEqualTo(1);
            await retiredTenantProvider.DeleteAsync(new FileStorageDeleteInput(written.ObjectKey), CancellationToken.None);
            await Assert.That(await retiredTenantProvider.ExistsAsync(new FileStorageExistsInput(written.ObjectKey), CancellationToken.None)).IsFalse();
            await Assert.That(await File.ReadAllBytesAsync(replacementPath)).IsEquivalentTo(new byte[] { 9 });
        }
        finally
        {
            original.Delete(true);
            replacement.Delete(true);
        }
    }

    [Test]
    public async Task S3Binding_TargetDriftAndVersions_DoNotReadOrDeleteReplacementBytes()
    {
        var settings = Substitute.For<IHierarchicalSettingsResolver>();
        var target = new Dictionary<string, string>
        {
            [GovernanceSettingKeys.Storage.Endpoint] = "https://original.example.test",
            [GovernanceSettingKeys.Storage.BucketName] = "original",
            [GovernanceSettingKeys.Storage.Region] = "region-original"
        };
        settings.ResolveAsync<string>(Arg.Any<string>(), Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(call => target[call.Arg<string>()!]);
        settings.ResolveAsync<bool>(Arg.Any<string>(), Arg.Any<SettingContext>(), Arg.Any<CancellationToken>()).Returns(true);
        var secrets = Substitute.For<IRetainedSecretResolver>();
        secrets.CaptureAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Reference(call.Arg<string>()!));
        secrets.ResolveAsync(Arg.Any<RetainedSecretReference>(), Arg.Any<CancellationToken>())
            .Returns(call => Resolved(call.Arg<RetainedSecretReference>()!));
        var client = Substitute.For<IAmazonS3>();
        var factory = Substitute.For<IS3ClientFactory>();
        factory.CreateDataClient(Arg.Any<S3Configuration>()).Returns(call =>
        {
            var config = call.Arg<S3Configuration>()!;
            if (config.Endpoint != "https://original.example.test" || config.BucketName != "original"
                || config.Region != "region-original" || !config.ForcePathStyle)
                throw new InvalidOperationException("Target drifted.");
            return client;
        });
        var bytes = new Dictionary<string, byte[]> { ["newer-version"] = [9] };
        client.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            using var buffer = new MemoryStream();
            call.Arg<PutObjectRequest>()!.InputStream.CopyTo(buffer);
            bytes["original-version"] = buffer.ToArray();
            return new PutObjectResponse { VersionId = "original-version" };
        });
        client.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<GetObjectRequest>()!;
            return new GetObjectResponse { ResponseStream = new MemoryStream(bytes[request.VersionId ?? "newer-version"]), VersionId = request.VersionId };
        });
        client.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<DeleteObjectRequest>()!;
            bytes.Remove(request.VersionId ?? "newer-version");
            return new DeleteObjectResponse { VersionId = request.VersionId };
        });
        client.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<GetObjectMetadataRequest>()!;
            return bytes.ContainsKey(request.VersionId ?? "newer-version")
                ? Task.FromResult(new GetObjectMetadataResponse())
                : Task.FromException<GetObjectMetadataResponse>(new AmazonS3Exception("absent")
                    { StatusCode = HttpStatusCode.NotFound, ErrorCode = "NoSuchVersion" });
        });
        var service = new StorageProviderBindingService(new BindingRepository(), settings, secrets,
            Options.Create(new LocalFileStorageOptions()), factory, NullLoggerFactory.Instance);
        var tenant = Guid.CreateVersion7();
        var binding = await service.CaptureAsync(StorageProviders.S3Compatible, tenant, CancellationToken.None);
        target[GovernanceSettingKeys.Storage.Endpoint] = "https://replacement.example.test";
        target[GovernanceSettingKeys.Storage.BucketName] = "replacement";
        target[GovernanceSettingKeys.Storage.Region] = "region-replacement";
        settings.ResolveAsync<bool>(Arg.Any<string>(), Arg.Any<SettingContext>(), Arg.Any<CancellationToken>()).Returns(false);
        var provider = await service.ResolveAsync(binding.Id, CancellationToken.None);
        await using var content = new MemoryStream([1, 2, 3]);
        var written = await provider.WriteAsync(new FileStorageWriteInput(tenant, content, "application/pdf", "resource.pdf", ".pdf", null, null), CancellationToken.None);
        await Assert.That(written.ProviderVersionId).IsEqualTo("original-version");
        var read = await provider.OpenReadAsync(new FileStorageReadInput(written.ObjectKey, null, written.ProviderVersionId), CancellationToken.None);
        await using (read.Content)
            await Assert.That(read.Content.ReadByte()).IsEqualTo(1);
        await Assert.That(read.ProviderVersionId).IsEqualTo(written.ProviderVersionId);
        await provider.DeleteAsync(new FileStorageDeleteInput(written.ObjectKey, written.ProviderVersionId), CancellationToken.None);
        await Assert.That(await provider.ExistsAsync(new FileStorageExistsInput(written.ObjectKey, written.ProviderVersionId), CancellationToken.None)).IsFalse();
        await Assert.That(bytes["newer-version"]).IsEquivalentTo(new byte[] { 9 });

        secrets.ResolveAsync(Arg.Any<RetainedSecretReference>(), Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Unavailable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.OpenReadAsync(
            new FileStorageReadInput(written.ObjectKey, null, "newer-version"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.DeleteAsync(
            new FileStorageDeleteInput(written.ObjectKey, "newer-version"), CancellationToken.None));
        await Assert.That(bytes["newer-version"]).IsEquivalentTo(new byte[] { 9 });
    }

    [Test]
    [Arguments("Enabled")]
    [Arguments("Suspended")]
    public async Task BoundVersionedBucket_UnknownVersionCannotClaimAbsenceOrDelete(string status)
    {
        var client = Substitute.For<IAmazonS3>();
        client.GetBucketVersioningAsync(Arg.Any<GetBucketVersioningRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetBucketVersioningResponse { VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.FindValue(status) } });
        var provider = CreateS3(client, boundTarget: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.ExistsAsync(new FileStorageExistsInput("key"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.DeleteAsync(new FileStorageDeleteInput("key"), CancellationToken.None));
    }

    [Test]
    public async Task BoundS3NativeHttp_DoesNotExportObjectUrl_GenericHttpStillExports()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        using var spans = new SpanCollector();
        using var tracing = Sdk.CreateTracerProviderBuilder().AddHttpClientInstrumentation().AddProcessor(spans).Build();
        var config = new S3Configuration
        {
            Endpoint = endpoint, BucketName = "original", ForcePathStyle = true,
            AccessKeyId = Guid.NewGuid().ToString("N"), SecretAccessKey = Guid.NewGuid().ToString("N")
        };
        var resolver = Substitute.For<IS3ConfigResolver>();
        resolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(config);
        var server = RespondAsync(listener, deadline.Token);
        var bound = new S3FileStorageProvider(resolver, new S3ClientFactory(), Substitute.For<IS3PreflightVerifier>(), boundTarget: true);
        await Assert.That(await bound.ExistsAsync(new FileStorageExistsInput("private-object-key", "exact-version"), deadline.Token)).IsTrue();
        await server.WaitAsync(deadline.Token);
        var readServer = RespondAsync(listener, deadline.Token);
        var read = await bound.OpenReadAsync(new FileStorageReadInput("private-object-key", null, "exact-version"), deadline.Token);
        await read.Content.DisposeAsync();
        await readServer.WaitAsync(deadline.Token);
        var deleteServer = RespondAsync(listener, deadline.Token);
        await bound.DeleteAsync(new FileStorageDeleteInput("private-object-key", "exact-version"), deadline.Token);
        await deleteServer.WaitAsync(deadline.Token);
        var unversionedServer = RespondUnversionedAsync(listener, deadline.Token);
        await Assert.That(await bound.ExistsAsync(new FileStorageExistsInput("private-object-key"), deadline.Token)).IsTrue();
        await unversionedServer.WaitAsync(deadline.Token);
        await Assert.That(spans.Exported.Any(span => span.GetTagItem("url.full")?.ToString()?.Contains("private-object-key", StringComparison.Ordinal) == true)).IsFalse();
        var genericServer = RespondAsync(listener, deadline.Token);
        var generic = new S3FileStorageProvider(resolver, new S3ClientFactory(), Substitute.For<IS3PreflightVerifier>());
        await Assert.That(await generic.ExistsAsync(new FileStorageExistsInput("generic-object-key"), deadline.Token)).IsTrue();
        await genericServer.WaitAsync(deadline.Token);
        await Assert.That(spans.Exported.Any(span => span.GetTagItem("url.full")?.ToString()?.Contains("generic-object-key", StringComparison.Ordinal) == true)).IsTrue();
    }

    private static async Task RespondUnversionedAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        await RespondAsync(listener, cancellationToken, "<VersioningConfiguration xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\"/>");
        await RespondAsync(listener, cancellationToken);
    }

    private static async Task RespondAsync(TcpListener listener, CancellationToken cancellationToken, string body = "")
    {
        using var socket = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = socket.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken))) { }
        var response = $"HTTP/1.1 200 OK\r\nContent-Length: {Encoding.ASCII.GetByteCount(body)}\r\nContent-Type: application/xml\r\nConnection: close\r\nx-amz-version-id: exact-version\r\n\r\n{body}";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken);
    }

    private sealed class SpanCollector : BaseProcessor<Activity>
    {
        public System.Collections.Concurrent.ConcurrentQueue<Activity> Exported { get; } = new();
        public override void OnEnd(Activity data) => Exported.Enqueue(data);
    }

    private static RetainedSecretReference Reference(string key) => RetainedSecretReference.Capture(
        SecretBinding.CreateEnvironmentVariable(key, SecretScope.Instance, null, key.Replace('.', '_')), "Environment");

    private static SecretResolutionResult Resolved(RetainedSecretReference reference) => SecretResolutionResult.Resolved(
        new ResolvedSecret(reference.SettingKey, Guid.NewGuid().ToString("N"), reference.SourceType, reference.Scope, reference.ScopeId, DateTime.UtcNow));

    private sealed class BindingRepository : IStorageProviderBindingRepository
    {
        private readonly Dictionary<Guid, StorageProviderBinding> _bindings = [];
        public Task AddAsync(StorageProviderBinding binding, CancellationToken cancellationToken)
        {
            _bindings.Add(binding.Id, binding);
            return Task.CompletedTask;
        }
        public Task<StorageProviderBinding?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_bindings.GetValueOrDefault(id));
    }

    private static S3FileStorageProvider CreateS3(IAmazonS3 client, bool boundTarget = false)
    {
        var config = new S3Configuration
        {
            Endpoint = "https://storage.example.test", BucketName = "original",
            AccessKeyId = Guid.NewGuid().ToString("N"), SecretAccessKey = Guid.NewGuid().ToString("N")
        };
        var resolver = Substitute.For<IS3ConfigResolver>();
        resolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(config);
        var factory = Substitute.For<IS3ClientFactory>();
        factory.CreateDataClient(Arg.Any<S3Configuration>()).Returns(client);
        return new S3FileStorageProvider(resolver, factory, Substitute.For<IS3PreflightVerifier>(), boundTarget);
    }
}
