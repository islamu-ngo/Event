using Explore.Application.Features.RegistrationForms.Requests.Commands;
using Explore.Application.Features.RegistrationForms.Requests.Queries;

namespace Event.Application.UnitTests.Features.RegistrationForms;

public sealed class RegistrationFormAuthoringContractTests
{
    [Test]
    public async Task AuthoringSurface_ProvidesEveryNamedOperation()
    {
        Type[] operations =
        [
            typeof(CreateRegistrationWorkflowCommand),
            typeof(UpdateRegistrationWorkflowCommand),
            typeof(CreateRegistrationRequirementCommand),
            typeof(UpdateRegistrationRequirementCommand),
            typeof(DeleteRegistrationRequirementCommand),
            typeof(CreateRegistrationFormCommand),
            typeof(CreateRegistrationFormVersionCommand),
            typeof(AddRegistrationFormSectionCommand),
            typeof(UpdateRegistrationFormSectionCommand),
            typeof(DeleteRegistrationFormSectionCommand),
            typeof(AddRegistrationFormFieldCommand),
            typeof(UpdateRegistrationFormFieldCommand),
            typeof(DeleteRegistrationFormFieldCommand),
            typeof(AddRegistrationFormFieldOptionCommand),
            typeof(UpdateRegistrationFormFieldOptionCommand),
            typeof(RetireRegistrationFormFieldOptionCommand),
            typeof(AddRegistrationFormRuleCommand),
            typeof(UpdateRegistrationFormRuleCommand),
            typeof(DeleteRegistrationFormRuleCommand),
            typeof(PublishRegistrationFormVersionCommand),
            typeof(GetRegistrationWorkflowQuery),
            typeof(GetRegistrationFormQuery),
            typeof(GetRegistrationFormVersionQuery),
            typeof(GetRegistrationFormPublishPreflightQuery)
        ];

        await Assert.That(operations.Select(type => type.Name).Distinct().Count()).IsEqualTo(operations.Length);
    }
}
