using System.Diagnostics;
using System.IO.Pipes;
using Assembly = System.Reflection.Assembly;
using System.Security;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Explore.GeneratedContracts.Tests;

public sealed class GeneratedContractGraphTests
{
    [Test]
    public async Task OneSolutionGraphMustNotReplaceTheCompiledClientsEmbeddedCapture()
    {
        string root = FindRepositoryRoot();
        string directory = Directory.CreateTempSubdirectory("contract-graph-").FullName;
        string pipeName = "contract-graph-" + Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            string targets = Path.Combine(directory, "probe.targets");
            string solution = Path.Combine(directory, "probe.slnx");
            string schema = Path.Combine(directory, "schema.json");
            string source = Path.Combine(directory, "source.cs");
            string policy = Path.Combine(directory, "policy.txt");
            await File.WriteAllTextAsync(schema, "{}");
            await File.WriteAllTextAsync(source, "public class FirstGeneration {}");
            await File.WriteAllTextAsync(policy, "");
            await File.WriteAllTextAsync(targets, ProbeTargets);
            await File.WriteAllTextAsync(solution, $"""
                <Solution>
                  <Project Path="{SecurityElement.Escape(Path.Combine(root, "src/Explore.API/Explore.API.csproj"))}" />
                  <Project Path="{SecurityElement.Escape(Path.Combine(root, "src/Explore.Blazor.Client/Explore.Blazor.Client.csproj"))}" />
                  <Project Path="{SecurityElement.Escape(Path.Combine(root, "tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj"))}" />
                </Solution>
                """);

            await using var first = new ProbeConnection(pipeName);
            await using var second = new ProbeConnection(pipeName);
            Task firstReady = first.ReceiveAsync(timeout.Token);
            Task secondReady = second.ReceiveAsync(timeout.Token);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(Path.GetFullPath(Path.Combine(
                    System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
                    "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")))
                {
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    ArgumentList =
                    {
                        "msbuild", solution, "-t:ContractGraphProbe", "-m:4", "-v:minimal", "-nr:false",
                        "-p:Configuration=Release",
                        "-p:CustomAfterMicrosoftCommonTargets=" + targets,
                        "-p:ContractProbePipe=" + pipeName,
                        "-p:ContractProbeSchema=" + schema,
                    }
                }
            };
            process.Start();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            Task exit = process.WaitForExitAsync(timeout.Token);
            try
            {
                Task ready = Task.WhenAll(firstReady, secondReady);
                if (await Task.WhenAny(ready, exit) == exit)
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                    throw new InvalidOperationException("Graph probe exited before its barriers: " + await output + await error);
                }
                await ready;
                ProbeConnection client = first.Kind == "client" ? first : second;
                ProbeConnection reference = first.Kind == "reference" ? first : second;
                await Assert.That(client.Kind).IsEqualTo("client");
                await Assert.That(reference.Kind).IsEqualTo("reference");

                // Compile generation A while its real MSBuild client request is held.
                GeneratedContractFiles capture = GeneratedContractCapture.Create(schema, source, policy,
                    Path.Combine(directory, "capture"));
                string compilerInput = await File.ReadAllTextAsync(capture.Client);
                Assembly compiledClient = Compile(compilerInput);
                await Assert.That(compiledClient.GetType("FirstGeneration")).IsNotNull();

                if (!string.IsNullOrEmpty(reference.Properties))
                {
                    await using var competing = new ProbeConnection(pipeName);
                    Task competingReady = competing.ReceiveAsync(timeout.Token);
                    await client.ReleaseAsync(timeout.Token);
                    await reference.ReleaseAsync(timeout.Token);
                    if (await Task.WhenAny(competingReady, exit) == exit)
                    {
                        if (!process.HasExited)
                            process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                        throw new InvalidOperationException("Competing request exited before its barrier: " + await output + await error);
                    }
                    await competingReady;
                    await Assert.That(competing.Kind).IsEqualTo("client");
                    await Assert.That(competing.CaptureDirectory).IsEqualTo(client.CaptureDirectory);
                    await Assert.That(competing.TargetPath).IsEqualTo(client.TargetPath);
                    await File.WriteAllTextAsync(source, "public class CompetingGeneration {}");
                    GeneratedContractCapture.Create(schema, source, policy, Path.GetDirectoryName(capture.Client)!);
                    await competing.ReleaseAsync(timeout.Token);
                }
                else
                {
                    await client.ReleaseAsync(timeout.Token);
                    await reference.ReleaseAsync(timeout.Token);
                }
                await exit;
                await Assert.That(process.ExitCode).IsEqualTo(0).Because(await output + await error);
                Assembly reader = Compile("public class ArchitectureReader {}", capture.Client);
                using var embedded = new StreamReader(reader.GetManifestResourceStream("client-source")!);
                await Assert.That(await embedded.ReadToEndAsync()).IsEqualTo(compilerInput)
                    .Because("architecture resources must describe the actual compiled client, not another instance's later capture");
                await Assert.That(reference.Properties).IsEqualTo(string.Empty);
                await Assert.That(reference.TargetPath).IsEqualTo(string.Empty)
                    .Because("no architecture reference may introduce contract-control globals into transitive builds");
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
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Assembly Compile(string source, string? resource = null)
    {
        CSharpCompilation compilation = CSharpCompilation.Create("GraphProbe" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output, manifestResources: resource is null ? [] :
            [new ResourceDescription("client-source", () => File.OpenRead(resource), isPublic: true)]);
        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(output.ToArray());
    }

