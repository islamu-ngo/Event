using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Explore.GeneratedContracts.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["publish-schema", var schema, var destination])
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(schema));
                await GeneratedContractPublication.CopyAsync(schema, destination);
                return 0;
            }

            if (args is ["complete-client", var source, var schemaInput, var policy, var capture, var canonical])
            {
                Diagnostic[] errors = CSharpSyntaxTree.ParseText(File.ReadAllText(source))
                    .GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
                if (errors.Length != 0)
                    throw new InvalidDataException(string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
                using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(schemaInput));
                GeneratedContractFiles files = GeneratedContractCapture.Create(schemaInput, source, policy, capture);
                await GeneratedContractPublication.CopyAsync(files.Client, canonical);
                return 0;
            }

            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: Explore.GeneratedContracts <client.cs> <policy.txt> | publish-schema <schema> <destination> | complete-client <client> <schema> <policy> <capture-directory> <destination>");
                return 2;
            }

            TransformResult result =
                GeneratedContractTransformer.TransformFile(
                    args[0],
                    args[1]);
            Console.WriteLine(
                "Generated record policy: {0} records, {1} init accessors, changed={2}.",
                result.RecordCount,
                result.InitAccessorCount,
                result.Changed);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
