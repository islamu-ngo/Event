using System.Diagnostics;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel]
public sealed class GitRepositoryValidatorTests
{
    [Test]
    public async Task DescriptorSuppliedTagsAreAuthoritativeAndPriorLineBaseIsAllowed()
    {
        using var repo = GitRepositoryFixture.Create();
        string v100 = repo.Commit("v1.0.0");
        repo.AnnotatedTag("v1.0.0", v100);
        string candidate = repo.Commit("v1.1.0 preparation");
        repo.Branch("v1.1", candidate);
        repo.Checkout("main");
        string otherLine = repo.Commit("v1.2.0");
        repo.AnnotatedTag("v1.2.0", otherLine);

        GitReleaseValidationResult result = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            repo.Path,
            "v1.1",
            "1.1.0",
            "refs/tags/v1.0.0",
            "refs/tags/v1.0.0",
            candidate));

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Identity?.ObjectFormat).IsEqualTo("sha1");
        await Assert.That(result.Identity?.OidLength).IsEqualTo(40);
        await Assert.That(result.Identity?.BaseStableTag).IsEqualTo("v1.0.0");
        await Assert.That(result.Identity?.PreviousPublishedTag).IsEqualTo("v1.0.0");
        await Assert.That(result.Identity?.CandidateOid).IsEqualTo(candidate);
        await Assert.That(result.Identity?.BaseStableCommitOid).IsEqualTo(v100);
    }

    [Test]
    public async Task NewerReachableSameLineTagDoesNotOverrideDescriptorSelection()
    {
        using var repo = GitRepositoryFixture.Create();
        string v100 = repo.Commit("v1.0.0");
        repo.AnnotatedTag("v1.0.0", v100);
        string v110 = repo.Commit("v1.1.0");
        repo.AnnotatedTag("v1.1.0", v110);
        string v111 = repo.Commit("v1.1.1");
        repo.AnnotatedTag("v1.1.1", v111);
        string candidate = repo.Commit("v1.1.2 preparation");
        repo.Branch("v1.1", candidate);

        GitReleaseValidationResult result = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            repo.Path,
            "v1.1",
            "1.1.2",
            BaseStableRef: "refs/tags/v1.0.0",
            PreviousPublishedRef: "refs/tags/v1.1.0",
            CandidateOid: candidate));

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Diagnostics).Contains("git_unexpected_newer_tag:v1.1.1");
    }

    [Test]
    public async Task BaselineTagsAreIgnoredByStrictSemVerReleaseTagDiscovery()
    {
        using var repo = GitRepositoryFixture.Create();
        string baseline = repo.Commit("baseline");
        repo.AnnotatedTag("changelog-baseline-2026-08-15", baseline);
        string v100 = repo.Commit("v1.0.0");
        repo.AnnotatedTag("v1.0.0", v100);
        string candidate = repo.Commit("v1.0.1 preparation");
        repo.Branch("v1.0", candidate);

        GitReleaseValidationResult result = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            repo.Path,
            "v1.0",
            "1.0.1",
            "refs/tags/v1.0.0",
            "refs/tags/v1.0.0",
            candidate));

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Diagnostics).DoesNotContain("git_malformed_tag_ref:refs/tags/changelog-baseline-2026-08-15");
    }

    [Test]
    public async Task BaselineLowerBoundAllowsAnySelectedSemVerWhenNoGovernedStableTagExists()
    {
        using var preOne = GitRepositoryFixture.Create();
        string preOneBaseline = preOne.Commit("baseline");
        preOne.AnnotatedTag("changelog-baseline-2026-08-15", preOneBaseline);
        string preOneCandidate = preOne.Commit("0.1.0 preparation");
        preOne.Branch("v0.1", preOneCandidate);

        using var laterSemVer = GitRepositoryFixture.Create();
        string laterBaseline = laterSemVer.Commit("baseline");
        laterSemVer.AnnotatedTag("changelog-baseline-2026-08-15", laterBaseline);
        string laterCandidate = laterSemVer.Commit("2.0.0 preparation");
        laterSemVer.Branch("v2.0", laterCandidate);

        GitReleaseValidationResult preOneResult = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            preOne.Path,
            "v0.1",
            "0.1.0",
            "refs/tags/changelog-baseline-2026-08-15",
            "refs/tags/changelog-baseline-2026-08-15",
            preOneCandidate));
        GitReleaseValidationResult laterResult = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            laterSemVer.Path,
            "v2.0",
            "2.0.0",
            "refs/tags/changelog-baseline-2026-08-15",
            "refs/tags/changelog-baseline-2026-08-15",
            laterCandidate));

        await Assert.That(preOneResult.IsValid).IsTrue();
        await Assert.That(preOneResult.Identity?.BaseStableTag).IsEqualTo("changelog-baseline-2026-08-15");
        await Assert.That(laterResult.IsValid).IsTrue();
        await Assert.That(laterResult.Identity?.BaseStableTag).IsEqualTo("changelog-baseline-2026-08-15");
    }

    [Test]
    public async Task BaselineLowerBoundFailsWhenReachableGovernedStableSemVerTagAlreadyExists()
    {
        using var repo = GitRepositoryFixture.Create();
        string baseline = repo.Commit("baseline");
        repo.AnnotatedTag("changelog-baseline-2026-08-15", baseline);
        string stable = repo.Commit("existing governed stable");
        repo.AnnotatedTag("v0.1.0", stable);
        string candidate = repo.Commit("0.2.0 preparation");
        repo.Branch("v0.2", candidate);

        GitReleaseValidationResult result = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            repo.Path,
            "v0.2",
            "0.2.0",
            "refs/tags/changelog-baseline-2026-08-15",
            "refs/tags/changelog-baseline-2026-08-15",
            candidate));

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Diagnostics).Contains("git_baseline_stable_tag_exists:v0.1.0");
    }

    [Test]
    public async Task MovedDescriptorSelectedTagFailsExpectedCommitIdentity()
    {
        using var repo = GitRepositoryFixture.Create();
        string selectedCommit = repo.Commit("v1.1.0");
        repo.AnnotatedTag("v1.1.0", selectedCommit);
        string selectedTagObject = repo.Resolve("refs/tags/v1.1.0");
        string candidate = repo.Commit("v1.1.1 preparation");
        repo.Branch("v1.1", candidate);
        repo.DeleteTag("v1.1.0");
        repo.AnnotatedTag("v1.1.0", candidate, "moved");

        GitReleaseValidationResult result = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            repo.Path,
            "v1.1",
            "1.1.1",
            BaseStableRef: "refs/tags/v1.1.0",
            PreviousPublishedRef: "refs/tags/v1.1.0",
            CandidateOid: candidate,
            ExpectedTagObjectOids: new Dictionary<string, string> { ["v1.1.0"] = selectedTagObject }));

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Diagnostics).Contains("git_tag_object_mismatch:v1.1.0");
    }

    [Test]
    public async Task FullyPresentPromisorRepositoryPassesButMissingSelectedObjectFailsOffline()
    {
        using var repo = GitRepositoryFixture.Create();
        string v100 = repo.Commit("v1.0.0");
        repo.AnnotatedTag("v1.0.0", v100);
        string candidate = repo.Commit("v1.1.0 preparation");
        repo.Branch("v1.1", candidate);
        repo.MarkPartialClone();

        GitReleaseValidationResult present = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            repo.Path,
            "v1.1",
            "1.1.0",
            BaseStableRef: "refs/tags/v1.0.0",
            PreviousPublishedRef: "refs/tags/v1.0.0",
            CandidateOid: candidate));
        GitReleaseValidationResult missing = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            repo.Path,
            "v1.1",
            "1.1.0",
            BaseStableRef: "refs/tags/v1.0.0",
            PreviousPublishedRef: "refs/tags/v1.0.0",
            CandidateOid: new string('f', candidate.Length)));

        await Assert.That(present.IsValid).IsTrue();
        await Assert.That(present.Diagnostics).DoesNotContain("git_partial_clone_objects_missing");
        await Assert.That(missing.Diagnostics).Contains("git_missing_object:candidate");
        await Assert.That(missing.Diagnostics).Contains("git_partial_clone_objects_missing");
    }

    [Test]
    public async Task PromisorDiagnosticRequiresAnActuallyUnavailableSelectedTagObject()
    {
        using var repo = GitRepositoryFixture.Create();
        string selectedCommit = repo.Commit("v1.1.0");
        repo.AnnotatedTag("v1.1.0", selectedCommit);
        string selectedTagObject = repo.Resolve("refs/tags/v1.1.0");
        string candidate = repo.Commit("v1.1.1 preparation");
        repo.Branch("v1.1", candidate);
        repo.MarkPartialClone();
        var request = new GitReleaseValidationRequest(
            repo.Path,
            "v1.1",
            "1.1.1",
            BaseStableRef: "refs/tags/v1.1.0",
            PreviousPublishedRef: "refs/tags/v1.1.0",
            CandidateOid: candidate);

        GitReleaseValidationResult complete = GitRepositoryValidator.Validate(request);
        repo.RemoveObject(selectedTagObject);
        GitReleaseValidationResult missing = GitRepositoryValidator.Validate(request);

        await Assert.That(complete.IsValid).IsTrue();
        await Assert.That(complete.Diagnostics).DoesNotContain("git_partial_clone_objects_missing");
        await Assert.That(missing.IsValid).IsFalse();
        await Assert.That(missing.Diagnostics).Contains("git_missing_object:v1.1.0");
        await Assert.That(missing.Diagnostics).Contains("git_partial_clone_objects_missing");
    }

    [Test]
    public async Task LightweightReleaseTagsFailClosed()
    {
        using var repo = GitRepositoryFixture.Create();
        string v110 = repo.Commit("v1.1.0");
        repo.LightweightTag("v1.1.0", v110);
        string candidate = repo.Commit("v1.1.1 preparation");
        repo.Branch("v1.1", candidate);

        GitReleaseValidationResult result = GitRepositoryValidator.Validate(Request(repo, "1.1.1", candidate));

        await Assert.That(result.Diagnostics).Contains("git_lightweight_tag:v1.1.0");
    }

    [Test]
    public async Task RepositoryStateSafetyFailuresAreStableDiagnostics()
    {
        using var repo = GitRepositoryFixture.Create();
        string v110 = repo.Commit("v1.1.0");
        repo.AnnotatedTag("v1.1.0", v110);
        string candidate = repo.Commit("v1.1.1 preparation");
        repo.Branch("v1.1", candidate);
        repo.Replace(v110, candidate);
        repo.WriteGraft(candidate, v110);

        GitReleaseValidationResult result = GitRepositoryValidator.Validate(Request(repo, "1.1.1", candidate));

        await Assert.That(result.Diagnostics).Contains("git_replace_refs_present");
        await Assert.That(result.Diagnostics).Contains("git_grafts_present");
    }

    [Test]
    public async Task UnqualifiedRefsMissingObjectsAndWrongLineVersionsFailClosed()
    {
        using var repo = GitRepositoryFixture.Create();
        string v110 = repo.Commit("v1.1.0");
        repo.AnnotatedTag("v1.1.0", v110);
        string candidate = repo.Commit("v1.1.1 preparation");
        repo.Branch("v1.1", candidate);
        repo.LightweightTag("v1.1", candidate);

        GitReleaseValidationResult unqualified = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(repo.Path, "v1.1", "1.1.1", "v1.1.0", "refs/tags/v1.1.0", candidate));
        GitReleaseValidationResult wrongLine = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(repo.Path, "v1.1", "1.2.0", "refs/tags/v1.1.0", "refs/tags/v1.1.0", candidate));
        GitReleaseValidationResult missing = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(repo.Path, "v1.1", "1.1.1", "refs/tags/v1.1.0", "refs/tags/v1.1.0", new string('f', 40)));

        await Assert.That(unqualified.Diagnostics).Contains("git_malformed_tag_ref:v1.1.0");
        await Assert.That(wrongLine.Diagnostics).Contains("git_selected_version_line_mismatch");
        await Assert.That(missing.Diagnostics).Contains("git_missing_object:candidate");
    }

    [Test]
    public async Task ShallowRepositoriesAndNonAncestorPreviousFailClosedWhileOlderCandidatesStillVerify()
    {
        using var source = GitRepositoryFixture.Create();
        string v110 = source.Commit("v1.1.0");
        source.AnnotatedTag("v1.1.0", v110);
        string candidate = source.Commit("v1.1.1 preparation");
        source.Branch("v1.1", candidate);
        using GitRepositoryFixture shallow = source.CloneDepthOne();

        GitReleaseValidationResult shallowResult = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            shallow.Path,
            "v1.1",
            "1.1.1",
            "refs/tags/v1.1.0",
            "refs/tags/v1.1.0",
            candidate));

        using var moved = GitRepositoryFixture.Create();
        string previous = moved.Commit("v1.1.0");
        moved.AnnotatedTag("v1.1.0", previous);
        string oldCandidate = moved.Commit("old preparation");
        string newCandidate = moved.Commit("moved preparation");
        moved.Branch("v1.1", newCandidate);
        // The branch has since advanced to newCandidate. oldCandidate is still a complete, linear,
        // base-tag-descended range end, so it must keep verifying: that is the durability contract.
        GitReleaseValidationResult behindBranchTip = GitRepositoryValidator.Validate(Request(moved, "1.1.1", oldCandidate));

        using var unrelated = GitRepositoryFixture.Create();
        string previousUnrelated = unrelated.Commit("v1.1.0");
        unrelated.AnnotatedTag("v1.1.0", previousUnrelated);
        unrelated.Orphan("release work");
        string unrelatedCandidate = unrelated.Commit("v1.1.1 preparation");
        unrelated.Branch("v1.1", unrelatedCandidate);
        GitReleaseValidationResult unrelatedResult = GitRepositoryValidator.Validate(Request(unrelated, "1.1.1", unrelatedCandidate));

        await Assert.That(shallowResult.Diagnostics).Contains("git_shallow_repository");
        await Assert.That(behindBranchTip.IsValid).IsTrue();
        await Assert.That(unrelatedResult.Diagnostics).Contains("git_previous_not_ancestor");
    }

    [Test]
    public async Task Sha256RepositoryUsesObservedFullObjectLength()
    {
        using var repo = GitRepositoryFixture.Create("sha256");
        string v110 = repo.Commit("v1.1.0");
        repo.AnnotatedTag("v1.1.0", v110);
        string candidate = repo.Commit("v1.1.1 preparation");
        repo.Branch("v1.1", candidate);

        GitReleaseValidationResult result = GitRepositoryValidator.Validate(Request(repo, "1.1.1", candidate));

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Identity?.ObjectFormat).IsEqualTo("sha256");
        await Assert.That(result.Identity?.OidLength).IsEqualTo(candidate.Length);
        await Assert.That(result.Identity?.CandidateOid).IsEqualTo(candidate);
    }

    [Test]
    public async Task SymbolicCandidateRevisionsFailClosedAndVersionTagGlobStaysReserved()
    {
        using var repo = GitRepositoryFixture.Create();
        string v110 = repo.Commit("v1.1.0");
        repo.AnnotatedTag("v1.1.0", v110);
        string candidate = repo.Commit("v1.1.1 preparation");
        repo.Branch("v1.1", candidate);

        GitReleaseValidationResult result = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            repo.Path,
            "v1.1",
            "1.1.1",
            "refs/tags/v1.1.0",
            "refs/tags/v1.1.0",
            "HEAD"));

        await Assert.That(result.Diagnostics).Contains("git_object_id_not_full:candidate");
        await Assert.That(ReleaseRefNamespacePolicy.EvaluateBranchCreation("refs/heads/v1.1").Diagnostic).IsEqualTo("ref_namespace_version_tag_glob_reserved");
    }

    [Test]
    public async Task AmbientWrongLineTagIsIgnoredAndRecreatedTagObjectFailsClosed()
    {
        using var wrongLine = GitRepositoryFixture.Create();
        string v110 = wrongLine.Commit("v1.1.0");
        wrongLine.AnnotatedTag("v1.1.0", v110);
        wrongLine.Orphan("parallel release line");
        string misplaced = wrongLine.Commit("misplaced v1.1.1");
        wrongLine.AnnotatedTag("v1.1.1", misplaced);
        wrongLine.Checkout("main");
        string candidate = wrongLine.Commit("v1.1.2 preparation");
        wrongLine.Branch("v1.1", candidate);

        GitReleaseValidationResult wrongLineResult = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            wrongLine.Path,
            "v1.1",
            "1.1.2",
            "refs/tags/v1.1.0",
            "refs/tags/v1.1.0",
            candidate));

        using var recreated = GitRepositoryFixture.Create();
        string stable = recreated.Commit("v1.1.0");
        recreated.AnnotatedTag("v1.1.0", stable);
        string originalTagObject = recreated.Resolve("refs/tags/v1.1.0");
        recreated.DeleteTag("v1.1.0");
        recreated.AnnotatedTag("v1.1.0", stable, "recreated");
        string recreatedCandidate = recreated.Commit("v1.1.1 preparation");
        recreated.Branch("v1.1", recreatedCandidate);
        GitReleaseValidationResult recreatedResult = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
            recreated.Path,
            "v1.1",
            "1.1.1",
            "refs/tags/v1.1.0",
            "refs/tags/v1.1.0",
            recreatedCandidate,
            ExpectedTagObjectOids: new Dictionary<string, string> { ["v1.1.0"] = originalTagObject }));

        await Assert.That(wrongLineResult.Diagnostics).Contains("git_wrong_line_tag:v1.1.1");
        await Assert.That(recreatedResult.Diagnostics).Contains("git_tag_object_mismatch:v1.1.0");
    }

    [Test]
    public async Task HostileGitConfigurationIsIgnoredAndTimeoutsAreBounded()
    {
        using var repo = GitRepositoryFixture.Create();
        string v110 = repo.Commit("v1.1.0");
        repo.AnnotatedTag("v1.1.0", v110);
        string candidate = repo.Commit("v1.1.1 preparation");
        repo.Branch("v1.1", candidate);
        string hostileConfig = System.IO.Path.Combine(repo.Path, "hostile.gitconfig");
        File.WriteAllText(hostileConfig, "this is not valid git config");
        string? previousGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        string? previousSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_SYSTEM");
        string? previousCount = Environment.GetEnvironmentVariable("GIT_CONFIG_COUNT");
        string? previousKey = Environment.GetEnvironmentVariable("GIT_CONFIG_KEY_0");
        string? previousValue = Environment.GetEnvironmentVariable("GIT_CONFIG_VALUE_0");
        string? previousGitDirectory = Environment.GetEnvironmentVariable("GIT_DIR");
        GitReleaseValidationRequest request = Request(repo, "1.1.1", candidate);

        try
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", hostileConfig);
            Environment.SetEnvironmentVariable("GIT_CONFIG_SYSTEM", hostileConfig);
            Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "1");
            Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "invalid key");
            Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", "hostile");
            Environment.SetEnvironmentVariable("GIT_DIR", System.IO.Path.Combine(repo.Path, "not-the-repository"));
            GitReleaseValidationResult isolated = GitRepositoryValidator.Validate(request);
            GitReleaseValidationResult invalidTimeout = GitRepositoryValidator.Validate(request, TimeSpan.FromMinutes(1));

            await Assert.That(isolated.IsValid).IsTrue();
            await Assert.That(invalidTimeout.Diagnostics).Contains("git_timeout_invalid");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previousGlobal);
            Environment.SetEnvironmentVariable("GIT_CONFIG_SYSTEM", previousSystem);
            Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", previousCount);
            Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", previousKey);
            Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", previousValue);
            Environment.SetEnvironmentVariable("GIT_DIR", previousGitDirectory);
        }
    }

    [Test]
    public async Task GitRunnerUsesExplicitDeterministicEnvironmentAndCleansTemporaryGlobalConfig()
    {
        using var repo = GitRepositoryFixture.Create();
        string v110 = repo.Commit("v1.1.0");
        repo.AnnotatedTag("v1.1.0", v110);
        string candidate = repo.Commit("v1.1.1 preparation");
        repo.Branch("v1.1", candidate);
        string home = System.IO.Path.Combine(repo.Path, "hostile-home");
        Directory.CreateDirectory(home);
        File.WriteAllText(System.IO.Path.Combine(home, ".gitconfig"), "[alias]\nrev-parse = !sh -c 'exit 99'\n");
        string? previousHome = Environment.GetEnvironmentVariable("HOME");
        string[] before = Directory.GetDirectories(System.IO.Path.GetTempPath(), "islamu-release-git-*");

        try
        {
            Environment.SetEnvironmentVariable("HOME", home);
            GitReleaseValidationResult result = GitRepositoryValidator.Validate(Request(repo, "1.1.1", candidate));

            await Assert.That(result.IsValid).IsTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", previousHome);
        }

        string[] after = Directory.GetDirectories(System.IO.Path.GetTempPath(), "islamu-release-git-*");
        await Assert.That(after.Except(before, StringComparer.Ordinal).ToArray()).IsEmpty();
    }

    [Test]
    public async Task Sha256RepositoriesUseGitReportedObjectLengthWhenSupported()
    {
        using GitRepositoryFixture? repo = GitRepositoryFixture.CreateSha256OrNull();
        if (repo is null)
        {
            return;
        }

        string v110 = repo.Commit("v1.1.0");
        repo.AnnotatedTag("v1.1.0", v110);
        string candidate = repo.Commit("v1.1.1 preparation");
        repo.Branch("v1.1", candidate);

        GitReleaseValidationResult result = GitRepositoryValidator.Validate(Request(repo, "1.1.1", candidate));

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Identity?.ObjectFormat).IsEqualTo("sha256");
        await Assert.That(result.Identity?.OidLength).IsEqualTo(64);
        await Assert.That(result.Identity?.CandidateOid?.Length).IsEqualTo(64);
    }

    private static GitReleaseValidationRequest Request(GitRepositoryFixture repo, string version, string candidate) => new(
            repo.Path,
            "v1.1",
            version,
            "refs/tags/v1.1.0",
            "refs/tags/v1.1.0",
            candidate);

    private sealed class GitRepositoryFixture : IDisposable
    {
        private GitRepositoryFixture(string path) => Path = path;

        public string Path { get; }

        public static GitRepositoryFixture Create(string objectFormat = "sha1")
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"islamu-git-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            var fixture = new GitRepositoryFixture(path);
            fixture.Git("init", $"--object-format={objectFormat}", "--initial-branch=main");
            fixture.Commit("initial");
            return fixture;
        }

        public static GitRepositoryFixture? CreateSha256OrNull()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"islamu-git-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            var fixture = new GitRepositoryFixture(path);
            if (!TryRun(path, out _, "init", "--object-format=sha256", "--initial-branch=main"))
            {
                fixture.Dispose();
                return null;
            }

            fixture.Commit("initial");
            return fixture;
        }

        public GitRepositoryFixture CloneDepthOne()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"islamu-git-{Guid.NewGuid():N}");
            Run(null, "clone", "--depth", "1", "--branch", "v1.1", new Uri(Path).AbsoluteUri, path);
            return new GitRepositoryFixture(path);
        }

        public string Commit(string message)
        {
            File.AppendAllText(System.IO.Path.Combine(Path, "file.txt"), message + Environment.NewLine);
            Git("add", "file.txt");
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "commit", "-m", message);
            return Git("rev-parse", "HEAD").Trim();
        }

        public void AnnotatedTag(string name, string target, string? message = null) => Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", name, target, "-m", message ?? name);
        public void LightweightTag(string name, string target) => Git("tag", name, target);
        public void DeleteTag(string name) => Git("tag", "--delete", name);
        public void Branch(string name, string target) => Git("branch", "-f", name, target);
        public void Checkout(string name) => Git("checkout", name);
        public string Resolve(string reference) => Git("rev-parse", "--verify", reference).Trim();
        public void Replace(string oldObject, string newObject) => Git("replace", oldObject, newObject);
        public void MarkPartialClone() => Git("config", "remote.origin.promisor", "true");
        public void RemoveObject(string oid) => File.Delete(System.IO.Path.Combine(Path, ".git", "objects", oid[..2], oid[2..]));
        public void WriteGraft(string child, string parent)
        {
            string info = System.IO.Path.Combine(Path, ".git", "info");
            Directory.CreateDirectory(info);
            File.WriteAllText(System.IO.Path.Combine(info, "grafts"), child + " " + parent + Environment.NewLine);
        }

        public void Orphan(string message)
        {
            Git("checkout", "--orphan", "orphan-work");
            File.WriteAllText(System.IO.Path.Combine(Path, "file.txt"), message + Environment.NewLine);
            Git("add", "file.txt");
        }

        private string Git(params string[] args) => Run(Path, args);

        private static string Run(string? workingDirectory, params string[] args)
        {
            if (TryRun(workingDirectory, out string output, args))
            {
                return output;
            }

            throw new InvalidOperationException($"git {string.Join(' ', args)} failed");
        }

        private static bool TryRun(string? workingDirectory, out string output, params string[] args)
        {
            output = string.Empty;
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                },
            };
            string nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add($"core.hooksPath={nullDevice}");
            foreach (string arg in args)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            process.Start();
            output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
