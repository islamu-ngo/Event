using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationFormFieldConfiguration : IEntityTypeConfiguration<RegistrationFormField>
{
    public void Configure(EntityTypeBuilder<RegistrationFormField> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_registration_form_fields_consent_metadata",
            "(requires_explicit_consent AND consent_purpose_code IS NOT NULL AND consent_text IS NOT NULL AND consent_text_version IS NOT NULL " +
            "AND length(btrim(consent_purpose_code)) > 0 AND length(btrim(consent_text)) > 0 AND length(btrim(consent_text_version)) > 0) OR " +
            "(NOT requires_explicit_consent AND consent_purpose_code IS NULL AND consent_text IS NULL AND consent_text_version IS NULL)"));
        builder.Property(field => field.Id).ValueGeneratedNever();
        builder.Property(field => field.Namespace).IsRequired().HasMaxLength(100);
        builder.Property(field => field.Key).IsRequired().HasMaxLength(100);
        builder.Property(field => field.Label).IsRequired().HasMaxLength(500);
        builder.Property(field => field.ConsentPurposeCode).HasMaxLength(100);
        builder.Property(field => field.ConsentText).HasMaxLength(4000);
        builder.Property(field => field.ConsentTextVersion).HasMaxLength(100);
        builder.Property(field => field.ExportPurposeCode).HasMaxLength(100);
        builder.Property(field => field.RegexPattern).HasMaxLength(1000);
        builder.Property(field => field.AllowedUrlSchemes).HasMaxLength(200);
        builder.Property(field => field.CreatedAt).IsRequired();
        builder.Property(field => field.IsDeleted).HasDefaultValue(false);
        builder.Property(field => field.IsAnalyticsRelevant).HasDefaultValue(false);
        builder.Property(field => field.IsOperationallyFilterable).HasDefaultValue(false);
        builder.Property(field => field.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(field => new
        {
            field.TenantId,
            field.EventId,
            field.RegistrationFormId,
            field.RegistrationFormVersionId,
            field.RegistrationFormSectionId,
            field.Id
        });
        builder.HasAlternateKey(field => new
        {
            field.TenantId,
            field.EventId,
            field.RegistrationFormId,
            field.RegistrationFormVersionId,
            field.RegistrationFormSectionId,
            field.Id,
            field.FieldTypeId
        });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(field => field.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationFormSection>().WithMany(section => section.Fields)
            .HasForeignKey(field => new
            {
                field.TenantId,
                field.EventId,
                field.RegistrationFormId,
                field.RegistrationFormVersionId,
                field.RegistrationFormSectionId
            })
            .HasPrincipalKey(section => new
            {
                section.TenantId,
                section.EventId,
                section.RegistrationFormId,
                section.RegistrationFormVersionId,
                section.Id
            }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(field => field.FieldType).WithMany().HasForeignKey(field => field.FieldTypeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(field => field.OrganizerVisibility).WithMany().HasForeignKey(field => field.OrganizerVisibilityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(field => field.RetentionPolicy).WithMany().HasForeignKey(field => field.RetentionPolicyId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(field => new
        {
            field.TenantId,
            field.EventId,
            field.RegistrationFormVersionId,
            field.RegistrationFormSectionId,
            field.Ordinal
        }).IsUnique().HasFilter("is_deleted = false");
        builder.HasIndex(field => new
        {
            field.TenantId,
            field.EventId,
            field.RegistrationFormVersionId,
            field.Namespace,
            field.Key
        }).IsUnique().HasFilter("is_deleted = false");
    }
}
