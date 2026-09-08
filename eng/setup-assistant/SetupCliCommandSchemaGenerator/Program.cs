using ISLAMU.Event.SetupAssistant.Cli;

if (args.Length != 1 || args[0] is not ("--write" or "--check"))
{
    Console.Error.WriteLine("Usage: setup-cli-command-schema-generator (--write|--check)");
    return 64;
}

try
{
    DirectoryInfo? current = new(Directory.GetCurrentDirectory());
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "Explore.slnx"))) current = current.Parent;
    if (current is null) throw new InvalidDataException("repository-root-not-found");
    string path = Path.Combine(current.FullName, SetupCliCommandSchemaMetadata.RelativePath.Replace('/', Path.DirectorySeparatorChar));
    byte[] expected = SetupCliCommandSchemaMetadata.GenerateSchema();
    if (args[0] == "--check")
    {
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(expected))
        {
            Console.Error.WriteLine("event-setup-command-schema-stale");
            return 1;
        }
        Console.WriteLine("event-setup-command-schema-current");
        return 0;
    }
    File.WriteAllBytes(path, expected);
    Console.WriteLine("event-setup-command-schema-generated");
    return 0;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
{
    Console.Error.WriteLine("event-setup-command-schema-failed");
    return 1;
}
