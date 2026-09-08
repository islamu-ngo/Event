using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationFormFieldOptionConfiguration : IEntityTypeConfiguration<RegistrationFormFieldOption>
{
    public void Configure(EntityTypeBuilder<RegistrationFormFieldOption> builder)
    {
        builder.Property(option => option.Id).ValueGeneratedNever();
        builder.Property(option => option.Key).IsRequired().HasMaxLength(100);
        builder.Property(option => option.Label).IsRequired().HasMaxLength(500);
        builder.Property(option => option.CreatedAt).IsRequired();
        builder.Property(option => option.IsDeleted).HasDefaultValue(false);
        builder.Property(option => option.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(option => new
        {
            option.TenantId,
            option.EventId,
            option.RegistrationFormId,
            option.RegistrationFormVersionId,
            option.RegistrationFormSectionId,
            option.RegistrationFormFieldId,
            option.Id
        });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(option => option.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationFormField>().WithMany(field => field.Options)
            .HasForeignKey(option => new
            {
                option.TenantId,
                option.EventId,
                option.RegistrationFormId,
                option.RegistrationFormVersionId,
                option.RegistrationFormSectionId,
                option.RegistrationFormFieldId
            })
            .HasPrincipalKey(field => new
            {
                field.TenantId,
                field.EventId,
                field.RegistrationFormId,
                field.RegistrationFormVersionId,
                field.RegistrationFormSectionId,
                field.Id
            }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(option => new
        {
            option.TenantId,
            option.EventId,
            option.RegistrationFormVersionId,
            option.RegistrationFormFieldId,
            option.Ordinal
        }).IsUnique().HasFilter("is_deleted = false");
        builder.HasIndex(option => new
        {
            option.TenantId,
            option.EventId,
            option.RegistrationFormVersionId,
            option.RegistrationFormFieldId,
            option.Key
        }).IsUnique().HasFilter("is_deleted = false");
    }
}
