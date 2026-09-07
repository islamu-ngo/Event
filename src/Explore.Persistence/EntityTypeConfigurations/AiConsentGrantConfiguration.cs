using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.EntityTypeConfigurations;

public sealed class AiConsentGrantConfiguration : IEntityTypeConfiguration<AiConsentGrant>
{
    public void Configure(EntityTypeBuilder<AiConsentGrant> builder)
    {
        builder.HasKey(g => g.Id);

        builder.Property(g => g.EntityName)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(g => g.FieldName)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(g => g.Purpose)
            .HasMaxLength(512);

        builder.Property(g => g.ConcurrencyStamp)
            .IsConcurrencyToken();

        builder.Property(g => g.CreatedAt).IsRequired();
        builder.Property(g => g.TenantId).IsRequired();
        builder.Property(g => g.SubjectUserId).IsRequired();
        builder.Property(g => g.GrantedAtUtc).IsRequired();
        builder.Property(g => g.StatusId).IsRequired();
        builder.Property(g => g.ProviderTrustTierId).IsRequired();

        builder.HasIndex(g => new { g.SubjectUserId, g.EntityName, g.FieldName, g.ProviderTrustTierId })
            .HasDatabaseName("IX_AiConsentGrants_Subject_Entity_Field_Tier");

        builder.HasIndex(g => g.TenantId)
            .HasDatabaseName("IX_AiConsentGrants_TenantId");

        builder.HasOne(g => g.SubjectUser)
            .WithMany()
            .HasForeignKey(g => g.SubjectUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(g => g.Tenant)
            .WithMany()
            .HasForeignKey(g => g.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
