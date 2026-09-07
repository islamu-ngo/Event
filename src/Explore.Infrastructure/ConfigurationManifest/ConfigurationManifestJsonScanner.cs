namespace Explore.Infrastructure.ConfigurationManifest;

using System.Text;
using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Ingestion;

internal static class ConfigurationManifestJsonScanner
{
    private const int MaximumDepth = 16;
    private const int MaximumTokens = 262_144;
    private const int MaximumObjectProperties = 512;
    private const int MaximumArrayEntries = 256;
    private const int MaximumPropertyNameBytes = 256;
    private const int MaximumStringBytes = 65_536;
    private const int MaximumNumberBytes = 128;

    public static void Validate(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(
            bytes,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumDepth
            });
        var containers = new Stack<ContainerFrame>();
        bool rootStarted = false;
        bool rootCompleted = false;
        int tokenCount = 0;

        while (reader.Read())
        {
            tokenCount++;
            if (tokenCount > MaximumTokens)
                throw LimitExceeded();
            if (rootCompleted)
                throw new JsonException("Trailing JSON content is not allowed.");

            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    StartValue(containers);
                    if (!rootStarted)
                        rootStarted = true;
                    containers.Push(ContainerFrame.Object());
                    break;

                case JsonTokenType.StartArray:
                    if (!rootStarted)
                        throw new JsonException(
                            "The configuration manifest root must be an object.");
                    StartValue(containers);
                    containers.Push(ContainerFrame.Array());
                    break;

                case JsonTokenType.EndObject:
                    EndContainer(containers, isObject: true);
                    rootCompleted = containers.Count == 0;
                    break;

                case JsonTokenType.EndArray:
                    EndContainer(containers, isObject: false);
                    rootCompleted = containers.Count == 0;
                    break;

                case JsonTokenType.PropertyName:
                    ValidateProperty(reader, containers);
                    break;

                case JsonTokenType.String:
                    string stringValue = reader.GetString()
                        ?? throw new JsonException("A JSON string value was null.");
                    if (Encoding.UTF8.GetByteCount(stringValue) > MaximumStringBytes)
                        throw LimitExceeded();
                    StartValue(containers);
                    break;

                case JsonTokenType.Number:
                    if (TokenByteLength(reader) > MaximumNumberBytes)
                        throw LimitExceeded();
                    StartValue(containers);
                    break;

                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                    StartValue(containers);
                    break;

                default:
                    throw new JsonException(
                        "The configuration manifest contains an unsupported JSON token.");
            }
        }

        if (!rootStarted || !rootCompleted)
            throw new JsonException(
                "The configuration manifest must contain one complete root object.");
    }

    private static void ValidateProperty(
        Utf8JsonReader reader,
        Stack<ContainerFrame> containers)
    {
        if (containers.Count == 0 || !containers.Peek().IsObject)
            throw new JsonException("A property appeared outside an object.");

        string propertyName = reader.GetString()
            ?? throw new JsonException("A JSON property name was null.");
        if (Encoding.UTF8.GetByteCount(propertyName) > MaximumPropertyNameBytes)
            throw LimitExceeded();

        ContainerFrame frame = containers.Peek();
        frame.EntryCount++;
        if (frame.EntryCount > MaximumObjectProperties)
            throw LimitExceeded();
        if (!frame.PropertyNames!.Add(propertyName))
        {
            throw new ConfigurationManifestIngestionException(
                ConfigurationManifestIngestionFailureCodes.DuplicateProperty,
                "The configuration manifest contains a duplicate JSON property.");
        }
    }

    private static void StartValue(Stack<ContainerFrame> containers)
    {
        if (containers.Count == 0)
            return;

        ContainerFrame frame = containers.Peek();
        if (frame.IsObject)
            return;

        frame.EntryCount++;
        if (frame.EntryCount > MaximumArrayEntries)
            throw LimitExceeded();
    }

    private static void EndContainer(
        Stack<ContainerFrame> containers,
        bool isObject)
    {
        if (containers.Count == 0 || containers.Peek().IsObject != isObject)
            throw new JsonException(
                "The configuration manifest has mismatched JSON containers.");

        containers.Pop();
    }

    private static int TokenByteLength(Utf8JsonReader reader) =>
        checked((int)(reader.HasValueSequence
            ? reader.ValueSequence.Length
            : reader.ValueSpan.Length));

    private static ConfigurationManifestIngestionException LimitExceeded() =>
        new(
            ConfigurationManifestIngestionFailureCodes.JsonLimitExceeded,
            "The configuration manifest exceeds a JSON structural limit.");

    private sealed class ContainerFrame
    {
        private ContainerFrame(bool isObject)
        {
            IsObject = isObject;
            PropertyNames = isObject
                ? new HashSet<string>(StringComparer.Ordinal)
                : null;
        }

        public bool IsObject { get; }
        public HashSet<string>? PropertyNames { get; }
        public int EntryCount { get; set; }

        public static ContainerFrame Object() => new(isObject: true);
        public static ContainerFrame Array() => new(isObject: false);
    }
}
