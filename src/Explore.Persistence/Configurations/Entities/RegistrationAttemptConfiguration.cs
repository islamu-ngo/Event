using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationAttemptConfiguration : IEntityTypeConfiguration<RegistrationAttempt>
{
    public void Configure(EntityTypeBuilder<RegistrationAttempt> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_registration_attempts_provider_pair",
                "(registration_provider_binding_id IS NULL) = (provider_mapping_revision_hash IS NULL)");
            table.HasCheckConstraint("ck_registration_attempts_provider_key",
                "(registration_provider_binding_id IS NULL AND registration_provider_binding_key = '00000000-0000-0000-0000-000000000000') OR " +
                "(registration_provider_binding_id IS NOT NULL AND registration_provider_binding_key = registration_provider_binding_id)");
            table.HasCheckConstraint("ck_registration_attempts_expiry", "expires_at > created_at");
            table.HasCheckConstraint("ck_registration_attempts_supersession",
                $"(status_id = {(int)RegistrationAttemptStatusEnum.Superseded} AND superseded_at IS NOT NULL AND superseded_by_registration_attempt_id IS NOT NULL AND supersession_reason IS NOT NULL) OR " +
                $"(status_id <> {(int)RegistrationAttemptStatusEnum.Superseded} AND superseded_at IS NULL AND superseded_by_registration_attempt_id IS NULL AND supersession_reason IS NULL)");
            table.HasCheckConstraint("ck_registration_attempts_consumption",
                $"(status_id = {(int)RegistrationAttemptStatusEnum.Consumed} AND consumed_at IS NOT NULL) OR " +
                $"(status_id <> {(int)RegistrationAttemptStatusEnum.Consumed} AND consumed_at IS NULL AND submission_consumption_claim_id IS NULL)");
        });
        builder.Property(attempt => attempt.Id).ValueGeneratedNever();
        var providerBindingKey = builder.Property<Guid>("RegistrationProviderBindingKey");
        providerBindingKey
            .HasColumnType("uuid")
            .HasComputedColumnSql(
                "COALESCE(registration_provider_binding_id, '00000000-0000-0000-0000-000000000000'::uuid)",
                stored: true)
            .ValueGeneratedOnAdd()
            .IsRequired();
        providerBindingKey.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        providerBindingKey.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(attempt => attempt.CapabilityTokenHash)
            .HasConversion(hash => hash.Value, value => CapabilityTokenHash.Create(value)).HasMaxLength(44).IsRequired();
        builder.Property(attempt => attempt.ProviderMappingRevisionHash)
            .HasConversion(hash => hash == null ? null : hash.Value,
                value => value == null ? null : RegistrationEvidenceHash.Create(value)).HasMaxLength(44);
        var providerMappingRevisionHashKey = builder.Property<string>("ProviderMappingRevisionHashKey");
        providerMappingRevisionHashKey
            .HasComputedColumnSql("COALESCE(provider_mapping_revision_hash, '')", stored: true)
            .HasMaxLength(44)
            .ValueGeneratedOnAdd()
            .IsRequired();
        providerMappingRevisionHashKey.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        providerMappingRevisionHashKey.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        builder.Property(attempt => attempt.SupersessionReason).HasMaxLength(500);
        builder.Property(attempt => attempt.CreatedAt).IsRequired();
        builder.Property(attempt => attempt.IsDeleted).HasDefaultValue(false);
        builder.Property(attempt => attempt.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(attempt => new { attempt.TenantId, attempt.Id });
        builder.HasAlternateKey(attempt => new
        {
            attempt.TenantId,
            attempt.EventId,
            attempt.RegistrationOrderId,
            attempt.RegistrationWorkflowId,
            attempt.RegistrationRequirementId,
            attempt.RegistrationChannelId,
            attempt.RegistrationFormId,
            attempt.RegistrationFormVersionId,
            attempt.Id
        });
        builder.HasAlternateKey(
            nameof(RegistrationAttempt.TenantId),
            nameof(RegistrationAttempt.EventId),
            nameof(RegistrationAttempt.RegistrationOrderId),
            nameof(RegistrationAttempt.RegistrationWorkflowId),
            nameof(RegistrationAttempt.RegistrationRequirementId),
            nameof(RegistrationAttempt.Id));
        builder.HasOne<Tenant>().WithMany().HasForeignKey(attempt => attempt.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany().HasForeignKey(attempt => new { attempt.TenantId, attempt.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationWorkflow>().WithMany()
            .HasForeignKey(attempt => new { attempt.TenantId, attempt.EventId, attempt.RegistrationWorkflowId })
            .HasPrincipalKey(workflow => new { workflow.TenantId, workflow.EventId, workflow.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationRequirement>().WithMany()
            .HasForeignKey(attempt => new { attempt.TenantId, attempt.EventId, attempt.RegistrationWorkflowId, attempt.RegistrationRequirementId })
            .HasPrincipalKey(requirement => new { requirement.TenantId, requirement.EventId, requirement.RegistrationWorkflowId, requirement.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationForm>().WithMany()
            .HasForeignKey(attempt => new { attempt.TenantId, attempt.EventId, attempt.RegistrationFormId })
            .HasPrincipalKey(form => new { form.TenantId, form.EventId, form.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationFormVersion>().WithMany()
            .HasForeignKey(attempt => new { attempt.TenantId, attempt.EventId, attempt.RegistrationFormId, attempt.RegistrationFormVersionId })
            .HasPrincipalKey(version => new { version.TenantId, version.EventId, version.RegistrationFormId, version.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(attempt => attempt.Status).WithMany().HasForeignKey(attempt => attempt.StatusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationProviderBinding>().WithMany()
            .HasForeignKey(nameof(RegistrationAttempt.TenantId), nameof(RegistrationAttempt.RegistrationProviderBindingId), "ProviderMappingRevisionHashKey")
            .HasPrincipalKey(nameof(RegistrationProviderBinding.TenantId), nameof(RegistrationProviderBinding.Id), nameof(RegistrationProviderBinding.PublishedMappingRevisionHashKey))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationAttempt>().WithMany()
            .HasForeignKey(attempt => new
            {
                attempt.TenantId,
                attempt.EventId,
                attempt.RegistrationOrderId,
                attempt.RegistrationWorkflowId,
                attempt.RegistrationRequirementId,
                attempt.SupersededByRegistrationAttemptId
            })
            .HasPrincipalKey(replacement => new
            {
                replacement.TenantId,
                replacement.EventId,
                replacement.RegistrationOrderId,
                replacement.RegistrationWorkflowId,
                replacement.RegistrationRequirementId,
                replacement.Id
            }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(attempt => new { attempt.TenantId, attempt.CapabilityTokenHash }).IsUnique();
        builder.HasIndex(attempt => new { attempt.TenantId, attempt.StatusId, attempt.ExpiresAt });
    }
}
