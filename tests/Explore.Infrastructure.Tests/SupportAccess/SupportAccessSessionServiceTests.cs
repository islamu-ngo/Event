using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Explore.Infrastructure.Tests.SupportAccess;

public sealed class SupportAccessSessionServiceTests
{
    private static readonly Guid ActorUserId = Guid.NewGuid();
    private static readonly Guid TargetTenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();

    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly IAdminContext _adminContext = Substitute.For<IAdminContext>();
    private readonly ITenantContextAccessor _tenantContextAccessor = Substitute.For<ITenantContextAccessor>();
    private readonly IHierarchicalSettingsResolver _settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
    private readonly ISupportAccessSessionRepository _sessionRepository = Substitute.For<ISupportAccessSessionRepository>();
    private readonly SupportAccessSessionService _service;

    public SupportAccessSessionServiceTests()
    {
        _adminContext.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns(ActorUserId);
        _tenantContextAccessor.TenantId.Returns(TargetTenantId);
        _sessionRepository.GetActiveOwnedSessionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns((SupportAccessSession?)null);
        ConfigureSettings(enabled: true, allowWriteMode: true);

        _service = new SupportAccessSessionService(
            _httpContextAccessor,
            _adminContext,
            _tenantContextAccessor,
            _settingsResolver,
            _sessionRepository);
    }

    [Test]
    public async Task GetCurrentAsync_WithoutTrustedForwardedHeader_ReturnsInactiveAndSkipsRepository()
    {
        _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());

        var context = await _service.GetCurrentAsync();

        await Assert.That(context.IsActive).IsFalse();
        await _sessionRepository.DidNotReceive().GetActiveOwnedSessionAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetCurrentAsync_WithTrustedForwardedHeader_ReturnsValidatedContext()
    {
        var session = CreateSession(SupportAccessModeEnum.ReadOnly);
        ConfigureRepositorySession(session, TargetTenantId);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[SupportAccessHeaderNames.SessionId] = session.Id.ToString("D");
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var context = await _service.GetCurrentAsync();

        await Assert.That(context.IsActive).IsTrue();
        await Assert.That(context.SessionId).IsEqualTo((Guid?)session.Id);
        await Assert.That(context.TargetTenantId).IsEqualTo((Guid?)TargetTenantId);
        await Assert.That(context.AllowsWrites).IsFalse();
    }

    [Test]
    public async Task ValidateForwardedSessionAsync_ReadOnlySession_ReturnsActiveReadOnlyContext()
    {
        ConfigureSettings(enabled: true, allowWriteMode: false);
        var session = CreateSession(SupportAccessModeEnum.ReadOnly);
        ConfigureRepositorySession(session, TargetTenantId);

        var context = await _service.ValidateForwardedSessionAsync(session.Id, ActorUserId, TargetTenantId);

        await Assert.That(context.IsActive).IsTrue();
        await Assert.That(context.Mode).IsEqualTo((SupportAccessModeEnum?)SupportAccessModeEnum.ReadOnly);
        await Assert.That(context.AllowsWrites).IsFalse();
    }

    [Test]
    public async Task ValidateForwardedSessionAsync_WriteSession_WhenWriteModeAllowed_ReturnsWritableContext()
    {
        ConfigureSettings(enabled: true, allowWriteMode: true);
        var session = CreateSession(SupportAccessModeEnum.Write);
        ConfigureRepositorySession(session, TargetTenantId);

        var context = await _service.ValidateForwardedSessionAsync(session.Id, ActorUserId, TargetTenantId);

        await Assert.That(context.IsActive).IsTrue();
        await Assert.That(context.Mode).IsEqualTo((SupportAccessModeEnum?)SupportAccessModeEnum.Write);
        await Assert.That(context.AllowsWrites).IsTrue();
    }

    [Test]
    public async Task ValidateForwardedSessionAsync_WriteSession_WhenWriteModeDisabled_ReturnsInactive()
    {
        ConfigureSettings(enabled: true, allowWriteMode: false);
        var session = CreateSession(SupportAccessModeEnum.Write);
        ConfigureRepositorySession(session, TargetTenantId);

        var context = await _service.ValidateForwardedSessionAsync(session.Id, ActorUserId, TargetTenantId);

        await Assert.That(context.IsActive).IsFalse();
        await Assert.That(context.AllowsWrites).IsFalse();
        await Assert.That(context.WasForwarded).IsTrue();
    }

