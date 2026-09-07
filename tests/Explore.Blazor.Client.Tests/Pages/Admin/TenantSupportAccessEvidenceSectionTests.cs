using System.Text.Json;
using AngleSharp.Dom;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.SupportAccess;
using Explore.Blazor.Client.Models.Responses;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class TenantSupportAccessEvidenceSectionTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly BlazorTestContext _ctx;
    private readonly ITenantOnboardingService _tenantOnboardingService;
    private readonly IInstanceOnboardingService _instanceOnboardingService;
    private readonly ITenantPublicExperienceAdminService _publicExperienceAdminService;
    private readonly ITenantBrandingSettingsAdminService _brandingSettingsAdminService;
    private readonly ITenantDirectoryOperatorIdentityAdminService _directoryOperatorIdentityAdminService;
    private readonly ITenantStorageSettingsAdminService _storageSettingsAdminService;
    private readonly ISupportAccessClientService _supportAccessClientService;
    private readonly IAccessibilityAnnouncerService _announcer;

    public TenantSupportAccessEvidenceSectionTests()
    {
        _ctx = new BlazorTestContext();
        _ctx.AddShellStateMocks();
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Tenant Admin", "tenant-admin@example.com", _tenantId);

        _tenantOnboardingService = _ctx.AddMockService<ITenantOnboardingService>();
        _instanceOnboardingService = _ctx.AddMockService<IInstanceOnboardingService>();
        _publicExperienceAdminService = _ctx.AddMockService<ITenantPublicExperienceAdminService>();
        _brandingSettingsAdminService = _ctx.AddMockService<ITenantBrandingSettingsAdminService>();
        _directoryOperatorIdentityAdminService =
            _ctx.AddMockService<ITenantDirectoryOperatorIdentityAdminService>();
        _storageSettingsAdminService = _ctx.AddMockService<ITenantStorageSettingsAdminService>();
        _supportAccessClientService = _ctx.AddMockService<ISupportAccessClientService>();
        _announcer = Substitute.For<IAccessibilityAnnouncerService>();
        _announcer.AnnouncePoliteAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        _announcer.AnnounceAssertiveAsync(Arg.Any<string>()).Returns(Task.CompletedTask);

        _ctx.Services.RemoveAll<IAccessibilityAnnouncerService>();
        _ctx.Services.AddSingleton(_announcer);

        ConfigureDefaults();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task EvidenceSection_WithAuditHalLink_RendersAuditAffordanceAndLoadsEvents()
    {
        var sessionId = Guid.NewGuid();
        var session = CreateSessionResource(sessionId, withAuditLink: true, allowsWrites: true, isActive: true);
        var auditEvents = CreateAuditEventCollection(CreateAuditEvent(sessionId, "SessionStarted"));
        _supportAccessClientService.GetSessionsAsync(_tenantId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ServiceResult<HalCollectionResourceOfSupportAccessSessionDto>.Success(
                CreateSessionCollection(session)));
        _supportAccessClientService.GetAuditEventsAsync(_tenantId, sessionId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ServiceResult<HalCollectionResourceOfSupportAccessAuditEventDto>.Success(auditEvents));

        var cut = RenderEvidenceSection();
        cut.WaitForAssertion(() =>
        {
            if (FindAuditButtons(cut).Count != 1)
            {
                throw new InvalidOperationException("Audit affordance was not rendered from the HAL link.");
            }
        });

        await cut.InvokeAsync(() => FindAuditButtons(cut).Single().Click());

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("SessionStarted", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Audit event row was not rendered.");
            }
        });
        await _supportAccessClientService.Received(1)
            .GetAuditEventsAsync(_tenantId, sessionId, 100, Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnouncePoliteAsync("1 audit events loaded.");
    }

    [Test]
    public async Task EvidenceSection_WithoutAuditHalLink_DoesNotExposeAuditOrOperatorActions()
    {
        var session = CreateSessionResource(Guid.NewGuid(), withAuditLink: false, allowsWrites: true, isActive: true);
        _supportAccessClientService.GetSessionsAsync(_tenantId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ServiceResult<HalCollectionResourceOfSupportAccessSessionDto>.Success(
                CreateSessionCollection(session)));

        var cut = RenderEvidenceSection();
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("customer_support", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Support session row was not rendered.");
            }
        });

        await Assert.That(FindAuditButtons(cut)).IsEmpty();
        await Assert.That(cut.Markup).DoesNotContain("Start Support Access", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("Force-stop", StringComparison.OrdinalIgnoreCase);
        await _supportAccessClientService.DidNotReceive()
            .GetAuditEventsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TenantSettingsLayout_SupportEvidenceSection_IsReadOnlyAndRemovesSaveFooter()
    {
        _supportAccessClientService.GetSessionsAsync(_tenantId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ServiceResult<HalCollectionResourceOfSupportAccessSessionDto>.Success(
                new HalCollectionResourceOfSupportAccessSessionDto()));

        var cut = _ctx.RenderMudComponent<TenantAdminSettingsLayout>();
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Support Evidence", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Support Evidence navigation item was not rendered.");
            }
        });

        cut.FindComponents<MudListItem<string>>()
            .Single(item => item.Instance.Text == "Support Evidence")
            .Find("[role='option']")
            .Click();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Support Access Evidence", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Support access evidence section was not rendered.");
            }
        });
        await Assert.That(cut.Markup).DoesNotContain("Save Tenant Settings", StringComparison.OrdinalIgnoreCase);
    }

    private void ConfigureDefaults()
    {
        var tenantStatus = new TenantOnboardingStatusDto
        {
            IsCompleted = true,
            IsAuthenticated = true,
            IsCurrentUserTenantAdministrator = true,
            TenantId = _tenantId
        };
        tenantStatus.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
            new Dictionary<string, object>
            {
                ["manage-tenant-settings"] = new { href = "/api/tenant-onboarding/policy-settings" }
            });
        _tenantOnboardingService.GetStatusAsync()
            .Returns(Task.FromResult<TenantOnboardingStatusDto?>(tenantStatus));
        _tenantOnboardingService.GetManagementSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TenantPolicySettingsDto?>(new TenantPolicySettingsDto()));
        _instanceOnboardingService.GetStatusAsync()
            .Returns(Task.FromResult<InstanceOnboardingStatusDto?>(new InstanceOnboardingStatusDto
            {
                IsCompleted = true,
                IsAuthenticated = true,
                IsCurrentUserInstanceAdmin = false,
                SelectedDeploymentMode = "MultiTenant"
            }));
        _publicExperienceAdminService.ApplyAnnouncementBarSettingsAsync(Arg.Any<TenantPolicySettingsDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _publicExperienceAdminService.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TenantPublicExperienceAdminModel()));
        _brandingSettingsAdminService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TenantBrandingSettingsAdminModel { Exists = true, CanReplace = true }));
        _directoryOperatorIdentityAdminService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new TenantDirectoryOperatorIdentityAdminModel
            {
                Exists = true
            }));
        _storageSettingsAdminService.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HalResourceOfTenantStorageSettingsDto()));
        _supportAccessClientService.GetSessionsAsync(_tenantId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ServiceResult<HalCollectionResourceOfSupportAccessSessionDto>.Success(
                new HalCollectionResourceOfSupportAccessSessionDto()));
        _supportAccessClientService.GetAuditEventsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ServiceResult<HalCollectionResourceOfSupportAccessAuditEventDto>.Success(
                new HalCollectionResourceOfSupportAccessAuditEventDto()));
    }

    private IRenderedComponent<TenantSupportAccessEvidenceSection> RenderEvidenceSection() =>
        _ctx.RenderMudComponent<TenantSupportAccessEvidenceSection>();

    private HalResourceOfSupportAccessSessionDto CreateSessionResource(
        Guid sessionId,
        bool withAuditLink,
        bool allowsWrites,
        bool isActive)
    {
        Dictionary<string, HalLink> links = new(StringComparer.OrdinalIgnoreCase);
        if (withAuditLink)
        {
            links["audit-events"] = new HalLink
            {
                Href = $"/api/support-access/tenants/{_tenantId:D}/sessions/{sessionId:D}/audit-events",
                Method = HttpMethod.Get.Method,
                Title = "Support-access audit events"
            };
        }

        return new HalResourceOfSupportAccessSessionDto
        {
            Id = sessionId,
            TargetTenantId = _tenantId,
            IsActive = isActive,
            AllowsWrites = allowsWrites,
            ModeName = allowsWrites ? "Write" : "ReadOnly",
            StatusName = isActive ? "Active" : "Ended",
            ReasonCode = "customer_support",
            TicketReference = "SUP-123",
            StartedAtUtc = TestTime.UtcNow.AddMinutes(-10),
            ExpiresAtUtc = TestTime.UtcNow.AddMinutes(20),
            EndedAtUtc = isActive ? null : TestTime.UtcNow.AddMinutes(-1),
            _links = links
        };
    }

    private HalResourceOfSupportAccessAuditEventDto CreateAuditEvent(Guid sessionId, string eventTypeName)
    {
        return new HalResourceOfSupportAccessAuditEventDto
        {
            Id = Guid.NewGuid(),
            SupportAccessSessionId = sessionId,
            TargetTenantId = _tenantId,
            OccurredAtUtc = TestTime.UtcNow,
            EventTypeName = eventTypeName,
            RouteName = "ListTenantEvents",
            Action = "read",
            Outcome = "allowed",
            HttpStatusCode = 200,
            _links = new Dictionary<string, HalLink>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static HalCollectionResourceOfSupportAccessSessionDto CreateSessionCollection(
        HalResourceOfSupportAccessSessionDto session) => new()
        {
            _embedded = new HalCollectionEmbeddedOfSupportAccessSessionDto { Items = [session] },
            TotalCount = 1,
            PageSize = 100
        };

    private static HalCollectionResourceOfSupportAccessAuditEventDto CreateAuditEventCollection(
        HalResourceOfSupportAccessAuditEventDto auditEvent) => new()
        {
            _embedded = new HalCollectionEmbeddedOfSupportAccessAuditEventDto { Items = [auditEvent] },
            TotalCount = 1,
            PageSize = 100
        };

    private static IReadOnlyList<IElement> FindAuditButtons(IRenderedComponent<TenantSupportAccessEvidenceSection> cut) =>
        cut.FindAll("button")
            .Where(button => button.GetAttribute("aria-label")?.StartsWith("View audit events", StringComparison.Ordinal) == true)
            .ToList();

}
