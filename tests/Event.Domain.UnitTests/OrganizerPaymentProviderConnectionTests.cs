using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests;

public sealed class OrganizerPaymentProviderConnectionTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid OrganizerActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000111");
    private static readonly DateTime Now = new(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Create_NormalizesStableIdentityAndStartsPendingOnboarding()
    {
        OrganizerPaymentProviderConnection connection = Connection(" acct_123 ");

        await Assert.That(connection.TenantId).IsEqualTo(TenantId);
        await Assert.That(connection.OrganizerActorId).IsEqualTo(OrganizerActorId);
        await Assert.That(connection.ProviderCode).IsEqualTo("stripe");
        await Assert.That(connection.ConnectPlatformId).IsEqualTo("platform-live-eu");
        await Assert.That(connection.ExternalAccountId).IsEqualTo("acct_123");
        await Assert.That(connection.ActiveScopeKey).IsEqualTo($"{TenantId:N}|{OrganizerActorId:N}|stripe|platform-live-eu");
        await Assert.That(connection.ActiveUniquenessSlot).IsEqualTo("active");
        await Assert.That(connection.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding);
        await Assert.That(connection.ReplacesConnectionId).IsNull();
    }

    [Test]
    public async Task Create_RejectsUnknownProviderAndInvalidIdentityBounds()
    {
        await Assert.That(() => OrganizerPaymentProviderConnection.Create(Guid.Empty, TenantId, OrganizerActorId, "stripe", "platform", "acct", Now))
            .Throws<ArgumentException>();
        await Assert.That(() => OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), TenantId, OrganizerActorId, "paypal", "platform", "acct", Now))
            .Throws<ArgumentException>();
        await Assert.That(() => OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), TenantId, OrganizerActorId, "stripe", new string('p', 121), "acct", Now))
            .Throws<ArgumentException>();
        await Assert.That(() => OrganizerPaymentProviderConnection.Create(Guid.CreateVersion7(), TenantId, OrganizerActorId, "stripe", "platform", new string('a', 201), DateTime.SpecifyKind(Now, DateTimeKind.Local)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ApplyReadiness_StoresBoundedProjectionAndOnlyReadyWhenComplete()
    {
        OrganizerPaymentProviderConnection connection = Connection();

        connection.ApplyReadiness(OrganizerPaymentProviderReadinessObservation.Create(
            "be",
            ChargeCapabilityState.Active,
            ProviderRequirementsState.Satisfied,
            ["eur", "usd", "EUR"],
            Now.AddMinutes(1),
            "rev-001"));

        await Assert.That(connection.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Ready);
        await Assert.That(connection.MerchantCountryCode).IsEqualTo("BE");
        await Assert.That(connection.SupportedCurrencyCodes.SequenceEqual(["EUR", "USD"])).IsTrue();
        await Assert.That(connection.LastReadinessEvidenceRevision).IsEqualTo("rev-001");

        connection.ApplyReadiness(OrganizerPaymentProviderReadinessObservation.Create(
            "BE",
            ChargeCapabilityState.Pending,
            ProviderRequirementsState.Satisfied,
            ["EUR"],
            Now.AddMinutes(2),
            "rev-002"));

        await Assert.That(connection.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Restricted);
    }

    [Test]
    public async Task ApplyReadiness_RejectsStaleOrEqualEvidenceAndRawShapeInputs()
    {
        OrganizerPaymentProviderConnection connection = Connection();
        connection.ApplyReadiness(ReadyObservation(Now.AddMinutes(1), "rev-002"));

        await Assert.That(() => connection.ApplyReadiness(ReadyObservation(Now, "rev-003"))).Throws<InvalidOperationException>();
        await Assert.That(() => connection.ApplyReadiness(ReadyObservation(Now.AddMinutes(1), "rev-002"))).Throws<InvalidOperationException>();
        await Assert.That(() => OrganizerPaymentProviderReadinessObservation.Create("Belgium", ChargeCapabilityState.Active, ProviderRequirementsState.Satisfied, ["EUR"], Now, "rev"))
            .Throws<ArgumentException>();
        await Assert.That(() => OrganizerPaymentProviderReadinessObservation.Create("BE", ChargeCapabilityState.Active, ProviderRequirementsState.Satisfied, ["XXX"], Now, "rev"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ReplaceWith_CreatesFutureConnectionAndKeepsReverseLineageSeparate()
    {
        OrganizerPaymentProviderConnection old = Connection("acct_old");
        Guid previousStamp = old.ConcurrencyStamp;

        OrganizerPaymentProviderConnection replacement = old.ReplaceWith(Guid.Parse("018e4e5c-7f00-7000-8000-000000000222"), "acct_new", Now.AddMinutes(3));

        await Assert.That(old.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.Replaced);
        await Assert.That(old.ActiveUniquenessSlot).IsEqualTo($"replaced:{old.Id:N}");
        await Assert.That(old.ExternalAccountId).IsEqualTo("acct_old");
        await Assert.That(old.ReplacedByConnectionId).IsNull();
        await Assert.That(replacement.ReplacesConnectionId).IsEqualTo(old.Id);
        await Assert.That(replacement.ActiveUniquenessSlot).IsEqualTo("active");
        await Assert.That(replacement.ExternalAccountId).IsEqualTo("acct_new");
        await Assert.That(replacement.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding);

        Guid retiredStamp = old.ConcurrencyStamp;
        old.MarkReplacedBy(replacement.Id);

        await Assert.That(old.ReplacedByConnectionId).IsEqualTo(replacement.Id);
        await Assert.That(old.ConcurrencyStamp).IsNotEqualTo(retiredStamp);
        await Assert.That(old.ConcurrencyStamp).IsNotEqualTo(previousStamp);
    }

    [Test]
    public async Task MarkReplacedBy_ValidatesStateAndIdentityAndIsIdempotent()
    {
        OrganizerPaymentProviderConnection active = Connection("acct_active");
        OrganizerPaymentProviderConnection old = Connection("acct_old");
        OrganizerPaymentProviderConnection replacement = old.ReplaceWith(Guid.Parse("018e4e5c-7f00-7000-8000-000000000222"), "acct_new", Now.AddMinutes(3));

        await Assert.That(() => active.MarkReplacedBy(replacement.Id)).Throws<InvalidOperationException>();
        await Assert.That(() => old.MarkReplacedBy(Guid.Empty)).Throws<ArgumentException>();
        await Assert.That(() => old.MarkReplacedBy(old.Id)).Throws<ArgumentException>();

        old.MarkReplacedBy(replacement.Id);
        Guid linkedStamp = old.ConcurrencyStamp;
        old.MarkReplacedBy(replacement.Id);

        await Assert.That(old.ReplacedByConnectionId).IsEqualTo(replacement.Id);
        await Assert.That(old.ConcurrencyStamp).IsEqualTo(linkedStamp);
        await Assert.That(() => old.MarkReplacedBy(Guid.CreateVersion7())).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReplaceWith_ValidatesNewAccountBeforeMutatingOldConnection()
    {
        OrganizerPaymentProviderConnection old = Connection("acct_old");

        await Assert.That(() => old.ReplaceWith(Guid.CreateVersion7(), "", Now.AddMinutes(3))).Throws<ArgumentException>();

        await Assert.That(old.StatusId).IsEqualTo((int)OrganizerPaymentProviderConnectionStatusEnum.PendingOnboarding);
        await Assert.That(old.ExternalAccountId).IsEqualTo("acct_old");
        await Assert.That(old.ReplacedByConnectionId).IsNull();
    }

    [Test]
    public async Task RecipientSnapshot_IsImmutableReadyOnlyAndCurrencyScoped()
    {
        OrganizerPaymentProviderConnection connection = ReadyConnection();
        Guid instancePolicyVersionId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000301");
        Guid tenantPolicyVersionId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000302");

        OrganizerPaymentRecipientSnapshot snapshot = connection.CreateRecipientSnapshot("eur", instancePolicyVersionId, tenantPolicyVersionId, Now.AddMinutes(4));
        connection.Disable("operator_disabled", Now.AddMinutes(5));

        await Assert.That(connection.ActiveUniquenessSlot).IsEqualTo($"disabled:{connection.Id:N}");
        await Assert.That(snapshot.TenantId).IsEqualTo(connection.TenantId);
        await Assert.That(snapshot.OrganizerActorId).IsEqualTo(connection.OrganizerActorId);
        await Assert.That(snapshot.ProviderCode).IsEqualTo("stripe");
        await Assert.That(snapshot.ConnectPlatformId).IsEqualTo("platform-live-eu");
        await Assert.That(snapshot.ExternalAccountId).IsEqualTo("acct_123");
        await Assert.That(snapshot.CurrencyCode).IsEqualTo("EUR");
        await Assert.That(snapshot.ProfileCode).IsEqualTo("OrganizerDirect");
        await Assert.That(snapshot.InstancePolicyVersionId).IsEqualTo(instancePolicyVersionId);
        await Assert.That(snapshot.TenantPolicyVersionId).IsEqualTo(tenantPolicyVersionId);
    }

    [Test]
    public async Task RecipientSnapshot_HasNoPublicConstructorThatBypassesFactoryInvariants()
    {
        bool hasPublicConstructor = typeof(OrganizerPaymentRecipientSnapshot)
            .GetConstructors()
            .Any(constructor => constructor.IsPublic);

        await Assert.That(hasPublicConstructor).IsFalse();
    }

    [Test]
    public async Task RecipientSnapshot_FactoryRejectsInvalidIdentitiesAndRawProviderFacts()
    {
        await Assert.That(() => OrganizerPaymentRecipientSnapshot.Create(Guid.Empty, OrganizerActorId, Guid.CreateVersion7(), "stripe", "platform", "acct", "BE", "EUR", Guid.CreateVersion7(), null, Now))
            .Throws<ArgumentException>();
        await Assert.That(() => OrganizerPaymentRecipientSnapshot.Create(TenantId, OrganizerActorId, Guid.CreateVersion7(), "paypal", "platform", "acct", "BE", "EUR", Guid.CreateVersion7(), null, Now))
            .Throws<ArgumentException>();
        await Assert.That(() => OrganizerPaymentRecipientSnapshot.Create(TenantId, OrganizerActorId, Guid.CreateVersion7(), "stripe", new string('p', 121), "acct", "BE", "EUR", Guid.CreateVersion7(), null, Now))
            .Throws<ArgumentException>();
        await Assert.That(() => OrganizerPaymentRecipientSnapshot.Create(TenantId, OrganizerActorId, Guid.CreateVersion7(), "stripe", "platform", new string('a', 201), "BE", "EUR", Guid.CreateVersion7(), null, Now))
            .Throws<ArgumentException>();
        await Assert.That(() => OrganizerPaymentRecipientSnapshot.Create(TenantId, OrganizerActorId, Guid.CreateVersion7(), "stripe", "platform", "acct", "Belgium", "EUR", Guid.CreateVersion7(), null, Now))
            .Throws<ArgumentException>();
        await Assert.That(() => OrganizerPaymentRecipientSnapshot.Create(TenantId, OrganizerActorId, Guid.CreateVersion7(), "stripe", "platform", "acct", "BE", "XXX", Guid.CreateVersion7(), null, Now))
            .Throws<ArgumentException>();
        await Assert.That(() => OrganizerPaymentRecipientSnapshot.Create(TenantId, OrganizerActorId, Guid.CreateVersion7(), "stripe", "platform", "acct", "BE", "EUR", Guid.Empty, null, Now))
            .Throws<ArgumentException>();
        await Assert.That(() => OrganizerPaymentRecipientSnapshot.Create(TenantId, OrganizerActorId, Guid.CreateVersion7(), "stripe", "platform", "acct", "BE", "EUR", Guid.CreateVersion7(), Guid.Empty, Now))
            .Throws<ArgumentException>();
        await Assert.That(() => OrganizerPaymentRecipientSnapshot.Create(TenantId, OrganizerActorId, Guid.CreateVersion7(), "stripe", "platform", "acct", "BE", "EUR", Guid.CreateVersion7(), null, DateTime.SpecifyKind(Now, DateTimeKind.Local)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task RecipientSnapshot_RejectsTimestampBeforeConnectionCreationOrLatestReadinessEvidence()
    {
        OrganizerPaymentProviderConnection connection = ReadyConnection();

        await Assert.That(() => connection.CreateRecipientSnapshot("EUR", Guid.CreateVersion7(), null, Now.AddMinutes(-1))).Throws<ArgumentException>();
        await Assert.That(() => connection.CreateRecipientSnapshot("EUR", Guid.CreateVersion7(), null, Now.AddSeconds(30))).Throws<ArgumentException>();
    }

    [Test]
    public async Task RecipientSnapshot_FailsClosedForRestrictedDisabledReplacedOrUnsupportedCurrency()
    {
        OrganizerPaymentProviderConnection restricted = Connection();
        restricted.ApplyReadiness(OrganizerPaymentProviderReadinessObservation.Create("BE", ChargeCapabilityState.Inactive, ProviderRequirementsState.Satisfied, ["EUR"], Now.AddMinutes(1), "rev-1"));
        OrganizerPaymentProviderConnection disabled = ReadyConnection();
        disabled.Disable("operator_disabled", Now.AddMinutes(4));
        OrganizerPaymentProviderConnection replaced = ReadyConnection();
        _ = replaced.ReplaceWith(Guid.CreateVersion7(), "acct_new", Now.AddMinutes(4));

        await Assert.That(() => restricted.CreateRecipientSnapshot("EUR", Guid.CreateVersion7(), null, Now.AddMinutes(5))).Throws<InvalidOperationException>();
        await Assert.That(() => disabled.CreateRecipientSnapshot("EUR", Guid.CreateVersion7(), null, Now.AddMinutes(5))).Throws<InvalidOperationException>();
        await Assert.That(() => replaced.CreateRecipientSnapshot("EUR", Guid.CreateVersion7(), null, Now.AddMinutes(5))).Throws<InvalidOperationException>();
        await Assert.That(() => ReadyConnection().CreateRecipientSnapshot("USD", Guid.CreateVersion7(), null, Now.AddMinutes(5))).Throws<ArgumentException>();
    }

    [Test]
    public async Task TryCreateRecipientSnapshot_ReturnsTypedUnavailableForEveryNonReadyBoundary()
    {
        OrganizerPaymentProviderConnection pending = Connection();
        OrganizerPaymentProviderConnection restricted = Connection();
        restricted.ApplyReadiness(OrganizerPaymentProviderReadinessObservation.Create(
            "BE", ChargeCapabilityState.Pending, ProviderRequirementsState.CurrentlyDue, ["EUR"], Now.AddMinutes(1), "rev-restricted"));
        OrganizerPaymentProviderConnection disabled = ReadyConnection();
        disabled.Disable("operator_disabled", Now.AddMinutes(4));
        OrganizerPaymentProviderConnection replaced = ReadyConnection();
        _ = replaced.ReplaceWith(Guid.CreateVersion7(), "acct_new", Now.AddMinutes(4));
        OrganizerPaymentProviderConnection stale = ReadyConnection();

        OrganizerPaymentRecipientSnapshotResult[] results =
        [
            pending.TryCreateRecipientSnapshot("EUR", Guid.CreateVersion7(), null, Now.AddMinutes(5), TimeSpan.FromMinutes(5)),
            restricted.TryCreateRecipientSnapshot("EUR", Guid.CreateVersion7(), null, Now.AddMinutes(5), TimeSpan.FromMinutes(5)),
            disabled.TryCreateRecipientSnapshot("EUR", Guid.CreateVersion7(), null, Now.AddMinutes(5), TimeSpan.FromMinutes(5)),
            replaced.TryCreateRecipientSnapshot("EUR", Guid.CreateVersion7(), null, Now.AddMinutes(5), TimeSpan.FromMinutes(5)),
            ReadyConnection().TryCreateRecipientSnapshot("USD", Guid.CreateVersion7(), null, Now.AddMinutes(5), TimeSpan.FromMinutes(5)),
            stale.TryCreateRecipientSnapshot("EUR", Guid.CreateVersion7(), null, Now.AddMinutes(7), TimeSpan.FromMinutes(5))
        ];

        await Assert.That(results.Select(result => result.FailureCode)).IsEquivalentTo([
            "payment_connection_pending",
            "payment_connection_restricted",
            "payment_connection_disabled",
            "payment_connection_replaced",
            "payment_currency_unsupported",
            "payment_readiness_stale"]);
        await Assert.That(results.All(result => !result.Success && result.Snapshot is null)).IsTrue();
    }

    private static OrganizerPaymentProviderConnection ReadyConnection()
    {
        OrganizerPaymentProviderConnection connection = Connection();
        connection.ApplyReadiness(ReadyObservation(Now.AddMinutes(1), "rev-001"));
        return connection;
    }

    private static OrganizerPaymentProviderConnection Connection(string externalAccountId = "acct_123") =>
        OrganizerPaymentProviderConnection.Create(
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000101"),
            TenantId,
            OrganizerActorId,
            "STRIPE",
            " platform-live-eu ",
            externalAccountId,
            Now);

    private static OrganizerPaymentProviderReadinessObservation ReadyObservation(DateTime observedAt, string revision) =>
        OrganizerPaymentProviderReadinessObservation.Create(
            "BE",
            ChargeCapabilityState.Active,
            ProviderRequirementsState.Satisfied,
            ["EUR"],
            observedAt,
            revision);
}
