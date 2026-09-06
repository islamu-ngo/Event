// ABOUTME: Marks derived location text for provider-specific Unicode binary collations.
// ABOUTME: Keeps location search independent from unrelated portable ordinal ASCII properties.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Schema;

internal static class LocationUnicodePropertyExtensions
{
    internal const string AnnotationName = "Explore:LocationUnicode";

    internal static PropertyBuilder<string> UseLocationUnicodeCollation(this PropertyBuilder<string> property) =>
        property.UseCollation("C").HasAnnotation(AnnotationName, true);
}
