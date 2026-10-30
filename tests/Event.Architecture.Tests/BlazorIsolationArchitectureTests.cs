using System.CodeDom.Compiler;
using System.Text.Json;
using System.Xml.Linq;
using Mono.Cecil;

namespace Event.Architecture.Tests;

public sealed class BlazorIsolationArchitectureTests
{
    private static readonly string[] BlazorProjects =
    [
        "Explore.Blazor", "Explore.Blazor.Client", "Explore.Blazor.IntegrationTests", "Explore.Blazor.Client.Tests"
    ];

    private static readonly string[] ForbiddenProjects =
    [
        "Explore.API", "Explore.Application", "Explore.Domain", "Explore.Infrastructure", "Explore.Persistence"
    ];

    [Test]
    public async Task BlazorProjects_ShouldNotReferenceApiOwnedLayers()
    {
        var violations = BlazorProjects.SelectMany(project => FindForbiddenProjectReferences(
                Path.Combine(ResolveProjectPath(project), $"{project}.csproj"))
            .Select(forbidden => $"{project} -> {forbidden}")).ToArray();
        await Assert.That(violations).IsEmpty()
            .Because("Blazor projects communicate with the backend through generated API clients only");
    }

    [Test]
    public async Task BlazorAssemblies_ShouldNotDependOnApiOwnedLayers()
    {
        var violations = new List<string>();
        foreach (var project in BlazorProjects)
        {
            using var assembly = CompiledDependencyGraph.ReadProject(project);
            violations.AddRange(assembly.MainModule.Types.Where(HasBackendDependency)
                .Select(type => $"{project}:{type.FullName}"));
        }
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task BlazorProductionBackendContracts_ShouldComeFromGeneratedApiClient()
    {
        using var assembly = CompiledDependencyGraph.ReadProject("Explore.Blazor.Client");
        var interfaces = assembly.MainModule.Types.Where(type => type.IsInterface
            && CompiledDependencyGraph.IsGeneratedContract(type)).ToArray();
        await Assert.That(interfaces).IsNotEmpty();
        var implementations = assembly.MainModule.Types.Where(type => !type.IsInterface
            && CompiledDependencyGraph.IsGeneratedContract(type)
            && type.Interfaces.Any(contract => interfaces.Any(@interface => @interface.FullName == contract.InterfaceType.FullName)))
            .ToArray();
        await Assert.That(implementations).IsNotEmpty();
        var operations = implementations.SelectMany(type => WireOperations(type, interfaces)).ToArray();
        await Assert.That(operations).IsNotEmpty();
        var declaredPayloads = operations.SelectMany(method =>
            method.Parameters.Select(parameter => parameter.ParameterType).Prepend(method.ReturnType));
        var bodyPayloads = implementations.SelectMany(SerializationPayloads).ToArray();
        await Assert.That(bodyPayloads).IsNotEmpty();
        var violations = NonGeneratedPayloads(assembly.MainModule, declaredPayloads.Concat(bodyPayloads));
        await Assert.That(violations).IsEmpty()
            .Because("canonical backend client signatures and actual serialization bodies use generated transport owners");
        await Assert.That(implementations.SelectMany(UnsupportedSerializationCalls).ToArray()).IsEmpty()
            .Because("canonical transport serialization must expose a compiled generic payload identity");
        var guestClient = implementations.Single(type => type.FullName ==
            typeof(Explore.Blazor.Client.Clients.GuestRegistrationOrderClient).FullName);
        var convenience = guestClient.Methods.Single(method => method.Name ==
            nameof(Explore.Blazor.Client.Clients.IGuestRegistrationOrderClient.StartGuestRegistrationOrderWithCapabilityAsync));
        await Assert.That(operations.Any(method => method.FullName == convenience.FullName)).IsFalse();
        await Assert.That(CompiledDependencyGraph.ReachableBodies(convenience)
            .Where(method => method.HasBody).SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand).OfType<MethodReference>()
            .Any(method => method.DeclaringType.FullName == guestClient.FullName && method.Name ==
                nameof(Explore.Blazor.Client.Clients.IGuestRegistrationOrderClient.StartGuestRegistrationOrderAsync))).IsTrue();
        await Assert.That(bodyPayloads.SelectMany(CompiledDependencyGraph.Expand).Any(type => type.FullName ==
            typeof(Explore.Blazor.Client.Clients.GuestRegistrationOrderStartResult).FullName)).IsFalse();
    }

