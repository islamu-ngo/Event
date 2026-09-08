using System.Text;
using Explore.ServiceDefaults.Compliance;
using Microsoft.Extensions.Compliance.Classification;

namespace Explore.Diagnostic.UnitTests.Compliance;

public class ComplianceRedactorTests
{
    [Test]
    [DisplayName("StarRedactor replaces sensitive PII with fixed asterisks mask")]
    public async Task StarRedactor_MasksPiiStrings()
    {
        var redactor = new StarRedactor("****");

        var input = "user@example.com";
        var dest = new char[redactor.GetRedactedLength(input)];
        var written = redactor.Redact(input.AsSpan(), dest.AsSpan());

        await Assert.That(written).IsEqualTo(4);
        await Assert.That(new string(dest)).IsEqualTo("****");
    }

    [Test]
    [DisplayName("StarRedactor handles empty inputs gracefully without allocation")]
    public async Task StarRedactor_HandlesEmptyInput()
    {
        var redactor = StarRedactor.Instance;
        var dest = new char[10];
        var written = redactor.Redact(ReadOnlySpan<char>.Empty, dest.AsSpan());

        await Assert.That(written).IsEqualTo(0);
        await Assert.That(redactor.GetRedactedLength(ReadOnlySpan<char>.Empty)).IsEqualTo(0);
    }

    [Test]
    [DisplayName("HmacRedactor produces deterministic 64-character hex string for same input and key")]
    public async Task HmacRedactor_ProducesDeterministicHex()
    {
        var key = Encoding.UTF8.GetBytes("super-secret-pepper-key-minimum-16-bytes");
        var redactor = new HmacRedactor(key);

        var input = "user@example.com";
        var dest1 = new char[redactor.GetRedactedLength(input)];
        var dest2 = new char[redactor.GetRedactedLength(input)];

        var written1 = redactor.Redact(input.AsSpan(), dest1.AsSpan());
        var written2 = redactor.Redact(input.AsSpan(), dest2.AsSpan());

        await Assert.That(written1).IsEqualTo(64);
        await Assert.That(written2).IsEqualTo(64);
        await Assert.That(new string(dest1)).IsEqualTo(new string(dest2));
    }

    [Test]
    [DisplayName("HmacRedactor produces distinct hashes for different inputs")]
    public async Task HmacRedactor_ProducesDistinctHashesForDifferentInputs()
    {
        var key = Encoding.UTF8.GetBytes("super-secret-pepper-key-minimum-16-bytes");
        var redactor = new HmacRedactor(key);

        var dest1 = new char[64];
        var dest2 = new char[64];

        redactor.Redact("alice@example.com".AsSpan(), dest1.AsSpan());
        redactor.Redact("bob@example.com".AsSpan(), dest2.AsSpan());

        await Assert.That(new string(dest1)).IsNotEqualTo(new string(dest2));
    }

    [Test]
    [DisplayName("HmacRedactor throws on short keys below 16 bytes")]
    public async Task HmacRedactor_ThrowsOnShortKey()
    {
        var shortKey = Encoding.UTF8.GetBytes("short-key");
        Assert.Throws<ArgumentException>(() => new HmacRedactor(shortKey));
        await Task.CompletedTask;
    }

    [Test]
    [DisplayName("DataTaxonomy attributes map to canonical classifications")]
    public async Task DataTaxonomyAttributes_MapToCanonicalClassifications()
    {
        var piiAttr = new PiiDataAttribute();
        var sensitiveAttr = new SensitiveDataAttribute();
        var internalAttr = new InternalInformationAttribute();
        var publicAttr = new PublicInformationAttribute();

        await Assert.That(piiAttr.Classification).IsEqualTo(DataTaxonomy.PiiData);
        await Assert.That(sensitiveAttr.Classification).IsEqualTo(DataTaxonomy.SensitiveData);
        await Assert.That(internalAttr.Classification).IsEqualTo(DataTaxonomy.InternalInformation);
        await Assert.That(publicAttr.Classification).IsEqualTo(DataTaxonomy.PublicInformation);
    }
}
