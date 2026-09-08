using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventContactShareExportConfiguration : IEntityTypeConfiguration<EventContactShareExport>
{
    public void Configure(EntityTypeBuilder<EventContactShareExport> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Format).IsRequired().HasMaxLength(20);
        builder.Property(e => e.PurposeCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.RequestedFieldKeysSnapshot).IsRequired().HasMaxLength(2000);
        builder.Property(e => e.IncludedFieldKeysSnapshot).IsRequired().HasMaxLength(2000);
        builder.Property(e => e.PolicyVersion).IsRequired().HasMaxLength(100);
        builder.Property(e => e.ContentHash).HasMaxLength(64);

        builder.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.RecipientActor)
            .WithMany()
            .HasForeignKey(e => e.RecipientActorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Event)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EventId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ExportedByUser).WithMany().HasForeignKey(e => e.ExportedByUserId).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
        builder.Metadata.FindNavigation(nameof(EventContactShareExport.Items))?.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(e => new { e.TenantId, e.RecipientActorId, e.CreatedAt })
            .HasDatabaseName("ix_eventcontactshareexports_recipient_date");
    }
}
