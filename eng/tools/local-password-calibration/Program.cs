using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Explore.Application.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

if (args.Length != 2 || !int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out int iterations)
    || iterations is < LocalIdentityOptions.MinimumHashIterations or > LocalIdentityOptions.MaximumHashIterations
    || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int samples)
    || samples is < 20 or > 1000)
{
    Console.Error.WriteLine("Usage: LocalPasswordCalibration <iterations:100000..2000000> <samples:20..1000>");
    return 2;
}

var hasher = new PasswordHasher<object>(Options.Create(new PasswordHasherOptions
{
    CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
    IterationCount = iterations
}));
var subject = new object();
string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
string hash = hasher.HashPassword(subject, password);
_ = hasher.VerifyHashedPassword(subject, hash, password);
var createMilliseconds = new double[samples];
var verifyMilliseconds = new double[samples];
for (int index = 0; index < samples; index++)
{
    long start = Stopwatch.GetTimestamp();
    hash = hasher.HashPassword(subject, password);
    createMilliseconds[index] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    start = Stopwatch.GetTimestamp();
    PasswordVerificationResult result = hasher.VerifyHashedPassword(subject, hash, password);
    verifyMilliseconds[index] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    if (result != PasswordVerificationResult.Success)
        throw new InvalidOperationException("Native calibration verification failed.");
}
Array.Sort(createMilliseconds);
Array.Sort(verifyMilliseconds);
Console.WriteLine(FormattableString.Invariant(
    $"algorithm=PBKDF2-HMAC-SHA512 format=IdentityV3 iterations={iterations} saltBits=128 subkeyBits=256 samples={samples}"));
Console.WriteLine(FormattableString.Invariant(
    $"hash_p50_ms={createMilliseconds[(samples - 1) / 2]:F3} hash_p95_ms={createMilliseconds[(int)Math.Ceiling(samples * 0.95) - 1]:F3}"));
Console.WriteLine(FormattableString.Invariant(
    $"verify_p50_ms={verifyMilliseconds[(samples - 1) / 2]:F3} verify_p95_ms={verifyMilliseconds[(int)Math.Ceiling(samples * 0.95) - 1]:F3}"));
return 0;
