using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Api.IntegrationTests.Builders;

/// <summary>
/// Builds <see cref="Tenant"/> instances for test data seeding.
/// Defaults to an active tenant with a unique slug.
/// </summary>
public sealed class TenantBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _fullName = "Test Tenant";
    private string _slug = $"test-tenant-{Guid.NewGuid().ToString("N")[..8]}";
    private string? _description;
    private int _tenantStatusId = (int)TenantStatusEnum.Active;

    public TenantBuilder WithId(Guid id) { _id = id; return this; }
    public TenantBuilder WithFullName(string fullName) { _fullName = fullName; return this; }
    public TenantBuilder WithSlug(string slug) { _slug = slug; return this; }
    public TenantBuilder WithDescription(string description) { _description = description; return this; }
    public TenantBuilder WithStatus(TenantStatusEnum status) { _tenantStatusId = (int)status; return this; }

    public Tenant Build() => new()
    {
        Id = _id,
        FullName = _fullName,
        Slug = _slug,
        Description = _description,
        TenantStatusId = _tenantStatusId,
        TenantStatus = null!
    };
}
