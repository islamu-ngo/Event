using ISLAMU.Event.Setup.Core;
using ISLAMU.Event.Setup.Core.Environment;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace ISLAMU.Event.SetupAssistant.Cli;

internal static class SetupCliPortabilityHandlers
{
    internal static SetupCliCommandResult Portability(SetupCliCommand command, SetupCliInvocation invocation, bool tenant)
    {
        (SetupProfile profile, SetupSelection selection) = Context(tenant);
        if (command.Operation == "create")
        {
            OfflinePortabilityResult created = tenant
                ? OfflinePortabilityWorkflow.CreateTenantPackage(profile, selection, "event-setup", "tenant-source", "Tenant source", null)
                : OfflinePortabilityWorkflow.CreateManifest(profile, selection, "event-setup", null, null);
            return FormatCreated(command, invocation, created, tenant);
        }

        byte[] input = SetupCliIoOperations.Read(invocation, command.Input!);
        OfflinePortabilityResult opened = Open(tenant, profile, selection, input);
        if (!opened.Succeeded) return SetupCliResults.CoreFailure(opened.Diagnostics, SetupCliExitCode.Data);
        OfflinePortabilityDocument document = opened.Document!;
        SetupCliMachineCoverage coverage = SetupCliResults.Coverage(OfflinePortabilityWorkflow.Coverage(document));
        if (command.Operation == "diff") return Diff(command, invocation, tenant, profile, selection, document);
        if (command.Operation == "coverage") return SetupCliResults.Success([], coverage, SetupCliResults.Readiness(coverage));
        if (command.Operation is "open" or "validate")
            return SetupCliResults.Success([InputArtifact(tenant, input, coverage)], coverage, SetupCliResults.Ready());

        OfflinePortabilityOutput? output = command.Operation == "export"
            ? OfflinePortabilityWorkflow.Export(document).Output
            : OfflinePortabilityWorkflow.Format(document).Output;
        if (output is null) return SetupCliResults.Failure(SetupCliExitCode.Validation, "artifact-not-ready");
        SetupCliIoOperations.Write(invocation, command, output.Bytes);
        return SetupCliResults.Success([SetupCliResults.Artifact(Kind(tenant), output.MediaType, output.Bytes.Span, "public",
            SetupCliResults.PathIntent(command.Output), command.DryRun ? "planned" : "written", coverage)], coverage, SetupCliResults.Ready());
    }

    internal static SetupCliCommandResult Legal(SetupCliCommand command, SetupCliInvocation invocation)
    {
        byte[] input = SetupCliIoOperations.Read(invocation, command.Input!);
        SetupProfile profile = new(new SetupProfileIdentity("event-setup"), [], [new SetupTopologyKey("standalone")]);
        SetupSelection selection = new(SetupScope.Instance, ConfigurationImportApplyMode.PreviewOnly,
            [new PortableSectionKey("instance.legal_documents")]);
        OfflinePortabilityResult opened = OfflinePortabilityWorkflow.OpenManifest(profile, selection, input);
        if (!opened.Succeeded) return SetupCliResults.CoreFailure(opened.Diagnostics, SetupCliExitCode.Data);
        return command.Operation == "preview"
            ? SetupCliResults.Failure(SetupCliExitCode.Incomplete, "legal-identity-required")
            : SetupCliResults.Success([SetupCliResults.Artifact("legal-draft", "application/json", input, "public", "input", "none")]);
    }

    internal static SetupCliCommandResult Doctor()
    {
        _ = CanonicalEnvironmentCatalogue.Catalogue.Definitions.Count;
        byte[] bytes = SetupCliCommandSchemaMetadata.GenerateSchema();
        return SetupCliResults.Success([SetupCliResults.Artifact("doctor-report", "application/json", bytes, "public", "none", "none")]);
    }

