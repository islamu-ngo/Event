using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Explore.Domain.Services.EventResources;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Services;

public sealed class EventResourceDocumentPolicyTests
{
    private const string Word = EventResourceGovernancePolicy.WordDocumentMediaType;
    private const string Slides = EventResourceGovernancePolicy.PowerPointPresentationMediaType;
    private const long Maximum = 10_485_760;
    private const string TypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryNonMacroPackagesAreAccepted(bool slides)
    {
        using var content = Package(slides);
        await Assert.That(EventResourceDocumentPolicy.Matches(content, slides ? Slides : Word, Maximum, default)).IsTrue();
        await Assert.That(content.CanRead).IsTrue();
    }

    [Test]
    public async Task InternalRelativeRelationshipsAndImagePartsRemainUsable()
    {
        var parts = Parts(false);
        parts.Add(("docProps/thumbnail.gif", "GIF89a thumbnail"));
        parts.Add(("word/_rels/document.xml.rels", $"<Relationships xmlns=\"{RelationshipsNamespace}\"><Relationship Id=\"rId2\" Type=\"{OfficeRelationships}image\" Target=\"../docProps/thumbnail.gif\"/></Relationships>"));
        Replace(parts, "[Content_Types].xml", parts[0].Text.Replace("</Types>", "<Default Extension=\"gif\" ContentType=\"image/gif\"/></Types>", StringComparison.Ordinal));
        using var content = Create(parts, CompressionLevel.Optimal);
        await Assert.That(EventResourceDocumentPolicy.Matches(content, Word, Maximum, default)).IsTrue();
    }

