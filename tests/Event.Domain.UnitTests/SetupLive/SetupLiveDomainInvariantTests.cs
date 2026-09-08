namespace Event.Domain.UnitTests.SetupLive;

using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Explore.Domain.Interfaces;

public sealed class SetupLiveDomainInvariantTests
{
    private const string Namespace = "Explore.Domain.SetupLive";
    private static readonly Assembly DomainAssembly =
        typeof(global::Explore.Domain.Tenant).Assembly;
    private static readonly DateTime CreatedAt =
        new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid PersistenceStamp =
        Guid.Parse("01991f00-0000-7000-8000-000000000099");

    [Test]
    public async Task EnrollmentOwnsExactMutableAuditedSurface()
    {
        Type type = RequireType("SetupTargetEnrollment");

        await Assert.That(typeof(ITenantEntity).IsAssignableFrom(type)).IsTrue();
        await Assert.That(typeof(IAuditableEntity).IsAssignableFrom(type)).IsTrue();
        await Assert.That(typeof(IConcurrencyAware).IsAssignableFrom(type)).IsTrue();
        AssertExactProperties(type,
            Property("Id", typeof(Guid)),
            Property("TenantId", typeof(Guid), publicSetter: true),
            Property("ActorId", typeof(Guid)),
            Property("ChallengeDigest", typeof(string)),
            Property("CapabilityDigest", typeof(string)),
            Property("ScopeDigest", typeof(string)),
            Property("Generation", typeof(long)),
            Property("State", RequireType("SetupEnrollmentState")),
            Property("CreatedAt", typeof(DateTime), publicSetter: true),
            Property("CreatedBy", typeof(Guid?), publicSetter: true),
            Property("UpdatedAt", typeof(DateTime?), publicSetter: true),
            Property("UpdatedBy", typeof(Guid?), publicSetter: true),
            Property("ExpiresAt", typeof(DateTime)),
            Property("RevokedAt", typeof(DateTime?)),
            Property("ExpiredAt", typeof(DateTime?)),
            Property("ConcurrencyStamp", typeof(Guid), publicSetter: true));
        AssertExactMethods(type,
            StaticMethod("Create", type,
                typeof(Guid), typeof(Guid), typeof(Guid), typeof(string),
                typeof(string), typeof(string), typeof(DateTime), typeof(DateTime)),
            InstanceMethod("RotateCapability", typeof(bool),
                typeof(string), typeof(DateTime), typeof(DateTime)),
            InstanceMethod("Revoke", typeof(bool), typeof(DateTime)),
            InstanceMethod("Expire", typeof(bool), typeof(DateTime)),
            InstanceMethod("IsAvailable", typeof(bool),
                typeof(Guid), typeof(Guid), typeof(long), typeof(DateTime)));
        AssertEnum(RequireType("SetupEnrollmentState"), "Active", "Revoked", "Expired");
        await Assert.That(type.GetProperty("Revision")).IsNull();
    }

    [Test]
    public async Task ClaimOwnsExactImmutableTenantEvidenceSurface()
    {
        Type type = RequireType("SetupEnrollmentIssuanceClaim");

        await Assert.That(typeof(ITenantEntity).IsAssignableFrom(type)).IsTrue();
        await Assert.That(typeof(IAuditableEntity).IsAssignableFrom(type)).IsFalse();
        await Assert.That(typeof(IConcurrencyAware).IsAssignableFrom(type)).IsFalse();
        AssertExactProperties(type,
            Property("Id", typeof(Guid)),
            Property("TenantId", typeof(Guid), publicSetter: true),
            Property("ActorId", typeof(Guid)),
            Property("OperationKey", typeof(Guid)),
            Property("EnrollmentId", typeof(Guid)),
            Property("EnrollmentGeneration", typeof(long)),
            Property("RequestFingerprint", typeof(string)),
            Property("ClaimedAt", typeof(DateTime)));
        AssertExactMethods(type,
            StaticMethod("Create", type,
                typeof(Guid), typeof(Guid), typeof(Guid), typeof(Guid), typeof(Guid),
                typeof(long), typeof(string), typeof(DateTime)),
            InstanceMethod("Match", RequireType("SetupReplayDecision"),
                typeof(Guid), typeof(Guid), typeof(string)));
        AssertEnum(RequireType("SetupReplayDecision"), "SameRequest", "Conflict");
    }

    [Test]
    public async Task OperationOwnsExactMutableAuditedSurface()
    {
        Type type = RequireType("SetupSecretBindingOperation");

        await Assert.That(typeof(ITenantEntity).IsAssignableFrom(type)).IsTrue();
        await Assert.That(typeof(IAuditableEntity).IsAssignableFrom(type)).IsTrue();
        await Assert.That(typeof(IConcurrencyAware).IsAssignableFrom(type)).IsTrue();
        AssertExactProperties(type,
            Property("Id", typeof(Guid)),
            Property("TenantId", typeof(Guid), publicSetter: true),
            Property("ActorId", typeof(Guid)),
            Property("EnrollmentId", typeof(Guid)),
            Property("EnrollmentGeneration", typeof(long)),
            Property("OperationKey", typeof(Guid)),
            Property("BindingKey", typeof(string)),
            Property("RequestFingerprint", typeof(string)),
            Property("CommitmentKeyVersion", typeof(int)),
            Property("SecretValueCommitment", typeof(string)),
            Property("State", RequireType("SetupSecretBindingOperationState")),
            Property("Outcome", RequireType("SetupSecretBindingOperationOutcome")),
            Property("CreatedAt", typeof(DateTime), publicSetter: true),
            Property("CreatedBy", typeof(Guid?), publicSetter: true),
            Property("UpdatedAt", typeof(DateTime?), publicSetter: true),
            Property("UpdatedBy", typeof(Guid?), publicSetter: true),
            Property("SettledAt", typeof(DateTime?)),
            Property("ConcurrencyStamp", typeof(Guid), publicSetter: true));
        AssertExactMethods(type,
            StaticMethod("CreateAccepted", type,
                typeof(Guid), typeof(Guid), typeof(Guid), typeof(Guid), typeof(long),
                typeof(Guid), typeof(string), typeof(string), typeof(int),
                typeof(string), typeof(DateTime)),
            InstanceMethod("Match", RequireType("SetupReplayDecision"),
                typeof(Guid), typeof(Guid), typeof(Guid), typeof(long), typeof(Guid),
                typeof(string), typeof(string), typeof(int), typeof(string)),
            InstanceMethod("CanDispatch", typeof(bool),
                RequireType("SetupTargetEnrollment"), typeof(DateTime)),
            InstanceMethod("Succeed", typeof(bool), typeof(DateTime)),
            InstanceMethod("Fail", typeof(bool),
                RequireType("SetupSecretBindingOperationOutcome"), typeof(DateTime)),
            InstanceMethod("Cancel", typeof(bool), typeof(DateTime)));
        AssertEnum(RequireType("SetupSecretBindingOperationState"),
            "Accepted", "Succeeded", "Failed", "Cancelled");
        AssertEnum(RequireType("SetupSecretBindingOperationOutcome"),
            "Accepted", "Ready", "Unavailable", "Unauthorized", "Invalid",
            "Cancelled", "UnavailableEnrollment");
        await Assert.That(type.GetProperty("Revision")).IsNull();
    }

