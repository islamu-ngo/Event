#:property RestorePackagesWithLockFile=false
#pragma warning disable CA1050

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

var options = ParseArgs(args);
if (options is null) return Fail("adapter_usage_invalid");

string providersRoot = Path.GetFullPath(options.ProvidersRoot);
string inputsPath = Path.GetFullPath(options.InputsPath);
string bundleRoot = Path.GetFullPath(options.BundleRoot);
string outputRoot = Path.GetFullPath(options.OutputRoot);
string? externalControlEvidencePath = string.IsNullOrWhiteSpace(options.ExternalControlEvidencePath) ? null : Path.GetFullPath(options.ExternalControlEvidencePath);

try
{
    ValidateRoot(providersRoot);
    ValidateRoot(bundleRoot);
    if (!File.Exists(inputsPath)) return Fail("adapter_inputs_missing");
    if (IsAlias(inputsPath)) return Fail("adapter_path_alias");
    if (externalControlEvidencePath is not null && (!File.Exists(externalControlEvidencePath) || IsAlias(externalControlEvidencePath))) return Fail("adapter_external_control_evidence_invalid");
    if (File.Exists(outputRoot) || Directory.Exists(outputRoot)) return Fail("adapter_output_exists");

    ReleaseInputs inputs = ReadInputs(inputsPath);
    if (inputs.DirtyWorktree) return Fail("adapter_dirty_worktree");
    ValidateInputs(inputs);

    string bundlePath = ResolveContainedPath(bundleRoot, inputs.ReleaseBundlePath);
    if (!File.Exists(bundlePath)) return Fail("adapter_bundle_missing");
    if (IsAlias(bundlePath)) return Fail("adapter_path_alias");
    string bundleSha256 = Sha256(File.ReadAllBytes(bundlePath));
    if (!string.Equals(bundleSha256, inputs.ReleaseBundleSha256, StringComparison.Ordinal)) return Fail("adapter_checksum_drift");

    IReadOnlyList<ProviderDefinition> providers = ReadProviders(providersRoot);
    if (!string.IsNullOrWhiteSpace(options.Provider))
    {
        providers = providers.Where(provider => string.Equals(provider.ProviderId, options.Provider, StringComparison.Ordinal)).ToArray();
        if (providers.Count == 0) return Fail("adapter_provider_missing");
    }

    ExternalControlEvidence? externalControlEvidence = externalControlEvidencePath is null ? null : ReadExternalControlEvidence(externalControlEvidencePath);

    Directory.CreateDirectory(outputRoot);
    string inputsSha256 = Sha256(File.ReadAllBytes(inputsPath));
    foreach (ProviderDefinition provider in providers.OrderBy(provider => provider.ProviderId, StringComparer.Ordinal))
    {
        ValidateProvider(provider, options.Operation, externalControlEvidence);
        WritePlan(outputRoot, provider, inputs, inputsSha256, bundleSha256);
    }

    Console.WriteLine($"adapter_validation_passed: providers={providers.Count}");
    return 0;
}
catch (AdapterException exception)
{
    if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
    return Fail(exception.Code);
}
catch (JsonException)
{
    if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
    return Fail("adapter_json_invalid");
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
{
    if (Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
    return Fail("adapter_path_invalid");
}

static AdapterOptions? ParseArgs(string[] args)
{
    string? providers = null;
    string? inputs = null;
    string? bundleRoot = null;
    string? output = null;
    string? provider = null;
    string? externalControlEvidence = null;
    string operation = "plan";
    for (int index = 0; index < args.Length; index++)
    {
        string key = args[index];
        if (index + 1 >= args.Length) return null;
        string value = args[++index];
        switch (key)
        {
            case "--providers": providers = value; break;
            case "--inputs": inputs = value; break;
            case "--bundle-root": bundleRoot = value; break;
            case "--output": output = value; break;
            case "--provider": provider = value; break;
            case "--operation": operation = value; break;
            case "--external-control-evidence": externalControlEvidence = value; break;
            default: return null;
        }
    }

    return string.IsNullOrWhiteSpace(providers) || string.IsNullOrWhiteSpace(inputs) || string.IsNullOrWhiteSpace(bundleRoot) || string.IsNullOrWhiteSpace(output)
        ? null
        : new AdapterOptions(providers, inputs, bundleRoot, output, provider, operation, externalControlEvidence);
}

static IReadOnlyList<ProviderDefinition> ReadProviders(string providersRoot)
{
    var normalizedProviderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var definitions = new List<ProviderDefinition>();
    string[] directories = Directory.EnumerateDirectories(providersRoot).Order(StringComparer.Ordinal).ToArray();
    foreach (string directory in directories)
    {
        string fullDirectory = Path.GetFullPath(directory);
        if (IsAlias(fullDirectory)) throw new AdapterException("adapter_path_alias");
        string relative = Path.GetRelativePath(providersRoot, fullDirectory).Replace(Path.DirectorySeparatorChar, '/');
        if (!normalizedProviderPaths.Add(relative.Normalize(NormalizationForm.FormC))) throw new AdapterException("adapter_path_alias");
    }

    foreach (string directory in directories)
    {
        string fullDirectory = Path.GetFullPath(directory);
        string relative = Path.GetRelativePath(providersRoot, fullDirectory).Replace(Path.DirectorySeparatorChar, '/');
        string definitionPath = Path.Combine(fullDirectory, "provider-definition.v1.json");
        if (!File.Exists(definitionPath)) continue;
        if (IsAlias(definitionPath)) throw new AdapterException("adapter_path_alias");
        ProviderDefinition provider = ReadProviderDefinition(definitionPath, fullDirectory);
        if (!string.Equals(provider.ProviderId, relative, StringComparison.Ordinal)) throw new AdapterException("adapter_provider_path_mismatch");
        definitions.Add(provider);
    }

    return definitions.Count == 0 ? throw new AdapterException("adapter_definition_missing") : definitions;
}

static ProviderDefinition ReadProviderDefinition(string path, string definitionDirectory)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
    JsonElement root = document.RootElement;
    RequireObject(root);
    RequireOnly(root, TopKeys());
    RequireKeys(root, TopKeys());
    JsonElement preview = root.GetProperty("previewLane");
    JsonElement final = root.GetProperty("finalLane");
    JsonElement capabilities = root.GetProperty("capabilities");
    JsonElement guards = root.GetProperty("guards");
    RequireObject(preview);
    RequireObject(final);
    RequireObject(capabilities);
    RequireObject(guards);
    RequireOnly(preview, PreviewKeys());
    RequireOnly(final, FinalKeys());
    RequireOnly(capabilities, CapabilityKeys());
    RequireOnly(guards, GuardKeys());
    RequireKeys(preview, PreviewSchemaKeys());
    RequireKeys(final, FinalSchemaKeys());
    RequireKeys(capabilities, CapabilityKeys());
    RequireKeys(guards, GuardKeys());

    return new ProviderDefinition(
        SchemaVersion: ReadString(root, "schemaVersion"),
        ProviderId: ReadString(root, "providerId"),
        DisplayName: ReadString(root, "displayName"),
        DefinitionDirectory: definitionDirectory,
        DiscoveryWorkflows: ReadStringArray(root, "discoveryWorkflows"),
        PublicationWorkflows: ReadStringArray(root, "publicationWorkflows"),
        Actions: ReadStringArray(root, "actions"),
        PreviewLane: new PreviewLane(ReadString(preview, "event"), ReadBool(preview, "trustedCodeOnly"), ReadStringArray(preview, "secrets"), ReadStringArray(preview, "permissions"), ReadStringArrayOrEmpty(preview, "requiredChecks"), ReadBool(preview, "alwaysPresentNoop")),
        FinalLane: new FinalLane(ReadString(final, "event"), ReadString(final, "trustedRef"), ReadBool(final, "trustedCodeOnly"), ReadBool(final, "environmentApproval"), ReadBool(final, "requiresSelfHostedTrustedRunner"), ReadStringArrayOrEmpty(final, "requiredChecks"), ReadBool(final, "alwaysPresentNoop"), ReadBool(final, "candidateStopsBeforeFinal")),
        Capabilities: new Capabilities(ReadBool(capabilities, "artifacts"), ReadInt(capabilities, "retentionDays"), ReadBool(capabilities, "protectedRefCas"), ReadBool(capabilities, "releasePublication"), ReadBool(capabilities, "operatorEvidenceRequired")),
        Guards: new Guards(ReadBool(guards, "immutableBundleVerification"), ReadBool(guards, "providerNeutralChecksumEquality"), ReadBool(guards, "metadataCanonical"), ReadBool(guards, "misleadingSuccessForbidden")),
        Diagnostics: ReadStringArray(root, "diagnostics"));
}

static ExternalControlEvidence ReadExternalControlEvidence(string path)
{
    if (new FileInfo(path).Length > 4096) throw new AdapterException("adapter_external_control_evidence_invalid");
    using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
    JsonElement root = document.RootElement;
    RequireObject(root);
    RequireOnly(root, ExternalEvidenceKeys());
    RequireKeys(root, ExternalEvidenceKeys());
    var evidence = new ExternalControlEvidence(
        ReadString(root, "schemaVersion"),
        ReadString(root, "providerId"),
        ReadString(root, "operation"),
        ReadString(root, "unsupportedCapability"),
        ReadBool(root, "approved"));
    return evidence.SchemaVersion == "release-adapter-external-control-evidence.v1" && evidence.Approved
        ? evidence
        : throw new AdapterException("adapter_external_control_evidence_invalid");
}

static ReleaseInputs ReadInputs(string inputsPath)
{
    using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(inputsPath));
    JsonElement root = document.RootElement;
    RequireObject(root);
    RequireOnly(root, InputKeys());
    RequireKeys(root, InputKeys());
    return new ReleaseInputs(
        ReadString(root, "schemaVersion"),
        ReadString(root, "targetOid"),
        ReadString(root, "expectedOldProtectedRefOid"),
        ReadString(root, "tagObjectId"),
        ReadString(root, "tagName"),
        ReadString(root, "releaseBundlePath"),
        ReadString(root, "releaseBundleSha256"),
        ReadString(root, "artifactManifestSha256"),
        ReadBool(root, "dirtyWorktree"));
}

