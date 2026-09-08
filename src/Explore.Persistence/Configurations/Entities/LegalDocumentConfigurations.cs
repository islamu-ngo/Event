namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class LegalDocumentConfiguration
    : IEntityTypeConfiguration<LegalDocument>
{
    public void Configure(EntityTypeBuilder<LegalDocument> builder)
    {
        builder.ToTable("legal_documents", table =>
        {
            table.HasCheckConstraint(
                "ck_legal_documents_scope_tenant",
                "(scope = 1 AND tenant_id IS NULL) OR " +
                "(scope = 2 AND tenant_id IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_legal_documents_state",
                "state >= 1 AND state <= 6");
            table.HasCheckConstraint(
                "ck_legal_documents_current_version",
                "current_version > 0");
        });

        builder.HasKey(document => document.Id);
        builder.Property(document => document.Scope).IsRequired();
        builder.Property(document => document.TenantId);
        builder.Property(document => document.AuthorityKey)
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(document => document.Kind).IsRequired();
        builder.Property(document => document.OwnerRole).IsRequired();
        builder.Property(document => document.State).IsRequired();
        builder.Property(document => document.CurrentVersion).IsRequired();
        builder.Property(document => document.AccountableIdentityReference)
            .HasMaxLength(200);
        builder.Property(document => document.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();
        builder.Property(document => document.CreatedAt).IsRequired();

        builder.HasIndex(document => new
            {
                document.AuthorityKey,
                document.Kind
            })
            .IsUnique();
        builder.HasIndex(document => new
            {
                document.TenantId,
                document.State,
                document.Kind
            });

        builder.HasMany(document => document.Versions)
            .WithOne(version => version.LegalDocument)
            .HasForeignKey(version => version.LegalDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(document => document.Versions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(document => document.Publications)
            .WithOne(publication => publication.LegalDocument)
            .HasForeignKey(publication => publication.LegalDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(document => document.Publications)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class LegalDocumentVersionConfiguration
    : IEntityTypeConfiguration<LegalDocumentVersion>
{
    public void Configure(EntityTypeBuilder<LegalDocumentVersion> builder)
    {
        builder.ToTable("legal_document_versions", table =>
        {
            table.HasCheckConstraint(
                "ck_legal_document_versions_version",
                "version > 0");
            table.HasCheckConstraint(
                "ck_legal_document_versions_state",
                "state >= 1 AND state <= 6");
        });

        builder.HasKey(version => version.Id);
        builder.Property(version => version.Version).IsRequired();
        builder.Property(version => version.Audience).IsRequired();
        builder.Property(version => version.State).IsRequired();
        builder.Property(version => version.ContentDigest)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(version => version.SourceOrigin).HasMaxLength(200);
        builder.Ignore(version => version.IsImported);
        builder.Property(version => version.RequiresFreshAcceptance).IsRequired();
        builder.Property(version => version.TemplateId).HasMaxLength(100);
        builder.Property(version => version.TemplateVersion).HasMaxLength(50);
        builder.Property(version => version.TemplateSourceKind);
        builder.Property(version => version.TemplateLicenseExpression)
            .HasMaxLength(100);
        builder.Property(version => version.TemplateReviewReference)
            .HasMaxLength(200);
        builder.Property(version => version.ReviewEvidenceReference)
            .HasMaxLength(200);
        builder.Property(version => version.AccountableIdentityReference)
            .HasMaxLength(200);
        builder.Property(version => version.CreatedAt).IsRequired();

        builder.HasIndex(version => new
            {
                version.LegalDocumentId,
                version.Version
            })
            .IsUnique();
        builder.HasIndex(version => new
            {
                version.State,
                version.ProposedEffectiveAt
            });

        builder.HasMany(version => version.Sources)
            .WithOne(source => source.LegalDocumentVersion)
            .HasForeignKey(source => source.LegalDocumentVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(version => version.Sources)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class LegalDocumentLocalizedSourceConfiguration
    : IEntityTypeConfiguration<LegalDocumentLocalizedSource>
{
    public void Configure(
        EntityTypeBuilder<LegalDocumentLocalizedSource> builder)
    {
        builder.ToTable("legal_document_localized_sources", table =>
        {
            table.HasCheckConstraint(
                "ck_legal_document_localized_sources_counts",
                $"utf8_byte_count >= 1 AND utf8_byte_count <= " +
                $"{ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumMarkdownUtf8BytesPerLocale} " +
                $"AND link_count >= 0 AND link_count <= " +
                $"{ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumLinksPerLocale} " +
                $"AND placeholder_count >= 0 AND placeholder_count <= " +
                $"{ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumPlaceholdersPerLocale}");
        });

        builder.HasKey(source => source.Id);
        builder.Property(source => source.LanguageTag)
            .HasMaxLength(ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumLanguageTagLength)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(source => source.Title)
            .HasMaxLength(ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumTitleLength)
            .IsRequired();
        builder.Property(source => source.Summary)
            .HasMaxLength(ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumSummaryLength)
            .IsRequired();
        builder.Property(source => source.Markdown)
            .HasMaxLength(
                ISLAMU.Wire.Contracts.ConfigurationPortability.LegalMarkdownContentLimits.MaximumMarkdownUtf8BytesPerLocale)
            .IsRequired();
        builder.Property(source => source.Utf8ByteCount)
            .HasColumnName("utf8_byte_count")
            .IsRequired();
        builder.Property(source => source.LinkCount).IsRequired();
        builder.Property(source => source.PlaceholderCount).IsRequired();

        builder.HasIndex(source => new
            {
                source.LegalDocumentVersionId,
                source.LanguageTag
            })
            .IsUnique();
    }
}

public sealed class LegalDocumentPublicationConfiguration
    : IEntityTypeConfiguration<LegalDocumentPublication>
{
    public void Configure(EntityTypeBuilder<LegalDocumentPublication> builder)
    {
        builder.ToTable("legal_document_publications", table =>
        {
            table.HasCheckConstraint(
                "ck_legal_document_publications_state",
                "lifecycle_state IN (5, 6)");
            table.HasCheckConstraint(
                "ck_legal_document_publications_version",
                "version > 0");
        });

        builder.HasKey(publication => publication.Id);
        builder.Property(publication => publication.Version).IsRequired();
        builder.Property(publication => publication.LifecycleState).IsRequired();
        builder.Property(publication => publication.ContentDigest)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(publication => publication.AccountableIdentityReference)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(publication => publication.ReviewEvidenceReference)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(publication => publication.EffectiveAt).IsRequired();
        builder.Property(publication => publication.OccurredAt).IsRequired();
        builder.Property(publication => publication.RequiresFreshAcceptance)
            .IsRequired();

        builder.HasOne(publication => publication.LegalDocumentVersion)
            .WithMany()
            .HasForeignKey(publication => publication.LegalDocumentVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(publication => new
            {
                publication.LegalDocumentId,
                publication.Version,
                publication.LifecycleState
            })
            .IsUnique();
        builder.HasIndex(publication => new
            {
                publication.LegalDocumentId,
                publication.OccurredAt
            });
    }
}
