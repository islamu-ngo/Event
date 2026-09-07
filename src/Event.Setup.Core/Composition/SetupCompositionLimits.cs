namespace ISLAMU.Event.Setup.Core.Composition;

public sealed record SetupCompositionLimits
{
    public const int DefaultAggregateSourceBytes = 4_194_304;
    public const int DefaultYamlDocuments = 1;
    public const int DefaultParserEvents = 131_072;
    public const int DefaultNormalizedNodes = 65_536;
    public const int DefaultNestingDepth = 32;
    public const int DefaultMappingEntries = 4_096;
    public const int DefaultSequenceEntries = 4_096;
    public const int DefaultScalarCharacters = 65_536;
    public const int DefaultAggregateScalarCharacters = 1_048_576;
    public const int DefaultDirectories = 256;
    public const int DefaultFiles = 1_024;
    public const int DefaultEntriesPerDirectory = 256;
    public const int DefaultRelativePathCharacters = 512;
    public const int DefaultPathDepth = 16;
    public const int DefaultPerFileBytes = 524_288;
    public const int DefaultAggregateDirectoryBytes = 4_194_304;
    public const int DefaultAggregateDirectoryNodes = 65_536;

    public static SetupCompositionLimits Default { get; } = new();

    public int AggregateSourceBytes => DefaultAggregateSourceBytes;
    public int YamlDocuments => DefaultYamlDocuments;
    public int ParserEvents => DefaultParserEvents;
    public int NormalizedNodes => DefaultNormalizedNodes;
    public int NestingDepth => DefaultNestingDepth;
    public int MappingEntries => DefaultMappingEntries;
    public int SequenceEntries => DefaultSequenceEntries;
    public int ScalarCharacters => DefaultScalarCharacters;
    public int AggregateScalarCharacters => DefaultAggregateScalarCharacters;
    public int Directories => DefaultDirectories;
    public int Files => DefaultFiles;
    public int EntriesPerDirectory => DefaultEntriesPerDirectory;
    public int RelativePathCharacters => DefaultRelativePathCharacters;
    public int PathDepth => DefaultPathDepth;
    public int PerFileBytes => DefaultPerFileBytes;
    public int AggregateDirectoryBytes => DefaultAggregateDirectoryBytes;
    public int AggregateDirectoryNodes => DefaultAggregateDirectoryNodes;

    internal void Validate()
    {
        if (AggregateSourceBytes <= 0 || YamlDocuments <= 0 || ParserEvents <= 0
            || NormalizedNodes <= 0 || NestingDepth <= 0 || MappingEntries <= 0
            || SequenceEntries <= 0 || ScalarCharacters <= 0
            || AggregateScalarCharacters <= 0 || Directories <= 0 || Files <= 0
            || EntriesPerDirectory <= 0 || RelativePathCharacters <= 0
            || PathDepth <= 0 || PerFileBytes <= 0 || AggregateDirectoryBytes <= 0
            || AggregateDirectoryNodes <= 0)
            throw new ArgumentOutOfRangeException(nameof(SetupCompositionLimits));
    }
}
