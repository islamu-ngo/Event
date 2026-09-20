namespace Explore.GeneratedContracts;

public static class GeneratedContractPublication
{
    /// <summary>Prepares a closed sibling file before replacing the published name.</summary>
    public static async Task PublishAsync(string destination, Func<string, Task> writeCandidate)
    {
        string fullPath = Path.GetFullPath(destination);
        string candidate = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await writeCandidate(candidate);
            if (!File.Exists(fullPath)
                || !File.ReadAllBytes(candidate).AsSpan().SequenceEqual(File.ReadAllBytes(fullPath)))
            {
                File.Move(candidate, fullPath, overwrite: true);
            }
        }
        finally
        {
            File.Delete(candidate);
        }
    }

    public static Task CopyAsync(string source, string destination) =>
        PublishAsync(destination, candidate =>
        {
            File.Copy(source, candidate);
            return Task.CompletedTask;
        });
}

public sealed record GeneratedContractFiles(string Schema, string Client, string MutablePolicy);

public static class GeneratedContractCapture
{
    public static GeneratedContractFiles Create(
        string schema, string client, string mutablePolicy, string directory)
    {
        // Read every input before changing a previous capture. Missing inputs are errors.
        byte[] schemaBytes = File.ReadAllBytes(schema);
        byte[] clientBytes = File.ReadAllBytes(client);
        byte[] policyBytes = File.ReadAllBytes(mutablePolicy);
        Directory.CreateDirectory(directory);
        var files = new GeneratedContractFiles(
            Path.Combine(directory, "openapi_islamu-event.json"),
            Path.Combine(directory, "EventApiTagClients.g.cs"),
            Path.Combine(directory, "mutable-generated-contracts.txt"));
        WriteIfChanged(files.Schema, schemaBytes);
        WriteIfChanged(files.Client, clientBytes);
        WriteIfChanged(files.MutablePolicy, policyBytes);
        return files;
    }

    private static void WriteIfChanged(string path, byte[] content)
    {
        if (!File.Exists(path) || !content.AsSpan().SequenceEqual(File.ReadAllBytes(path)))
            File.WriteAllBytes(path, content);
    }
}
