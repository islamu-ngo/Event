using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationProviderConnectionConfiguration : IEntityTypeConfiguration<RegistrationProviderConnection>
{
    public void Configure(EntityTypeBuilder<RegistrationProviderConnection> builder)
    {
        builder.Property(connection => connection.Id).ValueGeneratedNever();
        builder.Property(connection => connection.Name).IsRequired().HasMaxLength(120);
        builder.Property(connection => connection.ProviderCode).IsRequired().HasMaxLength(100);
        builder.Property(connection => connection.ProviderDeploymentCode).IsRequired().HasMaxLength(100);
        builder.Property(connection => connection.ApiVersion).IsRequired().HasMaxLength(100);
        builder.Property(connection => connection.AdapterPolicyVersion).IsRequired().HasMaxLength(100);
        builder.Property(connection => connection.ConformanceEvidenceRevision).IsRequired().HasMaxLength(120);
        builder.Property(connection => connection.ManagementApiBaseUrl).IsRequired().HasMaxLength(500);
        builder.Property(connection => connection.PublicBaseUrl).IsRequired().HasMaxLength(500);
        builder.Property(connection => connection.ProviderWorkspaceId).IsRequired().HasMaxLength(200);
        builder.Property(connection => connection.GrantedOAuthScopes).IsRequired().HasMaxLength(1000).HasDefaultValue(string.Empty);
        builder.Property(connection => connection.ProviderIdentity).IsRequired().HasMaxLength(200).HasDefaultValue(string.Empty);
        builder.Property(connection => connection.PubSubConfigurationReference).IsRequired().HasMaxLength(300).HasDefaultValue(string.Empty);
        builder.Property(connection => connection.CreatedAt).IsRequired();
        builder.Property(connection => connection.IsDeleted).HasDefaultValue(false);
        builder.Property(connection => connection.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(connection => new { connection.TenantId, connection.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(connection => connection.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationProviderKind>().WithMany().HasForeignKey(connection => connection.ProviderKindId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationProviderDeploymentKind>().WithMany().HasForeignKey(connection => connection.DeploymentKindId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SecretBinding>().WithMany().HasForeignKey(connection => connection.ApiTokenSecretBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SecretBinding>().WithMany().HasForeignKey(connection => connection.WebhookSecretBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(connection => connection.ApprovedOrigins).WithOne(origin => origin.Connection).HasForeignKey(origin => new { origin.TenantId, origin.RegistrationProviderConnectionId }).HasPrincipalKey(connection => new { connection.TenantId, connection.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(connection => new { connection.TenantId, connection.Name }).IsUnique();
        builder.HasIndex(connection => new { connection.TenantId, connection.ProviderCode, connection.ProviderDeploymentCode, connection.ApiVersion, connection.AdapterPolicyVersion, connection.ConformanceEvidenceRevision, connection.ProviderWorkspaceId }).IsUnique();
    }
}

public sealed class RegistrationProviderApprovedOriginConfiguration : IEntityTypeConfiguration<RegistrationProviderApprovedOrigin>
{
    public void Configure(EntityTypeBuilder<RegistrationProviderApprovedOrigin> builder)
    {
        builder.Property(origin => origin.Id).ValueGeneratedNever();
        builder.Property(origin => origin.Origin).IsRequired().HasMaxLength(300);
        builder.Property(origin => origin.CreatedAt).IsRequired();
        builder.Property(origin => origin.IsDeleted).HasDefaultValue(false);
        builder.HasAlternateKey(origin => new { origin.TenantId, origin.RegistrationProviderConnectionId, origin.Id });
        builder.HasIndex(origin => new { origin.TenantId, origin.RegistrationProviderConnectionId, origin.Origin }).IsUnique();
    }
}

public sealed class RegistrationProviderBindingConfiguration : IEntityTypeConfiguration<RegistrationProviderBinding>
{
    public void Configure(EntityTypeBuilder<RegistrationProviderBinding> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint("ck_registration_provider_bindings_publication", $"(state_id = {(int)RegistrationProviderBindingStateEnum.Published} AND published_mapping_revision_hash IS NOT NULL AND published_at IS NOT NULL) OR (state_id <> {(int)RegistrationProviderBindingStateEnum.Published})"));
        builder.Property(binding => binding.Id).ValueGeneratedNever();
        builder.Property(binding => binding.PublishedMappingRevisionHash).HasConversion(hash => hash == null ? null : hash.Value, value => value == null ? null : RegistrationEvidenceHash.Create(value)).HasMaxLength(44);
        builder.Property(binding => binding.PublishedMappingRevisionHashKey).IsRequired().HasMaxLength(44).HasDefaultValue(string.Empty);
        builder.Property(binding => binding.ProviderSurveyId).HasMaxLength(200);
        builder.Property(binding => binding.ProviderSurveyRevisionId).HasMaxLength(200);
        builder.Property(binding => binding.ProviderWebhookId).HasMaxLength(200);
        builder.Property(binding => binding.CreatedAt).IsRequired();
        builder.Property(binding => binding.IsDeleted).HasDefaultValue(false);
        builder.Property(binding => binding.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(binding => binding.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasAlternateKey(binding => new { binding.TenantId, binding.Id });
        builder.HasAlternateKey(binding => new { binding.TenantId, binding.Id, binding.PublishedMappingRevisionHashKey });
        builder.HasOne(binding => binding.Connection).WithMany().HasForeignKey(binding => new { binding.TenantId, binding.RegistrationProviderConnectionId }).HasPrincipalKey(connection => new { connection.TenantId, connection.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SecretBinding>().WithMany().HasForeignKey(binding => binding.WebhookSecretBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationFormVersion>().WithMany().HasForeignKey(binding => new { binding.TenantId, binding.RegistrationFormId, binding.RegistrationFormVersionId }).HasPrincipalKey(version => new { version.TenantId, version.RegistrationFormId, version.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationProviderPresentationMode>().WithMany().HasForeignKey(binding => binding.PresentationModeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationProviderCollectionMode>().WithMany().HasForeignKey(binding => binding.CollectionModeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationProviderCompletionMode>().WithMany().HasForeignKey(binding => binding.CompletionModeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationProviderTrustLevel>().WithMany().HasForeignKey(binding => binding.TrustLevelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationProviderDriftClass>().WithMany().HasForeignKey(binding => binding.DriftClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationProviderBindingState>().WithMany().HasForeignKey(binding => binding.StateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(binding => binding.FieldMappings).WithOne(mapping => mapping.Binding).HasForeignKey(mapping => new { mapping.TenantId, mapping.RegistrationProviderBindingId }).HasPrincipalKey(binding => new { binding.TenantId, binding.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(binding => binding.OptionMappings).WithOne(mapping => mapping.Binding).HasForeignKey(mapping => new { mapping.TenantId, mapping.RegistrationProviderBindingId }).HasPrincipalKey(binding => new { binding.TenantId, binding.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(binding => binding.Capabilities).WithOne(capability => capability.Binding).HasForeignKey(capability => new { capability.TenantId, capability.RegistrationProviderBindingId }).HasPrincipalKey(binding => new { binding.TenantId, binding.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(binding => new { binding.TenantId, binding.RegistrationProviderConnectionId, binding.RegistrationFormVersionId }).IsUnique();
    }
}

public sealed class RegistrationProviderCapabilityConfiguration : IEntityTypeConfiguration<RegistrationProviderCapability>
{
    public void Configure(EntityTypeBuilder<RegistrationProviderCapability> builder)
    {
        builder.Property(capability => capability.Id).ValueGeneratedNever();
        builder.Property(capability => capability.ProviderCode).IsRequired().HasMaxLength(100);
        builder.Property(capability => capability.DeploymentKind).IsRequired().HasMaxLength(100);
        builder.Property(capability => capability.ApiVersion).IsRequired().HasMaxLength(100);
        builder.Property(capability => capability.AdapterPolicyVersion).IsRequired().HasMaxLength(100);
        builder.Property(capability => capability.ConformanceEvidenceRevision).IsRequired().HasMaxLength(200);
        builder.Property(capability => capability.CapabilityCode).IsRequired().HasMaxLength(100);
        builder.Ignore(capability => capability.TupleKey);
        builder.Property(capability => capability.IsDeleted).HasDefaultValue(false);
        builder.HasIndex(capability => new { capability.RegistrationProviderBindingId, capability.ProviderCode, capability.DeploymentKind, capability.ApiVersion, capability.AdapterPolicyVersion, capability.ConformanceEvidenceRevision, capability.CapabilityCode }).IsUnique();
    }
}

public sealed class RegistrationProviderFieldMappingConfiguration : IEntityTypeConfiguration<RegistrationProviderFieldMapping>
{
    public void Configure(EntityTypeBuilder<RegistrationProviderFieldMapping> builder)
    {
        builder.Property(mapping => mapping.Id).ValueGeneratedNever();
        builder.Property(mapping => mapping.PlatformFieldKey).IsRequired().HasMaxLength(200);
        builder.Property(mapping => mapping.ProviderFieldKey).IsRequired().HasMaxLength(200);
        builder.Property(mapping => mapping.IsDeleted).HasDefaultValue(false);
        builder.HasAlternateKey(mapping => new { mapping.TenantId, mapping.RegistrationProviderBindingId, mapping.Id });
        builder.HasIndex(mapping => new { mapping.RegistrationProviderBindingId, mapping.PlatformFieldKey }).IsUnique();
        builder.HasIndex(mapping => new { mapping.RegistrationProviderBindingId, mapping.ProviderFieldKey }).IsUnique();
    }
}

public sealed class RegistrationProviderOptionMappingConfiguration : IEntityTypeConfiguration<RegistrationProviderOptionMapping>
{
    public void Configure(EntityTypeBuilder<RegistrationProviderOptionMapping> builder)
    {
        builder.Property(mapping => mapping.Id).ValueGeneratedNever();
        builder.Property(mapping => mapping.PlatformOptionKey).IsRequired().HasMaxLength(200);
        builder.Property(mapping => mapping.ProviderOptionKey).IsRequired().HasMaxLength(200);
        builder.Property(mapping => mapping.IsDeleted).HasDefaultValue(false);
        builder.HasOne<RegistrationProviderFieldMapping>().WithMany().HasForeignKey(mapping => new { mapping.TenantId, mapping.RegistrationProviderBindingId, mapping.RegistrationProviderFieldMappingId }).HasPrincipalKey(mapping => new { mapping.TenantId, mapping.RegistrationProviderBindingId, mapping.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(mapping => new { mapping.RegistrationProviderFieldMappingId, mapping.PlatformOptionKey }).IsUnique();
    }
}

public sealed class RegistrationProviderSchemaRevisionConfiguration : IEntityTypeConfiguration<RegistrationProviderSchemaRevision>
{
    public void Configure(EntityTypeBuilder<RegistrationProviderSchemaRevision> builder)
    {
        builder.Property(revision => revision.Id).ValueGeneratedNever();
        builder.Property(revision => revision.RevisionHash).HasConversion(hash => hash.Value, value => RegistrationEvidenceHash.Create(value)).HasMaxLength(44).IsRequired();
        builder.Property(revision => revision.ProviderSurveyId).IsRequired().HasMaxLength(200);
        builder.Property(revision => revision.ProviderSurveyRevisionId).HasMaxLength(200);
        builder.Property(revision => revision.ProviderSnapshotJson).IsRequired().HasColumnType("text");
        builder.Property(revision => revision.ProviderSnapshotSha256Hash).IsRequired().HasMaxLength(64);
        builder.Property(revision => revision.CreatedAt).IsRequired();
        builder.Property(revision => revision.IsDeleted).HasDefaultValue(false);
        builder.HasAlternateKey(revision => new { revision.TenantId, revision.Id });
        builder.HasOne(revision => revision.Connection).WithMany().HasForeignKey(revision => new { revision.TenantId, revision.RegistrationProviderConnectionId }).HasPrincipalKey(connection => new { connection.TenantId, connection.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationProviderSchemaAuthority>().WithMany().HasForeignKey(revision => revision.SchemaAuthorityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationProviderDriftClass>().WithMany().HasForeignKey(revision => revision.DriftClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(revision => new { revision.TenantId, revision.RegistrationProviderConnectionId, revision.ProviderSurveyId, revision.RevisionHash }).IsUnique();
    }
}
