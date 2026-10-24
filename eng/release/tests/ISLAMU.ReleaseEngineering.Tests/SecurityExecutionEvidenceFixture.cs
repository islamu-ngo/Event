using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace ISLAMU.ReleaseEngineering.Tests;

/// <summary>Produces synthetic admission inputs, not evidence of security test execution.</summary>
internal static class SecurityExecutionEvidenceFixture
{
    internal const string PacketFileName = "security-execution.v1.json";
    private static readonly XNamespace TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static JsonObject OwnedRegister(string shippedRegister)
    {
        var register = JsonNode.Parse(File.ReadAllBytes(shippedRegister))!.AsObject();
        JsonArray requirements = register["executionRequirements"]!.AsArray();
        foreach ((string id, string profile) in new[]
        {
            ("owned-runtime", "owned-apphost-assurance"),
            ("owned-browser-journeys", "owned-browser-assurance"),
            ("owned-browser-security", "owned-browser-assurance"),
            ("owned-load-control", "owned-apphost-load-assurance"),
            ("owned-load-correctness", "owned-apphost-load-assurance")
        })
        {
            foreach (JsonNode? previous in requirements.Where(requirement =>
                requirement?["laneId"]?.GetValue<string>() == id).ToArray())
                requirements.Remove(previous);
            requirements.Add(new JsonObject
            {
                ["laneId"] = id,
                ["project"] = "tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj",
                ["hostProfile"] = profile,
                ["requiredTests"] = new JsonArray(new[] { "alpha", "beta" }.Select(variant => (JsonNode)new JsonObject
                {
                    ["className"] = "Synthetic.OwnedProfile",
                    ["methodName"] = "SyntheticWitness",
                    ["testName"] = $"SyntheticWitness({id}, {variant})"
                }).ToArray())
            });
        }
        return register;
    }