    private static SetupCliCommandResult Diff(SetupCliCommand command, SetupCliInvocation invocation, bool tenant,
        SetupProfile profile, SetupSelection selection, OfflinePortabilityDocument candidate)
    {
        byte[] baselineBytes = SetupCliIoOperations.Read(invocation, command.Baseline!);
        OfflinePortabilityResult openedBaseline = Open(tenant, profile, selection, baselineBytes);
        if (!openedBaseline.Succeeded) return SetupCliResults.CoreFailure(openedBaseline.Diagnostics, SetupCliExitCode.Data);
        SetupDiffResult diff = OfflinePortabilityWorkflow.Diff(openedBaseline.Document!, candidate);
        string[] unchanged = diff.Unchanged.Select(item => item.Value).ToArray();
        string[] different = diff.Added.Concat(diff.Removed).Concat(diff.Changed).Select(item => item.Value).ToArray();
        SetupCliMachineCoverage facts = new(SetupCliResults.NormalizeKeys(unchanged), SetupCliResults.NormalizeKeys(different));
        return SetupCliResults.Success([], facts, SetupCliResults.Readiness(facts));
    }

    private static SetupCliCommandResult FormatCreated(SetupCliCommand command, SetupCliInvocation invocation,
        OfflinePortabilityResult created, bool tenant)
    {
        if (!created.Succeeded) return SetupCliResults.CoreFailure(created.Diagnostics, SetupCliExitCode.Validation);
        OfflinePortabilityResult validated = OfflinePortabilityWorkflow.Validate(created.Document!);
        if (!validated.Succeeded) return SetupCliResults.CoreFailure(validated.Diagnostics, SetupCliExitCode.Validation);
        OfflinePortabilityFormatResult formatted = OfflinePortabilityWorkflow.Format(validated.Document!);
        if (!formatted.Succeeded) return SetupCliResults.CoreFailure(formatted.Diagnostics, SetupCliExitCode.Validation);
        SetupCliIoOperations.Write(invocation, command, formatted.Output!.Bytes);
        SetupCliMachineCoverage coverage = SetupCliResults.Coverage(OfflinePortabilityWorkflow.Coverage(validated.Document!));
        return SetupCliResults.Success([SetupCliResults.Artifact(Kind(tenant), formatted.Output.MediaType,
            formatted.Output.Bytes.Span, "public", SetupCliResults.PathIntent(command.Output),
            command.DryRun ? "planned" : "written", coverage)], coverage, SetupCliResults.Ready());
    }

    private static OfflinePortabilityResult Open(bool tenant, SetupProfile profile, SetupSelection selection, byte[] bytes) =>
        tenant ? OfflinePortabilityWorkflow.OpenTenantPackage(profile, selection, bytes)
            : OfflinePortabilityWorkflow.OpenManifest(profile, selection, bytes);

    private static (SetupProfile, SetupSelection) Context(bool tenant)
    {
        SetupProfile profile = new(new SetupProfileIdentity("event-setup"), [], [new SetupTopologyKey("standalone")]);
        string[] names = tenant ? ["tenant.settings", "tenant.documents", "tenant.legal_documents"]
            : ["instance.settings", "instance.documents", "instance.legal_documents"];
        return (profile, new SetupSelection(tenant ? SetupScope.Tenant : SetupScope.Instance,
            ConfigurationImportApplyMode.PreviewOnly, names.Select(value => new PortableSectionKey(value))));
    }

    private static SetupCliMachineArtifact InputArtifact(bool tenant, byte[] input, SetupCliMachineCoverage coverage) =>
        SetupCliResults.Artifact(Kind(tenant), "application/json;v=v1alpha2", input, "public", "input", "none", coverage);
    private static string Kind(bool tenant) => tenant ? "tenant-configuration-package" : "configuration-manifest";
}

internal static class SetupCliIoOperations
{
    internal static byte[] Read(SetupCliInvocation invocation, string path)
    {
        ReadOnlyMemory<byte> bytes = invocation.Io.Input.Read(path, invocation.Io.MaximumBytes);
        if (bytes.Length > invocation.Io.MaximumBytes) throw new IOException("input-bound");
        return bytes.ToArray();
    }

    internal static void Write(SetupCliInvocation invocation, SetupCliCommand command, ReadOnlyMemory<byte> bytes)
    {
        if (!command.DryRun) invocation.Io.Output.Write(command.Output!, bytes, invocation.Io.MaximumBytes);
    }
}
