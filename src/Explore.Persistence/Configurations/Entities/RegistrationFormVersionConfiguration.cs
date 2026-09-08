using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationFormVersionConfiguration : IEntityTypeConfiguration<RegistrationFormVersion>
{
    public void Configure(EntityTypeBuilder<RegistrationFormVersion> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_registration_form_versions_schema_artifacts",
            "(status_id = 1 AND schema_hash IS NULL AND data_schema_artifact IS NULL AND ui_schema_artifact IS NULL AND logic_schema_artifact IS NULL AND mapping_artifact IS NULL) OR " +
            "(status_id IN (2, 3) AND schema_hash IS NOT NULL AND data_schema_artifact IS NOT NULL AND ui_schema_artifact IS NOT NULL AND logic_schema_artifact IS NOT NULL AND mapping_artifact IS NOT NULL)"));
        builder.Property(version => version.Id).ValueGeneratedNever();
        builder.Property(version => version.LanguageTag).IsRequired().HasMaxLength(35);
        builder.Property(version => version.SourceKindId).HasDefaultValue(1);
        builder.Property(version => version.SchemaHash).HasMaxLength(64);
        builder.Property(version => version.ExternalProviderSurveyId).HasMaxLength(200);
        builder.Property(version => version.ExternalProviderSurveyRevisionId).HasMaxLength(200);
        builder.Property(version => version.ExternalImportMappingRevisionHash).HasMaxLength(44);
        builder.Property(version => version.DataSchemaArtifact).HasColumnType("text");
        builder.Property(version => version.UiSchemaArtifact).HasColumnType("text");
        builder.Property(version => version.LogicSchemaArtifact).HasColumnType("text");
        builder.Property(version => version.MappingArtifact).HasColumnType("text");
        builder.Property(version => version.CreatedAt).IsRequired();
        builder.Property(version => version.IsDeleted).HasDefaultValue(false);
        builder.Property(version => version.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(version => new
        {
            version.TenantId,
            version.EventId,
            version.RegistrationFormId,
            version.Id
        });
        builder.HasAlternateKey(version => new
        {
            version.TenantId,
            version.RegistrationFormId,
            version.Id
        });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(version => version.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany()
            .HasForeignKey(version => new { version.TenantId, version.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationForm>().WithMany(form => form.Versions)
            .HasForeignKey(version => new { version.TenantId, version.EventId, version.RegistrationFormId })
            .HasPrincipalKey(form => new { form.TenantId, form.EventId, form.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(version => version.Status).WithMany().HasForeignKey(version => version.StatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(version => version.SourceKind).WithMany().HasForeignKey(version => version.SourceKindId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationProviderConnection>().WithMany()
            .HasForeignKey(version => version.ExternalRegistrationProviderConnectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationProviderSchemaRevision>().WithMany()
            .HasForeignKey(version => version.ExternalRegistrationProviderSchemaRevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(version => new { version.TenantId, version.EventId, version.RegistrationFormId, version.Version })
            .IsUnique().HasFilter("is_deleted = false");
        builder.HasIndex(version => new
        {
            version.TenantId,
            version.EventId,
            version.RegistrationFormId,
            version.StatusId,
            version.LanguageTag
        });
    }
}
