using Explore.Domain.Enums;

namespace Event.Domain.UnitTests;

/// <summary>
/// Verifies the public token grammar and independent digest identities that prevent duplicate issuance.
/// </summary>
public sealed class ExternalApiKeyIssuanceReceiptTests
{
    private static readonly Guid PrincipalId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000011");
    private static readonly Guid OwnerId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000022");
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000033");
    private static readonly Guid OtherId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000044");
    private static readonly Guid ReceiptId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000055");
    private static readonly Guid KeyId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000066");
    private static readonly DateTime CreatedAtUtc = new(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);
    private const string InputDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ChangedInputDigest = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string OperationKey = "issue:v1";

    /// <summary>
    /// Accepts the complete protocol alphabet without normalizing the caller's operation identity.
    /// </summary>
    [Test]
    [Arguments("a")]
    [Arguments("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._:-")]
    public async Task OperationKey_AcceptsTheRequiredAsciiAlphabet(string operationKey)
    {
        await Assert.That(ExternalApiKeyIssuanceReceipt.IsValidOperationKey(operationKey)).IsTrue();
    }

    /// <summary>
    /// Admits the maximum header length without truncating the fingerprint input.
    /// </summary>
    [Test]
    public async Task OperationKey_AcceptsExactly128Characters()
    {
        await Assert.That(ExternalApiKeyIssuanceReceipt.IsValidOperationKey(new string('a', 128))).IsTrue();
    }

