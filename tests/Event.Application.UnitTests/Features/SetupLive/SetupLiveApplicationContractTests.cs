namespace Event.Application.UnitTests.Features.SetupLive;

using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Explore.Application.Contracts.Secrets;

public sealed class SetupLiveApplicationContractTests
{
    private const int MaximumSecretBytes = 65_536;
    private const string ContractNamespace =
        "Explore.Application.Contracts.SetupLive";
    private const string SecretContractNamespace =
        "Explore.Application.Contracts.Secrets";
    private static readonly Assembly ApplicationAssembly =
        typeof(ISecretResolver).Assembly;

    [Test]
    public async Task WriterIsOneWayCancellationAwareAndValueFree()
    {
        Type request = RequireContractType("SetupSecretBindingWriteRequest");
        Type outcome = RequireContractType("SetupSecretBindingWriteOutcome");
        Type writer = RequireSecretContractType("ISetupSecretBindingWriter");

        await Assert.That(writer.IsInterface).IsTrue();
        AssertExactMethods(
            writer,
            Method(
                "WriteAsync",
                typeof(Task<>).MakeGenericType(outcome),
                request,
                typeof(CancellationToken)));
        AssertEnum(outcome, "Invalid", "Ready", "Unavailable", "Unauthorized");
        AssertNoReadSurface(writer);
    }

    [Test]
    public async Task WriterRequestCarriesOnlyResolvedIdentityAndScopedBytes()
    {
        Type request = RequireContractType("SetupSecretBindingWriteRequest");

        await Assert.That(request.IsSealed).IsTrue();
        AssertExactProperties(
            request,
            Property("TenantId", typeof(Guid)),
            Property("EnrollmentId", typeof(Guid)),
            Property("EnrollmentGeneration", typeof(long)),
            Property("OperationId", typeof(Guid)),
            Property("BindingId", typeof(Guid)),
            Property("BindingKey", typeof(string)),
            Property("SecretValue", typeof(ReadOnlyMemory<byte>)));
        AssertExactConstructor(
            request,
            typeof(Guid),
            typeof(Guid),
            typeof(long),
            typeof(Guid),
            typeof(Guid),
            typeof(string),
            typeof(ReadOnlyMemory<byte>));
        AssertSealedValueObject(request);
        object?[] valid =
        [
            Id(1), Id(2), 42L, Id(3), Id(4), "setup.signing", CanaryBytes()
        ];
        AssertExactPropertyValues(
            Create(request, valid),
            ("TenantId", valid[0]!),
            ("EnrollmentId", valid[1]!),
            ("EnrollmentGeneration", valid[2]!),
            ("OperationId", valid[3]!),
            ("BindingId", valid[4]!),
            ("BindingKey", valid[5]!));
        AssertIdentityBoundaries(request, valid, 0, 1, 3, 4);
        AssertPositiveBoundary(request, valid, 2);
        AssertBindingBoundary(request, valid, 5);
        AssertByteBoundary(request, valid, 6);
        AssertBorrowedMemory(request, valid, 6, "SecretValue");
        object maximum = Create(
            request,
            With(
                valid,
                6,
                new ReadOnlyMemory<byte>(new byte[MaximumSecretBytes])));
        RequireContract(
            Read<ReadOnlyMemory<byte>>(maximum, "SecretValue").Length
                == MaximumSecretBytes,
            "invalid-setup-live-application-byte-ceiling");
        _ = Create(request, With(valid, 5, "setup.encryption"));
        await AssertValueFreeDiagnostics(
            request,
            valid,
            "setup.signing",
            "setup-d2-3-secret-canary");
    }

    [Test]
    public async Task CommitmentAuthorityIsPurposeSpecificAndVersioned()
    {
        Type request = RequireContractType("SetupSecretBindingCommitmentRequest");
        Type result = RequireContractType("SetupSecretBindingCommitment");
        Type authority =
            RequireContractType("ISetupSecretBindingCommitmentAuthority");

        await Assert.That(authority.IsInterface).IsTrue();
        AssertExactMethods(
            authority,
            Method(
                "CommitAsync",
                typeof(Task<>).MakeGenericType(result),
                request,
                typeof(CancellationToken)));
        AssertNoReadSurface(authority);
    }

    [Test]
    public async Task CommitmentVocabularyBindsIdentityWithoutReturningBytes()
    {
        Type request = RequireContractType("SetupSecretBindingCommitmentRequest");
        Type result = RequireContractType("SetupSecretBindingCommitment");

        AssertExactProperties(
            request,
            Property("TenantId", typeof(Guid)),
            Property("ActorId", typeof(Guid)),
            Property("EnrollmentId", typeof(Guid)),
            Property("EnrollmentGeneration", typeof(long)),
            Property("OperationKey", typeof(Guid)),
            Property("BindingKey", typeof(string)),
            Property("SecretValue", typeof(ReadOnlyMemory<byte>)));
        AssertExactConstructor(
            request,
            typeof(Guid),
            typeof(Guid),
            typeof(Guid),
            typeof(long),
            typeof(Guid),
            typeof(string),
            typeof(ReadOnlyMemory<byte>));
        AssertSealedValueObject(request);
        object?[] validRequest =
        [
            Id(10), Id(11), Id(12), 43L, Id(13), "setup.encryption",
            CanaryBytes()
        ];
        AssertExactPropertyValues(
            Create(request, validRequest),
            ("TenantId", validRequest[0]!),
            ("ActorId", validRequest[1]!),
            ("EnrollmentId", validRequest[2]!),
            ("EnrollmentGeneration", validRequest[3]!),
            ("OperationKey", validRequest[4]!),
            ("BindingKey", validRequest[5]!));
        AssertIdentityBoundaries(request, validRequest, 0, 1, 2, 4);
        AssertPositiveBoundary(request, validRequest, 3);
        AssertBindingBoundary(request, validRequest, 5);
        AssertByteBoundary(request, validRequest, 6);
        AssertBorrowedMemory(request, validRequest, 6, "SecretValue");
        _ = Create(request, With(validRequest, 5, "setup.signing"));
        await AssertValueFreeDiagnostics(
            request,
            validRequest,
            Id(10).ToString(),
            "setup.encryption",
            "setup-d2-3-secret-canary");

        AssertExactProperties(
            result,
            Property("KeyVersion", typeof(int)),
            Property("Commitment", typeof(string)));
        AssertExactConstructor(result, typeof(int), typeof(string));
        AssertSealedValueObject(result);
        object?[] validResult = [37, Digest('b')];
        AssertExactPropertyValues(
            Create(result, validResult),
            ("KeyVersion", validResult[0]!),
            ("Commitment", validResult[1]!));
        AssertPositiveBoundary(result, validResult, 0);
        foreach (string? invalid in InvalidCommitments())
            ExpectConstructorArgumentException(
                result,
                With(validResult, 1, invalid));
        await AssertValueFreeDiagnostics(
            result,
            validResult,
            Digest('b'));
    }

