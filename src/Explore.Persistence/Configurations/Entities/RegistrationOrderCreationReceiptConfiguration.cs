using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

/// <summary>Creation tombstones have no cascading parent relationship or TTL.</summary>
public sealed class RegistrationOrderCreationReceiptConfiguration
    : IEntityTypeConfiguration<RegistrationOrderCreationReceipt>
{
    public void Configure(EntityTypeBuilder<RegistrationOrderCreationReceipt> builder)
    {
        builder.HasKey(receipt => receipt.Id);
        builder.Property(receipt => receipt.Id).ValueGeneratedNever();
        builder.Property(receipt => receipt.OperationFingerprint).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(receipt => receipt.InputDigest).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(receipt => receipt.CreatedAtUtc).IsRequired();
        builder.HasIndex(receipt => new { receipt.TenantId, receipt.OperationFingerprint }).IsUnique();
    }
}
