using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Authorization;

public class EventRoleAuthorityCeilingServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("018f0000-0000-7000-8000-000000000101");
    private static readonly Guid EventId = Guid.Parse("018f0000-0000-7000-8000-000000000202");
    private static readonly Guid AssignerUserId = Guid.Parse("018f0000-0000-7000-8000-000000000303");
    private const string ApprovalPublishPermission = "event:approve-publish";

    private readonly IEventAuthoritySnapshotService _snapshotService;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRegistryService _permissionRegistry;
    private readonly EventRoleAuthorityCeilingService _service;

    public EventRoleAuthorityCeilingServiceTests()
    {
        _snapshotService = Substitute.For<IEventAuthoritySnapshotService>();
        _roleRepository = Substitute.For<IRoleRepository>();
        _permissionRegistry = Substitute.For<IPermissionRegistryService>();

        _service = new EventRoleAuthorityCeilingService(
            _snapshotService,
            _roleRepository,
            _permissionRegistry);

        _permissionRegistry.GetAllPermissionsAsync().Returns(Task.FromResult<IReadOnlyList<Permission>>(
            PermissionCodesFor(
                PermissionCodes.EventManageTeam,
                PermissionCodes.EventUpdate,
                PermissionCodes.EventRegistrationView,
                PermissionCodes.EventRegistrationManage,
                PermissionCodes.EventCheckInView,
                PermissionCodes.EventCheckInManage,
                PermissionCodes.EventManageOwner,
                PermissionCodes.EventTransferOwnership,
                PermissionCodes.EventDelete,
                PermissionCodes.EventManageFinance,
                ApprovalPublishPermission)));

        _roleRepository.GetByScopeAsync(RoleScopeEnum.Event).Returns(Task.FromResult<IReadOnlyList<Role>>(
            new[]
            {
                Role((int)RoleEnum.EventOwner, "event.owner", "Event Owner"),
                Role((int)RoleEnum.EventManager, "event.manager", "Event Manager"),
                Role((int)RoleEnum.RegistrationManager, "event.registration_manager", "Registration Manager"),
                Role((int)RoleEnum.CheckInStaff, "event.check_in_staff", "Check-In Staff")
            }));

        _roleRepository.GetByIdAsync((int)RoleEnum.EventOwner).Returns(Task.FromResult<Role?>(Role((int)RoleEnum.EventOwner, "event.owner", "Event Owner")));
        _roleRepository.GetByIdAsync((int)RoleEnum.EventManager).Returns(Task.FromResult<Role?>(Role((int)RoleEnum.EventManager, "event.manager", "Event Manager")));
        _roleRepository.GetByIdAsync((int)RoleEnum.RegistrationManager).Returns(Task.FromResult<Role?>(Role((int)RoleEnum.RegistrationManager, "event.registration_manager", "Registration Manager")));
        _roleRepository.GetByIdAsync((int)RoleEnum.CheckInStaff).Returns(Task.FromResult<Role?>(Role((int)RoleEnum.CheckInStaff, "event.check_in_staff", "Check-In Staff")));

        _roleRepository.GetPermissionsForRoleAsync((int)RoleEnum.EventOwner).Returns(Task.FromResult<IReadOnlyList<Permission>>(
            PermissionCodesFor(
                PermissionCodes.EventManageTeam,
                PermissionCodes.EventManageOwner,
                PermissionCodes.EventTransferOwnership,
                PermissionCodes.EventDelete,
                PermissionCodes.EventManageFinance)));

        _roleRepository.GetPermissionsForRoleAsync((int)RoleEnum.EventManager).Returns(Task.FromResult<IReadOnlyList<Permission>>(
            PermissionCodesFor(
                PermissionCodes.EventManageTeam,
                PermissionCodes.EventUpdate,
                PermissionCodes.EventRegistrationView,
                PermissionCodes.EventRegistrationManage,
                PermissionCodes.EventCheckInView,
                PermissionCodes.EventCheckInManage)));

        _roleRepository.GetPermissionsForRoleAsync((int)RoleEnum.RegistrationManager).Returns(Task.FromResult<IReadOnlyList<Permission>>(
            PermissionCodesFor(PermissionCodes.EventRegistrationView, PermissionCodes.EventRegistrationManage)));

        _roleRepository.GetPermissionsForRoleAsync((int)RoleEnum.CheckInStaff).Returns(Task.FromResult<IReadOnlyList<Permission>>(
            PermissionCodesFor(PermissionCodes.EventRegistrationView, PermissionCodes.EventCheckInView, PermissionCodes.EventCheckInManage)));
    }

    [Test]
    public async Task GetAssignableRolePresetsAsync_WithEventManagerAuthority_ReturnsSubordinatePresetsOnly()
    {
        ConfigureSnapshot(
            PermissionCodes.EventManageTeam,
            PermissionCodes.EventUpdate,
            PermissionCodes.EventRegistrationView,
            PermissionCodes.EventRegistrationManage,
            PermissionCodes.EventCheckInView,
            PermissionCodes.EventCheckInManage,
            PermissionCodes.EventManageOwner,
            PermissionCodes.EventTransferOwnership,
            PermissionCodes.EventDelete,
            PermissionCodes.EventManageFinance);

        var presets = await _service.GetAssignableRolePresetsAsync(TenantId, EventId, AssignerUserId, CancellationToken.None);

        await Assert.That(presets.Select(p => p.RoleId)).Contains((int)RoleEnum.EventManager);
        await Assert.That(presets.Select(p => p.RoleId)).Contains((int)RoleEnum.RegistrationManager);
        await Assert.That(presets.Select(p => p.RoleId)).Contains((int)RoleEnum.CheckInStaff);
        await Assert.That(presets.Select(p => p.RoleId)).DoesNotContain((int)RoleEnum.EventOwner);
    }

    [Test]
    public async Task CanAssignRoleAsync_ForEventOwner_DeniesBecauseOwnerPermissionsAreNonDelegable()
    {
        ConfigureSnapshot(
            PermissionCodes.EventManageTeam,
            PermissionCodes.EventManageOwner,
            PermissionCodes.EventTransferOwnership,
            PermissionCodes.EventDelete,
            PermissionCodes.EventManageFinance);

        var result = await _service.CanAssignRoleAsync(
            TenantId,
            EventId,
            AssignerUserId,
            (int)RoleEnum.EventOwner,
            CancellationToken.None);

        await Assert.That(result.IsAllowed).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(EventRoleAuthorityFailureCodes.AuthorityCeilingExceeded);
        await Assert.That(result.MissingPermissionCodes).Contains(PermissionCodes.EventManageOwner);
    }

    [Test]
    public async Task CanAssignRoleAsync_WhenRoleContainsApprovalPublish_DeniesPrivilegedAuthorityDelegation()
    {
        _roleRepository.GetPermissionsForRoleAsync((int)RoleEnum.EventManager).Returns(Task.FromResult<IReadOnlyList<Permission>>(
            PermissionCodesFor(
                PermissionCodes.EventManageTeam,
                PermissionCodes.EventUpdate,
                ApprovalPublishPermission)));
        ConfigureSnapshot(
            PermissionCodes.EventManageTeam,
            PermissionCodes.EventUpdate,
            ApprovalPublishPermission);

        var result = await _service.CanAssignRoleAsync(
            TenantId,
            EventId,
            AssignerUserId,
            (int)RoleEnum.EventManager,
            CancellationToken.None);

        await Assert.That(result.IsAllowed).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(EventRoleAuthorityFailureCodes.AuthorityCeilingExceeded);
        await Assert.That(result.MissingPermissionCodes).Contains(ApprovalPublishPermission);
    }

    [Test]
    public async Task CanAssignRoleAsync_WithoutManageTeam_DeniesAuthorityMissing()
    {
        ConfigureSnapshot(PermissionCodes.EventRegistrationView, PermissionCodes.EventCheckInManage);

        var result = await _service.CanAssignRoleAsync(
            TenantId,
            EventId,
            AssignerUserId,
            (int)RoleEnum.CheckInStaff,
            CancellationToken.None);

        await Assert.That(result.IsAllowed).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(EventRoleAuthorityFailureCodes.AuthorityMissing);
    }

    private void ConfigureSnapshot(params string[] permissionCodes)
    {
        var authority = new EventAuthorityForUser(
            new HashSet<string>(StringComparer.Ordinal),
            permissionCodes.ToHashSet(StringComparer.Ordinal),
            IsOwner: false,
            IsManager: true);

        _snapshotService.GetForUserAndEventsAsync(
                TenantId,
                AssignerUserId,
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new EventAuthoritySnapshot(
                TenantId,
                AssignerUserId,
                new Dictionary<Guid, EventAuthorityForUser> { [EventId] = authority })));
    }

    private static Role Role(int id, string masterCode, string fullName) =>
        new()
        {
            Id = id,
            MasterCode = masterCode,
            FullName = fullName,
            Scope = RoleScopeEnum.Event,
            IsSystem = true
        };

    private static IReadOnlyList<Permission> PermissionCodesFor(params string[] codes)
    {
        return codes.Select((code, index) => new Permission
        {
            Id = index + 1,
            MasterCode = code,
            FullName = code,
            ResourceKind = code.Split(':')[0],
            Action = code.Split(':')[1],
            GroupName = "Events",
            Scope = RoleScopeEnum.Event,
            IsSystem = true,
            IsActive = true
        }).ToArray();
    }
}