static void ValidateInputs(ReleaseInputs inputs)
{
    if (inputs.SchemaVersion != "release-adapter-inputs.v1") throw new AdapterException("adapter_input_schema_invalid");
    foreach (string oid in new[] { inputs.TargetOid, inputs.ExpectedOldProtectedRefOid, inputs.TagObjectId })
    {
        if (!FullOidPattern().IsMatch(oid)) throw new AdapterException("adapter_input_oid_not_full");
    }

    if (!Sha256Pattern().IsMatch(inputs.ReleaseBundleSha256) || !Sha256Pattern().IsMatch(inputs.ArtifactManifestSha256)) throw new AdapterException("adapter_input_checksum_invalid");
    if (inputs.ReleaseBundlePath.Length == 0 || inputs.ReleaseBundlePath.StartsWith("/", StringComparison.Ordinal) || inputs.ReleaseBundlePath.Contains("..", StringComparison.Ordinal)) throw new AdapterException("adapter_path_invalid");
}

static void ValidateProvider(ProviderDefinition provider, string operation, ExternalControlEvidence? externalControlEvidence)
{
    if (provider.SchemaVersion != "release-provider.v1") throw new AdapterException("adapter_definition_schema_invalid");
    if (!ProviderIdPattern().IsMatch(provider.ProviderId)) throw new AdapterException("adapter_provider_id_invalid");
    if (provider.PreviewLane.Secrets.Count != 0) throw new AdapterException("adapter_preview_secrets_forbidden");
    if (provider.PreviewLane.Permissions.Any(permission => !string.Equals(permission, "contents:read", StringComparison.Ordinal))) throw new AdapterException("adapter_preview_permission_forbidden");
    if (provider.ProviderId == "github" && !provider.FinalLane.EnvironmentApproval) throw new AdapterException("adapter_final_environment_required");
    if (provider.PreviewLane.TrustedCodeOnly) throw new AdapterException("adapter_preview_trust_invalid");
    if (!provider.FinalLane.TrustedCodeOnly || !provider.FinalLane.CandidateStopsBeforeFinal) throw new AdapterException("adapter_final_candidate_code_forbidden");
    ValidateTrustedRef(provider);
    ValidateFinalEvent(provider);
    if (!provider.PreviewLane.AlwaysPresentNoop || !provider.FinalLane.AlwaysPresentNoop) throw new AdapterException("adapter_required_check_missing");
    if (!provider.PreviewLane.RequiredChecks.Contains("release-adapter-preview", StringComparer.Ordinal) || !provider.FinalLane.RequiredChecks.Contains("release-adapter-final", StringComparer.Ordinal)) throw new AdapterException("adapter_required_check_missing");
    if (!provider.Guards.ImmutableBundleVerification || !provider.Guards.ProviderNeutralChecksumEquality || provider.Guards.MetadataCanonical) throw new AdapterException("adapter_guard_invalid");
    if (!provider.Guards.MisleadingSuccessForbidden) throw new AdapterException("adapter_misleading_success_forbidden");
    foreach (string action in provider.Actions)
    {
        if (!ActionPinPattern().IsMatch(action)) throw new AdapterException("adapter_action_pin_mutable");
    }

    if (provider.ProviderId == "tangled" && operation is "publish-release" or "update-protected-ref")
    {
        string unsupportedCapability = operation == "publish-release" ? "releasePublication" : "protectedRefCas";
        if (!provider.Capabilities.OperatorEvidenceRequired || provider.Capabilities.ReleasePublication || provider.Capabilities.ProtectedRefCas || !ExternalEvidenceMatches(externalControlEvidence, provider.ProviderId, operation, unsupportedCapability)) throw new AdapterException("adapter_provider_action_unsupported");
    }
    if (provider.ProviderId == "forgejo-codeberg" && !provider.FinalLane.RequiresSelfHostedTrustedRunner) throw new AdapterException("adapter_self_hosted_runner_required");
    if (provider.ProviderId == "github" && provider.PreviewLane.Event != "pull_request") throw new AdapterException("adapter_github_preview_event_invalid");
    ValidateDiscoveryWorkflows(provider);
    ValidatePublicationWorkflows(provider);
}

