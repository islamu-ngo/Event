namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Managed;
using Explore.Domain;

public sealed class ConfigurationExtensionAndOwnershipTests
{
    [Test]
    public async Task SignedPack_AcceptsDeclarativePayloadAndRejectsExecutableContent()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        JsonElement declarative = Json("{\"enabled\":true}");
        ConfigurationExtensionPack pack = Pack(declarative, signer);
        ConfigurationExtensionTrustPolicy policy = Policy(signer);

        ConfigurationExtensionValidationResult valid =
            ConfigurationExtensionPackValidator.Validate(pack, policy);
        ConfigurationExtensionValidationResult executable =
            ConfigurationExtensionPackValidator.Validate(
                Pack(Json("{\"sql\":\"drop table events\"}"), signer),
                policy);

        await Assert.That(valid.IsValid).IsTrue();
        await Assert.That(executable.IsValid).IsFalse();
        await Assert.That(executable.FailureCode)
            .IsEqualTo("configuration_extension_descriptor_invalid");
    }

    [Test]
    public async Task SignedPack_BindsFieldMeaningNotOnlySortedValues()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ConfigurationExtensionPack pack = Pack(Json("{\"enabled\":true}"), signer);
        ConfigurationExtensionPack tampered = pack with
        {
            PackId = pack.Provenance.Publisher,
            Provenance = pack.Provenance with { Publisher = pack.PackId }
        };

        await Assert.That(ConfigurationExtensionPackValidator.Validate(
                tampered,
                Policy(signer)).IsValid)
            .IsFalse();
    }

    [Test]
    public async Task OwnershipPlanner_PreservesUnmanagedFieldsAndRequiresTakeover()
    {
        ConfigurationManagedFieldRequest request = new(
            "/settings/title",
            Digest("current"),
            Digest("desired"),
            "manager-a",
            "manager-b",
            ConfigurationManagedFieldIntent.Set,
            TakeoverApproved: false);

        ConfigurationManagedPlan blocked = ConfigurationManagedOwnershipPlanner.Plan(
            ConfigurationManagedPlanMode.Apply,
            [request]);
        ConfigurationManagedPlan drift = ConfigurationManagedOwnershipPlanner.Plan(
            ConfigurationManagedPlanMode.DriftOnly,
            [request with { CurrentManager = null }]);

        await Assert.That(blocked.CanApply).IsFalse();
        await Assert.That(blocked.Fields.Single().Outcome)
            .IsEqualTo(ConfigurationManagedFieldOutcome.Conflict);
        await Assert.That(blocked.Fields.Single().ResultingManager)
            .IsEqualTo("manager-a");
        await Assert.That(drift.CanApply).IsFalse();
        await Assert.That(drift.Fields.Single().Outcome)
            .IsEqualTo(ConfigurationManagedFieldOutcome.Drift);
    }

    private static ConfigurationExtensionPack Pack(JsonElement section, ECDsa signer)
    {
        string payloadDigest = Digest(section.GetRawText());
        var descriptor = new ConfigurationExtensionDescriptor(
            "extension.community",
            1,
            "1.0",
            "2.0",
            [],
            ["/settings/enabled"],
            payloadDigest);
        var unsigned = new ConfigurationExtensionPack(
            "community-pack",
            "1.0",
            new ConfigurationExtensionProvenance(
                "community",
                "urn:community:pack",
                "MIT"),
            [descriptor],
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [descriptor.SectionKey] = section
            },
            new ConfigurationExtensionSignature("issuer", "key", "", "", ""));
        string digest = ConfigurationExtensionPackValidator.PackDigest(unsigned);
        string signature = Convert.ToBase64String(
            signer.SignHash(Convert.FromHexString(digest)));
        return unsigned with
        {
            Signature = new ConfigurationExtensionSignature(
                "issuer",
                "key",
                ConfigurationExtensionPackValidator.SignatureAlgorithm,
                digest,
                signature)
        };
    }

    private static ConfigurationExtensionTrustPolicy Policy(ECDsa signer) =>
        new(
            "1.5",
            new HashSet<string>(["MIT"], StringComparer.Ordinal),
            [new ConfigurationExtensionTrustedKey(
                "issuer",
                "key",
                Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()))]);

    private static JsonElement Json(string value) =>
        JsonDocument.Parse(value).RootElement.Clone();

    private static string Digest(string value) =>
        ConfigurationImportDigest.Compute([value]);
}

