using Explore.Application.Configuration;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Features.RegistrationForms;

public sealed class RegistrationFileAnswerPublicationGateTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, true)]
    public async Task Check_FileFieldPublicationMatchesDeploymentGate(bool enabled, bool expectedValid)
    {
        RegistrationFormVersion version = CreateVersionWithFileField();
        var service = new RegistrationFormPublishPreflightService(new RegistrationFileAnswerOptions
        {
            Enabled = enabled
        });

        var result = service.Check(version);

        await Assert.That(result.CanPublish).IsEqualTo(expectedValid);
        await Assert.That(result.Issues.Any(issue => issue.Code == "field.file_pipeline_disabled"))
            .IsEqualTo(!enabled);
    }

    private static RegistrationFormVersion CreateVersionWithFileField()
    {
        Guid tenantId = Guid.CreateVersion7();
        var form = RegistrationForm.Create(tenantId, Guid.CreateVersion7(), "native", "file-form", "File form", DateTime.UtcNow);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, DateTime.UtcNow);
        var section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Documents", DateTime.UtcNow);
        version.AddSection(section);
        version.AddField(section, RegistrationFormField.Create(
            Guid.CreateVersion7(), section, 1, "native", "document", "Document",
            RegistrationFieldTypeEnum.File, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, false, DateTime.UtcNow));
        return version;
    }
}
