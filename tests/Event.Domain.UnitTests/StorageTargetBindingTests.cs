using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests;

public sealed class StorageTargetBindingTests
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task UnboundSessionCannotStartProviderWork()
    {
        var session = Session(null);
        session.ReserveObjectKey("objects/reserved.png");
        await Assert.That(() => session.MarkUploading(Now)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReservedKeyCannotBeRedirected()
    {
        var session = Session(Binding());
        session.ReserveObjectKey("objects/original.png");
        await Assert.That(() => session.ReserveObjectKey("objects/replacement.png"))
            .Throws<InvalidOperationException>();
        await Assert.That(session.ObjectKey).IsEqualTo("objects/original.png");
    }

    [Test]
    public async Task SessionBindingCannotBeRedirected()
    {
        var binding = Binding();
        var session = Session(binding);
        await Assert.That(() => session.StorageProviderBindingId = Binding().Id)
            .Throws<InvalidOperationException>();
        await Assert.That(session.StorageProviderBindingId).IsEqualTo(binding.Id);
    }

    [Test]
    public async Task GenericFinalizationRequiresExactSettledKey()
    {
        var session = Session(Binding());
        session.ReserveObjectKey("objects/original.png");
        session.MarkUploading(Now);
        await Assert.That(() => session.Finalize(session.Id, "objects/replacement.png", null, Now))
            .Throws<InvalidOperationException>();
        await Assert.That(() => session.Finalize(session.Id, "objects/original.png", null, Now))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task LateGenericSettlementPreservesCanceledStateAndExactVersion()
    {
        var binding = Binding();
        var session = Session(binding);
        session.ReserveObjectKey("objects/original.png");
        session.MarkUploading(Now);
        session.Cancel(Now);
        session.RecordProducerSettlement(session.Id, binding.Id, session.ObjectKey!, "version-one");
        await Assert.That(session.ProducerSettled).IsTrue();
        await Assert.That(session.Status).IsEqualTo(StorageUploadSessionStates.Canceled);
        await Assert.That(() => session.RecordProducerSettlement(session.Id, binding.Id,
            session.ObjectKey!, "version-two")).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ProducerKeepsItsOriginalTargetWhenTheDefaultChanges()
    {
        var binding = Binding();
        var operation = StorageProducerOperation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            binding, "objects/generated.csv", Now);
        var replacement = StorageProviderBinding.Local(Path.Combine(Path.GetTempPath(), "replacement-storage"));
        await Assert.That(operation.ProviderBindingId).IsEqualTo(binding.Id);
        await Assert.That(operation.ProviderBindingId).IsNotEqualTo(replacement.Id);
        await Assert.That(operation.ProducerSettled).IsFalse();
        await Assert.That(operation.Retire(Now).State).IsEqualTo(StorageObjectDeletionState.AwaitingProducer);
    }

    [Test]
    public async Task ProducerRejectsMismatchedTargetKeyAndProviderAcknowledgements()
    {
        var binding = Binding();
        var operation = StorageProducerOperation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            binding, "objects/generated.csv", Now);
        await Assert.That(() => operation.Settle(Binding().Id, binding.Provider, operation.ObjectKey, null))
            .Throws<InvalidOperationException>();
        await Assert.That(() => operation.Settle(binding.Id, StorageProviders.S3Compatible, operation.ObjectKey, null))
            .Throws<InvalidOperationException>();
        await Assert.That(() => operation.Settle(binding.Id, binding.Provider, "objects/different.csv", null))
            .Throws<InvalidOperationException>();
        await Assert.That(operation.ProducerSettled).IsFalse();
    }

    [Test]
    public async Task SettledProducerTransfersExactVersionToExistingCleanupAuthority()
    {
        var binding = Binding();
        var operation = StorageProducerOperation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            binding, "objects/generated.csv", Now);
        operation.Settle(binding.Id, binding.Provider, operation.ObjectKey, "version-one");
        await Assert.That(() => operation.Settle(binding.Id, binding.Provider, operation.ObjectKey, "version-two"))
            .Throws<InvalidOperationException>();
        var tombstone = operation.Retire(Now);
        await Assert.That(tombstone.Id).IsEqualTo(operation.Id);
        await Assert.That(tombstone.ProviderBindingId).IsEqualTo(binding.Id);
        await Assert.That(tombstone.ObjectKey).IsEqualTo(operation.ObjectKey);
        await Assert.That(tombstone.ProviderObjectVersion).IsEqualTo("version-one");
        await Assert.That(tombstone.State).IsEqualTo(StorageObjectDeletionState.Ready);
    }

    private static StorageProviderBinding Binding() =>
        StorageProviderBinding.Local(Path.Combine(Path.GetTempPath(), "captured-storage"));

    [Test]
    public async Task DeletedMetadataCanForgetItsKeyWithoutAcquiringADifferentTarget()
    {
        var binding = Binding();
        var stored = new StorageObject
        {
            Id = Guid.CreateVersion7(), TenantId = Guid.CreateVersion7(), Tenant = null!, FileType = null!,
            Provider = binding.Provider, StorageProviderBindingId = binding.Id, ObjectKey = "objects/original.pdf",
            FullName = "file.pdf", SafeDisplayName = "file.pdf", Extension = "pdf",
            Purpose = StorageObjectPurposes.Document, Visibility = StorageObjectVisibilities.PrivateOwner,
            LifecycleState = StorageObjectLifecycleStates.Active
        };
        // The existing erasure owner clears content metadata before marking its retained row deleted.
        stored.ObjectKey = null;
        stored.MarkDeleted(null, Now);
        await Assert.That(stored.ObjectKey).IsNull();
        await Assert.That(stored.IsDeleted).IsTrue();
        await Assert.That(stored.StorageProviderBindingId).IsEqualTo(binding.Id);
        await Assert.That(() => stored.ObjectKey = "objects/replacement.pdf").Throws<InvalidOperationException>();
        await Assert.That(() => stored.StorageProviderBindingId = Binding().Id).Throws<InvalidOperationException>();
    }

    private static StorageUploadSession Session(StorageProviderBinding? binding) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = Guid.CreateVersion7(),
        Provider = StorageProviders.Local,
        StorageProviderBindingId = binding?.Id,
        ContentType = "image/png",
        SafeDisplayName = "image.png",
        Purpose = StorageObjectPurposes.EventImage,
        Visibility = StorageObjectVisibilities.PublicImage,
        Status = StorageUploadSessionStates.Reserved,
        ExpiresAt = Now.AddMinutes(15)
    };
}