    [Test]
    public async Task BlazorBoundary_RejectsBodyOnlyBackendDependenciesAndHandwrittenTransportPayloads()
    {
        using var assembly = CompiledDependencyGraph.ReadAssembly(typeof(BlazorIsolationArchitectureTests).Assembly.Location);
        var probes = CompiledDependencyGraph.Types(assembly.MainModule)
            .Where(type => type.DeclaringType?.FullName == typeof(BlazorIsolationArchitectureTests).FullName)
            .ToDictionary(type => type.Name, StringComparer.Ordinal);
        await Assert.That(HasBackendDependency(probes[nameof(BackendBodyProbe)])).IsTrue();
        await Assert.That(HasBackendDependency(probes[nameof(LocalPresentation)])).IsFalse();
        await Assert.That(WireOperations(probes[nameof(GeneratedTransportProbe)], [probes[nameof(IGeneratedTransportProbe)]])
            .Select(method => method.Name).ToArray()).IsEquivalentTo([nameof(GeneratedTransportProbe.Send)]);
        var bodyPayloads = SerializationPayloads(probes[nameof(GeneratedTransportProbe)]).ToArray();
        await Assert.That(bodyPayloads).IsNotEmpty();
        await Assert.That(NonGeneratedPayloads(assembly.MainModule, bodyPayloads))
            .Contains(probes[nameof(HandwrittenPayload)].FullName);
        await Assert.That(UnsupportedSerializationCalls(probes[nameof(RuntimeTypeSerializationProbe)]).ToArray()).IsNotEmpty();
    }

    private static bool HasBackendDependency(TypeDefinition type) =>
        CompiledDependencyGraph.Dependencies(type).Any(dependency => ForbiddenProjects.Any(project =>
            dependency.Namespace == project || dependency.Namespace.StartsWith($"{project}.", StringComparison.Ordinal)));