    [Test]
    public async Task SetupLiveNamespaceHasOnlyClosedValueFreePublicTypes()
    {
        _ = RequireType("SetupTargetEnrollment");

        string[] expected =
        [
            "SetupEnrollmentState",
            "SetupEnrollmentIssuanceClaim",
            "SetupReplayDecision",
            "SetupSecretBindingOperation",
            "SetupSecretBindingOperationOutcome",
            "SetupSecretBindingOperationState",
            "SetupTargetEnrollment"
        ];
        Type[] publicTypes = DomainAssembly.GetExportedTypes()
            .Where(type => type.Namespace == Namespace)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();
        await Assert.That(publicTypes.Select(type => type.Name)).IsEquivalentTo(expected);

        string[] forbiddenFragments =
        [
            "capability", "credential", "token", "targeturl", "provider",
            "source", "coordinate", "secretbytes", "secretvalue", "rawvalue",
            "response", "payload", "diagnostic", "errordetail", "p9-008"
        ];
        foreach (Type type in publicTypes)
        {
            string publicShape = string.Join('|', PublicShapeNames(type)).ToLowerInvariant();
            publicShape = publicShape.Replace(
                "rotatecapability", "rotate", StringComparison.Ordinal);
            publicShape = publicShape.Replace(
                "capabilitydigest", "digest", StringComparison.Ordinal);
            publicShape = publicShape.Replace(
                "secretvaluecommitment", "commitment", StringComparison.Ordinal);
            foreach (string forbidden in forbiddenFragments)
            {
                RequireContract(!publicShape.Contains(forbidden, StringComparison.Ordinal),
                    $"forbidden-setup-live-domain-surface:{type.FullName}:"
                    + $"{forbidden}:{publicShape}");
            }

            RequireContract(type.GetCustomAttribute<DebuggerDisplayAttribute>() is null,
                $"forbidden-setup-live-domain-debugger-display:{type.FullName}");
            RequireContract(type.GetCustomAttribute<DebuggerTypeProxyAttribute>() is null,
                $"forbidden-setup-live-domain-debugger-proxy:{type.FullName}");
        }

        string[] references = DomainAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty).ToArray();
        await Assert.That(references.Any(reference =>
            reference.Contains("P9", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    [Test]
    public async Task EnrollmentFactoryClosesEveryIdentityDigestAndTimeBoundary()
    {
        Type type = RequireType("SetupTargetEnrollment");
        object valid = CreateEnrollment();

        AssertUtc(valid, "CreatedAt", "ExpiresAt");
        await Assert.That(Read<long>(valid, "Generation")).IsEqualTo(1);
        await Assert.That(Read<object>(valid, "State").ToString()).IsEqualTo("Active");
        await Assert.That(Read<Guid?>(valid, "CreatedBy")).IsNull();
        await Assert.That(Read<DateTime?>(valid, "UpdatedAt")).IsNull();
        await Assert.That(Read<Guid?>(valid, "UpdatedBy")).IsNull();
        await Assert.That(Read<Guid>(valid, "ConcurrencyStamp")).IsEqualTo(Guid.Empty);

        object?[] arguments = EnrollmentArguments();
        foreach (int index in new[] { 0, 1, 2 })
        {
            foreach (Guid invalid in new[] { Guid.Empty, V4(index) })
                ExpectFactoryArgumentException(type, "Create", arguments, index, invalid);
        }

        foreach (int index in new[] { 3, 4, 5 })
        {
            foreach (string? invalid in InvalidHashes())
                ExpectFactoryArgumentException(type, "Create", arguments, index, invalid);
        }

        foreach (DateTime invalid in InvalidKinds(CreatedAt))
            ExpectFactoryArgumentException(type, "Create", arguments, 6, invalid);
        foreach (DateTime invalid in InvalidKinds(CreatedAt.AddHours(1)))
            ExpectFactoryArgumentException(type, "Create", arguments, 7, invalid);
        ExpectFactoryArgumentException(type, "Create", arguments, 7, CreatedAt);
        ExpectFactoryArgumentException(type, "Create", arguments, 7, CreatedAt.AddTicks(-1));
    }

    [Test]
    public async Task EnrollmentAvailabilityAndRotationFenceAllInputsAtomically()
    {
        _ = RequireType("SetupTargetEnrollment");
        object enrollment = CreateEnrollment();
        Set(enrollment, "ConcurrencyStamp", PersistenceStamp);
        Guid tenantId = Read<Guid>(enrollment, "TenantId");
        Guid actorId = Read<Guid>(enrollment, "ActorId");

        await Assert.That(IsAvailable(enrollment, tenantId, actorId, 1,
            CreatedAt.AddMinutes(1))).IsTrue();
        foreach ((Guid tenant, Guid actor, long generation, DateTime observedAt) invalid in
        new[]
        {
            (Id(20), actorId, 1L, CreatedAt.AddMinutes(1)),
            (tenantId, Id(21), 1L, CreatedAt.AddMinutes(1)),
            (tenantId, actorId, 0L, CreatedAt.AddMinutes(1)),
            (tenantId, actorId, -1L, CreatedAt.AddMinutes(1)),
            (tenantId, actorId, 2L, CreatedAt.AddMinutes(1)),
            (tenantId, actorId, 1L, CreatedAt.AddHours(1))
        })
        {
            object?[] before = Snapshot(enrollment);
            await Assert.That(IsAvailable(enrollment, invalid.tenant, invalid.actor,
                invalid.generation, invalid.observedAt)).IsFalse();
            AssertSnapshot(enrollment, before);
        }
        foreach (Guid invalid in new[] { Guid.Empty, V4(22) })
        {
            ExpectRejectedUnchanged(enrollment, () => IsAvailable(
                enrollment, invalid, actorId, 1, CreatedAt.AddMinutes(1)),
                typeof(ArgumentException));
            ExpectRejectedUnchanged(enrollment, () => IsAvailable(
                enrollment, tenantId, invalid, 1, CreatedAt.AddMinutes(1)),
                typeof(ArgumentException));
        }

        foreach (DateTime invalid in InvalidKinds(CreatedAt.AddMinutes(1)))
            ExpectRejectedUnchanged(enrollment, () => IsAvailable(
                enrollment, tenantId, actorId, 1, invalid), typeof(ArgumentException));
        ExpectRejectedUnchanged(enrollment, () => IsAvailable(
            enrollment, tenantId, actorId, 1, CreatedAt.AddTicks(-1)),
            typeof(ArgumentException));

        foreach ((object? value, int position) invalid in RotationInvalidArguments())
        {
            object?[] rotation =
                [Digest('d'), CreatedAt.AddHours(2), CreatedAt.AddMinutes(2)];
            rotation[invalid.position] = invalid.value;
            ExpectRejectedUnchanged(enrollment, () => Rotate(enrollment,
                (string?)rotation[0], (DateTime)rotation[1]!, (DateTime)rotation[2]!),
                typeof(ArgumentException));
        }

        bool changed = Rotate(enrollment, Digest('d'), CreatedAt.AddHours(2),
            CreatedAt.AddMinutes(2));
        await Assert.That(changed).IsTrue();
        await Assert.That(Read<long>(enrollment, "Generation")).IsEqualTo(2);
        await Assert.That(Read<string>(enrollment, "CapabilityDigest")).IsEqualTo(Digest('d'));
        await Assert.That(Read<Guid>(enrollment, "ConcurrencyStamp"))
            .IsEqualTo(PersistenceStamp);
        await Assert.That(Read<Guid?>(enrollment, "CreatedBy")).IsNull();
        await Assert.That(Read<DateTime?>(enrollment, "UpdatedAt")).IsNull();
        await Assert.That(Read<Guid?>(enrollment, "UpdatedBy")).IsNull();
        await Assert.That(IsAvailable(enrollment, tenantId, actorId, 1,
            CreatedAt.AddMinutes(3))).IsFalse();
        await Assert.That(IsAvailable(enrollment, tenantId, actorId, 2,
            CreatedAt.AddMinutes(3))).IsTrue();
    }

    [Test]
    public async Task EnrollmentGenerationOverflowAndTerminalMatrixAreAtomic()
    {
        _ = RequireType("SetupTargetEnrollment");
        object maximum = CreateEnrollment();
        Set(maximum, "Generation", long.MaxValue);
        Set(maximum, "ConcurrencyStamp", PersistenceStamp);
        ExpectRejectedUnchanged(maximum, () => Rotate(maximum, Digest('d'),
            CreatedAt.AddHours(2), CreatedAt.AddMinutes(1)),
            typeof(InvalidOperationException));

        foreach (string terminal in new[] { "Revoked", "Expired" })
        {
            object enrollment = CreateEnrollment();
            Set(enrollment, "ConcurrencyStamp", PersistenceStamp);
            DateTime terminalAt = terminal == "Revoked"
                ? CreatedAt.AddMinutes(1)
                : CreatedAt.AddHours(1);
            Dictionary<string, object?> expected = NamedSnapshot(enrollment);
            bool first = terminal == "Revoked"
                ? Revoke(enrollment, terminalAt)
                : Expire(enrollment, terminalAt);
            await Assert.That(first).IsTrue();
            expected["State"] = Enum.Parse(
                RequiredProperty(enrollment.GetType(), "State").PropertyType,
                terminal);
            expected[terminal == "Revoked" ? "RevokedAt" : "ExpiredAt"] = terminalAt;
            AssertNamedSnapshot(enrollment, expected);
            AssertUtc(enrollment, terminal == "Revoked" ? "RevokedAt" : "ExpiredAt");
            await Assert.That(Read<Guid?>(enrollment, "CreatedBy")).IsNull();
            await Assert.That(Read<DateTime?>(enrollment, "UpdatedAt")).IsNull();
            await Assert.That(Read<Guid?>(enrollment, "UpdatedBy")).IsNull();
            object?[] settled = Snapshot(enrollment);

            bool replay = terminal == "Revoked"
                ? Revoke(enrollment, CreatedAt.AddMinutes(2))
                : Expire(enrollment, CreatedAt.AddHours(1).AddMinutes(1));
            await Assert.That(replay).IsFalse();
            AssertSnapshot(enrollment, settled);

            ExpectRejectedUnchanged(enrollment,
                () => Rotate(enrollment, Digest('d'), CreatedAt.AddHours(2),
                    CreatedAt.AddMinutes(3)), typeof(InvalidOperationException));
            ExpectRejectedUnchanged(enrollment,
                () =>
                {
                    _ = terminal == "Revoked"
                        ? Expire(enrollment, CreatedAt.AddHours(1))
                        : Revoke(enrollment, CreatedAt.AddHours(1).AddMinutes(1));
                },
                typeof(InvalidOperationException));
        }

        object active = CreateEnrollment();
        foreach (DateTime invalid in InvalidKinds(CreatedAt.AddMinutes(1)))
        {
            ExpectRejectedUnchanged(active, () => Revoke(active, invalid),
                typeof(ArgumentException));
            ExpectRejectedUnchanged(active, () => Expire(active, invalid),
                typeof(ArgumentException));
        }
        ExpectRejectedUnchanged(active, () => Revoke(active, CreatedAt.AddTicks(-1)),
            typeof(ArgumentException));
        ExpectRejectedUnchanged(active, () => Expire(active, CreatedAt.AddTicks(-1)),
            typeof(ArgumentException));
        ExpectRejectedUnchanged(active, () => Expire(active, CreatedAt.AddMinutes(1)),
            typeof(ArgumentException));
    }

    [Test]
    public async Task ClaimFactoryClosesIdentityGenerationDigestAndUtcBoundaries()
    {
        Type type = RequireType("SetupEnrollmentIssuanceClaim");
        object valid = CreateClaim();
        AssertUtc(valid, "ClaimedAt");
        await Assert.That(Read<long>(valid, "EnrollmentGeneration")).IsEqualTo(1);

        object?[] arguments = ClaimArguments();
        foreach (int index in new[] { 0, 1, 2, 3, 4 })
        {
            foreach (Guid invalid in new[] { Guid.Empty, V4(index) })
                ExpectFactoryArgumentException(type, "Create", arguments, index, invalid);
        }
        foreach (long invalid in new[] { 0L, -1L })
            ExpectFactoryArgumentException(type, "Create", arguments, 5, invalid);
        foreach (string? invalid in InvalidHashes())
            ExpectFactoryArgumentException(type, "Create", arguments, 6, invalid);
        foreach (DateTime invalid in InvalidKinds(CreatedAt))
            ExpectFactoryArgumentException(type, "Create", arguments, 7, invalid);
    }

    [Test]
    public async Task ClaimReplayIsExactOrdinalValueFreeAndImmutable()
    {
        _ = RequireType("SetupEnrollmentIssuanceClaim");
        object claim = CreateClaim();
        Guid tenantId = Read<Guid>(claim, "TenantId");
        Guid actorId = Read<Guid>(claim, "ActorId");
        string fingerprint = Read<string>(claim, "RequestFingerprint");
        object?[] before = Snapshot(claim);

        await Assert.That(ClaimMatch(claim, tenantId, actorId, fingerprint).ToString())
            .IsEqualTo("SameRequest");
        foreach ((Guid tenant, Guid actor, string value) changed in new[]
        {
            (Id(30), actorId, fingerprint),
            (tenantId, Id(31), fingerprint),
            (tenantId, actorId, Digest('f'))
        })
        {
            await Assert.That(ClaimMatch(claim, changed.tenant, changed.actor,
                changed.value).ToString()).IsEqualTo("Conflict");
            AssertSnapshot(claim, before);
        }
        foreach (Guid invalid in new[] { Guid.Empty, V4(32) })
        {
            ExpectRejectedUnchanged(claim,
                () => _ = ClaimMatch(claim, invalid, actorId, fingerprint),
                typeof(ArgumentException));
            ExpectRejectedUnchanged(claim,
                () => _ = ClaimMatch(claim, tenantId, invalid, fingerprint),
                typeof(ArgumentException));
        }
        foreach (string? invalid in InvalidHashes())
        {
            ExpectRejectedUnchanged(claim,
                () => _ = ClaimMatch(claim, tenantId, actorId, invalid!),
                typeof(ArgumentException));
        }

        foreach (string? canary in EvidenceValues(claim))
            await Assert.That(claim.ToString()).DoesNotContain(canary!);
    }

    [Test]
    public async Task OperationFactoryClosesEveryImmutableInputBoundary()
    {
        Type type = RequireType("SetupSecretBindingOperation");
        object valid = CreateOperation();
        AssertUtc(valid, "CreatedAt");
        await Assert.That(Read<long>(valid, "EnrollmentGeneration")).IsEqualTo(1);
        await Assert.That(Read<int>(valid, "CommitmentKeyVersion")).IsEqualTo(1);
        await Assert.That(Read<object>(valid, "State").ToString()).IsEqualTo("Accepted");
        await Assert.That(Read<object>(valid, "Outcome").ToString()).IsEqualTo("Accepted");
        await Assert.That(Read<DateTime?>(valid, "SettledAt")).IsNull();
        await Assert.That(Read<Guid?>(valid, "CreatedBy")).IsNull();
        await Assert.That(Read<DateTime?>(valid, "UpdatedAt")).IsNull();
        await Assert.That(Read<Guid?>(valid, "UpdatedBy")).IsNull();
        await Assert.That(Read<Guid>(valid, "ConcurrencyStamp")).IsEqualTo(Guid.Empty);

        object?[] arguments = OperationArguments();
        foreach (int index in new[] { 0, 1, 2, 3, 5 })
        {
            foreach (Guid invalid in new[] { Guid.Empty, V4(index) })
                ExpectFactoryArgumentException(type, "CreateAccepted", arguments,
                    index, invalid);
        }
        foreach (long invalid in new[] { 0L, -1L })
            ExpectFactoryArgumentException(type, "CreateAccepted", arguments, 4, invalid);
        foreach (string? invalid in new[] { null, string.Empty, " ", "unknown.binding" })
            ExpectFactoryArgumentException(type, "CreateAccepted", arguments, 6, invalid);
        foreach (string? invalid in InvalidHashes())
        {
            ExpectFactoryArgumentException(type, "CreateAccepted", arguments, 7, invalid);
            ExpectFactoryArgumentException(type, "CreateAccepted", arguments, 9, invalid);
        }
        foreach (int invalid in new[] { 0, -1 })
            ExpectFactoryArgumentException(type, "CreateAccepted", arguments, 8, invalid);
        foreach (DateTime invalid in InvalidKinds(CreatedAt))
            ExpectFactoryArgumentException(type, "CreateAccepted", arguments, 10, invalid);
    }

    [Test]
    public async Task OperationReplayDecisionClosesEveryIdentityInputWithoutMutation()
    {
        _ = RequireType("SetupSecretBindingOperation");
        object operation = CreateOperation();
        object?[] exact = OperationReplayArguments(operation);
        object?[] before = Snapshot(operation);

        await Assert.That(OperationMatch(operation, exact).ToString())
            .IsEqualTo("SameRequest");
        (int Index, object Value)[] changes =
        [
            (0, Id(40)), (1, Id(41)), (2, Id(42)), (3, 2L), (4, Id(43)),
            (5, "setup.encryption"), (6, Digest('f')), (7, 2), (8, Digest('a'))
        ];
        foreach ((int index, object value) in changes)
        {
            object?[] changed = exact.ToArray();
            changed[index] = value;
            await Assert.That(OperationMatch(operation, changed).ToString())
                .IsEqualTo("Conflict");
            AssertSnapshot(operation, before);
        }
        foreach (int index in new[] { 0, 1, 2, 4 })
        {
            foreach (Guid invalid in new[] { Guid.Empty, V4(index + 60) })
            {
                object?[] changed = exact.ToArray();
                changed[index] = invalid;
                ExpectRejectedUnchanged(operation,
                    () => _ = OperationMatch(operation, changed),
                    typeof(ArgumentException));
            }
        }
        foreach (long invalid in new[] { 0L, -1L })
        {
            object?[] changed = exact.ToArray();
            changed[3] = invalid;
            ExpectRejectedUnchanged(operation,
                () => _ = OperationMatch(operation, changed),
                typeof(ArgumentException));
        }
        foreach (string? invalid in new[] { null, string.Empty, " ", "unknown.binding" })
        {
            object?[] changed = exact.ToArray();
            changed[5] = invalid;
            ExpectRejectedUnchanged(operation,
                () => _ = OperationMatch(operation, changed),
                typeof(ArgumentException));
        }
        foreach (int index in new[] { 6, 8 })
        {
            foreach (string? invalid in InvalidHashes())
            {
                object?[] changed = exact.ToArray();
                changed[index] = invalid;
                ExpectRejectedUnchanged(operation,
                    () => _ = OperationMatch(operation, changed),
                    typeof(ArgumentException));
            }
        }
        foreach (int invalid in new[] { 0, -1 })
        {
            object?[] changed = exact.ToArray();
            changed[7] = invalid;
            ExpectRejectedUnchanged(operation,
                () => _ = OperationMatch(operation, changed),
                typeof(ArgumentException));
        }

        foreach (string? canary in EvidenceValues(operation))
            await Assert.That(operation.ToString()).DoesNotContain(canary!);
    }

    [Test]
    public async Task DispatchFencesEnrollmentIdentityTenantActorGenerationAndTime()
    {
        _ = RequireType("SetupSecretBindingOperation");
        object enrollment = CreateEnrollment();
        object operation = CreateOperation(enrollment);
        DateTime observedAt = CreatedAt.AddMinutes(1);
        await Assert.That(CanDispatch(operation, enrollment, observedAt)).IsTrue();

        object[] mismatches =
        [
            CreateEnrollment(id: Id(50)),
            CreateEnrollment(tenantId: Id(51)),
            CreateEnrollment(actorId: Id(52)),
            CreateEnrollment(id: Read<Guid>(enrollment, "Id"), generation: 2)
        ];
        foreach (object mismatch in mismatches)
            await AssertDispatchRejectedUnchanged(operation, mismatch, observedAt);

        object rotated = CreateEnrollment(
            id: Read<Guid>(enrollment, "Id"),
            tenantId: Read<Guid>(enrollment, "TenantId"),
            actorId: Read<Guid>(enrollment, "ActorId"));
        Rotate(rotated, Digest('d'), CreatedAt.AddHours(2), CreatedAt.AddSeconds(1));
        await AssertDispatchRejectedUnchanged(operation, rotated, observedAt);

        object revoked = MatchingEnrollment(operation);
        Revoke(revoked, CreatedAt.AddSeconds(1));
        await AssertDispatchRejectedUnchanged(operation, revoked, observedAt);

        object expired = MatchingEnrollment(operation, expiresAt: observedAt);
        await AssertDispatchRejectedUnchanged(operation, expired, observedAt);
        await AssertDispatchRejectedUnchanged(operation, enrollment, CreatedAt.AddHours(1));

        foreach (DateTime invalid in InvalidKinds(observedAt))
            ExpectRejectedPairUnchanged(operation, enrollment,
                () => CanDispatch(operation, enrollment, invalid), typeof(ArgumentException));
        ExpectRejectedPairUnchanged(operation, enrollment,
            () => CanDispatch(operation, enrollment, CreatedAt.AddTicks(-1)),
            typeof(ArgumentException));
        ExpectRejectedUnchanged(operation,
            () => CanDispatch(operation, null!, observedAt),
            typeof(ArgumentNullException));
    }

    [Test]
    public async Task OperationRejectedMutationsAreAtomicAndStampIsPersistenceManaged()
    {
        _ = RequireType("SetupSecretBindingOperation");
        object operation = CreateOperation();
        Set(operation, "ConcurrencyStamp", PersistenceStamp);
        Type outcomeType = RequireType("SetupSecretBindingOperationOutcome");

        foreach (DateTime invalid in InvalidKinds(CreatedAt.AddMinutes(1))
            .Append(CreatedAt.AddTicks(-1)))
        {
            ExpectRejectedUnchanged(operation, () => Succeed(operation, invalid),
                typeof(ArgumentException));
            ExpectRejectedUnchanged(operation, () => Cancel(operation, invalid),
                typeof(ArgumentException));
            ExpectRejectedUnchanged(operation, () => Fail(operation,
                Enum.Parse(outcomeType, "Unavailable"), invalid),
                typeof(ArgumentException));
        }
        foreach (string invalidOutcome in new[] { "Accepted", "Ready", "Cancelled" })
        {
            ExpectRejectedUnchanged(operation, () => Fail(operation,
                Enum.Parse(outcomeType, invalidOutcome), CreatedAt.AddMinutes(1)),
                typeof(ArgumentException));
        }

        bool succeeded = Succeed(operation, CreatedAt.AddMinutes(1));
        await Assert.That(succeeded).IsTrue();
        await Assert.That(Read<object>(operation, "State").ToString())
            .IsEqualTo("Succeeded");
        await Assert.That(Read<object>(operation, "Outcome").ToString())
            .IsEqualTo("Ready");
        await Assert.That(Read<DateTime?>(operation, "SettledAt"))
            .IsEqualTo(CreatedAt.AddMinutes(1));
        await Assert.That(Read<Guid>(operation, "ConcurrencyStamp"))
            .IsEqualTo(PersistenceStamp);
        await Assert.That(Read<Guid?>(operation, "CreatedBy")).IsNull();
        await Assert.That(Read<DateTime?>(operation, "UpdatedAt")).IsNull();
        await Assert.That(Read<Guid?>(operation, "UpdatedBy")).IsNull();
        AssertUtc(operation, "SettledAt");
    }

    [Test]
    public async Task OperationTerminalReplayAndContradictionMatrixPreservesFirstOutcome()
    {
        _ = RequireType("SetupSecretBindingOperation");
        Type outcomeType = RequireType("SetupSecretBindingOperationOutcome");
        string[] terminals = ["Succeeded", "Failed", "Cancelled"];

        foreach (string terminal in terminals)
        {
            object operation = CreateOperation();
            Set(operation, "ConcurrencyStamp", PersistenceStamp);
            DateTime settledAt = CreatedAt.AddMinutes(1);
            Dictionary<string, object?> expected = NamedSnapshot(operation);
            bool first = Settle(operation, terminal, outcomeType,
                settledAt);
            await Assert.That(first).IsTrue();
            expected["State"] = Enum.Parse(
                RequiredProperty(operation.GetType(), "State").PropertyType,
                terminal);
            expected["Outcome"] = Enum.Parse(
                outcomeType,
                terminal switch
                {
                    "Succeeded" => "Ready",
                    "Failed" => "Unavailable",
                    "Cancelled" => "Cancelled",
                    _ => throw new ArgumentOutOfRangeException(nameof(terminal))
                });
            expected["SettledAt"] = settledAt;
            AssertNamedSnapshot(operation, expected);
            AssertUtc(operation, "SettledAt");
            object?[] settled = Snapshot(operation);

            bool replay = Settle(operation, terminal, outcomeType,
                CreatedAt.AddMinutes(2));
            await Assert.That(replay).IsFalse();
            AssertSnapshot(operation, settled);

            foreach (string contradiction in terminals.Where(value => value != terminal))
            {
                ExpectRejectedUnchanged(operation, () => Settle(operation,
                    contradiction, outcomeType, CreatedAt.AddMinutes(3)),
                    typeof(InvalidOperationException));
            }
        }

        foreach (string failedOutcome in new[]
        {
            "Unavailable", "Unauthorized", "Invalid", "UnavailableEnrollment"
        })
        {
            object failed = CreateOperation();
            await Assert.That(Fail(failed, Enum.Parse(outcomeType, failedOutcome),
                CreatedAt.AddMinutes(1))).IsTrue();
            await Assert.That(Read<object>(failed, "Outcome").ToString())
                .IsEqualTo(failedOutcome);
        }

        object changedFailure = CreateOperation();
        await Assert.That(Fail(changedFailure, Enum.Parse(outcomeType, "Unavailable"),
            CreatedAt.AddMinutes(1))).IsTrue();
        ExpectRejectedUnchanged(changedFailure, () => Fail(
            changedFailure,
            Enum.Parse(outcomeType, "Unauthorized"),
            CreatedAt.AddMinutes(2)),
            typeof(InvalidOperationException));
    }

    private static object CreateEnrollment(
        Guid? id = null,
        Guid? tenantId = null,
        Guid? actorId = null,
        long generation = 1,
        DateTime? expiresAt = null)
    {
        Type type = RequireType("SetupTargetEnrollment");
        object enrollment = InvokeStatic(type, "Create", type,
            [typeof(Guid), typeof(Guid), typeof(Guid), typeof(string), typeof(string),
                typeof(string), typeof(DateTime), typeof(DateTime)],
            id ?? Id(1), tenantId ?? Id(2), actorId ?? Id(3), Digest('a'), Digest('b'),
            Digest('c'), CreatedAt, expiresAt ?? CreatedAt.AddHours(1))!;
        if (generation != 1)
            Set(enrollment, "Generation", generation);
        return enrollment;
    }

    private static object CreateClaim() => InvokeStatic(
        RequireType("SetupEnrollmentIssuanceClaim"), "Create",
        RequireType("SetupEnrollmentIssuanceClaim"),
        [typeof(Guid), typeof(Guid), typeof(Guid), typeof(Guid), typeof(Guid),
            typeof(long), typeof(string), typeof(DateTime)], ClaimArguments())!;

    private static object CreateOperation(object? enrollment = null)
    {
        object owner = enrollment ?? CreateEnrollment();
        Type type = RequireType("SetupSecretBindingOperation");
        return InvokeStatic(type, "CreateAccepted", type,
            [typeof(Guid), typeof(Guid), typeof(Guid), typeof(Guid), typeof(long),
                typeof(Guid), typeof(string), typeof(string), typeof(int),
                typeof(string), typeof(DateTime)],
            Id(10), Read<Guid>(owner, "TenantId"), Read<Guid>(owner, "ActorId"),
            Read<Guid>(owner, "Id"), Read<long>(owner, "Generation"), Id(11),
            "setup.signing", Digest('d'), 1, Digest('e'), CreatedAt)!;
    }

    private static object MatchingEnrollment(object operation, DateTime? expiresAt = null) =>
        CreateEnrollment(Read<Guid>(operation, "EnrollmentId"),
            Read<Guid>(operation, "TenantId"), Read<Guid>(operation, "ActorId"),
            Read<long>(operation, "EnrollmentGeneration"), expiresAt);

    private static object?[] EnrollmentArguments() =>
    [
        Id(1), Id(2), Id(3), Digest('a'), Digest('b'), Digest('c'), CreatedAt,
        CreatedAt.AddHours(1)
    ];

    private static object?[] ClaimArguments() =>
    [
        Id(4), Id(2), Id(3), Id(5), Id(1), 1L, Digest('d'), CreatedAt
    ];

    private static object?[] OperationArguments() =>
    [
        Id(10), Id(2), Id(3), Id(1), 1L, Id(11), "setup.signing", Digest('d'),
        1, Digest('e'), CreatedAt
    ];

    private static object?[] OperationReplayArguments(object operation) =>
    [
        Read<Guid>(operation, "TenantId"), Read<Guid>(operation, "ActorId"),
        Read<Guid>(operation, "EnrollmentId"),
        Read<long>(operation, "EnrollmentGeneration"),
        Read<Guid>(operation, "OperationKey"), Read<string>(operation, "BindingKey"),
        Read<string>(operation, "RequestFingerprint"),
        Read<int>(operation, "CommitmentKeyVersion"),
        Read<string>(operation, "SecretValueCommitment")
    ];

    private static IEnumerable<(object? Value, int Position)> RotationInvalidArguments()
    {
        foreach (string? hash in InvalidHashes())
            yield return (hash, 0);
        foreach (DateTime value in InvalidKinds(CreatedAt.AddHours(2)))
            yield return (value, 1);
        foreach (DateTime value in InvalidKinds(CreatedAt.AddMinutes(2)))
            yield return (value, 2);
        yield return (CreatedAt.AddMinutes(2), 1);
        yield return (CreatedAt.AddMinutes(30), 1);
        yield return (CreatedAt.AddHours(1), 1);
        yield return (CreatedAt.AddTicks(-1), 2);
        yield return (CreatedAt.AddHours(1), 2);
    }

    private static IEnumerable<string?> InvalidHashes()
    {
        yield return null;
        yield return string.Empty;
        yield return new string('a', 63);
        yield return new string('a', 65);
        yield return new string('A', 64);
        yield return new string('g', 64);
    }

    private static IEnumerable<DateTime> InvalidKinds(DateTime value)
    {
        yield return DateTime.SpecifyKind(value, DateTimeKind.Local);
        yield return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
    }

    private static IEnumerable<string?> EvidenceValues(object instance) =>
        instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => (string?)property.GetValue(instance));

    private static bool IsAvailable(object enrollment, Guid tenantId, Guid actorId,
        long generation, DateTime observedAt) => (bool)Invoke(enrollment,
        "IsAvailable", typeof(bool),
        [typeof(Guid), typeof(Guid), typeof(long), typeof(DateTime)],
        tenantId, actorId, generation, observedAt)!;

    private static bool Rotate(object enrollment, string? digest, DateTime expiresAt,
        DateTime observedAt) => (bool)Invoke(enrollment, "RotateCapability", typeof(bool),
        [typeof(string), typeof(DateTime), typeof(DateTime)],
        digest, expiresAt, observedAt)!;

    private static bool Revoke(object enrollment, DateTime observedAt) =>
        (bool)Invoke(enrollment, "Revoke", typeof(bool), [typeof(DateTime)], observedAt)!;

    private static bool Expire(object enrollment, DateTime observedAt) =>
        (bool)Invoke(enrollment, "Expire", typeof(bool), [typeof(DateTime)], observedAt)!;

    private static object ClaimMatch(object claim, Guid tenantId, Guid actorId,
        string fingerprint) => Invoke(claim, "Match", RequireType("SetupReplayDecision"),
        [typeof(Guid), typeof(Guid), typeof(string)], tenantId, actorId, fingerprint)!;

    private static object OperationMatch(object operation, object?[] arguments) =>
        Invoke(operation, "Match", RequireType("SetupReplayDecision"),
            [typeof(Guid), typeof(Guid), typeof(Guid), typeof(long), typeof(Guid),
                typeof(string), typeof(string), typeof(int), typeof(string)], arguments)!;

    private static bool CanDispatch(object operation, object enrollment,
        DateTime observedAt) => (bool)Invoke(operation, "CanDispatch", typeof(bool),
        [RequireType("SetupTargetEnrollment"), typeof(DateTime)], enrollment, observedAt)!;

    private static bool Succeed(object operation, DateTime settledAt) =>
        (bool)Invoke(operation, "Succeed", typeof(bool), [typeof(DateTime)], settledAt)!;

    private static bool Fail(object operation, object outcome, DateTime settledAt) =>
        (bool)Invoke(operation, "Fail", typeof(bool),
            [RequireType("SetupSecretBindingOperationOutcome"), typeof(DateTime)],
            outcome, settledAt)!;

    private static bool Cancel(object operation, DateTime settledAt) =>
        (bool)Invoke(operation, "Cancel", typeof(bool), [typeof(DateTime)], settledAt)!;

    private static bool Settle(object operation, string terminal, Type outcomeType,
        DateTime settledAt) => terminal switch
        {
            "Succeeded" => Succeed(operation, settledAt),
            "Failed" => Fail(operation, Enum.Parse(outcomeType, "Unavailable"), settledAt),
            "Cancelled" => Cancel(operation, settledAt),
            _ => throw new ArgumentOutOfRangeException(nameof(terminal))
        };

    private static async Task AssertDispatchRejectedUnchanged(object operation,
        object enrollment, DateTime observedAt)
    {
        object?[] operationBefore = Snapshot(operation);
        object?[] enrollmentBefore = Snapshot(enrollment);
        await Assert.That(CanDispatch(operation, enrollment, observedAt)).IsFalse();
        AssertSnapshot(operation, operationBefore);
        AssertSnapshot(enrollment, enrollmentBefore);
    }

    private static void ExpectFactoryArgumentException(Type type, string method,
        object?[] validArguments, int changedIndex, object? invalidValue)
    {
        object?[] changed = validArguments.ToArray();
        changed[changedIndex] = invalidValue;
        Type[] parameterTypes = method == "CreateAccepted"
            ? [typeof(Guid), typeof(Guid), typeof(Guid), typeof(Guid), typeof(long),
                typeof(Guid), typeof(string), typeof(string), typeof(int), typeof(string),
                typeof(DateTime)]
            : type.Name == "SetupTargetEnrollment"
                ? [typeof(Guid), typeof(Guid), typeof(Guid), typeof(string), typeof(string),
                    typeof(string), typeof(DateTime), typeof(DateTime)]
                : [typeof(Guid), typeof(Guid), typeof(Guid), typeof(Guid), typeof(Guid),
                    typeof(long), typeof(string), typeof(DateTime)];
        ExpectException(() => InvokeStatic(type, method, type, parameterTypes, changed),
            typeof(ArgumentException));
    }

    private static void ExpectRejectedUnchanged(object owner, Action action,
        Type exceptionType)
    {
        object?[] before = Snapshot(owner);
        ExpectException(action, exceptionType);
        AssertSnapshot(owner, before);
    }

    private static void ExpectRejectedPairUnchanged(object first, object second,
        Action action, Type exceptionType)
    {
        object?[] firstBefore = Snapshot(first);
        object?[] secondBefore = Snapshot(second);
        ExpectException(action, exceptionType);
        AssertSnapshot(first, firstBefore);
        AssertSnapshot(second, secondBefore);
    }

    private static void ExpectException(Action action, Type exceptionType)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            RequireContract(exceptionType.IsInstanceOfType(exception),
                $"unexpected-setup-live-domain-exception:{exception.GetType().FullName}");
            AssertValueFreeException(exception);
            return;
        }

        throw new InvalidOperationException(
            $"missing-setup-live-domain-rejection:{exceptionType.FullName}");
    }

