using System.Reflection;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Event.Api.IntegrationTests.Features;

public sealed class RegistrationFormAtomicReorderApiContractTests
{
    [Test]
    public async Task ReorderRoutes_AreCanonicalPutOperationsReturningAuthoritativeVersion()
    {
        var expected = new Dictionary<string, (string Route, string Name)>
        {
            [nameof(RegistrationFormsController.ReorderSections)] = (
                "registration-forms/{formId:guid}/versions/{versionId:guid}/sections/reorder",
                RouteNames.ReorderRegistrationFormSections),
            [nameof(RegistrationFormsController.ReorderFields)] = (
                "registration-forms/{formId:guid}/versions/{versionId:guid}/sections/{sectionId:guid}/fields/reorder",
                RouteNames.ReorderRegistrationFormFields)
        };

        foreach ((string actionName, (string route, string name)) in expected)
        {
            MethodInfo action = typeof(RegistrationFormsController).GetMethod(actionName)!;
            HttpPutAttribute attribute = action.GetCustomAttribute<HttpPutAttribute>()!;
            await Assert.That(attribute.Template).IsEqualTo(route);
            await Assert.That(attribute.Name).IsEqualTo(name);
            await Assert.That(action.ReturnType.ToString()).Contains("RegistrationFormVersionDto");
        }
    }

    [Test]
    public async Task DraftVersion_AdvertisesUpdateAuthorizedReorderAffordances()
    {
        var version = new RegistrationFormVersionDto(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            1, 1, "draft", "Draft", "en-US", null, null, null, null, null,
            Guid.CreateVersion7(), [], []);
        var section = new RegistrationFormSectionDto(Guid.CreateVersion7(), 1, "Details", Guid.CreateVersion7(), []);
        var policy = new RegistrationFormVersionLinkPolicy();

        LinkDefinition sectionOrder = policy.GetLinks(version, null)
            .Single(link => link.Rel == LinkRelations.ReorderSections);
        LinkDefinition fieldOrder = policy.GetSectionLinks(version, section)
            .Single(link => link.Rel == LinkRelations.ReorderFields);

        foreach (LinkDefinition link in new[] { sectionOrder, fieldOrder })
        {
            await Assert.That(link.Method).IsEqualTo(HttpMethods.Put);
            await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.RegistrationForm);
            await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.RegistrationForms.Update);
        }
    }
}
