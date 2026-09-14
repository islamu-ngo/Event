using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.Tenants.Handlers.Commands.CreateTenantNavLink;
using Explore.Application.Features.Tenants.Handlers.Queries;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using NSubstitute;

namespace Event.Application.UnitTests.Profiles;

public sealed class TenantNavigationMapperTests
{
    private static readonly Guid TenantId = Guid.Parse("01990000-0000-7000-8000-000000000010");
    private static readonly Guid LinkId = Guid.Parse("01990000-0000-7000-8000-000000000011");

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("mdi-home")]
    public async Task QueryDisclosesDetachedNavigationScalars(string? icon)
    {
        var tenant = new Tenant { Id = TenantId, FullName = "Internal tenant", Slug = "internal", TenantStatus = null! };
        var link = new TenantNavigationLink
        {
            Id = LinkId, TenantId = TenantId, Tenant = tenant, Label = "Events",
            Url = "/events", Icon = icon, Order = 8, OpenInNewTab = true,
            CreatedAt = DateTime.UnixEpoch, CreatedBy = TenantId
        };
        tenant.NavigationLinks.Add(link);
        var rows = new List<TenantNavigationLink> { link };
        var repository = Substitute.For<ITenantNavigationLinkRepository>();
        repository.GetByTenantIdOrderedAsync(TenantId, default).Returns(rows);
        var handler = new GetTenantNavLinksQueryHandler(repository, Context());

        var result = await handler.QueryAsync(new(), default);
        link.Label = "Changed after projection";
        rows.Clear();

        var expected = new[] { new { Id = LinkId, Label = "Events", Url = "/events", Icon = icon, Order = 8, OpenInNewTab = true } };
        await Assert.That(JsonSerializer.Serialize(result)).IsEqualTo(JsonSerializer.Serialize(expected));
    }

    [Test]
    [Arguments(null, null)]
    [Arguments("", null)]
    [Arguments("  ", null)]
    [Arguments("  mdi-home  ", "mdi-home")]
    public async Task CreationKeepsTrustedOwnershipAndNormalizesOnlyAllowedFields(string? icon, string? expectedIcon)
    {
        var input = JsonSerializer.Deserialize<CreateTenantNavigationLinkDto>("""
            {
              "Label":"  Events  ","Url":"  /events  ","OpenInNewTab":true,
              "TenantId":"01990000-0000-7000-8000-000000000099",
              "Id":"01990000-0000-7000-8000-000000000098","Order":999,
              "IsActive":false,"IsDeleted":true,
              "CreatedBy":"01990000-0000-7000-8000-000000000097"
            }
            """)! with { Icon = icon };
        var repository = Substitute.For<ITenantNavigationLinkRepository>();
        repository.GetMaxOrderByTenantIdAsync(TenantId, default).Returns(7);
        TenantNavigationLink? stored = null;
        Guid inputId = default;
        repository.Create(Arg.Any<TenantNavigationLink>()).Returns(call =>
        {
            stored = call.Arg<TenantNavigationLink>();
            inputId = stored.Id;
            stored.Id = LinkId;
            return stored;
        });
        var handler = new CreateTenantNavLinkCommandHandler(repository, Context(), Settings());

        var result = await handler.ExecuteAsync(new() { NavigationLinkDto = input }, default);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsEqualTo(LinkId);
        await Assert.That(inputId).IsEqualTo(Guid.Empty);
        await Assert.That(stored!.TenantId).IsEqualTo(TenantId);
        await Assert.That(stored.Order).IsEqualTo(8);
        await Assert.That(stored.Label).IsEqualTo("Events");
        await Assert.That(stored.Url).IsEqualTo("/events");
        await Assert.That(stored.Icon).IsEqualTo(expectedIcon);
        await Assert.That(stored.OpenInNewTab).IsTrue();
        await Assert.That(stored.IsActive).IsTrue();
        await Assert.That(stored.IsDeleted).IsFalse();
        await Assert.That(stored.CreatedBy).IsNull();
    }

    [Test]
    [Arguments("http://example.invalid/events")]
    [Arguments("javascript:alert(1)")]
    public async Task InvalidUrlDoesNotCreateNavigation(string url)
    {
        var repository = Substitute.For<ITenantNavigationLinkRepository>();
        var stored = new List<TenantNavigationLink>();
        repository.Create(Arg.Any<TenantNavigationLink>()).Returns(call =>
        {
            var link = call.Arg<TenantNavigationLink>();
            stored.Add(link);
            return link;
        });
        var handler = new CreateTenantNavLinkCommandHandler(repository, Context(), Settings());

        var result = await handler.ExecuteAsync(new()
        {
            NavigationLinkDto = new() { Label = "Events", Url = url }
        }, default);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(stored.Count).IsEqualTo(0);
    }

    private static ITenantContext Context()
    {
        var context = Substitute.For<ITenantContext>();
        context.TenantId.Returns(TenantId);
        return context;
    }

    private static IHierarchicalSettingsResolver Settings()
    {
        var resolver = Substitute.For<IHierarchicalSettingsResolver>();
        resolver.ResolveAsync<bool>(
            GovernanceSettingKeys.Security.RequireHttpsExternalUrls,
            Arg.Any<SettingContext>(),
            default).Returns(true);
        return resolver;
    }
}
