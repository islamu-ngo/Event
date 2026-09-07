using System.Reflection;
using Explore.Application.Contracts.Admissions;
using Explore.Domain;

namespace Event.Architecture.Tests;

public sealed class AdmissionScannerCapabilityRepositoryBoundaryTests
{
    [Test]
    public async Task Phase21CheckInUsesDomainDecisionTransactionPortAndNoRepositoryDtoFacade()
    {
        Type application = typeof(IAdmissionScannerCapabilityRepository).Assembly.GetType(
            "Explore.Application.Contracts.Admissions.IAdmissionCheckInTransaction")
            ?? throw new InvalidOperationException("Missing IAdmissionCheckInTransaction.");
        MethodInfo execute = application.GetMethods().Single(method => method.Name == "ExecuteAsync");

        await Assert.That(Unwrap(execute.ReturnType)).Contains(typeof(AdmissionCheckInDecision));
        await Assert.That(application.Assembly.GetType(
            "Explore.Application.Contracts.Admissions.IAdmissionCheckInRepository")).IsNull();
        await Assert.That(application.Assembly.GetType(
            "Explore.Application.Contracts.Admissions.AdmissionCheckInPersistenceRequest")).IsNull();
        await Assert.That(application.Assembly.GetType(
            "Explore.Application.Contracts.Admissions.AdmissionCheckInPersistenceResult")).IsNull();
    }

    [Test]
    public async Task PostAuthenticationCheckInContractsCarryScannerIdNeverScannerPlaintext()
    {
        Type[] contracts =
        [
            typeof(AdmissionCheckInRequest),
            typeof(AdmissionCheckInBatchRequest),
            typeof(AdmissionCheckInAuthorizationRequest)
        ];

        foreach (Type contract in contracts)
        {
            PropertyInfo scanner = contract.GetProperties().Single(property =>
                property.Name.Contains("ScannerCapability", StringComparison.Ordinal));
            await Assert.That(scanner.Name).IsEqualTo("ScannerCapabilityId");
            await Assert.That(scanner.PropertyType).IsEqualTo(typeof(Guid?));
        }
    }

    [Test]
    public async Task ScannerCapabilityScopeIsOneDirectTargetWithoutCollectionCompatibility()
    {
        PropertyInfo issueTarget = typeof(AdmissionScannerCapabilityIssueRequest).GetProperty("TargetId")!;
        PropertyInfo descriptorTarget = typeof(AdmissionScannerCapabilityDescriptor).GetProperty("TargetId")!;

        await Assert.That(issueTarget.PropertyType).IsEqualTo(typeof(Guid));
        await Assert.That(descriptorTarget.PropertyType).IsEqualTo(typeof(Guid));
        await Assert.That(typeof(AdmissionScannerCapabilityIssueRequest).GetProperty("TargetIds")).IsNull();
        await Assert.That(typeof(AdmissionScannerCapabilityDescriptor).GetProperty("TargetIds")).IsNull();
        await Assert.That(typeof(AdmissionScannerCapability).GetProperty("AdmissionTargetId")!.PropertyType)
            .IsEqualTo(typeof(Guid));
        await Assert.That(typeof(AdmissionScannerCapability).GetProperty("Targets")).IsNull();
        await Assert.That(typeof(AdmissionScannerCapability).Assembly.GetType(
            "Explore.Domain.AdmissionScannerCapabilityTarget")).IsNull();
    }

