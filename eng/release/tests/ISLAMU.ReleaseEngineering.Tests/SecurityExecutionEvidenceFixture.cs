using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace ISLAMU.ReleaseEngineering.Tests;

/// <summary>Produces synthetic admission inputs, not evidence of security test execution.</summary>
internal static class SecurityExecutionEvidenceFixture
{
    internal const string PacketFileName = "security-execution.v1.json";
    private static readonly XNamespace TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static void Write(
        string releaseDirectory,
        string candidateOid,
        string corruption = "",
        string? repositoryPath = null)
    {
        if (repositoryPath is not null)
        {
            File.AppendAllText(Path.Combine(repositoryPath, ".git", "info", "exclude"),
                "\n/docs/internal/releases/*/security-execution.v1.json\n" +
                "/docs/internal/releases/*/security-execution-reports/\n");
        }

        string reportDirectory = Path.Combine(releaseDirectory, "security-execution-reports");
        Directory.CreateDirectory(reportDirectory);
        var lanes = new List<object>();

        foreach ((string id, string category) in new[]
        {
            ("security-integration", "Security"),
            ("cerbos-policy-contract", "PolicyContract")
        })
        {
            int count = string.Equals(corruption, "zero-tests", StringComparison.Ordinal) ? 0 : 1;
            string reportPath = Path.Combine(reportDirectory, id + ".trx");
            var results = new XElement(TrxNamespace + "Results");
            if (count > 0)
            {
                results.Add(new XElement(TrxNamespace + "UnitTestResult",
                    new XAttribute("testId", Guid.NewGuid().ToString("D")),
                    new XAttribute("executionId", Guid.NewGuid().ToString("D")),
                    new XAttribute("testName", id + "-protocol-input"),
                    new XAttribute("outcome", "Passed")));
            }

            var report = new XDocument(new XElement(TrxNamespace + "TestRun",
                results,
                new XElement(TrxNamespace + "ResultSummary",
                    new XAttribute("outcome", "Completed"),
                    new XElement(TrxNamespace + "Counters",
                        new XAttribute("total", count),
                        new XAttribute("executed", count),
                        new XAttribute("passed", count),
                        new XAttribute("failed", 0),
                        new XAttribute("error", 0),
                        new XAttribute("timeout", 0),
                        new XAttribute("aborted", 0),
                        new XAttribute("notExecuted", 0)))));
            report.Save(reportPath);

            string selector = "/*/*/*/*[Category=" + category + "]";
            const string project = "tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj";
            lanes.Add(new
            {
                id,
                project,
                selector,
                command = "dotnet test --project " + project +
                    " --configuration Release --no-build --verbosity minimal -- --treenode-filter \"" + selector +
                    "\" --minimum-expected-tests 1 --no-progress --report-trx --report-trx-filename " +
                    (id == "security-integration" ? "security-integration-tests.trx" : "cerbos-policy-contract-tests.trx"),
                runId = "42",
                runAttempt = 1,
                outcome = "passed",
                exitCode = 0,
                counts = new { selected = count, executed = count, passed = count, failed = 0, skipped = 0 },
                executionContext = new
                {
                    kind = "test-host",
                    environment = "Testing",
                    hostProfile = "dedicated-api-test-host",
                    databaseProvider = "not-attested",
                    databaseRole = "not-attested",
                    authenticationScheme = "not-attested"
                },
                artifacts = new[]
                {
                    new
                    {
                        kind = "trx",
                        path = "security-execution-reports/" + id + ".trx",
                        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(reportPath))).ToLowerInvariant()
                    }
                }
            });
        }

        if (string.Equals(corruption, "duplicate-lane", StringComparison.Ordinal))
        {
            lanes.Add(lanes[0]);
        }

        File.WriteAllText(Path.Combine(releaseDirectory, PacketFileName), JsonSerializer.Serialize(new
        {
            schemaVersion = "security-execution.v1",
            candidateOid,
            workflowPath = ".github/workflows/security-tests.yml",
            runId = "42",
            runAttempt = 1,
            eventName = "push",
            selection = "executed",
            lanes
        }, JsonOptions));
    }
}