    private static IEnumerable<MethodDefinition> WireOperations(
        TypeDefinition implementation, IEnumerable<TypeDefinition> interfaces)
    {
        var contracts = interfaces.Where(contract => implementation.Interfaces.Any(@interface =>
                @interface.InterfaceType.FullName == contract.FullName))
            .SelectMany(contract => contract.Methods).ToArray();
        var operations = implementation.Methods.Where(method => contracts.Any(contract =>
            method.Name == contract.Name && method.ReturnType.FullName == contract.ReturnType.FullName
            && method.Parameters.Select(parameter => parameter.ParameterType.FullName)
                .SequenceEqual(contract.Parameters.Select(parameter => parameter.ParameterType.FullName))))
            .ToArray();
        var boundaries = operations.Where(method => method.IsPublic)
            .Select(method => method.FullName).ToHashSet(StringComparer.Ordinal);
        return operations.Where(operation => CompiledDependencyGraph.ReachableBodies(operation,
                target => boundaries.Contains(target.FullName))
            .Where(method => method.HasBody).SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand).OfType<MethodReference>()
            .Any(method => method.DeclaringType.FullName == typeof(HttpClient).FullName
                && method.Name == nameof(HttpClient.SendAsync)));
    }

    private static IEnumerable<TypeReference> SerializationPayloads(TypeDefinition type) =>
        CompiledDependencyGraph.Members(type).OfType<GenericInstanceMethod>()
            .Where(method => method.DeclaringType.FullName == typeof(JsonSerializer).FullName
                    && method.Name is nameof(JsonSerializer.Serialize) or nameof(JsonSerializer.SerializeToUtf8Bytes)
                        or nameof(JsonSerializer.Deserialize) or nameof(JsonSerializer.DeserializeAsync)
                || method.Name == "ReadObjectResponseAsync"
                    && method.DeclaringType.FullName == type.FullName)
            .SelectMany(method => method.GenericArguments);

    private static IEnumerable<string> UnsupportedSerializationCalls(TypeDefinition type) =>
        CompiledDependencyGraph.Members(type).OfType<MethodReference>()
            .Where(method => method.DeclaringType.FullName == typeof(JsonSerializer).FullName
                && method.Name is nameof(JsonSerializer.Serialize) or nameof(JsonSerializer.SerializeToUtf8Bytes)
                    or nameof(JsonSerializer.Deserialize) or nameof(JsonSerializer.DeserializeAsync)
                && method is not GenericInstanceMethod)
            .Select(method => method.FullName);

    private static string[] NonGeneratedPayloads(ModuleDefinition module, IEnumerable<TypeReference> payloads)
    {
        var types = CompiledDependencyGraph.Types(module).ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var pending = new Queue<TypeReference>(payloads);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var violations = new List<string>();
        while (pending.TryDequeue(out var payload))
        {
            foreach (var reference in CompiledDependencyGraph.Expand(payload))
            {
                if (!types.TryGetValue(reference.FullName, out var type) || !visited.Add(type.FullName)) continue;
                if (!CompiledDependencyGraph.IsGeneratedContract(type)) violations.Add(type.FullName);
                foreach (var member in type.Properties.Select(property => property.PropertyType)
                    .Concat(type.Fields.Where(field => !field.IsStatic).Select(field => field.FieldType)))
                    pending.Enqueue(member);
                if (type.BaseType is not null) pending.Enqueue(type.BaseType);
            }
        }
        return [.. violations];
    }

    private static IEnumerable<string> FindForbiddenProjectReferences(string projectFile)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(projectFile));
        while (pending.TryPop(out var currentProject))
        {
            if (!visited.Add(currentProject)) continue;
            var projectDirectory = Path.GetDirectoryName(currentProject)!;
            var document = XDocument.Load(currentProject);
            foreach (var reference in document.Descendants()
                .Where(element => element.Name.LocalName is "ProjectReference" or "Reference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include)) continue;
                var forbidden = ForbiddenProjects.FirstOrDefault(project => include.Contains(project, StringComparison.OrdinalIgnoreCase));
                if (forbidden is not null)
                {
                    yield return forbidden;
                    continue;
                }
                if (reference.Name.LocalName == "Reference" || include.Contains("$(", StringComparison.Ordinal)) continue;
                var referencedProject = Path.GetFullPath(Path.Combine(projectDirectory, include));
                if (File.Exists(referencedProject)) pending.Push(referencedProject);
            }
        }
    }

    private static string ResolveProjectPath(string project)
    {
        var source = ContextSystemHelpers.RepoPath(project);
        return Directory.Exists(source) ? source : ContextSystemHelpers.RepoPath("tests", project);
    }

    private static class BackendBodyProbe
    {
        public static Func<Type> Dependency() => () => typeof(Explore.Application.Contracts.Persistence.IUserRepository);
    }

    [GeneratedCode("NSwag", "calibration")]
    private interface IGeneratedTransportProbe
    {
        Task<HttpResponseMessage> Send(HttpClient client);
        Task<HttpResponseMessage> Convenience(HttpClient client);
    }

    [GeneratedCode("NSwag", "calibration")]
    private sealed class GeneratedTransportProbe : IGeneratedTransportProbe
    {
        public Task<HttpResponseMessage> Send(HttpClient client) => SendCore(client);

        public Task<HttpResponseMessage> Convenience(HttpClient client) => Send(client);

        private async Task<HttpResponseMessage> SendCore(HttpClient client)
        {
            await Task.Yield();
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://backend.example.test");
            request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new HandwrittenPayload()));
            return await WireSender<int>.Send(client, request);
        }

        private static class WireSender<TMarker>
        {
            public static Task<HttpResponseMessage> Send(HttpClient client, HttpRequestMessage request) =>
                client.SendAsync(request);
        }
    }

    private sealed class HandwrittenPayload
    {
        public string? Value { get; init; }
    }

    [GeneratedCode("NSwag", "calibration")]
    private static class RuntimeTypeSerializationProbe
    {
        public static string Serialize() => JsonSerializer.Serialize(
            new HandwrittenPayload(), typeof(HandwrittenPayload), (JsonSerializerOptions?)null);
    }

    private sealed class LocalPresentation
    {
        public string? Value { get; init; }
    }
}
