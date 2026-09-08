namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Explore.Domain.SetupLive;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class SetupTargetEnrollmentConfiguration :
    IEntityTypeConfiguration<SetupTargetEnrollment>
{
    public void Configure(EntityTypeBuilder<SetupTargetEnrollment> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_setup_target_enrollments_generation",
                "generation > 0");
            table.HasCheckConstraint(
                "ck_setup_target_enrollments_lifecycle",
                "expires_at > created_at AND (" +
                "(state = 1 AND revoked_at IS NULL AND expired_at IS NULL) OR " +
                "(state = 2 AND revoked_at IS NOT NULL AND expired_at IS NULL) OR " +
                "(state = 3 AND revoked_at IS NULL AND expired_at IS NOT NULL))");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.ChallengeDigest)
            .HasMaxLength(64).IsFixedLength().IsRequired()
            .UsePortableOrdinalAscii();
        builder.Property(value => value.CapabilityDigest)
            .HasMaxLength(64).IsFixedLength().IsRequired()
            .UsePortableOrdinalAscii();
        builder.Property(value => value.ScopeDigest)
            .HasMaxLength(64).IsFixedLength().IsRequired()
            .UsePortableOrdinalAscii();
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.Property(value => value.ExpiresAt).IsRequired();
        builder.Property(value => value.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(value => new
        {
            value.TenantId,
            value.Id,
            value.ActorId
        });
        builder.HasOne<Tenant>().WithMany()
            .HasForeignKey(value => value.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SetupEnrollmentIssuanceClaimConfiguration :
    IEntityTypeConfiguration<SetupEnrollmentIssuanceClaim>
{
    public void Configure(
        EntityTypeBuilder<SetupEnrollmentIssuanceClaim> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_setup_enrollment_claims_generation",
            "enrollment_generation > 0"));
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.RequestFingerprint)
            .HasMaxLength(64).IsFixedLength().IsRequired()
            .UsePortableOrdinalAscii();
        builder.Property(value => value.ClaimedAt).IsRequired();
        builder.HasIndex(value => new { value.TenantId, value.OperationKey })
            .IsUnique();
        builder.HasOne<SetupTargetEnrollment>().WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.EnrollmentId,
                value.ActorId
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
                value.ActorId
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SetupSecretBindingOperationConfiguration :
    IEntityTypeConfiguration<SetupSecretBindingOperation>
{
    public void Configure(
        EntityTypeBuilder<SetupSecretBindingOperation> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_setup_secret_operations_versions",
                "enrollment_generation > 0 AND commitment_key_version > 0");
            table.HasCheckConstraint(
                "ck_setup_secret_operations_binding",
                "binding_key IN ('setup.signing', 'setup.encryption')");
            table.HasCheckConstraint(
                "ck_setup_secret_operations_lifecycle",
                "(state = 1 AND outcome = 1 AND settled_at IS NULL) OR " +
                "(state = 2 AND outcome = 2 AND settled_at IS NOT NULL) OR " +
                "(state = 3 AND outcome IN (3, 4, 5, 7) AND settled_at IS NOT NULL) OR " +
                "(state = 4 AND outcome = 6 AND settled_at IS NOT NULL)");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.BindingKey)
            .HasMaxLength(32).IsRequired()
            .UsePortableOrdinalAscii();
        builder.Property(value => value.RequestFingerprint)
            .HasMaxLength(64).IsFixedLength().IsRequired()
            .UsePortableOrdinalAscii();
        builder.Property(value => value.SecretValueCommitment)
            .HasMaxLength(64).IsFixedLength().IsRequired()
            .UsePortableOrdinalAscii();
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.Property(value => value.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasIndex(value => new { value.TenantId, value.OperationKey })
            .IsUnique();
        builder.HasOne<SetupTargetEnrollment>().WithMany()
            .HasForeignKey(value => new
            {
                value.TenantId,
                value.EnrollmentId,
                value.ActorId
            })
            .HasPrincipalKey(value => new
            {
                value.TenantId,
                value.Id,
                value.ActorId
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