public sealed class ConfigurationDirectTransferSecurityTests
{
    private static readonly DateTime Now =
        new(2026, 8, 30, 20, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Policy_RejectsPrivateDestinationAndAcceptsPublicHttps443()
    {
        await Assert.That(() =>
                ConfigurationDirectTransferPolicy.ValidateDestinationOrigin(
                    new Uri("https://internal.example/"),
                    [IPAddress.Parse("10.0.0.8")]))
            .Throws<ArgumentException>();
        await Assert.That(() =>
                ConfigurationDirectTransferPolicy.ValidateDestinationOrigin(
                    new Uri("https://mapped.example/"),
                    [IPAddress.Parse("::ffff:127.0.0.1")]))
            .Throws<ArgumentException>();

        Uri endpoint = ConfigurationDirectTransferPolicy.ValidateDestinationOrigin(
            new Uri("https://target.example/"),
            [IPAddress.Parse("203.0.113.10")]);

        await Assert.That(endpoint.AbsolutePath)
            .IsEqualTo(ConfigurationDirectTransferPolicy.DestinationPath);
    }

    [Test]
    public async Task Session_RequiresMutualApprovalAndCompletesIdempotently()
    {
        Guid uploader = Guid.CreateVersion7();
        Guid reviewer = Guid.CreateVersion7();
        string nonce = Digest("nonce");
        string artifact = Digest("artifact");
        ConfigurationDirectTransferSession session =
            ConfigurationDirectTransferSession.Create(
                Guid.CreateVersion7(),
                "source.example",
                "instance",
                targetTenantId: null,
                Digest("origin"),
                Digest("proof"),
                nonce,
                artifact,
                artifactByteLength: 128,
                Now,
                Now.AddMinutes(15));

        await Assert.That(() => session.AcceptChunk(
                0,
                128,
                Digest("chunk"),
                nonce,
                Now.AddMinutes(1)))
            .Throws<InvalidOperationException>();

        session.ApproveDestination(reviewer, Digest("proof"), Now.AddMinutes(1));
        session.ApproveSource(uploader, Now.AddMinutes(2));
        bool accepted = session.AcceptChunk(
            0,
            128,
            Digest("chunk"),
            nonce,
            Now.AddMinutes(3));
        session.Complete(artifact, nonce, Now.AddMinutes(4));
        session.Complete(artifact, nonce, Now.AddHours(1));

        await Assert.That(accepted).IsTrue();
        await Assert.That(session.Status)
            .IsEqualTo(ConfigurationDirectTransferStatus.Received);
        await Assert.That(session.NextOffset).IsEqualTo(128);
    }

    private static string Digest(string value) =>
        ConfigurationImportDigest.Compute([value]);
}

public sealed class ConfigurationManagedOperationsTests
{
    private static readonly DateTime Now =
        new(2026, 8, 30, 20, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Schedule_SeparatesActorsAndFencesStaleRevision()
    {
        Guid uploader = Guid.CreateVersion7();
        Guid reviewer = Guid.CreateVersion7();
        Guid applier = Guid.CreateVersion7();
        ConfigurationManagedApplySchedule schedule =
            ConfigurationManagedApplySchedule.Create(
                Guid.CreateVersion7(),
                "instance",
                Digest("artifact"),
                Digest("revision"),
                Digest("plan"),
                uploader,
                Now.AddMinutes(5),
                Now.AddHours(1),
                Now);

        await Assert.That(() => schedule.Approve(uploader, Now.AddMinutes(1)))
            .Throws<InvalidOperationException>();

        schedule.Approve(reviewer, Now.AddMinutes(1));
        await Assert.That(() => schedule.Apply(
                reviewer,
                Digest("revision"),
                Now.AddMinutes(6)))
            .Throws<InvalidOperationException>();

        schedule.Apply(applier, Digest("changed"), Now.AddMinutes(6));

        await Assert.That(schedule.Status)
            .IsEqualTo(ConfigurationManagedApplyScheduleStatus.Stale);
        await Assert.That(schedule.AppliedBy).IsNull();
    }

    [Test]
    public async Task SupportRepresentations_AreValueFreeTypeNames()
    {
        ConfigurationManagedPlan plan = ConfigurationManagedOwnershipPlanner.Plan(
            ConfigurationManagedPlanMode.DriftOnly,
            [new ConfigurationManagedFieldRequest(
                "/settings/title",
                Digest("private-current-value"),
                Digest("private-desired-value"),
                null,
                "manager",
                ConfigurationManagedFieldIntent.Set,
                TakeoverApproved: false)]);

        await Assert.That(plan.ToString()).IsEqualTo(nameof(ConfigurationManagedPlan));
        await Assert.That(plan.ToString()).DoesNotContain("private");
    }

    private static string Digest(string value) =>
        ConfigurationImportDigest.Compute([value]);
}
