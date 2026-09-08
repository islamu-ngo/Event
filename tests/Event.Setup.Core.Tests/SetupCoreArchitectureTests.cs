namespace ISLAMU.Setup.Core.Tests;

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using ISLAMU.Event.Setup.Core;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class SetupCoreArchitectureTests
{
    private static readonly Assembly ProductAssembly = typeof(SetupProfile).Assembly;

    [Test]
    public async Task ProductAssemblyRemainsWireBclAndApprovedYamlOnlyWithoutAmbientAuthority()
    {
        string[] violations = SetupCoreAssemblyVerifier.VerifyAssembly(ProductAssembly);

        await Assert.That(violations).IsEmpty()
            .Because(string.Join("; ", violations));
    }

    [Test]
    public async Task OfflinePortabilityClosureHasNoDotenvOrLiveTargetCapability()
    {
        string[] violations = SetupCoreAssemblyVerifier.VerifyOfflinePortabilityClosure(ProductAssembly);

        await Assert.That(violations).IsEmpty()
            .Because(string.Join("; ", violations));
    }

    [Test]
    public async Task SetupLiveWireVocabularyDoesNotCreateCoreLiveAuthority()
    {
        Assembly wireAssembly = typeof(ConfigurationManifestV1Alpha2).Assembly;
        string[] required =
        [
            "ISLAMU.Wire.Contracts.SetupLive.CreateSetupTargetEnrollmentRequest",
            "ISLAMU.Wire.Contracts.SetupLive.SetupTargetEnrollmentData",
            "ISLAMU.Wire.Contracts.SetupLive.SetupSecretBindingReadinessItem",
            "ISLAMU.Wire.Contracts.SetupLive.SetupSecretBindingOperationData"
        ];
        string[] missing = required
            .Where(name => wireAssembly.GetType(name) is null)
            .ToArray();
        string[] leakedCoreAuthority = ProductAssembly.GetExportedTypes()
            .Where(type => type.Namespace?.Contains(
                "SetupLive", StringComparison.Ordinal) == true)
            .Select(type => type.FullName!)
            .ToArray();

        await Assert.That(missing).IsEmpty()
            .Because("D2-1 requires the package-free Wire vocabulary: "
                + string.Join(", ", missing));
        await Assert.That(leakedCoreAuthority).IsEmpty()
            .Because("Setup Core remains offline and cannot own live authority: "
                + string.Join(", ", leakedCoreAuthority));
    }

    [Test]
    public async Task CompiledVerifierRejectsAmbientCallsMutableCollectionsAndLeakingDiagnostics()
    {
        string[] ambient = SetupCoreAssemblyVerifier.VerifyTypes([typeof(SyntheticAmbientFixture)]);
        string[] mutable = SetupCoreAssemblyVerifier.VerifyTypes([typeof(SyntheticMutableFixture)]);
        string[] leaking = SetupCoreAssemblyVerifier.VerifyTypes([typeof(SyntheticLeakingDiagnostic)]);

        await Assert.That(ambient.Any(item => item.Contains("UtcNow", StringComparison.Ordinal))).IsTrue();
        await Assert.That(mutable.Any(item => item.Contains("mutable collection", StringComparison.Ordinal))).IsTrue();
        await Assert.That(leaking.Any(item => item.Contains("diagnostic field", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task CompiledVerifierAllowsOnlyExactAssemblyNamespaceAndInternalIoRoles()
    {
        string[] assemblyViolations = SetupCoreAssemblyVerifier.VerifyAssemblyReferences(
        [
            new AssemblyName("System.Runtime"),
            new AssemblyName("netstandard"),
            new AssemblyName("Event.Wire.Contracts"),
            new AssemblyName("YamlDotNet"),
            new AssemblyName("Unapproved.Dependency")
        ]);
        SyntheticNamespaceCanaries canaries = SyntheticVerifierFactory.CreateNamespaceCanaries();
        string[] exact = SetupCoreAssemblyVerifier.VerifyTypes(
            [canaries.ApprovedIo], canaries.Assembly);
        string[] denied = SetupCoreAssemblyVerifier.VerifyTypes(
            [canaries.ChildNamespaceIo, canaries.RootNamespaceIo,
                canaries.PublicIo, canaries.Network], canaries.Assembly);

        await Assert.That(assemblyViolations).HasSingleItem();
        await Assert.That(assemblyViolations[0]).Contains("Unapproved.Dependency");
        await Assert.That(exact).IsEmpty().Because(string.Join("; ", exact));
        await Assert.That(denied.Any(item =>
            item.Contains(canaries.ChildNamespaceIo.FullName!, StringComparison.Ordinal)
            && item.Contains("namespace", StringComparison.Ordinal))).IsTrue();
        await Assert.That(denied.Any(item =>
            item.Contains(canaries.ChildNamespaceIo.FullName!, StringComparison.Ordinal)
            && item.Contains("ambient call", StringComparison.Ordinal))).IsTrue();
        await Assert.That(denied.Any(item =>
            item.Contains(canaries.RootNamespaceIo.FullName!, StringComparison.Ordinal)
            && item.Contains("ambient call", StringComparison.Ordinal))).IsTrue();
        await Assert.That(denied.Any(item =>
            item.Contains(canaries.PublicIo.FullName!, StringComparison.Ordinal)
            && item.Contains("public method dependency", StringComparison.Ordinal))).IsTrue();
        await Assert.That(denied.Any(item =>
            item.Contains(canaries.Network.FullName!, StringComparison.Ordinal)
            && item.Contains("ambient call", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task CompiledVerifierAllowsParserEventsAndRejectsEveryForbiddenYamlRole()
    {
        SyntheticYamlCanaries canaries = SyntheticVerifierFactory.CreateYamlCanaries();
        string[] approved = SetupCoreAssemblyVerifier.VerifyTypes(
            [canaries.Approved], canaries.Assembly);
        string[] denied = SetupCoreAssemblyVerifier.VerifyTypes(
            canaries.Forbidden, canaries.Assembly);

        await Assert.That(approved).IsEmpty().Because(string.Join("; ", approved));
        foreach (Type forbidden in canaries.Forbidden)
            await Assert.That(denied.Any(item =>
                item.Contains(forbidden.FullName!, StringComparison.Ordinal)
                && item.Contains("forbidden yaml role", StringComparison.Ordinal))).IsTrue();
        await Assert.That(denied.Any(item =>
            item.Contains(canaries.ConstructorAndLocal.FullName!, StringComparison.Ordinal)
            && item.Contains("forbidden yaml role", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task PublicCollectionInputsAreSnapshottedAndInvariantlyOrdered()
    {
        var capabilities = new List<SetupCapabilityKey>
        {
            new("legal"), new("configuration"), new("legal")
        };
        var topology = new List<SetupTopologyKey> { new("split"), new("single") };
        var sections = new List<PortableSectionKey>
        {
            new("tenant.settings"), new("instance.documents")
        };
        var profile = new SetupProfile(new SetupProfileIdentity("offline"), capabilities, topology);
        var selection = new SetupSelection(
            SetupScope.Instance,
            ConfigurationImportApplyMode.ApplySelected,
            sections);

        capabilities.Clear();
        topology.Add(new SetupTopologyKey("added"));
        sections[0] = new PortableSectionKey("extensions");

        await Assert.That(string.Join(",", profile.Capabilities.Select(item => item.Value)))
            .IsEqualTo("configuration,legal");
        await Assert.That(string.Join(",", profile.Topology.Select(item => item.Value)))
            .IsEqualTo("single,split");
        await Assert.That(string.Join(",", selection.Sections.Select(item => item.Value)))
            .IsEqualTo("instance.documents,tenant.settings");
        await Assert.That(((IList)profile.Capabilities).IsReadOnly).IsTrue();
    }

    [Test]
    public async Task DigestEqualityFormattingAndOrderingAreCultureIndependentAndRepeatable()
    {
        const string expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string cultureName in new[] { "tr-TR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
                ArtifactDigest computed = ArtifactDigest.Compute("abc"u8);
                ArtifactDigest parsed = ArtifactDigest.Parse(expected.ToUpperInvariant());
                var keys = new[]
                {
                    new PortableSectionKey("tenant.settings"),
                    new PortableSectionKey("instance.settings")
                }.Order().Select(item => item.Value).ToArray();

                await Assert.That(computed.ToString()).IsEqualTo(expected);
                await Assert.That(parsed).IsEqualTo(computed);
                await Assert.That(string.Join(",", keys))
                    .IsEqualTo("instance.settings,tenant.settings");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Test]
    public async Task ReadinessDerivesReadyIncompleteAndBlockedWithoutValues()
    {
        PortableSectionKey instance = new("instance.settings");
        PortableSectionKey tenant = new("tenant.settings");
        SetupReadinessResult ready = SetupReadiness.Evaluate(
            new SetupReadinessInput([instance], [instance, tenant], []));
        SetupReadinessResult incomplete = SetupReadiness.Evaluate(
            new SetupReadinessInput([tenant, instance], [tenant], []));
        SetupReadinessResult blocked = SetupReadiness.Evaluate(
            new SetupReadinessInput([instance], [instance], [tenant]));

        await Assert.That(ready.State).IsEqualTo(SetupReadinessState.Ready);
        await Assert.That(incomplete.State).IsEqualTo(SetupReadinessState.Incomplete);
        await Assert.That(incomplete.Missing.Select(item => item.Value))
            .IsEquivalentTo(["instance.settings"]);
        await Assert.That(blocked.State).IsEqualTo(SetupReadinessState.Blocked);
        await Assert.That(blocked.Blocked.Select(item => item.Value))
            .IsEquivalentTo(["tenant.settings"]);
    }

    [Test]
    public async Task DiffAndCoverageUseOnlyOrderedSectionKeysAndDigests()
    {
        PortableSectionKey a = new("instance.documents");
        PortableSectionKey b = new("instance.settings");
        PortableSectionKey c = new("tenant.settings");
        ArtifactDigest first = ArtifactDigest.Compute("first"u8);
        ArtifactDigest second = ArtifactDigest.Compute("second"u8);
        var baseline = new Dictionary<PortableSectionKey, ArtifactDigest>
        {
            [b] = first, [a] = first
        };
        var candidate = new Dictionary<PortableSectionKey, ArtifactDigest>
        {
            [c] = second, [b] = second
        };
        SetupDiffInput input = new(baseline, candidate);
        SetupCoverageInput coverageInput = new([c, a, b], [b, a]);

        baseline.Clear();
        candidate.Clear();
        SetupDiffResult diff = SetupDiff.Compare(input);
        SetupCoverageResult coverage = SetupCoverage.Calculate(coverageInput);

        await Assert.That(diff.Added.Select(item => item.Value)).IsEquivalentTo(["tenant.settings"]);
        await Assert.That(diff.Removed.Select(item => item.Value)).IsEquivalentTo(["instance.documents"]);
        await Assert.That(diff.Changed.Select(item => item.Value)).IsEquivalentTo(["instance.settings"]);
        await Assert.That(diff.Unchanged).IsEmpty();
        await Assert.That(coverage.Covered.Select(item => item.Value))
            .IsEquivalentTo(["instance.documents", "instance.settings"]);
        await Assert.That(coverage.Missing.Select(item => item.Value))
            .IsEquivalentTo(["tenant.settings"]);
        await Assert.That(coverage.IsComplete).IsFalse();
    }

    [Test]
    public async Task WorkflowTransitionsAreExplicitRepeatableAndInvalidTransitionsAreValueSafe()
    {
        SetupTransitionResult validated = SetupWorkflow.Transition(
            SetupWorkflowState.Draft, SetupWorkflowAction.Validate);
        SetupTransitionResult ready = SetupWorkflow.Transition(
            validated.State, SetupWorkflowAction.MarkReady);
        SetupTransitionResult exported = SetupWorkflow.Transition(
            ready.State, SetupWorkflowAction.Export);
        SetupTransitionResult invalid = SetupWorkflow.Transition(
            SetupWorkflowState.Draft, SetupWorkflowAction.Export);
        SetupTransitionResult repeated = SetupWorkflow.Transition(
            SetupWorkflowState.Draft, SetupWorkflowAction.Export);

        await Assert.That(exported.State).IsEqualTo(SetupWorkflowState.Exported);
        await Assert.That(exported.Succeeded).IsTrue();
        await Assert.That(invalid.Succeeded).IsFalse();
        await Assert.That(invalid.State).IsEqualTo(SetupWorkflowState.Draft);
        await Assert.That(invalid.Diagnostic).IsEqualTo(repeated.Diagnostic);
        await Assert.That(invalid.Diagnostic!.Code.Value).IsEqualTo("invalid-transition");
        await Assert.That(invalid.Diagnostic.Path.Value).IsEqualTo("$.workflow.state");
        await Assert.That(invalid.Diagnostic.GetType().GetProperties().Select(property => property.Name))
            .IsEquivalentTo(["Code", "Path", "Severity"]);
    }
}

internal static class SetupCoreAssemblyVerifier
{
    private const string CompositionNamespace = "ISLAMU.Event.Setup.Core.Composition";

    private static readonly string[] ForbiddenPublicFragments =
    [
        "Password", "Secret", "Pii", "Credential", "ConnectionString",
        "Email", "UserId", "TenantId", "Provider", "DeploymentCoordinate",
        "EnvironmentValue", "LiveAuthority", "Message", "SuppliedValue"
    ];

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    private static readonly HashSet<string> ApprovedYamlTypes = new(StringComparer.Ordinal)
    {
        "YamlDotNet.Core.AnchorName",
        "YamlDotNet.Core.IParser",
        "YamlDotNet.Core.Mark",
        "YamlDotNet.Core.Parser",
        "YamlDotNet.Core.ScalarStyle",
        "YamlDotNet.Core.TagDirectiveCollection",
        "YamlDotNet.Core.TagName",
        "YamlDotNet.Core.YamlException",
        "YamlDotNet.Core.Tokens.TagDirective",
        "YamlDotNet.Core.Tokens.VersionDirective"
    };

    internal static string[] VerifyAssembly(Assembly assembly)
    {
        var failures = new List<string>();
        failures.AddRange(VerifyAssemblyReferences(assembly.GetReferencedAssemblies()));
        failures.AddRange(VerifyTypes(assembly.GetTypes(), assembly));
        return failures.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    internal static string[] VerifyAssemblyReferences(IEnumerable<AssemblyName> references) =>
        references.Where(reference => reference.Name is not null
                && !reference.Name.StartsWith("System", StringComparison.Ordinal)
                && reference.Name is not "netstandard" and not "Event.Wire.Contracts" and not "YamlDotNet")
            .Select(reference => $"forbidden assembly reference {reference.Name}")
            .ToArray();

    internal static string[] VerifyOfflinePortabilityClosure(Assembly assembly)
    {
        Type[] types = assembly.GetExportedTypes().Where(type =>
            type.Name.StartsWith("OfflinePortability", StringComparison.Ordinal)
            || type.Name.StartsWith("OfflineLegal", StringComparison.Ordinal)).ToArray();
        string[] forbiddenCapabilities =
        [
            "Dotenv", "EnvironmentValue", "Secret", "Credential", "Provider",
            "Deployment", "Topology", "Target", "Mapping", "Live", "Apply",
            "Publish", "Schedule", "Accept", "Consent", "UserId", "TenantId"
        ];
        var failures = new List<string>();
        foreach (Type type in types)
        {
            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.Name != "get_RequiresFreshAcceptance"
                    && forbiddenCapabilities.Any(fragment =>
                        method.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                    failures.Add($"offline authority method {type.FullName}.{method.Name}");
                foreach (Type dependency in method.GetParameters().Select(parameter => parameter.ParameterType)
                             .Append(method.ReturnType))
                    if (dependency.FullName?.Contains("Dotenv", StringComparison.OrdinalIgnoreCase) == true)
                        failures.Add($"offline dotenv dependency {type.FullName}.{method.Name}");
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType.FullName?.Contains("Dotenv", StringComparison.OrdinalIgnoreCase) == true)
                    failures.Add($"offline dotenv property {type.FullName}.{property.Name}");
                if (property.Name != "RequiresFreshAcceptance"
                    && forbiddenCapabilities.Any(fragment =>
                        property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                    failures.Add($"offline authority property {type.FullName}.{property.Name}");
            }
        }

        return failures.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    internal static string[] VerifyTypes(IEnumerable<Type> types, Assembly? reviewedAssembly = null)
    {
        Type[] closure = types.ToArray();
        reviewedAssembly ??= typeof(SetupProfile).Assembly;
        var failures = new List<string>();
        foreach (Type type in closure)
        {
            bool isPublicContract = type.IsPublic || type.IsNestedPublic;
            bool isEnvironmentContract = string.Equals(
                type.Namespace, "ISLAMU.Event.Setup.Core.Environment", StringComparison.Ordinal);
            bool isCompositionContract = string.Equals(
                type.Namespace, CompositionNamespace, StringComparison.Ordinal);
            if (isPublicContract
                && IsReviewedAssembly(type.Assembly, reviewedAssembly)
                && type.Namespace != "ISLAMU.Event.Setup.Core"
                && !isEnvironmentContract
                && !isCompositionContract)
                failures.Add($"forbidden public namespace {type.FullName}");

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
                    && !field.IsLiteral
                    && !field.IsInitOnly)
                    failures.Add($"writable static field {type.FullName}.{field.Name}");
            }

            if (isPublicContract && HasForbiddenPublicName(type.Name)
                && !(isEnvironmentContract && IsApprovedEnvironmentName(type.Name)))
                failures.Add($"forbidden public type claim {type.FullName}");

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (isPublicContract && IsMutableCollection(property.PropertyType))
                    failures.Add($"mutable collection {type.FullName}.{property.Name}");
                if (isPublicContract && HasForbiddenPublicName(property.Name)
                    && !(isEnvironmentContract && IsApprovedEnvironmentName(property.Name)))
                    failures.Add($"forbidden public member claim {type.FullName}.{property.Name}");
                if (isPublicContract && IsForbiddenDependency(property.PropertyType))
                    failures.Add($"forbidden public dependency {type.FullName}.{property.Name}");
                if (isPublicContract
                    && type.Name.EndsWith("Diagnostic", StringComparison.Ordinal)
                    && (isEnvironmentContract
                        ? property.Name is not "Code" and not "Path" and not "Key" and not "Category"
                        : property.Name is not "Code" and not "Path" and not "Severity"))
                    failures.Add($"value-bearing diagnostic field {type.FullName}.{property.Name}");
            }

            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (isPublicContract
                    && ((HasForbiddenPublicName(method.Name)
                        && !(isEnvironmentContract && IsApprovedEnvironmentName(method.Name)))
                    || method.GetParameters().Any(parameter =>
                        HasForbiddenPublicName(parameter.Name ?? string.Empty)
                        && !(isEnvironmentContract && IsApprovedEnvironmentName(parameter.Name ?? string.Empty)))))
                    failures.Add($"forbidden public method claim {type.FullName}.{method.Name}");
                if (isPublicContract
                    && (IsForbiddenDependency(method.ReturnType)
                        || method.GetParameters().Any(parameter => IsForbiddenDependency(parameter.ParameterType))))
                    failures.Add($"forbidden public method dependency {type.FullName}.{method.Name}");
            }

            foreach (MethodBase method in ImplementationBodies(type))
            {
                foreach (MethodBase called in CalledMethods(method).Where(IsAmbientCall))
                {
                    if (!IsApprovedAmbientCall(type, called, reviewedAssembly))
                        failures.Add($"ambient call {type.FullName}.{method.Name} -> {called.DeclaringType?.FullName}.{called.Name}");
                }
            }

            foreach (Type dependency in ReferencedTypes(type)
                         .SelectMany(TypeClosure)
                         .Where(IsForbiddenYamlRole))
                failures.Add($"forbidden yaml role {type.FullName} -> {dependency.FullName}");
        }

        return failures.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool HasForbiddenPublicName(string name) =>
        ForbiddenPublicFragments.Any(fragment =>
            name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static bool IsApprovedEnvironmentName(string name) => name is
        "Provider" or "Providers" or "get_Providers"
        or "SecretBindingEnvironmentKeys" or "get_SecretBindingEnvironmentKeys"
        or "secretBindingEnvironmentKeys" or "IsSecret" or "get_IsSecret" or "isSecret"
        or "Value" or "get_Value" or "value" or "CopyValue" or "CopyUtf8Bytes"
        or "GeneratedDotenvValue" or "LocalSecretGenerationProfile"
        or "LocalSecretGenerator" or "LocalSecretGenerationResult"
        or "ComposeNoSecrets" or "ComposeWithSecrets";

    private static bool IsForbiddenDependency(Type type)
    {
        return TypeClosure(type).Any(static dependency =>
        {
            string typeName = dependency.FullName ?? string.Empty;
            string typeNamespace = dependency.Namespace ?? string.Empty;
            return typeNamespace.StartsWith("System.IO", StringComparison.Ordinal)
                || typeNamespace.StartsWith("System.Net", StringComparison.Ordinal)
                || typeName == "System.Diagnostics.Process"
                || typeName.StartsWith("Microsoft.Extensions", StringComparison.Ordinal)
                || typeName.StartsWith("System.Reflection", StringComparison.Ordinal);
        });
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags fields = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        if (type.BaseType is not null)
            yield return type.BaseType;
        foreach (Type dependency in type.GetInterfaces())
            yield return dependency;
        foreach (FieldInfo field in type.GetFields(fields))
            yield return field.FieldType;
        foreach (PropertyInfo property in type.GetProperties(fields))
            yield return property.PropertyType;

        foreach (MethodBase method in type.GetMethods(fields).Cast<MethodBase>()
                     .Concat(type.GetConstructors(fields)))
        {
            if (method is MethodInfo methodInfo)
                yield return methodInfo.ReturnType;
            foreach (ParameterInfo parameter in method.GetParameters())
                yield return parameter.ParameterType;
            foreach (LocalVariableInfo local in method.GetMethodBody()?.LocalVariables ?? [])
                yield return local.LocalType;
            foreach (MethodBase called in CalledMethods(method))
            {
                if (called.DeclaringType is not null)
                    yield return called.DeclaringType;
                if (called is MethodInfo calledMethod)
                    yield return calledMethod.ReturnType;
                foreach (ParameterInfo parameter in called.GetParameters())
                    yield return parameter.ParameterType;
            }
        }
    }

    private static IEnumerable<Type> TypeClosure(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (Type dependency in TypeClosure(element))
                yield return dependency;
        }
        foreach (Type argument in type.IsGenericType ? type.GetGenericArguments() : [])
        {
            foreach (Type dependency in TypeClosure(argument))
                yield return dependency;
        }
    }

    private static bool IsForbiddenYamlRole(Type type)
    {
        string typeNamespace = type.Namespace ?? string.Empty;
        if (!typeNamespace.Equals("YamlDotNet", StringComparison.Ordinal)
            && !typeNamespace.StartsWith("YamlDotNet.", StringComparison.Ordinal))
            return false;
        return typeNamespace != "YamlDotNet.Core.Events"
            && !ApprovedYamlTypes.Contains(type.FullName ?? string.Empty);
    }

    private static bool IsMutableCollection(Type type)
    {
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
            return false;
        if (type.IsArray || typeof(IList).IsAssignableFrom(type) || typeof(IDictionary).IsAssignableFrom(type))
            return true;
        if (!type.IsGenericType)
            return false;
        Type definition = type.GetGenericTypeDefinition();
        return definition == typeof(List<>)
            || definition == typeof(Dictionary<,>)
            || definition == typeof(HashSet<>)
            || definition == typeof(ICollection<>)
            || definition == typeof(IList<>)
            || definition == typeof(IDictionary<,>)
            || definition == typeof(ISet<>);
    }

    private static IEnumerable<MethodBase> ImplementationBodies(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return type.GetMethods(flags).Cast<MethodBase>()
            .Concat(type.GetConstructors(flags))
            .Where(method => method.GetMethodBody() is not null);
    }

    private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (int index = 0; index < il.Length;)
        {
            OpCode opCode;
            byte first = il[index++];
            short value = first == 0xfe
                ? (short)(0xfe00 | il[index++])
                : first;
            if (!OpCodesByValue.TryGetValue(value, out opCode))
                yield break;

            int operandStart = index;
            int operandSize = OperandSize(opCode.OperandType, il, operandStart);
            if (opCode.OperandType == OperandType.InlineMethod && operandStart + 4 <= il.Length)
            {
                int token = BitConverter.ToInt32(il, operandStart);
                MethodBase? called = null;
                try
                {
                    called = method.Module.ResolveMethod(
                        token,
                        method.DeclaringType?.GetGenericArguments(),
                        method.IsGenericMethod ? method.GetGenericArguments() : null);
                }
                catch (ArgumentException)
                {
                }

                if (called is not null)
                    yield return called;
            }

            index = operandStart + operandSize;
        }
    }

    private static int OperandSize(OperandType operandType, byte[] il, int index) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI
            or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
            or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => index + 4 <= il.Length
            ? 4 + BitConverter.ToInt32(il, index) * 4
            : il.Length - index,
        _ => 0
    };

    private static bool IsAmbientCall(MethodBase method)
    {
        Type? owner = method.DeclaringType;
        string ownerName = owner?.FullName ?? string.Empty;
        string ownerNamespace = owner?.Namespace ?? string.Empty;
        return owner == typeof(Environment)
            || owner == typeof(Random)
            || owner == typeof(Guid) && method.Name == nameof(Guid.NewGuid)
            || owner == typeof(DateTime) && method.Name is "get_Now" or "get_UtcNow" or "get_Today"
            || owner == typeof(DateTimeOffset) && method.Name is "get_Now" or "get_UtcNow"
            || ownerName == "System.Threading.Thread" && method.Name == "Sleep"
            || ownerName == "System.Threading.Tasks.Task" && method.Name == "Delay"
            || ownerNamespace.StartsWith("System.IO", StringComparison.Ordinal)
            || ownerNamespace.StartsWith("System.Net", StringComparison.Ordinal)
            || ownerNamespace.StartsWith("System.Reflection", StringComparison.Ordinal)
            || owner == typeof(System.Security.Cryptography.RandomNumberGenerator)
            || owner?.IsSubclassOf(typeof(System.Security.Cryptography.RandomNumberGenerator)) == true
            || ownerName == "System.Diagnostics.Process";
    }

    private static bool IsApprovedAmbientCall(Type caller, MethodBase called, Assembly reviewedAssembly)
    {
        string calledNamespace = called.DeclaringType?.Namespace ?? string.Empty;
        if (IsReviewedAssembly(caller.Assembly, reviewedAssembly)
            && caller.Namespace == CompositionNamespace
            && calledNamespace.StartsWith("System.IO", StringComparison.Ordinal))
            return true;
        return caller.FullName == "ISLAMU.Event.Setup.Core.Environment.LocalSecretGenerator"
            && (called.DeclaringType == typeof(System.Security.Cryptography.RandomNumberGenerator)
                || called.DeclaringType?.IsSubclassOf(
                    typeof(System.Security.Cryptography.RandomNumberGenerator)) == true);
    }

    private static bool IsReviewedAssembly(Assembly candidate, Assembly reviewedAssembly) =>
        string.Equals(
            candidate.GetName().Name, reviewedAssembly.GetName().Name, StringComparison.Ordinal);
}

public sealed class SyntheticAmbientFixture
{
    public DateTime Read() => DateTime.UtcNow;
}

public sealed class SyntheticMutableFixture
{
    public List<string> Items { get; } = [];
}

public sealed record SyntheticLeakingDiagnostic(string Code, string Path, string Severity, string SuppliedValue);

internal sealed record SyntheticNamespaceCanaries(
    Assembly Assembly, Type ApprovedIo, Type ChildNamespaceIo, Type RootNamespaceIo,
    Type PublicIo, Type Network);

internal sealed record SyntheticYamlCanaries(
    Assembly Assembly, Type Approved, Type ConstructorAndLocal, Type[] Forbidden);

internal static class SyntheticVerifierFactory
{
    private const string CompositionNamespace = "ISLAMU.Event.Setup.Core.Composition";

    internal static SyntheticNamespaceCanaries CreateNamespaceCanaries()
    {
        ModuleBuilder module = CreateModule();
        Type approved = DefineCall(module, $"{CompositionNamespace}.ApprovedIo",
            typeof(Path).GetMethod(nameof(Path.GetTempPath), Type.EmptyTypes)!);
        Type child = DefineCall(module, $"{CompositionNamespace}.Child.DeniedIo",
            typeof(Path).GetMethod(nameof(Path.GetTempPath), Type.EmptyTypes)!);
        Type root = DefineCall(module, "ISLAMU.Event.Setup.Core.DeniedIo",
            typeof(Path).GetMethod(nameof(Path.GetTempPath), Type.EmptyTypes)!);
        Type publicIo = DefinePublicParameter(
            module, $"{CompositionNamespace}.PublicIo", typeof(Stream));
        Type network = DefineCall(module, $"{CompositionNamespace}.Network",
            typeof(System.Net.Dns).GetMethod(nameof(System.Net.Dns.GetHostName), Type.EmptyTypes)!);
        return new SyntheticNamespaceCanaries(
            module.Assembly, approved, child, root, publicIo, network);
    }

    internal static SyntheticYamlCanaries CreateYamlCanaries()
    {
        ModuleBuilder module = CreateModule();
        Type parser = DefineDependency(module, "YamlDotNet.Core.Parser");
        Type scalar = DefineDependency(module, "YamlDotNet.Core.Events.Scalar");
        Type approved = DefineFields(
            module, $"{CompositionNamespace}.ApprovedYaml", [parser, scalar]);

        string[] roles =
        [
            "YamlDotNet.Serialization.DeserializerBuilder",
            "YamlDotNet.Serialization.SerializerBuilder",
            "YamlDotNet.Core.Emitter",
            "YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention",
            "YamlDotNet.Serialization.ObjectFactories.DefaultObjectFactory",
            "YamlDotNet.Serialization.TypeInspectors.ReadablePropertiesTypeInspector",
            "YamlDotNet.Serialization.TypeDiscriminators.TypeDiscriminator",
            "YamlDotNet.Remote.IncludeResolver",
            "YamlDotNet.Dynamic.PolymorphicType"
        ];
        var forbidden = new List<Type>(roles.Length + 1);
        for (int index = 0; index < roles.Length; index++)
        {
            Type dependency = DefineDependency(module, roles[index]);
            forbidden.Add(DefineFields(
                module, $"{CompositionNamespace}.ForbiddenYamlRole{index}", [dependency]));
        }
        Type constructorDependency = DefineDependency(
            module, "YamlDotNet.Serialization.ConstructorOnlyRole");
        Type constructorAndLocal = DefineConstructorAndLocal(
            module, $"{CompositionNamespace}.ConstructorAndLocalYamlRole", constructorDependency);
        forbidden.Add(constructorAndLocal);
        return new SyntheticYamlCanaries(
            module.Assembly, approved, constructorAndLocal, forbidden.ToArray());
    }

    private static ModuleBuilder CreateModule()
    {
        var name = new AssemblyName($"SetupCoreVerifierCanaries.{Guid.NewGuid():N}");
        return AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run)
            .DefineDynamicModule(name.Name!);
    }

    private static Type DefineDependency(ModuleBuilder module, string fullName)
    {
        TypeBuilder builder = module.DefineType(
            fullName, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        return builder.CreateType()!;
    }

    private static Type DefineFields(ModuleBuilder module, string fullName, Type[] dependencies)
    {
        TypeBuilder builder = module.DefineType(
            fullName, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
        for (int index = 0; index < dependencies.Length; index++)
            builder.DefineField($"_dependency{index}", dependencies[index], FieldAttributes.Private);
        return builder.CreateType()!;
    }

    private static Type DefineCall(ModuleBuilder module, string fullName, MethodInfo called)
    {
        TypeBuilder builder = module.DefineType(
            fullName, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
        MethodBuilder method = builder.DefineMethod(
            "Call", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Call, called);
        if (called.ReturnType != typeof(void))
            il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        return builder.CreateType()!;
    }

    private static Type DefinePublicParameter(ModuleBuilder module, string fullName, Type dependency)
    {
        TypeBuilder builder = module.DefineType(
            fullName, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
        MethodBuilder method = builder.DefineMethod(
            "Use", MethodAttributes.Public | MethodAttributes.Static, typeof(void), [dependency]);
        method.GetILGenerator().Emit(OpCodes.Ret);
        return builder.CreateType()!;
    }

    private static Type DefineConstructorAndLocal(
        ModuleBuilder module, string fullName, Type dependency)
    {
        TypeBuilder builder = module.DefineType(
            fullName, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
        MethodBuilder method = builder.DefineMethod(
            "Construct", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        LocalBuilder local = il.DeclareLocal(dependency);
        il.Emit(OpCodes.Newobj, dependency.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, local);
        il.Emit(OpCodes.Ret);
        return builder.CreateType()!;
    }
}