    [Test]
    public async Task NonSeekableZipWritersDataDescriptorsAreValidated()
    {
        using var bytes = new MemoryStream();
        using (var output = new NonSeekableOutput(bytes))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var part in Parts(false))
            {
                using var entry = archive.CreateEntry(part.Name).Open();
                entry.Write(Encoding.UTF8.GetBytes(part.Text));
            }
        bytes.Position = 0;
        await Assert.That(EventResourceDocumentPolicy.Matches(bytes, Word, Maximum, default)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommentsCannotBecomeUnboundedArchiveMetadata(bool archiveComment)
    {
        using var content = Package(false);
        using (var archive = new ZipArchive(content, ZipArchiveMode.Update, leaveOpen: true))
        {
            if (archiveComment) archive.Comment = new string('x', 1025);
            else archive.Entries[0].Comment = new string('x', 1025);
        }
        content.Position = 0;
        await Assert.That(EventResourceDocumentPolicy.Matches(content, Word, Maximum, default)).IsFalse();
    }

    [Test]
    public async Task PdfIsOnlyASignatureCheckNotAScanVerdict()
    {
        using var content = new MemoryStream("%PDF-1.7\nnot parsed or malware scanned"u8.ToArray());
        await Assert.That(EventResourceDocumentPolicy.Matches(content, EventResourceGovernancePolicy.PdfMediaType, Maximum, default)).IsTrue();
    }

    [Test]
    [Arguments("missing-types")]
    [Arguments("missing-main")]
    [Arguments("missing-root-relationship")]
    [Arguments("wrong-main-type")]
    [Arguments("wrong-main-root")]
    [Arguments("macro-type")]
    [Arguments("macro-part")]
    [Arguments("embedded-package")]
    [Arguments("executable")]
    [Arguments("executable-as-image")]
    [Arguments("forged-entry-count")]
    [Arguments("multi-disk")]
    [Arguments("zip64")]
    [Arguments("symlink")]
    [Arguments("local-encryption-only")]
    [Arguments("declared-entry-budget")]
    [Arguments("declared-aggregate-budget")]
    [Arguments("active-x")]
    [Arguments("unknown-content-type")]
    [Arguments("untyped-part")]
    [Arguments("duplicate-part")]
    [Arguments("case-alias")]
    [Arguments("traversal")]
    [Arguments("backslash")]
    [Arguments("percent-path")]
    [Arguments("absolute-path")]
    [Arguments("empty-path-segment")]
    [Arguments("trailing-dot")]
    [Arguments("external-relationship")]
    [Arguments("embedded-relationship")]
    [Arguments("dangling-relationship")]
    [Arguments("dtd")]
    [Arguments("malformed-xml")]
    [Arguments("deep-xml")]
    [Arguments("duplicate-default")]
    [Arguments("duplicate-override")]
    [Arguments("encrypted")]
    [Arguments("unsupported-compression")]
    [Arguments("central-local-name-disagreement")]
    [Arguments("crc-corruption")]
    [Arguments("truncated")]
    [Arguments("trailing-data")]
    [Arguments("zip-only")]
    [Arguments("expansion-ratio")]
    [Arguments("entry-count")]
    [Arguments("xml-budget")]
    [Arguments("aggregate-budget")]
    public async Task UnsafeOrAmbiguousContainersFailClosed(string attack)
    {
        using var content = Attack(attack);
        long budget = attack == "aggregate-budget" ? 2_048 : Maximum;
        await Assert.That(EventResourceDocumentPolicy.Matches(content, Word, budget, default)).IsFalse();
        await Assert.That(content.CanRead).IsTrue();
    }

    [Test]
    public async Task AWordPackageCannotBeRelabeledAsAPresentation()
    {
        using var content = Package(false);
        await Assert.That(EventResourceDocumentPolicy.Matches(content, Slides, Maximum, default)).IsFalse();
    }

    [Test]
    public async Task CancellationIsNotReportedAsAnUnsafeDocument()
    {
        using var content = Package(false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Task.FromResult(
            EventResourceDocumentPolicy.Matches(content, Word, Maximum, cancellation.Token)));
    }

    private static MemoryStream Attack(string attack)
    {
        var parts = Parts(false);
        var main = "word/document.xml";
        switch (attack)
        {
            case "missing-types": parts.RemoveAll(x => x.Name == "[Content_Types].xml"); break;
            case "missing-main": parts.RemoveAll(x => x.Name == main); break;
            case "missing-root-relationship": parts.RemoveAll(x => x.Name == "_rels/.rels"); break;
            case "wrong-main-type": Replace(parts, "[Content_Types].xml", parts[0].Text.Replace("wordprocessingml.document.main", "presentationml.presentation.main", StringComparison.Ordinal)); break;
            case "wrong-main-root": Replace(parts, main, "<wrong/>"); break;
            case "macro-type": Replace(parts, "[Content_Types].xml", parts[0].Text.Replace("application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", "application/vnd.ms-word.document.macroEnabled.main+xml", StringComparison.Ordinal)); break;
            case "macro-part": parts.Add(("word/vbaProject.bin", "macro")); break;
            case "embedded-package": parts.Add(("word/embeddings/document.xml", "<document/>")); break;
            case "executable": parts.Add(("word/media/runner.exe", "MZ")); break;
            case "executable-as-image":
                parts.Add(("word/media/picture.png", "MZ executable payload"));
                Replace(parts, "[Content_Types].xml", parts[0].Text.Replace("</Types>", "<Default Extension=\"png\" ContentType=\"image/png\"/></Types>", StringComparison.Ordinal));
                break;
            case "active-x": parts.Add(("word/activeX/control.xml", "<control/>")); break;
            case "unknown-content-type": Replace(parts, "[Content_Types].xml", parts[0].Text.Replace("application/xml", "application/x-unknown", StringComparison.Ordinal)); break;
            case "untyped-part": parts.Add(("word/media/picture.bin", "opaque")); break;
            case "duplicate-part": parts.Add((main, "<duplicate/>")); break;
            case "case-alias": parts.Add(("WORD/document.xml", "<duplicate/>")); break;
            case "traversal": parts.Add(("word/../payload.xml", "<payload/>")); break;
            case "backslash": parts.Add(("word\\payload.xml", "<payload/>")); break;
            case "percent-path": parts.Add(("word/%64ocument.xml", "<payload/>")); break;
            case "absolute-path": parts.Add(("/word/payload.xml", "<payload/>")); break;
            case "empty-path-segment": parts.Add(("word//payload.xml", "<payload/>")); break;
            case "trailing-dot": parts.Add(("word/payload.xml.", "<payload/>")); break;
            case "external-relationship": Replace(parts, "_rels/.rels", RootRelationships("https://example.invalid/template", " TargetMode=\"External\"")); break;
            case "embedded-relationship": Replace(parts, "_rels/.rels", RootRelationships(main).Replace("/officeDocument\"", "/oleObject\"", StringComparison.Ordinal)); break;
            case "dangling-relationship": Replace(parts, "_rels/.rels", RootRelationships("word/missing.xml")); break;
            case "dtd": Replace(parts, main, "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///nonexistent'>]><x>&e;</x>"); break;
            case "malformed-xml": Replace(parts, main, "<document>"); break;
            case "deep-xml": Replace(parts, main, parts[2].Text.Replace("<w:body/>", string.Concat(Enumerable.Repeat("<x>", 70)) + string.Concat(Enumerable.Repeat("</x>", 70)), StringComparison.Ordinal)); break;
            case "duplicate-default": Replace(parts, "[Content_Types].xml", parts[0].Text.Replace("</Types>", "<Default Extension=\"xml\" ContentType=\"application/xml\"/></Types>", StringComparison.Ordinal)); break;
            case "duplicate-override": Replace(parts, "[Content_Types].xml", parts[0].Text.Replace("</Types>", $"<Override PartName=\"/{main}\" ContentType=\"application/xml\"/></Types>", StringComparison.Ordinal)); break;
            case "zip-only": parts.Clear(); parts.Add(("anything.xml", "<anything/>")); break;
            case "expansion-ratio": parts.Add(("word/large.xml", "<x>" + new string('x', 100_000) + "</x>")); break;
            case "entry-count": for (int i = 0; i < 512; i++) parts.Add(($"word/part{i}.xml", "<x/>")); break;
            case "xml-budget": parts.Add(("word/large.xml", "<x>" + new string('x', 1_048_576) + "</x>")); break;
            case "aggregate-budget": parts.Add(("word/large.xml", "<x>" + new string('x', 1_800) + "</x>")); break;
        }

        using var package = Create(parts, attack is "expansion-ratio" or "aggregate-budget" ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
        var bytes = package.ToArray();
        int central = Find(bytes, 0x02014b50);
        switch (attack)
        {
            case "encrypted": bytes[6] |= 1; bytes[central + 8] |= 1; break;
            case "local-encryption-only": bytes[6] |= 1; break;
            case "forged-entry-count": BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 12), 65535); break;
            case "multi-disk": bytes[^18] = 1; break;
            case "zip64": bytes[central + 6] = 45; break;
            case "symlink": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 38), 0xA0000000); break;
            case "declared-entry-budget": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 24), 8_388_609); break;
            case "declared-aggregate-budget": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 24), uint.MaxValue); break;
            case "unsupported-compression": bytes[8] = 99; bytes[central + 10] = 99; break;
            case "central-local-name-disagreement": bytes[30] = (byte)'X'; break;
            case "crc-corruption": bytes[central + 16] ^= 1; bytes[14] ^= 1; break;
            case "truncated": bytes = bytes[..^12]; break;
            case "trailing-data": bytes = [.. bytes, 0x42]; break;
        }
        return new MemoryStream(bytes);
    }

    private static MemoryStream Package(bool slides) => Create(Parts(slides), CompressionLevel.Optimal);

    private static List<(string Name, string Text)> Parts(bool slides)
    {
        string main = slides ? "ppt/presentation.xml" : "word/document.xml";
        string kind = slides ? "presentationml.presentation" : "wordprocessingml.document";
        string root = slides
            ? "<p:presentation xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"><p:sldIdLst/></p:presentation>"
            : "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body/></w:document>";
        return
        [
            ("[Content_Types].xml", $"<Types xmlns=\"{TypesNamespace}\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/{main}\" ContentType=\"application/vnd.openxmlformats-officedocument.{kind}.main+xml\"/></Types>"),
            ("_rels/.rels", RootRelationships(main)),
            (main, root)
        ];
    }

    private static string RootRelationships(string target, string mode = "") =>
        $"<Relationships xmlns=\"{RelationshipsNamespace}\"><Relationship Id=\"rId1\" Type=\"{OfficeRelationships}officeDocument\" Target=\"{target}\"{mode}/></Relationships>";

    private static void Replace(List<(string Name, string Text)> parts, string name, string text) => parts[parts.FindIndex(p => p.Name == name)] = (name, text);

    private static MemoryStream Create(List<(string Name, string Text)> parts, CompressionLevel compression)
    {
        var content = new MemoryStream();
        using (var archive = new ZipArchive(content, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var part in parts)
            {
                using var entry = archive.CreateEntry(part.Name, compression).Open();
                entry.Write(Encoding.UTF8.GetBytes(part.Text));
            }
        content.Position = 0;
        return content;
    }

    private sealed class NonSeekableOutput(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }

    private static int Find(byte[] bytes, uint signature)
    {
        for (int i = 0; i <= bytes.Length - 4; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) == signature) return i;
        throw new InvalidOperationException("ZIP fixture has no central directory.");
    }
}
