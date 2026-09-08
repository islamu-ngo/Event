using System.Globalization;
using System.Text;
using System.Text.Json;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Event.Setup.Core.Environment;
using ISLAMU.Event.SetupAssistant.Cli;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace ISLAMU.SetupAssistant.Cli.Tests;

public sealed class SetupCliApplicationTests
{
    [Test]
    public async Task EveryPortabilityOperationUsesRealCoreArtifacts()
    {
        byte[] manifest = Artifact(tenant: false);
        byte[] package = Artifact(tenant: true);
        foreach ((string family, byte[] input) in new[] { ("manifest", manifest), ("tenant-package", package) })
        {
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["candidate"] = input, ["baseline"] = input };
            string[][] operations =
            [
                [family, "create", "--dry-run", "--machine"],
                [family, "open", "--input", "candidate", "--machine"],
                [family, "validate", "--input", "candidate", "--machine"],
                [family, "format", "--input", "candidate", "--output", "formatted", "--machine"],
                [family, "diff", "--baseline", "baseline", "--input", "candidate", "--machine"],
                [family, "coverage", "--input", "candidate", "--machine"],
                [family, "export", "--input", "candidate", "--output", "exported", "--machine"]
            ];
            foreach (string[] command in operations)
            {
                RunResult result = Run(command, files);
                await Assert.That(result.Exit).IsEqualTo(SetupCliExitCode.Success).Because(string.Join(' ', command.Take(2)));
                await Assert.That(SetupCliMachineContractVerifier.Validate(result.StandardOutput)).IsEmpty();
            }
        }
    }

    [Test]
    public async Task DiffRequiresBaselineAndReportsIdenticalAndChangedSections()
    {
        byte[] baseline = Artifact(tenant: false);
        byte[] changed = Artifact(tenant: false, changeSection: true);
        RunResult missing = Run(["manifest", "diff", "--input", "candidate", "--machine"], new() { ["candidate"] = baseline });
        RunResult identical = Run(["manifest", "diff", "--baseline", "baseline", "--input", "candidate", "--machine"],
            new() { ["baseline"] = baseline, ["candidate"] = baseline });
        RunResult different = Run(["manifest", "diff", "--baseline", "baseline", "--input", "candidate", "--machine"],
            new() { ["baseline"] = baseline, ["candidate"] = changed });

        await Assert.That(missing.Exit).IsEqualTo(SetupCliExitCode.Usage);
        using JsonDocument sameJson = JsonDocument.Parse(identical.StandardOutput);
        using JsonDocument changedJson = JsonDocument.Parse(different.StandardOutput);
        await Assert.That(sameJson.RootElement.GetProperty("coverage").GetProperty("missingKeys").GetArrayLength()).IsEqualTo(0);
        await Assert.That(changedJson.RootElement.GetProperty("coverage").GetProperty("missingKeys").EnumerateArray()
            .Select(item => item.GetString())).Contains("instance.settings");
    }

    [Test]
    public async Task CatalogueOperationsWriteOnlyBoundedNonValueMetadata()
    {
        RunResult list = Run(["catalogue", "list", "--output", "catalogue", "--machine"]);
        RunResult show = Run(["catalogue", "show", "--key", "API_HTTP_PORT", "--output", "show", "--machine"]);
        RunResult describe = Run(["catalogue", "describe", "--key", "API_HTTP_PORT", "--dry-run", "--machine"]);
        RunResult missing = Run(["catalogue", "show", "--dry-run", "--machine"]);

        await Assert.That(list.Exit).IsEqualTo(SetupCliExitCode.Success);
        await Assert.That(show.Exit).IsEqualTo(SetupCliExitCode.Success);
        await Assert.That(describe.Exit).IsEqualTo(SetupCliExitCode.Success);
        await Assert.That(missing.Exit).IsEqualTo(SetupCliExitCode.Usage);
        string body = Encoding.UTF8.GetString(list.Files["catalogue"]);
        await Assert.That(body).Contains("API_HTTP_PORT");
        await Assert.That(body).DoesNotContain("safeDefault", StringComparison.OrdinalIgnoreCase);
        await Assert.That(body).DoesNotContain("help", StringComparison.OrdinalIgnoreCase);
        using JsonDocument shown = JsonDocument.Parse(show.Files["show"]);
        await Assert.That(shown.RootElement.GetProperty("key").GetString()).IsEqualTo("API_HTTP_PORT");
        await Assert.That(shown.RootElement.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(["key", "category", "sensitivity", "requirement", "restart", "generation", "activation"]);
    }

    [Test]
    public async Task EnvironmentSelectionIsExplicitRelevantAndAlwaysNoSecret()
    {
        RunResult rendered = Run(["env", "render", "--topology", "standalone", "--capability", "database",
            "--capability", "storage", "--provider", "sqlite", "--provider", "local", "--output", "dotenv", "--machine"]);
        RunResult defaults = Run(["env", "render", "--output", "default", "--machine"]);
        RunResult unknown = Run(["env", "render", "--provider", "removed", "--dry-run", "--machine"]);
        RunResult validated = Run(["env", "validate", "--input", "empty", "--machine"], new() { ["empty"] = [] });

        await Assert.That(rendered.Exit).IsEqualTo(SetupCliExitCode.Success);
        await Assert.That(defaults.Exit).IsEqualTo(SetupCliExitCode.Success);
        await Assert.That(unknown.Exit).IsEqualTo(SetupCliExitCode.Usage);
        await Assert.That(validated.Exit).IsEqualTo(SetupCliExitCode.Success);
        string dotenv = Encoding.UTF8.GetString(rendered.Files["dotenv"]);
        await Assert.That(dotenv).DoesNotContain("STRIPE_", StringComparison.Ordinal);
        await Assert.That(dotenv).DoesNotContain("KEYCLOAK_", StringComparison.Ordinal);
        await Assert.That(dotenv).DoesNotContain("CERBOS_", StringComparison.Ordinal);
        await Assert.That(dotenv).DoesNotContain("INFISICAL_", StringComparison.Ordinal);
    }

    [Test]
    public async Task RemainingFamiliesAliasesAndRemovedTuiHaveClosedOutcomes()
    {
        byte[] manifest = Artifact(tenant: false);
        RunResult legal = Run(["legal", "validate", "--input", "manifest", "--machine"], new() { ["manifest"] = manifest });
        RunResult preview = Run(["legal", "preview", "--input", "manifest", "--machine"], new() { ["manifest"] = manifest });
        RunResult doctor = Run(["doctor", "--machine"]);
        RunResult removedTui = Run(["tui", "--machine"]);
        RunResult alias = Run(["catalog", "list", "--machine"]);

        await Assert.That(legal.Exit).IsEqualTo(SetupCliExitCode.Success);
        await Assert.That(preview.Exit).IsEqualTo(SetupCliExitCode.Incomplete);
        await Assert.That(doctor.Exit).IsEqualTo(SetupCliExitCode.Success);
        await Assert.That(removedTui.Exit).IsEqualTo(SetupCliExitCode.Usage);
        await Assert.That(alias.Exit).IsEqualTo(SetupCliExitCode.Usage);
    }

    [Test]
    public async Task WritesRequireIntentDryRunDoesNotWriteAndExistingOutputFailsClosed()
    {
        RunResult missing = Run(["manifest", "create", "--machine"]);
        RunResult planned = Run(["manifest", "create", "--dry-run", "--machine"]);
        RunResult written = Run(["manifest", "create", "--output", "created", "--machine"]);
        RunResult existing = Run(["manifest", "create", "--output", "existing", "--machine"],
            existingOutputs: new() { ["existing"] = [1] });

        await Assert.That(missing.Exit).IsEqualTo(SetupCliExitCode.Usage);
        await Assert.That(planned.Files).IsEmpty();
        await Assert.That(written.Files.ContainsKey("created")).IsTrue();
        await Assert.That(existing.Exit).IsEqualTo(SetupCliExitCode.Io);
        await Assert.That(existing.Files["existing"]).IsEquivalentTo(new byte[] { 1 });
    }

    [Test]
    public async Task InputUsesCanonicalFourMiBBoundAndFailsAtPlusOne()
    {
        var exactInput = new SizedInput(4 * 1024 * 1024);
        var tooLargeInput = new SizedInput((4 * 1024 * 1024) + 1);
        RunResult exact = Run(["manifest", "validate", "--input", "bounded", "--machine"], input: exactInput);
        RunResult tooLarge = Run(["manifest", "validate", "--input", "bounded", "--machine"], input: tooLargeInput);

        await Assert.That(exactInput.ObservedMaximum).IsEqualTo(4 * 1024 * 1024);
        await Assert.That(tooLargeInput.ObservedMaximum).IsEqualTo(4 * 1024 * 1024);
        await Assert.That(exact.Exit).IsEqualTo(SetupCliExitCode.Data);
        await Assert.That(tooLarge.Exit).IsEqualTo(SetupCliExitCode.Io);
    }

    [Test]
    public async Task MachineOutputIsCultureIndependentDeterministicAndSingleFramed()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            RunResult first = Run(["doctor", "--machine"]);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            RunResult second = Run(["doctor", "--machine"]);
            await Assert.That(first.StandardOutput).IsEquivalentTo(second.StandardOutput);
            await Assert.That(SetupCliMachineContractVerifier.Validate(first.StandardOutput)).IsEmpty();
            await Assert.That(first.StandardOutput.Count(value => value == (byte)'\n')).IsEqualTo(1);
            await Assert.That(first.StandardError).IsEmpty();
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    private static RunResult Run(string[] arguments, Dictionary<string, byte[]>? inputs = null,
        Dictionary<string, byte[]>? existingOutputs = null, ISetupCliInput? input = null)
    {
        var output = new MemoryWriter(existingOutputs);
        var error = new MemoryWriter();
        var io = new SetupCliIo(input ?? new MemoryInput(inputs), output, error, 65_536, 4 * 1024 * 1024);
        var invocation = new SetupCliInvocation(
            arguments,
            SetupCliMode.Text,
            io,
            new SetupCliEnvironmentPresence([]));
        SetupCliExitCode exit = new SetupCliApplication().Run(invocation);
        output.Values.TryGetValue("-", out byte[]? stdout);
        error.Values.TryGetValue("-", out byte[]? stderr);
        return new RunResult(exit, stdout ?? [], stderr ?? [], output.Values
            .Where(item => item.Key != "-").ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    private static byte[] Artifact(bool tenant, bool changeSection = false)
    {
        SetupProfile profile = new(new SetupProfileIdentity("test-profile"), [], [new SetupTopologyKey("standalone")]);
        string[] names = tenant ? ["tenant.settings", "tenant.documents", "tenant.legal_documents"]
            : ["instance.settings", "instance.documents", "instance.legal_documents"];
        SetupSelection selection = new(tenant ? SetupScope.Tenant : SetupScope.Instance,
            ConfigurationImportApplyMode.PreviewOnly, names.Select(name => new PortableSectionKey(name)));
        OfflinePortabilityResult created = tenant
            ? OfflinePortabilityWorkflow.CreateTenantPackage(profile, selection, "package-source", "tenant-source", "Tenant source", null)
            : OfflinePortabilityWorkflow.CreateManifest(profile, selection, "manifest-source", null, null);
        OfflinePortabilityDocument document = created.Document!;
        if (changeSection)
        {
            using JsonDocument value = JsonDocument.Parse("true");
            var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["enabled"] = value.RootElement.Clone() };
            document = OfflinePortabilityWorkflow.Edit(document,
                new OfflinePortabilitySectionEdit(new PortableSectionKey("instance.settings"), OfflinePortabilitySectionSnapshot.Settings(settings))).Document!;
        }
        OfflinePortabilityResult validated = OfflinePortabilityWorkflow.Validate(document);
        return OfflinePortabilityWorkflow.Format(validated.Document!).Output!.Bytes.ToArray();
    }

    private sealed class MemoryInput(Dictionary<string, byte[]>? values = null) : ISetupCliInput
    {
        private readonly Dictionary<string, byte[]> _values = values ?? new(StringComparer.Ordinal);
        public ReadOnlyMemory<byte> Read(string path, int maximumBytes) =>
            _values.TryGetValue(path, out byte[]? bytes) ? bytes : throw new IOException("missing");
    }

    private sealed class SizedInput(int size) : ISetupCliInput
    {
        public int ObservedMaximum { get; private set; }
        public ReadOnlyMemory<byte> Read(string path, int maximumBytes)
        {
            ObservedMaximum = maximumBytes;
            return new byte[size];
        }
    }

    private sealed class MemoryWriter
        : ISetupCliWriter
    {
        public MemoryWriter(Dictionary<string, byte[]>? values = null) =>
            Values = values is null ? new(StringComparer.Ordinal) : new(values, StringComparer.Ordinal);
        public Dictionary<string, byte[]> Values { get; }
        public void Write(string path, ReadOnlyMemory<byte> bytes, int maximumBytes)
        {
            if (bytes.Length > maximumBytes || Values.ContainsKey(path)) throw new IOException("write-rejected");
            Values.Add(path, bytes.ToArray());
        }
    }

    private sealed record RunResult(SetupCliExitCode Exit, byte[] StandardOutput, byte[] StandardError,
        IReadOnlyDictionary<string, byte[]> Files);
}
