using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventSessionTemplateConfiguration : IEntityTypeConfiguration<EventSessionTemplate>
{
    public void Configure(EntityTypeBuilder<EventSessionTemplate> builder)
    {
        builder.Property(e => e.Id)
            .HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.ConcurrencyStamp)
            .IsConcurrencyToken();

        builder.Property(e => e.SessionTemplateKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.HasOne(e => e.EventTemplate)
            .WithMany()
            .HasForeignKey(e => e.EventTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Definitions)
            .WithOne(d => d.EventSessionTemplate)
            .HasForeignKey(d => d.EventSessionTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.EventTemplateId, e.SessionTemplateKey, e.Version })
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.IsPublished, e.IsActive });
    }
}
