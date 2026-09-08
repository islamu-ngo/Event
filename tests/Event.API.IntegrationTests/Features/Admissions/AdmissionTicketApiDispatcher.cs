namespace Event.Api.IntegrationTests.Features;

internal sealed class AdmissionScenarioDispatcher(
    AdmissionApiScenario scenario,
    AdmissionApiRequestContracts contracts)
{
    internal object? Dispatch(object request, Type responseType)
    {
        Type requestType = request.GetType();
        if (requestType == contracts.RecoveryRequest.RequestType)
        {
            RequireResponse(contracts.RecoveryRequest, responseType);
            return scenario.RequestRecovery(
                RequiredString(contracts.RecoveryRequest, request, "Email"), responseType);
        }
        if (requestType == contracts.RecoveryConsume.RequestType)
        {
            RequireResponse(contracts.RecoveryConsume, responseType);
            return scenario.ConsumeRecovery(
                RequiredString(contracts.RecoveryConsume, request, "Capability"), responseType);
        }
        if (requestType == contracts.AccountList.RequestType)
        {
            RequireResponse(contracts.AccountList, responseType);
            return scenario.GetAccountTickets(responseType);
        }
        if (requestType == contracts.AccountDetail.RequestType)
        {
            RequireResponse(contracts.AccountDetail, responseType);
            return scenario.GetAccountTicket(
                RequiredGuid(contracts.AccountDetail, request, "TicketId"), responseType);
        }
        if (requestType == contracts.AccountQr.RequestType)
        {
            RequireResponse(contracts.AccountQr, responseType);
            return scenario.GetAccountQr(
                RequiredGuid(contracts.AccountQr, request, "TicketId"), responseType);
        }
        if (requestType == contracts.AccountPrint.RequestType)
        {
            RequireResponse(contracts.AccountPrint, responseType);
            return scenario.GetAccountPrint(
                RequiredGuid(contracts.AccountPrint, request, "TicketId"), responseType);
        }

        throw new InvalidOperationException(
            $"Admission API test dispatcher rejects exact request type {requestType.AssemblyQualifiedName}.");
    }

    private static void RequireResponse(AdmissionRequestContract contract, Type actual)
    {
        if (actual != contract.ResponseType)
            throw new InvalidOperationException(
                $"{contract.RequestType.FullName} requested {actual.FullName}; expected {contract.ResponseType.FullName}.");
    }

    private static string RequiredString(
        AdmissionRequestContract contract,
        object request,
        string property)
    {
        object? value = ExactProperty(contract, property).GetValue(request);
        return value as string
            ?? throw new InvalidOperationException($"{contract.RequestType.FullName}.{property} must be a string.");
    }

    private static Guid RequiredGuid(
        AdmissionRequestContract contract,
        object request,
        string property)
    {
        object? value = ExactProperty(contract, property).GetValue(request);
        return value is Guid id
            ? id
            : throw new InvalidOperationException($"{contract.RequestType.FullName}.{property} must be a Guid.");
    }

    private static System.Reflection.PropertyInfo ExactProperty(
        AdmissionRequestContract contract,
        string property) => contract.ConsumedProperties.Single(candidate => candidate.Name == property);
}
