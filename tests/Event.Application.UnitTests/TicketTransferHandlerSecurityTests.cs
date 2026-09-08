using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Admissions.Handlers.Commands;
using Explore.Application.Features.Admissions.Handlers.Queries;
using Explore.Application.Features.Admissions.Requests.Commands;
using Explore.Application.Features.Admissions.Requests.Queries;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using NSubstitute;

namespace Event.Application.UnitTests;

public sealed class TicketTransferHandlerSecurityTests
{
    private static readonly DateTime UtcNow =
        new(2026, 8, 28, 6, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ExpiredCapabilityReadStopsBeforeDigestMatching()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid ticketId = Guid.CreateVersion7();
        Guid transferId = Guid.CreateVersion7();
        string rawCapability =
            Guid.CreateVersion7().ToString("N");
        AdmissionTicketTransfer transfer =
            Uninitialized<AdmissionTicketTransfer>();
        Set(
            transfer,
            nameof(AdmissionTicketTransfer.StatusId),
            (int)AdmissionTicketTransferStatus.Offered);
        Set(
            transfer,
            nameof(AdmissionTicketTransfer.ExpiresAt),
            UtcNow.AddMinutes(-1));
        Set(
            transfer,
            nameof(
                AdmissionTicketTransfer
                    .CapabilityDigest),
            Digest(rawCapability));
        var access =
            new AdmissionTicketTransferAccessContext(
                transfer,
                Uninitialized<AdmissionTicket>(),
                Uninitialized<RegistrationOrder>(),
                Uninitialized<
                    RegistrationParticipant>(),
                null);
        IAdmissionTicketTransferRepository repository =
            Substitute.For<
                IAdmissionTicketTransferRepository>();
        repository.GetAccessAsync(
                tenantId,
                eventId,
                ticketId,
                transferId,
                Arg.Any<CancellationToken>())
            .Returns(access);
        ITenantContext tenant =
            Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        ICurrentUserService currentUser =
            Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns((Guid?)null);
        IGuestCapabilityTokenService capabilities =
            Substitute.For<
                IGuestCapabilityTokenService>();
        capabilities.Matches(
                Arg.Any<string?>(),
                Arg.Any<CapabilityTokenHash>())
            .Returns(true);
        var handler =
            new GetTicketTransferQueryHandler(
                repository,
                tenant,
                currentUser,
                capabilities,
                new FixedTimeProvider(UtcNow));

        var result = await handler.Handle(
            new GetTicketTransferQuery(
                eventId,
                ticketId,
                transferId,
                rawCapability),
            CancellationToken.None);

        await Assert.That(result).IsNull();
        capabilities.DidNotReceiveWithAnyArgs()
            .Matches(
                default,
                default);
    }

    [Test]
    public async Task ExistingOfferDoesNotRevealNewUnmatchedClaim()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid ticketId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        string rawCapability =
            Guid.CreateVersion7().ToString("N");
        string digest = Digest(rawCapability);
        AdmissionTicket ticket =
            Uninitialized<AdmissionTicket>();
        Set(
            ticket,
            nameof(AdmissionTicket.Id),
            ticketId);
        Set(
            ticket,
            nameof(
                AdmissionTicket
                    .RegistrationOrderId),
            orderId);
        Set(
            ticket,
            nameof(
                AdmissionTicket
                    .HolderSubjectUserId),
            userId);
        RegistrationOrder order =
            Uninitialized<RegistrationOrder>();
        Set(
            order,
            nameof(RegistrationOrder.AccountUserId),
            userId);
        AdmissionTicketTransfer existing =
            Uninitialized<AdmissionTicketTransfer>();
        IAdmissionTicketTransferRepository repository =
            Substitute.For<
                IAdmissionTicketTransferRepository>();
        repository.GetTicketAsync(
                tenantId,
                eventId,
                ticketId,
                Arg.Any<CancellationToken>())
            .Returns(ticket);
        repository.GetOrderAsync(
                tenantId,
                eventId,
                orderId,
                Arg.Any<CancellationToken>())
            .Returns(order);
        repository.GetEventStartsAtUtcAsync(
                tenantId,
                eventId,
                Arg.Any<CancellationToken>())
            .Returns(UtcNow.AddDays(1));
        repository.OfferAsync(
                Arg.Any<
                    AdmissionTicketTransferOfferRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new AdmissionTicketTransferResult(
                AdmissionTicketTransferOutcome
                    .AlreadyOffered,
                existing,
                ticket));
        ITenantContext tenant =
            Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(tenantId);
        ICurrentUserService currentUser =
            Substitute.For<ICurrentUserService>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(userId);
        IGuestCapabilityTokenService capabilities =
            Substitute.For<
                IGuestCapabilityTokenService>();
        capabilities.Issue().Returns(
            new GuestCapabilityTokenIssue(
                rawCapability,
                CapabilityTokenHash.Create(digest)));
        var handler =
            new OfferTicketTransferCommandHandler(
                repository,
                tenant,
                currentUser,
                capabilities,
                new InlineUnitOfWork(),
                new FixedTimeProvider(UtcNow));

        var result = await handler.Handle(
            new OfferTicketTransferCommand(
                eventId,
                ticketId),
            CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    private static string Digest(string value) =>
        Convert.ToBase64String(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(value)));

    private static T Uninitialized<T>()
        where T : class =>
        (T)RuntimeHelpers.GetUninitializedObject(
            typeof(T));

    private static void Set<T>(
        T target,
        string propertyName,
        object? value)
        where T : class =>
        typeof(T).GetProperty(
                propertyName,
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic)!
            .SetValue(target, value);

    private sealed class FixedTimeProvider(
        DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(utcNow);
    }

    private sealed class InlineUnitOfWork :
        IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default) =>
            operation(ct);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) =>
            operation(ct);

        public Task<T> ExecuteSerializableAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) =>
            operation(ct);
    }
}
