using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel]
public sealed class ReleaseProviderAdapterScriptTests
{
    [Test]
    public async Task ProviderAdapterScriptAcceptsTheCheckedInPublicationWorkflow()
    {
        using var fixture = ProviderFixture.CreateSingle("github");

        ScriptResult result = fixture.Run();

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Output);
        await Assert.That(result.Output).Contains("adapter_validation_passed: providers=1");
    }

    [Test]
    public async Task ProviderAdapterScriptEnforcesThePublicationProjectionContract()
    {
        // A published page is the weakest surface in the system, so the few things that *are*
        // enforceable are enforced at manifest-validation time rather than trusted to prose.
        using var untrustedOrigin = ProviderFixture.CreateSingle("github");
        untrustedOrigin.MutatePublicationWorkflow("github", yaml => yaml.Replace("on:\n  workflow_dispatch:", "on:\n  pull_request:", StringComparison.Ordinal));

        using var missingReference = ProviderFixture.CreateSingle("forgejo-codeberg");
        missingReference.MutatePublicationWorkflow("forgejo-codeberg", yaml => yaml.Replace("release-notes-sha256", "release-title", StringComparison.Ordinal));

        using var missingAsset = ProviderFixture.CreateSingle("forgejo-codeberg");
        missingAsset.MutatePublicationWorkflow("forgejo-codeberg", yaml => yaml.Replace("sbom", "changelog", StringComparison.Ordinal));

        using var mutableAction = ProviderFixture.CreateSingle("github");
        mutableAction.MutatePublicationWorkflow("github", yaml => yaml + "      - uses: actions/checkout@v4\n");

        using var unevidencedNoop = ProviderFixture.CreateSingle("tangled");
        unevidencedNoop.MutatePublicationWorkflow("tangled", yaml => yaml.Replace("recorded-no-op", "published", StringComparison.Ordinal));

        ScriptResult originResult = untrustedOrigin.Run();
        ScriptResult referenceResult = missingReference.Run();
        ScriptResult assetResult = missingAsset.Run();
        ScriptResult actionResult = mutableAction.Run();
        ScriptResult noopResult = unevidencedNoop.Run();

        await Assert.That(originResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(originResult.Output).Contains("adapter_publication_untrusted_origin");
        await Assert.That(referenceResult.Output).Contains("adapter_publication_release_notes_reference_missing");
        await Assert.That(assetResult.Output).Contains("adapter_publication_asset_missing");
        await Assert.That(actionResult.Output).Contains("adapter_action_pin_mutable");
        await Assert.That(noopResult.Output).Contains("adapter_publication_noop_evidence_required");
    }

    [Test]
    public async Task ProviderAdapterScriptRejectsPublicationInventoryAndProjectionDrift()
    {
        using var inventory = ProviderFixture.CreateSingle("github");
        inventory.MutatePublicationWorkflow("github", yaml => yaml.Replace(
            "--inventory \"$inputs/authorized-inventory.v1.json\"", "--inventory \"$inputs/current-release.json\"", StringComparison.Ordinal));
        using var evidence = ProviderFixture.CreateSingle("github");
        evidence.MutatePublicationWorkflow("github", yaml => yaml.Replace("--retained-evidence", "--unverified-evidence", StringComparison.Ordinal));
        using var check = ProviderFixture.CreateSingle("github");
        check.MutatePublicationWorkflow("github", yaml => yaml.Replace(" --check", "", StringComparison.Ordinal));
        using var projection = ProviderFixture.CreateSingle("github");
        projection.MutatePublicationWorkflow("github", yaml => yaml.Replace(
            "PAGE: docs/public/changelog/README.md", "PAGE: docs/public/README.md", StringComparison.Ordinal));
        using var manifest = ProviderFixture.CreateSingle("github");
        manifest.MutatePublicationWorkflow("github", yaml => yaml.Replace(
            "MANIFEST: docs/public/changelog/publication-manifest.v1.json", "MANIFEST: docs/public/changelog/unbound.json", StringComparison.Ordinal));

        foreach (ScriptResult result in new[] { inventory, evidence, check }.Select(fixture => fixture.Run()))
        {
            await Assert.That(result.ExitCode).IsNotEqualTo(0);
            await Assert.That(result.Output).Contains("adapter_publication_inventory_contract_missing");
        }
        foreach (ScriptResult result in new[] { projection, manifest }.Select(fixture => fixture.Run()))
        {
            await Assert.That(result.ExitCode).IsNotEqualTo(0);
            await Assert.That(result.Output).Contains("adapter_publication_projection_paths_invalid");
        }
    }

    [Test]
    public async Task ProviderAdapterScriptRequiresActualPublicationCommandsAndManifestRetention()
    {
        using var commentedCommands = ProviderFixture.CreateSingle("github");
        commentedCommands.MutatePublicationJob(job =>
        {
            var steps = (YamlSequenceNode)job.Children[new YamlScalarNode("steps")];
            var transport = steps.Children.Cast<YamlMappingNode>().Single(step =>
                step.Children.TryGetValue(new YamlScalarNode("id"), out YamlNode? id) && ((YamlScalarNode)id).Value == "transport");
            string program = ((YamlScalarNode)transport.Children[new YamlScalarNode("run")]).Value!;
            transport.Children[new YamlScalarNode("run")] = new YamlScalarNode(
                string.Join('\n', program.Split('\n').Select(line => "# " + line)));
        });
        using var missingManifest = ProviderFixture.CreateSingle("github");
        missingManifest.MutatePublicationWorkflow("github", yaml => yaml.Replace(
            "${{ github.workspace }}/.publication-state/generated-manifest.json",
            "${{ github.workspace }}/.publication-state/unrelated.json", StringComparison.Ordinal));
        using var optionalRetention = ProviderFixture.CreateSingle("github");
        optionalRetention.MutatePublicationWorkflow("github", yaml => yaml.Replace("if-no-files-found: error", "if-no-files-found: ignore", StringComparison.Ordinal));
        using var proseOnlyEnvironment = ProviderFixture.CreateSingle("github");
        proseOnlyEnvironment.MutatePublicationJob(job =>
        {
            job.Children.Remove(new YamlScalarNode("environment"));
            job.Children[new YamlScalarNode("name")] = new YamlScalarNode("environment: production");
        });

        ScriptResult commandsResult = commentedCommands.Run();
        await Assert.That(commandsResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(commandsResult.Output).Contains("adapter_publication_inventory_contract_missing");
        foreach (ProviderFixture fixture in new[] { missingManifest, optionalRetention })
        {
            ScriptResult result = fixture.Run();
            await Assert.That(result.ExitCode).IsNotEqualTo(0);
            await Assert.That(result.Output).Contains("adapter_publication_manifest_retention_missing");
        }
        ScriptResult environmentResult = proseOnlyEnvironment.Run();
        await Assert.That(environmentResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(environmentResult.Output).Contains("adapter_final_environment_required");
    }

    [Test]
    public async Task ProviderAdapterScriptRejectsParsedPublicationTrustViolations()
    {
        using var inlineEvent = ProviderFixture.CreateSingle("github");
        inlineEvent.MutatePublicationJob(_ => { }, workflow =>
            workflow.Children[new YamlScalarNode("on")] = new YamlSequenceNode(new YamlScalarNode("workflow_dispatch"), new YamlScalarNode("pull_request_target")));
        using var candidateRef = ProviderFixture.CreateSingle("github");
        candidateRef.MutatePublicationJob(job => job.Children[new YamlScalarNode("if")] =
            new YamlScalarNode("${{ github.event_name == 'workflow_dispatch' }}"));
        using var sharedRunner = ProviderFixture.CreateSingle("github");
        sharedRunner.MutatePublicationJob(job =>
        {
            var runner = (YamlMappingNode)job.Children[new YamlScalarNode("runs-on")];
            runner.Children[new YamlScalarNode("group")] = new YamlScalarNode("candidate-builds");
        });
        using var candidateLauncher = ProviderFixture.CreateSingle("github");
        candidateLauncher.MutatePublicationJob(job =>
        {
            var environment = (YamlMappingNode)job.Children[new YamlScalarNode("env")];
            environment.Children[new YamlScalarNode("TRUSTED_LAUNCHER")] = new YamlScalarNode("${{ inputs.launcher }}");
        });

        foreach (ProviderFixture fixture in new[] { inlineEvent, candidateRef })
        {
            ScriptResult result = fixture.Run();
            await Assert.That(result.ExitCode).IsNotEqualTo(0);
            await Assert.That(result.Output).Contains("adapter_publication_untrusted_origin");
        }
        ScriptResult runnerResult = sharedRunner.Run();
        await Assert.That(runnerResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(runnerResult.Output).Contains("adapter_self_hosted_runner_required");
        ScriptResult launcherResult = candidateLauncher.Run();
        await Assert.That(launcherResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(launcherResult.Output).Contains("adapter_final_candidate_code_forbidden");
    }

    [Test]
    public async Task ProviderAdapterScriptRejectsUnknownAndMissingSchemaKeys()
    {
        using var unknown = ProviderFixture.CreateSingle("github");
        unknown.MutateProvider("github", json => json.Replace("\n  \"displayName\"", "\n  \"unexpected\": true,\n  \"displayName\"", StringComparison.Ordinal));
        using var missing = ProviderFixture.CreateSingle("github");
        missing.MutateProvider("github", json => json.Replace("\n  \"providerId\": \"github\",", string.Empty, StringComparison.Ordinal));

        ScriptResult unknownResult = unknown.Run();
        ScriptResult missingResult = missing.Run();

        await Assert.That(unknownResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(unknownResult.Output).Contains("adapter_definition_unknown_key");
        await Assert.That(missingResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(missingResult.Output).Contains("adapter_definition_missing_key");
    }

    [Test]
    public async Task ProviderAdapterScriptRejectsShortOidsAndChecksumDrift()
    {
        using var shortOid = ProviderFixture.CreateSingle("github");
        shortOid.WriteInputs(targetOid: "abc123");
        using var drift = ProviderFixture.CreateSingle("github");
        drift.WriteInputs(bundleSha256: new string('0', 64));

        ScriptResult shortResult = shortOid.Run();
        ScriptResult driftResult = drift.Run();

        await Assert.That(shortResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(shortResult.Output).Contains("adapter_input_oid_not_full");
        await Assert.That(driftResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(driftResult.Output).Contains("adapter_checksum_drift");
    }

    [Test]
    public async Task ProviderAdapterScriptRejectsSecretsInPreviewAndCandidateCodeInFinal()
    {
        using var previewSecret = ProviderFixture.CreateSingle("github");
        previewSecret.MutateProvider("github", json => json.Replace("\"secrets\": []", "\"secrets\": [\"TOKEN\"]", StringComparison.Ordinal));
        using var finalCandidate = ProviderFixture.CreateSingle("github");
        finalCandidate.MutateProvider("github", json => json.Replace("\"trustedCodeOnly\": true", "\"trustedCodeOnly\": false", StringComparison.Ordinal));

        ScriptResult secretResult = previewSecret.Run();
        ScriptResult candidateResult = finalCandidate.Run();

        await Assert.That(secretResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(secretResult.Output).Contains("adapter_preview_secrets_forbidden");
        await Assert.That(candidateResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(candidateResult.Output).Contains("adapter_final_candidate_code_forbidden");
    }

    [Test]
    public async Task ProviderAdapterScriptRejectsElevatedPreviewPermissions()
    {
        using var contentsWrite = ProviderFixture.CreateSingle("github");
        contentsWrite.MutateProvider("github", json => json.Replace("\"permissions\": [\"contents:read\"]", "\"permissions\": [\"contents:write\"]", StringComparison.Ordinal));
        using var idTokenWrite = ProviderFixture.CreateSingle("github");
        idTokenWrite.MutateProvider("github", json => json.Replace("\"permissions\": [\"contents:read\"]", "\"permissions\": [\"id-token:write\"]", StringComparison.Ordinal));

        ScriptResult contentsWriteResult = contentsWrite.Run();
        ScriptResult idTokenWriteResult = idTokenWrite.Run();

        await Assert.That(contentsWriteResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(contentsWriteResult.Output).Contains("adapter_preview_permission_forbidden");
        await Assert.That(idTokenWriteResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(idTokenWriteResult.Output).Contains("adapter_preview_permission_forbidden");
    }

    [Test]
    public async Task ProviderAdapterScriptRejectsMutableActionsAndMissingNoopCheck()
    {
        using var mutable = ProviderFixture.CreateSingle("github");
        mutable.MutateProvider("github", json => json.Replace("actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", "actions/checkout@v4", StringComparison.Ordinal));
        using var missingCheck = ProviderFixture.CreateSingle("github");
        missingCheck.MutateProvider("github", json => json.Replace("\n    \"requiredChecks\": [\"release-adapter-preview\"],", string.Empty, StringComparison.Ordinal));

        ScriptResult mutableResult = mutable.Run();
        ScriptResult missingCheckResult = missingCheck.Run();

        await Assert.That(mutableResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(mutableResult.Output).Contains("adapter_action_pin_mutable");
        await Assert.That(missingCheckResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(missingCheckResult.Output).Contains("adapter_required_check_missing");
    }

    [Test]
    public async Task ProviderAdapterScriptRejectsUnsupportedTangledProtectedAction()
    {
        using var fixture = ProviderFixture.CreateSingle("tangled");

        ScriptResult result = fixture.Run("--provider", "tangled", "--operation", "publish-release");

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Output).Contains("adapter_provider_action_unsupported");
    }

    [Test]
    public async Task ProviderAdapterScriptRequiresExternalEvidenceForTangledUnsupportedActions()
    {
        using var selfAsserted = ProviderFixture.CreateSingle("tangled");
        selfAsserted.MutateProvider("tangled", json => json.Replace("\"operatorEvidenceRequired\": true", "\"operatorEvidenceRequired\": false", StringComparison.Ordinal));
        using var withEvidence = ProviderFixture.CreateSingle("tangled");
        string evidencePath = withEvidence.WriteExternalControlEvidence("releasePublication", "publish-release");

        ScriptResult selfAssertedResult = selfAsserted.Run("--provider", "tangled", "--operation", "publish-release");
        ScriptResult withEvidenceResult = withEvidence.Run("--provider", "tangled", "--operation", "publish-release", "--external-control-evidence", evidencePath);

        await Assert.That(selfAssertedResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(selfAssertedResult.Output).Contains("adapter_provider_action_unsupported");
        await Assert.That(withEvidenceResult.ExitCode).IsEqualTo(0);
        await Assert.That(withEvidenceResult.Output).Contains("adapter_validation_passed: providers=1");
    }

    [Test]
    public async Task ProviderAdapterScriptRejectsPrOriginFinalEventsForAllProviders()
    {
        using var github = ProviderFixture.CreateSingle("github");
        github.MutateFinalEvent("github", "pull_request_target");
        using var forgejo = ProviderFixture.CreateSingle("forgejo-codeberg");
        forgejo.MutateFinalEvent("forgejo-codeberg", "pull_request_target");
        using var tangled = ProviderFixture.CreateSingle("tangled");
        tangled.MutateFinalEvent("tangled", "pull_request");

        ScriptResult githubResult = github.Run();
        ScriptResult forgejoResult = forgejo.Run();
        ScriptResult tangledResult = tangled.Run();

        await Assert.That(githubResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(githubResult.Output).Contains("adapter_final_event_forbidden");
        await Assert.That(forgejoResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(forgejoResult.Output).Contains("adapter_final_event_forbidden");
        await Assert.That(tangledResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(tangledResult.Output).Contains("adapter_final_event_forbidden");
    }

    [Test]
    public async Task ProviderAdapterScriptRejectsUnknownAndMixedFinalEventVariants()
    {
        using var unknown = ProviderFixture.CreateSingle("github");
        unknown.MutateFinalEvent("github", "workflow-dispatch");
        using var mixedCase = ProviderFixture.CreateSingle("github");
        mixedCase.MutateFinalEvent("github", "Workflow_Dispatch");
        using var trailingSpace = ProviderFixture.CreateSingle("forgejo-codeberg");
        trailingSpace.MutateFinalEvent("forgejo-codeberg", "workflow_dispatch ");

        ScriptResult unknownResult = unknown.Run();
        ScriptResult mixedCaseResult = mixedCase.Run();
        ScriptResult trailingSpaceResult = trailingSpace.Run();

        await Assert.That(unknownResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(unknownResult.Output).Contains("adapter_final_event_forbidden");
        await Assert.That(mixedCaseResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(mixedCaseResult.Output).Contains("adapter_final_event_forbidden");
        await Assert.That(trailingSpaceResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(trailingSpaceResult.Output).Contains("adapter_final_event_forbidden");
    }

    [Test]
    public async Task ProviderAdapterScriptRejectsPathAliasesLinksDirtyWorktreeAndMisleadingSuccess()
    {
        using var alias = ProviderFixture.CreateSingle("github");
        Directory.CreateDirectory(Path.Combine(alias.ProvidersRoot, "GitHub"));
        File.Copy(alias.ProviderPath("github"), Path.Combine(alias.ProvidersRoot, "GitHub", "provider-definition.v1.json"));
        using var link = ProviderFixture.CreateSingle("github");
        File.Delete(link.ProviderPath("github"));
        File.CreateSymbolicLink(link.ProviderPath("github"), link.InputPath);
        using var dirty = ProviderFixture.CreateSingle("github");
        dirty.WriteInputs(dirtyWorktree: true);
        using var misleading = ProviderFixture.CreateSingle("github");
        misleading.MutateProvider("github", json => json.Replace("\"misleadingSuccessForbidden\": true", "\"misleadingSuccessForbidden\": false", StringComparison.Ordinal));

        ScriptResult aliasResult = alias.Run();
        ScriptResult linkResult = link.Run();
        ScriptResult dirtyResult = dirty.Run();
        ScriptResult misleadingResult = misleading.Run();

        await Assert.That(aliasResult.Output).Contains("adapter_path_alias");
        await Assert.That(linkResult.Output).Contains("adapter_path_alias");
        await Assert.That(dirtyResult.Output).Contains("adapter_dirty_worktree");
        await Assert.That(misleadingResult.Output).Contains("adapter_misleading_success_forbidden");
    }

    [Test]
    public async Task ProviderAdapterScriptRejectsHardlinkedInputAndPromotedBundle()
    {
        using var inputHardlink = ProviderFixture.CreateSingle("github");
        inputHardlink.ReplaceInputWithHardlink();
        using var bundleHardlink = ProviderFixture.CreateSingle("github");
        bundleHardlink.ReplaceBundleWithHardlink();

        ScriptResult inputResult = inputHardlink.Run();
        ScriptResult bundleResult = bundleHardlink.Run();

        await Assert.That(inputResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(inputResult.Output).Contains("adapter_path_alias");
        await Assert.That(bundleResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(bundleResult.Output).Contains("adapter_path_alias");
    }

    [Test]
    public async Task GitHubFinalDiscoveryChecksOutTrustedDefaultBranchNotEventSha()
    {
        string workflow = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "release-adapter-final.yml"));

        await Assert.That(workflow).DoesNotContain("ref: ${{ github.sha }}");
        await Assert.That(workflow).Contains("ref: ${{ github.event.repository.default_branch }}");
        await Assert.That(workflow).Contains("environment: production");
        await Assert.That(workflow).Contains("permissions:\n  contents: read");
    }

    [Test]
    public async Task ProviderAdapterScriptRejectsDiscoveryWorkflowParityDrift()
    {
        using var tangled = ProviderFixture.CreateSingle("tangled");
        tangled.MergeDiscoveryWorkflows("tangled");
        using var githubEnvironment = ProviderFixture.CreateSingle("github");
        githubEnvironment.MutateProvider("github", json => json.Replace("\"environmentApproval\": true", "\"environmentApproval\": false", StringComparison.Ordinal));
        using var githubAction = ProviderFixture.CreateSingle("github");
        githubAction.MutateProvider("github", json => json.Replace("\"actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68\"", "\"actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68\",\n    \"actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a\"", StringComparison.Ordinal));
        using var githubPreview = ProviderFixture.CreateSingle("github");
        githubPreview.AddWorkflowDispatchToPreview("github");

        ScriptResult tangledResult = tangled.Run();
        ScriptResult githubEnvironmentResult = githubEnvironment.Run();
        ScriptResult githubActionResult = githubAction.Run();
        ScriptResult githubPreviewResult = githubPreview.Run();

        await Assert.That(tangledResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(tangledResult.Output).Contains("adapter_discovery_workflow_mismatch");
        await Assert.That(githubEnvironmentResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(githubEnvironmentResult.Output).Contains("adapter_final_environment_required");
        await Assert.That(githubActionResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(githubActionResult.Output).Contains("adapter_action_manifest_mismatch");
        await Assert.That(githubPreviewResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(githubPreviewResult.Output).Contains("adapter_discovery_workflow_mismatch");
    }

    [Test]
    public async Task ProviderAdapterScriptRejectsNoCheckoutDiscoveryTrustDrift()
    {
        using var forgejoOverclaim = ProviderFixture.CreateSingle("forgejo-codeberg");
        forgejoOverclaim.MutateProvider("forgejo-codeberg", json => json.Replace("\"trustedRef\": \"no-checkout-discovery\"", "\"trustedRef\": \"default-branch\"", StringComparison.Ordinal));
        using var forgejoCheckout = ProviderFixture.CreateSingle("forgejo-codeberg");
        forgejoCheckout.MutateFinalWorkflow("forgejo-codeberg", "      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1\n");
        using var forgejoCandidateRef = ProviderFixture.CreateSingle("forgejo-codeberg");
        forgejoCandidateRef.MutateFinalWorkflow("forgejo-codeberg", "      - run: printf '%s\\n' '${{ github.sha }}'\n");
        using var tangledExternalCommand = ProviderFixture.CreateSingle("tangled");
        tangledExternalCommand.MutateFinalWorkflow("tangled", "      - run: curl https://example.invalid/release.sh\n");
        using var tangledNonliteral = ProviderFixture.CreateSingle("tangled");
        tangledNonliteral.MutateFinalWorkflow("tangled", "      - run: printf '%s\\n' '${{ github.event.repository.default_branch }}'\n");

        ScriptResult forgejoOverclaimResult = forgejoOverclaim.Run();
        ScriptResult forgejoCheckoutResult = forgejoCheckout.Run();
        ScriptResult forgejoCandidateRefResult = forgejoCandidateRef.Run();
        ScriptResult tangledExternalCommandResult = tangledExternalCommand.Run();
        ScriptResult tangledNonliteralResult = tangledNonliteral.Run();

        await Assert.That(forgejoOverclaimResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(forgejoOverclaimResult.Output).Contains("adapter_final_trusted_ref_invalid");
        await Assert.That(forgejoCheckoutResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(forgejoCheckoutResult.Output).Contains("adapter_no_checkout_discovery_invalid");
        await Assert.That(forgejoCandidateRefResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(forgejoCandidateRefResult.Output).Contains("adapter_no_checkout_discovery_invalid");
        await Assert.That(tangledExternalCommandResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(tangledExternalCommandResult.Output).Contains("adapter_no_checkout_discovery_invalid");
        await Assert.That(tangledNonliteralResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(tangledNonliteralResult.Output).Contains("adapter_no_checkout_discovery_invalid");
    }

    [Test]
    public async Task ProviderAdapterScriptEmitsPlansForAllThreeProvidersWithIdenticalNormalizedChecksums()
    {
        using var fixture = ProviderFixture.CreateAll();

        ScriptResult result = fixture.Run();

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output).Contains("adapter_validation_passed: providers=3");
        foreach (string provider in new[] { "forgejo-codeberg", "tangled", "github" })
        {
            string planPath = Path.Combine(fixture.OutputRoot, provider + ".transport-plan.v1.json");
            await Assert.That(File.Exists(planPath)).IsTrue();
            using JsonDocument plan = JsonDocument.Parse(File.ReadAllBytes(planPath));
            JsonElement root = plan.RootElement;
            await Assert.That(root.GetProperty("providerId").GetString()).IsEqualTo(provider);
            await Assert.That(root.GetProperty("authoritativeChecksums").GetRawText()).IsEqualTo(fixture.ExpectedChecksumJson);
            await Assert.That(root.GetProperty("transportOnly").GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty("metadataAuthoritative").GetBoolean()).IsFalse();
        }
    }

    private sealed class ProviderFixture : IDisposable
    {
        private ProviderFixture(IEnumerable<string> providers)
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-provider-adapter-{Guid.NewGuid():N}");
            ProvidersRoot = Path.Combine(Root, "providers");
            BundleRoot = Path.Combine(Root, "bundle");
            OutputRoot = Path.Combine(Root, "plans");
            InputPath = Path.Combine(Root, "release-inputs.json");
            Directory.CreateDirectory(ProvidersRoot);
            Directory.CreateDirectory(BundleRoot);
            File.WriteAllText(Path.Combine(BundleRoot, "promoted-bundle.tar"), "normalized bundle bytes\n");
            BundleSha256 = Sha256(File.ReadAllBytes(Path.Combine(BundleRoot, "promoted-bundle.tar")));
            WriteInputs();
            foreach (string provider in providers) WriteProvider(provider);
        }

        public string Root { get; }
        public string ProvidersRoot { get; }
        public string BundleRoot { get; }
        public string OutputRoot { get; }
        public string InputPath { get; }
        public string BundleSha256 { get; }
        public string ExpectedChecksumJson => "{\"promotedBundleSha256\":\"" + BundleSha256 + "\",\"releaseInputsSha256\":\"" + Sha256(File.ReadAllBytes(InputPath)) + "\"}";

        public static ProviderFixture CreateSingle(string provider) => new([provider]);
        public static ProviderFixture CreateAll() => new(["forgejo-codeberg", "tangled", "github"]);

        public void WriteInputs(string? targetOid = null, string? bundleSha256 = null, bool dirtyWorktree = false)
        {
            string oid = targetOid ?? new string('a', 40);
            File.WriteAllText(InputPath, $$"""
            {
              "schemaVersion": "release-adapter-inputs.v1",
              "targetOid": "{{oid}}",
              "expectedOldProtectedRefOid": "{{new string('b', 40)}}",
              "tagObjectId": "{{new string('c', 40)}}",
              "tagName": "v1.1.0",
              "releaseBundlePath": "promoted-bundle.tar",
              "releaseBundleSha256": "{{bundleSha256 ?? BundleSha256}}",
              "artifactManifestSha256": "{{new string('d', 64)}}",
              "dirtyWorktree": {{dirtyWorktree.ToString().ToLowerInvariant()}}
            }
            """);
        }

        public string ProviderPath(string provider) => Path.Combine(ProvidersRoot, provider, "provider-definition.v1.json");

        public string WriteExternalControlEvidence(string unsupportedCapability, string operation)
        {
            string path = Path.Combine(Root, "external-control-evidence.json");
            File.WriteAllText(path, $$"""
            {
              "schemaVersion": "release-adapter-external-control-evidence.v1",
              "providerId": "tangled",
              "operation": "{{operation}}",
              "unsupportedCapability": "{{unsupportedCapability}}",
              "approved": true
            }
            """);
            return path;
        }

        public void ReplaceInputWithHardlink()
        {
            string original = Path.Combine(Root, "original-input.json");
            File.Move(InputPath, original);
            CreateHardLink(InputPath, original);
        }

        public void ReplaceBundleWithHardlink()
        {
            string bundle = Path.Combine(BundleRoot, "promoted-bundle.tar");
            string original = Path.Combine(BundleRoot, "original-promoted-bundle.tar");
            File.Move(bundle, original);
            CreateHardLink(bundle, original);
        }

        public void MutateProvider(string provider, Func<string, string> mutate) => File.WriteAllText(ProviderPath(provider), mutate(File.ReadAllText(ProviderPath(provider))));

        public void MutatePublicationWorkflow(string provider, Func<string, string> mutate)
        {
            string path = Path.Combine(ProvidersRoot, provider, "release-publish.yml");
            string original = File.ReadAllText(path);
            string mutated = mutate(original);
            if (mutated == original) throw new InvalidOperationException("publication workflow mutation did not change its input");
            File.WriteAllText(path, mutated);
        }

        public void MutatePublicationJob(Action<YamlMappingNode> mutate, Action<YamlMappingNode>? mutateWorkflow = null) =>
            MutatePublicationWorkflow("github", yaml =>
            {
                var stream = new YamlStream();
                using var reader = new StringReader(yaml);
                stream.Load(reader);
                var workflow = (YamlMappingNode)stream.Documents[0].RootNode;
                var jobs = (YamlMappingNode)workflow.Children[new YamlScalarNode("jobs")];
                mutate((YamlMappingNode)jobs.Children[new YamlScalarNode("release-publish")]);
                mutateWorkflow?.Invoke(workflow);
                using var writer = new StringWriter();
                stream.Save(writer, assignAnchors: false);
                return writer.ToString();
            });

        public void MutateFinalEvent(string provider, string finalEvent) => MutateProvider(provider, json => json
            .Replace("\"event\": \"workflow_dispatch\",\n    \"trustedRef\": \"default-branch\"", $"\"event\": \"{finalEvent}\",\n    \"trustedRef\": \"default-branch\"", StringComparison.Ordinal)
            .Replace("\"event\": \"workflow_dispatch\",\n    \"trustedRef\": \"no-checkout-discovery\"", $"\"event\": \"{finalEvent}\",\n    \"trustedRef\": \"no-checkout-discovery\"", StringComparison.Ordinal)
            .Replace("\"event\": \"manual\",\n    \"trustedRef\": \"no-checkout-discovery\"", $"\"event\": \"{finalEvent}\",\n    \"trustedRef\": \"no-checkout-discovery\"", StringComparison.Ordinal));

        public void MergeDiscoveryWorkflows(string provider)
        {
            string directory = Path.Combine(ProvidersRoot, provider);
            File.WriteAllText(Path.Combine(directory, "release-adapter-preview.yml"), "name: merged\non:\n  push:\n  pull_request:\n  manual:\njobs:\n  release-adapter-preview:\n    steps:\n      - run: echo preview\n  release-adapter-final:\n    steps:\n      - run: echo final\n");
            File.WriteAllText(Path.Combine(directory, "release-adapter-final.yml"), "name: final\non:\n  manual:\njobs:\n  release-adapter-final:\n    steps:\n      - run: echo final\n");
        }

        public void AddWorkflowDispatchToPreview(string provider)
        {
            string path = Path.Combine(ProvidersRoot, provider, "release-adapter-preview.yml");
            File.WriteAllText(path, File.ReadAllText(path).Replace("  pull_request:\n", "  pull_request:\n  workflow_dispatch:\n", StringComparison.Ordinal));
        }

        public void MutateFinalWorkflow(string provider, string steps)
        {
            string path = Path.Combine(ProvidersRoot, provider, "release-adapter-final.yml");
            File.WriteAllText(path, File.ReadAllText(path).Replace("      - run: printf '%s\\n' 'release-adapter-final: transport-only no-checkout discovery'\n", steps, StringComparison.Ordinal));
        }

        public ScriptResult Run(params string[] extraArguments)
        {
            string repoRoot = FindRepositoryRoot();
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = repoRoot,
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add(".ci/scripts/validate-release-provider-adapters.cs");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("--providers");
            startInfo.ArgumentList.Add(ProvidersRoot);
            startInfo.ArgumentList.Add("--inputs");
            startInfo.ArgumentList.Add(InputPath);
            startInfo.ArgumentList.Add("--bundle-root");
            startInfo.ArgumentList.Add(BundleRoot);
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(OutputRoot);
            foreach (string argument in extraArguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("dotnet failed to start");
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException(output);
            }

            return new ScriptResult(process.ExitCode, output);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private void WriteProvider(string provider)
        {
            Directory.CreateDirectory(Path.Combine(ProvidersRoot, provider));
            bool tangled = provider == "tangled";
            bool forgejo = provider == "forgejo-codeberg";
            string finalEvent = tangled ? "manual" : "workflow_dispatch";
            string trustedRef = provider == "github" ? "default-branch" : "no-checkout-discovery";
            string actions = provider == "github"
                ? "\"actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1\",\n    \"actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68\""
                : string.Empty;
            File.WriteAllText(ProviderPath(provider), $$"""
            {
              "schemaVersion": "release-provider.v1",
              "providerId": "{{provider}}",
              "displayName": "{{provider}}",
              "discoveryWorkflows": ["release-adapter-preview.yml", "release-adapter-final.yml"],
              "publicationWorkflows": ["release-publish.yml"],
              "actions": [{{actions}}],
              "previewLane": {
                "event": "pull_request",
                "trustedCodeOnly": false,
                "secrets": [],
                "permissions": ["contents:read"],
                "requiredChecks": ["release-adapter-preview"],
                "alwaysPresentNoop": true
              },
              "finalLane": {
                "event": "{{finalEvent}}",
                "trustedRef": "{{trustedRef}}",
                "trustedCodeOnly": true,
                "environmentApproval": {{(!tangled).ToString().ToLowerInvariant()}},
                "requiresSelfHostedTrustedRunner": {{forgejo.ToString().ToLowerInvariant()}},
                "requiredChecks": ["release-adapter-final"],
                "alwaysPresentNoop": true,
                "candidateStopsBeforeFinal": true
              },
              "capabilities": {
                "artifacts": true,
                "retentionDays": {{(tangled ? 30 : 90)}},
                "protectedRefCas": {{(!tangled).ToString().ToLowerInvariant()}},
                "releasePublication": {{(!tangled).ToString().ToLowerInvariant()}},
                "operatorEvidenceRequired": {{tangled.ToString().ToLowerInvariant()}}
              },
              "guards": {
                "immutableBundleVerification": true,
                "providerNeutralChecksumEquality": true,
                "metadataAuthoritative": false,
                "misleadingSuccessForbidden": true
              },
              "diagnostics": ["adapter_validation_passed"]
            }
            """);
            WriteDiscoveryWorkflows(provider, finalEvent);
            WritePublicationWorkflow(provider, tangled);
        }

        private void WritePublicationWorkflow(string provider, bool unsupportedPublication)
        {
            if (provider == "github")
            {
                File.Copy(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "release-publish.yml"),
                    Path.Combine(ProvidersRoot, provider, "release-publish.yml"));
                return;
            }
            string environment = provider == "tangled" ? string.Empty : "    environment: production\n";
            string noop = unsupportedPublication
                ? "      - run: |\n          printf '%s\\n' 'release-publish: recorded-no-op, provider has no documented release publication API'\n"
                : string.Empty;
            File.WriteAllText(
                Path.Combine(ProvidersRoot, provider, "release-publish.yml"),
                "name: publish\non:\n  workflow_dispatch:\njobs:\n  release-publish:\n" + environment + "    steps:\n" + noop +
                "      - run: |\n          printf '%s\\n' 'page-header: release-notes-sha256 and tag-reference'\n" +
                "      - run: |\n          printf '%s\\n' 'assets: release-evidence.v1.json artifacts.sha256 container-image-digests sbom'\n");
        }

        private void WriteDiscoveryWorkflows(string provider, string finalEvent)
        {
            string directory = Path.Combine(ProvidersRoot, provider);
            string finalIf = provider == "tangled" ? "manual" : "workflow_dispatch";
            string finalEnvironment = provider == "tangled" ? string.Empty : "    environment: production\n";
            string finalActions = provider == "github"
                ? "      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1\n      - uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68\n"
                : string.Empty;
            string finalCondition = provider == "github" ? $"    if: ${{{{ github.event_name == '{finalIf}' }}}}\n" : string.Empty;
            string finalRun = provider == "github" ? "echo 'ref: ${{ github.event.repository.default_branch }}'" : "printf '%s\\n' 'release-adapter-final: transport-only no-checkout discovery'";
            File.WriteAllText(Path.Combine(directory, "release-adapter-preview.yml"), "name: preview\non:\n  pull_request:\njobs:\n  release-adapter-preview:\n    if: ${{ github.event_name == 'pull_request' }}\n    steps:\n      - run: echo preview\n");
            File.WriteAllText(Path.Combine(directory, "release-adapter-final.yml"), $"name: final\non:\n  {finalEvent}:\njobs:\n  release-adapter-final:\n{finalCondition}{finalEnvironment}    steps:\n{finalActions}      - run: {finalRun}\n");
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, ".ci"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void CreateHardLink(string linkPath, string existingPath)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLinkW(linkPath, existingPath, IntPtr.Zero)) throw new InvalidOperationException($"CreateHardLinkW failed: {Marshal.GetLastWin32Error()}");
            return;
        }

        using var process = Process.Start(new ProcessStartInfo("/usr/bin/ln")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList = { existingPath, linkPath },
        }) ?? throw new InvalidOperationException("ln failed to start");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(output);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    private sealed record ScriptResult(int ExitCode, string Output);
}
