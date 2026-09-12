using System.Reflection;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Event.Api.IntegrationTests.Features;

public sealed class GuestRegistrationCapabilityContractTests
{
    [Test]
    public async Task GuestRouteFamilies_HaveFiveIndependentConcreteOwners()
    {
        string[][] families =
        [
            [RouteNames.StartGuestRegistrationOrder, RouteNames.GetGuestRegistrationOrder,
                RouteNames.ContinueGuestRegistrationOrder, RouteNames.FinalizeGuestRegistrationOrder,
                RouteNames.CancelGuestRegistrationOrder],
            [RouteNames.LaunchGuestNativeRegistrationAttempt, RouteNames.GetGuestNativeRegistrationRequirementProgress,
                RouteNames.LaunchGuestRegistrationProviderAttempt, RouteNames.SkipGuestNativeRegistrationRequirement,
                RouteNames.SubmitGuestNativeRegistrationAttempt],
            [RouteNames.GetGuestRegistrationOrderParticipants, RouteNames.AddGuestRegistrationOrderParticipant,
                RouteNames.UpdateGuestRegistrationOrderParticipant, RouteNames.AssignGuestRegistrationOrderTickets,
                RouteNames.DeferGuestRegistrationOrderTickets],
            [RouteNames.ApplyGuestRegistrationOrderPromotion, RouteNames.RemoveGuestRegistrationOrderPromotion],
            [RouteNames.ClaimGuestRegistrationOrder]
        ];
        var actions = typeof(EventControllerBase).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>()
                    .Where(route => route.Name is not null)
                    .Select(route => (Name: route.Name!, Owner: type))))
            .Where(action => families.Any(family => family.Contains(action.Name)))
            .ToArray();
        await Assert.That(actions.Select(action => action.Name))
            .IsEquivalentTo(families.SelectMany(family => family));
        var owners = new List<Type>();
        foreach (string[] family in families)
        {
            Type[] familyOwners = actions.Where(action => family.Contains(action.Name))
                .Select(action => action.Owner).Distinct().ToArray();
            await Assert.That(familyOwners).HasSingleItem();
            owners.Add(familyOwners.Single());
        }
        await Assert.That(owners.Distinct().Count()).IsEqualTo(5);
    }
}
