using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ExternalApiKeyIssuanceReceiptConfiguration : IEntityTypeConfiguration<ExternalApiKeyIssuanceReceipt>
{
    public void Configure(EntityTypeBuilder<ExternalApiKeyIssuanceReceipt> builder)
    {
        builder.HasKey(receipt => receipt.Id);
        builder.Property(receipt => receipt.Id).ValueGeneratedNever();
        builder.Property(receipt => receipt.OperationFingerprint).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(receipt => receipt.InputDigest).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(receipt => receipt.ExternalApiKeyId).IsRequired();
        builder.Property(receipt => receipt.CreatedAtUtc).IsRequired();
        builder.HasIndex(receipt => receipt.OperationFingerprint).IsUnique();
        builder.HasIndex(receipt => receipt.TenantId);
    }
}
