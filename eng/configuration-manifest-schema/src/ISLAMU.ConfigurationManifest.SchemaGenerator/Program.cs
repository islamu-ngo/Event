namespace ISLAMU.ConfigurationManifest.SchemaGenerator;

internal static class Program
{
    private const int UsageError = 64;

    public static int Main(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine(
                "Usage: configuration-manifest-schema (--write|--check) (manifest|tenant-package) <schema-path>");
            return 0;
        }

        if (args.Length != 3
            || args[0] is not ("--write" or "--check")
            || args[1] is not ("manifest" or "tenant-package"))
        {
            Console.Error.WriteLine(
                "Usage: configuration-manifest-schema (--write|--check) (manifest|tenant-package) <schema-path>");
            return UsageError;
        }

        string artifactPath = Path.GetFullPath(args[2]);
        byte[] generated;
        try
        {
            generated = args[1] == "manifest"
                ? ConfigurationManifestJsonSchemaGenerator
                    .GenerateConfigurationManifest()
                : ConfigurationManifestJsonSchemaGenerator
                    .GenerateTenantConfigurationPackage();
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }

        if (args[0] == "--check")
        {
            if (!File.Exists(artifactPath))
            {
                Console.Error.WriteLine("The configuration-manifest schema artifact is missing.");
                return 1;
            }

            byte[] existing = File.ReadAllBytes(artifactPath);
            if (!existing.AsSpan().SequenceEqual(generated))
            {
                Console.Error.WriteLine("The configuration-manifest schema artifact is stale.");
                return 1;
            }

            Console.WriteLine("Configuration-manifest schema artifact is current.");
            return 0;
        }

        string? directory = Path.GetDirectoryName(artifactPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            Console.Error.WriteLine("The schema output directory does not exist.");
            return 1;
        }

        File.WriteAllBytes(artifactPath, generated);
        Console.WriteLine("Configuration-manifest schema artifact generated.");
        return 0;
    }
}
