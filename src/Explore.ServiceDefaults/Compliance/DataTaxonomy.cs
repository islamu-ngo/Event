using Microsoft.Extensions.Compliance.Classification;

namespace Explore.ServiceDefaults.Compliance;

/// <summary>
/// Authoritative data taxonomy classifications for the ISLAMU Event platform.
/// </summary>
public static class DataTaxonomy
{
    public static string TaxonomyName => "ISLAMU.Event.DataTaxonomy";

    public static DataClassification PublicInformation => new(TaxonomyName, nameof(PublicInformation));

    public static DataClassification InternalInformation => new(TaxonomyName, nameof(InternalInformation));

    public static DataClassification SensitiveData => new(TaxonomyName, nameof(SensitiveData));

    public static DataClassification PiiData => new(TaxonomyName, nameof(PiiData));
}

/// <summary>
/// Annotates personally identifiable information (PII) such as email, phone, name, physical address, and IP.
/// Triggered for framework redaction in telemetry and structured logs.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PiiDataAttribute : DataClassificationAttribute
{
    public PiiDataAttribute() : base(DataTaxonomy.PiiData) { }
}

/// <summary>
/// Annotates sensitive security data such as API tokens, client secrets, passwords, and private signing keys.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class SensitiveDataAttribute : DataClassificationAttribute
{
    public SensitiveDataAttribute() : base(DataTaxonomy.SensitiveData) { }
}

/// <summary>
/// Annotates internal system information not intended for public disclosure.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class InternalInformationAttribute : DataClassificationAttribute
{
    public InternalInformationAttribute() : base(DataTaxonomy.InternalInformation) { }
}

/// <summary>
/// Annotates public information suitable for unrestricted disclosure.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PublicInformationAttribute : DataClassificationAttribute
{
    public PublicInformationAttribute() : base(DataTaxonomy.PublicInformation) { }
}