// A published release page is a noncanonical projection of the signed tag. It is mutable and
// unsigned, so the contract it must satisfy is stated here and machine-checked: trusted final lane
// only, canonical notes hash and tag reference on the page, self-verifying assets attached, and a
// recorded operator no-op for any provider that has no release API at all.
static void ValidatePublicationWorkflows(ProviderDefinition provider)
{
    foreach (string workflow in provider.PublicationWorkflows)
    {
        string path = ResolveDiscoveryWorkflowPath(provider, workflow);
        if (!File.Exists(path) || IsAlias(path)) throw new AdapterException("adapter_publication_workflow_missing");
        string text = File.ReadAllText(path);
        if (!text.Contains("release-publish", StringComparison.Ordinal)) throw new AdapterException("adapter_publication_workflow_mismatch");
        if (WorkflowHasEvent(text, "pull_request") || WorkflowHasEvent(text, "pull_request_target") || WorkflowHasEvent(text, "push"))
        {
            throw new AdapterException("adapter_publication_untrusted_origin");
        }

        if (!text.Contains("canonical-notes-sha256", StringComparison.Ordinal) || !text.Contains("tag-reference", StringComparison.Ordinal))
        {
            throw new AdapterException("adapter_publication_canonical_reference_missing");
        }

        foreach (string asset in RequiredPublicationAssets())
        {
            if (!text.Contains(asset, StringComparison.Ordinal)) throw new AdapterException("adapter_publication_asset_missing");
        }

        foreach (string action in ExtractUsedActions(text))
        {
            if (!ActionPinPattern().IsMatch(action)) throw new AdapterException("adapter_action_pin_mutable");
        }

        if (provider.Capabilities.ReleasePublication)
        {
            if (provider.FinalLane.EnvironmentApproval && !text.Contains("environment:", StringComparison.Ordinal)) throw new AdapterException("adapter_final_environment_required");
        }
        else
        {
            if (!provider.Capabilities.OperatorEvidenceRequired) throw new AdapterException("adapter_publication_noop_evidence_required");
            if (!text.Contains("recorded-no-op", StringComparison.Ordinal)) throw new AdapterException("adapter_publication_noop_evidence_required");
        }
    }
}