    [Test]
    public async Task CoordinatorLocksOneEnrollmentGenerationWithCancellation()
    {
        Type request = RequireContractType("SetupSecretBindingCoordinationRequest");
        Type coordinator =
            RequireContractType("ISetupSecretBindingOperationCoordinator");

        await Assert.That(coordinator.IsInterface).IsTrue();
        AssertExactProperties(
            request,
            Property("TenantId", typeof(Guid)),
            Property("EnrollmentId", typeof(Guid)),
            Property("EnrollmentGeneration", typeof(long)));
        AssertExactConstructor(
            request,
            typeof(Guid),
            typeof(Guid),
            typeof(long));
        AssertSealedValueObject(request);
        object?[] validRequest = [Id(20), Id(21), 44L];
        AssertExactPropertyValues(
            Create(request, validRequest),
            ("TenantId", validRequest[0]!),
            ("EnrollmentId", validRequest[1]!),
            ("EnrollmentGeneration", validRequest[2]!));
        AssertIdentityBoundaries(request, validRequest, 0, 1);
        AssertPositiveBoundary(request, validRequest, 2);
        await Assert.That(request.GetProperty("ActorId")).IsNull();
        await Assert.That(request.GetProperty("OperationId")).IsNull();
        await Assert.That(request.GetProperty("OperationKey")).IsNull();
        await AssertValueFreeDiagnostics(
            request,
            validRequest,
            Id(20).ToString(),
            Id(21).ToString());
        AssertExactMethods(
            coordinator,
            Method(
                "AcquireAsync",
                typeof(Task<>).MakeGenericType(typeof(IAsyncDisposable)),
                request,
                typeof(CancellationToken)));
    }

    [Test]
    public async Task BarrierAndMetadataAreClosedToExactDispatchMilestone()
    {
        Type barrier =
            RequireSecretContractType("ISetupSecretBindingCommitBarrier");
        Type metadata = RequireContractType("SetupSecretBindingContractMetadata");

        await Assert.That(barrier.IsInterface).IsTrue();
        AssertExactMethods(
            barrier,
            Method(
                "WaitBeforeProviderDispatchAsync",
                typeof(Task),
                typeof(CancellationToken)));
        AssertExactConstant(
            metadata,
            "CommitmentAuthorityKey",
            "setup.secret_binding_commitment_hmac_key");
        AssertExactConstant(metadata, "MilestoneEventId", 19620);
        AssertExactConstant(metadata, "MilestoneEventName", "SetupLiveMilestone");
        AssertExactConstant(metadata, "Operation", "secret_binding.write");
        AssertExactConstant(
            metadata,
            "BeforeProviderDispatchMilestone",
            "before_provider_dispatch");
        AssertExactStaticMetadata(metadata);
    }

    [Test]
    public async Task ContractSurfaceExcludesHandlersReadersProvidersAndP9008()
    {
        AssertAssemblyModuleBaseline();
        _ = RequireSecretContractType("ISetupSecretBindingWriter");
        _ = RequireSecretContractType("ISetupSecretBindingCommitBarrier");

        string[] expectedSetupLiveTypes =
        [
            "ISetupSecretBindingCommitmentAuthority",
            "ISetupSecretBindingOperationCoordinator",
            "SetupSecretBindingCommitment",
            "SetupSecretBindingCommitmentRequest",
            "SetupSecretBindingContractMetadata",
            "SetupSecretBindingCoordinationRequest",
            "SetupSecretBindingWriteOutcome",
            "SetupSecretBindingWriteRequest"
        ];
        Type[] setupLiveTypes = ApplicationAssembly.GetExportedTypes()
            .Where(type => type.Namespace == ContractNamespace
                || type.Namespace?.StartsWith(
                    $"{ContractNamespace}.",
                    StringComparison.Ordinal) == true)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();
        await Assert.That(setupLiveTypes.Select(type => type.Name))
            .IsEquivalentTo(expectedSetupLiveTypes);

        Type[] secretTypes = ApplicationAssembly.GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith(
                    SecretContractNamespace,
                    StringComparison.Ordinal) == true
                && type.Name.StartsWith(
                    "ISetupSecretBinding",
                    StringComparison.Ordinal))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();
        await Assert.That(secretTypes.Select(type => type.Name)).IsEquivalentTo(
            new[]
            {
                "ISetupSecretBindingCommitBarrier",
                "ISetupSecretBindingWriter"
            });

