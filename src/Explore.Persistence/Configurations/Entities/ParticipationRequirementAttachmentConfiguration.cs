using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ParticipationRequirementAttachmentConfiguration
    : IEntityTypeConfiguration<ParticipationRequirementAttachment>
{
    public void Configure(EntityTypeBuilder<ParticipationRequirementAttachment> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_participation_requirement_attachments_configuration_event",
                "event_id = participation_configuration_id");
            table.HasCheckConstraint(
                "ck_participation_requirement_attachments_questionnaire_form",
                "(is_standalone_questionnaire = true AND registration_form_id IS NOT NULL AND registration_form_version_id IS NOT NULL) OR " +
                "(is_standalone_questionnaire = false AND registration_form_id IS NULL AND registration_form_version_id IS NULL)");
        });
        builder.Property(attachment => attachment.Id).ValueGeneratedNever();
        builder.Property(attachment => attachment.CreatedAt).IsRequired();
        builder.Property(attachment => attachment.IsDeleted).HasDefaultValue(false);
        builder.Property(attachment => attachment.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne<EventParticipationConfiguration>()
            .WithMany(configuration => configuration.RequirementAttachments)
            .HasForeignKey(attachment => new { attachment.TenantId, attachment.ParticipationConfigurationId })
            .HasPrincipalKey(configuration => new { configuration.TenantId, configuration.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<RegistrationWorkflow>()
            .WithMany()
            .HasForeignKey(attachment => new
            {
                attachment.TenantId,
                attachment.EventId,
                attachment.RegistrationWorkflowId
            })
            .HasPrincipalKey(workflow => new { workflow.TenantId, workflow.EventId, workflow.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(attachment => attachment.RegistrationRequirement)
            .WithMany()
            .HasForeignKey(attachment => new
            {
                attachment.TenantId,
                attachment.EventId,
                attachment.RegistrationWorkflowId,
                attachment.RegistrationRequirementId
            })
            .HasPrincipalKey(requirement => new
            {
                requirement.TenantId,
                requirement.EventId,
                requirement.RegistrationWorkflowId,
                requirement.Id
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(attachment => attachment.RegistrationFormVersion)
            .WithMany()
            .HasForeignKey(attachment => new
            {
                attachment.TenantId,
                attachment.EventId,
                attachment.RegistrationFormId,
                attachment.RegistrationFormVersionId
            })
            .HasPrincipalKey(version => new
            {
                version.TenantId,
                version.EventId,
                version.RegistrationFormId,
                version.Id
            })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(attachment => new
        {
            attachment.ParticipationConfigurationId,
            attachment.RegistrationRequirementId
        })
            .IsUnique()
            .HasFilter("is_deleted = false");
        builder.HasIndex(attachment => new
        {
            attachment.ParticipationConfigurationId,
            attachment.IsStandaloneQuestionnaire
        })
            .IsUnique()
            .HasFilter("is_deleted = false AND is_standalone_questionnaire = true");
        builder.HasIndex(attachment => new { attachment.TenantId, attachment.EventId });
    }
}
