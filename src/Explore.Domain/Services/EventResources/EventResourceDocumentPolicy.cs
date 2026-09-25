using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Explore.Domain.ValueObjects;

namespace Explore.Domain.Services.EventResources;

/// <summary>
/// Conservative, deterministic container validation; this is not malware scanning.
/// Input must be a stable seekable snapshot, owned by the caller. PDF is signature-only.
/// OOXML uses classic single-disk ZIP (stored/deflate), at most 512 entries, 512-byte ASCII
/// part names, 4 KiB extra fields, 1 KiB entry/archive comments, a 100:1 per-entry
/// expansion ceiling, and no ZIP64.
/// Expanded total is capped at min(maximumBytes, 64 MiB), each part at min(maximumBytes,
/// 8 MiB), each XML part at min(maximumBytes, 1 MiB), and XML depth at 64.
/// Unknown/binary/active/embedded parts and all external relationships fail closed.
/// These deliberately narrow v1 limits can reject otherwise legitimate Office packages.
/// Only caller-supplied bytes are read: no extraction or external resource resolution.
/// XML may be read twice (CRC then parsing); both passes have the same hard byte ceiling
/// and fixed-size buffers.
/// </summary>
public static class EventResourceDocumentPolicy
{
    private const int MaximumEntries = 512;
    private const string ContentTypes = "[Content_Types].xml";
    private const string TypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string MainTypePrefix = "application/vnd.openxmlformats-officedocument.";
    private static readonly HashSet<string> XmlTypes = new(StringComparer.Ordinal)
    {
        "application/xml", "application/vnd.openxmlformats-package.relationships+xml",
        "application/vnd.openxmlformats-package.core-properties+xml",
        MainTypePrefix + "extended-properties+xml", MainTypePrefix + "custom-properties+xml",
        MainTypePrefix + "wordprocessingml.document.main+xml", MainTypePrefix + "wordprocessingml.styles+xml",
        MainTypePrefix + "wordprocessingml.settings+xml", MainTypePrefix + "wordprocessingml.webSettings+xml",
        MainTypePrefix + "wordprocessingml.fontTable+xml", MainTypePrefix + "wordprocessingml.numbering+xml",
        MainTypePrefix + "wordprocessingml.header+xml", MainTypePrefix + "wordprocessingml.footer+xml",
        MainTypePrefix + "wordprocessingml.footnotes+xml", MainTypePrefix + "wordprocessingml.endnotes+xml",
        MainTypePrefix + "wordprocessingml.comments+xml", MainTypePrefix + "theme+xml",
        MainTypePrefix + "presentationml.presentation.main+xml", MainTypePrefix + "presentationml.slide+xml",
        MainTypePrefix + "presentationml.slideLayout+xml", MainTypePrefix + "presentationml.slideMaster+xml",
        MainTypePrefix + "presentationml.notesSlide+xml", MainTypePrefix + "presentationml.notesMaster+xml",
        MainTypePrefix + "presentationml.handoutMaster+xml", MainTypePrefix + "presentationml.presProps+xml",
        MainTypePrefix + "presentationml.viewProps+xml", MainTypePrefix + "presentationml.tableStyles+xml",
        MainTypePrefix + "drawing+xml", MainTypePrefix + "drawingml.chart+xml",
        MainTypePrefix + "drawingml.chartShapes+xml",
        MainTypePrefix + "drawingml.diagramData+xml", MainTypePrefix + "drawingml.diagramLayout+xml",
        MainTypePrefix + "drawingml.diagramStyle+xml", MainTypePrefix + "drawingml.diagramColors+xml"
    };
    private static readonly HashSet<string> RelationshipKinds = new(StringComparer.Ordinal)
    {
        "officeDocument", "extended-properties", "custom-properties", "styles", "settings", "webSettings",
        "fontTable", "numbering", "header", "footer", "footnotes", "endnotes", "comments", "theme", "image",
        "slide", "slideLayout", "slideMaster", "notesSlide", "notesMaster", "handoutMaster", "presProps",
        "viewProps", "tableStyles", "chart", "chartUserShapes", "diagramData", "diagramLayout", "diagramQuickStyle", "diagramColors"
    };