        string[] forbidden =
        [
            "Registration", "Callback", "Credential", "ProviderCoordinate",
            "Coordinate", "ResponseBody", "Payload"
        ];
        foreach (Type type in setupLiveTypes.Concat(secretTypes))
        {
            string shape = string.Join(
                '|',
                PublicShape(type)).ToLowerInvariant();
            foreach (string fragment in forbidden)
            {
                RequireContract(
                    !shape.Contains(fragment.ToLowerInvariant(), StringComparison.Ordinal),
                    $"forbidden-setup-live-application-surface:"
                    + $"{type.FullName}:{fragment}");
            }
            foreach (MethodInfo method in type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.Static))
            {
                RequireContract(
                    !new[] { "Read", "Resolve", "Export", "Preview", "Echo" }
                        .Any(fragment => method.Name.Contains(
                            fragment,
                            StringComparison.OrdinalIgnoreCase)),
                    $"forbidden-setup-live-application-method:"
                    + $"{type.FullName}.{method.Name}");
            }
        }

        string[] featureOwners = ApplicationAssembly.GetExportedTypes()
            .Where(type => type.Namespace is not null
                && type.Namespace.StartsWith(
                    "Explore.Application.Features.SetupLive",
                    StringComparison.Ordinal))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(featureOwners).IsEquivalentTo(
            [
                "Explore.Application.Features.SetupLive.SetupLiveApplicationService",
                "Explore.Application.Features.SetupLive.SetupLiveApplicationStatus",
                "Explore.Application.Features.SetupLive.SetupLiveEnrollmentResult",
                "Explore.Application.Features.SetupLive.SetupLiveReadinessResult",
                "Explore.Application.Features.SetupLive.SetupLiveSecretBindingResult"
            ]);

        Type[] forwardedOwners = ApplicationAssembly.GetForwardedTypes()
            .Where(type => type.Namespace == ContractNamespace
                || type.Namespace?.StartsWith(
                    $"{ContractNamespace}.",
                    StringComparison.Ordinal) == true
                || type.Namespace?.StartsWith(
                    SecretContractNamespace,
                    StringComparison.Ordinal) == true
                    && type.Name.StartsWith(
                        "ISetupSecretBinding",
                        StringComparison.Ordinal))
            .ToArray();
        await Assert.That(forwardedOwners).IsEmpty();
    }

    private static Type RequireContractType(string name) =>
        RequireType($"{ContractNamespace}.{name}");

    private static Type RequireSecretContractType(string name) =>
        RequireType($"{SecretContractNamespace}.{name}");

    private static Type RequireType(string fullName)
    {
        Type type = ApplicationAssembly.GetType(
            fullName,
            throwOnError: false,
            ignoreCase: false)
        ?? throw new InvalidOperationException(
            $"missing-setup-live-application-owner:{fullName}");
        RequireContract(
            type.Assembly == ApplicationAssembly
            && type.Module == ApplicationAssembly.ManifestModule
            && type.IsPublic
            && !type.IsNested
            && type.DeclaringType is null
            && !ApplicationAssembly.GetForwardedTypes().Contains(type),
            $"invalid-setup-live-application-type-ownership:{fullName}");
        AssertAttributeSet(
            type.CustomAttributes,
            AttributeWitness(type).CustomAttributes,
            fullName);
        return type;
    }

    private static void AssertExactProperties(
        Type type,
        params PropertyContract[] expected)
    {
        PropertyInfo[] actual = type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();
        string[] expectedNames = expected
            .Select(contract => contract.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        RequireContract(
            actual.Select(property => property.Name).SequenceEqual(expectedNames),
            $"invalid-setup-live-application-properties:{type.FullName}");
        foreach (PropertyContract contract in expected)
        {
            PropertyInfo property = actual.Single(candidate =>
                candidate.Name == contract.Name);
            RequireContract(
                property.PropertyType == contract.Type,
                $"invalid-setup-live-application-property-type:"
                + $"{type.FullName}.{contract.Name}");
            RequireContract(
                property.DeclaringType == type
                && property.GetMethod is
                {
                    IsPublic: true,
                    IsStatic: false,
                    IsSpecialName: true
                }
                && property.GetMethod.Attributes
                    == (MethodAttributes.Public
                        | MethodAttributes.HideBySig
                        | MethodAttributes.SpecialName)
                && property.GetSetMethod(nonPublic: true) is null,
                $"mutable-setup-live-application-property:"
                + $"{type.FullName}.{contract.Name}");
            RequireContract(
                property.GetIndexParameters().Length == 0
                && property.GetAccessors(nonPublic: true).Length == 1
                && property.GetMethod.GetParameters().Length == 0,
                $"indexed-setup-live-application-property:"
                + $"{type.FullName}.{contract.Name}");
            Type witnessType = AttributeWitness(type);
            PropertyInfo witnessProperty =
                witnessType.GetProperty(contract.Name)
                ?? throw new InvalidOperationException(
                    $"missing-setup-live-attribute-witness:"
                    + $"{witnessType.FullName}.{contract.Name}");
            RequireContract(
                property.Attributes == witnessProperty.Attributes
                && property.GetRequiredCustomModifiers().SequenceEqual(
                    witnessProperty.GetRequiredCustomModifiers())
                && property.GetOptionalCustomModifiers().SequenceEqual(
                    witnessProperty.GetOptionalCustomModifiers()),
                $"invalid-setup-live-application-property-metadata:"
                + $"{type.FullName}.{contract.Name}");
            AssertAttributeSet(
                property.CustomAttributes,
                witnessProperty.CustomAttributes,
                $"{type.FullName}.{contract.Name}");
            AssertMethodMetadata(
                property.GetMethod,
                witnessProperty.GetMethod
                    ?? throw new InvalidOperationException(
                        $"missing-setup-live-getter-witness:"
                        + $"{witnessType.FullName}.{contract.Name}"),
                requireBody: true);
        }
        AssertExactBackingFields(type, expected);
    }

    private static void AssertExactConstructor(
        Type type,
        params Type[] parameterTypes)
    {
        ConstructorInfo[] constructors = type.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance);
        RequireContract(
            constructors.Length == 1
            && constructors[0].IsPublic
            && constructors[0].GetParameters()
                .Select(parameter => parameter.ParameterType)
                .SequenceEqual(parameterTypes),
            $"invalid-setup-live-application-constructor:{type.FullName}");
        RequireContract(
            type.TypeInitializer is null,
            $"invalid-setup-live-application-type-initializer:{type.FullName}");
        ConstructorInfo witnessConstructor = AttributeWitness(type)
            .GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance)
            .Single();
        AssertAttributeSet(
            constructors[0].CustomAttributes,
            witnessConstructor.CustomAttributes,
            $"{type.FullName}.ctor");
        ParameterInfo[] parameters = constructors[0].GetParameters();
        ParameterInfo[] witnessParameters =
            witnessConstructor.GetParameters();
        for (int index = 0; index < parameters.Length; index++)
        {
            AssertParameterMetadata(
                parameters[index],
                witnessParameters[index],
                compareType: true);
        }
        RequireContract(
            constructors[0].Attributes
                == witnessConstructor.Attributes
            && constructors[0].CallingConvention
                == witnessConstructor.CallingConvention
            && constructors[0].GetMethodImplementationFlags()
                == witnessConstructor.GetMethodImplementationFlags()
            && (constructors[0].GetMethodBody() is not null)
                == (witnessConstructor.GetMethodBody() is not null),
            $"invalid-setup-live-application-constructor-metadata:"
            + $"{type.FullName}");
        AssertConstructorSignatureMetadata(
            constructors[0],
            witnessConstructor);
    }

    private static void AssertSealedValueObject(Type type)
    {
        MethodInfo[] declaredMethods = type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName
                || method.Name.StartsWith("op_", StringComparison.Ordinal))
            .ToArray();
        RequireContract(
            type.IsClass
            && type.IsSealed
            && type.Attributes
                == (TypeAttributes.Public
                    | TypeAttributes.Sealed
                    | TypeAttributes.BeforeFieldInit)
            && type.BaseType == typeof(object)
            && type.IsAutoLayout
            && !type.IsImport
            && (type.Attributes & TypeAttributes.Serializable) == 0
            && type.GetInterfaces().Length == 0
            && !typeof(IDisposable).IsAssignableFrom(type)
            && !typeof(IAsyncDisposable).IsAssignableFrom(type)
            && declaredMethods.Length == 0
            && type.GetProperties(
                BindingFlags.Public | BindingFlags.Static
                | BindingFlags.DeclaredOnly).Length == 0
            && type.GetFields(
                BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly).Length == 0
            && type.GetEvents(
                BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly).Length == 0
            && type.GetNestedTypes(
                BindingFlags.Public | BindingFlags.NonPublic).Length == 0
            && type.GetMethods(
                    BindingFlags.NonPublic | BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly).Length == 0
            && type.GetProperties(
                BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly).Length == 0
            && type.GetEvents(
                BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly).Length == 0
            && type.GetCustomAttribute<DebuggerDisplayAttribute>() is null
            && type.GetCustomAttribute<DebuggerTypeProxyAttribute>() is null,
            $"invalid-setup-live-application-value-object:{type.FullName}");
    }

    private static void AssertIdentityBoundaries(
        Type type,
        object?[] validArguments,
        params int[] positions)
    {
        foreach (int position in positions)
        {
            ExpectConstructorArgumentException(
                type,
                With(validArguments, position, Guid.Empty));
            foreach (Guid invalid in NonV7(position))
            {
                ExpectConstructorArgumentException(
                    type,
                    With(validArguments, position, invalid));
            }
            ExpectConstructorArgumentException(
                type,
                With(
                    validArguments,
                    position,
                    Guid.Parse(
                        $"01991f00-0000-7000-0000-{position:D12}")));
        }
    }

    private static void AssertExactPropertyValues(
        object instance,
        params (string Name, object Value)[] expected)
    {
        foreach ((string name, object value) in expected)
        {
            RequireContract(
                Equals(instance.GetType().GetProperty(name)?.GetValue(instance), value),
                $"invalid-setup-live-application-property-mapping:"
                + $"{instance.GetType().FullName}.{name}");
        }
    }

    private static void AssertPositiveBoundary(
        Type type,
        object?[] validArguments,
        int position)
    {
        Type parameterType = type.GetConstructors().Single()
            .GetParameters()[position].ParameterType;
        foreach (object invalid in parameterType == typeof(int)
            ? new object[] { 0, -1 }
            : new object[] { 0L, -1L })
        {
            ExpectConstructorArgumentException(
                type,
                With(validArguments, position, invalid));
        }
    }

    private static void AssertBindingBoundary(
        Type type,
        object?[] validArguments,
        int position)
    {
        foreach (string? invalid in
            new[] { null, string.Empty, " ", "unknown.binding" })
        {
            ExpectConstructorArgumentException(
                type,
                With(validArguments, position, invalid));
        }
    }

    private static void AssertByteBoundary(
        Type type,
        object?[] validArguments,
        int position)
    {
        ExpectConstructorArgumentException(
            type,
            With(
                validArguments,
                position,
                ReadOnlyMemory<byte>.Empty));
        ExpectConstructorArgumentException(
            type,
            With(
                validArguments,
                position,
                new ReadOnlyMemory<byte>(
                    new byte[MaximumSecretBytes + 1])));
    }

    private static void AssertBorrowedMemory(
        Type type,
        object?[] validArguments,
        int position,
        string propertyName)
    {
        byte[] value = CanaryBytes().ToArray();
        var owner = new byte[value.Length + 4];
        const int offset = 2;
        value.CopyTo(owner.AsSpan(offset));
        var supplied = new ReadOnlyMemory<byte>(
            owner,
            offset,
            value.Length);

        object instance = Create(
            type,
            With(validArguments, position, supplied));
        ReadOnlyMemory<byte> exposed =
            Read<ReadOnlyMemory<byte>>(instance, propertyName);
        RequireContract(
            MemoryMarshal.TryGetArray(exposed, out ArraySegment<byte> segment)
            && ReferenceEquals(segment.Array, owner)
            && segment.Offset == offset
            && segment.Count == value.Length,
            $"copied-setup-live-application-borrowed-memory:{type.FullName}");

        owner[offset] ^= 0xff;
        RequireContract(
            exposed.Span[0] == owner[offset],
            $"detached-setup-live-application-borrowed-memory:{type.FullName}");
        Array.Clear(owner);
    }

    private static object Create(Type type, object?[] arguments)
    {
        try
        {
            return Activator.CreateInstance(type, arguments)
                ?? throw new InvalidOperationException(
                    $"missing-setup-live-application-instance:{type.FullName}");
        }
        catch (TargetInvocationException exception)
            when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void ExpectConstructorArgumentException(
        Type type,
        object?[] arguments)
    {
        try
        {
            _ = Create(type, arguments);
        }
        catch (ArgumentException exception)
        {
            AssertValueFreeException(exception);
            return;
        }

        throw new InvalidOperationException(
            $"missing-setup-live-application-rejection:{type.FullName}");
    }

    private static void AssertValueFreeException(Exception exception)
    {
        var diagnostics = new List<string>();
        for (Exception? current = exception; current is not null;
             current = current.InnerException)
        {
            diagnostics.Add(current.Message);
            diagnostics.Add(current.ToString());
            if (current is ArgumentException argument)
                diagnostics.Add(argument.ParamName ?? string.Empty);
        }

        string combined = string.Join('|', diagnostics);
        foreach (string canary in DiagnosticCanaries())
        {
            RequireContract(
                !combined.Contains(canary, StringComparison.Ordinal),
                $"value-bearing-setup-live-application-exception:"
                + $"{exception.GetType().FullName}");
        }
    }

    private static IEnumerable<string> DiagnosticCanaries()
    {
        foreach (int value in Enumerable.Range(0, 100))
        {
            yield return Id(value).ToString();
            foreach (Guid invalid in NonV7(value))
                yield return invalid.ToString();
        }
        yield return "setup.signing";
        yield return "setup.encryption";
        yield return "unknown.binding";
        foreach (string canary in SecretDiagnosticCanaries())
            yield return canary;
        foreach (char value in "abcdef")
            yield return Digest(value);
        foreach (string? value in InvalidCommitments())
        {
            if (!string.IsNullOrEmpty(value))
                yield return value;
        }
    }

    private static void AssertExactStaticMetadata(Type type)
    {
        string[] fields = type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly)
            .Select(field => field.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expected =
        [
            "BeforeProviderDispatchMilestone",
            "CommitmentAuthorityKey",
            "MilestoneEventId",
            "MilestoneEventName",
            "Operation"
        ];
        RequireContract(
            type.IsClass
            && type.IsAbstract
            && type.IsSealed
            && type.Attributes
                == (TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Sealed
                    | TypeAttributes.BeforeFieldInit)
            && type.BaseType == typeof(object)
            && type.IsAutoLayout
            && !type.IsImport
            && (type.Attributes & TypeAttributes.Serializable) == 0
            && type.GetInterfaces().Length == 0
            && fields.SequenceEqual(expected)
            && type.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static).Length == 0
            && type.TypeInitializer is null
            && type.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly).Length == 0
            && type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly).Length == 0
            && type.GetEvents(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly).Length == 0
            && type.GetNestedTypes(
                BindingFlags.Public | BindingFlags.NonPublic).Length == 0,
            $"invalid-setup-live-application-metadata:{type.FullName}");
    }

    private static void AssertExactMethods(
        Type type,
        params MethodContract[] expected)
    {
        RequireContract(
            type.IsInterface
            && type.Attributes
                == (TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract
                    | TypeAttributes.BeforeFieldInit)
            && type.IsAutoLayout
            && !type.IsImport
            && (type.Attributes & TypeAttributes.Serializable) == 0
            && type.GetInterfaces().Length == 0
            && type.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static).Length == 0
            && type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static).Length == 0
            && type.GetEvents(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static).Length == 0
            && type.GetNestedTypes(
                BindingFlags.Public | BindingFlags.NonPublic).Length == 0,
            $"inherited-setup-live-application-interface:{type.FullName}:"
            + $"attributes={type.Attributes}:"
            + $"interfaces={type.GetInterfaces().Length}:"
            + $"properties={type.GetProperties().Length}:"
            + $"fields={type.GetFields().Length}:"
            + $"events={type.GetEvents().Length}:"
            + $"nested={type.GetNestedTypes().Length}");
        MethodInfo[] methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly)
            .ToArray();
        RequireContract(
            methods.Length == expected.Length,
            $"invalid-setup-live-application-method-count:{type.FullName}");
        foreach (MethodContract contract in expected)
        {
            MethodInfo? method = methods.SingleOrDefault(candidate =>
                candidate.Name == contract.Name
                && candidate.ReturnType == contract.ReturnType
                && candidate.GetParameters()
                    .Select(parameter => parameter.ParameterType)
                    .SequenceEqual(contract.ParameterTypes));
            RequireContract(
                method is not null,
                $"missing-setup-live-application-method:"
                + $"{type.FullName}.{contract.Name}");
            RequireContract(
                method.IsPublic
                && !method.IsStatic
                && method.IsAbstract
                && !method.IsFinal
                && !method.IsGenericMethod
                && !method.ContainsGenericParameters
                && !method.IsSpecialName
                && method.DeclaringType == type
                && method.GetMethodBody() is null,
                $"invalid-setup-live-application-method-modifiers:"
                + $"{type.FullName}.{contract.Name}");
            MethodInfo witnessMethod = AttributeWitness(type).GetMethod(
                    contract.Name,
                    BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException(
                    $"missing-setup-live-method-witness:"
                    + $"{type.FullName}.{contract.Name}");
            AssertMethodMetadata(
                method,
                witnessMethod,
                requireBody: false);
        }
    }

    private static void AssertEnum(Type type, params string[] expected)
    {
        int[] expectedValues = Enumerable.Range(0, expected.Length).ToArray();
        int[] actualValues = Enum.GetValues(type)
            .Cast<object>()
            .Select(Convert.ToInt32)
            .ToArray();
        FieldInfo[] allFields = type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly)
            .OrderBy(field => field.MetadataToken)
            .ToArray();
        FieldInfo[] fields = allFields.Where(field => field.IsLiteral).ToArray();
        RequireContract(
            type.IsEnum
            && type.Attributes
                == (TypeAttributes.Public | TypeAttributes.Sealed)
            && Enum.GetUnderlyingType(type) == typeof(int)
            && Enum.GetNames(type).SequenceEqual(expected)
            && actualValues.SequenceEqual(expectedValues)
            && actualValues.Distinct().Count() == expected.Length
            && allFields.Length == expected.Length + 1
            && fields.Select(field => field.Name).SequenceEqual(expected)
            && !type.IsDefined(typeof(FlagsAttribute), inherit: false)
            && type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly).Length == 0
            && type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly).Length == 0
            && type.GetEvents(
                BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly).Length == 0
            && type.GetNestedTypes(
                BindingFlags.Public | BindingFlags.NonPublic).Length == 0,
            $"invalid-setup-live-application-enum:{type.FullName}");
        FieldInfo[] storage = allFields.Where(field => !field.IsLiteral).ToArray();
        RequireContract(
            storage.Length == 1
            && storage[0].Name == "value__"
            && storage[0].IsPublic
            && !storage[0].IsStatic
            && storage[0].FieldType == typeof(int)
            && storage[0].IsSpecialName
            && (storage[0].Attributes & FieldAttributes.RTSpecialName) != 0,
            $"invalid-setup-live-application-enum-storage:{type.FullName}");
        RequireContract(
            type.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static).Length == 0
            && type.TypeInitializer is null
            && type.GetInterfaces()
                .OrderBy(candidate => candidate.FullName, StringComparer.Ordinal)
                .SequenceEqual(
                    typeof(Enum).GetInterfaces().OrderBy(
                        candidate => candidate.FullName,
                        StringComparer.Ordinal)),
            $"invalid-setup-live-application-enum-behavior:{type.FullName}");
        foreach (FieldInfo field in fields)
        {
            FieldInfo witnessField = AttributeWitness(type).GetField(field.Name)
                ?? throw new InvalidOperationException(
                    $"missing-setup-live-enum-witness:"
                    + $"{type.FullName}.{field.Name}");
            AssertFieldMetadata(
                field,
                witnessField,
                $"{type.FullName}.{field.Name}");
        }
        FieldInfo witnessStorage = AttributeWitness(type).GetField(
                "value__",
                BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"missing-setup-live-enum-storage-witness:{type.FullName}");
        AssertFieldMetadata(
            storage[0],
            witnessStorage,
            $"{type.FullName}.value__");
    }

    private static void AssertNoReadSurface(Type type)
    {
        string[] forbidden =
        [
            "Read", "Resolve", "Get", "Echo", "Preview", "Export", "Value"
        ];
        foreach (MethodInfo method in type.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            RequireContract(
                !forbidden.Any(fragment => method.Name.Contains(
                    fragment,
                    StringComparison.OrdinalIgnoreCase)),
                $"forbidden-setup-live-application-read:{type.FullName}.{method.Name}");
        }
    }

    private static void AssertExactConstant(
        Type type,
        string name,
        object expected)
    {
        FieldInfo? field = type.GetField(
            name,
            BindingFlags.Public | BindingFlags.Static);
        RequireContract(
            field is not null
            && field.IsLiteral
            && field.FieldType == expected.GetType()
            && Equals(field.GetRawConstantValue(), expected),
            $"invalid-setup-live-application-constant:{type.FullName}.{name}");
        FieldInfo witnessField = AttributeWitness(type).GetField(
                name,
                BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"missing-setup-live-constant-witness:"
                + $"{type.FullName}.{name}");
        AssertFieldMetadata(
            field,
            witnessField,
            $"{type.FullName}.{name}");
    }

    private static async Task AssertValueFreeDiagnostics(
        Type type,
        object?[] constructorArguments,
        params string[] canaries)
    {
        object instance = Create(type, constructorArguments);
        foreach (string canary in canaries
            .Concat(SecretDiagnosticCanaries())
            .Distinct(StringComparer.Ordinal))
            await Assert.That(instance.ToString()).DoesNotContain(canary);
        AssertSealedValueObject(type);
    }

    private static IEnumerable<string> PublicShape(Type type) =>
        type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.Static)
            .Select(member => member.Name)
            .Concat(type.GetFields(
                    BindingFlags.Public | BindingFlags.Instance
                    | BindingFlags.Static)
                .Select(field => field.FieldType.FullName
                    ?? field.FieldType.Name))
            .Concat(type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance
                    | BindingFlags.Static)
                .Select(property => property.PropertyType.FullName
                    ?? property.PropertyType.Name))
            .Concat(type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance
                    | BindingFlags.Static)
                .Select(method => method.ReturnType.FullName
                    ?? method.ReturnType.Name))
            .Concat(type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance
                    | BindingFlags.Static)
                .SelectMany(method => method.GetParameters())
                .Select(parameter => parameter.ParameterType.FullName
                    ?? parameter.ParameterType.Name))
            .Concat(type.GetEvents(
                    BindingFlags.Public | BindingFlags.Instance
                    | BindingFlags.Static)
                .Select(@event => @event.EventHandlerType?.FullName
                    ?? @event.Name))
            .Concat(type.GetNestedTypes(BindingFlags.Public)
                .Select(nested => nested.FullName ?? nested.Name))
            .Concat(type.GetInterfaces()
                .Select(@interface => @interface.FullName
                    ?? @interface.Name));

    private static void AssertAssemblyModuleBaseline()
    {
        CustomAttributeData[] assemblyAttributes =
            ApplicationAssembly.CustomAttributes.ToArray();
        RequireContract(
            assemblyAttributes.Length == 13,
            "invalid-setup-live-application-assembly-attribute-count");
        AssertExactManifestAttribute(
            assemblyAttributes,
            typeof(CompilationRelaxationsAttribute),
            [typeof(int)],
            [8]);
        AssertExactManifestAttribute(
            assemblyAttributes,
            typeof(DebuggableAttribute),
            [typeof(DebuggableAttribute.DebuggingModes)],
            [2]);
        AssertExactManifestAttribute(
            assemblyAttributes,
            typeof(ExtensionAttribute),
            [],
            []);
        AssertExactManifestAttribute(
            assemblyAttributes,
            typeof(RuntimeCompatibilityAttribute),
            [],
            [],
            new NamedAttributeExpectation(
                nameof(RuntimeCompatibilityAttribute.WrapNonExceptionThrows),
                true));
        AssertExactManifestAttribute(
            assemblyAttributes,
            typeof(System.Runtime.Versioning.TargetFrameworkAttribute),
            [typeof(string)],
            [".NETCoreApp,Version=v10.0"],
            new NamedAttributeExpectation(
                nameof(System.Runtime.Versioning.TargetFrameworkAttribute
                    .FrameworkDisplayName),
                ".NET 10.0"));
        AssertExactManifestAttribute(
            assemblyAttributes,
            typeof(AssemblyCompanyAttribute),
            [typeof(string)],
            ["ISLAMU (ASBL en formation)"]);
        AssertExactManifestAttribute(
            assemblyAttributes,
            typeof(AssemblyConfigurationAttribute),
            [typeof(string)],
            ["Release"]);
        AssertExactManifestAttribute(
            assemblyAttributes,
            typeof(AssemblyCopyrightAttribute),
            [typeof(string)],
            [
                "Copyright (c) 2026 ISLAMU (ASBL en formation). "
                + "Licensed under AGPL-3.0-or-later."
            ]);
        AssertExactManifestAttribute(
            assemblyAttributes,
            typeof(AssemblyFileVersionAttribute),
            [typeof(string)],
            ["1.0.0.0"]);
        AssertInformationalVersion(assemblyAttributes);
        AssertExactManifestAttribute(
            assemblyAttributes,
            typeof(AssemblyMetadataAttribute),
            [typeof(string), typeof(string)],
            ["RepositoryUrl", "https://github.com/islamu-ngo/Event"]);
        AssertExactManifestAttribute(
            assemblyAttributes,
            typeof(AssemblyProductAttribute),
            [typeof(string)],
            ["Explore.Application"]);
        AssertExactManifestAttribute(
            assemblyAttributes,
            typeof(AssemblyTitleAttribute),
            [typeof(string)],
            ["Explore.Application"]);

        Module[] modules = ApplicationAssembly.GetModules();
        RequireContract(
            modules.Length == 1
            && ReferenceEquals(modules[0], ApplicationAssembly.ManifestModule)
            && modules[0].Name == "Explore.Application.dll"
            && modules[0].ScopeName == "Explore.Application.dll",
            "invalid-setup-live-application-module-layout");
        CustomAttributeData[] moduleAttributes =
            ApplicationAssembly.ManifestModule.CustomAttributes.ToArray();
        CustomAttributeData moduleWitness =
            typeof(SetupLiveApplicationContractTests).Module.CustomAttributes
                .Single(attribute =>
                    attribute.AttributeType.FullName
                        == "System.Runtime.CompilerServices.RefSafetyRulesAttribute");
        AssertAttributeSet(
            moduleAttributes,
            [moduleWitness],
            "Explore.Application.ManifestModule");
    }

    private static void AssertInformationalVersion(
        IReadOnlyCollection<CustomAttributeData> attributes)
    {
        CustomAttributeData attribute = attributes.Single(candidate =>
            candidate.AttributeType == typeof(AssemblyInformationalVersionAttribute));
        string value = (string)attribute.ConstructorArguments.Single().Value!;
        string revision = value["1.0.0+".Length..];
        RequireContract(
            attribute.Constructor.DeclaringType
                == typeof(AssemblyInformationalVersionAttribute)
            && attribute.NamedArguments.Count == 0
            && value.StartsWith("1.0.0+", StringComparison.Ordinal)
            && revision.Length == 40
            && revision.All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f'),
            "invalid-setup-live-application-informational-version");
    }

    private static void AssertExactManifestAttribute(
        IEnumerable<CustomAttributeData> attributes,
        Type attributeType,
        Type[] constructorParameterTypes,
        object[] constructorArguments,
        params NamedAttributeExpectation[] namedArguments)
    {
        CustomAttributeData attribute = attributes.Single(candidate =>
            candidate.AttributeType == attributeType);
        ConstructorInfo constructor = attributeType.GetConstructor(
                constructorParameterTypes)
            ?? throw new InvalidOperationException(
                $"missing-setup-live-manifest-constructor:"
                + $"{attributeType.AssemblyQualifiedName}");
        RequireContract(
            attribute.Constructor == constructor
            && attribute.ConstructorArguments.Count
                == constructorArguments.Length
            && attribute.ConstructorArguments
                .Select(argument => argument.Value)
                .SequenceEqual(constructorArguments)
            && attribute.NamedArguments.Count == namedArguments.Length,
            $"invalid-setup-live-manifest-attribute:"
            + $"{attributeType.AssemblyQualifiedName}");
        foreach (NamedAttributeExpectation expected in namedArguments)
        {
            CustomAttributeNamedArgument actual =
                attribute.NamedArguments.Single(argument =>
                    argument.MemberName == expected.Name);
            MemberInfo expectedMember =
                (MemberInfo?)attributeType.GetProperty(expected.Name)
                ?? attributeType.GetField(expected.Name)
                ?? throw new InvalidOperationException(
                    $"missing-setup-live-manifest-member:"
                    + $"{attributeType.AssemblyQualifiedName}.{expected.Name}");
            RequireContract(
                actual.MemberInfo == expectedMember
                && Equals(actual.TypedValue.Value, expected.Value),
                $"invalid-setup-live-manifest-named-argument:"
                + $"{attributeType.AssemblyQualifiedName}.{expected.Name}");
        }
    }

    private static void AssertMethodMetadata(
        MethodInfo method,
        MethodInfo witness,
        bool requireBody)
    {
        RequireContract(
            (method.Attributes & MethodAttributes.PinvokeImpl) == 0
            && (method.CallingConvention & CallingConventions.VarArgs) == 0
            && method.ReturnParameter.Attributes
                == witness.ReturnParameter.Attributes
            && method.ReturnParameter.GetRequiredCustomModifiers()
                .SequenceEqual(
                    witness.ReturnParameter.GetRequiredCustomModifiers())
            && method.ReturnParameter.GetOptionalCustomModifiers()
                .SequenceEqual(
                    witness.ReturnParameter.GetOptionalCustomModifiers()),
            $"modified-setup-live-application-return:"
            + $"{method.DeclaringType?.FullName}.{method.Name}");
        RequireContract(
            method.Attributes == witness.Attributes
            && method.GetMethodImplementationFlags()
                == witness.GetMethodImplementationFlags()
            && method.CallingConvention == witness.CallingConvention
            && method.IsGenericMethod == witness.IsGenericMethod
            && method.GetGenericArguments().Length
                == witness.GetGenericArguments().Length,
            $"invalid-setup-live-application-method-metadata:"
            + $"{method.DeclaringType?.FullName}.{method.Name}");
        AssertAttributeSet(
            method.CustomAttributes,
            witness.CustomAttributes,
            $"{method.DeclaringType?.FullName}.{method.Name}");
        AssertAttributeSet(
            method.ReturnParameter.CustomAttributes,
            witness.ReturnParameter.CustomAttributes,
            $"{method.DeclaringType?.FullName}.{method.Name}:return");
        ParameterInfo[] parameters = method.GetParameters();
        ParameterInfo[] witnessParameters = witness.GetParameters();
        RequireContract(
            parameters.Length == witnessParameters.Length,
            $"invalid-setup-live-application-method-parameter-count:"
            + $"{method.DeclaringType?.FullName}.{method.Name}");
        for (int index = 0; index < parameters.Length; index++)
        {
            AssertParameterMetadata(
                parameters[index],
                witnessParameters[index],
                compareType: false);
        }
        RequireContract(
            (method.GetMethodBody() is not null) == requireBody
            && (witness.GetMethodBody() is not null) == requireBody,
            $"invalid-setup-live-application-method-implementation:"
            + $"{method.DeclaringType?.FullName}.{method.Name}");
    }

    private static void AssertConstructorSignatureMetadata(
        ConstructorInfo constructor,
        ConstructorInfo witness)
    {
        MethodDefinitionMetadata actual = ReadMethodDefinition(constructor);
        MethodDefinitionMetadata expected = ReadMethodDefinition(witness);
        RequireContract(
            actual.ParameterCount == expected.ParameterCount
            && actual.ReturnTypeCode == SignatureTypeCode.Void
            && actual.ReturnTypeCode == expected.ReturnTypeCode
            && actual.ReturnParameter == expected.ReturnParameter,
            $"invalid-setup-live-application-constructor-signature:"
            + $"{constructor.DeclaringType?.FullName}");
    }

    private static MethodDefinitionMetadata ReadMethodDefinition(
        MethodBase method)
    {
        using FileStream stream = File.OpenRead(method.Module.Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle handle = MetadataTokens.MethodDefinitionHandle(
            method.MetadataToken & 0x00ff_ffff);
        MethodDefinition definition = reader.GetMethodDefinition(handle);
        BlobReader signature = reader.GetBlobReader(definition.Signature);
        SignatureHeader header = signature.ReadSignatureHeader();
        if (header.IsGeneric)
            _ = signature.ReadCompressedInteger();
        int parameterCount = signature.ReadCompressedInteger();
        SignatureTypeCode returnTypeCode =
            signature.ReadSignatureTypeCode();
        ReturnParameterMetadata? returnParameter = null;
        foreach (ParameterHandle parameterHandle in definition.GetParameters())
        {
            Parameter parameter = reader.GetParameter(parameterHandle);
            if (parameter.SequenceNumber != 0)
                continue;
            returnParameter = new ReturnParameterMetadata(
                parameter.Attributes,
                reader.GetString(parameter.Name),
                parameter.GetCustomAttributes().Count);
        }
        return new MethodDefinitionMetadata(
            parameterCount,
            returnTypeCode,
            returnParameter);
    }

    private static void AssertParameterMetadata(
        ParameterInfo parameter,
        ParameterInfo witness,
        bool compareType)
    {
        RequireContract(
            !parameter.IsOptional
            && !parameter.HasDefaultValue
            && !parameter.IsIn
            && !parameter.IsOut
            && !parameter.IsRetval
            && parameter.Attributes == witness.Attributes
            && (!compareType || parameter.ParameterType == witness.ParameterType)
            && (parameter.Attributes & ParameterAttributes.HasFieldMarshal)
                == 0
            && parameter.GetRequiredCustomModifiers().SequenceEqual(
                witness.GetRequiredCustomModifiers())
            && parameter.GetOptionalCustomModifiers().SequenceEqual(
                witness.GetOptionalCustomModifiers())
            && parameter.GetCustomAttribute<ParamArrayAttribute>() is null,
            $"modified-setup-live-application-parameter:"
            + $"{parameter.Member.DeclaringType?.FullName}:"
            + $"{parameter.Member.Name}:{parameter.Position}");
        AssertAttributeSet(
            parameter.CustomAttributes,
            witness.CustomAttributes,
            $"{parameter.Member.DeclaringType?.FullName}:"
            + $"{parameter.Member.Name}:{parameter.Position}");
    }

    private static void AssertAttributeSet(
        IEnumerable<CustomAttributeData> attributes,
        IEnumerable<CustomAttributeData> witnessAttributes,
        string owner)
    {
        List<CustomAttributeData> remaining =
            witnessAttributes.ToList();
        foreach (CustomAttributeData attribute in attributes)
        {
            int match = remaining.FindIndex(candidate =>
                AttributeEquals(attribute, candidate));
            RequireContract(
                match >= 0,
                $"invalid-setup-live-application-attribute-provenance:{owner}");
            remaining.RemoveAt(match);
        }
        RequireContract(
            remaining.Count == 0,
            $"invalid-setup-live-application-attribute-provenance:{owner}");
    }

    private static void AssertExactBackingFields(
        Type type,
        IReadOnlyCollection<PropertyContract> properties)
    {
        FieldInfo[] fields = type.GetFields(
                BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .ToArray();
        RequireContract(
            fields.Length == properties.Count,
            $"invalid-setup-live-application-backing-fields:{type.FullName}");
        foreach (PropertyContract property in properties)
        {
            FieldInfo? field = fields.SingleOrDefault(candidate =>
                candidate.Name == $"<{property.Name}>k__BackingField");
            RequireContract(
                field is not null
                && field.IsPrivate
                && field.IsInitOnly
                && !field.IsStatic
                && (field.Attributes & FieldAttributes.NotSerialized) == 0
                && field.FieldType == property.Type
                && field.IsDefined(
                    typeof(System.Runtime.CompilerServices
                        .CompilerGeneratedAttribute),
                    inherit: false),
                $"invalid-setup-live-application-backing-field:"
                + $"{type.FullName}.{property.Name}");
            FieldInfo witnessField = AttributeWitness(type).GetField(
                    $"<{property.Name}>k__BackingField",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException(
                    $"missing-setup-live-backing-field-witness:"
                    + $"{type.FullName}.{property.Name}");
            AssertFieldMetadata(
                field,
                witnessField,
                $"{type.FullName}.<{property.Name}>k__BackingField");
        }
    }

    private static void AssertFieldMetadata(
        FieldInfo field,
        FieldInfo witness,
        string owner)
    {
        bool fieldTypeMatches = witness.FieldType == witness.DeclaringType
            ? field.FieldType == field.DeclaringType
            : field.FieldType == witness.FieldType;
        RequireContract(
            field.Attributes == witness.Attributes
            && fieldTypeMatches
            && field.GetRequiredCustomModifiers().SequenceEqual(
                witness.GetRequiredCustomModifiers())
            && field.GetOptionalCustomModifiers().SequenceEqual(
                witness.GetOptionalCustomModifiers())
            && (!field.IsLiteral
                || Equals(
                    field.GetRawConstantValue(),
                    witness.GetRawConstantValue())),
            $"invalid-setup-live-application-field-metadata:{owner}");
        AssertAttributeSet(
            field.CustomAttributes,
            witness.CustomAttributes,
            owner);
    }

    private static bool AttributeEquals(
        CustomAttributeData left,
        CustomAttributeData right) =>
        left.AttributeType == right.AttributeType
        && left.Constructor == right.Constructor
        && TypedArgumentsEqual(
            left.ConstructorArguments,
            right.ConstructorArguments)
        && NamedArgumentsEqual(
            left.NamedArguments,
            right.NamedArguments);

    private static bool NamedArgumentsEqual(
        IList<CustomAttributeNamedArgument> left,
        IList<CustomAttributeNamedArgument> right)
    {
        if (left.Count != right.Count)
            return false;

        var remaining = right.ToList();
        foreach (CustomAttributeNamedArgument argument in left)
        {
            int match = remaining.FindIndex(candidate =>
                candidate.IsField == argument.IsField
                && candidate.MemberInfo == argument.MemberInfo
                && TypedArgumentEquals(
                    candidate.TypedValue,
                    argument.TypedValue));
            if (match < 0)
                return false;
            remaining.RemoveAt(match);
        }
        return remaining.Count == 0;
    }

    private static bool TypedArgumentsEqual(
        IList<CustomAttributeTypedArgument> left,
        IList<CustomAttributeTypedArgument> right) =>
        left.Count == right.Count
        && left.Zip(right, TypedArgumentEquals).All(equal => equal);

    private static bool TypedArgumentEquals(
        CustomAttributeTypedArgument left,
        CustomAttributeTypedArgument right)
    {
        if (left.ArgumentType != right.ArgumentType)
            return false;

        if (left.Value is IList<CustomAttributeTypedArgument> leftValues
            && right.Value is IList<CustomAttributeTypedArgument> rightValues)
        {
            return TypedArgumentsEqual(leftValues, rightValues);
        }

        return Equals(left.Value, right.Value);
    }

    private static Type AttributeWitness(Type type) => type.FullName switch
    {
        $"{ContractNamespace}.SetupSecretBindingWriteRequest" =>
            typeof(SetupSecretBindingWriteRequestAttributeWitness),
        $"{ContractNamespace}.SetupSecretBindingCommitmentRequest" =>
            typeof(SetupSecretBindingCommitmentRequestAttributeWitness),
        $"{ContractNamespace}.SetupSecretBindingCommitment" =>
            typeof(SetupSecretBindingCommitmentAttributeWitness),
        $"{ContractNamespace}.SetupSecretBindingCoordinationRequest" =>
            typeof(SetupSecretBindingCoordinationRequestAttributeWitness),
        $"{ContractNamespace}.SetupSecretBindingWriteOutcome" =>
            typeof(SetupSecretBindingWriteOutcomeAttributeWitness),
        $"{ContractNamespace}.SetupSecretBindingContractMetadata" =>
            typeof(SetupSecretBindingContractMetadataAttributeWitness),
        $"{ContractNamespace}.ISetupSecretBindingCommitmentAuthority" =>
            typeof(ISetupSecretBindingCommitmentAuthorityAttributeWitness),
        $"{ContractNamespace}.ISetupSecretBindingOperationCoordinator" =>
            typeof(ISetupSecretBindingOperationCoordinatorAttributeWitness),
        $"{SecretContractNamespace}.ISetupSecretBindingWriter" =>
            typeof(ISetupSecretBindingWriterAttributeWitness),
        $"{SecretContractNamespace}.ISetupSecretBindingCommitBarrier" =>
            typeof(ISetupSecretBindingCommitBarrierAttributeWitness),
        _ => throw new InvalidOperationException(
            $"missing-setup-live-type-witness:{type.FullName}")
    };

    private static PropertyContract Property(string name, Type type) =>
        new(name, type);

    private static MethodContract Method(
        string name,
        Type returnType,
        params Type[] parameterTypes) =>
        new(name, returnType, parameterTypes);

    private static void RequireContract(bool condition, string diagnostic)
    {
        if (!condition)
            throw new InvalidOperationException(diagnostic);
    }

    private static Guid Id(int value) => Guid.Parse(
        $"01991f00-0000-7000-8000-{value:D12}");

    private static IEnumerable<Guid> NonV7(int value)
    {
        yield return Guid.Parse($"10000000-0000-1000-8000-{value:D12}");
        yield return Guid.Parse($"10000000-0000-4000-8000-{value:D12}");
        yield return Guid.Parse($"10000000-0000-6000-8000-{value:D12}");
        yield return Guid.Parse($"10000000-0000-8000-8000-{value:D12}");
    }

    private static IEnumerable<string> SecretDiagnosticCanaries()
    {
        byte[] value = CanaryBytes().ToArray();
        yield return Encoding.UTF8.GetString(value);
        yield return Convert.ToHexString(value);
        yield return Convert.ToHexString(value).ToLowerInvariant();
        yield return Convert.ToBase64String(value);
        yield return string.Join(",", value);
        yield return string.Join("-", value);
    }

    private static string Digest(char value) => new(value, 64);

    private static IEnumerable<string?> InvalidCommitments()
    {
        yield return null;
        yield return string.Empty;
        yield return new string('a', 63);
        yield return new string('a', 65);
        yield return new string('A', 64);
        yield return new string('g', 64);
    }

    private static object?[] With(
        object?[] values,
        int position,
        object? replacement)
    {
        object?[] changed = values.ToArray();
        changed[position] = replacement;
        return changed;
    }

    private static T Read<T>(object instance, string propertyName) =>
        (T)(instance.GetType().GetProperty(propertyName)?.GetValue(instance)
            ?? throw new InvalidOperationException(
                $"missing-setup-live-application-value:"
                + $"{instance.GetType().FullName}.{propertyName}"));

    private static ReadOnlyMemory<byte> CanaryBytes() =>
        Encoding.UTF8.GetBytes("setup-d2-3-secret-canary");

    private sealed record PropertyContract(string Name, Type Type);

    private sealed record MethodContract(
        string Name,
        Type ReturnType,
        Type[] ParameterTypes);

    private sealed record MethodDefinitionMetadata(
        int ParameterCount,
        SignatureTypeCode ReturnTypeCode,
        ReturnParameterMetadata? ReturnParameter);

    private sealed record ReturnParameterMetadata(
        ParameterAttributes Attributes,
        string Name,
        int CustomAttributeCount);

    private sealed record NamedAttributeExpectation(
        string Name,
        object Value);
}

internal sealed class SetupSecretBindingWriteRequestAttributeWitness
{
    public SetupSecretBindingWriteRequestAttributeWitness(
        Guid tenantId,
        Guid enrollmentId,
        long enrollmentGeneration,
        Guid operationId,
        Guid bindingId,
        string bindingKey,
        ReadOnlyMemory<byte> secretValue)
    {
        TenantId = tenantId;
        EnrollmentId = enrollmentId;
        EnrollmentGeneration = enrollmentGeneration;
        OperationId = operationId;
        BindingId = bindingId;
        BindingKey = bindingKey;
        SecretValue = secretValue;
    }

    public Guid TenantId { get; }
    public Guid EnrollmentId { get; }
    public long EnrollmentGeneration { get; }
    public Guid OperationId { get; }
    public Guid BindingId { get; }
    public string BindingKey { get; }
    public ReadOnlyMemory<byte> SecretValue { get; }
}

internal enum SetupSecretBindingWriteOutcomeAttributeWitness
{
    Invalid = 0,
    Ready = 1,
    Unavailable = 2,
    Unauthorized = 3
}

internal sealed class SetupSecretBindingCommitmentRequestAttributeWitness
{
    public SetupSecretBindingCommitmentRequestAttributeWitness(
        Guid tenantId,
        Guid actorId,
        Guid enrollmentId,
        long enrollmentGeneration,
        Guid operationKey,
        string bindingKey,
        ReadOnlyMemory<byte> secretValue)
    {
        TenantId = tenantId;
        ActorId = actorId;
        EnrollmentId = enrollmentId;
        EnrollmentGeneration = enrollmentGeneration;
        OperationKey = operationKey;
        BindingKey = bindingKey;
        SecretValue = secretValue;
    }

    public Guid TenantId { get; }
    public Guid ActorId { get; }
    public Guid EnrollmentId { get; }
    public long EnrollmentGeneration { get; }
    public Guid OperationKey { get; }
    public string BindingKey { get; }
    public ReadOnlyMemory<byte> SecretValue { get; }
}

internal sealed class SetupSecretBindingCommitmentAttributeWitness
{
    public SetupSecretBindingCommitmentAttributeWitness(
        int keyVersion,
        string commitment)
    {
        KeyVersion = keyVersion;
        Commitment = commitment;
    }

    public int KeyVersion { get; }
    public string Commitment { get; }
}

internal sealed class SetupSecretBindingCoordinationRequestAttributeWitness
{
    public SetupSecretBindingCoordinationRequestAttributeWitness(
        Guid tenantId,
        Guid enrollmentId,
        long enrollmentGeneration)
    {
        TenantId = tenantId;
        EnrollmentId = enrollmentId;
        EnrollmentGeneration = enrollmentGeneration;
    }

    public Guid TenantId { get; }
    public Guid EnrollmentId { get; }
    public long EnrollmentGeneration { get; }
}

internal interface ISetupSecretBindingWriterAttributeWitness
{
    Task<SetupSecretBindingWriteOutcomeAttributeWitness> WriteAsync(
        SetupSecretBindingWriteRequestAttributeWitness request,
        CancellationToken cancellationToken);
}

internal interface ISetupSecretBindingCommitmentAuthorityAttributeWitness
{
    Task<SetupSecretBindingCommitmentAttributeWitness> CommitAsync(
        SetupSecretBindingCommitmentRequestAttributeWitness request,
        CancellationToken cancellationToken);
}

internal interface ISetupSecretBindingOperationCoordinatorAttributeWitness
{
    Task<IAsyncDisposable> AcquireAsync(
        SetupSecretBindingCoordinationRequestAttributeWitness request,
        CancellationToken cancellationToken);
}

internal interface ISetupSecretBindingCommitBarrierAttributeWitness
{
    Task WaitBeforeProviderDispatchAsync(
        CancellationToken cancellationToken);
}

internal static class SetupSecretBindingContractMetadataAttributeWitness
{
    public const string CommitmentAuthorityKey =
        "setup.secret_binding_commitment_hmac_key";
    public const int MilestoneEventId = 19620;
    public const string MilestoneEventName = "SetupLiveMilestone";
    public const string Operation = "secret_binding.write";
    public const string BeforeProviderDispatchMilestone =
        "before_provider_dispatch";
}
