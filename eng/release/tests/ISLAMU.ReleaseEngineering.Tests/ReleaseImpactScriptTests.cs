using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class ReleaseImpactScriptTests
{
    [Test]
    public async Task ImgBotModifiedAssetsDoNotRequireHumanChecklist()
    {
        ReleaseImpactEvaluationResult result = Evaluate("imgbot[bot]", "Bot", null, 1,
            new ReleaseImpactChangedFile("assets/banner.png", ReleaseImpactFileChangeKind.Modified, null));

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments("imgbot[bot]", "User", "assets/banner.png", "modified")]
    [Arguments("untrusted[bot]", "Bot", "assets/banner.png", "modified")]
    [Arguments("imgbot[bot]", "Bot", "src/page.svg", "modified")]
    [Arguments("imgbot[bot]", "Bot", "assets/banner.png", "added")]
    [Arguments("imgbot[bot]", "Bot", "assets/banner.png", "renamed")]
    [Arguments("imgbot[bot]", "Bot", "assets/security.png", "modified")]
    public async Task AutomaticDispositionRequiresExactIdentityAndSafeModifiedAssets(
        string author, string accountType, string filename, string status)
    {
        ReleaseImpactEvaluationResult result = EvaluateFromJson(author, accountType, null, 1,
            new { filename, status });

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(2)]
    public async Task MissingOrIncompleteFilesCannotProveNoImpact(int expectedCount)
    {
        ReleaseImpactEvaluationResult result = EvaluateFromJson("contributor", "User", "## Release Impact\n- [x] Not applicable\n",
            expectedCount, new { filename = "assets/banner.png", status = "modified" });

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task RenameClassifiesBothOriginalAndCurrentPaths()
    {
        ReleaseImpactEvaluationResult result = EvaluateFromJson("contributor", "User", "## Release Impact\n- [x] Not applicable\n", 1,
            new { filename = "assets/banner.png", status = "renamed", previous_filename = "src/security.cs" });

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task ImgBotMixedSourceAndImageChangesCannotReceiveAutomaticDisposition()
    {
        ReleaseImpactEvaluationResult result = EvaluateFromJson("imgbot[bot]", "Bot", null, 2,
            new { filename = "assets/banner.png", status = "modified" },
            new { filename = "src/security.cs", status = "modified" });

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task EmptyFileMetadataCannotEstablishNoImpact()
    {
        ReleaseImpactEvaluationResult result = EvaluateFromJson("contributor", "User", "## Release Impact\n- [x] Not applicable\n", 1);

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    [Arguments("src/security.cs", "Security/auth impact documented")]
    [Arguments("src/Migrations/Init.cs", "Migration/data/rollback impact documented")]
    [Arguments("appsettings.json", "Configuration/secrets/deployment impact documented")]
    [Arguments("src/Explore.Blazor.Client/Clients/EventApiTagClients.g.cs", "OpenAPI/client contract impact documented")]
    [Arguments("docs/OPERATIONS.md", "Operator/self-hosting/release-note impact documented")]
    public async Task EveryImpactCategoryRequiresItsOwnDisposition(string filename, string label)
    {
        var metadata = new { filename, status = "modified" };
        ReleaseImpactEvaluationResult omitted = EvaluateFromJson("contributor", "User", "## Release Impact\n- [x] Not applicable\n", 1, metadata);
        ReleaseImpactEvaluationResult documented = EvaluateFromJson("contributor", "User",
            $"## Release Impact\n- [x] {label}\nDetails:\nReviewed release impact.\n", 1, metadata);

        await Assert.That(omitted.IsValid).IsFalse();
        await Assert.That(documented.IsValid).IsTrue().Because(documented.Message);
    }

    [Test]
    public async Task HumanNoImpactAndDocumentedSecurityDispositionsRemainValid()
    {
        ReleaseImpactEvaluationResult noImpact = EvaluateFromJson("contributor", "User", "## Release Impact\n- [x] Not applicable\n", 1,
            new { filename = "assets/banner.png", status = "modified" });
        ReleaseImpactEvaluationResult security = EvaluateFromJson("contributor", "User",
            "## Release Impact\n- [x] Security/auth impact documented\nDetails:\nReviewed security change.\n", 1,
            new { filename = "src/security.cs", status = "modified" });

        await Assert.That(noImpact.IsValid).IsTrue();
        await Assert.That(security.IsValid).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LargePullRequestRequiresEveryFileInProcessWithoutCompilerSpawns(bool omitLastFile)
    {
        object[] files = Enumerable.Range(0, omitLastFile ? 3000 : 3001)
            .Select(index => (object)new
            {
                filename = $"assets/reviewed-image-with-a-long-enough-name-to-exceed-environment-limits-{index}.png",
                status = "modified",
            }).ToArray();

        ReleaseImpactEvaluationResult result = EvaluateFromJson("contributor", "User",
            "## Release Impact\n- [x] Not applicable\n", 3001, files);

        await Assert.That(result.IsValid).IsEqualTo(!omitLastFile);
    }

    [Test]
    public async Task TrustedBotsSkipReleaseImpactValidation()
    {
        ReleaseImpactEvaluationResult dependabot = Evaluate("dependabot[bot]", "Bot", null, 0);
        ReleaseImpactEvaluationResult actionsBot = Evaluate("github-actions[bot]", "Bot", null, 0);

        await Assert.That(dependabot.IsValid).IsTrue();
        await Assert.That(actionsBot.IsValid).IsTrue();
    }

    private static ReleaseImpactEvaluationResult Evaluate(
        string author,
        string accountType,
        string? body,
        int expectedCount,
        params ReleaseImpactChangedFile[] files) =>
        ReleaseImpactPolicy.Evaluate(new ReleaseImpactEvaluationRequest(
            author,
            string.Equals(accountType, "Bot", StringComparison.Ordinal),
            body ?? string.Empty,
            expectedCount,
            files));

    private static ReleaseImpactEvaluationResult EvaluateFromJson(
        string author,
        string accountType,
        string? body,
        int expectedCount,
        params object[] files)
    {
        string eventJson = JsonSerializer.Serialize(new
        {
            pull_request = new
            {
                user = new { login = author, type = accountType },
                body,
                changed_files = expectedCount,
            },
        });
        string filesJson = JsonSerializer.Serialize(files);
        return ReleaseImpactPolicy.EvaluateFromJson(eventJson, filesJson);
    }
}
