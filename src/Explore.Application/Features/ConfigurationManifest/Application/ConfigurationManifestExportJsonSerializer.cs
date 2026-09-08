namespace Explore.Application.Features.ConfigurationManifest.Application;

using System.Text.Json;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;

internal static class ConfigurationManifestExportJsonSerializer
{
    public static byte[] Serialize(ConfigurationManifestV1Alpha2 manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        using var stream = new BoundedExportStream(
            ConfigurationManifestExportContract.MaximumUtf8Bytes);
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions { Indented = true }))
        {
            WriteManifest(writer, manifest);
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void WriteManifest(Utf8JsonWriter writer, ConfigurationManifestV1Alpha2 manifest)
    {
        writer.WriteStartObject();
        writer.WriteString("$schema", manifest.Schema);
        writer.WriteString("apiVersion", manifest.ApiVersion);
        writer.WriteString("kind", manifest.Kind);
        writer.WritePropertyName("metadata");
        WriteMetadata(writer, manifest.Metadata);
        writer.WritePropertyName("spec");
        WriteSpec(writer, manifest.Spec);
        writer.WriteEndObject();
    }

    private static void WriteMetadata(
        Utf8JsonWriter writer,
        ConfigurationManifestMetadataV1Alpha2 metadata)
    {
        writer.WriteStartObject();
        writer.WriteString("name", metadata.Name);
        if (metadata.Export is { } export)
        {
            writer.WritePropertyName("export");
            writer.WriteStartObject();
            writer.WriteString("authorityScope", export.AuthorityScope);
            writer.WriteBoolean("effectiveValuesFlattened", export.EffectiveValuesFlattened);
            writer.WriteBoolean("sensitiveValuesOmitted", export.SensitiveValuesOmitted);
            writer.WritePropertyName("sovereignLockedFields");
            writer.WriteStartArray();
            foreach (string field in export.SovereignLockedFields.Order(StringComparer.Ordinal))
                writer.WriteStringValue(field);
            writer.WriteEndArray();
            writer.WriteBoolean("sovereignValuesOmitted", export.SovereignValuesOmitted);
            writer.WriteString("view", export.View);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteSpec(Utf8JsonWriter writer, ConfigurationManifestSpecV1Alpha2 spec)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("instance");
        WriteConfigurationScope(
            writer,
            spec.Instance.Settings,
            spec.Instance.Documents,
            spec.Instance.LegalDocuments);
        writer.WritePropertyName("tenants");
        writer.WriteStartArray();
        foreach (ConfigurationManifestTenantV1Alpha2 tenant in spec.Tenants
                     .OrderBy(value => value.Metadata.Name, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("metadata");
            writer.WriteStartObject();
            writer.WriteString("name", tenant.Metadata.Name);
            writer.WriteEndObject();
            writer.WritePropertyName("spec");
            writer.WriteStartObject();
            writer.WriteString("displayName", tenant.Spec.DisplayName);
            WriteConfigurationMembers(
                writer,
                tenant.Spec.Settings,
                tenant.Spec.Documents,
                tenant.Spec.LegalDocuments);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteConfigurationScope(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, JsonElement> settings,
        IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> documents,
        IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>
            legalDocuments)
    {
        writer.WriteStartObject();
        WriteConfigurationMembers(writer, settings, documents, legalDocuments);
        writer.WriteEndObject();
    }

    private static void WriteConfigurationMembers(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, JsonElement> settings,
        IReadOnlyDictionary<string, ConfigurationManifestDocumentV1Alpha2> documents,
        IReadOnlyDictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>
            legalDocuments)
    {
        writer.WritePropertyName("settings");
        writer.WriteStartObject();
        foreach ((string key, JsonElement value) in settings
                     .OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(key);
            WriteCanonicalJson(writer, value);
        }
        writer.WriteEndObject();

        writer.WritePropertyName("documents");
        writer.WriteStartObject();
        foreach ((string key, ConfigurationManifestDocumentV1Alpha2 document) in documents
                     .OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(key);
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", document.SchemaVersion);
            writer.WritePropertyName("payload");
            WriteCanonicalJson(writer, document.Payload);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();

        writer.WritePropertyName("legalDocuments");
        writer.WriteStartObject();
        foreach ((
                     string key,
                     ConfigurationManifestLegalDocumentV1Alpha2 legalDocument)
                 in legalDocuments.OrderBy(
                     entry => entry.Key,
                     StringComparer.Ordinal))
        {
            writer.WritePropertyName(key);
            JsonElement element = JsonSerializer.SerializeToElement(
                legalDocument,
                ConfigurationPortabilityJsonContext.Default
                    .ConfigurationManifestLegalDocumentV1Alpha2);
            WriteCanonicalJson(writer, element);
        }

        writer.WriteEndObject();
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            JsonProperty[] properties = element.EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToArray();
            if (properties.Select(property => property.Name)
                .Distinct(StringComparer.Ordinal).Count() != properties.Length)
            {
                throw new InvalidOperationException(
                    "A stored manifest document contains duplicate JSON properties.");
            }

            writer.WriteStartObject();
            foreach (JsonProperty property in properties)
            {
                writer.WritePropertyName(property.Name);
                WriteCanonicalJson(writer, property.Value);
            }
            writer.WriteEndObject();
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (JsonElement item in element.EnumerateArray())
                WriteCanonicalJson(writer, item);
            writer.WriteEndArray();
            return;
        }

        element.WriteTo(writer);
    }

    private sealed class BoundedExportStream(int maximumBytes) : Stream
    {
        private readonly MemoryStream _inner = new();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public byte[] ToArray() => _inner.ToArray();

        public override void Flush() => _inner.Flush();

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacity(count);
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacity(buffer.Length);
            _inner.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            EnsureCapacity(1);
            _inner.WriteByte(value);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }

        private void EnsureCapacity(int additionalBytes)
        {
            if (additionalBytes < 0 || _inner.Length + additionalBytes > maximumBytes)
                throw new ConfigurationManifestExportTooLargeException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
