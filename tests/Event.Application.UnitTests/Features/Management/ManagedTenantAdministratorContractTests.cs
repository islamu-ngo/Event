// ABOUTME: Specifies the closed managed administrator contract and durable identity-only request hash.
// ABOUTME: Rejects retired invitation/password inputs and requires explicit directory operator readiness.

using System.Text.Json;
using Explore.Application.DTOs.Management.Validators;
using Explore.Application.Features.Management;

namespace Event.Application.UnitTests.Features.Management;

public sealed class ManagedTenantAdministratorContractTests
{
    [Test]
    public async Task LocalSubjectReferenceRoundTripsWithoutCredentialMaterial()
    {
        var request = ManagedTenantProvisioningRequestCodec.Deserialize(RequestJson());
        await Assert.That(new ManagementTenantProvisioningRequestValidator().Validate(request).IsValid).IsTrue();
        string snapshot = ManagedTenantProvisioningRequestCodec.Serialize(ManagedTenantProvisioningRequestCodec.Normalize(request));
        using var json = JsonDocument.Parse(snapshot);
        await Assert.That(json.RootElement.GetProperty("administrator").GetProperty("localIdentity")
            .GetProperty("localSubjectId").GetGuid()).IsEqualTo(Subject);
        await Assert.That(ManagedTenantProvisioningRequestCodec.ComputeHash(request))
            .IsEqualTo(ManagedTenantProvisioningRequestCodec.ComputeHash(ManagedTenantProvisioningRequestCodec.Deserialize(snapshot)));
    }

    [Test]
    [Arguments("\"invitation\":{\"email\":\"admin@example.test\",\"firstName\":\"Managed\",\"lastName\":\"Admin\"}")]
    [Arguments("\"localIdentity\":{\"localSubjectId\":\"018e4e5c-7f00-7000-8000-000000000001\",\"password\":null}")]
    [Arguments("\"localIdentity\":{\"localSubjectId\":\"018e4e5c-7f00-7000-8000-000000000001\",\"operationId\":null}")]
    public async Task RetiredAndUnknownAdministratorFieldsFailClosed(string administrator)
    {
        await Assert.That(() => ManagedTenantProvisioningRequestCodec.Deserialize(RequestJson(administrator)))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments("{}")]
    [Arguments("{\"localIdentity\":{\"localSubjectId\":\"00000000-0000-0000-0000-000000000000\"}}")]
    [Arguments("{\"localIdentity\":{\"localSubjectId\":\"018e4e5c-7f00-7000-8000-000000000001\"},\"externalIdentity\":{\"identityProvider\":\"https://issuer.example.test\",\"subject\":\"external\",\"email\":\"admin@example.test\",\"firstName\":\"Admin\",\"lastName\":\"User\"}}")]
    public async Task ExactlyOneNonemptyIdentityIsRequired(string administrator)
    {
        var request = ManagedTenantProvisioningRequestCodec.Deserialize(RequestJson(administrator[1..^1]));
        await Assert.That(new ManagementTenantProvisioningRequestValidator().Validate(request).IsValid).IsFalse();
    }

    [Test]
    public async Task DirectoryOperatorIdentityIsRequiredAndParticipatesInHash()
    {
        var request = ManagedTenantProvisioningRequestCodec.Deserialize(RequestJson());
        var missing = ManagedTenantProvisioningRequestCodec.Deserialize(RequestJson().Replace(DirectoryJson, "null", StringComparison.Ordinal));
        await Assert.That(new ManagementTenantProvisioningRequestValidator().Validate(missing).IsValid).IsFalse();
        var changed = ManagedTenantProvisioningRequestCodec.Deserialize(RequestJson().Replace("Operator ASBL", "Another ASBL", StringComparison.Ordinal));
        await Assert.That(ManagedTenantProvisioningRequestCodec.ComputeHash(request))
            .IsNotEqualTo(ManagedTenantProvisioningRequestCodec.ComputeHash(changed));
    }

    private static readonly Guid Subject = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private const string DirectoryJson = """{"publicName":"Operator","legalName":"Operator ASBL","operatorKindCode":"registered_organization","jurisdictionCountryCode":"BE","registrationIdentifier":"BE 0123.456.789","publicContactEmail":"contact@example.test","legalNoticeUrl":"https://example.test/legal","termsUrl":"https://example.test/terms","privacyUrl":"https://example.test/privacy"}""";
    private static string RequestJson(string? administrator = null) => $$"""
        {"externalRequestId":"managed-1","externalCustomerReference":"customer-1","tenantName":"Managed Tenant","tenantSlug":"managed-tenant",
         "administrator":{ {{administrator ?? $"\"localIdentity\":{{\"localSubjectId\":\"{Subject:D}\"}}"}} },
         "plan":{"key":"managed","versionId":"018e4e5c-7f00-7000-8000-000000000002"},"directoryOperatorIdentity":{{DirectoryJson}}}
        """;
}
