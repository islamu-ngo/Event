using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class UserAuthenticationTokenConfiguration : IEntityTypeConfiguration<UserAuthenticationToken>
{
    public void Configure(EntityTypeBuilder<UserAuthenticationToken> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_user_authentication_tokens_envelope_version",
                "envelope_version = 1");
            table.HasCheckConstraint(
                "ck_user_authentication_tokens_ciphertext_not_empty",
                "octet_length(session_ciphertext) >= 29");
            table.HasCheckConstraint(
                "ck_user_authentication_tokens_required_text",
                "length(btrim(provider)) > 0 AND length(btrim(subject_did)) > 0 AND length(btrim(encryption_key_id)) > 0 AND length(btrim(o_auth_client_key_id)) > 0");
        });

        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.Provider).HasMaxLength(500).IsRequired();
        builder.Property(e => e.SubjectDid).HasMaxLength(2048).IsRequired();
        builder.Property(e => e.SessionCiphertext).HasColumnType("bytea").IsRequired();
        builder.Property(e => e.EncryptionKeyId).HasMaxLength(128).IsRequired();
        builder.Property(e => e.OAuthClientKeyId).HasMaxLength(128).IsRequired();
        builder.Property(e => e.EnvelopeVersion).HasDefaultValue(1).IsRequired();
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
        builder.Property(e => e.PdsHost).HasMaxLength(2048);

        builder.HasIndex(e => new { e.TenantId, e.Provider, e.SubjectDid })
            .IsUnique();

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
