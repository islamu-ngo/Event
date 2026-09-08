using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.RegistrationOrders.Handlers.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using MediatR;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace ApplicationUnitTests;

public sealed class TicketPurchaseAccessCommandHandlerTests
{
    private static readonly DateTime UtcNow =
        new(
            2026,
            8,
            27,
            12,
            0,
            0,
            DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.CreateVersion7();
    private readonly Guid _eventId = Guid.CreateVersion7();
    private readonly Guid _orderId = Guid.CreateVersion7();
    private readonly Guid _accountId = Guid.CreateVersion7();
    private readonly IRegistrationInventoryRepository _inventory =
        Substitute.For<IRegistrationInventoryRepository>();
    private readonly ITicketPurchaseGovernanceRepository _governance =
        Substitute.For<ITicketPurchaseGovernanceRepository>();
    private readonly ICurrentUserService _currentUser =
        Substitute.For<ICurrentUserService>();
    private readonly ITenantContext _tenant =
        Substitute.For<ITenantContext>();
    private readonly IGuestCapabilityTokenService _capabilities =
        Substitute.For<IGuestCapabilityTokenService>();
    private readonly IMediator _mediator =
        Substitute.For<IMediator>();

    public TicketPurchaseAccessCommandHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_accountId);
    }

    [Test]
    public async Task AuthenticatedAccessSelectsCurrentTenantEventPolicy()
    {
        RegistrationOrder order =
            CreateOrder(_accountId, guestHash: null);
        TicketPurchasePolicyVersion currentPolicy =
            CreatePolicy();
        ReserveTicketPurchaseCommand? forwarded = null;
        _inventory.GetOrderWithLinesAsync(
                _orderId,
                _tenantId,
                Arg.Any<CancellationToken>())
            .Returns(order);
        _governance.GetCurrentPolicyVersionAsync(
                _tenantId,
                _eventId,
                Arg.Any<CancellationToken>())
            .Returns(currentPolicy);
        _mediator.Send(
                Arg.Any<ReserveTicketPurchaseCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                forwarded =
                    call.ArgAt<ReserveTicketPurchaseCommand>(0);
                return BaseCommandResponse.Success(
                    _orderId,
                    "Reserved.");
            });
        var handler =
            new ReserveAuthenticatedTicketPurchaseCommandHandler(
                _inventory,
                _governance,
                _currentUser,
                _tenant,
                _mediator);

        BaseCommandResponse<Guid> result = await handler.Handle(
            new ReserveAuthenticatedTicketPurchaseCommand(
                _eventId,
                _orderId,
                Guid.CreateVersion7(),
                "operation"),
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(forwarded).IsNotNull();
        await Assert.That(forwarded!.PolicyVersionId)
            .IsEqualTo(currentPolicy.Id);
        await Assert.That(forwarded.AccessMode)
            .IsEqualTo(
                TicketPurchaseAccessMode.AuthenticatedAccount);
    }

    [Test]
    public async Task AccountOwnershipFailurePrecedesCurrentPolicyLookup()
    {
        _inventory.GetOrderWithLinesAsync(
                _orderId,
                _tenantId,
                Arg.Any<CancellationToken>())
            .Returns(
                CreateOrder(
                    Guid.CreateVersion7(),
                    guestHash: null));
        var handler =
            new ReserveAuthenticatedTicketPurchaseCommandHandler(
                _inventory,
                _governance,
                _currentUser,
                _tenant,
                _mediator);

        BaseCommandResponse<Guid> result = await handler.Handle(
            new ReserveAuthenticatedTicketPurchaseCommand(
                _eventId,
                _orderId,
                null,
                "operation"),
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode)
            .IsEqualTo("registration_order_not_found");
        await _governance.DidNotReceiveWithAnyArgs()
            .GetCurrentPolicyVersionAsync(
                default,
                default,
                default);
    }

    [Test]
    public async Task GuestCapabilityFailurePrecedesCurrentPolicyLookup()
    {
        CapabilityTokenHash guestHash =
            CapabilityTokenHash.Create(
                Convert.ToBase64String(new byte[32]));
        _inventory.GetOrderWithLinesAsync(
                _orderId,
                _tenantId,
                Arg.Any<CancellationToken>())
            .Returns(CreateOrder(null, guestHash));
        _capabilities.Matches(
                Arg.Any<string?>(),
                guestHash)
            .Returns(false);
        var handler =
            new ReserveGuestTicketPurchaseCommandHandler(
                _inventory,
                _governance,
                _capabilities,
                _tenant,
                new FixedTimeProvider(UtcNow),
                _mediator);

        BaseCommandResponse<Guid> result = await handler.Handle(
            new ReserveGuestTicketPurchaseCommand(
                _eventId,
                _orderId,
                TicketPurchaseAccessMode.NameOnly,
                "invalid",
                "operation"),
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode)
            .IsEqualTo("registration_order_not_found");
        await _governance.DidNotReceiveWithAnyArgs()
            .GetCurrentPolicyVersionAsync(
                default,
                default,
                default);
    }

    private RegistrationOrder CreateOrder(
        Guid? accountUserId,
        CapabilityTokenHash? guestHash) =>
        RegistrationOrder.Create(
            _orderId,
            _tenantId,
            _eventId,
            accountUserId,
            purchaserActorId: null,
            BookingPartyTypeEnum.Individual,
            Guid.CreateVersion7(),
            RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(),
                (int)ParticipationHandlingModeEnum.PlatformManaged,
                (int)AdvanceRegistrationObligationEnum.Required,
                (int)IdentityAccessModeEnum.CapabilityTokenAllowed,
                GuestRecoveryPolicyEnum.CapabilityLinkOnly),
            registrationWorkflowVersionId: null,
            guestHash,
            "USD",
            UtcNow.AddMinutes(-1),
            UtcNow.AddMinutes(15));

    private TicketPurchasePolicyVersion CreatePolicy() =>
        TicketPurchasePolicyVersion.Create(
            _tenantId,
            _eventId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            5,
            4,
            6,
            UtcNow);

    private sealed class FixedTimeProvider(
        DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(utcNow);
    }
}
