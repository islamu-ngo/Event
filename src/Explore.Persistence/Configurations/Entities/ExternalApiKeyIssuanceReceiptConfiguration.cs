using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

/// <summary>Retains unique, digest-only issuance evidence independently of credential deletion.</summary>
public sealed class ExternalApiKeyIssuanceReceiptConfiguration : IEntityTypeConfiguration<ExternalApiKeyIssuanceReceipt>
{
    /// <summary>Maps bounded fingerprints and lookup indexes without a cascading credential relationship.</summary>
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
