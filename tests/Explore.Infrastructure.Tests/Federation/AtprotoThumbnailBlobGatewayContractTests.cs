using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CarpaNet;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Models.Storage;
using Explore.Atproto.Transport;
using Explore.Infrastructure.Services.Federation;

namespace Explore.Infrastructure.Tests.Federation;

public sealed class AtprotoThumbnailBlobGatewayContractTests
{
    private const string Did = "did:plc:z72i7hdynmk6r22z27h6tvur";
    private const string Cid = "bafyreicmjnvdxyjrjk4gcof66qyu3xqcfzqasygyncnczd4gggac2ig2wy";
    private const string OtherCid = "bafyreiaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const int MaximumBytes = 8;
    private const string ReturnedProvider = "recording-provider";
    private const string ReturnedObjectKey = "returned/staged-object";
    private const long ReturnedSizeBytes = 777;
    private const string ReturnedContentType = "provider/returned-content-type";
    private const string ReturnedSha256Checksum = "provider-returned-sha256";
    private static readonly byte[] ImageBytes = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private static readonly byte[] ValidPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAACXBIWXMAAAABAAAAAQBPJcTWAAAAEElEQVR4nGP8ywACLGCSAQANEQED1LYyQAAAAABJRU5ErkJggg==");
    private static readonly byte[] ProgressiveJpegBytes = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wgARCAACAAIDAREAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAAB//EABUBAQEAAAAAAAAAAAAAAAAAAAYI/9oADAMBAAIQAxAAAAE5C1T/AP/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAQUCf//EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQMBAT8Bf//EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQIBAT8Bf//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEABj8Cf//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAT8hf//aAAwDAQACAAMAAAAQ/wD/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAEDAQE/EH//xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAECAQE/EH//xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAE/EH//2Q==");

    [Test]
    public async Task FetchAndStageAsync_ValidBlob_UsesCurrentVerifiedPdsAndStagesExactBytes()
    {
        string cid = CidFor(ValidPngBytes);
        var fixture = new Fixture(ValidPngBytes, maximumBytes: ValidPngBytes.Length);

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(cid, "image/png", ValidPngBytes.Length),
            TenantId(),
            CancellationToken.None);

        await Assert.That(fixture.IdentityRequests).IsEqualTo(1);
        await Assert.That(fixture.PdsRequests).IsEqualTo(1);
        await Assert.That(fixture.PdsRequestUris).IsEquivalentTo(
            [$"https://current-pds.example/xrpc/com.atproto.sync.getBlob?did={Uri.EscapeDataString(Did)}&cid={cid}"]);
        await Assert.That(fixture.LastPdsRequest!.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(fixture.LastPdsRequest.Headers.Authorization).IsNull();
        await Assert.That(fixture.Storage.Bytes).IsEquivalentTo(ValidPngBytes);
        await Assert.That(fixture.Storage.LastWrite).IsEqualTo(new FileStorageWriteEnvelope(
            TenantId(),
            "image/png",
            cid,
            null,
            ValidPngBytes.Length,
            ValidPngBytes.Length));
        await Assert.That(result!).IsEqualTo(new FileStorageWriteResult(
            ReturnedProvider,
            ReturnedObjectKey,
            ReturnedSizeBytes,
            ReturnedContentType,
            ReturnedSha256Checksum));
    }

