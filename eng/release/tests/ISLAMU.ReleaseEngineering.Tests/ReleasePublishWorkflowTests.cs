using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel("RuntimePromotionTrustRoot")]
public sealed class ReleasePublishWorkflowTests
{
    [Test]
    public async Task DispatchesShareOneNonCancellingPublicationQueue()
    {
        YamlMappingNode workflow = LoadWorkflow();
        var triggers = Mapping(workflow, "on");
        await Assert.That(triggers.Children.Count).IsEqualTo(1);
        await Assert.That(triggers.Children.ContainsKey(new YamlScalarNode("workflow_dispatch"))).IsTrue();
        var concurrency = Mapping(workflow, "concurrency");
        await Assert.That(Text(concurrency, "group")).IsEqualTo("release-public-changelog");
        await Assert.That(Text(concurrency, "cancel-in-progress")).IsEqualTo("false");
        var inputs = Mapping(Mapping(triggers, "workflow_dispatch"), "inputs");
        var modes = (YamlSequenceNode)Mapping(inputs, "mode").Children[new YamlScalarNode("options")];
        await Assert.That(modes.Children.Select(value => ((YamlScalarNode)value).Value!).ToArray())
            .IsEquivalentTo(new[] { "propose", "validate", "reconcile" });
        await Assert.That(Text(Mapping(inputs, "proposal_head"), "type")).IsEqualTo("string");
    }

    [Test]
    public async Task PrivilegedJobRequiresDefaultBranchAndProtectedEnvironment()
    {
        YamlMappingNode job = Job();
        await Assert.That(Text(job, "if")).IsEqualTo(
            "${{ github.event_name == 'workflow_dispatch' && github.ref == format('refs/heads/{0}', github.event.repository.default_branch) }}");
        await Assert.That(Text(job, "environment")).IsEqualTo("production");
        var permissions = Mapping(job, "permissions");
        await Assert.That(permissions.Children.Keys.Select(key => ((YamlScalarNode)key).Value!).ToArray())
            .IsEquivalentTo(new[] { "contents", "pull-requests", "statuses", "checks" });
        await Assert.That(Text(permissions, "contents")).IsEqualTo("write");
        await Assert.That(Text(permissions, "pull-requests")).IsEqualTo("write");
        await Assert.That(Text(permissions, "statuses")).IsEqualTo("write");
        await Assert.That(Text(permissions, "checks")).IsEqualTo("read");
    }

    [Test]
    public async Task WorkflowDeadlineAccommodatesAllProjectionAttempts()
    {
        int timeoutMinutes = int.Parse(Text(Job(), "timeout-minutes"), System.Globalization.CultureInfo.InvariantCulture);
        TimeSpan maximumProjectionTime = PublicationInventoryVerificationBudget.PromotedProcessTimeout * 6;

        await Assert.That(TimeSpan.FromMinutes(timeoutMinutes)).IsGreaterThan(maximumProjectionTime);
    }

    [Test]
    public async Task ExternalActionsArePinnedAndCheckoutNeverPersistsCredentials()
    {
        foreach (YamlMappingNode step in Steps().Where(step => step.Children.ContainsKey(new YamlScalarNode("uses"))))
        {
            YamlNode node = step.Children[new YamlScalarNode("uses")];
            string action = ((YamlScalarNode)node).Value!;
            string[] pieces = action.Split('@');
            await Assert.That(pieces.Length).IsEqualTo(2);
            await Assert.That(pieces[1].Length).IsEqualTo(40);
            await Assert.That(pieces[1].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')).IsTrue();
            if (!pieces[0].Equals("actions/checkout", StringComparison.Ordinal)) continue;
            var options = Mapping(step, "with");
            await Assert.That(Text(options, "ref")).IsEqualTo("${{ github.event.repository.default_branch }}");
            await Assert.That(Text(options, "persist-credentials")).IsEqualTo("false");
            await Assert.That(Text(options, "fetch-depth")).IsEqualTo("0");
        }
    }

    [Test]
    public async Task EveryExecutedBashProgramHasValidSyntax()
    {
        int checkedPrograms = 0;
        foreach (YamlMappingNode step in Steps().Where(step => step.Children.ContainsKey(new YamlScalarNode("run"))))
        {
            YamlNode node = step.Children[new YamlScalarNode("run")];
            ProcessResult result = await RunBash(((YamlScalarNode)node).Value!, syntaxOnly: true);
            await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
            checkedPrograms++;
        }
        await Assert.That(checkedPrograms).IsGreaterThanOrEqualTo(4);
    }

    [Test]
    public async Task ProposalAndReconciliationAreSeparateExplicitDispatchOperations()
    {
        await Assert.That(Text(Step("propose"), "if")).IsEqualTo("${{ inputs.mode == 'propose' }}");
        await Assert.That(Text(Step("validate"), "if")).IsEqualTo("${{ inputs.mode == 'validate' }}");
        await Assert.That(Text(Step("reconcile"), "if")).IsEqualTo("${{ inputs.mode == 'reconcile' }}");
        YamlMappingNode retained = Step("retain");
        await Assert.That(Text(retained, "if")).IsEqualTo("${{ always() }}");
        await Assert.That(Text(Mapping(retained, "with"), "if-no-files-found")).IsEqualTo("error");
    }

    [Test]
    public async Task UnmergedProposalCannotAdvanceTheMirror()
    {
        using var fixture = await TransportFixture.Create();
        fixture.SetPullRequest(merged: false);
        ProcessResult result = await fixture.Reconcile();
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(fixture.ReceiptState()).IsEqualTo("pending");
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Initial);
        await Assert.That(File.Exists(fixture.StatusesPath)).IsFalse();
    }