static IReadOnlyList<string> RequiredPublicationAssets() =>
    ["release-evidence.v1.json", "artifacts.sha256", "container-image-digests", "sbom"];

static void ValidateDiscoveryWorkflows(ProviderDefinition provider)
{
    if (provider.DiscoveryWorkflows.Count == 0) throw new AdapterException("adapter_discovery_workflow_missing");
    var declaredActions = provider.Actions.ToHashSet(StringComparer.Ordinal);
    var usedActions = new HashSet<string>(StringComparer.Ordinal);
    var seenPreview = false;
    var seenFinal = false;
    foreach (string workflow in provider.DiscoveryWorkflows)
    {
        string path = ResolveDiscoveryWorkflowPath(provider, workflow);
        if (!File.Exists(path) || IsAlias(path)) throw new AdapterException("adapter_discovery_workflow_missing");
        string text = File.ReadAllText(path);
        foreach (string action in ExtractUsedActions(text)) usedActions.Add(action);
        bool hasPreview = text.Contains("release-adapter-preview", StringComparison.Ordinal);
        bool hasFinal = text.Contains("release-adapter-final", StringComparison.Ordinal);
        if (hasPreview)
        {
            seenPreview = true;
            if (!WorkflowHasEvent(text, provider.PreviewLane.Event) || WorkflowHasEvent(text, "workflow_dispatch") || WorkflowHasEvent(text, "manual")) throw new AdapterException("adapter_discovery_workflow_mismatch");
            if (provider.ProviderId == "github" && !text.Contains("github.event_name == 'pull_request'", StringComparison.Ordinal)) throw new AdapterException("adapter_discovery_workflow_mismatch");
        }

        if (hasFinal)
        {
            seenFinal = true;
            if (!WorkflowHasEvent(text, provider.FinalLane.Event) || WorkflowHasEvent(text, "pull_request") || WorkflowHasEvent(text, "pull_request_target")) throw new AdapterException("adapter_discovery_workflow_mismatch");
            if (provider.FinalLane.EnvironmentApproval && !text.Contains("environment:", StringComparison.Ordinal)) throw new AdapterException("adapter_final_environment_required");
            if (provider.ProviderId == "github")
            {
                if (!text.Contains("github.event_name == 'workflow_dispatch'", StringComparison.Ordinal)) throw new AdapterException("adapter_discovery_workflow_mismatch");
                if (!text.Contains("ref: ${{ github.event.repository.default_branch }}", StringComparison.Ordinal)) throw new AdapterException("adapter_final_trusted_ref_invalid");
            }
            else if (provider.FinalLane.TrustedRef == "no-checkout-discovery")
            {
                ValidateNoCheckoutDiscoveryWorkflow(text);
            }
        }
    }

    if (!seenPreview || !seenFinal) throw new AdapterException("adapter_discovery_workflow_mismatch");
    if (!declaredActions.SetEquals(usedActions)) throw new AdapterException("adapter_action_manifest_mismatch");
}

