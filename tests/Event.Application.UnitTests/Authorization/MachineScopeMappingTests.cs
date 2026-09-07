using Explore.Application.Authorization;
using Explore.Domain.Constants;

namespace Event.Application.UnitTests.Authorization;

public class MachineScopeMappingTests
{
    [Test]
    public async Task ScopesPermit_WithEmptyScopes_ReturnsFalse()
    {
        bool result = MachineScopeMapping.ScopesPermit([], ResourceKinds.Event, AuthorizationActions.View);
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ScopesPermit_WithAdminInstanceScope_AllowsEveryResource()
    {
        var scopes = new[] { ExternalApiKeyScopes.AdminInstance };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Event, AuthorizationActions.Delete)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.InstanceSetting, AuthorizationActions.Update)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.PlatformNamespace, AuthorizationActions.Create)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Tenant, AuthorizationActions.Update)).IsTrue();
    }

    [Test]
    public async Task ScopesPermit_WithAdminInstanceScope_DeniesAllOrganizerClaimActions()
    {
        var scopes = new[] { ExternalApiKeyScopes.AdminInstance };

        foreach (var action in new[]
        {
            AuthorizationActions.Events.ClaimOrganizer,
            AuthorizationActions.Events.WithdrawOrganizerClaim,
            AuthorizationActions.Events.ManagePublicActions,
            AuthorizationActions.Events.ViewOrganizerClaims,
            AuthorizationActions.Events.ReviewOrganizerClaim
        })
        {
            await Assert.That(MachineScopeMapping.ScopesPermit(
                scopes,
                ResourceKinds.EventOrganizerClaim,
                action)).IsFalse();
        }
    }

    [Test]
    public async Task ScopesPermit_WithEventsReadOnly_AllowsEventReadButDeniesWrite()
    {
        var scopes = new[] { ExternalApiKeyScopes.EventsRead };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Event, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Event, AuthorizationActions.Create)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Event, AuthorizationActions.Update)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Event, AuthorizationActions.Delete)).IsFalse();
    }

    [Test]
    public async Task ScopesPermit_WithEventsWrite_AllowsReadAndWriteForAllEventResources()
    {
        var scopes = new[] { ExternalApiKeyScopes.EventsWrite };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Event, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Event, AuthorizationActions.Update)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EventSession, AuthorizationActions.Create)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EventSessionAgendaItem, AuthorizationActions.Delete)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EventDay, AuthorizationActions.Update)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EventAgendaItem, AuthorizationActions.Delete)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EventContactShareConsent, AuthorizationActions.ViewSharedContacts)).IsTrue();
    }

    [Test]
    public async Task ScopesPermit_WithEventsWrite_AllowsStorageObjectWrites()
    {
        var scopes = new[] { ExternalApiKeyScopes.EventsWrite };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.StorageObject, AuthorizationActions.Create)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.StorageObject, AuthorizationActions.Delete)).IsTrue();
    }

    [Test]
    public async Task ScopesPermit_WithOrganizationsRead_AllowsOrgReadButDeniesWrite()
    {
        var scopes = new[] { ExternalApiKeyScopes.OrganizationsRead };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Organization, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.OrganizationMember, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.OrganizationReview, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Organization, AuthorizationActions.Create)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Organization, AuthorizationActions.ManageMembers)).IsFalse();
    }

    [Test]
    public async Task ScopesPermit_WithOrganizationsWrite_AllowsAllOrgActions()
    {
        var scopes = new[] { ExternalApiKeyScopes.OrganizationsWrite };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Organization, AuthorizationActions.Create)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Organization, AuthorizationActions.Delete)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.OrganizationMember, AuthorizationActions.ManageMembers)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.StorageObject, AuthorizationActions.Create)).IsTrue();
    }

    [Test]
    public async Task ScopesPermit_WithGroupsWrite_AllowsGroupMutations()
    {
        var scopes = new[] { ExternalApiKeyScopes.GroupsWrite };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Group, AuthorizationActions.Create)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.GroupMember, AuthorizationActions.ManageMembers)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Group, AuthorizationActions.Delete)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Organization, AuthorizationActions.Create)).IsFalse();
    }

    [Test]
    public async Task ScopesPermit_WithUsersRead_DeniesUserUpdates()
    {
        var scopes = new[] { ExternalApiKeyScopes.UsersRead };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.User, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.User, AuthorizationActions.Update)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.User, AuthorizationActions.Delete)).IsFalse();
    }

    [Test]
    public async Task ScopesPermit_WithUsersWrite_AllowsNotificationAccess()
    {
        var scopes = new[] { ExternalApiKeyScopes.UsersWrite };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Notification, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Notification, AuthorizationActions.Delete)).IsTrue();
    }

    [Test]
    public async Task ScopesPermit_WithUsersScopes_AllowsAiConversationSelfServiceActions()
    {
        var readScopes = new[] { ExternalApiKeyScopes.UsersRead };
        var writeScopes = new[] { ExternalApiKeyScopes.UsersWrite };

        await Assert.That(MachineScopeMapping.ScopesPermit(readScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(readScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.SendMessage)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(readScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.ConfirmAction)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(readScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.RejectAction)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(readScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.CancelRun)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(readScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.ProposeAction)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(writeScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.Create)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(writeScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.SendMessage)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(writeScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.ConfirmAction)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(writeScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.RejectAction)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(writeScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.CancelRun)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(writeScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.ProposeAction)).IsTrue();
    }

    [Test]
    public async Task ScopesPermit_WithMcpScopes_AllowsOnlyMcpConversationReadAndProposal()
    {
        var readScopes = new[] { ExternalApiKeyScopes.McpRead };
        var proposeScopes = new[] { ExternalApiKeyScopes.McpPropose };

        await Assert.That(MachineScopeMapping.ScopesPermit(readScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(readScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.ProposeAction)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(readScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.SendMessage)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(readScopes, ResourceKinds.Event, AuthorizationActions.View)).IsFalse();

        await Assert.That(MachineScopeMapping.ScopesPermit(proposeScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(proposeScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.ProposeAction)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(proposeScopes, ResourceKinds.AiConversation, AuthorizationActions.AiConversations.ConfirmAction)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(proposeScopes, ResourceKinds.Event, AuthorizationActions.Create)).IsFalse();
    }

    [Test]
    public async Task ScopesPermit_WithLookupsRead_AllowsLookupReadsButDeniesWrites()
    {
        var scopes = new[] { ExternalApiKeyScopes.LookupsRead };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Category, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Tag, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Location, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.LocationRoom, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Category, AuthorizationActions.Create)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Tag, AuthorizationActions.Delete)).IsFalse();
    }

    [Test]
    public async Task ScopesPermit_WithAdminTenant_AllowsAllTenantScopedResources()
    {
        var scopes = new[] { ExternalApiKeyScopes.AdminTenant };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Tenant, AuthorizationActions.Update)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.TenantSetting, AuthorizationActions.Update)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.TenantUserRoleGrant, AuthorizationActions.Create)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Category, AuthorizationActions.Create)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Actor, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EmailDispatch, AuthorizationActions.EmailDispatches.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EmailDispatch, AuthorizationActions.EmailDispatches.ManageTenant)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EmailDispatch, AuthorizationActions.EmailDispatches.Park)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EmailDispatch, AuthorizationActions.EmailDispatches.Replay)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EmailDispatch, AuthorizationActions.EmailDispatches.Resolve)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Webhook, AuthorizationActions.Webhooks.BulkReplay)).IsTrue();
    }

    [Test]
    public async Task ScopesPermit_WithNonAdminTenantScope_DeniesEmailDispatchOperations()
    {
        var scopes = new[] { ExternalApiKeyScopes.EventsWrite };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EmailDispatch, AuthorizationActions.EmailDispatches.View)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EmailDispatch, AuthorizationActions.EmailDispatches.ManageTenant)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EmailDispatch, AuthorizationActions.EmailDispatches.Park)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EmailDispatch, AuthorizationActions.EmailDispatches.Replay)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.EmailDispatch, AuthorizationActions.EmailDispatches.Resolve)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Webhook, AuthorizationActions.Webhooks.BulkReplay)).IsFalse();
    }

    [Test]
    public async Task ScopesPermit_WithAdminTenantOnly_DeniesInstanceScopedResources()
    {
        var scopes = new[] { ExternalApiKeyScopes.AdminTenant };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.InstanceSetting, AuthorizationActions.Update)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.AtprotoRecord, AuthorizationActions.Create)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.IndexedDid, AuthorizationActions.Create)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.PlatformNamespace, AuthorizationActions.View)).IsFalse();
    }

    [Test]
    public async Task ScopesPermit_WithApiKeysManageOnly_DeniesAllResourceKinds()
    {
        var scopes = new[] { ExternalApiKeyScopes.ApiKeysManage };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Event, AuthorizationActions.View)).IsFalse();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Organization, AuthorizationActions.Create)).IsFalse();
    }

    [Test]
    public async Task ScopesPermit_WithMultipleScopes_UnionsBehavior()
    {
        var scopes = new[]
        {
            ExternalApiKeyScopes.EventsRead,
            ExternalApiKeyScopes.OrganizationsRead,
            ExternalApiKeyScopes.GroupsRead
        };

        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Event, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Organization, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Group, AuthorizationActions.View)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Event, AuthorizationActions.Create)).IsFalse();
    }

    [Test]
    public async Task ScopesPermit_UnknownResourceKind_DeniesEvenWithAdminTenant()
    {
        var scopes = new[] { ExternalApiKeyScopes.AdminTenant };

        bool result = MachineScopeMapping.ScopesPermit(scopes, "synthetic:unknown", AuthorizationActions.View);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ScopesPermit_ScopeNameIsCaseInsensitive()
    {
        var scopes = new[] { "EVENTS:READ" };

        bool result = MachineScopeMapping.ScopesPermit(scopes, ResourceKinds.Event, AuthorizationActions.View);

        await Assert.That(result).IsTrue();
    }
}
