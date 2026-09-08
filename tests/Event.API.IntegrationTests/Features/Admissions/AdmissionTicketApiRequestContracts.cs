using System.Reflection;
using Explore.Application.DTOs.RegistrationOrders;
using MediatR;

namespace Event.Api.IntegrationTests.Features;

internal sealed class AdmissionApiRequestContracts
{
    private const string Requests = "Explore.Application.Features.AdmissionTickets.Requests";
    private const string Dtos = "Explore.Application.DTOs.AdmissionTickets";

    private AdmissionApiRequestContracts(
        AdmissionRequestContract recoveryRequest,
        AdmissionRequestContract recoveryConsume,
        AdmissionRequestContract accountList,
        AdmissionRequestContract accountDetail,
        AdmissionRequestContract accountQr,
        AdmissionRequestContract accountPrint)
    {
        RecoveryRequest = recoveryRequest;
        RecoveryConsume = recoveryConsume;
        AccountList = accountList;
        AccountDetail = accountDetail;
        AccountQr = accountQr;
        AccountPrint = accountPrint;
    }

    internal AdmissionRequestContract RecoveryRequest { get; }
    internal AdmissionRequestContract RecoveryConsume { get; }
    internal AdmissionRequestContract AccountList { get; }
    internal AdmissionRequestContract AccountDetail { get; }
    internal AdmissionRequestContract AccountQr { get; }
    internal AdmissionRequestContract AccountPrint { get; }

    internal static AdmissionApiRequestContracts Resolve()
    {
        Assembly application = typeof(RegistrationOrderDto).Assembly;
        Type ticket = RequiredType(application, $"{Dtos}.AdmissionTicketDto");
        return new AdmissionApiRequestContracts(
            Exact(application, $"{Requests}.Commands.RequestAdmissionTicketRecoveryCommand",
                RequiredType(application, $"{Dtos}.AdmissionTicketRecoveryRequestResultDto"), "Email"),
            Exact(application, $"{Requests}.Commands.RedeemAdmissionTicketRecoveryCommand",
                RequiredType(application, $"{Dtos}.AdmissionTicketRecoveryConsumeResultDto"), "Capability"),
            Exact(application, $"{Requests}.Queries.GetCurrentAdmissionTicketsQuery",
                typeof(IReadOnlyList<>).MakeGenericType(ticket)),
            Exact(application, $"{Requests}.Queries.GetCurrentAdmissionTicketQuery", ticket, "TicketId"),
            Exact(application, $"{Requests}.Commands.ReissueCurrentAdmissionTicketQrCommand",
                RequiredType(application, $"{Dtos}.AdmissionTicketQrDeliveryDto"), "TicketId"),
            Exact(application, $"{Requests}.Commands.ReissueCurrentAdmissionTicketPrintCommand",
                RequiredType(application, $"{Dtos}.AdmissionTicketPrintDeliveryDto"), "TicketId"));
    }

    internal static AdmissionApiRequestContracts ForProbe() => new(
        Exact(typeof(CanonicalProbeRequests.RequestAdmissionTicketRecoveryCommand),
            typeof(ProbeResponse), "Email"),
        Exact(typeof(CanonicalProbeRequests.RedeemAdmissionTicketRecoveryCommand),
            typeof(ProbeResponse), "Capability"),
        Exact(typeof(CanonicalProbeRequests.GetCurrentAdmissionTicketsQuery),
            typeof(IReadOnlyList<ProbeResponse>)),
        Exact(typeof(CanonicalProbeRequests.GetCurrentAdmissionTicketQuery),
            typeof(ProbeResponse), "TicketId"),
        Exact(typeof(CanonicalProbeRequests.ReissueCurrentAdmissionTicketQrCommand),
            typeof(ProbeResponse), "TicketId"),
        Exact(typeof(CanonicalProbeRequests.ReissueCurrentAdmissionTicketPrintCommand),
            typeof(ProbeResponse), "TicketId"));

    private static AdmissionRequestContract Exact(
        Assembly assembly,
        string requestName,
        Type responseType,
        params string[] properties) => Exact(
            RequiredType(assembly, requestName), responseType, properties);

    private static AdmissionRequestContract Exact(
        Type requestType,
        Type responseType,
        params string[] properties)
    {
        Type declaredResponse = requestType.GetInterfaces()
            .Single(contract => contract.IsGenericType
                                && contract.GetGenericTypeDefinition() == typeof(IRequest<>))
            .GetGenericArguments()[0];
        if (declaredResponse != responseType)
            throw new InvalidOperationException(
                $"{requestType.FullName} declares {declaredResponse.FullName}, expected {responseType.FullName}.");
        PropertyInfo[] consumed = properties.Select(property =>
            requestType.GetProperty(property, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"{requestType.FullName} lacks machine property {property}.")).ToArray();
        return new AdmissionRequestContract(requestType, responseType, consumed);
    }

    private static Type RequiredType(Assembly assembly, string fullName) =>
        assembly.GetType(fullName, throwOnError: false)
        ?? throw new InvalidOperationException($"Phase 20 API RED: missing exact contract {fullName}.");
}

internal sealed record AdmissionRequestContract(
    Type RequestType,
    Type ResponseType,
    IReadOnlyList<PropertyInfo> ConsumedProperties);

internal static class CanonicalProbeRequests
{
    internal sealed record RequestAdmissionTicketRecoveryCommand(string Email) : IRequest<ProbeResponse>;
    internal sealed record RedeemAdmissionTicketRecoveryCommand(string Capability, string WrongMember)
        : IRequest<ProbeResponse>;
    internal sealed record GetCurrentAdmissionTicketsQuery : IRequest<IReadOnlyList<ProbeResponse>>;
    internal sealed record GetCurrentAdmissionTicketQuery(Guid TicketId, ProbeNestedTicket Nested)
        : IRequest<ProbeResponse>;
    internal sealed record ReissueCurrentAdmissionTicketQrCommand(Guid TicketId) : IRequest<ProbeResponse>;
    internal sealed record ReissueCurrentAdmissionTicketPrintCommand(Guid TicketId) : IRequest<ProbeResponse>;
}

internal static class DecoyProbeRequests
{
    internal sealed record RedeemAdmissionTicketRecoveryCommand(string Capability) : IRequest<ProbeResponse>;
}

internal sealed record ProbeNestedTicket(Guid TicketId);
internal sealed record ProbeResponse;
internal sealed record WrongProbeResponse;
