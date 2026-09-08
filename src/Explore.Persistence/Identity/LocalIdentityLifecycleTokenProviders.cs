// ABOUTME: Registers native ASP.NET Identity Data Protection token providers with fixed lifecycle lifetimes.
// ABOUTME: Provider and per-operation purposes isolate verification/email change from password recovery.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NativeOptions = Microsoft.Extensions.Options.Options;

namespace Explore.Persistence.Identity;

internal static class LocalIdentityLifecycleTokenProviders
{
    internal const string Verification = "LocalLifecycleVerification";
    internal const string Recovery = "LocalLifecycleRecovery";

    internal static IdentityBuilder AddLocalLifecycleTokenProviders(this IdentityBuilder builder) => builder
        .AddTokenProvider<VerificationProvider>(Verification)
        .AddTokenProvider<RecoveryProvider>(Recovery);

    internal sealed class VerificationProvider(IDataProtectionProvider protection, ILogger<DataProtectorTokenProvider<LocalIdentityUser>> logger)
        : DataProtectorTokenProvider<LocalIdentityUser>(protection, NativeOptions.Create(new DataProtectionTokenProviderOptions
        { Name = Verification, TokenLifespan = TimeSpan.FromMinutes(30) }), logger);

    internal sealed class RecoveryProvider(IDataProtectionProvider protection, ILogger<DataProtectorTokenProvider<LocalIdentityUser>> logger)
        : DataProtectorTokenProvider<LocalIdentityUser>(protection, NativeOptions.Create(new DataProtectionTokenProviderOptions
        { Name = Recovery, TokenLifespan = TimeSpan.FromMinutes(15) }), logger);
}
