using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Configuration;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class LocalPasswordBlocklistSnapshotTests
{
    [Test]
    public async Task NativeHasherNormalizesCanonicalEquivalentsWithoutTrimmingOrCaseFolding()
    {
        string prefix = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) + "A";
        string composed = prefix + char.ConvertFromUtf32(0xE9);
        string decomposed = prefix + char.ConvertFromUtf32(0x65) + char.ConvertFromUtf32(0x301);
        var hasher = new NormalizedLocalIdentityPasswordHasher(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = LocalIdentityOptions.MinimumHashIterations
        }));
        var user = new LocalIdentityUser();
        string hash = hasher.HashPassword(user, composed);
        await Assert.That(hasher.VerifyHashedPassword(user, hash, decomposed))
            .IsEqualTo(PasswordVerificationResult.Success);
        await Assert.That(hasher.VerifyHashedPassword(user, hash, composed.ToLowerInvariant()))
            .IsEqualTo(PasswordVerificationResult.Failed);
        await Assert.That(hasher.VerifyHashedPassword(user, hash, " " + composed))
            .IsEqualTo(PasswordVerificationResult.Failed);
        await Assert.That(hasher.VerifyHashedPassword(user, hash, new string((char)0xD800, 15)))
            .IsEqualTo(PasswordVerificationResult.Failed);
    }

    public enum InvalidSnapshot { MissingFile, MissingDigest, WrongDigest, Version, Empty, DuplicateCanonical, Metadata, UnknownField, Oversized, TooManyEntries }

    [Test]
    public async Task WholeNormalizedLongPasswordIsBlockedWithoutSubstringBansOrLiveReload()
    {
        string prefix = Convert.ToHexString(RandomNumberGenerator.GetBytes(40));
        string blocked = prefix + char.ConvertFromUtf32(0xE9);
        string equivalent = prefix + char.ConvertFromUtf32(0x65) + char.ConvertFromUtf32(0x301);
        await using var file = new SnapshotFile([blocked]);
        LocalPasswordBlocklistSnapshot capture = LocalPasswordBlocklistSnapshot.Load(file.Options);
        await Assert.That(capture.IsUsable).IsTrue();
        await Assert.That(capture.Count).IsEqualTo(1);
        await Assert.That(capture.Allows(blocked)).IsFalse();
        await Assert.That(capture.Allows(equivalent)).IsFalse();
        await Assert.That(capture.Allows(blocked + char.ConvertFromUtf32(0x20))).IsTrue();
        await Assert.That(capture.Allows(prefix)).IsTrue();
        File.Delete(file.Options.PasswordBlocklistPath!);
        // A loaded snapshot is immutable; pathname changes never publish a partial replacement.
        await Assert.That(capture.Allows(equivalent)).IsFalse();
        await Assert.That(LocalPasswordBlocklistSnapshot.Load(file.Options).IsUsable).IsFalse();
    }

    [Test]
    [Arguments(InvalidSnapshot.MissingFile)]
    [Arguments(InvalidSnapshot.MissingDigest)]
    [Arguments(InvalidSnapshot.WrongDigest)]
    [Arguments(InvalidSnapshot.Version)]
    [Arguments(InvalidSnapshot.Empty)]
    [Arguments(InvalidSnapshot.DuplicateCanonical)]
    [Arguments(InvalidSnapshot.Metadata)]
    [Arguments(InvalidSnapshot.UnknownField)]
    [Arguments(InvalidSnapshot.Oversized)]
    [Arguments(InvalidSnapshot.TooManyEntries)]
    public async Task UnusableCaptureDeniesCredentialsAndRequiredReadiness(InvalidSnapshot invalid)
    {
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        string[] entries = invalid switch
        {
            InvalidSnapshot.Empty => [],
            InvalidSnapshot.TooManyEntries => Enumerable.Repeat(password, LocalPasswordBlocklistSnapshot.MaximumEntries + 1).ToArray(),
            InvalidSnapshot.DuplicateCanonical => [password + char.ConvertFromUtf32(0xE9),
                password + char.ConvertFromUtf32(0x65) + char.ConvertFromUtf32(0x301)],
            _ => [password]
        };
        await using var file = new SnapshotFile(entries, invalid);
        LocalPasswordBlocklistSnapshot capture = LocalPasswordBlocklistSnapshot.Load(file.Options);
        await Assert.That(capture.IsUsable).IsFalse();
        await Assert.That(capture.Count).IsEqualTo(0);
        await Assert.That(capture.Allows(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)))).IsFalse();
        var options = Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = 210_000
        });
        HealthCheckResult required = await new LocalPasswordPolicyHealthCheck(capture, options, true)
            .CheckHealthAsync(new HealthCheckContext());
        await Assert.That(required.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(required.Exception).IsNull();
        HealthCheckResult optional = await new LocalPasswordPolicyHealthCheck(capture, options, false)
            .CheckHealthAsync(new HealthCheckContext());
        await Assert.That(optional.Status).IsEqualTo(HealthStatus.Healthy);
    }

    [Test]
    public async Task StatusReportsEffectiveHashParametersWithoutCredentialMaterial()
    {
        await using var file = new SnapshotFile([Convert.ToHexString(RandomNumberGenerator.GetBytes(24))]);
        var capture = LocalPasswordBlocklistSnapshot.Load(file.Options);
        HealthCheckResult status = await new LocalPasswordPolicyHealthCheck(capture, Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = 310_000
        }), true).CheckHealthAsync(new HealthCheckContext());
        await Assert.That(status.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(status.Data["algorithm"]).IsEqualTo("PBKDF2-HMAC-SHA512");
        await Assert.That(status.Data["format"]).IsEqualTo("IdentityV3");
        await Assert.That(status.Data["iterations"]).IsEqualTo(310_000);
        await Assert.That(status.Data.Keys.Order().ToArray()).IsEquivalentTo(new[]
        {
            "algorithm", "format", "iterations", "saltBits", "subkeyBits", "normalization",
            "snapshotUsable", "snapshotFormat", "snapshotEntries"
        });
    }

    private sealed class SnapshotFile : IAsyncDisposable
    {
        internal LocalIdentityOptions Options { get; } = new();

        internal SnapshotFile(string[] entries, InvalidSnapshot? invalid = null)
        {
            Options.PasswordBlocklistPath = Path.Combine(Path.GetTempPath(), $"password-snapshot-{Guid.CreateVersion7():N}.json");
            var document = new Dictionary<string, object?>
            {
                ["Version"] = invalid == InvalidSnapshot.Version ? 2 : 1,
                ["Revision"] = Guid.CreateVersion7().ToString("N"),
                ["Source"] = "Runtime-generated native test authority",
                ["License"] = invalid == InvalidSnapshot.Metadata ? null : "Project-owned test data",
                ["Passwords"] = entries
            };
            if (invalid == InvalidSnapshot.UnknownField) document["Unexpected"] = true;
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document);
            File.WriteAllBytes(Options.PasswordBlocklistPath, bytes);
            Options.PasswordBlocklistSha256 = Convert.ToHexString(SHA256.HashData(bytes));
            if (invalid == InvalidSnapshot.MissingFile) File.Delete(Options.PasswordBlocklistPath);
            if (invalid == InvalidSnapshot.MissingDigest) Options.PasswordBlocklistSha256 = null;
            if (invalid == InvalidSnapshot.WrongDigest)
            {
                byte[] wrongDigest = SHA256.HashData(bytes);
                wrongDigest[0] ^= 1;
                Options.PasswordBlocklistSha256 = Convert.ToHexString(wrongDigest);
            }
            if (invalid == InvalidSnapshot.Oversized)
            {
                using var file = File.OpenWrite(Options.PasswordBlocklistPath);
                file.SetLength(LocalPasswordBlocklistSnapshot.MaximumBytes + 1L);
            }
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(Options.PasswordBlocklistPath!);
            return ValueTask.CompletedTask;
        }
    }
}
