using System.Formats.Cbor;
using System.Net;
using System.Net.Http.Headers;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CarpaNet;
using CarpaNet.Cbor;
using CarpaNet.Identity;
using Explore.Atproto.Transport;
using Explore.Domain;
using Explore.Infrastructure.Services.Federation;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoPdsSnapshotGatewayTests
{
    private const string Did = "did:plc:snapshot-owner";
    private const long SnapshotVersion = 1_768_212_000_000_000;

    [Test]
    public async Task FetchAsync_ValidRepository_UsesExactPublicGetRepoAndMaterializesCanonicalEvent()
    {
        var transport = new SnapshotTransport(Did, "https://pds.example", SnapshotCar.Create(Did));
        var gateway = new AtprotoPdsSnapshotGateway(transport.CreatePrimaryHandler);

        var result = await gateway.FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsTrue();
        await Assert.That(result.FailureCode).IsNull();
        await Assert.That(result.Snapshot!.Did).IsEqualTo(Did);
        await Assert.That(result.Snapshot.PresentIdentities).HasSingleItem();
        await Assert.That(result.Snapshot.Items).HasSingleItem();
        await Assert.That(result.Snapshot.Items[0].Record.Direction).IsEqualTo(AtprotoRecordDirection.Inbound);
        await Assert.That(result.Snapshot.Items[0].Record.SourceVersion).IsEqualTo(SnapshotVersion);
        await Assert.That(result.Snapshot.Items[0].Record.SourceCursor).IsNull();
        await Assert.That(result.Snapshot.Items[0].EventProjection!.Name).IsEqualTo("Recovered event");
        await Assert.That(transport.PdsRequests).IsEqualTo(1);
        await Assert.That(transport.LastPdsRequest!.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(transport.LastPdsRequest.RequestUri!.AbsolutePath)
            .IsEqualTo("/xrpc/com.atproto.sync.getRepo");
        await Assert.That(transport.LastPdsRequest.RequestUri.Query).IsEqualTo("?did=did%3Aplc%3Asnapshot-owner");
        await Assert.That(transport.LastPdsRequest.Headers.Authorization).IsNull();
    }

    [Test]
    public async Task FetchAsync_ContentRejectedTargetRecord_RetainsPresenceWithoutMaterializingIt()
    {
        var gateway = Gateway(SnapshotCar.Create(Did, recordType: "app.example.wrong"));

        var result = await gateway.FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsTrue();
        await Assert.That(result.Snapshot!.PresentIdentities).HasSingleItem();
        await Assert.That(result.Snapshot.Items).IsEmpty();
    }

    [Test]
    public async Task FetchAsync_EventAndRsvpRepository_MaterializesBothExactCollectionsAndSubjectBinding()
    {
        var result = await Gateway(SnapshotCar.Create(Did, includeRsvp: true))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsTrue();
        await Assert.That(result.Snapshot!.PresentIdentities).Count().IsEqualTo(2);
        await Assert.That(result.Snapshot.Items).Count().IsEqualTo(2);
        var rsvp = result.Snapshot.Items.Single(item =>
            item.Record.Collection == "community.lexicon.calendar.rsvp");
        await Assert.That(rsvp.EventProjection).IsNull();
        await Assert.That(rsvp.Record.SubjectUri)
            .IsEqualTo($"at://{Did}/community.lexicon.calendar.event/3mrecovered22");
        await Assert.That(rsvp.Record.SubjectCid).IsNotNull();
    }

    [Test]
    public async Task FetchAsync_RepositoryDidMismatch_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create("did:plc:different-owner"))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.Snapshot).IsNull();
        await Assert.That(result.FailureCode).IsEqualTo("repository_identity_mismatch");
    }

    [Test]
    public async Task FetchAsync_BlockContentDoesNotMatchCid_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create(Did, tamperRecordBlock: true))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_integrity_invalid");
    }

    [Test]
    public async Task FetchAsync_MissingReferencedRecordBlock_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create(Did, omitRecordBlock: true))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_incomplete");
    }

    [Test]
    public async Task FetchAsync_TruncatedCar_FailsBeforeRepositoryAllocation()
    {
        SnapshotRepository complete = SnapshotCar.Create(Did);
        var result = await Gateway(complete with { Car = complete.Car[..^1] })
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_framing_invalid");
    }

    [Test]
    public async Task FetchAsync_UnsafePdsEndpoint_FailsBeforePdsRequest()
    {
        var transport = new SnapshotTransport(Did, "https://127.0.0.1", SnapshotCar.Create(Did));
        var result = await new AtprotoPdsSnapshotGateway(transport.CreatePrimaryHandler)
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("pds_endpoint_invalid");
        await Assert.That(transport.PdsRequests).IsEqualTo(0);
    }

    [Test]
    public async Task FetchAsync_DeclaredOversizeResponse_FailsBeforeBuffering()
    {
        var transport = new SnapshotTransport(Did, "https://pds.example", SnapshotCar.Create(Did))
        {
            DeclaredCarLength = AtprotoPdsSnapshotGateway.MaximumCarBytes + 1L
        };
        var result = await new AtprotoPdsSnapshotGateway(transport.CreatePrimaryHandler)
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_too_large");
    }

    [Test]
    public async Task SafetyBounds_AreExplicitAndFinite()
    {
        await Assert.That(AtprotoPdsSnapshotGateway.MaximumCarBytes).IsGreaterThan(0);
        await Assert.That(AtprotoPdsSnapshotGateway.MaximumBlocks).IsGreaterThan(0);
        await Assert.That(AtprotoPdsSnapshotGateway.MaximumTargetRecords).IsGreaterThan(0);
        await Assert.That(AtprotoPdsSnapshotGateway.MaximumTargetRecordBytes).IsGreaterThan(0);
        await Assert.That(AtprotoPdsSnapshotGateway.MaximumTargetRecordDepth).IsGreaterThan(0);
        await Assert.That(AtprotoPdsSnapshotGateway.RequestTimeout).IsGreaterThan(TimeSpan.Zero);
        await Assert.That(AtprotoPdsSnapshotGateway.RequestTimeout).IsLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task FetchAsync_ValidSecp256k1Commit_Completes()
    {
        var result = await Gateway(SnapshotCar.Create(Did, signingKey: SnapshotSigningKey.K256))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsTrue();
        await Assert.That(result.Snapshot!.Items).HasSingleItem();
    }

    [Test]
    public async Task RepositorySignatureVerifier_OfficialP256InteropVector_AcceptsLowSAndRejectsHighS()
    {
        DidDocument document = DidDocument.FromJson($$"""
            {"id":"{{Did}}","verificationMethod":[{"id":"#atproto","type":"Multikey","controller":"{{Did}}","publicKeyMultibase":"zDnaembgSGUhZULN2Caob4HLJPaxBh92N7rtH21TErzqf8HQo"}]}
            """);
        AtprotoRepositorySigningKey key = AtprotoRepositorySnapshotVerifier.ReadSigningKey(document, Did);
        byte[] unsignedCommit = Convert.FromHexString("a16568656c6c6f65776f726c64");
        byte[] lowS = Convert.FromHexString(
            "daf64db06dd42afbcefc20e5addbf2651212385ca58a7061d09ba973a29c5a82561311e9b427ddb8f95e0db1b7ae4a376199e7f4bc34223a62c5599ec490de82");
        byte[] highS = Convert.FromHexString(
            "daf64db06dd42afbcefc20e5addbf2651212385ca58a7061d09ba973a29c5a82a9ecee154bd8224806a1f24e4851b5c85b4d12b8eae37c4a90f4712437d246cf");

        bool lowSValid = AtprotoRepositorySnapshotVerifier.VerifySignature(
            new([], unsignedCommit, lowS),
            key);
        bool highSValid = AtprotoRepositorySnapshotVerifier.VerifySignature(
            new([], unsignedCommit, highS),
            key);

        await Assert.That(lowSValid).IsTrue();
        await Assert.That(highSValid).IsFalse();
    }

    [Test]
    public async Task FetchAsync_ZeroSignature_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create(Did, signatureFault: SignatureFault.Zero))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_signature_invalid");
    }

    [Test]
    public async Task FetchAsync_TamperedSignature_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create(Did, signatureFault: SignatureFault.Tampered))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_signature_invalid");
    }

    [Test]
    public async Task FetchAsync_HighSSignature_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create(Did, signatureFault: SignatureFault.HighS))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_signature_invalid");
    }

    [Test]
    public async Task FetchAsync_RotatedSigningKey_RefreshesIdentityOnceWithoutRefetchingSameOrigin()
    {
        SnapshotRepository repository = SnapshotCar.Create(Did, signingKey: SnapshotSigningKey.RotatedP256);
        var transport = new SnapshotTransport(Did, "https://pds.example", repository)
        {
            InitialPublicKeyMultibase = SnapshotSigningKey.P256.PublicKeyMultibase,
            RefreshedPublicKeyMultibase = SnapshotSigningKey.RotatedP256.PublicKeyMultibase
        };

        var result = await new AtprotoPdsSnapshotGateway(transport.CreatePrimaryHandler)
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsTrue();
        await Assert.That(transport.IdentityRequests).IsEqualTo(2);
        await Assert.That(transport.PdsRequests).IsEqualTo(1);
    }

    [Test]
    public async Task FetchAsync_RotatedSigningKeyAndPds_RefetchesFromRefreshedOrigin()
    {
        SnapshotRepository repository = SnapshotCar.Create(Did, signingKey: SnapshotSigningKey.RotatedP256);
        var transport = new SnapshotTransport(Did, "https://old-pds.example", repository)
        {
            InitialPublicKeyMultibase = SnapshotSigningKey.P256.PublicKeyMultibase,
            RefreshedPublicKeyMultibase = SnapshotSigningKey.RotatedP256.PublicKeyMultibase,
            RefreshedPdsEndpoint = "https://new-pds.example"
        };

        var result = await new AtprotoPdsSnapshotGateway(transport.CreatePrimaryHandler)
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsTrue();
        await Assert.That(transport.IdentityRequests).IsEqualTo(2);
        await Assert.That(transport.PdsRequests).IsEqualTo(2);
        await Assert.That(transport.PdsRequestHosts).IsEquivalentTo(["old-pds.example", "new-pds.example"]);
    }

    [Test]
    public async Task FetchAsync_AmbiguousAtprotoSigningKeys_FailsBeforePdsRequest()
    {
        SnapshotRepository repository = SnapshotCar.Create(Did);
        var transport = new SnapshotTransport(Did, "https://pds.example", repository)
        {
            DuplicateAtprotoSigningKey = true
        };

        var result = await new AtprotoPdsSnapshotGateway(transport.CreatePrimaryHandler)
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("identity_signing_key_invalid");
        await Assert.That(transport.PdsRequests).IsEqualTo(0);
    }

    [Test]
    public async Task FetchAsync_UnknownSigningKeyCodec_FailsBeforePdsRequest()
    {
        SnapshotRepository repository = SnapshotCar.Create(Did);
        var transport = new SnapshotTransport(Did, "https://pds.example", repository)
        {
            InitialPublicKeyMultibase = "z11111111111111111111111111111111111"
        };

        var result = await new AtprotoPdsSnapshotGateway(transport.CreatePrimaryHandler)
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("identity_signing_key_invalid");
        await Assert.That(transport.PdsRequests).IsEqualTo(0);
    }

    [Test]
    public async Task FetchAsync_MstPrefixExceedsPreviousKey_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create(Did, mstFault: MstFault.PrefixOverrun))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_structure_invalid");
    }

    [Test]
    public async Task FetchAsync_MstKeysOutOfOrder_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create(Did, mstFault: MstFault.InNodeOrder))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_structure_invalid");
    }

    [Test]
    public async Task FetchAsync_MstKeyOnWrongLayer_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create(Did, mstFault: MstFault.WrongLayer))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_structure_invalid");
    }

    [Test]
    public async Task FetchAsync_MstChildOutsideParentRange_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create(Did, mstFault: MstFault.WrongChildRange))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_structure_invalid");
    }

    [Test]
    public async Task FetchAsync_MalformedTargetRecordPath_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create(Did, recordKey: "."))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_structure_invalid");
    }

    [Test]
    public async Task FetchAsync_InvalidCollectionNsid_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create(Did, collection: "community..calendar.event"))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_structure_invalid");
    }

    [Test]
    public async Task FetchAsync_NonTidTargetRecordKey_RetainsPresenceWithoutMaterializing()
    {
        var result = await Gateway(SnapshotCar.Create(Did, recordKey: "self"))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsTrue();
        await Assert.That(result.Snapshot!.PresentIdentities).HasSingleItem();
        await Assert.That(result.Snapshot.Items).IsEmpty();
    }

    [Test]
    public async Task FetchAsync_MaximumLengthRecordKey_RetainsPresenceWithoutMaterializing()
    {
        var result = await Gateway(SnapshotCar.Create(Did, recordKey: new string('a', 512)))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsTrue();
        await Assert.That(result.Snapshot!.PresentIdentities).HasSingleItem();
        await Assert.That(result.Snapshot.Items).IsEmpty();
    }

    [Test]
    public async Task FetchAsync_OverMaximumLengthRecordKey_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create(Did, recordKey: new string('a', 513)))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_structure_invalid");
    }

    [Test]
    public async Task FetchAsync_InvalidRevisionTid_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create(Did, revision: "3mrecovered20"))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_structure_invalid");
    }

    [Test]
    public async Task FetchAsync_FutureRevisionTid_FailsWholeSnapshot()
    {
        string future = SnapshotCar.Tid(SnapshotVersion + (long)TimeSpan.FromMinutes(6).TotalMicroseconds);
        var result = await Gateway(SnapshotCar.Create(Did, revision: future))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_structure_invalid");
    }

    [Test]
    public async Task FetchAsync_MissingPrevField_FailsWholeSnapshot()
    {
        var result = await Gateway(SnapshotCar.Create(Did, omitPrev: true))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("repository_structure_invalid");
    }

    [Test]
    public async Task FetchAsync_AdditionalCarRoot_IsAccepted()
    {
        var result = await Gateway(SnapshotCar.Create(Did, includeAdditionalRoot: true))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsTrue();
    }

    [Test]
    public async Task FetchAsync_OversizeTargetRecord_RetainsPresenceWithoutDecoding()
    {
        var result = await Gateway(SnapshotCar.Create(
                Did,
                recordPaddingBytes: (1024 * 1024) + 1))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsTrue();
        await Assert.That(result.Snapshot!.PresentIdentities).HasSingleItem();
        await Assert.That(result.Snapshot.Items).IsEmpty();
    }

    [Test]
    public async Task FetchAsync_DeepTargetRecord_RetainsPresenceWithoutRecursiveDecoding()
    {
        var result = await Gateway(SnapshotCar.Create(Did, recordNestingDepth: 80))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsTrue();
        await Assert.That(result.Snapshot!.PresentIdentities).HasSingleItem();
        await Assert.That(result.Snapshot.Items).IsEmpty();
    }

    [Test]
    public async Task FetchAsync_DeepTaggedTargetRecord_RetainsPresenceWithoutRecursiveDecoding()
    {
        var result = await Gateway(SnapshotCar.Create(Did, recordTagDepth: 80))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsTrue();
        await Assert.That(result.Snapshot!.PresentIdentities).HasSingleItem();
        await Assert.That(result.Snapshot.Items).IsEmpty();
    }

    [Test]
    [Arguments(RecordFault.FloatingPoint)]
    [Arguments(RecordFault.UnsignedIntegerOverflow)]
    [Arguments(RecordFault.NonTextMapKey)]
    public async Task FetchAsync_NonDagCborTargetRecord_RetainsPresenceWithoutMaterializing(
        RecordFault fault)
    {
        var result = await Gateway(SnapshotCar.Create(Did, recordFault: fault))
            .FetchAsync(Did, SnapshotVersion, CancellationToken.None);

        await Assert.That(result.IsComplete).IsTrue();
        await Assert.That(result.Snapshot!.PresentIdentities).HasSingleItem();
        await Assert.That(result.Snapshot.Items).IsEmpty();
    }

    private static AtprotoPdsSnapshotGateway Gateway(SnapshotRepository repository) => new(
        new SnapshotTransport(Did, "https://pds.example", repository).CreatePrimaryHandler,
        new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(SnapshotVersion / 1_000)));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SnapshotTransport(string did, string pdsEndpoint, SnapshotRepository repository)
    {
        public long? DeclaredCarLength { get; init; }
        public string InitialPublicKeyMultibase { get; init; } = repository.SigningKey.PublicKeyMultibase;
        public string? RefreshedPublicKeyMultibase { get; init; }
        public string? RefreshedPdsEndpoint { get; init; }
        public bool DuplicateAtprotoSigningKey { get; init; }
        public int IdentityRequests { get; private set; }
        public int PdsRequests { get; private set; }
        public List<string> PdsRequestHosts { get; } = [];
        public HttpRequestMessage? LastPdsRequest { get; private set; }

        public DelegateHandler CreatePrimaryHandler(AtprotoOutboundPolicy _) =>
            new DelegateHandler(request =>
            {
                if (request.RequestUri!.Host == "plc.directory")
                {
                    IdentityRequests++;
                    string publicKey = IdentityRequests == 1
                        ? InitialPublicKeyMultibase
                        : RefreshedPublicKeyMultibase ?? InitialPublicKeyMultibase;
                    string endpoint = IdentityRequests == 1
                        ? pdsEndpoint
                        : RefreshedPdsEndpoint ?? pdsEndpoint;
                    string duplicate = DuplicateAtprotoSigningKey
                        ? $",{{\"id\":\"{did}#atproto\",\"type\":\"Multikey\",\"controller\":\"{did}\",\"publicKeyMultibase\":\"{publicKey}\"}}"
                        : string.Empty;
                    string document = $$"""
                        {"id":"{{did}}","verificationMethod":[{"id":"#atproto","type":"Multikey","controller":"{{did}}","publicKeyMultibase":"{{publicKey}}"}{{duplicate}}],"service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"{{endpoint}}"}]}
                        """;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(document, Encoding.UTF8, "application/json")
                    };
                }

                PdsRequests++;
                PdsRequestHosts.Add(request.RequestUri.Host);
                LastPdsRequest = request;
                var content = new ByteArrayContent(repository.Car);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.ipld.car");
                if (DeclaredCarLength.HasValue)
                {
                    content.Headers.ContentLength = DeclaredCarLength.Value;
                }

                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            });
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responder(request));
        }
    }

    private static class SnapshotCar
    {
        private const string Collection = "community.lexicon.calendar.event";
        private const string RecordKey = "3mrecovered22";
        private const string RsvpCollection = "community.lexicon.calendar.rsvp";

        public static SnapshotRepository Create(
            string repositoryDid,
            string recordType = Collection,
            bool tamperRecordBlock = false,
            bool omitRecordBlock = false,
            bool includeRsvp = false,
            SnapshotSigningKey? signingKey = null,
            SignatureFault signatureFault = SignatureFault.None,
            MstFault mstFault = MstFault.None,
            string? collection = null,
            string? recordKey = null,
            string? revision = null,
            bool omitPrev = false,
            bool includeAdditionalRoot = false,
            int recordPaddingBytes = 0,
            int recordNestingDepth = 0,
            int recordTagDepth = 0,
            RecordFault recordFault = RecordFault.None)
        {
            signingKey ??= SnapshotSigningKey.P256;
            collection ??= Collection;
            recordKey ??= RecordKey;
            revision ??= Tid(SnapshotVersion);
            byte[] record = Record(
                recordType,
                recordPaddingBytes,
                recordNestingDepth,
                recordTagDepth,
                recordFault);
            ATCid recordCid = Cid(record);
            var records = new List<SnapshotLeaf>
            {
                new($"{collection}/{recordKey}", recordCid, record)
            };
            if (includeRsvp || mstFault is MstFault.WrongLayer or MstFault.WrongChildRange)
            {
                byte[] rsvp = Rsvp(repositoryDid, recordCid, recordKey);
                records.Add(new($"{RsvpCollection}/3mrsvp2222222", Cid(rsvp), rsvp));
            }

            if (mstFault != MstFault.None)
            {
                byte[] peer = Record(recordType, 0, 0, 0, RecordFault.None);
                records.Add(new($"{Collection}/peer-4", Cid(peer), peer));
            }

            MstBuild mst = Mst(records, mstFault);
            byte[] commit = Commit(repositoryDid, mst.RootCid, signingKey, revision, omitPrev, signatureFault);
            ATCid commitCid = Cid(commit);
            byte[] extraRootData = CanonicalNullMap("noop");
            ATCid extraRootCid = Cid(extraRootData);
            byte[] header = Header(commitCid, includeAdditionalRoot ? extraRootCid : (ATCid?)null);
            if (tamperRecordBlock)
            {
                record[^1] ^= 0x01;
            }

            using var stream = new MemoryStream();
            WriteSection(stream, header);
            WriteBlock(stream, commitCid, commit);
            foreach (SnapshotBlock block in mst.Blocks)
            {
                WriteBlock(stream, block.Cid, block.Data);
            }

            if (includeAdditionalRoot)
            {
                WriteBlock(stream, extraRootCid, extraRootData);
            }

            if (!omitRecordBlock)
            {
                foreach (var value in records)
                {
                    WriteBlock(stream, value.Cid, value.Data);
                }
            }

            return new(stream.ToArray(), signingKey);
        }

        public static string Tid(long unixMicroseconds)
        {
            const string alphabet = "234567abcdefghijklmnopqrstuvwxyz";
            Span<char> value = stackalloc char[13];
            ulong timestamp = checked((ulong)unixMicroseconds);
            for (int index = 10; index >= 0; index--)
            {
                value[index] = alphabet[(int)(timestamp & 31)];
                timestamp >>= 5;
            }

            value[11] = '2';
            value[12] = '2';
            return new string(value);
        }

        private static byte[] Record(
            string type,
            int paddingBytes,
            int nestingDepth,
            int tagDepth,
            RecordFault fault)
        {
            var writer = new DagCborWriter(CborConformanceMode.Canonical);
            writer.WriteStartMap(
                3
                + (paddingBytes > 0 ? 1 : 0)
                + (nestingDepth > 0 ? 1 : 0)
                + (tagDepth > 0 ? 1 : 0)
                + (fault == RecordFault.None ? 0 : 1));
            writer.WriteTextString("name");
            writer.WriteTextString("Recovered event");
            writer.WriteTextString("$type");
            writer.WriteTextString(type);
            if (nestingDepth > 0)
            {
                writer.WriteTextString("nested");
                for (int depth = 0; depth < nestingDepth; depth++)
                {
                    writer.WriteStartArray(1);
                }

                writer.WriteNull();
                for (int depth = 0; depth < nestingDepth; depth++)
                {
                    writer.WriteEndArray();
                }
            }

            if (tagDepth > 0)
            {
                writer.WriteTextString("tagged");
                for (int depth = 0; depth < tagDepth; depth++)
                {
                    writer.WriteTag((CborTag)42);
                }

                byte[] link = Cid("tagged"u8.ToArray()).ToBytes();
                var taggedLink = new byte[link.Length + 1];
                link.CopyTo(taggedLink, 1);
                writer.WriteByteString(taggedLink);
            }

            if (fault == RecordFault.FloatingPoint)
            {
                writer.WriteTextString("rating");
                writer.WriteDouble(1.5);
            }

            if (fault == RecordFault.UnsignedIntegerOverflow)
            {
                writer.WriteTextString("counter");
                writer.WriteUInt64(ulong.MaxValue);
            }

            if (paddingBytes > 0)
            {
                writer.WriteTextString("padding");
                writer.WriteByteString(new byte[paddingBytes]);
            }

            writer.WriteTextString("createdAt");
            writer.WriteTextString("2026-01-12T10:00:00Z");
            if (fault == RecordFault.NonTextMapKey)
            {
                writer.WriteTextString("invalidMap");
                writer.WriteStartMap(1);
                writer.WriteInt32(1);
                writer.WriteTextString("invalid");
                writer.WriteEndMap();
            }

            writer.WriteEndMap();
            return writer.Encode();
        }

        private static byte[] Rsvp(string did, ATCid eventCid, string eventRecordKey)
        {
            var writer = new DagCborWriter(CborConformanceMode.Canonical);
            writer.WriteStartMap(3);
            writer.WriteTextString("$type");
            writer.WriteTextString("community.lexicon.calendar.rsvp");
            writer.WriteTextString("status");
            writer.WriteTextString("community.lexicon.calendar.rsvp#going");
            writer.WriteTextString("subject");
            writer.WriteStartMap(2);
            writer.WriteTextString("cid");
            writer.WriteTextString(eventCid.Value);
            writer.WriteTextString("uri");
            writer.WriteTextString($"at://{did}/{Collection}/{eventRecordKey}");
            writer.WriteEndMap();
            writer.WriteEndMap();
            return writer.Encode();
        }

        private static MstBuild Mst(List<SnapshotLeaf> leaves, MstFault fault)
        {
            leaves.Sort((left, right) => CompareBytes(left.KeyBytes, right.KeyBytes));
            var blocks = new List<SnapshotBlock>();
            if (fault is MstFault.PrefixOverrun or MstFault.InNodeOrder or MstFault.WrongLayer)
            {
                SnapshotBlock flat = EncodeNode(null, leaves.Select(leaf => new MstLocalEntry(leaf, null)).ToList(), fault);
                blocks.Add(flat);
                return new(flat.Cid, blocks);
            }

            int rootLayer = leaves.Max(leaf => leaf.Depth);
            SnapshotBlock root = BuildMstNode(leaves, rootLayer, blocks, isRoot: true, fault);
            return new(root.Cid, blocks);
        }

        private static SnapshotBlock BuildMstNode(
            List<SnapshotLeaf> leaves,
            int layer,
            List<SnapshotBlock> blocks,
            bool isRoot,
            MstFault fault)
        {
            List<int> pivots = leaves
                .Select((leaf, index) => (leaf, index))
                .Where(value => value.leaf.Depth == layer)
                .Select(value => value.index)
                .ToList();
            ATCid? left = null;
            var entries = new List<MstLocalEntry>();
            if (pivots.Count == 0)
            {
                SnapshotBlock child = BuildMstNode(leaves, layer - 1, blocks, isRoot: false, fault);
                left = child.Cid;
            }
            else
            {
                if (pivots[0] > 0)
                {
                    left = BuildMstNode(leaves.GetRange(0, pivots[0]), layer - 1, blocks, false, fault).Cid;
                }

                for (int index = 0; index < pivots.Count; index++)
                {
                    int pivot = pivots[index];
                    int next = index + 1 < pivots.Count ? pivots[index + 1] : leaves.Count;
                    ATCid? tree = pivot + 1 < next
                        ? BuildMstNode(leaves.GetRange(pivot + 1, next - pivot - 1), layer - 1, blocks, false, fault).Cid
                        : (ATCid?)null;
                    entries.Add(new(leaves[pivot], tree));
                }
            }

            if (isRoot && fault == MstFault.WrongChildRange && entries.Count > 0)
            {
                entries[0] = entries[0] with { Tree = left };
                left = null;
            }

            SnapshotBlock node = EncodeNode(left, entries, isRoot ? fault : MstFault.None);
            blocks.Add(node);
            return node;
        }

        private static SnapshotBlock EncodeNode(ATCid? left, List<MstLocalEntry> entries, MstFault fault)
        {
            if (fault == MstFault.InNodeOrder)
            {
                entries.Reverse();
            }

            var writer = new DagCborWriter(CborConformanceMode.Canonical);
            writer.WriteStartMap(2);
            writer.WriteTextString("e");
            writer.WriteStartArray(entries.Count);
            byte[] previous = [];
            for (int index = 0; index < entries.Count; index++)
            {
                MstLocalEntry entry = entries[index];
                byte[] key = entry.Leaf.KeyBytes;
                int prefix = CommonPrefix(previous, key);
                if (fault == MstFault.PrefixOverrun && index == 1)
                {
                    prefix = previous.Length + 1;
                }

                writer.WriteStartMap(4);
                writer.WriteTextString("k");
                writer.WriteByteString(key.AsSpan(Math.Min(prefix, key.Length)));
                writer.WriteTextString("p");
                writer.WriteInt32(prefix);
                writer.WriteTextString("t");
                if (entry.Tree is { } tree)
                {
                    writer.WriteCidLink(tree);
                }
                else
                {
                    writer.WriteNull();
                }

                writer.WriteTextString("v");
                writer.WriteCidLink(entry.Leaf.Cid);
                writer.WriteEndMap();
                previous = key;
            }

            writer.WriteEndArray();
            writer.WriteTextString("l");
            if (left is { } leftCid)
            {
                writer.WriteCidLink(leftCid);
            }
            else
            {
                writer.WriteNull();
            }

            writer.WriteEndMap();
            byte[] data = writer.Encode();
            return new(Cid(data), data);
        }

        private static byte[] Commit(
            string did,
            ATCid mstCid,
            SnapshotSigningKey signingKey,
            string revision,
            bool omitPrev,
            SignatureFault fault)
        {
            byte[] unsigned = UnsignedCommit(did, mstCid, revision, omitPrev);
            byte[] signature = signingKey.Sign(unsigned);
            if (fault == SignatureFault.Zero)
            {
                Array.Clear(signature);
            }
            else if (fault == SignatureFault.Tampered)
            {
                signature[0] ^= 0x01;
            }
            else if (fault == SignatureFault.HighS)
            {
                signature = signingKey.ToHighS(signature);
            }

            var writer = new DagCborWriter(CborConformanceMode.Canonical);
            writer.WriteStartMap(omitPrev ? 5 : 6);
            writer.WriteTextString("did");
            writer.WriteTextString(did);
            writer.WriteTextString("rev");
            writer.WriteTextString(revision);
            writer.WriteTextString("sig");
            writer.WriteByteString(signature);
            writer.WriteTextString("data");
            writer.WriteCidLink(mstCid);
            if (!omitPrev)
            {
                writer.WriteTextString("prev");
                writer.WriteNull();
            }

            writer.WriteTextString("version");
            writer.WriteInt32(3);
            writer.WriteEndMap();
            return writer.Encode();
        }

        private static byte[] UnsignedCommit(string did, ATCid mstCid, string revision, bool omitPrev)
        {
            var writer = new DagCborWriter(CborConformanceMode.Canonical);
            writer.WriteStartMap(omitPrev ? 4 : 5);
            writer.WriteTextString("did");
            writer.WriteTextString(did);
            writer.WriteTextString("rev");
            writer.WriteTextString(revision);
            writer.WriteTextString("data");
            writer.WriteCidLink(mstCid);
            if (!omitPrev)
            {
                writer.WriteTextString("prev");
                writer.WriteNull();
            }

            writer.WriteTextString("version");
            writer.WriteInt32(3);
            writer.WriteEndMap();
            return writer.Encode();
        }

        private static byte[] Header(ATCid rootCid, ATCid? additionalRoot)
        {
            var writer = new DagCborWriter(CborConformanceMode.Canonical);
            writer.WriteStartMap(2);
            writer.WriteTextString("roots");
            writer.WriteStartArray(additionalRoot.HasValue ? 2 : 1);
            writer.WriteCidLink(rootCid);
            if (additionalRoot is { } extra)
            {
                writer.WriteCidLink(extra);
            }

            writer.WriteEndArray();
            writer.WriteTextString("version");
            writer.WriteInt32(1);
            writer.WriteEndMap();
            return writer.Encode();
        }

        private static byte[] CanonicalNullMap(string key)
        {
            var writer = new DagCborWriter(CborConformanceMode.Canonical);
            writer.WriteStartMap(1);
            writer.WriteTextString(key);
            writer.WriteNull();
            writer.WriteEndMap();
            return writer.Encode();
        }

        private static int CommonPrefix(byte[] left, byte[] right)
        {
            int length = Math.Min(left.Length, right.Length);
            int index = 0;
            while (index < length && left[index] == right[index])
            {
                index++;
            }

            return index;
        }

        private static int CompareBytes(byte[] left, byte[] right) => left.AsSpan().SequenceCompareTo(right);

        private static ATCid Cid(byte[] data) => ATCid.FromSha256Hash(SHA256.HashData(data));

        private static void WriteBlock(Stream stream, ATCid cid, byte[] data)
        {
            byte[] cidBytes = cid.ToBytes();
            WriteVarint(stream, checked((ulong)(cidBytes.Length + data.Length)));
            stream.Write(cidBytes);
            stream.Write(data);
        }

        private static void WriteSection(Stream stream, byte[] data)
        {
            WriteVarint(stream, checked((ulong)data.Length));
            stream.Write(data);
        }

        private static void WriteVarint(Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        private sealed record SnapshotLeaf(string Path, ATCid Cid, byte[] Data)
        {
            public byte[] KeyBytes { get; } = Encoding.ASCII.GetBytes(Path);
            public int Depth { get; } = ComputeDepth(Encoding.ASCII.GetBytes(Path));

            private static int ComputeDepth(byte[] key)
            {
                byte[] hash = SHA256.HashData(key);
                int zeroBits = 0;
                foreach (byte current in hash)
                {
                    if (current == 0)
                    {
                        zeroBits += 8;
                        continue;
                    }

                    zeroBits += BitOperations.LeadingZeroCount(current) - 24;
                    break;
                }

                return zeroBits / 2;
            }
        }

        private sealed record MstLocalEntry(SnapshotLeaf Leaf, ATCid? Tree);
        private sealed record SnapshotBlock(ATCid Cid, byte[] Data);
        private sealed record MstBuild(ATCid RootCid, IReadOnlyList<SnapshotBlock> Blocks);
    }

    private sealed record SnapshotRepository(byte[] Car, SnapshotSigningKey SigningKey);

    private enum SignatureFault
    {
        None,
        Zero,
        Tampered,
        HighS
    }

    private enum MstFault
    {
        None,
        PrefixOverrun,
        InNodeOrder,
        WrongLayer,
        WrongChildRange
    }

    public enum RecordFault
    {
        None,
        FloatingPoint,
        UnsignedIntegerOverflow,
        NonTextMapKey
    }

    private sealed class SnapshotSigningKey
    {
        private static readonly BigInteger P256Order = BigInteger.Parse(
            "00FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551",
            System.Globalization.NumberStyles.HexNumber);
        private static readonly BigInteger K256Order = BigInteger.Parse(
            "00FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141",
            System.Globalization.NumberStyles.HexNumber);
        private readonly ECParameters _parameters;
        private readonly BigInteger _order;

        private SnapshotSigningKey(ECParameters parameters, byte[] codec, BigInteger order)
        {
            _parameters = parameters;
            _order = order;
            byte[] compressed = new byte[35];
            codec.CopyTo(compressed, 0);
            compressed[2] = (byte)((parameters.Q.Y![^1] & 1) == 0 ? 0x02 : 0x03);
            parameters.Q.X!.CopyTo(compressed, 3);
            PublicKeyMultibase = "z" + Base58Encode(compressed);
        }

        public static SnapshotSigningKey P256 { get; } = CreateP256();
        public static SnapshotSigningKey RotatedP256 { get; } = CreateP256();
        public static SnapshotSigningKey K256 { get; } = CreateK256();
        public string PublicKeyMultibase { get; }

        public byte[] Sign(byte[] data)
        {
            using ECDsa signer = ECDsa.Create(_parameters);
            byte[] signature = signer.SignData(
                data,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            var s = new BigInteger(signature.AsSpan(32), isUnsigned: true, isBigEndian: true);
            if (s > _order / 2)
            {
                WriteFixedWidth(_order - s, signature.AsSpan(32));
            }

            return signature;
        }

        public byte[] ToHighS(byte[] signature)
        {
            byte[] high = (byte[])signature.Clone();
            var s = new BigInteger(high.AsSpan(32), isUnsigned: true, isBigEndian: true);
            WriteFixedWidth(_order - s, high.AsSpan(32));
            return high;
        }

        private static SnapshotSigningKey CreateP256()
        {
            using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return new(signer.ExportParameters(includePrivateParameters: true), [0x80, 0x24], P256Order);
        }

        private static SnapshotSigningKey CreateK256()
        {
            using ECDsa signer = ECDsa.Create(ECCurve.CreateFromValue("1.3.132.0.10"));
            return new(signer.ExportParameters(includePrivateParameters: true), [0xe7, 0x01], K256Order);
        }

        private static void WriteFixedWidth(BigInteger value, Span<byte> destination)
        {
            byte[] encoded = value.ToByteArray(isUnsigned: true, isBigEndian: true);
            destination.Clear();
            encoded.CopyTo(destination[^encoded.Length..]);
        }

        private static string Base58Encode(ReadOnlySpan<byte> data)
        {
            const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
            var value = new BigInteger(data, isUnsigned: true, isBigEndian: true);
            var encoded = new StringBuilder();
            while (value > BigInteger.Zero)
            {
                value = BigInteger.DivRem(value, 58, out BigInteger remainder);
                encoded.Insert(0, alphabet[(int)remainder]);
            }

            foreach (byte current in data)
            {
                if (current != 0)
                {
                    break;
                }

                encoded.Insert(0, '1');
            }

            return encoded.ToString();
        }
    }
}