    internal static void Write(
        string releaseDirectory,
        string candidateOid,
        string corruption = "",
        string? repositoryPath = null,
        string? registerPath = null)
    {
        if (repositoryPath is not null)
        {
            File.AppendAllText(Path.Combine(repositoryPath, ".git", "info", "exclude"),
                "\n/docs/internal/releases/*/security-execution.v1.json\n" +
                "/docs/internal/releases/*/security-execution-reports/\n" +
                "/docs/internal/releases/*/proofs/\n");
        }

        string reportDirectory = Path.Combine(releaseDirectory, "security-execution-reports");
        Directory.CreateDirectory(reportDirectory);
        var lanes = new List<object>();

        var requirements = registerPath is null ? SecurityControlRegister.ExecutionRequirements
            : SecurityControlRegister.ReadExecutionRequirements(registerPath);
        foreach ((string id, var requirement) in requirements)
        {
            int count = string.Equals(corruption, "zero-tests", StringComparison.Ordinal) ? 0 : requirement.Tests.Count;
            string reportPath = Path.Combine(reportDirectory, id + ".trx");
            var results = new XElement(TrxNamespace + "Results");
            var definitions = new XElement(TrxNamespace + "TestDefinitions");
            foreach (var test in requirement.Tests.Take(count))
            {
                string testId = Guid.NewGuid().ToString("D");
                string executionId = Guid.NewGuid().ToString("D");
                results.Add(new XElement(TrxNamespace + "UnitTestResult",
                    new XAttribute("testId", testId),
                    new XAttribute("executionId", executionId),
                    new XAttribute("testName", test.TestName),
                    new XAttribute("outcome", "Passed")));
                definitions.Add(new XElement(TrxNamespace + "UnitTest",
                    new XAttribute("id", testId), new XAttribute("name", test.TestName),
                    new XElement(TrxNamespace + "Execution", new XAttribute("id", executionId)),
                    new XElement(TrxNamespace + "TestMethod",
                        new XAttribute("className", test.ClassName), new XAttribute("name", test.MethodName))));
            }

            var report = new XDocument(new XElement(TrxNamespace + "TestRun",
                new XAttribute("id", Guid.NewGuid().ToString("D")),
                results,
                definitions,
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

            var registered = requirement.Tests.OrderBy(test => test.ClassName, StringComparer.Ordinal)
                .ThenBy(test => test.MethodName, StringComparer.Ordinal).ThenBy(test => test.TestName, StringComparer.Ordinal).ToArray();
            byte[] discoveryBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                tests = registered.Select((test, ordinal) => new
                {
                    uid = "synthetic private uid " + ordinal,
                    displayName = test.TestName,
                    type = new
                    {
                        @namespace = test.ClassName[..test.ClassName.LastIndexOf('.')],
                        typeName = test.ClassName[(test.ClassName.LastIndexOf('.') + 1)..], methodName = test.MethodName
                    }
                })
            });
            File.WriteAllBytes(Path.Combine(reportDirectory, id + ".discovery.json"), discoveryBytes);
            var artifacts = new List<object>();
            bool separate = id is "owned-load-control" or "owned-load-correctness";
            int invocations = separate ? registered.Length : 1;
            for (int invocation = 0; invocation < invocations; invocation++)
            {
                var privateReport = new XDocument(report);
                if (separate)
                {
                    privateReport.Root!.SetAttributeValue("id", Guid.NewGuid().ToString("D"));
                    string name = registered[invocation].TestName;
                    privateReport.Root.Element(TrxNamespace + "Results")!.Elements()
                        .Where(result => (string?)result.Attribute("testName") != name).Remove();
                    privateReport.Root.Element(TrxNamespace + "TestDefinitions")!.Elements()
                        .Where(definition => (string?)definition.Attribute("name") != name).Remove();
                    XElement counters = privateReport.Descendants(TrxNamespace + "Counters").Single();
                    foreach (string field in new[] { "total", "executed", "passed" }) counters.SetAttributeValue(field, count == 0 ? 0 : 1);
                }
                using var reportStream = new MemoryStream();
                privateReport.Save(reportStream);
                File.WriteAllBytes(Path.Combine(reportDirectory, id + "." + invocation + ".trx"), reportStream.ToArray());
                int selected = separate ? Math.Min(count, 1) : count;
                string discoveryDigest = Convert.ToHexString(SHA256.HashData(discoveryBytes)).ToLowerInvariant();
                string reportDigest = Convert.ToHexString(SHA256.HashData(reportStream.ToArray())).ToLowerInvariant();
                byte[] proof = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schemaVersion = 1,
                    privateDiscoverySha256 = discoveryDigest,
                    privateTrxSha256 = reportDigest,
                    counts = new { selected, executed = selected, passed = selected, failed = 0, skipped = 0 },
                    nodes = Enumerable.Range(0, selected).Select(ordinal => new
                    {
                        node = ordinal, requiredTest = separate ? invocation : ordinal, outcome = "passed"
                    })
                });
                string relative = "proofs/" + id + "/" + invocation + ".json";
                string destination = Path.Combine(releaseDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, proof);
                byte[] invocationRecord = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schemaVersion = 1, synthetic = true, candidateOid, runId = "42", runAttempt = 1,
                    lane = id, invocation, privateDiscoverySha256 = discoveryDigest, privateTrxSha256 = reportDigest
                });
                File.WriteAllBytes(Path.Combine(reportDirectory, id + "." + invocation + ".invocation.json"), invocationRecord);
                artifacts.Add(new
                {
                    kind = "native-proof", invocation, path = relative,
                    sha256 = Convert.ToHexString(SHA256.HashData(proof)).ToLowerInvariant(),
                    privateInvocationSha256 = Convert.ToHexString(SHA256.HashData(invocationRecord)).ToLowerInvariant()
                });
            }

            lanes.Add(new
            {
                id,
                project = requirement.Project,
                selector = requirement.Selector,
                command = SecurityExecutionEvidence.ExecutionCommandName,
                runId = "42",
                runAttempt = 1,
                outcome = "passed",
                exitCode = 0,
                counts = new { selected = count, executed = count, passed = count, failed = 0, skipped = 0 },
                executionContext = new
                {
                    kind = "test-host",
                    environment = requirement.Environment,
                    hostProfile = requirement.HostProfile,
                    databaseProvider = "not-attested",
                    databaseRole = "not-attested",
                    authenticationScheme = "not-attested",
                    authenticationAuthority = "not-attested"
                },
                artifacts
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
