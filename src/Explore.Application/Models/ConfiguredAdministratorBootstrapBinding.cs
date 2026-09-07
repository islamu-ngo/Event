// ABOUTME: Carries the immutable server-derived binding for one configured administrator generation.
// ABOUTME: Contains a keyed fingerprint and canonical account key, never a raw bootstrap selector.

using Explore.Application.Authentication;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Models;

public sealed record ConfiguredAdministratorBootstrapBinding
{
    public ConfiguredAdministratorBootstrapBinding(
        ProviderAccountKey accountKey,
        long generation,
        string identityFingerprint,
        CompleteInstanceOnboardingRequest settings,
        ConfiguredAdministratorProfile administratorProfile)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentOutOfRangeException.ThrowIfLessThan(generation, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityFingerprint);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(administratorProfile);

        AccountKey = accountKey;
        Generation = generation;
        IdentityFingerprint = identityFingerprint;
        Settings = settings;
        AdministratorProfile = administratorProfile;
    }

    public ProviderAccountKey AccountKey { get; }
    public long Generation { get; }
    public string IdentityFingerprint { get; }
    public CompleteInstanceOnboardingRequest Settings { get; }
    public ConfiguredAdministratorProfile AdministratorProfile { get; }
}

public sealed record ConfiguredAdministratorProfile
{
    public ConfiguredAdministratorProfile(string? email, string? firstName, string? lastName)
    {
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public string? Email { get; }
    public string? FirstName { get; }
    public string? LastName { get; }
}