    public static bool Matches(Stream content, string contentType, long maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();
        if (!content.CanSeek || maximumBytes <= 0 || content.Length <= 0 || content.Length > maximumBytes)
            return false;
        content.Position = 0;
        try
        {
            if (contentType == EventResourceGovernancePolicy.PdfMediaType)
            {
                Span<byte> signature = stackalloc byte[5];
                content.ReadExactly(signature);
                return signature.SequenceEqual("%PDF-"u8);
            }
            bool slides = contentType == EventResourceGovernancePolicy.PowerPointPresentationMediaType;
            if (!slides && contentType != EventResourceGovernancePolicy.WordDocumentMediaType)
                return false;

            var parts = ReadDirectory(content, maximumBytes, cancellationToken);
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count != parts.Count) return false;
            var entries = archive.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
            var buffer = new byte[65_536];
            foreach (var part in parts.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entries.TryGetValue(part.Name, out var entry) || entry.Length != part.Length || entry.CompressedLength != part.Compressed)
                    return false;
                using var data = entry.Open();
                long read = 0;
                uint crc = uint.MaxValue;
                int count;
                while ((count = data.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    read += count;
                    if (read > part.Length) return false;
                    foreach (byte value in buffer.AsSpan(0, count))
                    {
                        crc ^= value;
                        for (int bit = 0; bit < 8; bit++)
                            crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
                    }
                }
                if (read != part.Length || ~crc != part.Crc) return false;
            }
            return ValidatePackage(entries, slides, maximumBytes, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or XmlException or DecoderFallbackException)
        {
            return false;
        }
        finally
        {
            content.Position = 0;
        }
    }

