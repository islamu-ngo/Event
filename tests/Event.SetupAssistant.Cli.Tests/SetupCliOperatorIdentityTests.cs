namespace ISLAMU.SetupAssistant.Cli.Tests;

using System.Text;
using System.Text.Json;
using ISLAMU.Event.SetupAssistant.Cli;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class SetupCliOperatorIdentityTests
{
    [Test]
    public async Task ExportYamlThenImportCreatesVerifiedRequestWithoutPublicIdentityDisclosure()
    {
        var io = new MemoryIo();
        io.Files["identity.json"] = Identity();
        SetupCliExitCode exported = Run(io, "portability", "export-operator-identity",
            "--input", "identity.json", "--output", "identity.yaml", "--format", "yaml", "--machine");
        await Assert.That(exported).IsEqualTo(SetupCliExitCode.Success);
        await Assert.That(SetupCliMachineContractVerifier.Validate(io.StandardOutput)).IsEmpty();
        OperatorIdentityManifest manifest = OperatorIdentityManifestCodec.Read(io.Files["identity.yaml"]);
        await Assert.That(Encoding.UTF8.GetString(io.StandardOutput).Contains("Independent", StringComparison.Ordinal)).IsFalse();
        io.Files["api-export.json"] = OperatorIdentityManifestJson.Serialize(manifest);
        await Assert.That(Run(io, "portability", "export-operator-identity", "--input", "api-export.json",
            "--output", "api-export.yaml", "--format", "yaml", "--machine")).IsEqualTo(SetupCliExitCode.Success);
        await Assert.That(io.Files["api-export.yaml"].SequenceEqual(io.Files["identity.yaml"])).IsTrue();

        SetupCliExitCode imported = Run(io, "portability", "import-operator-identity",
            "--input", "identity.yaml", "--output", "import.json",
            "--expected-revision", manifest.RevisionHash, "--machine");
        await Assert.That(imported).IsEqualTo(SetupCliExitCode.Success);
        await Assert.That(SetupCliMachineContractVerifier.Validate(io.StandardOutput)).IsEmpty();
        using JsonDocument request = JsonDocument.Parse(io.Files["import.json"]);
        await Assert.That(request.RootElement.GetProperty("expectedRevisionHash").GetString()).IsEqualTo(manifest.RevisionHash);
        OperatorIdentityManifest verified = OperatorIdentityManifestJson.Parse(
            Encoding.UTF8.GetBytes(request.RootElement.GetProperty("manifest").GetRawText()));
        await Assert.That(verified.ContentDigest).IsEqualTo(manifest.ContentDigest);
        await Assert.That(Encoding.UTF8.GetString(io.StandardOutput).Contains("contact@", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ImportRequiresExplicitTargetRevisionAndDryRunNeverWritesArtifacts()
    {
        var io = new MemoryIo();
        using JsonDocument identity = JsonDocument.Parse(Identity());
        io.Files["identity.json"] = OperatorIdentityManifestCodec.Write(
            OperatorIdentityManifestJson.Create(identity.RootElement));
        await Assert.That(Run(io, "portability", "import-operator-identity", "--input", "identity.json",
            "--output", "import.json", "--machine")).IsEqualTo(SetupCliExitCode.Usage);
        await Assert.That(io.Files.ContainsKey("import.json")).IsFalse();
        await Assert.That(Run(io, "portability", "import-operator-identity", "--input", "identity.json",
            "--expected-revision", "absent", "--dry-run", "--machine")).IsEqualTo(SetupCliExitCode.Success);
        await Assert.That(io.Files.Count).IsEqualTo(1);
        await Assert.That(SetupCliMachineContractVerifier.Validate(io.StandardOutput)).IsEmpty();
    }

    [Test]
    public async Task TamperedManifestUnknownFormatAndMixedCommandFlagsFailClosed()
    {
        var io = new MemoryIo();
        using JsonDocument identity = JsonDocument.Parse(Identity());
        byte[] valid = OperatorIdentityManifestCodec.Write(OperatorIdentityManifestJson.Create(identity.RootElement));
        io.Files["identity.json"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(valid)
            .Replace("Independent ASBL", "Altered ASBL", StringComparison.Ordinal));
        await Assert.That(Run(io, "portability", "import-operator-identity", "--input", "identity.json",
            "--output", "import.json", "--expected-revision", "absent", "--machine")).IsEqualTo(SetupCliExitCode.Data);
        await Assert.That(io.Files.ContainsKey("import.json")).IsFalse();
        await Assert.That(Run(io, "portability", "export-operator-identity", "--input", "identity.json",
            "--format", "xml", "--dry-run", "--machine")).IsEqualTo(SetupCliExitCode.Usage);
        await Assert.That(Run(io, "doctor", "--expected-revision", "absent", "--machine")).IsEqualTo(SetupCliExitCode.Usage);
    }

    private static byte[] Identity() => """
        {"operatorId":"0198e2a4-5340-7f89-8abc-b8bdf43e0ea8","revision":"0198e2a4-5340-7f89-8abc-b8bdf43e0ea9",
        "publicName":"Independent Operator","legalName":"Independent ASBL","operatorKindCode":"registered_organization",
        "jurisdictionCountryCode":"BE","registrationIdentifier":"BE 0123.456.789","publicContactEmail":"contact@example.test",
        "websiteUrl":"https://example.test","legalNoticeUrl":"https://example.test/legal","termsUrl":"https://example.test/terms",
        "privacyUrl":"https://example.test/privacy","isOfficialInstance":false,"officialOrigin":"https://example.test"}
        """u8.ToArray();

    private static SetupCliExitCode Run(MemoryIo io, params string[] arguments) =>
        new SetupCliApplication().Run(new(arguments, SetupCliMode.Machine, new(io, io, io, 65_536, 4 * 1024 * 1024), new([])));

    private sealed class MemoryIo : ISetupCliInput, ISetupCliWriter
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public byte[] StandardOutput { get; private set; } = [];
        public ReadOnlyMemory<byte> Read(string path, int maximumBytes) => Files[path];
        public void Write(string path, ReadOnlyMemory<byte> bytes, int maximumBytes)
        {
            if (path == "-") StandardOutput = bytes.ToArray();
            else Files.Add(path, bytes.ToArray());
        }
    }
}