static void ValidateTrustedRef(ProviderDefinition provider)
{
    bool valid = provider.ProviderId == "github"
        ? provider.FinalLane.TrustedRef == "default-branch"
        : provider.FinalLane.TrustedRef == "no-checkout-discovery";
    if (!valid) throw new AdapterException("adapter_final_trusted_ref_invalid");
}

static void ValidateNoCheckoutDiscoveryWorkflow(string text)
{
    if (ExtractUsedActions(text).Any()) throw new AdapterException("adapter_no_checkout_discovery_invalid");
    foreach (string line in text.Split('\n'))
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
        string execution = trimmed.StartsWith("- run:", StringComparison.Ordinal) ? trimmed[2..].TrimStart() : trimmed;
        if (trimmed == "printf '%s\\n' 'release-adapter-final: transport-only no-checkout discovery'" || execution == "run: printf '%s\\n' 'release-adapter-final: transport-only no-checkout discovery'") continue;
        if (trimmed.Contains("checkout", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("uses:", StringComparison.Ordinal) || trimmed.StartsWith("- uses:", StringComparison.Ordinal) || trimmed.StartsWith("container:", StringComparison.Ordinal) || trimmed.StartsWith("image:", StringComparison.Ordinal)) throw new AdapterException("adapter_no_checkout_discovery_invalid");
        if (execution.StartsWith("run:", StringComparison.Ordinal) && (execution.Contains("${{", StringComparison.Ordinal) || execution != "run: |" && execution != "run: printf '%s\\n' 'release-adapter-final: transport-only no-checkout discovery'")) throw new AdapterException("adapter_no_checkout_discovery_invalid");
        if (trimmed.StartsWith("printf ", StringComparison.Ordinal)) throw new AdapterException("adapter_no_checkout_discovery_invalid");
        if (trimmed.Contains("candidate", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("targetOid", StringComparison.Ordinal) || trimmed.Contains("target-oid", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("curl ", StringComparison.Ordinal) || trimmed.Contains("wget ", StringComparison.Ordinal) || trimmed.Contains("bash ", StringComparison.Ordinal) || trimmed.Contains("sh ", StringComparison.Ordinal) || trimmed.Contains("dotnet ", StringComparison.Ordinal) || trimmed.Contains("git ", StringComparison.Ordinal) || trimmed.Contains("gh ", StringComparison.Ordinal) || trimmed.Contains("python", StringComparison.Ordinal) || trimmed.Contains("node ", StringComparison.Ordinal) || trimmed.Contains("npm ", StringComparison.Ordinal) || trimmed.Contains("docker ", StringComparison.Ordinal) || trimmed.Contains("podman ", StringComparison.Ordinal)) throw new AdapterException("adapter_no_checkout_discovery_invalid");
    }
}

static string ResolveDiscoveryWorkflowPath(ProviderDefinition provider, string workflow)
{
    if (Path.IsPathRooted(workflow)) return Path.GetFullPath(workflow);
    if (workflow.StartsWith(".ci/", StringComparison.Ordinal) || workflow.StartsWith(".github/", StringComparison.Ordinal)) return Path.GetFullPath(workflow);
    return Path.GetFullPath(Path.Combine(provider.DefinitionDirectory, workflow));
}

static IEnumerable<string> ExtractUsedActions(string text)
{
    foreach (string line in text.Split('\n'))
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith("- uses:", StringComparison.Ordinal)) trimmed = trimmed[2..].TrimStart();
        if (!trimmed.StartsWith("uses:", StringComparison.Ordinal)) continue;
        string value = trimmed["uses:".Length..].Trim().Split('#')[0].Trim().Trim('"', '\'');
        if (!value.StartsWith("./", StringComparison.Ordinal)) yield return value;
    }
}