    private static Dictionary<string, Part> ReadDirectory(Stream content, long maximumBytes, CancellationToken cancellationToken)
    {
        // Locate only an EOCD whose declared comment ends at the exact inspected length.
        int tailLength = (int)Math.Min(content.Length, 65_557);
        byte[] tail = new byte[tailLength];
        content.Position = content.Length - tailLength;
        content.ReadExactly(tail);
        int end = tail.Length - 22;
        while (end >= 0 && (U32(tail, end) != 0x06054b50 || end + 22 + U16(tail, end + 20) != tail.Length)) end--;
        Require(end >= 0);
        Require(U16(tail, end + 4) == 0 && U16(tail, end + 6) == 0 && U16(tail, end + 20) <= 1024);
        int count = U16(tail, end + 10);
        Require(count > 0 && count <= MaximumEntries && count == U16(tail, end + 8));
        long directorySize = U32(tail, end + 12);
        long directoryOffset = U32(tail, end + 16);
        long endOffset = content.Length - tail.Length + end;
        Require(directoryOffset + directorySize == endOffset);
        var parts = new Dictionary<string, Part>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        content.Position = directoryOffset;
        byte[] header = new byte[46];
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            content.ReadExactly(header);
            Require(U32(header, 0) == 0x02014b50 && U16(header, 6) <= 20);
            ushort flags = U16(header, 8);
            ushort method = U16(header, 10);
            Require((flags & ~0x080E) == 0 && method is 0 or 8 && (method != 0 || (flags & 6) == 0));
            uint compressed = U32(header, 20), length = U32(header, 24), offset = U32(header, 42);
            Require(compressed <= maximumBytes && length <= Math.Min(maximumBytes, 8L * 1024 * 1024));
            Require(length <= Math.Max(1L, compressed) * 100 && (method != 0 || length == compressed));
            expanded += length;
            Require(expanded <= Math.Min(maximumBytes, 64L * 1024 * 1024));
            int nameLength = U16(header, 28), extraLength = U16(header, 30), commentLength = U16(header, 32);
            Require(nameLength is > 0 and <= 512 && extraLength <= 4096 && commentLength <= 1024 && U16(header, 34) == 0);
            // Reject directory and Unix special-file attributes; no symlink semantics are accepted.
            uint attributes = U32(header, 38);
            Require((attributes & 0x10) == 0 && ((attributes >> 16) & 0xF000) is 0 or 0x8000);
            byte[] nameBytes = new byte[nameLength];
            content.ReadExactly(nameBytes);
            Require(nameBytes.All(b => b is >= 0x21 and < 0x7F));
            string name = Encoding.ASCII.GetString(nameBytes);
            Require(IsPartName(name) && IsPassivePart(name));
            ReadExtra(content, extraLength);
            content.Seek(commentLength, SeekOrigin.Current);
            Require(content.Position <= endOffset);
            Require(parts.TryAdd(name, new Part(name, flags, method, U32(header, 16), compressed, length, offset)));
        }
        Require(content.Position == endOffset);
        long nextOffset = 0;
        byte[] local = new byte[30];
        foreach (var part in parts.Values.OrderBy(p => p.Offset))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(part.Offset == nextOffset);
            content.Position = part.Offset;
            content.ReadExactly(local);
            Require(U32(local, 0) == 0x04034b50 && U16(local, 4) <= 20 && U16(local, 6) == part.Flags && U16(local, 8) == part.Method);
            bool descriptor = (part.Flags & 8) != 0;
            Require(descriptor
                ? (U32(local, 14) == 0 || U32(local, 14) == part.Crc) && (U32(local, 18) == 0 || U32(local, 18) == part.Compressed) && (U32(local, 22) == 0 || U32(local, 22) == part.Length)
                : U32(local, 14) == part.Crc && U32(local, 18) == part.Compressed && U32(local, 22) == part.Length);
            int nameLength = U16(local, 26), extraLength = U16(local, 28);
            Require(nameLength == part.Name.Length && extraLength <= 4096);
            byte[] name = new byte[nameLength];
            content.ReadExactly(name);
            Require(name.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(part.Name)));
            ReadExtra(content, extraLength);
            nextOffset = content.Position + part.Compressed;
            Require(nextOffset <= directoryOffset);
            if (descriptor)
            {
                content.Position = nextOffset;
                byte[] values = new byte[12];
                content.ReadExactly(values.AsSpan(0, 4));
                if (U32(values, 0) == 0x08074b50) content.ReadExactly(values.AsSpan(0, 4));
                content.ReadExactly(values.AsSpan(4, 8));
                Require(U32(values, 0) == part.Crc && U32(values, 4) == part.Compressed && U32(values, 8) == part.Length);
                nextOffset = content.Position;
            }
        }
        Require(nextOffset == directoryOffset);
        return parts;
    }

    private static void ReadExtra(Stream content, int length)
    {
        byte[] extra = new byte[length];
        content.ReadExactly(extra);
        int offset = 0;
        while (offset < extra.Length)
        {
            Require(offset + 4 <= extra.Length);
            ushort kind = U16(extra, offset), size = U16(extra, offset + 2);
            // ZIP64, Unicode path aliases and encryption metadata are outside this subset.
            Require(kind is not (0x0001 or 0x7075 or 0x0017 or 0x9901));
            offset += 4 + size;
            Require(offset <= extra.Length);
        }
    }

    private static bool ValidatePackage(Dictionary<string, ZipArchiveEntry> entries, bool slides, long maximumBytes, CancellationToken token)
    {
        string main = slides ? "ppt/presentation.xml" : "word/document.xml";
        string mainType = MainTypePrefix + (slides ? "presentationml.presentation.main+xml" : "wordprocessingml.document.main+xml");
        if (!entries.TryGetValue(ContentTypes, out var types) || !entries.ContainsKey(main) || !entries.ContainsKey("_rels/.rels")) return false;
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ReadXml(types, maximumBytes, reader =>
        {
            if (reader.Depth == 0) Require(reader.LocalName == "Types" && reader.NamespaceURI == TypesNamespace);
            else
            {
                Require(reader.Depth == 1 && reader.NamespaceURI == TypesNamespace);
                string mime = RequiredAttribute(reader, "ContentType");
                Require(IsAllowedType(mime));
                if (reader.LocalName == "Default")
                {
                    string extension = RequiredAttribute(reader, "Extension");
                    Require(extension.All(char.IsAsciiLetterOrDigit) && defaults.TryAdd(extension, mime));
                }
                else
                {
                    Require(reader.LocalName == "Override");
                    string name = RequiredAttribute(reader, "PartName");
                    Require(name.StartsWith('/') && IsPartName(name[1..]) && entries.ContainsKey(name[1..]) && overrides.TryAdd(name[1..], mime));
                }
            }
        }, token);
        if (!overrides.TryGetValue(main, out string? actualMainType) || actualMainType != mainType) return false;
        int mainRelationships = 0;
        foreach (var entry in entries.Values)
        {
            token.ThrowIfCancellationRequested();
            if (entry.FullName == ContentTypes) continue;
            string extension = Path.GetExtension(entry.FullName)[1..];
            if (!overrides.TryGetValue(entry.FullName, out string? mime) && !defaults.TryGetValue(extension, out mime)) return false;
            bool xml = extension is "xml" or "rels";
            if (xml != XmlTypes.Contains(mime) || !MatchesPartType(extension, mime)) return false;
            if (!xml)
            {
                if (!MatchesImageSignature(entry, mime)) return false;
                continue;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            ReadXml(entry, maximumBytes, reader =>
            {
                if (entry.FullName == main && reader.Depth == 0)
                    Require(reader.LocalName == (slides ? "presentation" : "document") && reader.NamespaceURI == (slides ? "http://schemas.openxmlformats.org/presentationml/2006/main" : "http://schemas.openxmlformats.org/wordprocessingml/2006/main"));
                if (extension != "rels") return;
                Require(reader.NamespaceURI == RelationshipsNamespace);
                if (reader.Depth == 0) { Require(reader.LocalName == "Relationships"); return; }
                Require(reader.Depth == 1 && reader.LocalName == "Relationship" && ids.Add(RequiredAttribute(reader, "Id")));
                string? mode = reader.GetAttribute("TargetMode");
                Require(mode is null or "Internal");
                string kind = RequiredAttribute(reader, "Type");
                Require(kind == "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"
                    || (kind.StartsWith(OfficeRelationships, StringComparison.Ordinal) && RelationshipKinds.Contains(kind[OfficeRelationships.Length..])));
                string target = ResolveTarget(entry.FullName, RequiredAttribute(reader, "Target"));
                Require(entries.ContainsKey(target));
                if (kind == OfficeRelationships + "officeDocument")
                {
                    Require(entry.FullName == "_rels/.rels" && target == main);
                    mainRelationships++;
                }
            }, token);
        }
        return mainRelationships == 1;
    }

    private static bool MatchesImageSignature(ZipArchiveEntry entry, string mime)
    {
        using var data = entry.Open();
        Span<byte> signature = stackalloc byte[8];
        int count = data.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);
        ReadOnlySpan<byte> prefix = signature[..count];
        return mime switch
        {
            "image/png" => prefix.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => prefix.StartsWith(new byte[] { 255, 216, 255 }),
            "image/gif" => prefix.StartsWith("GIF87a"u8) || prefix.StartsWith("GIF89a"u8),
            "image/bmp" => prefix.StartsWith("BM"u8),
            "image/tiff" => prefix.StartsWith("II\u002a\0"u8) || prefix.StartsWith("MM\0\u002a"u8),
            _ => false
        };
    }

    private static void ReadXml(ZipArchiveEntry entry, long maximumBytes, Action<XmlReader> element, CancellationToken token)
    {
        long budget = Math.Min(maximumBytes, 1_048_576);
        Require(entry.Length > 0 && entry.Length <= budget);
        using var data = entry.Open();
        using var reader = XmlReader.Create(data, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = budget, MaxCharactersFromEntities = 0,
            IgnoreComments = true, IgnoreProcessingInstructions = true
        });
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            Require(reader.Depth <= 64);
            if (reader.NodeType == XmlNodeType.Element) element(reader);
        }
    }

    private static string ResolveTarget(string relationshipName, string target)
    {
        Require(target.Length is > 0 and <= 512
            && !target.Contains('\\', StringComparison.Ordinal) && !target.Contains('%', StringComparison.Ordinal)
            && !target.Contains(':', StringComparison.Ordinal) && !target.Contains('?', StringComparison.Ordinal)
            && !target.Contains('#', StringComparison.Ordinal) && !target.StartsWith('/'));
        var segments = new List<string>();
        if (relationshipName != "_rels/.rels")
        {
            int marker = relationshipName.LastIndexOf("/_rels/", StringComparison.Ordinal);
            Require(marker >= 0 && relationshipName.EndsWith(".rels", StringComparison.Ordinal));
            segments.AddRange(relationshipName[..marker].Split('/'));
        }
        foreach (string segment in target.Split('/'))
        {
            if (segment == "..") { Require(segments.Count > 0); segments.RemoveAt(segments.Count - 1); }
            else { Require(segment != "." && segment.Length > 0); segments.Add(segment); }
        }
        string resolved = string.Join('/', segments);
        Require(IsPartName(resolved));
        return resolved;
    }

    private static bool IsPartName(string name) => name.Length is > 0 and <= 512
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '.' or '[' or ']')
        && name.Split('/').All(s => s.Length > 0 && s is not ("." or "..") && !s.EndsWith('.'));

    private static bool IsPassivePart(string name)
    {
        if (name == ContentTypes) return true;
        if (name.Split('/').Any(s => s.Contains("vba", StringComparison.OrdinalIgnoreCase)
            || s.Equals("embeddings", StringComparison.OrdinalIgnoreCase) || s.Equals("activeX", StringComparison.OrdinalIgnoreCase)
            || s.Equals("customUI", StringComparison.OrdinalIgnoreCase) || s.Equals("controls", StringComparison.OrdinalIgnoreCase))) return false;
        return Path.GetExtension(name) is ".xml" or ".rels" or ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff";
    }

    private static bool IsAllowedType(string mime) => XmlTypes.Contains(mime) || mime is "image/png" or "image/jpeg" or "image/gif" or "image/bmp" or "image/tiff";

    private static bool MatchesPartType(string extension, string mime) => extension switch
    {
        "rels" => mime == "application/vnd.openxmlformats-package.relationships+xml",
        "xml" => XmlTypes.Contains(mime) && mime != "application/vnd.openxmlformats-package.relationships+xml",
        "png" => mime == "image/png", "jpg" or "jpeg" => mime == "image/jpeg",
        "gif" => mime == "image/gif", "bmp" => mime == "image/bmp", "tif" or "tiff" => mime == "image/tiff", _ => false
    };

    private static string RequiredAttribute(XmlReader reader, string name)
    {
        string? value = reader.GetAttribute(name);
        Require(!string.IsNullOrEmpty(value));
        return value!;
    }

    private static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static void Require(bool condition) { if (!condition) throw new InvalidDataException("Document container is not supported."); }
    private sealed record Part(string Name, ushort Flags, ushort Method, uint Crc, uint Compressed, uint Length, uint Offset);
}
