using System.Diagnostics.CodeAnalysis;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text.Json;
using CarpaNet;
using CarpaNet.Cbor;
using CarpaNet.Cbor.Converters;
using CarpaNet.Identity;
using CarpaNet.Jetstream;
using CarpaNet.Repo;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Atproto.Transport;
using Explore.Domain;
using Explore.Domain.ValueObjects;

namespace Explore.Infrastructure.Services.Federation;

public sealed class AtprotoPdsSnapshotGateway : IAtprotoPdsSnapshotGateway
{
    internal const int MaximumCarBytes = 64 * 1024 * 1024;
    internal const int MaximumBlocks = 50_000;
    internal const int MaximumTargetRecords = 10_000;
    internal const int MaximumTargetRecordBytes = 1024 * 1024;
    internal const int MaximumTargetRecordDepth = 64;
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private const int MaximumIdentityResponseBytes = 1024 * 1024;
    private readonly Func<AtprotoOutboundPolicy, HttpMessageHandler> _primaryHandlerFactory;
    private readonly TimeProvider _timeProvider;

    public AtprotoPdsSnapshotGateway()
        : this(
            policy => AtprotoHardenedHttpClient.CreatePrimaryHandler(policy, TimeSpan.FromSeconds(5)),
            TimeProvider.System)
    {
    }

