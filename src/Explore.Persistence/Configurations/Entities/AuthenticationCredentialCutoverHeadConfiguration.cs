using Explore.Domain.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class AuthenticationCredentialCutoverHeadConfiguration
    : IEntityTypeConfiguration<AuthenticationCredentialCutoverHead>
{
    public void Configure(EntityTypeBuilder<AuthenticationCredentialCutoverHead> builder)
    {
        builder.ToTable("authentication_credential_cutover_heads");
        builder.HasKey(head => head.InstanceId);
        builder.Property(head => head.InstanceId).ValueGeneratedNever();
        builder.Property(head => head.ConcurrencyStamp).IsConcurrencyToken().ValueGeneratedNever();
        builder.HasOne<AuthenticationCredentialCutover>().WithMany()
            .HasForeignKey(head => new { head.InstanceId, head.CurrentOperationId })
            .HasPrincipalKey(operation => new { operation.InstanceId, operation.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
