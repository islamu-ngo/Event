using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Explore.GeneratedContracts.Tests;

public sealed class GeneratedClientBuildLifecycleTests
{
    [Test]
    public async Task FinalTargetsHonorOlderSelectedSchemaAndRecoverMissingOutputsWithoutPublishingFailures()
    {
        string directory = Directory.CreateTempSubdirectory("contract-lifecycle-").FullName;
        try
        {
            string schemaA = Path.Combine(directory, "a.json");
            string schemaB = Path.Combine(directory, "b.json");
            string policy = Path.Combine(directory, "policy.txt");
            string published = Path.Combine(directory, "published.cs");
            string capture = Path.Combine(directory, "capture");
            string capturedClient = Path.Combine(capture, "EventApiTagClients.g.cs");
            string capturedSchema = Path.Combine(capture, "openapi_islamu-event.json");
            string stamp = Path.Combine(directory, "complete.stamp");
            await File.WriteAllTextAsync(schemaA, Schema("ContractA"));
            await File.WriteAllTextAsync(schemaB, Schema("ContractB"));
            await File.WriteAllTextAsync(policy, string.Empty);
            DateTime oldTimestamp = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(schemaA, oldTimestamp);
            File.SetLastWriteTimeUtc(schemaB, oldTimestamp);

            string root = RepositoryRoot();
            string[] properties =
            [
                "-p:Configuration=Release", "-p:GeneratedApiClientFile=" + published,
                "-p:GeneratedContractCaptureDirectory=" + capture,
                "-p:GeneratedRecordMutablePolicy=" + policy,
                "-p:GeneratedRecordTransformerStamp=" + stamp,
            ];
            async Task<(int ExitCode, string Output)> Run(string target, string schema) =>
                await RunTarget(root, target, [.. properties, "-p:GeneratedApiSchemaFile=" + schema]);

            var first = await Run("GenerateApiClient", schemaA);
            await Assert.That(first.ExitCode).IsEqualTo(0).Because(first.Output);
            await Assert.That(DeclaredContracts(await File.ReadAllTextAsync(capturedClient))).Contains("ContractA");
            await Assert.That(File.GetLastWriteTimeUtc(schemaB) < File.GetLastWriteTimeUtc(stamp)).IsTrue();

            var second = await Run("GenerateApiClient", schemaB);
            await Assert.That(second.ExitCode).IsEqualTo(0).Because(second.Output);
            await Assert.That(await File.ReadAllTextAsync(capturedSchema)).IsEqualTo(await File.ReadAllTextAsync(schemaB));
            await Assert.That(DeclaredContracts(await File.ReadAllTextAsync(capturedClient))).Contains("ContractB");
            await Assert.That(DeclaredContracts(await File.ReadAllTextAsync(capturedClient))).DoesNotContain("ContractA");
            string complete = await File.ReadAllTextAsync(published);
            await Assert.That(await File.ReadAllTextAsync(capturedClient)).IsEqualTo(complete);

            File.Delete(capturedClient);
            var missing = await Run("GetGeneratedContractInputs", schemaB);
            await Assert.That(missing.ExitCode).IsNotEqualTo(0);
            var recovered = await Run("GenerateApiClient", schemaB);
            await Assert.That(recovered.ExitCode).IsEqualTo(0).Because(recovered.Output);
            await Assert.That(await File.ReadAllTextAsync(capturedClient)).IsEqualTo(complete);

            File.Delete(published);
            var republished = await Run("GenerateApiClient", schemaB);
            await Assert.That(republished.ExitCode).IsEqualTo(0).Because(republished.Output);
            await Assert.That(await File.ReadAllTextAsync(published)).IsEqualTo(complete);

            await File.WriteAllTextAsync(policy, "UnknownGeneratedContract");
            File.Delete(stamp);
            var failed = await Run("GenerateApiClient", schemaB);
            await Assert.That(failed.ExitCode).IsNotEqualTo(0);
            await Assert.That(File.Exists(stamp)).IsFalse();
            await Assert.That(await File.ReadAllTextAsync(published)).IsEqualTo(complete);
            var missingInput = await Run("GenerateApiClient", Path.Combine(directory, "missing.json"));
            await Assert.That(missingInput.ExitCode).IsNotEqualTo(0);
            await Assert.That(await File.ReadAllTextAsync(published)).IsEqualTo(complete);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string[] DeclaredContracts(string source) => CSharpSyntaxTree.ParseText(source).GetRoot()
        .DescendantNodes().OfType<TypeDeclarationSyntax>().Select(type => type.Identifier.ValueText).ToArray();

    private static string Schema(string contract)
    {
        var schemas = new JsonObject();
        for (int index = 0; index < 100; index++)
            schemas.Add("Response" + index, new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["value"] = new JsonObject { ["type"] = "string" } } });
        schemas.Add(contract, new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["marker"] = new JsonObject { ["type"] = "boolean" } } });
        var document = JsonNode.Parse("""
            {"openapi":"3.0.1","info":{"title":"Contract generation fixture","version":"1"},
             "paths":{"/probe":{"get":{"tags":["Probe"],"operationId":"ReadProbe",
             "responses":{"200":{"description":"response","content":{"application/json":{"schema":{}}}}}}}},
             "components":{"schemas":{}}}
            """)!;
        document["components"]!["schemas"] = schemas;
        document["paths"]!["/probe"]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"] =
            new JsonObject { ["$ref"] = "#/components/schemas/" + contract };
        return document.ToJsonString();
    }

    private static async Task<(int ExitCode, string Output)> RunTarget(string root, string target, string[] properties)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "msbuild", "src/Explore.Blazor.Client/Explore.Blazor.Client.csproj", "-t:" + target, "-v:quiet", "-nr:false" }
            }
        };
        foreach (string property in properties)
            process.StartInfo.ArgumentList.Add(property);
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output + await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
