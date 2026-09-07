using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Schema;

internal static class LocationUnicodePropertyExtensions
{
    internal const string AnnotationName = "Explore:LocationUnicode";

    internal static PropertyBuilder<string> UseLocationUnicodeCollation(this PropertyBuilder<string> property) =>
        property.UseCollation("C").HasAnnotation(AnnotationName, true);
}
