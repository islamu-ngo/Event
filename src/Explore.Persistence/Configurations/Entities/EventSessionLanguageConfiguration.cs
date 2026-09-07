using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventSessionLanguageConfiguration : IEntityTypeConfiguration<EventSessionLanguage>
{
    public void Configure(EntityTypeBuilder<EventSessionLanguage> builder)
    {
        builder.Property(e => e.ConcurrencyStamp)
            .IsConcurrencyToken();

        builder.HasOne(e => e.EventSession)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EventSessionId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Language)
            .WithMany()
            .HasForeignKey(e => e.LanguageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique constraint: one language per event session
        builder.HasIndex(e => new { e.TenantId, e.EventSessionId, e.LanguageId })
            .IsUnique();
    }
}