    internal AtprotoPdsSnapshotGateway(
        Func<AtprotoOutboundPolicy, HttpMessageHandler> primaryHandlerFactory,
        TimeProvider? timeProvider = null)
    {
        _primaryHandlerFactory = primaryHandlerFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AtprotoPdsSnapshotFetchResult> FetchAsync(
        string did,
        long snapshotVersion,
        CancellationToken cancellationToken)
    {
        if (!AtprotoDid.TryParse(did, out AtprotoDid parsedDid)
            || !IsSupportedDid(parsedDid)
            || !TryFromUnixMicroseconds(snapshotVersion, out DateTime observedAt))
        {
            return AtprotoPdsSnapshotFetchResult.Failed("snapshot_request_invalid");
        }

        var policy = new AtprotoOutboundPolicy(allowsDevelopmentLoopback: false);
        try
        {
            ResolvedIdentity identity = await ResolveIdentityAsync(
                parsedDid,
                policy,
                cancellationToken).ConfigureAwait(false);
            byte[] car = await FetchRepositoryAsync(
                identity.PdsOrigin,
                parsedDid.Value,
                policy,
                cancellationToken).ConfigureAwait(false);
            AtprotoVerifiedRepositorySnapshot snapshot = ReadAndValidateSnapshot(
                parsedDid.Value,
                car,
                cancellationToken);

            if (!AtprotoRepositorySnapshotVerifier.VerifySignature(snapshot, identity.SigningKey))
            {
                ResolvedIdentity refreshedIdentity = await ResolveIdentityAsync(
                    parsedDid,
                    policy,
                    cancellationToken).ConfigureAwait(false);
                if (refreshedIdentity.PdsOrigin != identity.PdsOrigin)
                {
                    car = await FetchRepositoryAsync(
                        refreshedIdentity.PdsOrigin,
                        parsedDid.Value,
                        policy,
                        cancellationToken).ConfigureAwait(false);
                    snapshot = ReadAndValidateSnapshot(parsedDid.Value, car, cancellationToken);
                }

                if (!AtprotoRepositorySnapshotVerifier.VerifySignature(
                        snapshot,
                        refreshedIdentity.SigningKey))
                {
                    throw new SnapshotValidationException("repository_signature_invalid");
                }
            }

            return MaterializeSnapshot(
                parsedDid.Value,
                snapshotVersion,
                observedAt,
                snapshot,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SnapshotValidationException exception)
        {
            return AtprotoPdsSnapshotFetchResult.Failed(exception.FailureCode);
        }
        catch (AtprotoRepositorySnapshotValidationException exception)
        {
            return AtprotoPdsSnapshotFetchResult.Failed(exception.FailureCode);
        }
        catch (Exception exception) when (exception is InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or OverflowException
            or JsonException
            or CryptographicException)
        {
            return AtprotoPdsSnapshotFetchResult.Failed("repository_invalid");
        }
        catch
        {
            return AtprotoPdsSnapshotFetchResult.Failed("identity_resolution_failed");
        }
    }

    private async Task<ResolvedIdentity> ResolveIdentityAsync(
        AtprotoDid did,
        AtprotoOutboundPolicy policy,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(policy, MaximumIdentityResponseBytes);
        using var resolver = new IdentityResolver(client);
        DidDocument document = await resolver.ResolveDidAsync(
            did.Value,
            skipCache: true,
            cancellationToken).ConfigureAwait(false);
        if (!AtprotoDid.TryParse(document.Id, out AtprotoDid providerDid) || providerDid != did)
        {
            throw new SnapshotValidationException("identity_binding_mismatch");
        }

        AtprotoRepositorySigningKey signingKey = AtprotoRepositorySnapshotVerifier.ReadSigningKey(
            document,
            did.Value);

        DidService[] services = document.Service
            .Where(service => string.Equals(service.Type, "AtprotoPersonalDataServer", StringComparison.Ordinal)
                && (string.Equals(service.Id, "#atproto_pds", StringComparison.Ordinal)
                    || string.Equals(service.Id, $"{did.Value}#atproto_pds", StringComparison.Ordinal)))
            .ToArray();
        if (services.Length != 1
            || !Uri.TryCreate(services[0].ServiceEndpoint, UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || endpoint.AbsolutePath is not ("" or "/"))
        {
            throw new SnapshotValidationException("pds_endpoint_invalid");
        }

        try
        {
            policy.ValidateUri(endpoint);
        }
        catch (AtprotoOAuthSecurityException)
        {
            throw new SnapshotValidationException("pds_endpoint_invalid");
        }

        return new(
            new Uri(endpoint.GetLeftPart(UriPartial.Authority) + "/", UriKind.Absolute),
            signingKey);
    }

    private async Task<byte[]> FetchRepositoryAsync(
        Uri pdsOrigin,
        string did,
        AtprotoOutboundPolicy policy,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(policy, MaximumCarBytes);
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildGetRepoUri(pdsOrigin, did));
            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new SnapshotValidationException("repository_fetch_failed");
            }

            byte[] car = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (car.Length == 0)
            {
                throw new SnapshotValidationException("repository_invalid");
            }

            return car;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SnapshotValidationException)
        {
            throw;
        }
        catch (AtprotoOAuthSecurityException exception)
            when (exception.FailureCode == "response_too_large")
        {
            throw new SnapshotValidationException("repository_too_large");
        }
        catch
        {
            throw new SnapshotValidationException("repository_fetch_failed");
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The returned HttpClient owns and disposes the complete handler chain.")]
    private HttpClient CreateClient(AtprotoOutboundPolicy policy, int maximumResponseBytes) => new(
        new AtprotoBoundedResponseHandler(maximumResponseBytes, _primaryHandlerFactory(policy)),
        disposeHandler: true)
    {
        Timeout = RequestTimeout
    };

    private static Uri BuildGetRepoUri(Uri pdsOrigin, string did) => new(
        pdsOrigin,
        $"xrpc/com.atproto.sync.getRepo?did={Uri.EscapeDataString(did)}");

    private AtprotoVerifiedRepositorySnapshot ReadAndValidateSnapshot(
        string did,
        byte[] car,
        CancellationToken cancellationToken)
    {
        ValidateCarFraming(car, cancellationToken);
        ValidatedCar validatedCar = ValidateCarBlocks(car, cancellationToken);
        return AtprotoRepositorySnapshotVerifier.ReadAndValidate(
            validatedCar.RootCid,
            validatedCar.Blocks,
            did,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static AtprotoPdsSnapshotFetchResult MaterializeSnapshot(
        string did,
        long snapshotVersion,
        DateTime observedAt,
        AtprotoVerifiedRepositorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var present = new List<AtprotoPdsSnapshotIdentity>();
        var items = new List<AtprotoPdsSnapshotItem>();
        foreach (AtprotoVerifiedRepositoryRecord record in snapshot.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AtprotoJetstreamConstants.Collections.Contains(
                    record.Collection,
                    StringComparer.Ordinal))
            {
                continue;
            }

            if (present.Count >= MaximumTargetRecords)
            {
                throw new SnapshotValidationException("repository_target_limit_exceeded");
            }

            present.Add(new(record.Collection, record.RecordKey));
            if (record.Data.Length > MaximumTargetRecordBytes
                || !AtprotoRepositorySnapshotVerifier.IsValidTid(record.RecordKey))
            {
                continue;
            }

            AtprotoPdsSnapshotItem? item = TryMaterialize(
                did,
                record.Collection,
                record.RecordKey,
                record.Cid,
                record.Data,
                snapshotVersion,
                observedAt,
                cancellationToken);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        AtprotoPdsSnapshotIdentity[] orderedPresent = present
            .OrderBy(identity => identity.Collection, StringComparer.Ordinal)
            .ThenBy(identity => identity.RecordKey, StringComparer.Ordinal)
            .ToArray();
        AtprotoPdsSnapshotItem[] orderedItems = items
            .OrderBy(item => item.Record.Collection, StringComparer.Ordinal)
            .ThenBy(item => item.Record.RecordKey, StringComparer.Ordinal)
            .ToArray();
        return AtprotoPdsSnapshotFetchResult.Complete(new(did, orderedPresent, orderedItems));
    }

    private static void ValidateCarFraming(
        ReadOnlySpan<byte> car,
        CancellationToken cancellationToken)
    {
        if (car.Length is 0 or > MaximumCarBytes)
        {
            throw new SnapshotValidationException("repository_too_large");
        }

        int offset = 0;
        int headerLength = ReadSectionLength(car, ref offset);
        offset += headerLength;
        while (offset < car.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int blockLength = ReadSectionLength(car, ref offset);
            offset += blockLength;
        }
    }

    private static int ReadSectionLength(ReadOnlySpan<byte> car, ref int offset)
    {
        ulong value = 0;
        int shift = 0;
        while (shift <= 63)
        {
            if (offset >= car.Length)
            {
                throw new SnapshotValidationException("repository_framing_invalid");
            }

            byte current = car[offset++];
            if (shift == 63 && (current & 0xfe) != 0)
            {
                throw new SnapshotValidationException("repository_framing_invalid");
            }

            value |= (ulong)(current & 0x7f) << shift;
            if ((current & 0x80) == 0)
            {
                if (value is 0 or > int.MaxValue || value > (ulong)(car.Length - offset))
                {
                    throw new SnapshotValidationException("repository_framing_invalid");
                }

                return (int)value;
            }

            shift += 7;
        }

        throw new SnapshotValidationException("repository_framing_invalid");
    }

    private static ValidatedCar ValidateCarBlocks(byte[] car, CancellationToken cancellationToken)
    {
        using var reader = new CarReader(car);
        if (reader.Header.Roots.Count is < 1 or > AtprotoRepositorySnapshotVerifier.MaximumCarRoots
            || !reader.Header.Roots[0].IsAtProtoBlessedFormat)
        {
            throw new SnapshotValidationException("repository_structure_invalid");
        }

        var blocks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        int count = 0;
        foreach (CarBlock block in reader.ReadBlocks())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > MaximumBlocks)
            {
                throw new SnapshotValidationException("repository_block_limit_exceeded");
            }

            if (!VerifyBlock(block.Cid, block.Data, cancellationToken))
            {
                throw new SnapshotValidationException("repository_integrity_invalid");
            }

            if (blocks.TryGetValue(block.Cid.Value, out byte[]? existing))
            {
                if (!existing.AsSpan().SequenceEqual(block.Data))
                {
                    throw new SnapshotValidationException("repository_integrity_invalid");
                }
            }
            else
            {
                blocks.Add(block.Cid.Value, block.Data);
            }
        }

        if (!blocks.ContainsKey(reader.Header.Roots[0].Value))
        {
            throw new SnapshotValidationException("repository_incomplete");
        }

        return new(reader.Header.Roots[0], blocks);
    }

    private static bool VerifyBlock(ATCid cid, byte[] data, CancellationToken cancellationToken)
    {
        if (!cid.IsAtProtoBlessedFormat
            || cid.Hash is not { Length: ATCid.Sha256HashLength } hash)
        {
            return false;
        }

        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int offset = 0; offset < data.Length; offset += 64 * 1024)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(64 * 1024, data.Length - offset);
            hasher.AppendData(data, offset, length);
        }

        return CryptographicOperations.FixedTimeEquals(hasher.GetHashAndReset(), hash);
    }

