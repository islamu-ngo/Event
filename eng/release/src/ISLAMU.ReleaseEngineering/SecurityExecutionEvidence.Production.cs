using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;

namespace ISLAMU.ReleaseEngineering;

public static partial class SecurityExecutionEvidence
{
    /// <summary>
    /// Retains reports from both successfully completed CI lanes for the tested checkout.
    /// Invocation arguments come from that execution's GitHub environment, not a release target.
    /// </summary>
    public static int Run(string[] args, TextWriter output, string repositoryRoot)
    {
        if (args.Length != 6)
        {
            output.WriteLine("invalid_arguments: produce-security-execution requires <output-directory> <candidate-oid> <run-id> <run-attempt> <event-name>");
            return Program.UsageError;
        }

        string? packetPath = null;
        bool validated = false;
        string diagnostic = "invalid_execution";
        try
        {
            string directory = Path.GetFullPath(args[1], repositoryRoot);
            Directory.CreateDirectory(directory);
            packetPath = ResolveArtifact(directory, PacketFileName);
            File.Delete(packetPath);

            if (!int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int runAttempt))
            {
                output.WriteLine("security_execution_rejected: invalid_invocation");
                return Program.ToolchainRejected;
            }

            string projectDirectory = ResolveArtifact(repositoryRoot, Path.GetDirectoryName(ProjectPath)!.Replace('\\', '/'));
            var lanes = new List<object>();
            foreach ((string id, string category, string filename) in new[]
            {
                ("security-integration", "Security", "security-integration-tests.trx"),
                ("cerbos-policy-contract", "PolicyContract", "cerbos-policy-contract-tests.trx")
            })
            {
                diagnostic = "report_inventory_invalid";
                string[] reports = Directory.EnumerateFiles(projectDirectory, filename, new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = false,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    MatchCasing = MatchCasing.CaseSensitive
                }).Take(2).ToArray();
                if (reports.Length != 1)
                {
                    output.WriteLine("security_execution_rejected: report_inventory_invalid");
                    return Program.ToolchainRejected;
                }

                string relativeReport = Path.GetRelativePath(repositoryRoot, reports[0]).Replace('\\', '/');
                byte[] reportBytes = ReadBoundedFile(ResolveArtifact(repositoryRoot, relativeReport), MaximumReportBytes);
                diagnostic = "nonpassing_execution";
                if (!TryReadReport(reportBytes, out ExecutionCounts counts))
                {
                    output.WriteLine("security_execution_rejected: nonpassing_execution");
                    return Program.ToolchainRejected;
                }

                diagnostic = "artifact_inventory_invalid";
                var artifacts = new List<object>();
                string relativeLog = "artifacts/security-tests/" + Path.ChangeExtension(filename, ".log");
                foreach ((string kind, string relativePath) in new[]
                {
                    ("trx", relativeReport),
                    ("log", relativeLog)
                })
                {
                    string source = ResolveArtifact(repositoryRoot, relativePath);
                    if (kind == "log" && !File.Exists(source)) continue;
                    byte[] bytes = kind == "trx" ? reportBytes : ReadBoundedFile(source, MaximumReportBytes);
                    string destination = ResolveArtifact(directory, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.WriteAllBytes(destination, bytes);
                    artifacts.Add(new
                    {
                        kind,
                        path = relativePath,
                        sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
                    });
                }

                lanes.Add(new
                {
                    id,
                    project = ProjectPath,
                    selector = "/*/*/*/*[Category=" + category + "]",
                    command = Command(id, category),
                    runId = args[3],
                    runAttempt,
                    outcome = "passed",
                    exitCode = 0,
                    counts = new
                    {
                        selected = counts.Selected,
                        executed = counts.Executed,
                        passed = counts.Passed,
                        failed = counts.Failed,
                        skipped = counts.Skipped
                    },
                    executionContext = new
                    {
                        kind = "test-host",
                        environment = "Testing",
                        hostProfile = "dedicated-api-test-host",
                        databaseProvider = "not-attested",
                        databaseRole = "not-attested",
                        authenticationScheme = "not-attested"
                    },
                    artifacts
                });
            }

            byte[] packetBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = "security-execution.v1",
                workflowPath = ".github/workflows/security-tests.yml",
                selection = "executed",
                candidateOid = args[2],
                runId = args[3],
                runAttempt,
                eventName = args[5],
                lanes
            });
            diagnostic = "invalid_packet";
            if (packetBytes.Length > MaximumPacketBytes) throw new InvalidDataException();
            File.WriteAllBytes(packetPath, packetBytes);
            if (!TryValidate(directory, args[2], out string? packetSha256, out diagnostic))
            {
                output.WriteLine("security_execution_rejected: " + diagnostic);
                return Program.ToolchainRejected;
            }

            validated = true;
            output.WriteLine("security_execution_sha256=" + packetSha256);
            return Program.Success;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or XmlException or InvalidOperationException or ArgumentException)
        {
            output.WriteLine("security_execution_rejected: " + diagnostic);
            return Program.ToolchainRejected;
        }
        finally
        {
            if (!validated && packetPath is not null)
            {
                try
                {
                    File.Delete(packetPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    output.WriteLine("security_execution_rejected: packet_cleanup_failed");
                }
            }
        }
    }
}
