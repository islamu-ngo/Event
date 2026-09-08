using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class CustomPropertyAdminServiceTests
{
    [Test]
    public async Task GetEventProjectionStatusAsync_PreservesStatusHalLinks()
    {
        var projectionClient = Substitute.For<ICustomPropertyProjectionAdminClient>();
        var definitionClient = Substitute.For<ICustomPropertyDefinitionClient>();
        var governanceClient = Substitute.For<ICustomPropertyGovernanceClient>();
        var tenantId = Guid.NewGuid();
        projectionClient.GetCustomPropertyProjectionStatusAsync(tenantId, null, null, Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfProjectionStatusDto
            {
                _embedded = new HalCollectionEmbeddedOfProjectionStatusDto
                {
                    Items =
                    [
                        new HalResourceOfProjectionStatusDto
                        {
                            ProjectionName = "event_custom_property_projection",
                            ProjectionVersion = 1,
                            TenantId = tenantId,
                            RowsProcessed = 7,
                            _links = CreateLinks(
                                new HalResourceOfProjectionStatusDto()._links,
                                ("rebuild", "/rebuild"),
                                ("drain-dirty-scopes", "/drain"))
                        }
                    ]
                }
            });
        var service = new CustomPropertyAdminService(definitionClient, governanceClient, projectionClient, NullLogger<CustomPropertyAdminService>.Instance);

        var result = await service.GetEventProjectionStatusAsync(tenantId);

        var status = result.Single();
        await Assert.That(status.RowsProcessed).IsEqualTo(7);
        await Assert.That(status._links?.ContainsKey("rebuild") == true).IsTrue();
        await Assert.That(status._links?.ContainsKey("drain-dirty-scopes") == true).IsTrue();
        await Assert.That(status._links?.ContainsKey("missing") == true).IsFalse();
    }

    [Test]
    public async Task GetDirtyScopesAsync_PreservesItemHalLinksAndPagination()
    {
        var projectionClient = Substitute.For<ICustomPropertyProjectionAdminClient>();
        var definitionClient = Substitute.For<ICustomPropertyDefinitionClient>();
        var governanceClient = Substitute.For<ICustomPropertyGovernanceClient>();
        var tenantId = Guid.NewGuid();
        projectionClient.GetCustomPropertyProjectionDirtyScopesAsync(tenantId, "projection", 2, 10, null, null, Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfProjectionDirtyScopeDto
            {
                PageNumber = 2,
                PageSize = 10,
                TotalCount = 1,
                _embedded = new HalCollectionEmbeddedOfProjectionDirtyScopeDto
                {
                    Items =
                    [
                        new HalResourceOfProjectionDirtyScopeDto
                        {
                            Id = 42,
                            TenantId = tenantId,
                            ProjectionName = "projection",
                            ScopeType = 1,
                            ScopeId = Guid.NewGuid(),
                            Reason = "retry",
                            _links = CreateLinks(
                                new HalResourceOfProjectionDirtyScopeDto()._links,
                                ("drain", "/drain"))
                        }
                    ]
                }
            });
        var service = new CustomPropertyAdminService(definitionClient, governanceClient, projectionClient, NullLogger<CustomPropertyAdminService>.Instance);

        var result = await service.GetDirtyScopesAsync(tenantId, "projection", 2, 10);

        await Assert.That(result.PageNumber).IsEqualTo(2);
        await Assert.That(result.TotalCount).IsEqualTo(1);
        var scope = result.Items.Single();
        await Assert.That(scope.Id).IsEqualTo(42);
        await Assert.That(scope._links?.ContainsKey("drain") == true).IsTrue();
    }

    private static IDictionary<string, TLink> CreateLinks<TLink>(
        IDictionary<string, TLink>? _,
        params (string Rel, string Href)[] links)
        where TLink : class, new()
    {
        var hrefProperty = typeof(TLink).GetProperty("Href");
        if (hrefProperty is null)
        {
            throw new InvalidOperationException($"Link type '{typeof(TLink).Name}' does not expose an Href property.");
        }

        var result = new Dictionary<string, TLink>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rel, href) in links)
        {
            var link = new TLink();
            hrefProperty.SetValue(link, href);
            result[rel] = link;
        }

        return result;
    }
}