static bool WorkflowHasEvent(string text, string eventName)
{
    foreach (string line in text.Split('\n'))
    {
        string trimmed = line.Trim();
        if (trimmed == eventName + ":" || trimmed == "- " + eventName) return true;
    }

    return false;
}

static void ValidateFinalEvent(ProviderDefinition provider)
{
    if (provider.FinalLane.Event.StartsWith("pull_request", StringComparison.Ordinal)) throw new AdapterException("adapter_final_event_forbidden");
    bool allowed = provider.ProviderId switch
    {
        "github" => provider.FinalLane.Event == "workflow_dispatch",
        "forgejo-codeberg" => provider.FinalLane.Event == "workflow_dispatch",
        "tangled" => provider.FinalLane.Event is "manual" or "tag_push",
        _ => false,
    };
    if (!allowed) throw new AdapterException("adapter_final_event_forbidden");
}

static bool ExternalEvidenceMatches(ExternalControlEvidence? evidence, string providerId, string operation, string unsupportedCapability) =>
    evidence is not null &&
    evidence.ProviderId == providerId &&
    evidence.Operation == operation &&
    evidence.UnsupportedCapability == unsupportedCapability;

static void WritePlan(string outputRoot, ProviderDefinition provider, ReleaseInputs inputs, string inputsSha256, string bundleSha256)
{
    string path = Path.Combine(outputRoot, provider.ProviderId + ".transport-plan.v1.json");
    using FileStream stream = File.Create(path);
    using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
    writer.WriteStartObject();
    writer.WriteString("schemaVersion", "release-transport-plan.v1");
    writer.WriteString("providerId", provider.ProviderId);
    writer.WriteString("displayName", provider.DisplayName);
    writer.WriteBoolean("transportOnly", true);
    writer.WriteBoolean("metadataCanonical", false);
    writer.WriteString("tagName", inputs.TagName);
    writer.WriteString("tagObjectId", inputs.TagObjectId);
    writer.WriteString("expectedOldProtectedRefOid", inputs.ExpectedOldProtectedRefOid);
    writer.WriteString("targetOid", inputs.TargetOid);
    writer.WritePropertyName("canonicalChecksums");
    writer.WriteStartObject();
    writer.WriteString("promotedBundleSha256", bundleSha256);
    writer.WriteString("releaseInputsSha256", inputsSha256);
    writer.WriteEndObject();
    writer.WritePropertyName("lanes");
    writer.WriteStartObject();
    writer.WriteString("preview", provider.PreviewLane.Event);
    writer.WriteString("final", provider.FinalLane.Event);
    writer.WriteEndObject();
    writer.WritePropertyName("requiredChecks");
    writer.WriteStartArray();
    writer.WriteStringValue("release-adapter-preview");
    writer.WriteStringValue("release-adapter-final");
    writer.WriteEndArray();
    writer.WriteNumber("retentionDays", provider.Capabilities.RetentionDays);
    writer.WriteEndObject();
}

