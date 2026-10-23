namespace Event.Api.IntegrationTests.Features.Hateoas;

using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Ai;
using Explore.Application.Hateoas;
using Explore.Domain.Ai;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

public sealed class AiAssistantHateoasTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    [Test]
    public async Task DetailPolicy_WhenConversationActive_EmitsAuthenticatedSendLink()
    {
        var conversationId = Guid.CreateVersion7();
        var policy = new AiConversationDetailLinkPolicy();

        var links = policy.GetLinks(CreateDetail(conversationId, "Active"), AuthenticatedUser()).ToList();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        var send = links.Single(link => link.Rel == LinkRelations.SendMessage);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetAiConversation);
        await Assert.That(self.RequiresAuth).IsTrue();
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.AiConversation);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.AiConversations.View);
        await Assert.That(send.RouteName).IsEqualTo(RouteNames.SendAiMessage);
        await Assert.That(send.Method).IsEqualTo("POST");
        await Assert.That(send.RequiresAuth).IsTrue();
        await Assert.That(send.PermissionResourceKind).IsEqualTo(ResourceKinds.AiConversation);
        await Assert.That(send.PermissionAction).IsEqualTo(AuthorizationActions.AiConversations.SendMessage);
        // Send and read affordances describe the same conversation, so they must publish identical facts —
        // a divergence here would let one affordance be decided against a different owner.
        await Assert.That(send.PermissionFacts).IsNotNull();
        await Assert.That(send.PermissionFacts).IsEqualTo(self.PermissionFacts);
        await Assert.That(RouteValue(send.RouteValues, "conversationId")).IsEqualTo(conversationId);
    }

    [Test]
    public async Task DetailPolicy_WhenConversationNotActive_OmitsSendLink()
    {
        var policy = new AiConversationDetailLinkPolicy();

        var links = policy.GetLinks(CreateDetail(Guid.CreateVersion7(), "Blocked"), AuthenticatedUser()).ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.SendMessage)).IsFalse();
    }

    [Test]
    public async Task ProposedActionPolicy_WhenConversationActiveAndActionProposed_EmitsConfirmAndRejectLinks()
    {
        var conversationId = Guid.CreateVersion7();
        var proposedActionId = Guid.CreateVersion7();
        var conversation = CreateDetail(conversationId, "Active");
        var action = CreateProposedAction(proposedActionId, "Proposed");

        var links = AiProposedActionLinkPolicy.GetLinks(conversation, action, Now).ToList();

        var confirm = links.Single(link => link.Rel == LinkRelations.ConfirmAction);
        var reject = links.Single(link => link.Rel == LinkRelations.RejectAction);
        await Assert.That(confirm.RouteName).IsEqualTo(RouteNames.ConfirmAiProposedAction);
        await Assert.That(confirm.Method).IsEqualTo("POST");
        await Assert.That(confirm.RequiresAuth).IsTrue();
        await Assert.That(confirm.PermissionResourceKind).IsEqualTo(ResourceKinds.AiConversation);
        await Assert.That(confirm.PermissionAction).IsEqualTo(AuthorizationActions.AiConversations.ConfirmAction);
        await Assert.That(RouteValue(confirm.RouteValues, "conversationId")).IsEqualTo(conversationId);
        await Assert.That(RouteValue(confirm.RouteValues, "proposedActionId")).IsEqualTo(proposedActionId);
        await Assert.That(reject.RouteName).IsEqualTo(RouteNames.RejectAiProposedAction);
        await Assert.That(reject.Method).IsEqualTo("POST");
        await Assert.That(reject.RequiresAuth).IsTrue();
        await Assert.That(reject.PermissionAction).IsEqualTo(AuthorizationActions.AiConversations.RejectAction);
        await Assert.That(RouteValue(reject.RouteValues, "conversationId")).IsEqualTo(conversationId);
        await Assert.That(RouteValue(reject.RouteValues, "proposedActionId")).IsEqualTo(proposedActionId);
    }

    [Test]
    [Arguments("Executed")]
    [Arguments("Rejected")]
    [Arguments("Failed")]
    public async Task ProposedActionPolicy_WhenActionIsNotProposed_OmitsConfirmAndRejectLinks(string actionStatus)
    {
        var links = AiProposedActionLinkPolicy.GetLinks(
                CreateDetail(Guid.CreateVersion7(), "Active"),
                CreateProposedAction(Guid.CreateVersion7(), actionStatus), Now)
            .ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.ConfirmAction)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.RejectAction)).IsFalse();
    }

    [Test]
    public async Task ProposedActionPolicy_WhenConversationIsNotActive_OmitsConfirmAndRejectLinks()
    {
        var links = AiProposedActionLinkPolicy.GetLinks(
                CreateDetail(Guid.CreateVersion7(), "Archived"),
                CreateProposedAction(Guid.CreateVersion7(), "Proposed"), Now)
            .ToList();

        await Assert.That(links.Any(link => link.Rel == LinkRelations.ConfirmAction)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.RejectAction)).IsFalse();
    }

    [Test]
    [Arguments("expired")]
    [Arguments("future")]
    [Arguments("unimplemented")]
    public async Task ProposalWithoutLiveNativeExecutionOwnerDoesNotOfferConfirmation(string reason)
    {
        var action = CreateProposedAction(Guid.CreateVersion7(), "Proposed") with
        {
            CreatedAt = reason == "expired" ? Now.AddMinutes(-30).AddTicks(-1)
                : reason == "future" ? Now.AddTicks(1) : Now,
            Kind = reason == "unimplemented" ? "UpdateEventDraft" : "CreateEventDraft"
        };
        var links = AiProposedActionLinkPolicy.GetLinks(CreateDetail(Guid.CreateVersion7(), "Active"), action, Now).ToList();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.ConfirmAction)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.RejectAction)).IsTrue();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ConfirmedDraftRecoveryAffordanceRequiresExactPrincipalConfirmation(bool samePrincipal)
    {
        var conversation = CreateDetail(Guid.CreateVersion7(), "Active");
        var action = CreateProposedAction(Guid.CreateVersion7(), "Confirmed") with
        {
            ConfirmedBy = samePrincipal ? conversation.UserId : Guid.CreateVersion7(),
            ConfirmedAt = Now
        };
        var links = AiProposedActionLinkPolicy.GetLinks(conversation, action, Now).ToList();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.ConfirmAction)).IsEqualTo(samePrincipal);
        await Assert.That(links.Any(link => link.Rel == LinkRelations.RejectAction)).IsFalse();
    }

    public static IEnumerable<(AiProposedActionKind Kind, string Status)> ProposalOnlyStates()
    {
        foreach (var kind in Enum.GetValues<AiProposedActionKind>().Where(kind => kind != AiProposedActionKind.CreateEventDraft))
        {
            yield return (kind, "Proposed");
            yield return (kind, "Confirmed");
        }
    }

    [Test]
    [MethodDataSource(nameof(ProposalOnlyStates))]
    public async Task ProposalOnlyFamiliesNeverAdvertiseConfirmation(
        (AiProposedActionKind Kind, string Status) state)
    {
        var conversation = CreateDetail(Guid.CreateVersion7(), "Active");
        var action = CreateProposedAction(Guid.CreateVersion7(), state.Status) with
        {
            Kind = state.Kind.ToString(),
            ConfirmedBy = state.Status == "Confirmed" ? conversation.UserId : null,
            ConfirmedAt = state.Status == "Confirmed" ? Now : null
        };
        var links = AiProposedActionLinkPolicy.GetLinks(conversation, action, Now).ToList();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.ConfirmAction)).IsFalse();
        await Assert.That(links.Any(link => link.Rel == LinkRelations.RejectAction))
            .IsEqualTo(state.Status == "Proposed");
    }

    [Test]
    public async Task CollectionPolicy_EmitsAuthenticatedCreateAndActiveItemSendLinks()
    {
        var activeId = Guid.CreateVersion7();
        var archivedId = Guid.CreateVersion7();
        var policy = new AiConversationCollectionLinkPolicy();

        var activeLinks = policy.GetItemLinks(CreateSummary(activeId, "Active"), AuthenticatedUser()).ToList();
        var archivedLinks = policy.GetItemLinks(CreateSummary(archivedId, "Archived"), AuthenticatedUser()).ToList();
        var collectionLinks = policy.GetCollectionLinks(AuthenticatedUser()).ToList();

        var create = collectionLinks.Single(link => link.Rel == LinkRelations.Create);
        var send = activeLinks.Single(link => link.Rel == LinkRelations.SendMessage);
        await Assert.That(create.RouteName).IsEqualTo(RouteNames.CreateAiConversation);
        await Assert.That(create.Method).IsEqualTo("POST");
        await Assert.That(create.RequiresAuth).IsTrue();
        await Assert.That(create.PermissionResourceKind).IsEqualTo(ResourceKinds.AiConversation);
        await Assert.That(create.PermissionAction).IsEqualTo(AuthorizationActions.AiConversations.Create);
        await Assert.That(send.RequiresAuth).IsTrue();
        await Assert.That(send.PermissionAction).IsEqualTo(AuthorizationActions.AiConversations.SendMessage);
        await Assert.That(RouteValue(send.RouteValues, "conversationId")).IsEqualTo(activeId);
        await Assert.That(archivedLinks.Any(link => link.Rel == LinkRelations.SendMessage)).IsFalse();
    }

    [Test]
    public async Task AuthorizationEvaluator_WhenAnonymous_FailsClosedForAiAffordances()
    {
        var policy = new AiConversationCollectionLinkPolicy();
        var definitions = policy.GetItemLinks(CreateSummary(Guid.CreateVersion7(), "Active"), null)
            .Concat(policy.GetCollectionLinks(null))
            .ToList();
        var evaluator = new HateoasAuthorizationEvaluator(
            Substitute.For<IAuthorizationProvider>(),
            Substitute.For<Explore.Application.Contracts.Persistence.IEventRepository>(),
            Substitute.For<ITenantContext>(),
            Substitute.For<ILogger<HateoasAuthorizationEvaluator>>());

        var decisions = await evaluator.AreLinksAllowedAsync(definitions, new ClaimsPrincipal(new ClaimsIdentity()), new DefaultHttpContext());

        await Assert.That(decisions.Count).IsEqualTo(definitions.Count);
        await Assert.That(decisions.All(allowed => allowed == false)).IsTrue();
    }

    private static AiConversationDto CreateDetail(Guid id, string status) =>
        new()
        {
            Id = id,
            TenantId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            Status = status,
            CreatedAt = Now
        };

    private static AiConversationSummaryDto CreateSummary(Guid id, string status) =>
        new()
        {
            Id = id,
            TenantId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            Status = status,
            CreatedAt = Now
        };

    private static AiProposedActionDto CreateProposedAction(Guid id, string status) =>
        new()
        {
            Id = id,
            Kind = "CreateEventDraft",
            Version = new string('a', 64),
            Status = status,
            CreatedAt = Now
        };

    private static ClaimsPrincipal AuthenticatedUser() =>
        new(new ClaimsIdentity([new Claim("sub", Guid.CreateVersion7().ToString())], "test"));

    private static Guid RouteValue(object? routeValues, string key)
    {
        var value = routeValues?.GetType().GetProperty(key)?.GetValue(routeValues);
        return value is Guid guid ? guid : Guid.Empty;
    }
}
