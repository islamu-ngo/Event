using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Tenants.Handlers.Queries;
using Explore.Application.Features.Tenants.Requests.Queries;
using Explore.Application.Mappings;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Profiles;

public sealed class TenantMapperTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ResponsesDiscloseOnlyPublicTenantScalars(bool detail)
    {
        var tenant = Tenant();
        object response = detail ? TenantMapper.ToDetail(tenant) : TenantMapper.ToListItem(tenant);
        var expected = new
        {
            tenant.Id,
            FullName = "Public tenant",
            Slug = "public-tenant",
            IsActive = true
        };

        await Assert.That(JsonSerializer.Serialize(response)).IsEqualTo(JsonSerializer.Serialize(expected));
    }

    [Test]
    [Arguments(null, true)]
    [Arguments(null, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task ActivityPreservesLoadedStatusAndNumericFallback(bool? loadedActive, bool fallbackActive)
    {
        var tenant = Tenant();
        tenant.TenantStatusId = (int)(fallbackActive ? TenantStatusEnum.Active : TenantStatusEnum.Suspended);
        tenant.TenantStatus = loadedActive is { } active
            ? new TenantStatus { Id = tenant.TenantStatusId, MasterCode = "loaded", FullName = "Loaded status", IsActiveState = active }
            : null!;

        await Assert.That(TenantMapper.ToDetail(tenant).IsActive).IsEqualTo(loadedActive ?? fallbackActive);
        await Assert.That(TenantMapper.ToListItem(tenant).IsActive).IsEqualTo(loadedActive ?? fallbackActive);
    }

    [Test]
    public async Task QueryHandlersPreserveMissingResultsAndDetachedSnapshots()
    {
        var tenant = Tenant();
        var rows = new List<Tenant> { tenant };
        var repository = Substitute.For<ITenantRepository>();
        repository.GetById(tenant.Id).Returns(tenant);
        repository.GetAll().Returns(rows);
        var detailHandler = new GetTenantDetailsRequestHandler(repository);
        var listHandler = new GetTenantListRequestHandler(repository);

        var detail = await detailHandler.Handle(new(tenant.Id), default);
        var missing = await detailHandler.Handle(new(Guid.Parse("01990000-0000-7000-8000-000000000099")), default);
        var list = await listHandler.Handle(new(), default);
        tenant.FullName = "Changed after projection";
        rows.Clear();

        await Assert.That(missing).IsNull();
        await Assert.That(detail.FullName).IsEqualTo("Public tenant");
        await Assert.That(list.Count).IsEqualTo(1);
        await Assert.That(list[0].Id).IsEqualTo(tenant.Id);
        await Assert.That(list[0].FullName).IsEqualTo("Public tenant");
    }

    private static Tenant Tenant()
    {
        var tenant = new Tenant
        {
            Id = Guid.Parse("01990000-0000-7000-8000-000000000001"),
            FullName = "Public tenant",
            Slug = "public-tenant",
            Description = "Not part of the scalar disclosure",
            TenantStatusId = (int)TenantStatusEnum.Suspended,
            TenantStatus = new TenantStatus { Id = (int)TenantStatusEnum.Suspended, MasterCode = "loaded", FullName = "Loaded status", IsActiveState = true },
            CreatedAt = DateTime.UnixEpoch,
            CreatedBy = Guid.Parse("01990000-0000-7000-8000-000000000002"),
            UpdatedAt = DateTime.UnixEpoch.AddDays(1),
            UpdatedBy = Guid.Parse("01990000-0000-7000-8000-000000000003")
        };
        tenant.NavigationLinks.Add(new TenantNavigationLink
        {
            Id = Guid.Parse("01990000-0000-7000-8000-000000000004"),
            TenantId = tenant.Id,
            Tenant = tenant,
            Label = "Not part of this response",
            Url = "/private-navigation"
        });
        return tenant;
    }
}