static void ValidateRoot(string path)
{
    if (!Directory.Exists(path)) throw new AdapterException("adapter_path_invalid");
    if (IsAlias(path)) throw new AdapterException("adapter_path_alias");
}

static string ResolveContainedPath(string root, string relative)
{
    string fullPath = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
    string rootPrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
    if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal)) throw new AdapterException("adapter_path_invalid");
    return fullPath;
}

static bool IsAlias(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || (File.Exists(path) && !HasSingleFileLink(path));

static bool HasSingleFileLink(string path)
{
    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return UnixLinkCount(path) == 1;
    if (OperatingSystem.IsWindows()) return WindowsLinkCount(path) == 1;
    return true;
}

static int UnixLinkCount(string path)
{
    string executable = "/usr/bin/stat";
    if (!File.Exists(executable)) return 0;
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        },
    };
    process.StartInfo.ArgumentList.Add(OperatingSystem.IsMacOS() ? "-f" : "-c");
    process.StartInfo.ArgumentList.Add(OperatingSystem.IsMacOS() ? "%l" : "%h");
    process.StartInfo.ArgumentList.Add(path);
    if (!process.Start()) return 0;
    string output = process.StandardOutput.ReadToEnd();
    _ = process.StandardError.ReadToEnd();
    if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit();
        return 0;
    }

    return process.ExitCode == 0 && int.TryParse(output.Trim(), out int count) ? count : 0;
}

static int WindowsLinkCount(string path)
{
    using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    return NativeMethods.GetFileInformationByHandle(handle, out ByHandleFileInformation info) ? (int)info.NumberOfLinks : 0;
}

static void RequireObject(JsonElement element)
{
    if (element.ValueKind != JsonValueKind.Object) throw new AdapterException("adapter_definition_schema_invalid");
}

static void RequireOnly(JsonElement element, IReadOnlySet<string> allowed)
{
    foreach (JsonProperty property in element.EnumerateObject())
    {
        if (!allowed.Contains(property.Name)) throw new AdapterException("adapter_definition_unknown_key");
    }
}

static void RequireKeys(JsonElement element, IReadOnlySet<string> required)
{
    foreach (string key in required)
    {
        if (!element.TryGetProperty(key, out _)) throw new AdapterException("adapter_definition_missing_key");
    }
}

static string ReadString(JsonElement element, string key) => element.GetProperty(key).ValueKind == JsonValueKind.String ? element.GetProperty(key).GetString() ?? string.Empty : throw new AdapterException("adapter_definition_schema_invalid");

static bool ReadBool(JsonElement element, string key) => element.GetProperty(key).ValueKind is JsonValueKind.True or JsonValueKind.False ? element.GetProperty(key).GetBoolean() : throw new AdapterException("adapter_definition_schema_invalid");

static int ReadInt(JsonElement element, string key)
{
    if (!element.GetProperty(key).TryGetInt32(out int value) || value < 1) throw new AdapterException("adapter_definition_schema_invalid");
    return value;
}

static IReadOnlyList<string> ReadStringArray(JsonElement element, string key)
{
    JsonElement array = element.GetProperty(key);
    if (array.ValueKind != JsonValueKind.Array) throw new AdapterException("adapter_definition_schema_invalid");
    return array.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : throw new AdapterException("adapter_definition_schema_invalid")).ToArray();
}

static IReadOnlyList<string> ReadStringArrayOrEmpty(JsonElement element, string key) => element.TryGetProperty(key, out JsonElement array) ? ReadStringArray(element, key) : [];

static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

static int Fail(string code)
{
    Console.Error.WriteLine(code);
    return 1;
}

