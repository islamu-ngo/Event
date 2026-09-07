using System.Net;
using System.Text;
using CarpaNet.Jetstream;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Models.Storage;
using Explore.Atproto.Transport;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Services.Federation;
using Explore.Infrastructure.Tests.Infrastructure;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoAdapterDidBoundaryTests
{
    private const string CaseSensitiveDid = "did:plc:CaseSensitiveOwner";
    private const string ValidCid = "bafyreicmjnvdxyjrjk4gcof66qyu3xqcfzqasygyncnczd4gggac2ig2wy";

    [Test]
    public async Task SnapshotIngress_MalformedDidsFailBeforeHandlerOrNetwork()
    {
        foreach (string malformedDid in MalformedDids())
        {
            var transport = new RecordingTransport(malformedDid);
            var gateway = new AtprotoPdsSnapshotGateway(transport.CreateHandler);

            AtprotoPdsSnapshotFetchResult result = await gateway.FetchAsync(
                malformedDid,
                1_768_212_000_000_000,
                CancellationToken.None);

            await Assert.That(result.FailureCode).IsEqualTo("snapshot_request_invalid");
            await Assert.That(result.FailureCode).DoesNotContain(malformedDid);
            await Assert.That(transport.HandlerCreations).IsEqualTo(0);
            await Assert.That(transport.Requests).IsEmpty();
        }
    }

    [Test]
    public async Task SnapshotIngress_SyntacticallyValidFutureMethodRemainsAdapterPolicy()
    {
        const string futureDid = "did:future:CaseSensitiveOwner";
        var transport = new RecordingTransport(futureDid);
        var gateway = new AtprotoPdsSnapshotGateway(transport.CreateHandler);

        AtprotoPdsSnapshotFetchResult result = await gateway.FetchAsync(
            futureDid,
            1_768_212_000_000_000,
            CancellationToken.None);

        await Assert.That(result.FailureCode).IsEqualTo("snapshot_request_invalid");
        await Assert.That(transport.HandlerCreations).IsEqualTo(0);
        await Assert.That(transport.Requests).IsEmpty();
    }

    [Test]
    public async Task ThumbnailIngress_ExactCaseSensitiveScalarCrossesBothProviderBoundaries()
    {
        var transport = new RecordingTransport(CaseSensitiveDid);
        var storage = new RecordingStorage();
        var gateway = new AtprotoThumbnailBlobGateway(
            transport.CreateHandler,
            storage,
            maximumBytes: 64,
            requestTimeout: TimeSpan.FromSeconds(1));

        FileStorageWriteResult? result = await gateway.FetchAndStageAsync(
            new AtprotoThumbnailBlobCandidate(CaseSensitiveDid, ValidCid, "image/png", 8),
            Guid.CreateVersion7(),
            CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(transport.Requests.Count).IsEqualTo(2);
        await Assert.That(transport.Requests[^1].AbsoluteUri).IsEqualTo(
            $"https://pds.example/xrpc/com.atproto.sync.getBlob?did={Uri.EscapeDataString(CaseSensitiveDid)}&cid={ValidCid}");
        await Assert.That(storage.WriteObserved).IsFalse();
    }

    [Test]
    public async Task ThumbnailIngress_MalformedDidsFailBeforeHandlerNetworkOrStorage()
    {
        foreach (string malformedDid in MalformedDids())
        {
            var transport = new RecordingTransport(malformedDid);
            var storage = new RecordingStorage();
            var gateway = new AtprotoThumbnailBlobGateway(
                transport.CreateHandler,
                storage,
                maximumBytes: 64,
                requestTimeout: TimeSpan.FromSeconds(1));

            FileStorageWriteResult? result = await gateway.FetchAndStageAsync(
                new AtprotoThumbnailBlobCandidate(malformedDid, ValidCid, "image/png", 8),
                Guid.CreateVersion7(),
                CancellationToken.None);

            await Assert.That(result).IsNull();
            await Assert.That(transport.HandlerCreations).IsEqualTo(0);
            await Assert.That(transport.Requests).IsEmpty();
            await Assert.That(storage.WriteObserved).IsFalse();
        }
    }

    [Test]
    public async Task ArchiveIngress_MalformedDidsFailBeforeProviderAndDoNotLeakToLogs()
    {
        foreach (string malformedDid in MalformedDids())
        {
            var client = new RecordingArchiveClient();
            var logger = new TestListLogger<AtprotoJetstreamArchiveProbe>();
            var probe = new AtprotoJetstreamArchiveProbe(
                client,
                Options.Create(new AtprotoJetstreamOptions()),
                logger);

            AtprotoArchiveChangeScope result = await probe.ResolveChangedDidsAsync(
                100,
                [malformedDid],
                CancellationToken.None);

            await Assert.That(result.IsConclusive).IsFalse();
            await Assert.That(client.PlanObserved).IsFalse();
            await Assert.That(logger.Entries.All(entry => !entry.Message.Contains(malformedDid, StringComparison.Ordinal)))
                .IsTrue();
        }
    }

    [Test]
    public async Task ArchiveIngress_FutureMethodSupportAndExactScalarRemainArchivePolicy()
    {
        const string futureDid = "did:future:CaseSensitiveOwner";
        var client = new RecordingArchiveClient();
        var probe = new AtprotoJetstreamArchiveProbe(
            client,
            Options.Create(new AtprotoJetstreamOptions()),
            new TestListLogger<AtprotoJetstreamArchiveProbe>());

        await probe.ResolveChangedDidsAsync(100, [futureDid], CancellationToken.None);

        await Assert.That(client.Request!.Dids).IsEquivalentTo([futureDid]);
    }

    [Test]
    public async Task JetstreamIngressCarriesTypedDidAndEmitsExactScalarOnlyAtSubscribeBoundary()
    {
        AtprotoDid did = AtprotoDid.Parse(CaseSensitiveDid);
        var subscription = new AtprotoJetstreamSubscription(
            new Uri("https://jetstream.example.test"),
            AtprotoJetstreamConstants.Collections,
            [did],
            LiveCursor: 42,
            MaxMessageSizeBytes: 2_113_536);

        JetstreamV2SubscribeOptions options = CarpaNetJetstreamEventSource.CreateSubscribeOptions(subscription);

        await Assert.That(subscription.Dids).IsEquivalentTo([did]);
        await Assert.That(options.Dids).IsEquivalentTo([CaseSensitiveDid]);
    }

    [Test]
    [Arguments("did:plc:jetstream-sentinel?raw")]
    [Arguments("did:deleted:0198ab00000070008000000000000001")]
    public async Task JetstreamSubscriberMalformedConfigurationFailsBeforeStoreOrEventSourceConstruction(string malformedDid)
    {

        OptionsValidationException? exception = await Assert.That(() =>
                AtprotoJetstreamSubscriber.NormalizeAllowedDids([malformedDid]))
            .Throws<OptionsValidationException>();

        await Assert.That(exception!.Message).DoesNotContain(malformedDid);
        await Assert.That(exception.Message.Length).IsLessThan(240);
    }

    [Test]
    [Arguments("did:plc:diagnostic-sentinel?raw")]
    [Arguments("did:deleted:0198ab00000070008000000000000001")]
    public async Task SessionStore_NewMalformedProviderSubjectFailsBeforeRepositoryWithRedactedError(string diagnosticSentinel)
    {
        bool repositoryObserved = false;
        var repository = Substitute.For<IUserAuthenticationTokenRepository>();
        repository.GetAtprotoSessionForUpdateAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                repositoryObserved = true;
                return Task.FromResult<UserAuthenticationToken?>(null);
            });
        var context = new AtprotoOAuthSessionStoreContext(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            AtprotoDid.Parse(CaseSensitiveDid),
            new Uri("https://pds.example/"),
            "oauth-client-key");
        var store = new RepositoryBackedOAuthSessionStore(repository, null!, context);

        AtprotoOAuthSessionUnavailableException? exception = await Assert.That(
                async () => await store.GetAsync(diagnosticSentinel))
            .Throws<AtprotoOAuthSessionUnavailableException>();

        await Assert.That(repositoryObserved).IsFalse();
        await Assert.That(exception!.Message).DoesNotContain(diagnosticSentinel);
        await Assert.That(exception.Message.Length).IsLessThan(200);
    }

    private static string[] MalformedDids() =>
    [
        "not-a-did",
        "did:deleted:0198ab00000070008000000000000001",
        "did:plc: leading",
        "did:plc:trailing ",
        "did:plc:value?query",
        "did:plc:value#fragment",
        "did:plc:value%20encoded",
        "did:plc:control\u0001",
        "did:plc:" + new string('x', 2049)
    ];

    private sealed class RecordingTransport(string did)
    {
        public int HandlerCreations { get; private set; }
        public List<Uri> Requests { get; } = [];

        public DelegateHandler CreateHandler(AtprotoOutboundPolicy _)
        {
            HandlerCreations++;
            return new DelegateHandler((request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Requests.Add(request.RequestUri!);
                if (request.RequestUri!.Host == "plc.directory")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            $$"""{"id":"{{did}}","service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://pds.example"}]}""",
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
        }
    }

    private sealed class RecordingArchiveClient : IAtprotoJetstreamArchiveClient
    {
        public bool PlanObserved { get; private set; }
        public JetstreamSnapshotPlanRequest? Request { get; private set; }

        public Task<JetstreamSnapshotPlan> PlanSnapshotAsync(
            JetstreamSnapshotPlanRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PlanObserved = true;
            Request = request;
            return Task.FromResult(new JetstreamSnapshotPlan { SealedTipSeq = 1_000 });
        }

        public Task<IReadOnlyList<JetstreamSegmentRow>> GetBlockRowsAsync(
            string segmentName,
            int blockIndex,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No block was planned.");
    }

    private sealed class RecordingStorage : IFileStorageProvider
    {
        public string Provider => "recording";
        public bool WriteObserved { get; private set; }

        public Task<FileStorageWriteResult> WriteAsync(
            FileStorageWriteInput input,
            CancellationToken cancellationToken)
        {
            WriteObserved = true;
            throw new InvalidOperationException("Invalid provider content must not be staged.");
        }

        public Task<FileStorageDeleteResult> DeleteAsync(
            FileStorageDeleteInput input,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FileStorageDeleteResult(Provider, input.ObjectKey, Deleted: true));

        public Task<bool> ExistsAsync(
            FileStorageExistsInput input,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<FileStorageReadResult> OpenReadAsync(
            FileStorageReadInput input,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No staged object exists.");

        public Task<FileStorageProviderStatus> TestAsync(
            CancellationToken cancellationToken,
            bool testWritePermissions = false) =>
            Task.FromResult(new FileStorageProviderStatus(Provider, true, true, false));
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(send(request, cancellationToken));
    }
}