    [Test]
    public async Task EveryPhase21RepositoryAcceptsAndReturnsDomainEntitiesNeverDescriptorsOrPersistenceDtos()
    {
        Type repository = typeof(IAdmissionScannerCapabilityRepository);
        Type[] phase21Repositories = repository.Assembly.GetExportedTypes()
            .Where(type => type.IsInterface && type.Name.EndsWith("Repository", StringComparison.Ordinal) &&
                (type.Name.Contains("AdmissionCheckIn", StringComparison.Ordinal) ||
                 type.Name.Contains("AdmissionScanner", StringComparison.Ordinal)))
            .ToArray();
        await Assert.That(phase21Repositories).IsEquivalentTo([
            repository,
            typeof(IAdmissionCheckInReportingRepository)
        ]);
        Type[] reportingSignatureTypes = typeof(IAdmissionCheckInReportingRepository).GetMethods()
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType))
            .SelectMany(Unwrap)
            .Distinct()
            .ToArray();
        await Assert.That(reportingSignatureTypes).Contains(typeof(AdmissionTarget));
        await Assert.That(reportingSignatureTypes).Contains(typeof(AdmissionCheckInEvent));
        await Assert.That(reportingSignatureTypes).DoesNotContain(typeof(AdmissionCheckInState));
        await Assert.That(reportingSignatureTypes.Any(type =>
            type.Namespace == typeof(AdmissionCheckInSummary).Namespace)).IsFalse();

        Type summaryQuery = typeof(IAdmissionCheckInSummaryQuery);
        await Assert.That(summaryQuery.Name.EndsWith("Repository", StringComparison.Ordinal)).IsFalse();
        MethodInfo summaryMethod = summaryQuery.GetMethods().Single();
        await Assert.That(Unwrap(summaryMethod.ReturnType))
            .Contains(typeof(AdmissionCheckInSummaryProjection));
        await Assert.That(typeof(IAdmissionCheckInReportingRepository).GetMethods().Any(method =>
            method.Name.Contains("TargetEventsPage", StringComparison.Ordinal) ||
            method.Name.Contains("TargetStatesPage", StringComparison.Ordinal))).IsFalse();
        MethodInfo[] methods = repository.GetMethods(BindingFlags.Instance | BindingFlags.Public);
        Type[] signatureTypes = methods
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType))
            .SelectMany(Unwrap)
            .Distinct()
            .ToArray();

        await Assert.That(signatureTypes).Contains(typeof(AdmissionScannerCapability));
        await Assert.That(signatureTypes).DoesNotContain(typeof(AdmissionScannerCapabilityDescriptor));
        await Assert.That(signatureTypes.Any(type =>
            type.Name.Contains("PersistenceRequest", StringComparison.Ordinal) ||
            type.Name.Contains("Descriptor", StringComparison.Ordinal))).IsFalse();
        await Assert.That(repository.Assembly.GetType(
            "Explore.Application.Contracts.Admissions.AdmissionScannerCapabilityPersistenceRequest"))
            .IsNull();
        await Assert.That(repository.Assembly.GetType(
            "Explore.Application.Contracts.Admissions.AdmissionScannerCapabilityPersistenceResult"))
            .IsNull();

        MethodInfo store = methods.Single(method => method.Name == "StoreAsync");
        await Assert.That(store.GetParameters()[0].ParameterType)
            .IsEqualTo(typeof(AdmissionScannerCapability));
        Type storeOutcome = Unwrap(store.ReturnType).Single(type =>
            type == typeof(AdmissionScannerCapabilityStoreResult));
        PropertyInfo[] outcomeProperties = storeOutcome.GetProperties();
        await Assert.That(outcomeProperties.Select(property => property.Name))
            .IsEquivalentTo(["Created", "Capability", "Rejected"]);
        await Assert.That(outcomeProperties.Single(property => property.Name == "Capability").PropertyType)
            .IsEqualTo(typeof(AdmissionScannerCapability));
        await Assert.That(outcomeProperties.Single(property => property.Name == "Rejected").PropertyType)
            .IsEqualTo(typeof(bool));

        MethodInfo get = methods.Single(method => method.Name == "GetAsync");
        await Assert.That(Unwrap(get.ReturnType)).Contains(typeof(AdmissionScannerCapability));
        MethodInfo update = methods.Single(method => method.Name == "UpdateAsync");
        await Assert.That(update.GetParameters()[0].ParameterType)
            .IsEqualTo(typeof(AdmissionScannerCapability));
        await Assert.That(Unwrap(update.ReturnType)).Contains(typeof(AdmissionScannerCapability));
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        if (type.IsGenericType &&
            (type.GetGenericTypeDefinition() == typeof(Task<>) ||
             type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)))
        {
            foreach (Type nested in Unwrap(type.GetGenericArguments()[0])) yield return nested;
            yield break;
        }

        yield return type;
    }
}