    [Test]
    public async Task AcceptedMergeWithoutGitBookEvidenceRemainsPendingAndRepeatsWithoutCommits()
    {
        using var fixture = await TransportFixture.Create();
        ProcessResult first = await fixture.Reconcile();
        ProcessResult second = await fixture.Reconcile();
        await Assert.That(first.ExitCode).IsEqualTo(0).Because(first.Error);
        await Assert.That(second.ExitCode).IsEqualTo(0).Because(second.Error);
        await Assert.That(fixture.ReceiptState()).IsEqualTo("pending");
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Accepted);
        await Assert.That(await fixture.RemoteOid("docs/publication")).IsEqualTo(fixture.Accepted);
        await Assert.That(File.ReadAllLines(fixture.StatusesPath)).IsEquivalentTo(new[] { "pending", "pending" });
    }

    [Test]
    public async Task OnlyObservedAppSuccessForTheExactMirrorCommitIsDelivered()
    {
        using var fixture = await TransportFixture.Create();
        fixture.SetChecks(fixture.Accepted, app: 42, conclusion: "success");
        ProcessResult result = await fixture.Reconcile();
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(fixture.ReceiptState()).IsEqualTo("delivered");
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Accepted);
        await Assert.That(File.ReadAllLines(fixture.StatusesPath)).IsEquivalentTo(new[] { "pending", "success" });
    }

    [Test]
    [Arguments("wrong-app")]
    [Arguments("wrong-commit")]
    [Arguments("wrong-branch")]
    [Arguments("newer-failure")]
    public async Task UnrelatedOrSupersededSuccessfulChecksCannotClaimDelivery(string scenario)
    {
        using var fixture = await TransportFixture.Create();
        fixture.SetChecks(
            scenario == "wrong-commit" ? fixture.Initial : fixture.Accepted,
            scenario == "wrong-app" ? 99 : 42, "success",
            scenario == "wrong-branch" ? "docs/publication" : "docs/gitbook-sync",
            newerFailure: scenario == "newer-failure");
        ProcessResult result = await fixture.Reconcile();
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(fixture.ReceiptState()).IsEqualTo("pending");
        await Assert.That(File.ReadAllLines(fixture.StatusesPath)).IsEquivalentTo(new[] { "pending" });
    }

    [Test]
    public async Task MirrorWriteBackIsPreservedInsteadOfSilentlyOverwritten()
    {
        using var fixture = await TransportFixture.Create();
        ProcessResult mutation = await fixture.Git("""
            git switch docs/gitbook-sync
            printf '%s\n' 'GitBook write-back' > writeback.txt
            git add writeback.txt
            git commit -m 'Record mirror write-back'
            git push origin docs/gitbook-sync
            """);
        await Assert.That(mutation.ExitCode).IsEqualTo(0).Because(mutation.Error);
        string drift = await fixture.RemoteOid("docs/gitbook-sync");
        ProcessResult result = await fixture.Reconcile();
        await Assert.That(result.ExitCode).IsEqualTo(1).Because(result.Error);
        await Assert.That(fixture.ReceiptState()).IsEqualTo("drift");
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(drift);
        await Assert.That(await fixture.RemoteOid("docs/publication")).IsEqualTo(fixture.Accepted);
        await Assert.That(File.Exists(fixture.StatusesPath)).IsFalse();
    }

    [Test]
    public async Task TamperedAcceptedPageCannotReachTheMirror()
    {
        using var fixture = await TransportFixture.Create();
        ProcessResult mutation = await fixture.Git("""
            git switch docs/publication
            printf '%s\n' 'Unmanifested page' > docs/public/changelog/README.md
            git add docs/public/changelog/README.md
            git commit -m 'Record invalid accepted projection'
            git push origin docs/publication
            """);
        await Assert.That(mutation.ExitCode).IsEqualTo(0).Because(mutation.Error);
        ProcessResult result = await fixture.Reconcile();
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Initial);
        await Assert.That(File.Exists(fixture.StatusesPath)).IsFalse();
    }

    [Test]
    public async Task ConcurrentMirrorWriteBackRejectsThePushAndSurvivesReconciliation()
    {
        using var fixture = await TransportFixture.Create();
        ProcessResult mutation = await fixture.Git("""
            set -euo pipefail
            git switch -c mirror-race docs/gitbook-sync
            printf '%s\n' 'Concurrent GitBook write-back' > writeback.txt
            git add writeback.txt
            git commit -m 'Concurrent mirror writer'
            git push origin mirror-race
            """);
        await Assert.That(mutation.ExitCode).IsEqualTo(0).Because(mutation.Error);
        string race = await fixture.RemoteOid("mirror-race");
        // Inject the external writer at the exact boundary after the snapshot and before push.
        // All reads and pushes still use real Git; its non-fast-forward rejection must protect it.
        const string concurrentWriter = """
            git() {
              local argument
              for argument in "$@"; do
                if [[ "$argument" == push && ! -f "$FIXTURE/race-injected" ]]; then
                  command git --git-dir=../remote.git update-ref refs/heads/docs/gitbook-sync refs/heads/mirror-race
                  touch "$FIXTURE/race-injected"
                  break
                fi
              done
              command git "$@"
            }

            """;
        ProcessResult result = await fixture.Execute("reconcile", concurrentWriter);
        await Assert.That(result.ExitCode).IsEqualTo(1).Because(result.Error);
        await Assert.That(fixture.ReceiptState()).IsEqualTo("drift");
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(race);
        await Assert.That(await fixture.RemoteOid("docs/publication")).IsEqualTo(fixture.Accepted);
        await Assert.That(File.Exists(fixture.StatusesPath)).IsFalse();
    }

    [Test]
    public async Task UnprotectedAcceptanceCannotPassTheActivationGate()
    {
        using var fixture = await TransportFixture.Create();
        File.WriteAllText(Path.Combine(fixture.Root, "branch.json"), """{"protected":false}""");
        ProcessResult result = await fixture.Execute("propose");
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Initial);
    }

    [Test]
    public async Task MissingRetainedAuthorityFailsInsteadOfReportingSuccessfulNoOp()
    {
        using var fixture = await TransportFixture.Create();
        await fixture.RemoveInventorySignature();
        ProcessResult result = await fixture.Execute("propose");
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(await fixture.RemoteOid("docs/publication")).IsEqualTo(fixture.Accepted);
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Initial);
        await Assert.That(File.Exists(fixture.StatusesPath)).IsFalse();
    }

    [Test]
    public async Task BundleAuthorityPinsComeFromEnvironmentConfiguration()
    {
        YamlMappingNode environment = Mapping(Job(), "env");
        foreach (string key in new[]
        {
            "PROMOTION_PRINCIPAL", "BUNDLE_ID", "BUNDLE_VERSION", "POLICY_VERSION",
            "CONFIG_VERSION", "TRUST_VERSION", "MANIFEST_SHA256",
        })
        {
            await Assert.That(Text(environment, $"ISLAMU_RELEASE_{key}")).IsEqualTo($"${{{{ vars.RELEASE_{key} }}}}");
        }
    }

    [Test]
    public async Task SignedRuntimeProposesValidatesAndDeliversWithDurableIdentityReceipts()
    {
        using var fixture = await TransportFixture.Create(accepted: false);
        string proposed = fixture.ProposalHead;
        ProcessResult repeated = await fixture.Execute("propose");
        await Assert.That(repeated.ExitCode).IsEqualTo(0).Because(repeated.Output + repeated.Error);
        await Assert.That(await fixture.RemoteOid(fixture.ProposalBranch)).IsEqualTo(proposed);
        await Assert.That(await fixture.RemoteOid("docs/publication")).IsEqualTo(fixture.Initial);
        ProcessResult validation = await fixture.Execute("validate");
        await Assert.That(validation.ExitCode).IsEqualTo(0).Because(validation.Output + validation.Error);
        JsonNode[] statuses = fixture.StatusRecords();
        JsonNode validationStatus = statuses.Last(value => value["context"]!.GetValue<string>() == "release-publication/validation");
        await Assert.That(validationStatus["sha"]!.GetValue<string>()).IsEqualTo(proposed);
        await Assert.That(validationStatus["state"]!.GetValue<string>()).IsEqualTo("success");
        await fixture.AcceptProposal();
        fixture.SetChecks(fixture.Accepted, 42, "success");
        ProcessResult delivery = await fixture.Reconcile();
        await Assert.That(delivery.ExitCode).IsEqualTo(0).Because(delivery.Output + delivery.Error);
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Accepted);
        JsonNode[] receipts = await fixture.DurableReceipts();
        JsonNode delivered = receipts.Single(value => value["state"]!.GetValue<string>() == "delivered");
        await Assert.That(delivered["proposalCommit"]!.GetValue<string>()).IsEqualTo(proposed);
        await Assert.That(delivered["acceptedCommit"]!.GetValue<string>()).IsEqualTo(fixture.Accepted);
        await Assert.That(delivered["sourceCommit"]!.GetValue<string>()).IsEqualTo(await fixture.RemoteOid("develop"));
        await Assert.That(delivered["releases"]!.AsArray().Count).IsEqualTo(1);
        await Assert.That(delivered["releases"]![0]!["version"]!.GetValue<string>()).IsEqualTo("1.1.0");
        await Assert.That(delivered["releases"]![0]!["tagObjectId"]!.GetValue<string>()).IsEqualTo(fixture.FirstTagObject);
        await Assert.That(delivered["inputSetSha256"]!.GetValue<string>().Length).IsEqualTo(64);
        await Assert.That(delivered["projectionSha256"]!.GetValue<string>().Length).IsEqualTo(64);
        await Assert.That(delivered["gitbookCheck"]!["id"]!.GetValue<int>()).IsEqualTo(1);
        string ledger = await fixture.RemoteOid("docs/publication-receipts");
        ProcessResult repeatDelivery = await fixture.Reconcile();
        await Assert.That(repeatDelivery.ExitCode).IsEqualTo(0).Because(repeatDelivery.Output + repeatDelivery.Error);
        await Assert.That(await fixture.RemoteOid("docs/publication-receipts")).IsEqualTo(ledger);
    }

    [Test]
    public async Task ClosedUnmergedProposalIsReplacedByAnOpenProposal()
    {
        using var fixture = await TransportFixture.Create(accepted: false);
        string originalHead = fixture.ProposalHead;
        string pullPath = Path.Combine(fixture.Root, "pull.json");
        JsonNode closed = JsonNode.Parse(File.ReadAllText(pullPath))!;
        closed["state"] = "closed";
        File.WriteAllText(pullPath, closed.ToJsonString());

        ProcessResult result = await fixture.Execute("propose");

        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Output + result.Error);
        JsonNode reopened = JsonNode.Parse(File.ReadAllText(pullPath))!;
        await Assert.That(reopened["state"]!.GetValue<string>()).IsEqualTo("open");
        await Assert.That(reopened["merged"]!.GetValue<bool>()).IsFalse();
        await Assert.That(await fixture.RemoteOid(fixture.ProposalBranch)).IsEqualTo(originalHead);
        await Assert.That((await fixture.Execute("validate")).ExitCode).IsEqualTo(0);
    }

    [Test]
    [Arguments("")]
    [Arguments("false")]
    public async Task MissingOrDisabledActivationFailsExplicitly(string enabled)
    {
        using var fixture = await TransportFixture.Create(accepted: false);
        fixture.Overrides["PUBLICATION_ENABLED"] = enabled;
        ProcessResult result = await fixture.Execute("guard");
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(fixture.ReceiptState()).IsEqualTo("pending");
    }

    [Test]
    public async Task CheckoutLocalExecutableCannotBecomeTheTrustedLauncher()
    {
        using var fixture = await TransportFixture.Create(accepted: false);
        fixture.Overrides["TRUSTED_LAUNCHER"] = fixture.PlaceLauncherInCheckout();
        ProcessResult result = await fixture.Execute("guard");
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(File.Exists(fixture.StatusesPath)).IsFalse();
    }

    [Test]
    public async Task ModifiedPromotedBundleCannotValidateAProposal()
    {
        using var fixture = await TransportFixture.Create(accepted: false);
        File.AppendAllText(Path.Combine(fixture.Root, "bundle/policy/release-policy.yaml"), "tampered: true\n");
        ProcessResult result = await fixture.Execute("validate");
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(fixture.StatusRecords().Any(value => value["state"]!.GetValue<string>() == "success")).IsFalse();
    }

    [Test]
    public async Task ValidlyPromotedUnrunnableEngineCannotFallBackToLauncherCode()
    {
        using var fixture = await TransportFixture.Create(accepted: false);
        File.WriteAllText(Path.Combine(fixture.Root, "bundle/bin/ISLAMU.ReleaseEngineering.runtimeconfig.json"),
            """{"runtimeOptions":{"tfm":"net99.0","framework":{"name":"Microsoft.NETCore.App","version":"99.0.0"}}}""");
        await fixture.ResignBundle();
        ProcessResult result = await fixture.Execute("validate");
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(fixture.StatusRecords().Any(value => value["state"]!.GetValue<string>() == "success")).IsFalse();
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Initial);
    }

    [Test]
    [Arguments("")]
    [Arguments("old-head")]
    public async Task ValidationRequiresTheExactExplicitProposalHead(string head)
    {
        using var fixture = await TransportFixture.Create(accepted: false);
        fixture.Overrides["PROPOSAL_HEAD"] = head.Length == 0 ? "" : fixture.Initial;
        ProcessResult result = await fixture.Execute("validate");
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(File.Exists(fixture.StatusesPath)).IsFalse();
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Initial);
    }

    [Test]
    [Arguments("validate")]
    [Arguments("reconcile")]
    public async Task NewlyAuthorizedReleaseRejectsPreviouslyGeneratedUnion(string mode)
    {
        using var fixture = await TransportFixture.Create(accepted: mode == "reconcile");
        await fixture.PublishSecondRelease();
        ProcessResult result = await fixture.Execute(mode);
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Initial);
        await Assert.That(fixture.StatusRecords().Any(value => value["state"]!.GetValue<string>() == "success")).IsFalse();
        await Assert.That(fixture.ReceiptState()).IsEqualTo("pending");
    }

    [Test]
    public async Task ChangedAcceptanceBaseCannotReuseProposalValidation()
    {
        using var fixture = await TransportFixture.Create(accepted: false);
        ProcessResult mutation = await fixture.Git("""
            set -euo pipefail
            git switch docs/publication
            git commit --allow-empty -m 'Independent acceptance change'
            git push origin docs/publication
            """);
        await Assert.That(mutation.ExitCode).IsEqualTo(0).Because(mutation.Error);
        ProcessResult result = await fixture.Execute("validate");
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(fixture.StatusRecords().Any(value => value["state"]!.GetValue<string>() == "success")).IsFalse();
    }

    [Test]
    public async Task InventoryRaceAfterValidationReceiptCannotPostSuccess()
    {
        using var fixture = await TransportFixture.Create(accepted: false);
        await fixture.PublishSecondRelease(activate: false);
        ProcessResult result = await fixture.Execute("validate", """
            git() {
              if [[ "$*" == *"ls-remote --exit-code origin refs/heads/develop" ]]; then
                if [[ -f "$FIXTURE/snapshot-read" ]]; then
                  command git --git-dir=../remote.git update-ref refs/heads/develop refs/heads/new-inventory
                else
                  touch "$FIXTURE/snapshot-read"
                fi
              fi
              command git "$@"
            }

            """);
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(fixture.StatusRecords().Any(value => value["state"]!.GetValue<string>() == "success")).IsFalse();
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Initial);
    }

    [Test]
    public async Task ProposalSnapshotRaceRebuildsTheCompleteNewUnion()
    {
        using var fixture = await TransportFixture.Create(accepted: false);
        await fixture.PublishSecondRelease(activate: false);
        ProcessResult result = await fixture.Execute("propose", """
            git() {
              if [[ "$*" == *"ls-remote --exit-code origin refs/heads/develop" && ! -f "$FIXTURE/race-injected" ]]; then
                command git --git-dir=../remote.git update-ref refs/heads/develop refs/heads/new-inventory
                touch "$FIXTURE/race-injected"
              fi
              command git "$@"
            }

            """);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Output + result.Error);
        JsonNode receipt = fixture.LocalReceipt();
        await Assert.That(receipt["releases"]!.AsArray().Count).IsEqualTo(2);
        await Assert.That(receipt["proposalCommit"]!.GetValue<string>()).IsNotEqualTo(fixture.ProposalHead);
        await Assert.That(await fixture.RemoteOid("docs/publication")).IsEqualTo(fixture.Initial);
    }

    [Test]
    public async Task InventoryRaceBeforeMirrorMutationCannotPublishTheStaleUnion()
    {
        using var fixture = await TransportFixture.Create();
        await fixture.PublishSecondRelease(activate: false);
        ProcessResult result = await fixture.Execute("reconcile", """
            git() {
              if [[ "$*" == *"ls-remote --exit-code origin refs/heads/develop" ]]; then
                if [[ -f "$FIXTURE/snapshot-read" ]]; then
                  command git --git-dir=../remote.git update-ref refs/heads/develop refs/heads/new-inventory
                else
                  touch "$FIXTURE/snapshot-read"
                fi
              fi
              command git "$@"
            }

            """);
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Initial);
        await Assert.That(fixture.ReceiptState()).IsEqualTo("pending");
        await Assert.That(File.Exists(fixture.StatusesPath)).IsFalse();
    }

    [Test]
    public async Task DurableReceiptOutageStopsBeforeMirrorMutation()
    {
        using var fixture = await TransportFixture.Create();
        File.WriteAllText(Path.Combine(fixture.Root, "deny-receipts"), "");
        fixture.SetChecks(fixture.Accepted, 42, "success");
        ProcessResult result = await fixture.Reconcile();
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(fixture.LocalReceipt()["reason"]!.GetValue<string>()).IsEqualTo("transport-authorization-denied");
        await Assert.That(File.ReadAllLines(Path.Combine(fixture.Root, "denied-mutations")).Length).IsEqualTo(1);
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Initial);
        await Assert.That(File.Exists(fixture.StatusesPath)).IsFalse();
    }

    [Test]
    public async Task MergedProposalBranchMayBeDeletedBeforeReconciliation()
    {
        using var fixture = await TransportFixture.Create();
        ProcessResult deletion = await fixture.Git($"git push origin --delete {fixture.ProposalBranch}");
        await Assert.That(deletion.ExitCode).IsEqualTo(0).Because(deletion.Error);
        fixture.SetChecks(fixture.Accepted, 42, "success");
        ProcessResult result = await fixture.Reconcile();
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(fixture.ReceiptState()).IsEqualTo("delivered");
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Accepted);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TransientTransportHonorsRetryAfterAndStopsAtThreeAttempts(bool exhausted)
    {
        string root = Path.Combine(Path.GetTempPath(), $"publication-retry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string program = Text(Step("transport"), "run") + "\n" + """
                source "$STATE/transport.sh"
                trap - ERR
                transport_delay() { printf '%s\n' "$1" >> "$STATE/delays"; }
                gh() {
                  printf 'attempt\n' >> "$STATE/attempts"
                  if [[ "$EXHAUSTED" == true || "$(wc -l < "$STATE/attempts")" -lt 2 ]]; then
                    printf 'HTTP/2.0 429 Too Many Requests\r\nRetry-After: 7\r\n\r\n{"message":"rate limited"}\n'
                    return 1
                  fi
                  printf 'HTTP/2.0 200 OK\r\n\r\n{"ready":true}\n'
                }
                gh_api repos/islamu/event/branches/develop
                """;
            ProcessResult result = await RunBash(program, environment: new Dictionary<string, string>
            {
                ["STATE"] = root,
                ["EXHAUSTED"] = exhausted ? "true" : "false",
            });
            await Assert.That(result.ExitCode).IsEqualTo(exhausted ? 76 : 0);
            await Assert.That(File.ReadAllLines(Path.Combine(root, "attempts")).Length).IsEqualTo(exhausted ? 3 : 2);
            await Assert.That(File.ReadAllLines(Path.Combine(root, "delays"))).IsEquivalentTo(exhausted ? new[] { "7", "7" } : ["7"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ActualGithubCliHeadersAreSeparatedFromCheckPageJson()
    {
        string root = Path.Combine(Path.GetTempPath(), $"publication-http-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<System.Net.Sockets.TcpClient> accepted = listener.AcceptTcpClientAsync(timeout.Token).AsTask();
        Task server = Serve();
        try
        {
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            string program = Text(Step("transport"), "run") + "\n" + """
                source "$STATE/transport.sh"
                trap - ERR
                gh_api "$TEST_ENDPOINT"
                """;
            ProcessResult result = await RunBash(program, environment: new Dictionary<string, string>
            {
                ["STATE"] = root,
                ["TEST_ENDPOINT"] = $"http://127.0.0.1:{port}/check-runs",
                ["GH_TOKEN"] = Guid.NewGuid().ToString("N"),
            });
            await server;
            await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
            using JsonDocument page = JsonDocument.Parse(result.Output);
            await Assert.That(page.RootElement.GetProperty("check_runs").GetArrayLength()).IsEqualTo(0);
        }
        finally
        {
            timeout.Cancel();
            listener.Stop();
            try { await server; }
            catch (OperationCanceledException) { }
            Directory.Delete(root, recursive: true);
        }

        async Task Serve()
        {
            using System.Net.Sockets.TcpClient client = await accepted;
            await using System.Net.Sockets.NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
            byte[] response = System.Text.Encoding.UTF8.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 17\r\nConnection: close\r\n\r\n{\"check_runs\":[]}");
            await stream.WriteAsync(response, timeout.Token);
            await stream.FlushAsync(timeout.Token);
        }
    }

    [Test]
    public async Task FailedFetchDoesNotReturnPreviouslyObservedHead()
    {
        using var fixture = await TransportFixture.Create();
        ProcessResult result = await fixture.Git(
            Text(Step("transport"), "run") + "\n" + """
            source "$STATE/transport.sh"
            trap - ERR
            git fetch origin refs/heads/develop
            git_remote() { return 76; }
            if result=$(fetch_branch develop); then exit 91; else status=$?; fi
            [[ "$status" == 76 && -z "$result" ]]
            """);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
    }

    [Test]
    public async Task DeliveryReceiptOutageNeverReportsDeliveredStatus()
    {
        using var fixture = await TransportFixture.Create();
        File.WriteAllText(Path.Combine(fixture.Root, "deny-delivered-receipt"), "");
        fixture.SetChecks(fixture.Accepted, 42, "success");
        ProcessResult result = await fixture.Reconcile();
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(fixture.ReceiptState()).IsEqualTo("pending");
        await Assert.That(fixture.StatusRecords().Any(value => value["state"]!.GetValue<string>() == "success")).IsFalse();
        await Assert.That((await fixture.DurableReceipts()).Any(value => value["state"]!.GetValue<string>() == "delivered")).IsFalse();
    }

    [Test]
    public async Task CrashAfterMirrorPushRecoversFromDurablePendingCheckpoint()
    {
        using var fixture = await TransportFixture.Create();
        ProcessResult crash = await fixture.Execute("reconcile", """
            git() {
              local argument
              for argument in "$@"; do
                if [[ "$argument" == push ]]; then
                  command git "$@" || return
                  kill -KILL $$
                fi
              done
              command git "$@"
            }

            """);
        await Assert.That(crash.ExitCode).IsNotEqualTo(0);
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Accepted);
        JsonNode[] checkpoints = await fixture.DurableReceipts();
        await Assert.That(checkpoints.Any(value => value["reason"]!.GetValue<string>() == "mirror-prepared")).IsTrue();
        await Assert.That(checkpoints.Any(value => value["state"]!.GetValue<string>() == "delivered")).IsFalse();
        fixture.SetChecks(fixture.Accepted, 42, "success");
        ProcessResult resumed = await fixture.Reconcile();
        await Assert.That(resumed.ExitCode).IsEqualTo(0).Because(resumed.Output + resumed.Error);
        await Assert.That(fixture.ReceiptState()).IsEqualTo("delivered");
        await Assert.That(await fixture.RemoteOid("docs/publication")).IsEqualTo(fixture.Accepted);
    }

    [Test]
    public async Task CrashAfterProposalCommitReusesItWithoutDuplicateProposalCommits()
    {
        using var fixture = await TransportFixture.Create(accepted: false);
        await fixture.PublishSecondRelease();
        File.WriteAllText(Path.Combine(fixture.Root, "crash-after-proposal"), "");
        ProcessResult crash = await fixture.Execute("propose");
        await Assert.That(crash.ExitCode).IsNotEqualTo(0);
        ProcessResult refsBefore = await fixture.Git("git --git-dir=../remote.git for-each-ref --format='%(refname) %(objectname)' refs/heads/release-publication");
        await Assert.That(refsBefore.ExitCode).IsEqualTo(0).Because(refsBefore.Error);
        await Assert.That(refsBefore.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length).IsEqualTo(2);
        ProcessResult recovered = await fixture.Execute("propose");
        await Assert.That(recovered.ExitCode).IsEqualTo(0).Because(recovered.Output + recovered.Error);
        ProcessResult refsAfter = await fixture.Git("git --git-dir=../remote.git for-each-ref --format='%(refname) %(objectname)' refs/heads/release-publication");
        await Assert.That(refsAfter.Output).IsEqualTo(refsBefore.Output);
        await Assert.That(fixture.LocalReceipt()["releases"]!.AsArray().Count).IsEqualTo(2);
        await Assert.That(await fixture.RemoteOid("docs/publication")).IsEqualTo(fixture.Initial);
        await Assert.That(await fixture.RemoteOid("docs/gitbook-sync")).IsEqualTo(fixture.Initial);
    }

    [Test]
    public async Task ConcurrentReceiptAppendRetriesWithoutRewritingOtherWriter()
    {
        using var fixture = await TransportFixture.Create();
        File.WriteAllText(Path.Combine(fixture.Root, "race-receipt"), "");
        ProcessResult result = await fixture.Reconcile();
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Output + result.Error);
        string concurrent = File.ReadAllText(Path.Combine(fixture.Root, "concurrent-receipt")).Trim();
        ProcessResult ancestry = await fixture.Git($"git merge-base --is-ancestor {concurrent} {await fixture.RemoteOid("docs/publication-receipts")}");
        await Assert.That(ancestry.ExitCode).IsEqualTo(0).Because(ancestry.Error);
    }

    private static YamlMappingNode LoadWorkflow()
    {
        var stream = new YamlStream();
        using var reader = File.OpenText(Path.Combine(RepositoryRoot(), ".github", "workflows", "release-publish.yml"));
        stream.Load(reader);
        if (stream.Documents.Count != 1) throw new InvalidOperationException("Expected one workflow document.");
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static YamlMappingNode Job() => Mapping(Mapping(LoadWorkflow(), "jobs"), "release-publish");
    private static IEnumerable<YamlMappingNode> Steps() =>
        ((YamlSequenceNode)Job().Children[new YamlScalarNode("steps")]).Children.Cast<YamlMappingNode>();
    private static YamlMappingNode Step(string id) =>
        Steps().Single(step => step.Children.TryGetValue(new YamlScalarNode("id"), out YamlNode? node) &&
            ((YamlScalarNode)node).Value == id);
    private static YamlMappingNode Mapping(YamlMappingNode node, string key) =>
        (YamlMappingNode)node.Children[new YamlScalarNode(key)];
    private static string Text(YamlMappingNode node, string key) =>
        ((YamlScalarNode)node.Children[new YamlScalarNode(key)]).Value!;

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Explore.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static async Task<ProcessResult> RunBash(string program, bool syntaxOnly = false,
        string? directory = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo("/bin/bash")
        {
            WorkingDirectory = directory ?? RepositoryRoot(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (syntaxOnly) info.ArgumentList.Add("-n");
        info.ArgumentList.Add("-s");
        if (environment is not null)
            foreach ((string key, string value) in environment) info.Environment[key] = value;
        using Process process = Process.Start(info)!;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellation.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellation.Token);
        try
        {
            await process.StandardInput.WriteAsync(program.AsMemory(), cancellation.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellation.Token);
            return new ProcessResult(process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);

    private sealed class TransportFixture : IDisposable
    {
        private readonly GovernedReleaseFixture releases;
        private readonly List<AuthorizedInventoryEntry> entries = [];
        private AuthorizedInventoryEntry secondRelease = null!;
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        private TransportFixture(GovernedReleaseFixture releases) => this.releases = releases;
        public string Root => releases.Root;
        public string Initial { get; private set; } = "";
        public string Accepted { get; private set; } = "";
        public string ProposalHead { get; private set; } = "";
        public string ProposalBranch { get; private set; } = "";
        public string FirstTagObject => releases.FirstTagObject;
        public string StatusesPath => Path.Combine(Root, "statuses");
        public Dictionary<string, string> Overrides { get; } = new(StringComparer.Ordinal);
        private string Repository => releases.RepositoryPath;
        private string State => Path.Combine(Root, "state");
        private string Bundle => Path.Combine(Root, "bundle");
        private string Runtime => Path.Combine(Root, "runtime");
        private string Retained => Path.Combine(Repository, "eng/release/publication/retained");
        private Dictionary<string, string> Environment => new()
        {
            ["GH_REPO"] = "islamu/event",
            ["DEFAULT_BRANCH"] = "develop",
            ["PUBLICATION_ENABLED"] = "true",
            ["PUBLICATION_BASE"] = "https://github.com/ISLAMU/Event/blob/",
            ["TRUSTED_LAUNCHER"] = Path.Combine(Runtime, "ISLAMU.ReleaseEngineering"),
            ["PROPOSAL_PR"] = "17",
            ["PROPOSAL_HEAD"] = ProposalHead,
            ["RECEIPT_BRANCH"] = "docs/publication-receipts",
            ["GITBOOK_APP_ID"] = "42",
            ["GITBOOK_CHECK_NAME"] = "GitBook",
            ["STATE"] = State,
            ["RUNNER_TEMP"] = Path.Combine(Root, "runner-temp"),
            ["GITHUB_RUN_ID"] = "1",
            ["GITHUB_RUN_ATTEMPT"] = "1",
            ["GITHUB_WORKSPACE"] = Repository,
            ["FIXTURE"] = Root,
            ["PAGE"] = "docs/public/changelog/README.md",
            ["MANIFEST"] = "docs/public/changelog/publication-manifest.v1.json",
            ["GITHUB_STEP_SUMMARY"] = Path.Combine(Root, "summary"),
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = "/dev/null",
            ["GIT_AUTHOR_NAME"] = "Publication fixture",
            ["GIT_AUTHOR_EMAIL"] = "fixture@example.invalid",
            ["GIT_COMMITTER_NAME"] = "Publication fixture",
            ["GIT_COMMITTER_EMAIL"] = "fixture@example.invalid",
            ["GIT_AUTHOR_DATE"] = "2026-09-01T00:00:00Z",
            ["GIT_COMMITTER_DATE"] = "2026-09-01T00:00:00Z",
            ["ISLAMU_RELEASE_TRUSTED_BUNDLE"] = Bundle,
            ["ISLAMU_RELEASE_PROMOTION_RECEIPT"] = Path.Combine(Root, "authority/promotion-receipt.v1.json"),
            ["ISLAMU_RELEASE_PROMOTION_SIGNATURE"] = Path.Combine(Root, "authority/promotion-receipt.v1.json.sig"),
            ["ISLAMU_RELEASE_PROMOTION_PRINCIPAL"] = "fixture-tooling-promoter",
            ["ISLAMU_RELEASE_MANIFEST_SHA256"] = Digest(File.ReadAllBytes(Path.Combine(Bundle, "trusted-bundle.manifest.json"))),
            ["ISLAMU_RELEASE_BUNDLE_ID"] = "islamu-release-engineering",
            ["ISLAMU_RELEASE_BUNDLE_VERSION"] = "1.0.0",
            ["ISLAMU_RELEASE_POLICY_VERSION"] = "policy-v1",
            ["ISLAMU_RELEASE_CONFIG_VERSION"] = "config-v1",
            ["ISLAMU_RELEASE_TRUST_VERSION"] = "trust-v1",
        };

        public static async Task<TransportFixture> Create(bool accepted = true)
        {
            var fixture = new TransportFixture(GovernedReleaseFixture.CreateSha1());
            try
            {
                Directory.CreateDirectory(fixture.State);
                Directory.CreateDirectory(Path.Combine(fixture.Root, "runner-temp"));
                await fixture.RetainRelease(second: false);
                await fixture.RetainRelease(second: true);
                fixture.secondRelease = fixture.entries[1];
                fixture.entries.RemoveAt(1);
                await fixture.WriteInventory();
                await fixture.ProvisionRuntime();
                ProcessResult setup = await fixture.Git("""
                    set -euo pipefail
                    git init --bare ../remote.git
                    git config user.name 'Publication fixture'
                    git config user.email 'fixture@example.invalid'
                    git remote add origin ../remote.git
                    git branch docs/gitbook-sync
                    git branch docs/publication
                    git branch docs/publication-receipts
                    git switch develop
                    git add eng/release/publication/retained
                    git commit -m 'Retain signed complete publication inventory'
                    git push --tags origin develop docs/gitbook-sync docs/publication docs/publication-receipts
                    """);
                RequireSuccess(setup);
                fixture.Initial = await fixture.RemoteOid("docs/publication");
                File.WriteAllText(Path.Combine(fixture.Root, "checks.json"), """{"check_runs":[]}""");
                File.WriteAllText(Path.Combine(fixture.Root, "branch.json"), """{"protected":true}""");
                RequireSuccess(await fixture.Execute("propose"));
                using (JsonDocument proposal = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture.Root, "pull.json"))))
                {
                    fixture.ProposalHead = proposal.RootElement.GetProperty("head").GetProperty("sha").GetString()!;
                    fixture.ProposalBranch = proposal.RootElement.GetProperty("head").GetProperty("ref").GetString()!;
                }
                if (accepted)
                {
                    RequireSuccess(await fixture.Execute("validate"));
                    await fixture.AcceptProposal();
                }
                File.Delete(fixture.StatusesPath);
                File.Delete(Path.Combine(fixture.Root, "status-records"));
                return fixture;
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        public void SetPullRequest(bool merged) =>
            File.WriteAllText(Path.Combine(Root, "pull.json"), JsonSerializer.Serialize(new
            {
                merged,
                state = merged ? "closed" : "open",
                number = 17,
                merge_commit_sha = Accepted,
                @base = new { @ref = "docs/publication", sha = Initial, repo = new { full_name = "islamu/event" } },
                head = new { @ref = ProposalBranch, sha = ProposalHead, repo = new { full_name = "islamu/event" } },
            }));

        public async Task AcceptProposal()
        {
            File.Delete(Path.Combine(Repository, "docs/public/changelog/README.md"));
            File.Delete(Path.Combine(Repository, "docs/public/changelog/publication-manifest.v1.json"));
            RequireSuccess(await Git($"""
                set -euo pipefail
                git switch docs/publication
                git merge --no-ff {ProposalHead} -m 'Accept independently reviewed publication'
                git push origin docs/publication
                git push origin {ProposalHead}:refs/pull/17/head
                """));
            Accepted = await RemoteOid("docs/publication");
            SetPullRequest(merged: true);
        }

        private async Task RetainRelease(bool second)
        {
            string version = second ? GovernedReleaseFixture.SecondReleaseVersion : GovernedReleaseFixture.FirstReleaseVersion;
            string target = second ? releases.D : releases.B;
            string tag = second ? releases.SecondTagObject : releases.FirstTagObject;
            (int candidateCode, string candidateOutput) = releases.VerifyCandidate(version, target);
            if (candidateCode != Program.Success) throw new InvalidOperationException(candidateOutput);
            (int tagCode, string tagOutput) = releases.VerifyTag(version, target, tag);
            if (tagCode != Program.Success) throw new InvalidOperationException(tagOutput);
            string relative = $"docs/internal/releases/{version}/release-evidence.v1.json";
            byte[] evidence = File.ReadAllBytes(Path.Combine(Repository, relative));
            string evidencePath = Path.Combine(Retained, "evidence", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            File.WriteAllBytes(evidencePath, evidence);
            string approvalPath = Path.Combine(Retained, "evidence/publication-approvals", version + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(approvalPath)!);
            byte[] approval = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = "publication-approval.v1",
                version,
                tagObjectId = tag,
                evidenceSha256 = Digest(evidence),
                disclosureAuthorized = true,
            });
            File.WriteAllBytes(approvalPath, approval);
            ReleaseContext context = JsonSerializer.Deserialize<ReleaseContext>(
                File.ReadAllBytes(Path.Combine(Repository, $"docs/internal/releases/{version}/release-context.v1.json")), JsonOptions)!;
            var documents = context.Changes.Select(change => change.ChangeId).OfType<string>().Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).Select(id => new AuthorizedSourceDocument($"docs/internal/releases/changes/{id}.yaml", "fragment")).ToArray();
            using JsonDocument final = JsonDocument.Parse(evidence);
            entries.Add(new AuthorizedInventoryEntry(version, final.RootElement.GetProperty("line").GetString()!,
                final.RootElement.GetProperty("releaseDate").GetString()!, tag, target,
                $"docs/internal/releases/{version}", relative, Digest(evidence), true, Digest(approval), documents));
            await WriteInventory();
        }

        private async Task WriteInventory()
        {
            string inventory = Path.Combine(Retained, "authorized-inventory.v1.json");
            File.WriteAllBytes(inventory, JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = "authorized-inventory.v1",
                producer = "final-lane",
                completeSet = true,
                entries,
            }, JsonOptions));
            if (!File.Exists(Path.Combine(Root, "publication-key")))
                RequireSuccess(await Git("ssh-keygen -q -t ed25519 -N '' -f \"$FIXTURE/publication-key\""));
            File.Delete(inventory + ".sig");
            RequireSuccess(await Git("ssh-keygen -Y sign -f \"$FIXTURE/publication-key\" -n islamu-publication eng/release/publication/retained/authorized-inventory.v1.json"));
        }

        private async Task ProvisionRuntime()
        {
            Directory.CreateDirectory(Runtime);
            string binaryDirectory = Path.GetDirectoryName(typeof(ISLAMU.ReleaseEngineering.Program).Assembly.Location)!;
            string appHost = OperatingSystem.IsWindows() ? "ISLAMU.ReleaseEngineering.exe" : "ISLAMU.ReleaseEngineering";
            foreach (string name in new[]
            {
                appHost,
                "ISLAMU.ReleaseEngineering.dll",
                "ISLAMU.ReleaseEngineering.deps.json",
                "ISLAMU.ReleaseEngineering.runtimeconfig.json",
                "YamlDotNet.dll",
            })
            {
                string file = Path.Combine(binaryDirectory, name);
                File.Copy(file, Path.Combine(Runtime, name));
                File.Copy(file, Path.Combine(Bundle, "bin", name), overwrite: true);
            }
            File.Copy(Path.Combine(Root, "authority/allowed-promoters"),
                Path.Combine(Runtime, "ISLAMU.ReleaseEngineering.promotion-allowed-signers"), overwrite: true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(Path.Combine(Runtime, "ISLAMU.ReleaseEngineering"),
                    UnixFileMode.UserRead | UnixFileMode.UserExecute);
            string publicKey = string.Join(' ', File.ReadAllText(Path.Combine(Root, "publication-key.pub"))
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
            File.WriteAllText(Path.Combine(Bundle, "trust/publication-allowed-signers"),
                $"publication-approver namespaces=\"islamu-publication\" {publicKey}\n");
            await ResignBundle();
        }

        public async Task ResignBundle()
        {
            string manifestPath = Path.Combine(Bundle, "trusted-bundle.manifest.json");
            JsonNode manifest = JsonNode.Parse(File.ReadAllBytes(manifestPath))!;
            manifest["files"] = JsonSerializer.SerializeToNode(Directory.EnumerateFiles(Bundle, "*", SearchOption.AllDirectories)
                .Where(path => path != manifestPath).Order(StringComparer.Ordinal)
                .Select(path => new { path = Path.GetRelativePath(Bundle, path), sha256 = Digest(File.ReadAllBytes(path)) }));
            File.WriteAllBytes(manifestPath, ReleaseArtifactPolicy.NormalizeJson(manifest.ToJsonString()).Bytes!);
            string receiptPath = Path.Combine(Root, "authority/promotion-receipt.v1.json");
            JsonNode receipt = JsonNode.Parse(File.ReadAllBytes(receiptPath))!;
            receipt["bundleManifestSha256"] = Digest(File.ReadAllBytes(manifestPath));
            File.WriteAllBytes(receiptPath, ReleaseArtifactPolicy.NormalizeJson(receipt.ToJsonString()).Bytes!);
            File.Delete(receiptPath + ".sig");
            RequireSuccess(await Git("ssh-keygen -Y sign -f \"$FIXTURE/authority/promotion-key\" -n islamu-release-promotion \"$FIXTURE/authority/promotion-receipt.v1.json\""));
        }

        public async Task RemoveInventorySignature()
        {
            RequireSuccess(await Git("""
                set -euo pipefail
                git switch develop
                git rm eng/release/publication/retained/authorized-inventory.v1.json.sig
                git commit -m 'Remove retained authority'
                git push origin develop
                """));
        }

        public async Task PublishSecondRelease(bool activate = true)
        {
            // Evidence and tag signatures were retained before runtime promotion. Publishing
            // changes only the signed complete authorization set, never the release identities.
            RequireSuccess(await Git("git switch -c new-inventory develop"));
            entries.Add(secondRelease);
            await WriteInventory();
            RequireSuccess(await Git($"""
                set -euo pipefail
                git add eng/release/publication/retained
                git commit -m 'Authorize the second retained release'
                git push origin HEAD:refs/heads/{(activate ? "develop" : "new-inventory")}
                """));
        }

        public JsonNode LocalReceipt() => JsonNode.Parse(File.ReadAllBytes(Path.Combine(State, "receipt.json")))!;

        public JsonNode[] StatusRecords() =>
            File.Exists(Path.Combine(Root, "status-records"))
                ? File.ReadAllLines(Path.Combine(Root, "status-records")).Select(line => JsonNode.Parse(line)!).ToArray()
                : [];

        public async Task<JsonNode[]> DurableReceipts()
        {
            ProcessResult result = await Git("""
                set -euo pipefail
                git fetch origin refs/heads/docs/publication-receipts
                while IFS= read -r path; do git show "FETCH_HEAD:$path" | jq -c .; done \
                  < <(git ls-tree -r --name-only FETCH_HEAD -- receipts)
                """);
            RequireSuccess(result);
            return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!).ToArray();
        }

        public string PlaceLauncherInCheckout()
        {
            string target = Path.Combine(Repository, "untrusted-launcher");
            File.Copy(Path.Combine(Runtime, "ISLAMU.ReleaseEngineering"), target);
            return target;
        }

        public void SetChecks(string sha, int app, string conclusion,
            string branch = "docs/gitbook-sync", bool newerFailure = false)
        {
            object Check(int id, string result) => new
            {
                id,
                name = "GitBook",
                app = new { id = app },
                head_sha = sha,
                status = "completed",
                conclusion = result,
                check_suite = new { id = 123 },
            };
            object[] checks = newerFailure ? [Check(1, conclusion), Check(2, "failure")] : [Check(1, conclusion)];
            File.WriteAllText(Path.Combine(Root, "checks.json"), JsonSerializer.Serialize(new { check_runs = checks }));
            File.WriteAllText(Path.Combine(Root, "suite.json"), JsonSerializer.Serialize(new
            {
                head_branch = branch,
                head_sha = sha,
                app = new { id = app },
            }));
        }

        public string ReceiptState()
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(State, "receipt.json")));
            return document.RootElement.GetProperty("state").GetString()!;
        }

        public async Task<string> RemoteOid(string branch)
        {
            ProcessResult result = await Git($"git ls-remote --exit-code origin refs/heads/{branch}");
            if (result.ExitCode != 0) throw new InvalidOperationException(result.Error);
            return result.Output.Split('\t')[0].Trim();
        }

        public Task<ProcessResult> Git(string program) => RunBash(program, directory: Repository, environment: Environment);
        public Task<ProcessResult> Reconcile() => Execute("reconcile");
        public Task<ProcessResult> Execute(string step, string concurrentWriter = "")
        {
            Dictionary<string, string> environment = Environment;
            environment["MODE"] = step is "propose" or "validate" or "reconcile" ? step : "propose";
            foreach ((string key, string value) in Overrides) environment[key] = value;
            string program = concurrentWriter + """
            gh() {
              local endpoint= argument state= method=GET input= head= base= ref= sha= context=
              while [[ $# -gt 0 ]]; do
                argument=$1
                shift
                case "$argument" in
                  repos/*|graphql) endpoint=$argument ;;
                  --method) method=$1; shift ;;
                  --input) input=$1; shift ;;
                  -f)
                    argument=$1; shift
                    case "$argument" in
                      state=*) state=${argument#state=} ;;
                      head=*) head=${argument#head=} ;;
                      base=*) base=${argument#base=} ;;
                      ref=*) ref=${argument#ref=} ;;
                      sha=*) sha=${argument#sha=} ;;
                      context=*) context=${argument#context=} ;;
                    esac ;;
                esac
              done
              case "$endpoint" in
                repos/islamu/event/pulls/17) cat "$FIXTURE/pull.json" ;;
                repos/islamu/event/pulls)
                  if [[ "$method" == GET ]]; then
                    if [[ -f "$FIXTURE/pull.json" ]]; then
                      jq -s --arg head "${head#*:}" --arg state "$state" \
                        '[.[] | select(.head.ref == $head and ($state == "all" or .state == $state))]' "$FIXTURE/pull.json"
                    else printf '[]\n'; fi
                  else
                    sha=$(command git --git-dir="$FIXTURE/remote.git" rev-parse "refs/heads/$head")
                    jq -n --arg head "$head" --arg sha "$sha" --arg base "$base" \
                      '{number:17,state:"open",merged:false,head:{ref:$head,sha:$sha,repo:{full_name:"islamu/event"}},
                        base:{ref:$base,repo:{full_name:"islamu/event"}}}' > "$FIXTURE/pull.json"
                    printf '17\n'
                  fi ;;
                repos/islamu/event/git/refs)
                  command git --git-dir="$FIXTURE/remote.git" update-ref "$ref" "$sha" 0000000000000000000000000000000000000000 ;;
                graphql)
                  local branch old path contents blob tree new index
                  branch=$(jq -r '.variables.input.branch.branchName' "$input")
                  old=$(jq -r '.variables.input.expectedHeadOid' "$input")
                  if [[ "$branch" == docs/publication-receipts && -f "$FIXTURE/deny-receipts" ]]; then
                    printf 'denied\n' >> "$FIXTURE/denied-mutations"
                    printf 'HTTP/2.0 403 Forbidden\r\n\r\n{"message":"denied"}\n'
                    return 7
                  fi
                  if [[ "$branch" == docs/publication-receipts && -f "$FIXTURE/deny-delivered-receipt" ]] &&
                    jq -er '.variables.input.fileChanges.additions[0].contents | @base64d | fromjson | .state == "delivered"' "$input" >/dev/null; then
                    return 7
                  fi
                  index=$(mktemp "$FIXTURE/index.XXXXXXXX"); rm "$index"
                  GIT_INDEX_FILE="$index" command git read-tree "$old" || return
                  while IFS=$'\t' read -r path contents; do
                    blob=$(printf '%s' "$contents" | base64 -d | command git hash-object -w --stdin)
                    GIT_INDEX_FILE="$index" command git update-index --add --cacheinfo "100644,$blob,$path" || return
                  done < <(jq -r '.variables.input.fileChanges.additions[] | [.path,.contents] | @tsv' "$input")
                  tree=$(GIT_INDEX_FILE="$index" command git write-tree)
                  rm "$index"
                  new=$(jq -r '.variables.input.message.headline' "$input" | command git commit-tree "$tree" -p "$old") || return
                  # Transfer objects without changing the tested ref, then perform server-side CAS.
                  command git push origin "$new:refs/fixture/objects/$new" >/dev/null 2>&1 || return
                  if [[ "$branch" == docs/publication-receipts && -f "$FIXTURE/race-receipt" ]]; then
                    rm "$FIXTURE/race-receipt"
                    local concurrent
                    concurrent=$(printf 'Concurrent receipt append\n' | command git --git-dir="$FIXTURE/remote.git" commit-tree "$old^{tree}" -p "$old")
                    command git --git-dir="$FIXTURE/remote.git" update-ref "refs/heads/$branch" "$concurrent" "$old"
                    printf '%s\n' "$concurrent" > "$FIXTURE/concurrent-receipt"
                  fi
                  command git --git-dir="$FIXTURE/remote.git" update-ref "refs/heads/$branch" "$new" "$old" || return
                  if [[ "$branch" == release-publication/* && -f "$FIXTURE/crash-after-proposal" ]]; then
                    rm "$FIXTURE/crash-after-proposal"
                    kill -KILL $$
                    return 137
                  fi
                  jq -n --arg oid "$new" '{data:{createCommitOnBranch:{commit:{oid:$oid}}}}' ;;
                repos/islamu/event/commits/*/check-runs)
                  printf 'HTTP/2.0 200 OK\r\nContent-Type: application/json\r\n\r\n'
                  cat "$FIXTURE/checks.json" ;;
                repos/islamu/event/check-suites/123) cat "$FIXTURE/suite.json" ;;
                repos/islamu/event/statuses/*)
                  printf '%s\n' "$state" >> "$FIXTURE/statuses"
                  jq -cn --arg sha "${endpoint##*/}" --arg state "$state" --arg context "$context" \
                    '{sha:$sha,state:$state,context:$context}' >> "$FIXTURE/status-records" ;;
                repos/islamu/event/branches/*) cat "$FIXTURE/branch.json" ;;
                *) printf '%s\n' "Unexpected GitHub request: $endpoint" >&2; return 9 ;;
              esac
            }

            """ + "\n" + Text(Step("guard"), "run");
            if (step != "guard")
                program += "\n" + Text(Step("transport"), "run") + "\n" + Text(Step(step), "run");
            return RunBash(program, directory: Repository, environment: environment);
        }

        private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
        private static void RequireSuccess(ProcessResult result)
        {
            if (result.ExitCode != 0) throw new InvalidOperationException(result.Output + result.Error);
        }
        public void Dispose() => releases.Dispose();
    }
}