    [Test]
    [Arguments("image/svg+xml")]
    [Arguments("IMAGE/SVG+XML")]
    [Arguments("image/svg+xml; charset=utf-8")]
    [Arguments("image/bmp")]
    [Arguments("text/plain")]
    public async Task FetchAndStageAsync_NonAllowlistedCandidateNeverFetchesOrStages(string mimeType)
    {
        var fixture = new Fixture(ImageBytes) { ResponseMimeType = mimeType };

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(Cid, mimeType, ImageBytes.Length),
            TenantId(),
            CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(fixture.IdentityRequests).IsEqualTo(0);
        await Assert.That(fixture.PdsRequests).IsEqualTo(0);
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    public async Task FetchAndStageAsync_SvgBytesRelabeledAsPngNeverStage()
    {
        byte[] svgBytes = Encoding.UTF8.GetBytes(
            """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""");
        string cid = ATCid.FromSha256Hash(SHA256.HashData(svgBytes)).Value;
        var fixture = new Fixture(svgBytes, maximumBytes: svgBytes.Length)
        {
            ResponseMimeType = "image/png"
        };

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(cid, "image/png", svgBytes.Length),
            TenantId(),
            CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(fixture.IdentityRequests).IsEqualTo(1);
        await Assert.That(fixture.PdsRequests).IsEqualTo(1);
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    [Arguments("image/jpeg")]
    [Arguments("image/png")]
    [Arguments("image/gif")]
    [Arguments("image/webp")]
    [Arguments("image/avif")]
    public async Task FetchAndStageAsync_ValidRasterHeaderFollowedByActiveContentNeverStages(string mimeType)
    {
        byte[] bytes = ActiveContentContainerBytes(mimeType);
        string cid = CidFor(bytes);
        var fixture = new Fixture(bytes, maximumBytes: bytes.Length)
        {
            ResponseMimeType = mimeType
        };

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(cid, mimeType, bytes.Length),
            TenantId(),
            CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(fixture.IdentityRequests).IsEqualTo(1);
        await Assert.That(fixture.PdsRequests).IsEqualTo(1);
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    [Arguments("image/jpeg", false)]
    [Arguments("ImAgE/PnG", false)]
    [Arguments("image/gif", false)]
    [Arguments("IMAGE/GIF", true)]
    [Arguments("image/webp", false)]
    [Arguments("IMAGE/WEBP", true)]
    [Arguments("image/avif", false)]
    [Arguments("IMAGE/AVIF", true)]
    public async Task FetchAndStageAsync_AllowlistedRasterMimeStagesMatchingBytesCaseInsensitively(
        string mimeType,
        bool alternateSignature)
    {
        byte[] bytes = ValidRasterBytes(mimeType, alternateSignature);
        string cid = CidFor(bytes);
        var fixture = new Fixture(bytes, maximumBytes: bytes.Length)
        {
            ResponseMimeType = mimeType.ToUpperInvariant()
        };

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(cid, mimeType, bytes.Length),
            TenantId(),
            CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(1);
        await Assert.That(fixture.Storage.Objects.Count).IsEqualTo(1);
        await Assert.That(fixture.Storage.Bytes).IsEquivalentTo(bytes);
    }

    [Test]
    public async Task FetchAndStageAsync_PngBytesRelabeledAsJpegNeverStage()
    {
        string cid = ATCid.FromSha256Hash(SHA256.HashData(ImageBytes)).Value;
        var fixture = new Fixture(ImageBytes)
        {
            ResponseMimeType = "image/jpeg"
        };

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(cid, "image/jpeg", ImageBytes.Length),
            TenantId(),
            CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(fixture.IdentityRequests).IsEqualTo(1);
        await Assert.That(fixture.PdsRequests).IsEqualTo(1);
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    [Arguments("image/jpeg")]
    [Arguments("image/png")]
    [Arguments("image/gif")]
    [Arguments("image/webp")]
    [Arguments("image/avif")]
    public async Task FetchAndStageAsync_TruncatedRasterSignatureNeverStages(string mimeType)
    {
        byte[] valid = ValidRasterBytes(mimeType, alternateSignature: false);
        byte[] truncated = valid[..^1];
        string cid = CidFor(truncated);
        var fixture = new Fixture(truncated, maximumBytes: truncated.Length)
        {
            ResponseMimeType = mimeType
        };

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(cid, mimeType, truncated.Length),
            TenantId(),
            CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(fixture.IdentityRequests).IsEqualTo(1);
        await Assert.That(fixture.PdsRequests).IsEqualTo(1);
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    public async Task FetchAndStageAsync_ReResolvesDidForEveryCallAndUsesRemappedPds()
    {
        var fixture = new Fixture(ImageBytes)
        {
            PdsOrigins = ["https://old-pds.example", "https://new-pds.example"]
        };

        await fixture.Gateway.FetchAndStageAsync(Candidate(Cid, "image/png", 8), TenantId(), CancellationToken.None);
        await fixture.Gateway.FetchAndStageAsync(Candidate(Cid, "image/png", 8), TenantId(), CancellationToken.None);

        await Assert.That(fixture.IdentityRequests).IsEqualTo(2);
        await Assert.That(fixture.PdsRequestUris).IsEquivalentTo(
        [
            $"https://old-pds.example/xrpc/com.atproto.sync.getBlob?did={Uri.EscapeDataString(Did)}&cid={Cid}",
            $"https://new-pds.example/xrpc/com.atproto.sync.getBlob?did={Uri.EscapeDataString(Did)}&cid={Cid}"
        ]);
    }

    [Test]
    public async Task FetchAndStageAsync_RedirectTargetIsNeverFollowed()
    {
        var fixture = new Fixture(ImageBytes) { RedirectBlobResponse = true };

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(Cid, "image/png", 8),
            TenantId(),
            CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(fixture.PdsRequests).IsEqualTo(1);
        await Assert.That(fixture.RedirectTargetRequests).IsEqualTo(0);
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    public async Task FetchAndStageAsync_DeclaredOversizeRejectsBeforeBodyOrStorage()
    {
        var fixture = new Fixture(ImageBytes);
        var source = new ChunkedMemoryStream([.. ImageBytes, 0x00], chunkSize: 1);
        fixture.ContentFactory = () => new ProbeContent(source, declaredLength: MaximumBytes + 1, "image/png");

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(Cid, "image/png", MaximumBytes),
            TenantId(),
            CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(source.BytesRead).IsEqualTo(0);
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    public async Task FetchAndStageAsync_UndeclaredOversizeStopsAtBoundBeforeStorage()
    {
        var fixture = new Fixture(ImageBytes);
        var source = new ChunkedMemoryStream([.. ImageBytes, 0x00], chunkSize: 1);
        fixture.ContentFactory = () => new ProbeContent(source, declaredLength: null, "image/png");

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(Cid, "image/png", MaximumBytes),
            TenantId(),
            CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(source.BytesRead).IsEqualTo(MaximumBytes + 1);
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    public async Task FetchAndStageAsync_StalledBodyEndsOnCallerCancellationWithoutStorage()
    {
        var fixture = new Fixture(ImageBytes);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ContentFactory = () => new ProbeContent(new StallingStream(started), declaredLength: null, "image/png");
        using var cancellation = new CancellationTokenSource();

        Task<FileStorageWriteResult?> fetch = fixture.Gateway.FetchAndStageAsync(
            Candidate(Cid, "image/png", MaximumBytes), TenantId(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        await Assert.That(async () => await fetch).Throws<OperationCanceledException>();
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    public async Task FetchAndStageAsync_StalledBodyEndsOnGatewayTimeoutWithoutCallerCancellationOrStorage()
    {
        var fixture = new Fixture(ImageBytes, TimeSpan.FromMilliseconds(25));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ContentFactory = () => new ProbeContent(new StallingStream(started), declaredLength: null, "image/png");

        Task<FileStorageWriteResult?> fetch = fixture.Gateway.FetchAndStageAsync(
            Candidate(Cid, "image/png", MaximumBytes), TenantId(), CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        FileStorageWriteResult? result = await fetch.WaitAsync(TimeSpan.FromSeconds(1));

        await Assert.That(result).IsNull();
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    public async Task FetchAndStageAsync_CancellationMidStreamPropagatesWithoutStorage()
    {
        var fixture = new Fixture(ImageBytes);
        using var cancellation = new CancellationTokenSource();
        var source = new ChunkedMemoryStream(ImageBytes, chunkSize: 1, cancellation.Cancel);
        fixture.ContentFactory = () => new ProbeContent(source, declaredLength: null, "image/png");

        await Assert.That(async () => await fixture.Gateway.FetchAndStageAsync(
                Candidate(Cid, "image/png", MaximumBytes), TenantId(), cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(source.BytesRead).IsEqualTo(1);
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    public async Task FetchAndStageAsync_CancellationAfterStorageDeletesExactObject()
    {
        string cid = CidFor(ValidPngBytes);
        var fixture = new Fixture(ValidPngBytes, maximumBytes: ValidPngBytes.Length);
        using var cancellation = new CancellationTokenSource();
        fixture.Storage.CancelAfterWrite = cancellation;

        await Assert.That(async () => await fixture.Gateway.FetchAndStageAsync(
                Candidate(cid, "image/png", ValidPngBytes.Length), TenantId(), cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(fixture.Storage.Objects).IsEmpty();
        await Assert.That(fixture.Storage.DeletedKeys).IsEquivalentTo([ReturnedObjectKey]);
    }

    [Test]
    public async Task FetchAndStageAsync_InvalidCidOrContentBindingMismatchDoesNotStage()
    {
        var invalid = new Fixture(ImageBytes);
        var invalidResult = await invalid.Gateway.FetchAndStageAsync(
            Candidate("not-a-cid", "image/png", MaximumBytes), TenantId(), CancellationToken.None);

        var mismatch = new Fixture(ImageBytes);
        var mismatchResult = await mismatch.Gateway.FetchAndStageAsync(
            Candidate(OtherCid, "image/png", MaximumBytes), TenantId(), CancellationToken.None);

        await Assert.That(invalidResult).IsNull();
        await Assert.That(invalid.IdentityRequests).IsEqualTo(0);
        await Assert.That(invalid.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(mismatchResult).IsNull();
        await Assert.That(mismatch.IdentityRequests).IsEqualTo(1);
        await Assert.That(mismatch.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(mismatch.Storage.Objects).IsEmpty();
    }

    [Test]
    [Arguments("text/plain")]
    [Arguments("application/octet-stream")]
    [Arguments("image/png; instruction=ignore-previous-validation")]
    public async Task FetchAndStageAsync_NonImageResponseMimeDoesNotStage(string responseMime)
    {
        var fixture = new Fixture(ImageBytes) { ResponseMimeType = responseMime };

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(Cid, "image/png", MaximumBytes), TenantId(), CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    public async Task FetchAndStageAsync_DeclaredAndResponseRasterMimeMustMatch()
    {
        var fixture = new Fixture(ImageBytes) { ResponseMimeType = "image/jpeg" };

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(Cid, "image/png", MaximumBytes), TenantId(), CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    [Arguments(7L, 8L, 8L)]
    [Arguments(8L, 7L, 7L)]
    [Arguments(8L, 8L, 7L)]
    public async Task FetchAndStageAsync_CandidateHttpAndActualSizesMustMatchAndBePositive(
        long candidateSize,
        long httpSize,
        int actualSize)
    {
        var fixture = new Fixture(ImageBytes[..actualSize]) { DeclaredResponseLength = httpSize };

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(Cid, "image/png", candidateSize), TenantId(), CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    public async Task FetchAndStageAsync_StorageFailureLeavesNoObject()
    {
        string cid = CidFor(ValidPngBytes);
        var fixture = new Fixture(ValidPngBytes, maximumBytes: ValidPngBytes.Length)
        {
            Storage = { FailWrite = true }
        };

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(cid, "image/png", ValidPngBytes.Length), TenantId(), CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(1);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
        await Assert.That(fixture.Storage.DeletedKeys).IsEmpty();
    }

    [Test]
    public async Task CleanupAsync_DownstreamFailureDeletesExactStagedObjectIdempotently()
    {
        string cid = CidFor(ValidPngBytes);
        var fixture = new Fixture(ValidPngBytes, maximumBytes: ValidPngBytes.Length);
        var staged = await fixture.Gateway.FetchAndStageAsync(
            Candidate(cid, "image/png", ValidPngBytes.Length), TenantId(), CancellationToken.None);

        await fixture.Gateway.CleanupAsync(staged!, CancellationToken.None);
        await fixture.Gateway.CleanupAsync(staged!, CancellationToken.None);

        await Assert.That(fixture.Storage.DeletedKeys).IsEquivalentTo(
            [ReturnedObjectKey, ReturnedObjectKey]);
        await Assert.That(fixture.Storage.DeleteResults).IsEquivalentTo([true, false]);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    [Arguments("", "image/png", 8L)]
    [Arguments("not-a-cid", "image/png", 8L)]
    [Arguments(Cid, "text/plain; instruction=ignore-validation", 8L)]
    [Arguments(Cid, "image/png", 0L)]
    [Arguments(Cid, "image/png", 9L)]
    public async Task FetchAndStageAsync_MalformedOptionalCandidate_PerformsNoNetworkOrStorage(
        string cid,
        string mimeType,
        long declaredSize)
    {
        var fixture = new Fixture(ImageBytes);

        var result = await fixture.Gateway.FetchAndStageAsync(
            Candidate(cid, mimeType, declaredSize), TenantId(), CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(fixture.IdentityRequests).IsEqualTo(0);
        await Assert.That(fixture.PdsRequests).IsEqualTo(0);
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    [Test]
    public async Task FetchAndStageAsync_AbsentOptionalCandidate_PerformsNoNetworkOrStorage()
    {
        var fixture = new Fixture(ImageBytes);

        var result = await fixture.Gateway.FetchAndStageAsync(null, TenantId(), CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(fixture.IdentityRequests).IsEqualTo(0);
        await Assert.That(fixture.PdsRequests).IsEqualTo(0);
        await Assert.That(fixture.Storage.WriteCount).IsEqualTo(0);
        await Assert.That(fixture.Storage.Objects).IsEmpty();
    }

    private static Guid TenantId() => Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");

    private static AtprotoThumbnailBlobCandidate Candidate(string cid, string mimeType, long size) =>
        new(Did, cid, mimeType, size);

    private static string CidFor(byte[] bytes) =>
        ATCid.FromSha256Hash(SHA256.HashData(bytes)).Value;

    // Static fixtures use FFmpeg 8.1.2; the progressive JPEG and two-frame animated WebP use
    // ImageMagick 7.1.2-27 and were independently identified/decoded before being committed.
    private static byte[] ValidRasterBytes(string mimeType, bool alternateSignature) =>
        mimeType.ToLowerInvariant() switch
        {
            "image/jpeg" => ProgressiveJpegBytes,
            "image/png" => ValidPngBytes,
            "image/gif" => Convert.FromBase64String(
                alternateSignature
                    ? "R0lGODdhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw=="
                    : "R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw=="),
            "image/webp" => Convert.FromBase64String(
                alternateSignature
                    ? "UklGRsAAAABXRUJQVlA4WAoAAAACAAAAAQAAAQAAQU5JTQYAAAD/////AABBTk1GSAAAAAAAAAAAAAEAAAEAAGQAAAJWUDggMAAAANABAJ0BKgIAAgACADQloAJ0ugH4AAOwAP7wxAv/ILlhdcjX/yA/5Af8gP/48gAAAEFOTUZEAAAAAAAAAAAAAQAAAQAAZAAAAFZQOCAsAAAAlAEAnQEqAgACAAAANCWgAnS6AAOYAP75k2//kB//kB//kB//ID/iF3sgMAA="
                    : "UklGRhwAAABXRUJQVlA4TA8AAAAvAUAAAAcQ9Y/+ByKi/wEA"),
            "image/avif" => AvifFixture(alternateSignature),
            _ => throw new ArgumentOutOfRangeException(nameof(mimeType))
        };

    private static byte[] AvifFixture(bool compatibleBrandOnly)
    {
        byte[] bytes = Convert.FromBase64String(
            "AAAAIGZ0eXBhdmlmAAAAAGF2aWZtaWYxbWlhZk1BMUIAAAD5bWV0YQAAAAAAAAAvaGRscgAAAAAAAAAAcGljdAAAAAAAAAAAAAAAAFBpY3R1cmVIYW5kbGVyAAAAAA5waXRtAAAAAAABAAAAHmlsb2MAAAAARAAAAQABAAAAAQAAASEAAAAbAAAAKGlpbmYAAAAAAAEAAAAaaW5mZQIAAAAAAQAAYXYwMUNvbG9yAAAAAGppcHJwAAAAS2lwY28AAAAUaXNwZQAAAAAAAAACAAAAAgAAABBwaXhpAAAAAAMICAgAAAAMYXYxQ4EADAAAAAATY29scm5jbHgAAgACAAIAAAAAF2lwbWEAAAAAAAAAAQABBAECgwQAAAAjbWRhdAoFGAA2wCAyEhgAAABQAABAA1Lt5xf080WmIA==");
        if (compatibleBrandOnly)
        {
            "mif1"u8.CopyTo(bytes.AsSpan(8, 4));
        }

        return bytes;
    }

    private static byte[] ActiveContentContainerBytes(string mimeType)
    {
        byte[] activeContent = Encoding.UTF8.GetBytes(
            """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""");
        return [.. ValidRasterBytes(mimeType, alternateSignature: false), .. activeContent];
    }

    private sealed class Fixture
    {
        private readonly byte[] _bytes;

        public Fixture(
            byte[] bytes,
            TimeSpan? requestTimeout = null,
            int maximumBytes = MaximumBytes)
        {
            _bytes = bytes;
            Storage = new RecordingStorageProvider();
            Gateway = new AtprotoThumbnailBlobGateway(
                CreatePrimaryHandler,
                Storage,
                maximumBytes,
                requestTimeout: requestTimeout ?? TimeSpan.FromSeconds(5));
        }

        public AtprotoThumbnailBlobGateway Gateway { get; }
        public RecordingStorageProvider Storage { get; }
        public string ResponseMimeType { get; init; } = "image/png";
        public long? DeclaredResponseLength { get; init; }
        public bool RedirectBlobResponse { get; init; }
        public string[] PdsOrigins { get; init; } = ["https://current-pds.example"];
        public Func<HttpContent>? ContentFactory { get; set; }
        public int IdentityRequests { get; private set; }
        public int PdsRequests { get; private set; }
        public int RedirectTargetRequests { get; private set; }
        public List<string> PdsRequestUris { get; } = [];
        public HttpRequestMessage? LastPdsRequest { get; private set; }

        private HttpMessageHandler CreatePrimaryHandler(AtprotoOutboundPolicy _) =>
            new DelegateHandler((request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (request.RequestUri!.Host == "plc.directory")
                {
                    string pds = PdsOrigins[Math.Min(IdentityRequests, PdsOrigins.Length - 1)];
                    IdentityRequests++;
                    return Json($$"""
                        {"id":"{{Did}}","service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"{{pds}}"}]}
                        """);
                }

                if (request.RequestUri.Host == "redirect-target.example")
                {
                    RedirectTargetRequests++;
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                PdsRequests++;
                LastPdsRequest = request;
                PdsRequestUris.Add(request.RequestUri.AbsoluteUri);
                if (RedirectBlobResponse)
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                    redirect.Headers.Location = new Uri("https://redirect-target.example/blob");
                    return redirect;
                }

                HttpContent content = ContentFactory?.Invoke()
                    ?? new ProbeContent(_bytes, DeclaredResponseLength ?? _bytes.Length, ResponseMimeType);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            });

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingStorageProvider : IFileStorageProvider
    {
        public string Provider => "memory";
        public bool FailWrite { get; set; }
        public CancellationTokenSource? CancelAfterWrite { get; set; }
        public int WriteCount { get; private set; }
        public byte[] Bytes { get; private set; } = [];
        public Dictionary<string, byte[]> Objects { get; } = [];
        public List<string> DeletedKeys { get; } = [];
        public List<bool> DeleteResults { get; } = [];
        public FileStorageWriteEnvelope? LastWrite { get; private set; }

        public async Task<FileStorageWriteResult> WriteAsync(
            FileStorageWriteInput input,
            CancellationToken cancellationToken)
        {
            WriteCount++;
            LastWrite = new(
                input.TenantId,
                input.ContentType,
                input.SafeDisplayName,
                input.Extension,
                input.ExpectedSizeBytes,
                input.MaxSizeBytes);
            if (FailWrite)
            {
                throw new IOException("deterministic storage failure");
            }

            using var buffer = new MemoryStream();
            await input.Content.CopyToAsync(buffer, cancellationToken);
            Bytes = buffer.ToArray();
            Objects[ReturnedObjectKey] = Bytes;
            CancelAfterWrite?.Cancel();
            return new(
                ReturnedProvider,
                ReturnedObjectKey,
                ReturnedSizeBytes,
                ReturnedContentType,
                ReturnedSha256Checksum);
        }

        public Task<FileStorageDeleteResult> DeleteAsync(
            FileStorageDeleteInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeletedKeys.Add(input.ObjectKey);
            bool deleted = Objects.Remove(input.ObjectKey);
            DeleteResults.Add(deleted);
            if (deleted)
            {
                Bytes = [];
            }

            return Task.FromResult(new FileStorageDeleteResult(Provider, input.ObjectKey, deleted));
        }

        public Task<bool> ExistsAsync(FileStorageExistsInput input, CancellationToken cancellationToken) =>
            Task.FromResult(Objects.ContainsKey(input.ObjectKey));

        public Task<FileStorageReadResult> OpenReadAsync(
            FileStorageReadInput input,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FileStorageReadResult(
                new MemoryStream(Objects.GetValueOrDefault(input.ObjectKey) ?? [], writable: false),
                "image/png",
                Objects.GetValueOrDefault(input.ObjectKey)?.Length ?? 0,
                null));

        public Task<FileStorageProviderStatus> TestAsync(
            CancellationToken cancellationToken,
            bool testWritePermissions = false) =>
            Task.FromResult(new FileStorageProviderStatus(Provider, true, true, false));
    }

    private sealed record FileStorageWriteEnvelope(
        Guid TenantId,
        string ContentType,
        string SafeDisplayName,
        string? Extension,
        long? ExpectedSizeBytes,
        long? MaxSizeBytes);

    private sealed class ProbeContent : HttpContent
    {
        private readonly Stream _stream;
        private readonly long? _declaredLength;

        public ProbeContent(byte[] bytes, long? declaredLength, string contentType)
            : this(new ChunkedMemoryStream(bytes, chunkSize: bytes.Length), declaredLength, contentType)
        {
        }

        public ProbeContent(Stream stream, long? declaredLength, string contentType)
        {
            _stream = stream;
            _declaredLength = declaredLength;
            Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _declaredLength.GetValueOrDefault();
            return _declaredLength.HasValue;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(_stream);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            _stream.CopyToAsync(stream);
    }

    private sealed class ChunkedMemoryStream : MemoryStream
    {
        private readonly int _chunkSize;
        private readonly Action? _afterRead;

        public ChunkedMemoryStream(byte[] bytes, int chunkSize, Action? afterRead = null)
            : base(bytes, writable: false)
        {
            _chunkSize = chunkSize;
            _afterRead = afterRead;
        }

        public int BytesRead { get; private set; }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int read = await base.ReadAsync(buffer[..Math.Min(buffer.Length, _chunkSize)], cancellationToken);
            BytesRead += read;
            if (read > 0)
            {
                _afterRead?.Invoke();
            }

            return read;
        }
    }

    private sealed class StallingStream(TaskCompletionSource started) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            return new ValueTask<int>(WaitForCancellationAsync(cancellationToken));
        }

        private static async Task<int> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request, cancellationToken));
    }
}
