using Explore.Infrastructure.Services;

namespace Explore.Infrastructure.Tests.Authorization;

/// <summary>
/// The revision is only worth anything if it is stable for an unchanged store and different for a changed
/// one. Those are the two failure modes: a value that churns produces false drift alarms an operator
/// learns to ignore, and a value that collides hides the drift it exists to catch.
/// </summary>
public class CerbosStoreRevisionTests
{
    private static readonly (string?, string?)[] TwoPolicies =
    [
        ("resource.islamuevent_event.vdefault", "13466950985171780168"),
        ("derived_roles.islamuevent_explore_admin_roles", "17613918467673392044")
    ];

    /// <summary>
    /// Cerbos does not preserve request order in its response, so a fold that depended on order would
    /// report drift on every observation of a store nobody touched.
    /// </summary>
    [Test]
    public async Task Compute_IsIndependentOfPolicyOrder()
    {
        var forward = CerbosStoreRevision.Compute(TwoPolicies);
        var reversed = CerbosStoreRevision.Compute(TwoPolicies.Reverse());

        await Assert.That(forward).IsNotNull();
        await Assert.That(reversed).IsEqualTo(forward);
    }

    [Test]
    public async Task Compute_IsStableAcrossRepeatedObservationsOfTheSameStore()
    {
        await Assert.That(CerbosStoreRevision.Compute(TwoPolicies))
            .IsEqualTo(CerbosStoreRevision.Compute(TwoPolicies));
    }

    /// <summary>
    /// The case a policy listing cannot see: same identifiers, edited body. This is the reason the fold
    /// reads hashes at all instead of just counting identifiers.
    /// </summary>
    [Test]
    public async Task Compute_ChangesWhenAPolicyBodyChangesButItsIdentifierDoesNot()
    {
        var before = CerbosStoreRevision.Compute(TwoPolicies);
        var after = CerbosStoreRevision.Compute(
        [
            ("resource.islamuevent_event.vdefault", "6065751633899809269"),
            ("derived_roles.islamuevent_explore_admin_roles", "17613918467673392044")
        ]);

        await Assert.That(after).IsNotEqualTo(before);
    }

    [Test]
    public async Task Compute_ChangesWhenAPolicyIsAdded()
    {
        var before = CerbosStoreRevision.Compute(TwoPolicies);
        var after = CerbosStoreRevision.Compute(
        [
            ("resource.islamuevent_event.vdefault", "13466950985171780168"),
            ("derived_roles.islamuevent_explore_admin_roles", "17613918467673392044"),
            ("resource.islamuevent_tenant.vdefault", "99999999999999999999")
        ]);

        await Assert.That(after).IsNotEqualTo(before);
    }

    [Test]
    public async Task Compute_ChangesWhenAPolicyIsRemoved()
    {
        var before = CerbosStoreRevision.Compute(TwoPolicies);
        var after = CerbosStoreRevision.Compute(
            [("resource.islamuevent_event.vdefault", "13466950985171780168")]);

        await Assert.That(after).IsNotEqualTo(before);
    }

    /// <summary>
    /// An empty store is not "revision zero" — it is a store that identifies no policy set. Returning a
    /// real token for it would let an unpublished store read as a known, healthy revision.
    /// </summary>
    [Test]
    public async Task Compute_ReturnsNullForAnEmptyStore()
    {
        await Assert.That(CerbosStoreRevision.Compute([])).IsNull();
    }

    [Test]
    public async Task Compute_ReturnsNullWhenNoEntryCarriesBothIdentifierAndHash()
    {
        var incomplete = CerbosStoreRevision.Compute(
        [
            (null, "13466950985171780168"),
            ("resource.islamuevent_event.vdefault", null),
            ("   ", "   ")
        ]);

        await Assert.That(incomplete).IsNull();
    }

    /// <summary>
    /// Guards a subtle collision: without a separator between identifier and hash, a store holding
    /// <c>("ab", "1")</c> would fold identically to one holding <c>("a", "b1")</c>.
    /// </summary>
    [Test]
    public async Task Compute_DistinguishesStoresThatDifferOnlyInFieldBoundaries()
    {
        var left = CerbosStoreRevision.Compute([("ab", "1")]);
        var right = CerbosStoreRevision.Compute([("a", "b1")]);

        await Assert.That(left).IsNotNull();
        await Assert.That(right).IsNotEqualTo(left);
    }

    [Test]
    public async Task Compute_ProducesABoundedLowercaseHexToken()
    {
        var revision = CerbosStoreRevision.Compute(TwoPolicies);

        await Assert.That(revision).IsNotNull();
        await Assert.That(revision!.Length).IsEqualTo(16);
        await Assert.That(revision).Matches("^[0-9a-f]{16}$");
    }
}