    private static object?[] Snapshot(object owner) => owner.GetType()
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .OrderBy(property => property.Name, StringComparer.Ordinal)
        .Select(property => property.GetValue(owner)).ToArray();

    private static Dictionary<string, object?> NamedSnapshot(object owner) =>
        owner.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(
                property => property.Name,
                property => property.GetValue(owner),
                StringComparer.Ordinal);

    private static void AssertSnapshot(object owner, object?[] expected)
    {
        object?[] actual = Snapshot(owner);
        RequireContract(actual.SequenceEqual(expected),
            $"setup-live-domain-invalid-mutation:{owner.GetType().FullName}");
    }

    private static void AssertNamedSnapshot(
        object owner,
        Dictionary<string, object?> expected)
    {
        Dictionary<string, object?> actual = NamedSnapshot(owner);
        RequireContract(
            actual.Count == expected.Count
            && actual.All(pair => expected.TryGetValue(pair.Key, out object? value)
                && Equals(pair.Value, value)),
            $"setup-live-domain-invalid-terminal-result:{owner.GetType().FullName}");
    }

    private static void AssertValueFreeException(Exception exception)
    {
        List<string> diagnostics = [];
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
                $"value-bearing-setup-live-domain-exception:{exception.GetType().FullName}");
        }
    }

    private static IEnumerable<string> DiagnosticCanaries()
    {
        foreach (int value in Enumerable.Range(0, 100))
        {
            yield return Id(value).ToString();
            yield return V4(value).ToString();
        }
        foreach (char value in "abcdef")
            yield return Digest(value);
        foreach (string? value in InvalidHashes())
        {
            if (!string.IsNullOrEmpty(value))
                yield return value;
        }
        yield return "setup.signing";
        yield return "setup.encryption";
        yield return "unknown.binding";
    }

    private static void AssertUtc(object owner, params string[] names)
    {
        foreach (string name in names)
        {
            object value = Read<object>(owner, name);
            DateTime dateTime = value is DateTime direct
                ? direct
                : ((DateTime?)value)!.Value;
            RequireContract(dateTime.Kind == DateTimeKind.Utc,
                $"non-utc-setup-live-domain-value:{owner.GetType().FullName}.{name}");
        }
    }

    private static void Set(object owner, string name, object value)
    {
        PropertyInfo property = RequiredProperty(owner.GetType(), name);
        MethodInfo? setter = property.GetSetMethod(nonPublic: true);
        if (setter is null)
            throw new InvalidOperationException(
                $"missing-setup-live-domain-rehydration-seam:{owner.GetType().FullName}.{name}");
        setter.Invoke(owner, [value]);
    }

    private static void AssertExactProperties(Type type,
        params PropertyContract[] expected)
    {
        PropertyInfo[] actual = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
        string[] actualNames = actual.Select(property => property.Name).ToArray();
        string[] expectedNames = expected.Select(contract => contract.Name)
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        RequireContract(actualNames.SequenceEqual(expectedNames),
            $"invalid-setup-live-domain-properties:{type.FullName}");

        foreach (PropertyContract contract in expected)
        {
            PropertyInfo property = RequiredProperty(type, contract.Name);
            RequireContract(property.PropertyType == contract.Type,
                $"invalid-setup-live-domain-property-type:{type.FullName}.{contract.Name}");
            if (property.PropertyType.IsClass)
            {
                NullabilityState nullability = new NullabilityInfoContext()
                    .Create(property).ReadState;
                RequireContract(nullability == NullabilityState.NotNull,
                    $"invalid-setup-live-domain-property-nullability:"
                    + $"{type.FullName}.{contract.Name}");
            }
            RequireContract(property.GetMethod?.IsPublic == true,
                $"missing-setup-live-domain-property-getter:{type.FullName}.{contract.Name}");
            RequireContract((property.GetSetMethod(nonPublic: true)?.IsPublic ?? false)
                == contract.PublicSetter,
                $"invalid-setup-live-domain-property-setter:{type.FullName}.{contract.Name}");
            RequireContract(contract.PublicSetter || property.SetMethod is null
                || !property.SetMethod.IsPublic,
                $"invalid-setup-live-domain-property-mutability:{type.FullName}.{contract.Name}");
        }
    }

    private static void AssertExactMethods(Type type, params MethodContract[] expected)
    {
        MethodInfo[] declared = type.GetMethods(BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName).ToArray();
        RequireContract(declared.Length == expected.Length,
            $"invalid-setup-live-domain-method-count:{type.FullName}");
        foreach (MethodContract contract in expected)
            _ = RequiredMethod(type, contract.Name, contract.IsStatic,
                contract.ReturnType, contract.ParameterTypes);
    }

    private static void AssertEnum(Type type, params string[] expected)
    {
        RequireContract(type.IsEnum, $"invalid-setup-live-domain-enum:{type.FullName}");
        RequireContract(Enum.GetNames(type).SequenceEqual(expected),
            $"invalid-setup-live-domain-enum-values:{type.FullName}");
        RequireContract(Enum.GetUnderlyingType(type) == typeof(int),
            $"invalid-setup-live-domain-enum-underlying-type:{type.FullName}");
    }

    private static IEnumerable<string> PublicShapeNames(Type type) =>
        type.GetMembers(BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .Concat(type.GetProperties(BindingFlags.Public | BindingFlags.Instance
                    | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(property => property.PropertyType.Name))
            .Concat(type.GetMethods(BindingFlags.Public | BindingFlags.Instance
                    | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetParameters())
                .Select(parameter => parameter.ParameterType.Name));

    private static PropertyContract Property(string name, Type type,
        bool publicSetter = false) => new(name, type, publicSetter);

    private static MethodContract StaticMethod(string name, Type returnType,
        params Type[] parameters) => new(name, true, returnType, parameters);

    private static MethodContract InstanceMethod(string name, Type returnType,
        params Type[] parameters) => new(name, false, returnType, parameters);

    private static Type RequireType(string name) =>
        DomainAssembly.GetType($"{Namespace}.{name}", throwOnError: false,
            ignoreCase: false)
        ?? throw new InvalidOperationException(
            $"missing-setup-live-domain-owner:{name}");

    private static PropertyInfo RequiredProperty(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException(
            $"missing-setup-live-domain-property:{type.FullName}.{name}");

    private static MethodInfo RequiredMethod(Type type, string name, bool isStatic,
        Type returnType, params Type[] parameterTypes)
    {
        MethodInfo? method = type.GetMethod(name,
            BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance),
            binder: null, types: parameterTypes, modifiers: null);
        if (method is null)
        {
            string signature = string.Join(',', parameterTypes.Select(parameter =>
                parameter.FullName));
            throw new InvalidOperationException(
                $"missing-setup-live-domain-method:{type.FullName}.{name}({signature})");
        }
        if (method.ReturnType != returnType)
            throw new InvalidOperationException(
                $"invalid-setup-live-domain-method-return:{type.FullName}.{name}:"
                + $"{method.ReturnType.FullName}");
        return method;
    }

    private static T Read<T>(object instance, string propertyName)
    {
        object? value = RequiredProperty(instance.GetType(), propertyName)
            .GetValue(instance);
        if (value is null && default(T) is not null)
            throw new InvalidOperationException(
                $"missing-setup-live-domain-value:{instance.GetType().FullName}.{propertyName}");
        return (T)value!;
    }

    private static object? InvokeStatic(Type type, string methodName, Type returnType,
        Type[] parameterTypes, params object?[] arguments) => InvokeCore(type,
        instance: null, methodName, returnType, parameterTypes, arguments);

    private static object? Invoke(object instance, string methodName, Type returnType,
        Type[] parameterTypes, params object?[] arguments) => InvokeCore(
        instance.GetType(), instance, methodName, returnType, parameterTypes, arguments);

    private static object? InvokeCore(Type type, object? instance, string methodName,
        Type returnType, Type[] parameterTypes, object?[] arguments)
    {
        MethodInfo method = RequiredMethod(type, methodName, instance is null,
            returnType, parameterTypes);
        try
        {
            return method.Invoke(instance, arguments);
        }
        catch (TargetInvocationException exception)
            when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static void RequireContract(bool condition, string diagnostic)
    {
        if (!condition)
            throw new InvalidOperationException(diagnostic);
    }

    private static Guid Id(int value) => Guid.Parse(
        $"01991f00-0000-7000-8000-{value:D12}");

    private static Guid V4(int value) => Guid.Parse(
        $"10000000-0000-4000-8000-{value:D12}");

    private static string Digest(char value) => new(value, 64);

    private sealed record PropertyContract(
        string Name, Type Type, bool PublicSetter);

    private sealed record MethodContract(
        string Name, bool IsStatic, Type ReturnType, Type[] ParameterTypes);
}