    /// <summary>
    /// Rejects absent, combined, delimited, whitespace-bearing and non-ASCII header values.
    /// </summary>
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("\t")]
    [Arguments("\r\n")]
    [Arguments(" key")]
    [Arguments("key ")]
    [Arguments("two words")]
    [Arguments("key\tvalue")]
    [Arguments("key\nvalue")]
    [Arguments("key\0value")]
    [Arguments("key/value")]
    [Arguments("key\\value")]
    [Arguments("key|value")]
    [Arguments("key+value")]
    [Arguments("key=value")]
    [Arguments("key,value")]
    [Arguments("key\"value")]
    [Arguments("caf\u00e9")]
    [Arguments("key\u200bvalue")]
    [Arguments("key\u007fvalue")]
    public async Task OperationKey_RejectsMissingWhitespaceDelimitersAndNonAscii(string? operationKey)
    {
        await Assert.That(ExternalApiKeyIssuanceReceipt.IsValidOperationKey(operationKey)).IsFalse();
    }

    /// <summary>
    /// Rejects the first length beyond the admitted protocol boundary.
    /// </summary>
    [Test]
    public async Task OperationKey_Rejects129Characters()
    {
        await Assert.That(ExternalApiKeyIssuanceReceipt.IsValidOperationKey(new string('a', 129))).IsFalse();
    }

    /// <summary>
    /// Enumerates the ASCII complement independently of the production predicate to detect grammar widening.
    /// </summary>
    [Test]
    public async Task OperationKey_RejectsEveryAsciiCharacterOutsideTheAllowedAlphabet()
    {
        const string allowed = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789._:-";
        for (int value = 0; value < 128; value++)
        {
            char character = (char)value;
            if (!allowed.Contains(character))
            {
                await Assert.That(ExternalApiKeyIssuanceReceipt.IsValidOperationKey($"a{character}b")).IsFalse();
            }
        }
    }

    /// <summary>
    /// Pins the independently calculated digest for the versioned operation and explicit global namespace.
    /// </summary>
    [Test]
    public async Task Fingerprint_PinsOperationKindVersionAndExplicitGlobalScope()
    {
        // SHA-256 of UTF-8, no newline:
        // external-api-key-issuance:v1|principal:018e4e5c7f0070008000000000000011|scope:global|
        // owner-type:5|owner:018e4e5c7f0070008000000000000022|operation-key:8:issue:v1
        // The two comment lines above are one continuous input, not newline-delimited.
        string fingerprint = Fingerprint(null, ExternalApiKeyOwnerType.InstanceAdmin, OperationKey);

        await Assert.That(fingerprint).IsEqualTo("a3524ba3fc88621ec2e683446c60155f296e950e3f805aa1d1f722498f21476e");
    }

    /// <summary>
    /// Checks deterministic hexadecimal encoding while preserving case-sensitive operation input.
    /// </summary>
    [Test]
    public async Task Fingerprint_IsStableAndProducesA64CharacterHexIdentity()
    {
        string first = Fingerprint(TenantId, ExternalApiKeyOwnerType.User, "Issue:V1");
        string repeated = Fingerprint(TenantId, ExternalApiKeyOwnerType.User, "Issue:V1");

        await Assert.That(first.Length).IsEqualTo(64);
        await Assert.That(first.All(character => "0123456789abcdefABCDEF".Contains(character))).IsTrue();
        await Assert.That(repeated).IsEqualTo(first);
    }

    /// <summary>
    /// Changes one trusted identity dimension at a time so tenant, principal and owner namespaces cannot collide.
    /// </summary>
    [Test]
    [Arguments("principal")]
    [Arguments("tenant")]
    [Arguments("global")]
    [Arguments("owner")]
    [Arguments("owner-type")]
    [Arguments("operation-key")]
    [Arguments("operation-key-case")]
    public async Task Fingerprint_IsolatesEveryIdentityDimension(string changedDimension)
    {
        string original = Fingerprint(TenantId, ExternalApiKeyOwnerType.User, OperationKey);
        Guid? tenant = changedDimension switch
        {
            "global" => null,
            "tenant" => OtherId,
            _ => TenantId
        };
        string operationKey = changedDimension switch
        {
            "operation-key" => "issue:v2",
            "operation-key-case" => "Issue:v1",
            _ => OperationKey
        };
        string changed = ExternalApiKeyIssuanceReceipt.ComputeOperationFingerprint(
            changedDimension == "principal" ? OtherId : PrincipalId,
            tenant,
            changedDimension == "owner-type" ? ExternalApiKeyOwnerType.Organization : ExternalApiKeyOwnerType.User,
            changedDimension == "owner" ? OtherId : OwnerId,
            operationKey);

        await Assert.That(changed).IsNotEqualTo(original);
    }

    /// <summary>
    /// Keeps each owner category distinct even when all other identity inputs use global scope.
    /// </summary>
    [Test]
    public async Task Fingerprint_IsolatesAllOwnerTypesInGlobalScope()
    {
        string[] fingerprints = Enum.GetValues<ExternalApiKeyOwnerType>()
            .Select(ownerType => Fingerprint(null, ownerType, OperationKey))
            .ToArray();

        await Assert.That(fingerprints.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(5);
    }

    /// <summary>
    /// Separates delimiter placements and zero, one or two trailing delimiters in opaque operation tokens.
    /// </summary>
    [Test]
    public async Task Fingerprint_ResistsDelimiterAndLengthAmbiguity()
    {
        string[] operationKeys = ["a:b:c", "ab:c", "a:bc", "a.b:c", "a:b.c"];
        string[] fingerprints = operationKeys.Concat(
                Enumerable.Range(0, 3).Select(delimiterCount => "a" + new string(':', delimiterCount)))
            .Select(operationKey => Fingerprint(TenantId, ExternalApiKeyOwnerType.User, operationKey))
            .ToArray();

        await Assert.That(fingerprints.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(8);
    }

    /// <summary>
    /// Preserves operation identity across policy changes while retaining the independently supplied policy digest.
    /// </summary>
    [Test]
    public async Task Create_KeepsInputDigestSeparateFromOperationIdentity()
    {
        string fingerprint = Fingerprint(TenantId, ExternalApiKeyOwnerType.User, OperationKey);
        ExternalApiKeyIssuanceReceipt original = ExternalApiKeyIssuanceReceipt.Create(
            ReceiptId, fingerprint, InputDigest, TenantId, KeyId, CreatedAtUtc);
        ExternalApiKeyIssuanceReceipt changedPolicy = ExternalApiKeyIssuanceReceipt.Create(
            OtherId, fingerprint, ChangedInputDigest, TenantId, KeyId, CreatedAtUtc);

        await Assert.That(original.InputDigest).IsEqualTo(InputDigest);
        await Assert.That(changedPolicy.InputDigest).IsEqualTo(ChangedInputDigest);
        await Assert.That(changedPolicy.OperationFingerprint).IsEqualTo(original.OperationFingerprint);
        await Assert.That(original.OperationFingerprint).IsEqualTo(fingerprint);
    }

    /// <summary>
    /// Retains the exact preallocated receipt, tenant and key identities and UTC creation time.
    /// </summary>
    [Test]
    public async Task Create_PreservesExactTenantKeyAndPreallocatedIdentity()
    {
        string fingerprint = Fingerprint(TenantId, ExternalApiKeyOwnerType.User, OperationKey);
        ExternalApiKeyIssuanceReceipt receipt = ExternalApiKeyIssuanceReceipt.Create(
            ReceiptId, fingerprint, InputDigest, TenantId, KeyId, CreatedAtUtc);

        await Assert.That(receipt.Id).IsEqualTo(ReceiptId);
        await Assert.That(receipt.TenantId).IsEqualTo(TenantId);
        await Assert.That(receipt.ExternalApiKeyId).IsEqualTo(KeyId);
        await Assert.That(receipt.CreatedAtUtc).IsEqualTo(CreatedAtUtc);
    }

    /// <summary>
    /// Represents global scope as null rather than manufacturing a tenant sentinel.
    /// </summary>
    [Test]
    public async Task Create_GlobalScopeRemainsNullRatherThanASentinelTenant()
    {
        string fingerprint = Fingerprint(null, ExternalApiKeyOwnerType.InstanceAdmin, OperationKey);
        ExternalApiKeyIssuanceReceipt receipt = ExternalApiKeyIssuanceReceipt.Create(
            ReceiptId, fingerprint, InputDigest, null, KeyId, CreatedAtUtc);

        await Assert.That(receipt.TenantId).IsNull();
        await Assert.That(receipt.OperationFingerprint).IsEqualTo(fingerprint);
        await Assert.That(receipt.ExternalApiKeyId).IsEqualTo(KeyId);
    }

    /// <summary>
    /// Keeps historical receipts bound to the original operation instead of reopening issuance after elapsed time.
    /// </summary>
    [Test]
    public async Task Create_OldReceiptRetainsIdentityWithoutTimeBasedRenewal()
    {
        DateTime historicalCreation = CreatedAtUtc.AddYears(-20);
        string fingerprint = Fingerprint(TenantId, ExternalApiKeyOwnerType.User, OperationKey);
        ExternalApiKeyIssuanceReceipt historical = ExternalApiKeyIssuanceReceipt.Create(
            ReceiptId, fingerprint, InputDigest, TenantId, KeyId, historicalCreation);
        ExternalApiKeyIssuanceReceipt laterAttempt = ExternalApiKeyIssuanceReceipt.Create(
            OtherId, fingerprint, InputDigest, TenantId, KeyId, CreatedAtUtc);

        await Assert.That(historical.CreatedAtUtc).IsEqualTo(historicalCreation);
        await Assert.That(historical.Id).IsEqualTo(ReceiptId);
        await Assert.That(historical.ExternalApiKeyId).IsEqualTo(KeyId);
        await Assert.That(historical.OperationFingerprint).IsEqualTo(laterAttempt.OperationFingerprint);
        await Assert.That(historical.InputDigest).IsEqualTo(InputDigest);
    }

    /// <summary>
    /// Keeps independently created receipt entities from altering an earlier receipt's persisted identity.
    /// </summary>
    [Test]
    public async Task Create_AnotherReceiptCannotMutateTheOriginalIdentity()
    {
        string fingerprint = Fingerprint(TenantId, ExternalApiKeyOwnerType.User, OperationKey);
        ExternalApiKeyIssuanceReceipt original = ExternalApiKeyIssuanceReceipt.Create(
            ReceiptId, fingerprint, InputDigest, TenantId, KeyId, CreatedAtUtc);
        ExternalApiKeyIssuanceReceipt other = ExternalApiKeyIssuanceReceipt.Create(
            OtherId, Fingerprint(null, ExternalApiKeyOwnerType.InstanceAdmin, "other"),
            ChangedInputDigest, null, OtherId, CreatedAtUtc.AddDays(1));

        await Assert.That(original).IsNotEqualTo(other);
        await Assert.That(original.Id).IsEqualTo(ReceiptId);
        await Assert.That(original.OperationFingerprint).IsEqualTo(fingerprint);
        await Assert.That(original.InputDigest).IsEqualTo(InputDigest);
        await Assert.That(original.TenantId).IsEqualTo(TenantId);
        await Assert.That(original.ExternalApiKeyId).IsEqualTo(KeyId);
        await Assert.That(original.CreatedAtUtc).IsEqualTo(CreatedAtUtc);
    }

    /// <summary>
    /// Supplies the fixed principal and owner while individual cases vary scope, owner kind or operation token.
    /// </summary>
    private static string Fingerprint(Guid? tenantId, ExternalApiKeyOwnerType ownerType, string operationKey) =>
        ExternalApiKeyIssuanceReceipt.ComputeOperationFingerprint(PrincipalId, tenantId, ownerType, OwnerId, operationKey);
}