    private static AtprotoPdsSnapshotItem? TryMaterialize(
        string did,
        string collection,
        string recordKey,
        ATCid cid,
        byte[] recordData,
        long snapshotVersion,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        JsonElement record;
        try
        {
            if (!IsCanonicalRecordWithinDepth(recordData, cancellationToken))
            {
                return null;
            }

            var reader = new DagCborReader(recordData);
            record = new JsonElementCborConverter().ReadTyped(ref reader);
            if (reader.BytesRemaining != 0 || record.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or InvalidDataException
            or ArgumentException
            or OverflowException
            or JsonException
            or CborContentException)
        {
            return null;
        }

        // Synthetic envelope so PDS-sourced records go through exactly the same validation as streamed
        // ones. TimeUs carries the snapshot version because that is the axis SourceVersion is compared
        // on; Seq only has to clear the parser's cursor floor, and SourceCursor is nulled out below.
        var envelope = new JetstreamV2Event
        {
            Did = did,
            Seq = snapshotVersion,
            TimeUs = snapshotVersion,
            Kind = JetstreamV2EventKind.Commit,
            Commit = new JetstreamV2Commit
            {
                Operation = JetstreamV2CommitOperation.Update,
                Collection = collection,
                Rkey = recordKey,
                Cid = cid.Value,
                Record = record
            }
        };
        AtprotoJetstreamParsedEnvelope parsed = AtprotoJetstreamEnvelopeParser.Parse(
            envelope,
            currentCursor: 0,
            [did],
            observedAt);
        if (parsed.Record is null)
        {
            return null;
        }

        parsed.Record.SourceCursor = null;
        return new(parsed.Record, parsed.EventProjection);
    }

    private static bool IsCanonicalRecordWithinDepth(
        byte[] data,
        CancellationToken cancellationToken)
    {
        try
        {
            var reader = new CborReader(data, CborConformanceMode.Canonical);
            var containers = new List<RecordContainer>();
            bool rootSeen = false;
            int valuesRead = 0;
            while (true)
            {
                if ((valuesRead++ & 0xff) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                CborReaderState state = reader.PeekState();
                switch (state)
                {
                    case CborReaderState.UnsignedInteger:
                        if (!TryConsumeRecordItem(state, containers, ref rootSeen)
                            || reader.ReadUInt64() > long.MaxValue)
                        {
                            return false;
                        }

                        break;
                    case CborReaderState.NegativeInteger:
                        if (!TryConsumeRecordItem(state, containers, ref rootSeen)
                            || reader.ReadCborNegativeIntegerRepresentation() > long.MaxValue)
                        {
                            return false;
                        }

                        break;
                    case CborReaderState.ByteString:
                        if (!TryConsumeRecordItem(state, containers, ref rootSeen))
                        {
                            return false;
                        }

                        reader.ReadByteString();
                        break;
                    case CborReaderState.TextString:
                        if (!TryConsumeRecordItem(state, containers, ref rootSeen))
                        {
                            return false;
                        }

                        reader.ReadTextString();
                        break;
                    case CborReaderState.StartArray:
                        if (!TryConsumeRecordItem(state, containers, ref rootSeen)
                            || reader.ReadStartArray() is not { } arrayLength)
                        {
                            return false;
                        }

                        containers.Add(new(CborReaderState.EndArray, arrayLength, IsMap: false));
                        if (containers.Count > MaximumTargetRecordDepth)
                        {
                            return false;
                        }

                        break;
                    case CborReaderState.EndArray:
                        if (containers.Count == 0
                            || containers[^1].EndState != state
                            || containers[^1].RemainingItems != 0)
                        {
                            return false;
                        }

                        reader.ReadEndArray();
                        containers.RemoveAt(containers.Count - 1);
                        break;
                    case CborReaderState.StartMap:
                        if (!TryConsumeRecordItem(state, containers, ref rootSeen)
                            || reader.ReadStartMap() is not { } mapLength)
                        {
                            return false;
                        }

                        containers.Add(new(
                            CborReaderState.EndMap,
                            checked(mapLength * 2),
                            IsMap: true));
                        if (containers.Count > MaximumTargetRecordDepth)
                        {
                            return false;
                        }

                        break;
                    case CborReaderState.EndMap:
                        if (containers.Count == 0
                            || containers[^1].EndState != state
                            || containers[^1].RemainingItems != 0)
                        {
                            return false;
                        }

                        reader.ReadEndMap();
                        containers.RemoveAt(containers.Count - 1);
                        break;
                    case CborReaderState.Tag:
                        if (!TryConsumeRecordItem(state, containers, ref rootSeen)
                            || reader.ReadTag() != (CborTag)42
                            || reader.PeekState() != CborReaderState.ByteString)
                        {
                            return false;
                        }

                        byte[] cidLink = reader.ReadByteString();
                        if (cidLink.Length != 37 || cidLink[0] != 0)
                        {
                            return false;
                        }

                        ATCid.FromBytes(cidLink.AsSpan(1).ToArray());
                        break;
                    case CborReaderState.SimpleValue:
                    case CborReaderState.HalfPrecisionFloat:
                    case CborReaderState.SinglePrecisionFloat:
                    case CborReaderState.DoublePrecisionFloat:
                        return false;
                    case CborReaderState.Null:
                        if (!TryConsumeRecordItem(state, containers, ref rootSeen))
                        {
                            return false;
                        }

                        reader.ReadNull();
                        break;
                    case CborReaderState.Boolean:
                        if (!TryConsumeRecordItem(state, containers, ref rootSeen))
                        {
                            return false;
                        }

                        reader.ReadBoolean();
                        break;
                    case CborReaderState.Finished:
                        return rootSeen && containers.Count == 0 && reader.BytesRemaining == 0;
                    default:
                        return false;
                }
            }
        }
        catch (Exception exception) when (exception is CborContentException
            or InvalidOperationException
            or InvalidDataException
            or ArgumentException
            or OverflowException)
        {
            return false;
        }
    }

    private static bool TryConsumeRecordItem(
        CborReaderState state,
        List<RecordContainer> containers,
        ref bool rootSeen)
    {
        if (containers.Count == 0)
        {
            if (rootSeen)
            {
                return false;
            }

            rootSeen = true;
            return true;
        }

        int index = containers.Count - 1;
        RecordContainer container = containers[index];
        if (container.RemainingItems <= 0
            || (container.IsMap
                && container.RemainingItems % 2 == 0
                && state != CborReaderState.TextString))
        {
            return false;
        }

        containers[index] = container with { RemainingItems = container.RemainingItems - 1 };
        return true;
    }

    private static bool IsSupportedDid(AtprotoDid did) =>
        did.Value.Length <= 255
        && did.Method is "plc" or "web";

    private static bool TryFromUnixMicroseconds(long value, out DateTime result)
    {
        try
        {
            result = DateTime.UnixEpoch.AddTicks(checked(value * 10));
            return value > 0;
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
        {
            result = default;
            return false;
        }
    }

    private sealed class SnapshotValidationException(string failureCode) : Exception
    {
        public string FailureCode { get; } = failureCode;
    }

    private sealed record ResolvedIdentity(Uri PdsOrigin, AtprotoRepositorySigningKey SigningKey);

    private sealed record ValidatedCar(ATCid RootCid, IReadOnlyDictionary<string, byte[]> Blocks);

    private sealed record RecordContainer(
        CborReaderState EndState,
        int RemainingItems,
        bool IsMap);
}
