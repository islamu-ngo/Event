using Mono.Cecil;

namespace Event.Architecture.Tests;

internal static class CompiledDependencyGraph
{
    internal static AssemblyDefinition ReadProject(string project)
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var projectPath = ContextSystemHelpers.RepoPath(project);
        if (!Directory.Exists(projectPath)) projectPath = ContextSystemHelpers.RepoPath("tests", project);
        return ReadAssembly(Path.Combine(
            projectPath, "bin", output.Parent!.Name, output.Name, $"{project}.dll"));
    }

    internal static AssemblyDefinition ReadAssembly(string path)
    {
        var assembly = AssemblyDefinition.ReadAssembly(path);
        var resolver = (DefaultAssemblyResolver)assembly.MainModule.AssemblyResolver;
        resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
        resolver.AddSearchDirectory(AppContext.BaseDirectory);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        return assembly;
    }

    internal static IEnumerable<TypeDefinition> Types(ModuleDefinition module) =>
        module.Types.SelectMany(Types);

    internal static IEnumerable<TypeDefinition> Types(TypeDefinition root) =>
        root.NestedTypes.SelectMany(Types).Prepend(root);

    internal static TypeDefinition Root(TypeDefinition type) =>
        type.DeclaringType is null ? type : Root(type.DeclaringType);

    internal static IEnumerable<TypeReference> Expand(TypeReference type)
    {
        yield return type;
        if (type is GenericInstanceType generic)
        {
            foreach (var argument in generic.GenericArguments.SelectMany(Expand))
                yield return argument;
        }
        if (type is TypeSpecification specification)
        {
            foreach (var element in Expand(specification.ElementType))
                yield return element;
        }
        if (type is not Mono.Cecil.GenericParameter && type.DeclaringType is not null)
        {
            foreach (var declaring in Expand(type.DeclaringType))
                yield return declaring;
        }
    }

    internal static IEnumerable<TypeReference> Dependencies(TypeDefinition root) =>
        Types(root).SelectMany(DeclarationDependencies)
            .Concat(Members(root).SelectMany(MemberDependencies))
            .Concat(Types(root).SelectMany(type => type.Methods)
                .Where(method => method.HasBody)
                .SelectMany(method => method.Body.Variables.Select(variable => variable.VariableType)
                    .Concat(method.Body.ExceptionHandlers.Where(handler => handler.CatchType is not null)
                        .Select(handler => handler.CatchType))))
            .SelectMany(Expand);

    internal static IEnumerable<MemberReference> Members(TypeDefinition root) =>
        Types(root).SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand)
            .OfType<MemberReference>();

    internal static IEnumerable<MethodDefinition> ReachableBodies(
        MethodDefinition entry, Func<MethodDefinition, bool>? stopAt = null)
    {
        var owner = Root(entry.DeclaringType);
        var ownedTypes = Types(owner).ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var methods = ownedTypes.Values.SelectMany(type => type.Methods)
            .ToDictionary(method => method.FullName, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<MethodDefinition>();
        pending.Enqueue(entry);
        while (pending.TryDequeue(out var method))
        {
            if (!visited.Add(method.FullName)) continue;
            yield return method;
            foreach (var reference in method.CustomAttributes
                .Where(attribute => attribute.AttributeType.FullName is
                    "System.Runtime.CompilerServices.AsyncStateMachineAttribute" or
                    "System.Runtime.CompilerServices.IteratorStateMachineAttribute" or
                    "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute")
                .SelectMany(attribute => attribute.ConstructorArguments)
                .Select(argument => argument.Value).OfType<TypeReference>())
            {
                foreach (var generated in Types(owner).Where(type => type.FullName == reference.GetElementType().FullName)
                    .SelectMany(type => type.Methods))
                    pending.Enqueue(generated);
            }
            if (!method.HasBody) continue;
            foreach (var called in method.Body.Instructions.Select(instruction => instruction.Operand)
                .OfType<MethodReference>())
            {
                var definition = called is GenericInstanceMethod generic ? generic.ElementMethod : called;
                if (!methods.TryGetValue(definition.FullName, out var target))
                {
                    var declaring = definition.DeclaringType.GetElementType();
                    if (declaring.Scope != entry.Module || !ownedTypes.ContainsKey(declaring.FullName)) continue;
                    target = definition.Resolve();
                    if (target is null || target.Module != entry.Module || !methods.ContainsKey(target.FullName))
                        throw new InvalidOperationException($"Cannot resolve an owned method definition: {definition.FullName}");
                }
                if (stopAt?.Invoke(target) != true)
                    pending.Enqueue(target);
            }
        }
    }

    internal static bool IsGeneratedContract(TypeDefinition type) =>
        type.CustomAttributes.Any(attribute =>
            attribute.AttributeType.FullName == "System.CodeDom.Compiler.GeneratedCodeAttribute"
            && attribute.ConstructorArguments.Count > 0
            && attribute.ConstructorArguments[0].Value is "NSwag" or "NJsonSchema");

    private static IEnumerable<TypeReference> DeclarationDependencies(TypeDefinition type) =>
        (type.BaseType is null ? [] : new[] { type.BaseType })
            .Concat(type.Interfaces.Select(@interface => @interface.InterfaceType))
            .Concat(type.Fields.Select(field => field.FieldType))
            .Concat(type.Properties.Select(property => property.PropertyType))
            .Concat(type.Events.Select(@event => @event.EventType))
            .Concat(type.GenericParameters.SelectMany(parameter => parameter.Constraints)
                .Select(constraint => constraint.ConstraintType))
            .Concat(type.Methods.SelectMany(method => new[] { method.ReturnType }
                .Concat(method.Parameters.Select(parameter => parameter.ParameterType))
                .Concat(method.GenericParameters.SelectMany(parameter => parameter.Constraints)
                    .Select(constraint => constraint.ConstraintType))))
            .Concat(type.CustomAttributes
                .Concat(type.Methods.SelectMany(method => method.CustomAttributes))
                .Concat(type.Methods.SelectMany(method => method.Parameters).SelectMany(parameter => parameter.CustomAttributes))
                .Concat(type.Methods.SelectMany(method => method.MethodReturnType.CustomAttributes))
                .Concat(type.Fields.SelectMany(field => field.CustomAttributes))
                .Concat(type.Properties.SelectMany(property => property.CustomAttributes))
                .Concat(type.Events.SelectMany(@event => @event.CustomAttributes))
                .SelectMany(AttributeDependencies));

    private static IEnumerable<TypeReference> AttributeDependencies(CustomAttribute attribute) =>
        attribute.ConstructorArguments.Concat(attribute.Fields.Select(field => field.Argument))
            .Concat(attribute.Properties.Select(property => property.Argument))
            .SelectMany(AttributeArgumentDependencies).Prepend(attribute.AttributeType);

    private static IEnumerable<TypeReference> AttributeArgumentDependencies(CustomAttributeArgument argument)
    {
        yield return argument.Type;
        if (argument.Value is TypeReference type) yield return type;
        if (argument.Value is CustomAttributeArgument boxed)
            foreach (var reference in AttributeArgumentDependencies(boxed)) yield return reference;
        if (argument.Value is CustomAttributeArgument[] items)
            foreach (var reference in items.SelectMany(AttributeArgumentDependencies)) yield return reference;
    }

    private static IEnumerable<TypeReference> MemberDependencies(MemberReference member)
    {
        if (member is TypeReference type)
        {
            yield return type;
            yield break;
        }
        yield return member.DeclaringType;
        if (member is FieldReference field) yield return field.FieldType;
        if (member is MethodReference method)
        {
            yield return method.ReturnType;
            foreach (var parameter in method.Parameters) yield return parameter.ParameterType;
            if (method is GenericInstanceMethod generic)
                foreach (var argument in generic.GenericArguments) yield return argument;
        }
    }
}
