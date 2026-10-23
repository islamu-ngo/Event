namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationOrderCreationReceiptTests
{
    private static readonly DateTime Now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string Scope = new('a', 64);

    [Test]
    [Arguments(null, false)]
    [Arguments("", false)]
    [Arguments("order.retry:1_v2-3", true)]
    [Arguments(" spaced", false)]
    [Arguments("line\nbreak", false)]
    [Arguments("nonascii-\u00E9", false)]
    public async Task OperationKeysRequireTheClosedAsciiGrammar(string? key, bool accepted)
    {
        await Assert.That(RegistrationOrderCreationReceipt.IsValidOperationKey(key)).IsEqualTo(accepted);
        await Assert.That(RegistrationOrderCreationReceipt.IsValidOperationKey(new string('x', 128))).IsTrue();
        await Assert.That(RegistrationOrderCreationReceipt.IsValidOperationKey(new string('x', 129))).IsFalse();
    }

    [Test]
    public async Task FingerprintRetainsEveryOriginalAuthorityAxisAndExactOperationKey()
    {
        Guid tenant = Guid.CreateVersion7(), account = Guid.CreateVersion7();
        string original = RegistrationOrderCreationReceipt.ComputeOperationFingerprint(tenant, account, Scope, "Order.1");
        await Assert.That(original.Length).IsEqualTo(64);
        await Assert.That(original).IsEqualTo(
            RegistrationOrderCreationReceipt.ComputeOperationFingerprint(tenant, account, Scope.ToUpperInvariant(), "Order.1"));
        await Assert.That(original).IsNotEqualTo(
            RegistrationOrderCreationReceipt.ComputeOperationFingerprint(Guid.CreateVersion7(), account, Scope, "Order.1"));
        await Assert.That(original).IsNotEqualTo(
            RegistrationOrderCreationReceipt.ComputeOperationFingerprint(tenant, Guid.CreateVersion7(), Scope, "Order.1"));
        await Assert.That(original).IsNotEqualTo(
            RegistrationOrderCreationReceipt.ComputeOperationFingerprint(tenant, account, new string('b', 64), "Order.1"));
        await Assert.That(original).IsNotEqualTo(
            RegistrationOrderCreationReceipt.ComputeOperationFingerprint(tenant, account, Scope, "order.1"));
    }

    [Test]
    public async Task ReceiptNormalizesDigestsAndPreventsTenantReattribution()
    {
        Guid id = Guid.CreateVersion7(), tenant = Guid.CreateVersion7(), order = Guid.CreateVersion7();
        var receipt = RegistrationOrderCreationReceipt.Create(id, tenant, new string('A', 64),
            new string('B', 64), order, Now);
        await Assert.That(receipt.Id).IsEqualTo(id);
        await Assert.That(receipt.RegistrationOrderId).IsEqualTo(order);
        await Assert.That(receipt.OperationFingerprint).IsEqualTo(new string('a', 64));
        await Assert.That(receipt.InputDigest).IsEqualTo(new string('b', 64));
        receipt.TenantId = tenant;
        await Assert.That(() => receipt.TenantId = Guid.CreateVersion7()).Throws<InvalidOperationException>();
        await Assert.That(receipt.TenantId).IsEqualTo(tenant);
    }

    [Test]
    [Arguments("receipt")]
    [Arguments("tenant")]
    [Arguments("order")]
    [Arguments("digest")]
    [Arguments("time")]
    public async Task InvalidCreationEvidenceIsRejectedBeforeConstruction(string invalid)
    {
        await Assert.That(() => RegistrationOrderCreationReceipt.Create(
            invalid == "receipt" ? Guid.Empty : Guid.CreateVersion7(),
            invalid == "tenant" ? Guid.Empty : Guid.CreateVersion7(),
            Scope, invalid == "digest" ? new string('g', 64) : Scope,
            invalid == "order" ? Guid.Empty : Guid.CreateVersion7(),
            invalid == "time" ? DateTime.SpecifyKind(Now, DateTimeKind.Unspecified) : Now))
            .Throws<ArgumentException>();
    }
}
