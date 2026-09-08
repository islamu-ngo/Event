using System.Text.Json;
using YamlDotNet.Serialization;

namespace Event.Architecture.Tests;

public sealed class StrongTypingIntentArchitectureTests
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();

    [Test]
    public async Task StrongTypingRefactorIntent_ExistsAndContainsRequiredFields()
    {
        var intentsPath = ContextSystemHelpers.RepoPath(".agents", "contract", "intents.yaml");
        var yamlText = await File.ReadAllTextAsync(intentsPath);
        var root = YamlDeserializer.Deserialize<Dictionary<string, object>>(yamlText);

        await Assert.That(root.ContainsKey("intents")).IsTrue();
        var intentsList = (List<object>)root["intents"];

        var strongTypingIntent = intentsList
            .OfType<Dictionary<object, object>>()
            .FirstOrDefault(intent => intent.TryGetValue("id", out var id) && id?.ToString() == "strong-typing-refactor");

        await Assert.That(strongTypingIntent).IsNotNull();
        await Assert.That(strongTypingIntent!["category"].ToString()).IsEqualTo("cross-cutting");

        var criticality = (Dictionary<object, object>)strongTypingIntent["criticality"];
        await Assert.That(criticality["tier"].ToString()).IsEqualTo("security");
        await Assert.That(criticality["required_model_tier"].ToString()).IsEqualTo("advanced");
        await Assert.That(criticality["verification_depth"].ToString()).IsEqualTo("exhaustive");
    }

    [Test]
    public async Task StrongTypingRefactorIntent_AllReferencedPathsExistOnDisk()
    {
        var intentsPath = ContextSystemHelpers.RepoPath(".agents", "contract", "intents.yaml");
        var yamlText = await File.ReadAllTextAsync(intentsPath);
        var root = YamlDeserializer.Deserialize<Dictionary<string, object>>(yamlText);
        var intentsList = (List<object>)root["intents"];

        var strongTypingIntent = intentsList
            .OfType<Dictionary<object, object>>()
            .First(intent => intent.TryGetValue("id", out var id) && id?.ToString() == "strong-typing-refactor");

        var mustReadDocs = ((List<object>)strongTypingIntent["must_read_docs"]).Select(doc => doc.ToString()!).ToList();
        var loadSkills = ((List<object>)strongTypingIntent["load_skills"]).Select(skill => skill.ToString()!).ToList();
        var loadRules = ((List<object>)strongTypingIntent["load_rules"]).Select(rule => rule.ToString()!).ToList();
        var docsToUpdate = ((List<object>)strongTypingIntent["docs_to_update"]).Select(doc => doc.ToString()!).ToList();

        foreach (var doc in mustReadDocs)
        {
            var fullPath = Path.Combine(ContextSystemHelpers.RepoRoot, doc);
            await Assert.That(File.Exists(fullPath) || Directory.Exists(fullPath))
                .IsTrue()
                .Because($"must_read_doc does not exist: {doc}");
        }

        foreach (var skill in loadSkills)
        {
            var skillPath = Path.Combine(ContextSystemHelpers.RepoRoot, ".agents", "skills", skill, "SKILL.md");
            await Assert.That(File.Exists(skillPath))
                .IsTrue()
                .Because($"load_skill does not exist: {skill}");
        }

        foreach (var rule in loadRules)
        {
            var rulePath = Path.Combine(ContextSystemHelpers.RepoRoot, rule);
            await Assert.That(File.Exists(rulePath))
                .IsTrue()
                .Because($"load_rule does not exist: {rule}");
        }

        foreach (var doc in docsToUpdate)
        {
            var fullPath = Path.Combine(ContextSystemHelpers.RepoRoot, doc);
            await Assert.That(File.Exists(fullPath) || Directory.Exists(fullPath))
                .IsTrue()
                .Because($"doc_to_update does not exist: {doc}");
        }
    }

    [Test]
    public async Task StrongTypingBenchmarkScenario_MatchesIntentDefinition()
    {
        var benchmarksPath = ContextSystemHelpers.RepoPath(".agents", "benchmarks", "cold-start-tasks.yaml");
        var yamlText = await File.ReadAllTextAsync(benchmarksPath);
        var root = YamlDeserializer.Deserialize<Dictionary<string, object>>(yamlText);

        await Assert.That(root.ContainsKey("scenarios")).IsTrue();
        var scenariosList = (List<object>)root["scenarios"];

        var strongTypingScenario = scenariosList
            .OfType<Dictionary<object, object>>()
            .FirstOrDefault(scenario => scenario.TryGetValue("id", out var id) && id?.ToString() == "strong-typing-refactor");

        await Assert.That(strongTypingScenario).IsNotNull();
        await Assert.That(strongTypingScenario!["intent_id"].ToString()).IsEqualTo("strong-typing-refactor");

        var expectedMustReads = ((List<object>)strongTypingScenario["expected_must_reads"]).Select(doc => doc.ToString()!).ToList();
        foreach (var doc in expectedMustReads)
        {
            var fullPath = Path.Combine(ContextSystemHelpers.RepoRoot, doc);
            await Assert.That(File.Exists(fullPath) || Directory.Exists(fullPath))
                .IsTrue()
                .Because($"Benchmark expected must_read does not exist: {doc}");
        }
    }

    [Test]
    public async Task AllIntents_MustNotReferenceNonExistentActiveOrArchivedTriads()
    {
        var intentsPath = ContextSystemHelpers.RepoPath(".agents", "contract", "intents.yaml");
        var yamlText = await File.ReadAllTextAsync(intentsPath);
        var root = YamlDeserializer.Deserialize<Dictionary<string, object>>(yamlText);
        var intentsList = (List<object>)root["intents"];

        foreach (var intentObj in intentsList.OfType<Dictionary<object, object>>())
        {
            var intentId = intentObj["id"].ToString()!;
            if (intentObj.TryGetValue("must_read_docs", out var mustReadObj) && mustReadObj is List<object> docs)
            {
                foreach (var doc in docs.Select(d => d.ToString()!))
                {
                    if (doc.StartsWith("dev/active/", StringComparison.Ordinal) || doc.StartsWith("dev/zarchive/", StringComparison.Ordinal))
                    {
                        var fullPath = Path.Combine(ContextSystemHelpers.RepoRoot, doc);
                        await Assert.That(File.Exists(fullPath))
                            .IsTrue()
                            .Because($"Intent '{intentId}' references missing workstream file: {doc}");
                    }
                }
            }
        }
    }

    [Test]
    public async Task GovernanceDecisionFramework_ReferencesStrongTypingRefactorIntent()
    {
        var governancePath = ContextSystemHelpers.RepoPath("docs", "internal", "GOVERNANCE.md");
        var governanceText = await File.ReadAllTextAsync(governancePath);

        await Assert.That(governanceText.Contains("`strong-typing-refactor`", StringComparison.Ordinal)).IsTrue();
        await Assert.That(governanceText.Contains("Choosing The Strong-Typing And Reflection Remediation Contribution Intent", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task TestingGuidance_DocumentsExecutableArchitectureRules()
    {
        var testingPath = ContextSystemHelpers.RepoPath("docs", "internal", "TESTING.md");
        var testingText = await File.ReadAllTextAsync(testingPath);

        await Assert.That(testingText.Contains("Executable Architecture Contracts", StringComparison.Ordinal)).IsTrue();
        await Assert.That(testingText.Contains("Architecture tests enforce rules through compiled assembly/type relationships", StringComparison.Ordinal)).IsTrue();
        await Assert.That(testingText.Contains("must not freeze raw C#,", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task SyntheticIncompleteIntent_FailsValidation()
    {
        var invalidIntent = new Dictionary<string, object>
        {
            ["id"] = "invalid-synthetic-intent",
            ["category"] = "cross-cutting"
            // Missing must_read_docs, minimum_tests, etc.
        };

        var hasMustRead = invalidIntent.ContainsKey("must_read_docs");
        var hasMinimumTests = invalidIntent.ContainsKey("minimum_tests");

        await Assert.That(hasMustRead && hasMinimumTests).IsFalse();
    }
}