    [Test]
    public async Task ValidateForwardedSessionAsync_DisabledSupportAccess_SkipsRepositoryAndReturnsInactive()
    {
        ConfigureSettings(enabled: false, allowWriteMode: true);
        var sessionId = Guid.NewGuid();

        var context = await _service.ValidateForwardedSessionAsync(sessionId, ActorUserId, TargetTenantId);

        await Assert.That(context.IsActive).IsFalse();
        await Assert.That(context.WasForwarded).IsTrue();
        await _sessionRepository.DidNotReceive().GetActiveOwnedSessionAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ValidateForwardedSessionAsync_DifferentResolvedTenant_ReturnsInactive()
    {
        var session = CreateSession(SupportAccessModeEnum.ReadOnly);
        ConfigureRepositorySession(session, TargetTenantId);

        var context = await _service.ValidateForwardedSessionAsync(session.Id, ActorUserId, OtherTenantId);

        await Assert.That(context.IsActive).IsFalse();
        await Assert.That(context.WasForwarded).IsTrue();
        await _sessionRepository.Received(1).GetActiveOwnedSessionAsync(
            session.Id,
            ActorUserId,
            OtherTenantId,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments(SupportAccessSessionStatusEnum.Stopped)]
    [Arguments(SupportAccessSessionStatusEnum.Expired)]
    [Arguments(SupportAccessSessionStatusEnum.Revoked)]
    public async Task ValidateForwardedSessionAsync_TerminalSession_ReturnsInactiveForwarded(
        SupportAccessSessionStatusEnum terminalStatus)
    {
        var session = CreateSession(SupportAccessModeEnum.ReadOnly);
        var endedAtUtc = DateTimeOffset.UtcNow;
        switch (terminalStatus)
        {
            case SupportAccessSessionStatusEnum.Stopped:
                session.Stop(endedAtUtc);
                break;
            case SupportAccessSessionStatusEnum.Expired:
                session.Expire(endedAtUtc);
                break;
            case SupportAccessSessionStatusEnum.Revoked:
                session.Revoke(endedAtUtc, SupportAccessEndReasonEnum.RevokedByPolicy);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(terminalStatus), terminalStatus, null);
        }

        ConfigureRepositorySession(session, TargetTenantId);

        var context = await _service.ValidateForwardedSessionAsync(session.Id, ActorUserId, TargetTenantId);

        await Assert.That(context.IsActive).IsFalse();
        await Assert.That(context.WasForwarded).IsTrue();
    }

    private void ConfigureSettings(bool enabled, bool allowWriteMode)
    {
        _settingsResolver.ResolveGroupAsync<SupportAccessSettingGroup>(
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateSettings(enabled, allowWriteMode));
    }

    private void ConfigureRepositorySession(SupportAccessSession session, Guid resolvedTenantId)
    {
        _sessionRepository.GetActiveOwnedSessionAsync(
                session.Id,
                session.ActorUserId ?? throw new InvalidOperationException(),
                resolvedTenantId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(session);
    }

    private static SupportAccessSession CreateSession(SupportAccessModeEnum mode)
    {
        var now = DateTimeOffset.UtcNow;
        return SupportAccessSession.Start(
            ActorUserId,
            TargetTenantId,
            mode,
            "debugging",
            "Investigating a tenant support issue.",
            "SUP-1234",
            now.AddMinutes(-1),
            now.AddMinutes(30));
    }

    private static SupportAccessSettingGroup CreateSettings(bool enabled, bool allowWriteMode)
    {
        var group = new SupportAccessSettingGroup();
        group.Populate(new Dictionary<string, ResolvedSetting>
        {
            [GovernanceSettingKeys.SupportAccess.Enabled] = BooleanSetting(
                GovernanceSettingKeys.SupportAccess.Enabled,
                enabled),
            [GovernanceSettingKeys.SupportAccess.AllowWriteMode] = BooleanSetting(
                GovernanceSettingKeys.SupportAccess.AllowWriteMode,
                allowWriteMode)
        });
        return group;
    }

    private static ResolvedSetting BooleanSetting(string key, bool value) => new()
    {
        Key = key,
        Value = value ? "true" : "false",
        ValueType = SettingValueType.Boolean,
        Source = SettingSource.SystemDefault
    };
}
