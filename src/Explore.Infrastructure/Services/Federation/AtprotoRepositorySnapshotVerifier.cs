using System.Formats.Cbor;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CarpaNet;
using CarpaNet.Identity;

namespace Explore.Infrastructure.Services.Federation;

internal static class AtprotoRepositorySnapshotVerifier
{
    internal const int MaximumCommitBytes = 16 * 1024;
    internal const int MaximumMstNodeBytes = 1024 * 1024;
    internal const int MaximumRecordBytes = 64 * 1024 * 1024;
    internal const int MaximumCarRoots = 16;
    internal const int MaximumMstNodes = 50_000;
    internal const int MaximumRecords = 250_000;
    internal const int MaximumEntriesPerNode = 4_096;
    internal const int MaximumMstDepth = 64;
    internal static readonly TimeSpan MaximumFutureRevisionSkew = TimeSpan.FromMinutes(5);

    private const int DagCborMulticodec = 0x71;
    private const int MaximumRepositoryPathBytes = 830;
    private const string Base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    private const string TidAlphabet = "234567abcdefghijklmnopqrstuvwxyz";
    private static readonly BigInteger P256Prime = ParseUnsignedHex(
        "FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF");
    private static readonly BigInteger P256A = P256Prime - 3;
    private static readonly BigInteger P256B = ParseUnsignedHex(
        "5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B");
    private static readonly BigInteger P256Order = ParseUnsignedHex(
        "FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551");
    private static readonly BigInteger K256Prime = ParseUnsignedHex(
        "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F");
    private static readonly BigInteger K256Order = ParseUnsignedHex(
        "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141");

