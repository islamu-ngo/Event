using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class StorageProducerOperationConfiguration : IEntityTypeConfiguration<StorageProducerOperation>
{
    public void Configure(EntityTypeBuilder<StorageProducerOperation> builder)
    {
        builder.ToTable("storage_producer_operations");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Provider).HasMaxLength(50).IsRequired();
        builder.Property(value => value.ObjectKey).HasMaxLength(1024).IsRequired();
        builder.Property(value => value.ProviderVersionId).HasMaxLength(1024);
        builder.Property(value => value.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasOne<StorageProviderBinding>().WithMany()
            .HasForeignKey(value => value.ProviderBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.ProviderBindingId, value.ObjectKey }).IsUnique();
        builder.HasIndex(value => value.CreatedAtUtc);
        foreach (var name in new[] { nameof(StorageProducerOperation.Id), nameof(StorageProducerOperation.TenantId),
            nameof(StorageProducerOperation.ProviderBindingId), nameof(StorageProducerOperation.Provider),
            nameof(StorageProducerOperation.ObjectKey), nameof(StorageProducerOperation.CreatedAtUtc) })
            builder.Property(name).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
