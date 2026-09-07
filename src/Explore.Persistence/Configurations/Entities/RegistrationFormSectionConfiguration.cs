using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationFormSectionConfiguration : IEntityTypeConfiguration<RegistrationFormSection>
{
    public void Configure(EntityTypeBuilder<RegistrationFormSection> builder)
    {
        builder.Property(section => section.Id).ValueGeneratedNever();
        builder.Property(section => section.Title).IsRequired().HasMaxLength(200);
        builder.Property(section => section.CreatedAt).IsRequired();
        builder.Property(section => section.IsDeleted).HasDefaultValue(false);
        builder.Property(section => section.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(section => new
        {
            section.TenantId,
            section.EventId,
            section.RegistrationFormId,
            section.RegistrationFormVersionId,
            section.Id
        });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(section => section.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationFormVersion>().WithMany(version => version.Sections)
            .HasForeignKey(section => new
            {
                section.TenantId,
                section.EventId,
                section.RegistrationFormId,
                section.RegistrationFormVersionId
            })
            .HasPrincipalKey(version => new
            {
                version.TenantId,
                version.EventId,
                version.RegistrationFormId,
                version.Id
            }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(section => new
        {
            section.TenantId,
            section.EventId,
            section.RegistrationFormVersionId,
            section.Ordinal
        }).IsUnique().HasFilter("is_deleted = false");
    }
}
