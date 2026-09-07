using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationFormConfiguration : IEntityTypeConfiguration<RegistrationForm>
{
    public void Configure(EntityTypeBuilder<RegistrationForm> builder)
    {
        builder.Property(form => form.Id).ValueGeneratedNever();
        builder.Property(form => form.Namespace).IsRequired().HasMaxLength(100);
        builder.Property(form => form.Key).IsRequired().HasMaxLength(100);
        builder.Property(form => form.Name).IsRequired().HasMaxLength(200);
        builder.Property(form => form.CreatedAt).IsRequired();
        builder.Property(form => form.IsDeleted).HasDefaultValue(false);
        builder.Property(form => form.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(form => new { form.TenantId, form.EventId, form.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(form => form.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany()
            .HasForeignKey(form => new { form.TenantId, form.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(form => new { form.TenantId, form.EventId, form.Namespace, form.Key })
            .IsUnique().HasFilter("is_deleted = false");
    }
}
