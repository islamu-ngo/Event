// ABOUTME: Reads PostgreSQL notification schema contracts by tenant-qualified column shape.
// ABOUTME: Verifies migrated keys and indexes without depending on generated identifier spelling.

using Npgsql;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal static class NotificationMigrationSchemaContract
{
    internal static async Task<bool> HasForeignKeyAsync(
        NpgsqlConnection connection,
        string schema,
        string table,
        string[] columns,
        string principalTable,
        string[] principalColumns)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                JOIN pg_class p ON p.oid = c.confrelid
                JOIN pg_namespace pn ON pn.oid = p.relnamespace
                WHERE c.contype = 'f' AND c.convalidated AND c.confdeltype = 'r'
                  AND n.nspname = @schema AND t.relname = @table
                  AND pn.nspname = @schema AND p.relname = @principal_table
                  AND ARRAY(SELECT a.attname::text FROM unnest(c.conkey) WITH ORDINALITY k(id, position)
                            JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.id
                            ORDER BY k.position) = @columns
                  AND ARRAY(SELECT a.attname::text FROM unnest(c.confkey) WITH ORDINALITY k(id, position)
                            JOIN pg_attribute a ON a.attrelid = p.oid AND a.attnum = k.id
                            ORDER BY k.position) = @principal_columns)
            """, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("columns", columns);
        command.Parameters.AddWithValue("principal_table", principalTable);
        command.Parameters.AddWithValue("principal_columns", principalColumns);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    internal static async Task<bool> HasUniqueIndexAsync(
        NpgsqlConnection connection,
        string schema,
        string table,
        string[] columns,
        string? filter = null)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT pg_get_expr(i.indpred, i.indrelid)
            FROM pg_index i
            JOIN pg_class t ON t.oid = i.indrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = @schema AND t.relname = @table
              AND i.indisunique AND i.indisvalid AND i.indisready
              AND ARRAY(SELECT a.attname::text FROM unnest(i.indkey) WITH ORDINALITY k(id, position)
                        JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.id
                        WHERE k.position <= i.indnkeyatts ORDER BY k.position) = @columns
            """, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("columns", columns);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string? actual = reader.IsDBNull(0) ? null : reader.GetString(0).Trim('(', ')');
            if (string.Equals(actual, filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    internal static async Task<bool> HasCheckAsync(
        NpgsqlConnection connection, string schema, string table, string name)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM pg_constraint c
                JOIN pg_class t ON t.oid = c.conrelid
                JOIN pg_namespace n ON n.oid = t.relnamespace
                WHERE n.nspname = @schema AND t.relname = @table
                  AND c.contype = 'c' AND c.convalidated AND c.conname = @name)
            """, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("name", name);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    internal static async Task<bool> HasTableAsync(
        NpgsqlConnection connection, string schema, string table)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (SELECT 1 FROM information_schema.tables
                WHERE table_schema = @schema AND table_name = @table)
            """, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    internal static async Task<bool> HasColumnAsync(
        NpgsqlConnection connection, string schema, string table, string column, bool required = false)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (SELECT 1 FROM information_schema.columns
                WHERE table_schema = @schema AND table_name = @table AND column_name = @column
                  AND (NOT @required OR is_nullable = 'NO'))
            """, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        command.Parameters.AddWithValue("required", required);
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