static IReadOnlySet<string> TopKeys() => new HashSet<string>(["schemaVersion", "providerId", "displayName", "discoveryWorkflows", "publicationWorkflows", "actions", "previewLane", "finalLane", "capabilities", "guards", "diagnostics"], StringComparer.Ordinal);
static IReadOnlySet<string> PreviewKeys() => new HashSet<string>(["event", "trustedCodeOnly", "secrets", "permissions", "requiredChecks", "alwaysPresentNoop"], StringComparer.Ordinal);
static IReadOnlySet<string> PreviewSchemaKeys() => new HashSet<string>(["event", "trustedCodeOnly", "secrets", "permissions", "alwaysPresentNoop"], StringComparer.Ordinal);
static IReadOnlySet<string> FinalKeys() => new HashSet<string>(["event", "trustedRef", "trustedCodeOnly", "environmentApproval", "requiresSelfHostedTrustedRunner", "requiredChecks", "alwaysPresentNoop", "candidateStopsBeforeFinal"], StringComparer.Ordinal);
static IReadOnlySet<string> FinalSchemaKeys() => new HashSet<string>(["event", "trustedRef", "trustedCodeOnly", "environmentApproval", "requiresSelfHostedTrustedRunner", "alwaysPresentNoop", "candidateStopsBeforeFinal"], StringComparer.Ordinal);
static IReadOnlySet<string> CapabilityKeys() => new HashSet<string>(["artifacts", "retentionDays", "protectedRefCas", "releasePublication", "operatorEvidenceRequired"], StringComparer.Ordinal);
static IReadOnlySet<string> GuardKeys() => new HashSet<string>(["immutableBundleVerification", "providerNeutralChecksumEquality", "metadataCanonical", "misleadingSuccessForbidden"], StringComparer.Ordinal);
static IReadOnlySet<string> InputKeys() => new HashSet<string>(["schemaVersion", "targetOid", "expectedOldProtectedRefOid", "tagObjectId", "tagName", "releaseBundlePath", "releaseBundleSha256", "artifactManifestSha256", "dirtyWorktree"], StringComparer.Ordinal);
static IReadOnlySet<string> ExternalEvidenceKeys() => new HashSet<string>(["schemaVersion", "providerId", "operation", "unsupportedCapability", "approved"], StringComparer.Ordinal);
static Regex FullOidPattern() => Patterns.FullOid();
static Regex Sha256Pattern() => Patterns.Sha256();
static Regex ProviderIdPattern() => Patterns.ProviderId();
static Regex ActionPinPattern() => Patterns.ActionPin();

sealed record AdapterOptions(string ProvidersRoot, string InputsPath, string BundleRoot, string OutputRoot, string? Provider, string Operation, string? ExternalControlEvidencePath);
sealed record ProviderDefinition(string SchemaVersion, string ProviderId, string DisplayName, string DefinitionDirectory, IReadOnlyList<string> DiscoveryWorkflows, IReadOnlyList<string> PublicationWorkflows, IReadOnlyList<string> Actions, PreviewLane PreviewLane, FinalLane FinalLane, Capabilities Capabilities, Guards Guards, IReadOnlyList<string> Diagnostics);
sealed record PreviewLane(string Event, bool TrustedCodeOnly, IReadOnlyList<string> Secrets, IReadOnlyList<string> Permissions, IReadOnlyList<string> RequiredChecks, bool AlwaysPresentNoop);
sealed record FinalLane(string Event, string TrustedRef, bool TrustedCodeOnly, bool EnvironmentApproval, bool RequiresSelfHostedTrustedRunner, IReadOnlyList<string> RequiredChecks, bool AlwaysPresentNoop, bool CandidateStopsBeforeFinal);
sealed record Capabilities(bool Artifacts, int RetentionDays, bool ProtectedRefCas, bool ReleasePublication, bool OperatorEvidenceRequired);
sealed record Guards(bool ImmutableBundleVerification, bool ProviderNeutralChecksumEquality, bool MetadataCanonical, bool MisleadingSuccessForbidden);
sealed record ReleaseInputs(string SchemaVersion, string TargetOid, string ExpectedOldProtectedRefOid, string TagObjectId, string TagName, string ReleaseBundlePath, string ReleaseBundleSha256, string ArtifactManifestSha256, bool DirtyWorktree);
sealed record ExternalControlEvidence(string SchemaVersion, string ProviderId, string Operation, string UnsupportedCapability, bool Approved);
sealed class AdapterException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

[StructLayout(LayoutKind.Sequential)]
struct ByHandleFileInformation
{
    public uint FileAttributes;
    public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
    public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
    public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
    public uint VolumeSerialNumber;
    public uint FileSizeHigh;
    public uint FileSizeLow;
    public uint NumberOfLinks;
    public uint FileIndexHigh;
    public uint FileIndexLow;
}

static partial class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation fileInformation);
}

static partial class Patterns
{
    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant)] public static partial Regex FullOid();
    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)] public static partial Regex Sha256();
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant)] public static partial Regex ProviderId();
    [GeneratedRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)] public static partial Regex ActionPin();
}
