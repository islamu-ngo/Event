#:property RestorePackagesWithLockFile=false
#:property PublishAot=false
#:project ../../src/Explore.Infrastructure/Explore.Infrastructure.csproj

using System.Security.Cryptography;
using Explore.Application.Authorization;
using Explore.Infrastructure.Services;

// Inputs are deployment-owned expectations; no expected field is inferred from the signed receipt.
if (args.Length != 7)
    return 1;

try
{
    var receiptPath = args[0];
    var signaturePath = args[1];
    var keyPath = args[2];
    var revision = args[3];
    var environment = args[4];
    var pdpEndpoint = args[5];
    var packageRoot = Path.GetFullPath(args[6]);
    if (new FileInfo(receiptPath).Length is <= 0 or > 8192
        || new FileInfo(signaturePath).Length is <= 0 or > 1024
        || new FileInfo(keyPath).Length is <= 0 or > 16384)
        return 1;

    var artifacts = new List<PolicyPackageArtifact>();
    foreach (var file in Directory.EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal))
    {
        var logicalId = Path.GetRelativePath(packageRoot, file).Replace(Path.DirectorySeparatorChar, '/');
        var isSchema = logicalId.StartsWith("_schemas/", StringComparison.Ordinal)
            && logicalId.Count(character => character == '/') == 1
            && Path.GetExtension(file) == ".json";
        var isPolicy = !logicalId.Contains('/') && Path.GetExtension(file) is ".json" or ".yaml" or ".yml";
        if (!isSchema && !isPolicy)
            continue;
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            return 1;
        var bytes = await File.ReadAllBytesAsync(file);
        artifacts.Add(new PolicyPackageArtifact(logicalId,
            isSchema ? PolicyArtifactKind.Schema : PolicyArtifactKind.Policy,
            Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.LongLength,
            new Dictionary<string, string>()));
    }

    if (!artifacts.Any(artifact => artifact.Kind == PolicyArtifactKind.Schema)
        || !artifacts.Any(artifact => artifact.Kind == PolicyArtifactKind.Policy))
        return 1;
    var hash = CerbosPolicyPackageService.ComputeManifestHash(
        artifacts.OrderBy(artifact => artifact.LogicalId, StringComparer.Ordinal).ToArray());
    var document = await File.ReadAllBytesAsync(receiptPath);
    var signature = await File.ReadAllBytesAsync(signaturePath);
    var publicKey = await File.ReadAllTextAsync(keyPath);
    var verified = new CerbosPublicationReceiptVerifier().Verify(
        document, signature, publicKey,
        revision, hash, environment, pdpEndpoint, DateTimeOffset.UtcNow);
    Console.WriteLine(verified ? "cerbos_publication_verified" : "cerbos_publication_rejected");
    return verified ? 0 : 1;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine("cerbos_publication_input_unavailable");
    return 1;
}
