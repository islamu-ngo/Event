using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Pins a selected context and connector without borrowing transaction or connector ownership.</summary>
internal sealed class LocalIdentityExecutionContextCustody
{
    private readonly DbContextId contextId;
    private readonly DbConnection connection;

    private LocalIdentityExecutionContextCustody(DbContext context)
    {
        contextId = context.ContextId;
        connection = context.Database.GetDbConnection();
    }

    internal static LocalIdentityExecutionContextCustody Capture(DbContext context)
    {
        if (!context.Database.IsRelational())
            throw new InvalidOperationException("The Local identity writer requires a relational context.");
        return new(context);
    }

    internal void RequireCurrent(DbContext context)
    {
        if (context.ContextId != contextId || !ReferenceEquals(connection, context.Database.GetDbConnection())
            || (context.Database.CurrentTransaction is { } transaction
                && !ReferenceEquals(connection, transaction.GetDbTransaction().Connection)))
            throw new UnauthorizedAccessException("The original Local identity context or connector changed.");
    }
}