    internal static AtprotoVerifiedRepositorySnapshot ReadAndValidate(
        ATCid rootCid,
        IReadOnlyDictionary<string, byte[]> blocks,
        string expectedDid,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            if (blocks is null
                || string.IsNullOrEmpty(expectedDid)
                || !IsDagCborCid(rootCid))
            {
                throw Invalid("repository_structure_invalid");
            }

            byte[] commitBytes = GetVerifiedBlock(
                blocks,
                rootCid,
                MaximumCommitBytes,
                cancellationToken);
            ParsedCommit commit = ReadCommit(commitBytes, expectedDid, now);
            var state = new VerificationState(blocks, cancellationToken);
            WalkMstNode(commit.Data, expectedLayer: null, lowerBound: null, upperBound: null, isRoot: true, state);

            cancellationToken.ThrowIfCancellationRequested();
            AtprotoVerifiedRepositoryRecord[] records = state.Records.ToArray();
            return new AtprotoVerifiedRepositorySnapshot(records, commit.UnsignedBytes, commit.Signature);
        }
        catch (AtprotoRepositorySnapshotValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CborContentException
            or CryptographicException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or OverflowException)
        {
            throw Invalid("repository_structure_invalid", exception);
        }
    }

    internal static AtprotoRepositorySigningKey ReadSigningKey(
        DidDocument document,
        string expectedDid)
    {
        try
        {
            if (document is null
                || string.IsNullOrEmpty(expectedDid)
                || !string.Equals(document.Id, expectedDid, StringComparison.Ordinal))
            {
                throw Invalid("identity_binding_mismatch");
            }

            if (document.VerificationMethod is not { } verificationMethods
                || verificationMethods.Any(method => method is null))
            {
                throw Invalid("identity_signing_key_invalid");
            }

            VerificationMethod[] candidates = verificationMethods
                .Where(method => method.Id?.EndsWith("#atproto", StringComparison.Ordinal) == true)
                .ToArray();
            if (candidates.Length != 1)
            {
                throw Invalid("identity_signing_key_invalid");
            }

            VerificationMethod method = candidates[0];
            if ((method.Id != "#atproto" && method.Id != $"{expectedDid}#atproto")
                || !string.Equals(method.Controller, expectedDid, StringComparison.Ordinal)
                || !string.Equals(method.Type, "Multikey", StringComparison.Ordinal)
                || method.PublicKeyMultibase is not { } publicKeyMultibase)
            {
                throw Invalid("identity_signing_key_invalid");
            }

            byte[] multikey = DecodeBase58Multikey(publicKeyMultibase);
            AtprotoRepositorySigningCurve curve;
            BigInteger prime;
            BigInteger a;
            BigInteger b;
            BigInteger order;
            if (multikey[0] == 0x80 && multikey[1] == 0x24)
            {
                curve = AtprotoRepositorySigningCurve.P256;
                prime = P256Prime;
                a = P256A;
                b = P256B;
                order = P256Order;
            }
            else if (multikey[0] == 0xe7 && multikey[1] == 0x01)
            {
                curve = AtprotoRepositorySigningCurve.K256;
                prime = K256Prime;
                a = BigInteger.Zero;
                b = new BigInteger(7);
                order = K256Order;
            }
            else
            {
                throw Invalid("identity_signing_key_invalid");
            }

            ReadOnlySpan<byte> compressedPoint = multikey.AsSpan(2);
            (byte[] x, byte[] y) = DecompressPoint(compressedPoint, prime, a, b);
            return new AtprotoRepositorySigningKey(curve, x, y, order);
        }
        catch (AtprotoRepositorySnapshotValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CryptographicException
            or InvalidOperationException
            or ArgumentException
            or OverflowException)
        {
            throw Invalid("identity_signing_key_invalid", exception);
        }
    }

    internal static bool VerifySignature(
        AtprotoVerifiedRepositorySnapshot snapshot,
        AtprotoRepositorySigningKey signingKey)
    {
        if (snapshot.Signature.Length != 64 || !IsLowS(snapshot.Signature, signingKey.Order))
        {
            return false;
        }

        try
        {
            ECCurve curve = signingKey.Curve == AtprotoRepositorySigningCurve.P256
                ? ECCurve.NamedCurves.nistP256
                : ECCurve.CreateFromValue("1.3.132.0.10");
            using ECDsa verifier = ECDsa.Create(new ECParameters
            {
                Curve = curve,
                Q = new ECPoint
                {
                    X = signingKey.X,
                    Y = signingKey.Y
                }
            });
            byte[] hash = SHA256.HashData(snapshot.UnsignedCommitBytes);
            return verifier.VerifyHash(
                hash,
                snapshot.Signature,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception exception) when (exception is CryptographicException
            or ArgumentException
            or PlatformNotSupportedException
            or NotSupportedException)
        {
            return false;
        }
    }

    private static ParsedCommit ReadCommit(
        byte[] bytes,
        string expectedDid,
        DateTimeOffset now)
    {
        var reader = new CborReader(bytes, CborConformanceMode.Canonical);
        if (reader.ReadStartMap() != 6)
        {
            throw Invalid("repository_structure_invalid");
        }

        string? did = null;
        int? version = null;
        ATCid? data = null;
        string? rev = null;
        ATCid? prev = null;
        bool prevPresent = false;
        byte[]? signature = null;
        for (int index = 0; index < 6; index++)
        {
            string field = reader.ReadTextString();
            switch (field)
            {
                case "did" when did is null:
                    did = reader.ReadTextString();
                    break;
                case "version" when version is null:
                    version = reader.ReadInt32();
                    break;
                case "data" when data is null:
                    data = ReadCid(reader);
                    break;
                case "rev" when rev is null:
                    rev = reader.ReadTextString();
                    break;
                case "prev" when !prevPresent:
                    prevPresent = true;
                    prev = ReadNullableCid(reader);
                    break;
                case "sig" when signature is null:
                    signature = reader.ReadByteString();
                    break;
                default:
                    throw Invalid("repository_structure_invalid");
            }
        }

        reader.ReadEndMap();
        if (reader.BytesRemaining != 0
            || did is null
            || version != 3
            || data is null
            || rev is null
            || !prevPresent
            || signature is not { Length: 64 }
            || !IsValidRevision(rev, now))
        {
            throw Invalid("repository_structure_invalid");
        }

        if (!string.Equals(did, expectedDid, StringComparison.Ordinal))
        {
            throw Invalid("repository_identity_mismatch");
        }

        byte[] unsignedBytes = WriteUnsignedCommit(did, data.Value, rev, prev);
        return new ParsedCommit(data.Value, unsignedBytes, signature);
    }

    private static byte[] WriteUnsignedCommit(string did, ATCid data, string rev, ATCid? prev)
    {
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteStartMap(5);
        writer.WriteTextString("did");
        writer.WriteTextString(did);
        writer.WriteTextString("rev");
        writer.WriteTextString(rev);
        writer.WriteTextString("data");
        WriteCid(writer, data);
        writer.WriteTextString("prev");
        if (prev is { } previous)
        {
            WriteCid(writer, previous);
        }
        else
        {
            writer.WriteNull();
        }

        writer.WriteTextString("version");
        writer.WriteInt32(3);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static void WalkMstNode(
        ATCid cid,
        int? expectedLayer,
        byte[]? lowerBound,
        byte[]? upperBound,
        bool isRoot,
        VerificationState state)
    {
        state.CancellationToken.ThrowIfCancellationRequested();
        if (!IsDagCborCid(cid)
            || !state.VisitedNodes.Add(cid.Value)
            || ++state.NodeCount > MaximumMstNodes)
        {
            throw Invalid("repository_structure_invalid");
        }

        byte[] nodeBytes = GetVerifiedBlock(
            state.Blocks,
            cid,
            MaximumMstNodeBytes,
            state.CancellationToken);
        RawMstNode node = ReadMstNode(nodeBytes);
        if (node.Entries.Length == 0)
        {
            if (node.Left is null)
            {
                if (!isRoot)
                {
                    throw Invalid("repository_structure_invalid");
                }

                return;
            }

            if (isRoot || expectedLayer is null or <= 0)
            {
                throw Invalid("repository_structure_invalid");
            }

            WalkMstNode(
                node.Left.Value,
                expectedLayer.Value - 1,
                lowerBound,
                upperBound,
                isRoot: false,
                state);
            return;
        }

        int layer = expectedLayer ?? ComputeLayer(node.Entries[0].Key);
        if (layer is < 0 or > MaximumMstDepth)
        {
            throw Invalid("repository_structure_invalid");
        }

        byte[]? previous = null;
        foreach (RawMstEntry entry in node.Entries)
        {
            if (ComputeLayer(entry.Key) != layer
                || (previous is not null && Compare(previous, entry.Key) >= 0)
                || (lowerBound is not null && Compare(lowerBound, entry.Key) >= 0)
                || (upperBound is not null && Compare(entry.Key, upperBound) >= 0))
            {
                throw Invalid("repository_structure_invalid");
            }

            previous = entry.Key;
        }

        if (layer == 0 && (node.Left is not null || node.Entries.Any(entry => entry.Tree is not null)))
        {
            throw Invalid("repository_structure_invalid");
        }

        if (node.Left is { } left)
        {
            WalkMstNode(left, layer - 1, lowerBound, node.Entries[0].Key, isRoot: false, state);
        }

        for (int index = 0; index < node.Entries.Length; index++)
        {
            RawMstEntry entry = node.Entries[index];
            AddRecord(entry, state);
            if (entry.Tree is { } tree)
            {
                byte[]? nextKey = index + 1 < node.Entries.Length
                    ? node.Entries[index + 1].Key
                    : upperBound;
                WalkMstNode(tree, layer - 1, entry.Key, nextKey, isRoot: false, state);
            }
        }
    }

    private static void AddRecord(RawMstEntry entry, VerificationState state)
    {
        state.CancellationToken.ThrowIfCancellationRequested();
        if (++state.RecordCount > MaximumRecords
            || !TryReadRepositoryPath(entry.Key, out string collection, out string recordKey))
        {
            throw Invalid("repository_structure_invalid");
        }

        string path = $"{collection}/{recordKey}";
        if (!state.Paths.Add(path) || !IsDagCborCid(entry.Value))
        {
            throw Invalid("repository_structure_invalid");
        }

        byte[] recordBytes = GetVerifiedBlock(
            state.Blocks,
            entry.Value,
            MaximumRecordBytes,
            state.CancellationToken);
        state.Records.Add(new AtprotoVerifiedRepositoryRecord(collection, recordKey, entry.Value, recordBytes));
    }

    private static RawMstNode ReadMstNode(byte[] bytes)
    {
        var reader = new CborReader(bytes, CborConformanceMode.Canonical);
        if (reader.ReadStartMap() != 2)
        {
            throw Invalid("repository_structure_invalid");
        }

        ATCid? left = null;
        bool leftPresent = false;
        CompressedMstEntry[]? compressedEntries = null;
        for (int index = 0; index < 2; index++)
        {
            string field = reader.ReadTextString();
            switch (field)
            {
                case "l" when !leftPresent:
                    leftPresent = true;
                    left = ReadNullableCid(reader);
                    break;
                case "e" when compressedEntries is null:
                    int? count = reader.ReadStartArray();
                    if (count is null or < 0 or > MaximumEntriesPerNode)
                    {
                        throw Invalid("repository_structure_invalid");
                    }

                    compressedEntries = new CompressedMstEntry[count.Value];
                    for (int entryIndex = 0; entryIndex < compressedEntries.Length; entryIndex++)
                    {
                        compressedEntries[entryIndex] = ReadCompressedMstEntry(reader);
                    }

                    reader.ReadEndArray();
                    break;
                default:
                    throw Invalid("repository_structure_invalid");
            }
        }

        reader.ReadEndMap();
        if (reader.BytesRemaining != 0 || !leftPresent || compressedEntries is null)
        {
            throw Invalid("repository_structure_invalid");
        }

        var entries = new RawMstEntry[compressedEntries.Length];
        byte[] previous = [];
        for (int index = 0; index < compressedEntries.Length; index++)
        {
            CompressedMstEntry compressed = compressedEntries[index];
            if (compressed.PrefixLength < 0 || compressed.PrefixLength > previous.Length)
            {
                throw Invalid("repository_structure_invalid");
            }

            int keyLength = checked(compressed.PrefixLength + compressed.Suffix.Length);
            if (keyLength is 0 or > MaximumRepositoryPathBytes)
            {
                throw Invalid("repository_structure_invalid");
            }

            var key = new byte[keyLength];
            previous.AsSpan(0, compressed.PrefixLength).CopyTo(key);
            compressed.Suffix.CopyTo(key, compressed.PrefixLength);
            if (CommonPrefixLength(previous, key) != compressed.PrefixLength)
            {
                throw Invalid("repository_structure_invalid");
            }

            entries[index] = new RawMstEntry(key, compressed.Value, compressed.Tree);
            previous = key;
        }

        return new RawMstNode(left, entries);
    }

    private static CompressedMstEntry ReadCompressedMstEntry(CborReader reader)
    {
        if (reader.ReadStartMap() != 4)
        {
            throw Invalid("repository_structure_invalid");
        }

        int? prefixLength = null;
        byte[]? suffix = null;
        ATCid? value = null;
        ATCid? tree = null;
        bool treePresent = false;
        for (int index = 0; index < 4; index++)
        {
            string field = reader.ReadTextString();
            switch (field)
            {
                case "p" when prefixLength is null:
                    prefixLength = reader.ReadInt32();
                    break;
                case "k" when suffix is null:
                    suffix = reader.ReadByteString();
                    break;
                case "v" when value is null:
                    value = ReadCid(reader);
                    break;
                case "t" when !treePresent:
                    treePresent = true;
                    tree = ReadNullableCid(reader);
                    break;
                default:
                    throw Invalid("repository_structure_invalid");
            }
        }

        reader.ReadEndMap();
        if (prefixLength is null
            || suffix is null
            || suffix.Length > MaximumRepositoryPathBytes
            || value is null
            || !treePresent)
        {
            throw Invalid("repository_structure_invalid");
        }

        return new CompressedMstEntry(prefixLength.Value, suffix, value.Value, tree);
    }

    private static ATCid ReadCid(CborReader reader)
    {
        if (reader.PeekState() != CborReaderState.Tag || reader.ReadTag() != (CborTag)42)
        {
            throw Invalid("repository_structure_invalid");
        }

        byte[] bytes = reader.ReadByteString();
        if (bytes.Length != 37 || bytes[0] != 0)
        {
            throw Invalid("repository_structure_invalid");
        }

        ATCid cid = ATCid.FromBytes(bytes.AsSpan(1).ToArray());
        if (!IsDagCborCid(cid))
        {
            throw Invalid("repository_integrity_invalid");
        }

        return cid;
    }

    private static ATCid? ReadNullableCid(CborReader reader)
    {
        if (reader.PeekState() == CborReaderState.Null)
        {
            reader.ReadNull();
            return null;
        }

        return ReadCid(reader);
    }

    private static void WriteCid(CborWriter writer, ATCid cid)
    {
        byte[] cidBytes = cid.ToBytes();
        var taggedBytes = new byte[cidBytes.Length + 1];
        cidBytes.CopyTo(taggedBytes, 1);
        writer.WriteTag((CborTag)42);
        writer.WriteByteString(taggedBytes);
    }

    private static byte[] GetVerifiedBlock(
        IReadOnlyDictionary<string, byte[]> blocks,
        ATCid cid,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (!blocks.TryGetValue(cid.Value, out byte[]? bytes))
        {
            throw Invalid("repository_incomplete");
        }

        if (bytes.Length is 0 || bytes.Length > maximumBytes)
        {
            throw Invalid("repository_structure_invalid");
        }

        if (cid.Hash is not { Length: ATCid.Sha256HashLength } hash)
        {
            throw Invalid("repository_integrity_invalid");
        }

        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int offset = 0; offset < bytes.Length; offset += 64 * 1024)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(64 * 1024, bytes.Length - offset);
            hasher.AppendData(bytes, offset, length);
        }

        if (!CryptographicOperations.FixedTimeEquals(hasher.GetHashAndReset(), hash))
        {
            throw Invalid("repository_integrity_invalid");
        }

        return bytes;
    }

    private static bool IsDagCborCid(ATCid cid) => cid.IsAtProtoBlessedFormat
        && cid.Multicodec == DagCborMulticodec;

    private static bool IsValidRevision(string rev, DateTimeOffset now)
    {
        if (!TryDecodeTid(rev, out ulong value))
        {
            return false;
        }

        ulong revisionMicroseconds = value >> 10;
        long nowMicroseconds = checked((now.UtcDateTime.Ticks - DateTime.UnixEpoch.Ticks) / 10);
        long maximumMicroseconds = checked(nowMicroseconds
            + (long)(MaximumFutureRevisionSkew.TotalMilliseconds * 1_000));
        return maximumMicroseconds >= 0 && revisionMicroseconds <= (ulong)maximumMicroseconds;
    }

    internal static bool IsValidTid(string value) => TryDecodeTid(value, out _);

    private static bool TryDecodeTid(string value, out ulong decoded)
    {
        decoded = 0;
        if (value.Length != 13
            || TidAlphabet.IndexOf(value[0], StringComparison.Ordinal) is < 0 or > 15)
        {
            return false;
        }

        foreach (char character in value)
        {
            int digit = TidAlphabet.IndexOf(character, StringComparison.Ordinal);
            if (digit < 0)
            {
                decoded = 0;
                return false;
            }

            decoded = checked((decoded * 32) + (uint)digit);
        }

        return true;
    }

    private static bool TryReadRepositoryPath(
        byte[] path,
        out string collection,
        out string recordKey)
    {
        collection = string.Empty;
        recordKey = string.Empty;
        int separator = Array.IndexOf(path, (byte)'/');
        if (separator <= 0
            || separator == path.Length - 1
            || Array.IndexOf(path, (byte)'/', separator + 1) >= 0)
        {
            return false;
        }

        ReadOnlySpan<byte> collectionBytes = path.AsSpan(0, separator);
        ReadOnlySpan<byte> recordKeyBytes = path.AsSpan(separator + 1);
        if (!IsValidNsid(collectionBytes) || !IsValidRecordKey(recordKeyBytes))
        {
            return false;
        }

        collection = Encoding.ASCII.GetString(collectionBytes);
        recordKey = Encoding.ASCII.GetString(recordKeyBytes);
        return true;
    }

    private static bool IsValidNsid(ReadOnlySpan<byte> value)
    {
        if (value.Length is < 3 or > 317)
        {
            return false;
        }

        int segmentStart = 0;
        int segmentCount = 0;
        int finalSeparator = value.LastIndexOf((byte)'.');
        if (finalSeparator <= 0
            || finalSeparator >= value.Length - 1
            || finalSeparator > 253)
        {
            return false;
        }

        for (int index = 0; index <= finalSeparator; index++)
        {
            if (index != finalSeparator && value[index] != (byte)'.')
            {
                continue;
            }

            ReadOnlySpan<byte> segment = value[segmentStart..index];
            if (segment.Length is < 1 or > 63
                || segment[0] == (byte)'-'
                || segment[^1] == (byte)'-'
                || (segmentCount == 0 && IsAsciiDigit(segment[0])))
            {
                return false;
            }

            foreach (byte character in segment)
            {
                if (!IsAsciiLower(character) && !IsAsciiDigit(character) && character != (byte)'-')
                {
                    return false;
                }
            }

            segmentCount++;
            segmentStart = index + 1;
        }

        ReadOnlySpan<byte> name = value[(finalSeparator + 1)..];
        if (segmentCount < 2
            || name.Length is < 1 or > 63
            || !IsAsciiLetter(name[0]))
        {
            return false;
        }

        foreach (byte character in name)
        {
            if (!IsAsciiLetter(character) && !IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidRecordKey(ReadOnlySpan<byte> value)
    {
        if (value.Length is < 1 or > 512
            || value.SequenceEqual("."u8)
            || value.SequenceEqual(".."u8))
        {
            return false;
        }

        foreach (byte character in value)
        {
            if (!IsAsciiLetter(character)
                && !IsAsciiDigit(character)
                && character is not ((byte)'.' or (byte)'-' or (byte)'_' or (byte)':' or (byte)'~'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLower(byte value) => value is >= (byte)'a' and <= (byte)'z';

    private static bool IsAsciiDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    private static bool IsAsciiLetter(byte value) => IsAsciiLower(value)
        || value is >= (byte)'A' and <= (byte)'Z';

    private static int ComputeLayer(byte[] key)
    {
        byte[] hash = SHA256.HashData(key);
        int layer = 0;
        foreach (byte value in hash)
        {
            if (value < 64)
            {
                layer++;
            }

            if (value < 16)
            {
                layer++;
            }

            if (value < 4)
            {
                layer++;
            }

            if (value == 0)
            {
                layer++;
                continue;
            }

            break;
        }

        return layer;
    }

    private static int Compare(byte[] left, byte[] right) => left.AsSpan().SequenceCompareTo(right);

    private static int CommonPrefixLength(byte[] left, byte[] right)
    {
        int length = Math.Min(left.Length, right.Length);
        int index = 0;
        while (index < length && left[index] == right[index])
        {
            index++;
        }

        return index;
    }

    private static byte[] DecodeBase58Multikey(string encoded)
    {
        if (encoded.Length != 49 || encoded[0] != 'z' || encoded[1] == '1')
        {
            throw Invalid("identity_signing_key_invalid");
        }

        var decoded = new byte[35];
        for (int index = 1; index < encoded.Length; index++)
        {
            int digit = Base58Alphabet.IndexOf(encoded[index], StringComparison.Ordinal);
            if (digit < 0)
            {
                throw Invalid("identity_signing_key_invalid");
            }

            int carry = digit;
            for (int byteIndex = decoded.Length - 1; byteIndex >= 0; byteIndex--)
            {
                carry += decoded[byteIndex] * 58;
                decoded[byteIndex] = (byte)carry;
                carry >>= 8;
            }

            if (carry != 0)
            {
                throw Invalid("identity_signing_key_invalid");
            }
        }

        if (decoded[2] is not (0x02 or 0x03))
        {
            throw Invalid("identity_signing_key_invalid");
        }

        return decoded;
    }

    private static (byte[] X, byte[] Y) DecompressPoint(
        ReadOnlySpan<byte> compressedPoint,
        BigInteger prime,
        BigInteger a,
        BigInteger b)
    {
        if (compressedPoint.Length != 33 || compressedPoint[0] is not (0x02 or 0x03))
        {
            throw Invalid("identity_signing_key_invalid");
        }

        byte[] xBytes = compressedPoint[1..].ToArray();
        BigInteger x = new(xBytes, isUnsigned: true, isBigEndian: true);
        if (x >= prime)
        {
            throw Invalid("identity_signing_key_invalid");
        }

        BigInteger rightHandSide = PositiveModulo(
            (BigInteger.ModPow(x, 3, prime) + (a * x) + b),
            prime);
        BigInteger y = BigInteger.ModPow(rightHandSide, (prime + 1) >> 2, prime);
        if (BigInteger.ModPow(y, 2, prime) != rightHandSide)
        {
            throw Invalid("identity_signing_key_invalid");
        }

        bool expectsOdd = compressedPoint[0] == 0x03;
        if (!y.IsEven != expectsOdd)
        {
            y = prime - y;
        }

        byte[] encodedY = y.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (encodedY.Length > 32)
        {
            throw Invalid("identity_signing_key_invalid");
        }

        var yBytes = new byte[32];
        encodedY.CopyTo(yBytes, yBytes.Length - encodedY.Length);
        return (xBytes, yBytes);
    }

    private static bool IsLowS(byte[] signature, BigInteger order)
    {
        BigInteger r = new(signature.AsSpan(0, 32), isUnsigned: true, isBigEndian: true);
        BigInteger s = new(signature.AsSpan(32, 32), isUnsigned: true, isBigEndian: true);
        return r > BigInteger.Zero
            && r < order
            && s > BigInteger.Zero
            && s <= order >> 1;
    }

    private static BigInteger PositiveModulo(BigInteger value, BigInteger modulus)
    {
        BigInteger result = value % modulus;
        return result.Sign < 0 ? result + modulus : result;
    }

    private static BigInteger ParseUnsignedHex(string value) => BigInteger.Parse(
        $"0{value}",
        System.Globalization.NumberStyles.AllowHexSpecifier,
        System.Globalization.CultureInfo.InvariantCulture);

    private static AtprotoRepositorySnapshotValidationException Invalid(
        string failureCode,
        Exception? innerException = null) => new(failureCode, innerException);

    private sealed record ParsedCommit(ATCid Data, byte[] UnsignedBytes, byte[] Signature);

    private sealed record RawMstNode(ATCid? Left, RawMstEntry[] Entries);

    private sealed record RawMstEntry(byte[] Key, ATCid Value, ATCid? Tree);

    private sealed record CompressedMstEntry(
        int PrefixLength,
        byte[] Suffix,
        ATCid Value,
        ATCid? Tree);

    private sealed class VerificationState(
        IReadOnlyDictionary<string, byte[]> blocks,
        CancellationToken cancellationToken)
    {
        public IReadOnlyDictionary<string, byte[]> Blocks { get; } = blocks;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public HashSet<string> VisitedNodes { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Paths { get; } = new(StringComparer.Ordinal);

        public List<AtprotoVerifiedRepositoryRecord> Records { get; } = [];

        public int NodeCount { get; set; }

        public int RecordCount { get; set; }
    }
}

internal sealed record AtprotoVerifiedRepositorySnapshot(
    IReadOnlyList<AtprotoVerifiedRepositoryRecord> Records,
    byte[] UnsignedCommitBytes,
    byte[] Signature);

internal sealed record AtprotoVerifiedRepositoryRecord(
    string Collection,
    string RecordKey,
    ATCid Cid,
    byte[] Data);

internal enum AtprotoRepositorySigningCurve
{
    P256,
    K256
}

internal sealed record AtprotoRepositorySigningKey(
    AtprotoRepositorySigningCurve Curve,
    byte[] X,
    byte[] Y,
    BigInteger Order);

internal sealed class AtprotoRepositorySnapshotValidationException(
    string failureCode,
    Exception? innerException = null) : Exception(failureCode, innerException)
{
    public string FailureCode { get; } = failureCode;
}
