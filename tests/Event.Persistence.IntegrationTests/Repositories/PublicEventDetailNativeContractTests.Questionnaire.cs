using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class PublicEventDetailNativeContractTests
{
    [Test]
    [Arguments("visible")]
    [Arguments("retired")]
    [Arguments("version-deleted")]
    [Arguments("requirement-deleted")]
    [Arguments("attachment-deleted")]
    [Arguments("hidden")]
    [Arguments("other-mode")]
    public async Task RuntimeQuestionnaireArtifactsRequireCurrentPublicWalkInSources(string state)
    {
        var seeded = await SeedAsync();
        DateTime now = new(2040, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        Guid versionId;
        string schemaHash;
        await using (var writer = fixture.CreateDbContext())
        {
            var configuration = EventParticipationConfiguration.Create(
                seeded.Event, seeded.Tenant, (int)ParticipationHandlingModeEnum.WalkIn,
                (int)AdvanceRegistrationObligationEnum.NotApplicable, null, null, now);
            var workflow = RegistrationWorkflow.Create(seeded.Tenant, seeded.Event, "registration", now);
            var requirement = RegistrationRequirement.Create(
                workflow, 1, RegistrationRequirementCriticalityEnum.Informational, true,
                RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect,
                RegistrationAnswerSyncModeEnum.NONE, RegistrationRequirementSubjectTypeEnum.AllOrders, null, now);
            requirement.AddChannel(RegistrationChannel.Create(requirement, 1, true, null, now));
            workflow.AddRequirement(requirement);
            var form = RegistrationForm.Create(
                seeded.Tenant, seeded.Event, "platform.registration",
                Guid.CreateVersion7().ToString("N"), "Public questionnaire", now);
            var version = RegistrationFormVersion.Create(form, 1, "en", null, null, now);
            version.AddSection(RegistrationFormSection.Create(
                Guid.CreateVersion7(), version, 1, "Questions", now));
            version.PinGeneratedSchemaBundle(
                $"{{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"versionId\":\"{version.Id:D}\",\"version\":1,\"languageTag\":\"en\",\"data\":{{}},\"ui\":{{}},\"logic\":{{}},\"mapping\":{{}}}}",
                now.AddMinutes(1));
            var attachment = configuration.AttachRequirement(
                Guid.CreateVersion7(), workflow, requirement, version, true, now.AddMinutes(2));
            writer.AddRange(configuration, workflow, form, version);
            await writer.SaveChangesAsync();
            versionId = version.Id;
            schemaHash = version.SchemaHash!;
            switch (state)
            {
                case "visible":
                    break;
                case "retired":
                    version.Retire(now.AddMinutes(3));
                    break;
                case "version-deleted":
                    version.IsDeleted = true;
                    break;
                case "requirement-deleted":
                    requirement.IsDeleted = true;
                    break;
                case "attachment-deleted":
                    configuration.DetachRequirement(requirement.Id, now.AddMinutes(3));
                    break;
                case "hidden":
                    (await writer.Events.SingleAsync(value => value.Id == seeded.Event))
                        .VisibilityTypeId = (int)VisibilityTypeEnum.Private;
                    break;
                case "other-mode":
                    configuration.DetachRequirement(requirement.Id, now.AddMinutes(3));
                    configuration.Reconfigure((int)ParticipationHandlingModeEnum.InformationOnly,
                        (int)AdvanceRegistrationObligationEnum.NotApplicable, null, null);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state));
            }
            await writer.SaveChangesAsync();
        }

        await using var reader = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        var detail = await ReadAsync(reader, seeded.Event);
        var questionnaire = detail?.ParticipationConfiguration?.RequirementAttachments.SingleOrDefault();
        if (state == "visible")
        {
            await Assert.That(questionnaire).IsNotNull();
            await Assert.That(questionnaire!.RegistrationFormVersion!.Id).IsEqualTo(versionId);
            await Assert.That(questionnaire.RegistrationFormVersion.SchemaHash).IsEqualTo(schemaHash);
            await Assert.That(questionnaire.RegistrationFormVersion.DataSchemaArtifact).IsEqualTo("{}");
            await Assert.That(questionnaire.RegistrationRequirement!.CompletionEffectId)
                .IsEqualTo((int)RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect);
            await Assert.That(questionnaire.RegistrationRequirement.Channels.Count).IsEqualTo(0);
        }
        else
        {
            await Assert.That(questionnaire).IsNull();
        }
        await Assert.That(reader.ChangeTracker.Entries().Count()).IsEqualTo(0);
        string table = new HumanPrincipalSourceSql(reader).Table(typeof(RegistrationFormVersion));
        await reader.Database.OpenConnectionAsync();
        await using var command = reader.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT * FROM {table} LIMIT 1";
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync());
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
    }
}
