namespace Event.Domain.UnitTests.ConfigurationManifest;

public sealed class ConfigurationImportEffectReceiptTests
{
    private static readonly DateTime Now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string Digest = new('a', 64);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AppliedAndForwardRollbackBindOneImmutablePayloadReceipt(bool rollback)
    {
        var operation = ConfigurationImportOperation.CreateApplied(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "instance", null, Guid.CreateVersion7(),
            rollback ? Guid.CreateVersion7() : null, Digest, Digest, Digest, Digest, Digest,
            3, ["instance.settings"], Guid.CreateVersion7(), Digest, Now.AddDays(7),
            Guid.CreateVersion7(), true, Digest, [], Now, Now.AddMinutes(1));
        operation.BindEffectPayload("abc");
        await Assert.That(operation.EffectPayloadDigest)
            .IsEqualTo("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");
        await Assert.That(() => operation.BindEffectPayload("changed"))
            .Throws<InvalidOperationException>();
        await Assert.That(operation.EffectPayloadDigest)
            .IsEqualTo("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");
    }

    [Test]
    public async Task FailedImportCannotBindAPayloadReceipt()
    {
        var operation = ConfigurationImportOperation.CreateFailed(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "instance", null, Guid.CreateVersion7(),
            null, Digest, 3, ["instance.settings"], "failed", "Import failed.", Now, Now.AddMinutes(1));
        await Assert.That(() => operation.BindEffectPayload("abc")).Throws<InvalidOperationException>();
        await Assert.That(operation.EffectPayloadDigest).IsNull();
    }
}