    private sealed class ProbeConnection(string name) : IAsyncDisposable
    {
        private readonly NamedPipeServerStream pipe = new(name, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        private StreamReader? reader;
        private StreamWriter? writer;
        public string Kind { get; private set; } = string.Empty;
        public string TargetPath { get; private set; } = string.Empty;
        public string CaptureDirectory { get; private set; } = string.Empty;
        public string Properties { get; private set; } = string.Empty;

        public async Task ReceiveAsync(CancellationToken token)
        {
            await pipe.WaitForConnectionAsync(token);
            reader = new StreamReader(pipe, leaveOpen: true);
            writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            Kind = await reader.ReadLineAsync(token) ?? throw new EndOfStreamException();
            TargetPath = await reader.ReadLineAsync(token) ?? throw new EndOfStreamException();
            CaptureDirectory = await reader.ReadLineAsync(token) ?? throw new EndOfStreamException();
            Properties = await reader.ReadLineAsync(token) ?? throw new EndOfStreamException();
        }

        public Task ReleaseAsync(CancellationToken token) => writer!.WriteLineAsync("continue".AsMemory(), token);

        public async ValueTask DisposeAsync()
        {
            reader?.Dispose();
            if (writer is not null)
                await writer.DisposeAsync();
            await pipe.DisposeAsync();
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    // Substitute only expensive compiler/exporter work. Actual project evaluation,
    // architecture preparation, reference metadata and scheduler identities remain.
    private const string ProbeTargets = """
        <Project>
          <PropertyGroup>
            <OpenApiGenerateDocumentsOnBuild>false</OpenApiGenerateDocumentsOnBuild>
            <ContractProbeDependencies Condition="'$(MSBuildProjectName)' == 'Event.Architecture.Tests'">AssignProjectConfiguration</ContractProbeDependencies>
          </PropertyGroup>
          <UsingTask TaskName="ContractGraphBarrier" TaskFactory="RoslynCodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll">
            <ParameterGroup>
              <PipeName ParameterType="System.String" Required="true" />
              <Kind ParameterType="System.String" Required="true" />
              <OutputPath ParameterType="System.String" />
              <CaptureDirectory ParameterType="System.String" />
              <Properties ParameterType="System.String" />
            </ParameterGroup>
            <Task>
              <Using Namespace="System.IO" />
              <Using Namespace="System.IO.Pipes" />
              <Code Type="Fragment" Language="cs"><![CDATA[
                {
                    using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                    {
                        pipe.Connect(30000);
                        using (var writer = new StreamWriter(pipe, System.Text.Encoding.UTF8, 1024, true))
                        using (var reader = new StreamReader(pipe, System.Text.Encoding.UTF8, true, 1024, true))
                        {
                            writer.WriteLine(Kind);
                            writer.WriteLine(OutputPath ?? "");
                            writer.WriteLine(CaptureDirectory ?? "");
                            writer.WriteLine(Properties ?? "");
                            writer.Flush();
                            if (reader.ReadLine() != "continue")
                                throw new InvalidDataException("Graph barrier was not released.");
                        }
                    }
                }
              ]]></Code>
            </Task>
          </UsingTask>
          <Target Name="Build" />
          <Target Name="GetOpenApiContractInput" Returns="@(ProbeSchema)">
            <ItemGroup><ProbeSchema Include="$(ContractProbeSchema)" /></ItemGroup>
          </Target>
          <Target Name="ContractGraphProbe" DependsOnTargets="$(ContractProbeDependencies)">
            <ItemGroup>
              <ContractReferenceOverride Include="@(ProjectReference)" Condition="'%(ProjectReference.AdditionalProperties)' != ''" />
            </ItemGroup>
            <ContractGraphBarrier PipeName="$(ContractProbePipe)" Kind="client" OutputPath="$(TargetPath)"
                CaptureDirectory="$(GeneratedContractCaptureDirectory)" Properties="$(GeneratedApiSchemaFile)"
                Condition="'$(MSBuildProjectName)' == 'Explore.Blazor.Client'" />
            <ContractGraphBarrier PipeName="$(ContractProbePipe)" Kind="reference"
                OutputPath="@(ContractReferenceOverride->'%(Identity)=%(AdditionalProperties)')"
                Properties="%(ProjectReference.AdditionalProperties)"
                Condition="'$(MSBuildProjectName)' == 'Event.Architecture.Tests' And '%(ProjectReference.Filename)' == 'Explore.Blazor.Client'" />
            <MSBuild Projects="@(ProjectReference)" Targets="ContractGraphProbe" BuildInParallel="true"
                Condition="'$(MSBuildProjectName)' == 'Event.Architecture.Tests'" />
          </Target>
        </Project>
        """;
}
