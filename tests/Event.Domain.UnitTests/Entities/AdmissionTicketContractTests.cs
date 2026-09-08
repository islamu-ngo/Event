using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Event.Domain.UnitTests.Entities;

public sealed class AdmissionTicketContractTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ConfirmedOrderAndConcreteAssignmentAreTheCurrentAdmissionIssuanceAuthority()
    {
        AuthorityFixture fixture = CreateConfirmedAuthorityFixture();

        bool assignmentsCanConfirm = RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.PerTicketRequired,
            ticketUnitCount: 1,
            [fixture.Assignment],
            assignmentDeadline: null,
            UtcNow);

        await Assert.That(fixture.Order.RegistrationOrderStatusId)
            .IsEqualTo((int)RegistrationOrderStatusEnum.Confirmed);
        await Assert.That(fixture.Order.ConfirmedAt).IsEqualTo(UtcNow);
        await Assert.That(assignmentsCanConfirm).IsTrue();
        await Assert.That(fixture.Assignment.RegistrationOrderId).IsEqualTo(fixture.Order.Id);
        await Assert.That(fixture.Assignment.RegistrationOrderLineId).IsEqualTo(fixture.OrderLineId);
        await Assert.That(fixture.Assignment.AssignmentStatusId).IsEqualTo((int)AssignmentStatusEnum.Assigned);
        await Assert.That(fixture.Assignment.ParticipantId).IsNotNull();
    }

    [Test]
    public async Task IssueCreatesOneTicketWithOneLiveCredentialChild()
    {
        AdmissionContract contract = await AdmissionContract.Load();
        AuthorityFixture fixture = CreateConfirmedAuthorityFixture();
        IssueIdentity identity = IssueIdentity.Create(1);

        await Assert.That(() => contract.Issue(fixture, identity with { LookupDigest = string.Empty }))
            .Throws<ArgumentException>();
        await Assert.That(() => contract.Issue(fixture, identity with { LookupDigest = "not-canonical-base64" }))
            .Throws<ArgumentException>();
        await Assert.That(() => contract.Issue(
                fixture,
                identity with { LookupDigest = Convert.ToBase64String(new byte[31]) }))
            .Throws<ArgumentException>();

        object ticket = contract.Issue(fixture, identity);
        object credential = contract.Credentials(ticket).Single();

        await Assert.That(contract.TicketId(ticket)).IsEqualTo(identity.TicketId);
        await Assert.That(contract.TenantId(ticket)).IsEqualTo(fixture.Order.TenantId);
        await Assert.That(contract.EventId(ticket)).IsEqualTo(fixture.Order.EventId);
        await Assert.That(contract.RegistrationOrderId(ticket)).IsEqualTo(fixture.Order.Id);
        await Assert.That(contract.RegistrationOrderLineId(ticket)).IsEqualTo(fixture.OrderLine.Id);
        await Assert.That(contract.RegistrationTicketAssignmentId(ticket)).IsEqualTo(fixture.Assignment.Id);
        await Assert.That(contract.ParticipantId(ticket)).IsEqualTo(fixture.Participant.Id);
        await Assert.That(contract.TicketCatalogVersionId(ticket)).IsEqualTo(fixture.Catalog.Id);
        await Assert.That(contract.EventTicketTypeId(ticket)).IsEqualTo(fixture.TicketType.Id);
        await Assert.That(contract.DisplayReference(ticket)).IsEqualTo(identity.DisplayReference);
        await Assert.That(contract.CredentialId(credential)).IsEqualTo(identity.CredentialId);
        await Assert.That(contract.CredentialVersion(credential)).IsEqualTo(1);
        await Assert.That(contract.LookupKeyVersion(credential)).IsEqualTo(1);
        await Assert.That(contract.LookupDigest(credential)).IsEqualTo(identity.LookupDigest);
        await Assert.That(contract.CredentialStatus(credential)).IsEqualTo("Active");
        await Assert.That(contract.LiveCredentialCount(ticket)).IsEqualTo(1);
        await Assert.That(contract.Validates(ticket, 1, 1, identity.LookupDigest)).IsTrue();
        await Assert.That(ForbiddenAdmissionState(contract.TicketType, contract.CredentialType)).IsEmpty();
    }

    [Test]
    public async Task RotateRetainsRevokedHistoryAndOnlyNewerActiveCredentialValidates()
    {
        AdmissionContract contract = await AdmissionContract.Load();
        AuthorityFixture fixture = CreateConfirmedAuthorityFixture();
        IssueIdentity initial = IssueIdentity.Create(1);
        object ticket = contract.Issue(fixture, initial);
        object oldCredential = contract.Credentials(ticket).Single();
        Guid nextCredentialId = Guid.CreateVersion7();
        string nextDigest = Digest(2);

        contract.RotateCredential(
            ticket,
            nextCredentialId,
            credentialVersion: 2,
            lookupKeyVersion: 2,
            nextDigest,
            UtcNow.AddMinutes(1));

        object[] history = contract.Credentials(ticket);
        object currentCredential = history.Single(credential => contract.CredentialStatus(credential) == "Active");

        await Assert.That(history.Length).IsEqualTo(2);
        await Assert.That(contract.LiveCredentialCount(ticket)).IsEqualTo(1);
        await Assert.That(contract.CredentialStatus(oldCredential)).IsEqualTo("Revoked");
        await Assert.That(contract.LookupDigest(oldCredential)).IsEqualTo(initial.LookupDigest);
        await Assert.That(contract.CredentialId(currentCredential)).IsEqualTo(nextCredentialId);
        await Assert.That(contract.CredentialVersion(currentCredential)).IsEqualTo(2);
        await Assert.That(contract.LookupKeyVersion(currentCredential)).IsEqualTo(2);
        await Assert.That(contract.LookupDigest(currentCredential)).IsEqualTo(nextDigest);
        await Assert.That(contract.Validates(ticket, 1, 1, initial.LookupDigest)).IsFalse();
        await Assert.That(contract.Validates(ticket, 2, 2, nextDigest)).IsTrue();
    }

    [Test]
    public async Task TerminalTicketStatesCannotReactivateOrValidateCurrentCredential()
    {
        AdmissionContract contract = await AdmissionContract.Load();

        await Assert.That(contract.TicketStatusValues()).IsEquivalentTo(new Dictionary<string, int>
        {
            ["Active"] = 1,
            ["Suspended"] = 2,
            ["Revoked"] = 3,
            ["Cancelled"] = 4,
            ["Transferred"] = 5,
            ["Expired"] = 6
        });

        string[] terminalStatuses = ["Revoked", "Cancelled", "Transferred", "Expired"];
        foreach (string terminalStatus in terminalStatuses)
        {
            IssueIdentity identity = IssueIdentity.Create(1);
            object ticket = contract.Issue(CreateConfirmedAuthorityFixture(), identity);

            contract.TransitionTo(ticket, terminalStatus, UtcNow.AddMinutes(1));

            await Assert.That(contract.TicketStatus(ticket)).IsEqualTo(terminalStatus);
            await Assert.That(contract.LiveCredentialCount(ticket)).IsEqualTo(0);
            await Assert.That(contract.Credentials(ticket).All(
                credential => contract.CredentialStatus(credential) == "Revoked")).IsTrue();
            await Assert.That(contract.Validates(ticket, 1, 1, identity.LookupDigest)).IsFalse();
            await Assert.That(() => contract.TransitionTo(ticket, "Active", UtcNow.AddMinutes(2)))
                .Throws<InvalidOperationException>();
            await Assert.That(contract.TicketStatus(ticket)).IsEqualTo(terminalStatus);
        }
    }

    [Test]
    public async Task CancellationExplicitlyRevokesTheCurrentCredential()
    {
        AdmissionContract contract = await AdmissionContract.Load();
        IssueIdentity identity = IssueIdentity.Create(1);
        object ticket = contract.Issue(CreateConfirmedAuthorityFixture(), identity);

        contract.Cancel(ticket, UtcNow.AddMinutes(1));

        await Assert.That(contract.TicketStatus(ticket)).IsEqualTo("Cancelled");
        await Assert.That(contract.LiveCredentialCount(ticket)).IsEqualTo(0);
        await Assert.That(contract.CredentialStatus(contract.Credentials(ticket).Single())).IsEqualTo("Revoked");
        await Assert.That(contract.Validates(ticket, 1, 1, identity.LookupDigest)).IsFalse();
    }

    [Test]
    public async Task RefundAllocationsRejectNegativeAndOverAllocatedLineFactsWithoutMutation()
    {
        AdmissionContract contract = await AdmissionContract.Load();
        (long AcceptedMinor, long RefundedMinor)[] malformedAllocations =
        [
            (1_000L, -1L),
            (1_000L, 1_001L)
        ];

        foreach ((long acceptedMinor, long refundedMinor) in malformedAllocations)
        {
            AuthorityFixture fixture = CreateConfirmedAuthorityFixture();
            object ticket = contract.Issue(fixture, IssueIdentity.Create(1));

            await Assert.That(() => contract.ApplyRefundAllocations(
                    ticket,
                    [new RefundFact(
                        fixture.Assignment.Id,
                        fixture.OrderLineId,
                        true,
                        acceptedMinor,
                        refundedMinor)],
                    UtcNow.AddMinutes(1)))
                .Throws<ArgumentOutOfRangeException>();
            await Assert.That(contract.TicketStatus(ticket)).IsEqualTo("Active");
            await Assert.That(contract.LiveCredentialCount(ticket)).IsEqualTo(1);
        }
    }

    [Test]
    public async Task ZeroOverZeroAndPartialRelevantRefundsDoNotRevokeAdmission()
    {
        AdmissionContract contract = await AdmissionContract.Load();
        AuthorityFixture zeroFixture = CreateConfirmedAuthorityFixture();
        object zeroTicket = contract.Issue(zeroFixture, IssueIdentity.Create(1));
        AuthorityFixture partialFixture = CreateConfirmedAuthorityFixture();
        object partialTicket = contract.Issue(partialFixture, IssueIdentity.Create(1));

        contract.ApplyRefundAllocations(
            zeroTicket,
            [new RefundFact(zeroFixture.Assignment.Id, zeroFixture.OrderLineId, true, 0, 0)],
            UtcNow.AddMinutes(1));
        contract.ApplyRefundAllocations(
            partialTicket,
            [new RefundFact(partialFixture.Assignment.Id, partialFixture.OrderLineId, true, 1_000, 999)],
            UtcNow.AddMinutes(1));

        await Assert.That(contract.TicketStatus(zeroTicket)).IsEqualTo("Active");
        await Assert.That(contract.LiveCredentialCount(zeroTicket)).IsEqualTo(1);
        await Assert.That(contract.TicketStatus(partialTicket)).IsEqualTo("Active");
        await Assert.That(contract.LiveCredentialCount(partialTicket)).IsEqualTo(1);
    }

    [Test]
    public async Task RefundRevocationUsesMatchingAssignmentAcrossMultipleTicketLinesAndAddOns()
    {
        AdmissionContract contract = await AdmissionContract.Load();
        Guid otherAssignmentId = Guid.CreateVersion7();
        Guid otherTicketLineId = Guid.CreateVersion7();
        Guid addOnLineId = Guid.CreateVersion7();
        AuthorityFixture partialFixture = CreateConfirmedAuthorityFixture();
        IssueIdentity partialIdentity = IssueIdentity.Create(1);
        object partialTicket = contract.Issue(partialFixture, partialIdentity);
        AuthorityFixture fullFixture = CreateConfirmedAuthorityFixture();
        IssueIdentity fullIdentity = IssueIdentity.Create(1);
        object fullTicket = contract.Issue(fullFixture, fullIdentity);

        contract.ApplyRefundAllocations(
            partialTicket,
            [
                new RefundFact(partialFixture.Assignment.Id, partialFixture.OrderLineId, true, 1_000, 500),
                new RefundFact(otherAssignmentId, otherTicketLineId, true, 2_000, 2_000),
                new RefundFact(null, addOnLineId, false, 500, 500)
            ],
            UtcNow.AddMinutes(1));
        contract.ApplyRefundAllocations(
            fullTicket,
            [
                new RefundFact(fullFixture.Assignment.Id, fullFixture.OrderLineId, true, 1_000, 1_000),
                new RefundFact(otherAssignmentId, otherTicketLineId, true, 2_000, 500),
                new RefundFact(null, addOnLineId, false, 500, 500)
            ],
            UtcNow.AddMinutes(1));

        await Assert.That(contract.TicketStatus(partialTicket)).IsEqualTo("Active");
        await Assert.That(contract.Validates(partialTicket, 1, 1, partialIdentity.LookupDigest)).IsTrue();
        await Assert.That(contract.TicketStatus(fullTicket)).IsEqualTo("Revoked");
        await Assert.That(contract.LiveCredentialCount(fullTicket)).IsEqualTo(0);
        await Assert.That(contract.Validates(fullTicket, 1, 1, fullIdentity.LookupDigest)).IsFalse();
    }

    private static AuthorityFixture CreateConfirmedAuthorityFixture()
    {
        AdmissionTicketTestAuthority authority = AdmissionTicketTestAuthority.Create(UtcNow);
        return new(
            authority.Order,
            authority.OrderLine,
            authority.Assignment,
            authority.Participant,
            authority.Catalog,
            authority.TicketType);
    }

    private static string Digest(byte marker)
    {
        byte[] digest = new byte[32];
        digest[0] = marker;
        return Convert.ToBase64String(digest);
    }

    private static string[] ForbiddenAdmissionState(Type ticketType, Type credentialType)
    {
        string[] forbiddenFragments = ["PaymentAttempt", "PaymentStatus", "RefundAttempt", "Provider", "Stripe"];
        return ticketType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Concat(credentialType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Where(property => forbiddenFragments.Any(fragment =>
                property.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase) ||
                property.PropertyType.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .Select(property => $"{property.DeclaringType?.Name}.{property.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private sealed record AuthorityFixture(
        RegistrationOrder Order,
        RegistrationOrderLine OrderLine,
        RegistrationTicketAssignment Assignment,
        RegistrationParticipant Participant,
        EventTicketCatalogVersion Catalog,
        EventTicketType TicketType)
    {
        public Guid OrderLineId => OrderLine.Id;
    }

    private sealed record RefundFact(
        Guid? RegistrationTicketAssignmentId,
        Guid RegistrationOrderLineId,
        bool IsAdmissionRelevant,
        long AcceptedAmountMinor,
        long RefundedAmountMinor);

    private sealed record IssueIdentity(
        Guid TicketId,
        Guid CredentialId,
        string DisplayReference,
        string LookupDigest)
    {
        public static IssueIdentity Create(byte digestMarker) => new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            $"TKT-{Guid.CreateVersion7():N}",
            Digest(digestMarker));
    }

    private sealed class AdmissionContract
    {
        private readonly Type _ticketStatusType;
        private readonly Type _credentialStatusType;
        private readonly Type _refundAllocationType;
        private readonly MethodInfo _issue;
        private readonly MethodInfo _rotateCredential;
        private readonly MethodInfo _validatesCredential;
        private readonly MethodInfo _transitionTo;
        private readonly MethodInfo _cancel;
        private readonly MethodInfo _createRefundAllocation;
        private readonly MethodInfo _applyRefundAllocations;
        private readonly PropertyInfo _credentials;

        private AdmissionContract(
            Type ticketType,
            Type credentialType,
            Type ticketStatusType,
            Type credentialStatusType,
            Type refundAllocationType)
        {
            TicketType = ticketType;
            CredentialType = credentialType;
            _ticketStatusType = ticketStatusType;
            _credentialStatusType = credentialStatusType;
            _refundAllocationType = refundAllocationType;
            Type credentialCollectionType = typeof(IReadOnlyCollection<>).MakeGenericType(credentialType);
            Type refundCollectionType = typeof(IReadOnlyCollection<>).MakeGenericType(refundAllocationType);

            _issue = ExactMethod(
                ticketType,
                "Issue",
                isStatic: true,
                ticketType,
                typeof(RegistrationOrder),
                typeof(RegistrationOrderLine),
                typeof(RegistrationTicketAssignment),
                typeof(RegistrationParticipant),
                typeof(EventTicketCatalogVersion),
                typeof(EventTicketType),
                typeof(Guid),
                typeof(string),
                typeof(Guid),
                typeof(int),
                typeof(int),
                typeof(string),
                typeof(DateTime));
            _rotateCredential = ExactMethod(
                ticketType,
                "RotateCredential",
                isStatic: false,
                typeof(void),
                typeof(Guid),
                typeof(int),
                typeof(int),
                typeof(string),
                typeof(DateTime));
            _validatesCredential = ExactMethod(
                ticketType,
                "ValidatesCredential",
                isStatic: false,
                typeof(bool),
                typeof(int),
                typeof(int),
                typeof(string));
            _transitionTo = ExactMethod(
                ticketType,
                "TransitionTo",
                isStatic: false,
                typeof(void),
                ticketStatusType,
                typeof(DateTime));
            _cancel = ExactMethod(
                ticketType,
                "Cancel",
                isStatic: false,
                typeof(void),
                typeof(DateTime));
            _createRefundAllocation = ExactMethod(
                refundAllocationType,
                "Create",
                isStatic: true,
                refundAllocationType,
                typeof(Guid?),
                typeof(Guid),
                typeof(bool),
                typeof(long),
                typeof(long));
            _applyRefundAllocations = ExactMethod(
                ticketType,
                "ApplyRefundAllocations",
                isStatic: false,
                typeof(void),
                refundCollectionType,
                typeof(DateTime));
            _credentials = ExactProperty(ticketType, "Credentials", credentialCollectionType);
        }

        public Type TicketType { get; }

        public Type CredentialType { get; }

        public static async Task<AdmissionContract> Load()
        {
            Type? ticketType = PublicDomainType("AdmissionTicket");
            Type? credentialType = PublicDomainType("AdmissionTicketCredential");
            Type? ticketStatusType = PublicDomainType("AdmissionTicketStatusEnum");
            Type? credentialStatusType = PublicDomainType("AdmissionTicketCredentialStatusEnum");
            Type? refundAllocationType = PublicDomainType("AdmissionRefundLineAllocation");

            await Assert.That(ticketType).IsNotNull();
            await Assert.That(credentialType).IsNotNull();
            await Assert.That(ticketStatusType).IsNotNull();
            await Assert.That(credentialStatusType).IsNotNull();
            await Assert.That(refundAllocationType).IsNotNull();
            return new(
                ticketType!,
                credentialType!,
                ticketStatusType!,
                credentialStatusType!,
                refundAllocationType!);
        }

        public object Issue(AuthorityFixture fixture, IssueIdentity identity) => InvokeRequired(
            _issue,
            target: null,
            fixture.Order,
            fixture.OrderLine,
            fixture.Assignment,
            fixture.Participant,
            fixture.Catalog,
            fixture.TicketType,
            identity.TicketId,
            identity.DisplayReference,
            identity.CredentialId,
            1,
            1,
            identity.LookupDigest,
            UtcNow);

        public void RotateCredential(
            object ticket,
            Guid credentialId,
            int credentialVersion,
            int lookupKeyVersion,
            string lookupDigest,
            DateTime rotatedAtUtc) => Invoke(
            _rotateCredential,
            ticket,
            credentialId,
            credentialVersion,
            lookupKeyVersion,
            lookupDigest,
            rotatedAtUtc);

        public bool Validates(
            object ticket,
            int credentialVersion,
            int lookupKeyVersion,
            string lookupDigest) => (bool)InvokeRequired(
            _validatesCredential,
            ticket,
            credentialVersion,
            lookupKeyVersion,
            lookupDigest);

        public void TransitionTo(object ticket, string status, DateTime occurredAtUtc) => Invoke(
            _transitionTo,
            ticket,
            Enum.Parse(_ticketStatusType, status),
            occurredAtUtc);

        public void Cancel(object ticket, DateTime cancelledAtUtc) => Invoke(
            _cancel,
            ticket,
            cancelledAtUtc);

        public void ApplyRefundAllocations(
            object ticket,
            IReadOnlyCollection<RefundFact> facts,
            DateTime appliedAtUtc)
        {
            Array allocations = Array.CreateInstance(_refundAllocationType, facts.Count);
            int index = 0;
            foreach (RefundFact fact in facts)
            {
                allocations.SetValue(InvokeRequired(
                    _createRefundAllocation,
                    target: null,
                    fact.RegistrationTicketAssignmentId,
                    fact.RegistrationOrderLineId,
                    fact.IsAdmissionRelevant,
                    fact.AcceptedAmountMinor,
                    fact.RefundedAmountMinor), index++);
            }

            Invoke(_applyRefundAllocations, ticket, allocations, appliedAtUtc);
        }

        public Guid TicketId(object ticket) => GuidProperty(ticket, "Id");

        public Guid TenantId(object ticket) => GuidProperty(ticket, "TenantId");

        public Guid EventId(object ticket) => GuidProperty(ticket, "EventId");

        public Guid RegistrationOrderId(object ticket) => GuidProperty(ticket, "RegistrationOrderId");

        public Guid RegistrationOrderLineId(object ticket) => GuidProperty(ticket, "RegistrationOrderLineId");

        public Guid RegistrationTicketAssignmentId(object ticket) => GuidProperty(
            ticket,
            "RegistrationTicketAssignmentId");

        public Guid ParticipantId(object ticket) => GuidProperty(ticket, "ParticipantId");

        public Guid TicketCatalogVersionId(object ticket) => GuidProperty(ticket, "TicketCatalogVersionId");

        public Guid EventTicketTypeId(object ticket) => GuidProperty(ticket, "EventTicketTypeId");

        public string DisplayReference(object ticket) => (string)(ExactProperty(
            TicketType,
            "DisplayReference",
            typeof(string)).GetValue(ticket)
            ?? throw new InvalidOperationException("AdmissionTicket.DisplayReference is null."));

        public object[] Credentials(object ticket) => ((IEnumerable)(_credentials.GetValue(ticket)
                ?? throw new InvalidOperationException("AdmissionTicket.Credentials is null.")))
            .Cast<object>()
            .ToArray();

        public Guid CredentialId(object credential) => GuidProperty(credential, "Id");

        public int CredentialVersion(object credential) => IntProperty(credential, "CredentialVersion");

        public int LookupKeyVersion(object credential) => IntProperty(credential, "LookupKeyVersion");

        public string LookupDigest(object credential) => (string)(ExactProperty(
            CredentialType,
            "LookupDigest",
            typeof(string)).GetValue(credential)
            ?? throw new InvalidOperationException("AdmissionTicketCredential.LookupDigest is null."));

        public string TicketStatus(object ticket) => Enum.GetName(
            _ticketStatusType,
            IntProperty(ticket, "AdmissionTicketStatusId"))!;

        public string CredentialStatus(object credential) => Enum.GetName(
            _credentialStatusType,
            IntProperty(credential, "AdmissionTicketCredentialStatusId"))!;

        public int LiveCredentialCount(object ticket) => Credentials(ticket)
            .Count(credential => CredentialStatus(credential) == "Active");

        public Dictionary<string, int> TicketStatusValues() => Enum.GetNames(_ticketStatusType)
            .ToDictionary(
                name => name,
                name => Convert.ToInt32(Enum.Parse(_ticketStatusType, name), CultureInfo.InvariantCulture),
                StringComparer.Ordinal);

        private static Guid GuidProperty(object instance, string name) => (Guid)(ExactProperty(
            instance.GetType(),
            name,
            typeof(Guid)).GetValue(instance)
            ?? throw new InvalidOperationException($"{instance.GetType().Name}.{name} is null."));

        private static int IntProperty(object instance, string name) => Convert.ToInt32(
            ExactProperty(instance.GetType(), name, typeof(int)).GetValue(instance),
            CultureInfo.InvariantCulture);

        private static Type? PublicDomainType(string name) => typeof(RegistrationOrder).Assembly
            .GetTypes()
            .SingleOrDefault(type => type.IsPublic && type.Name == name);

        private static PropertyInfo ExactProperty(Type type, string name, Type propertyType)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property is null || property.PropertyType != propertyType)
            {
                throw new InvalidOperationException(
                    $"Required exact property '{type.Name}.{name}: {propertyType.Name}' is missing.");
            }

            return property;
        }

        private static MethodInfo ExactMethod(
            Type type,
            string name,
            bool isStatic,
            Type returnType,
            params Type[] parameterTypes)
        {
            MethodInfo[] namedMethods = type
                .GetMethods(BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance))
                .Where(method => method.Name == name)
                .ToArray();
            if (namedMethods.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Public behavior '{type.Name}.{name}' must have exactly one non-ambiguous overload.");
            }

            MethodInfo method = namedMethods[0];
            Type[] actualParameterTypes = method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .ToArray();
            if (method.ReturnType != returnType || !actualParameterTypes.SequenceEqual(parameterTypes))
            {
                throw new InvalidOperationException(
                    $"Public behavior '{type.Name}.{name}' does not match the exact Domain signature.");
            }

            return method;
        }

        private static object InvokeRequired(
            MethodInfo method,
            object? target,
            params object?[] arguments) => Invoke(method, target, arguments)
            ?? throw new InvalidOperationException($"Public behavior '{method.Name}' returned no aggregate result.");

        private static object? Invoke(MethodInfo method, object? target, params object?[] arguments)
        {
            try
            {
                return method.Invoke(target, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
    }
}
