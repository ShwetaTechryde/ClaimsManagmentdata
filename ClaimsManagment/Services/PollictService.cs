using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ClaimsManagment.Services;

public sealed class PollictService(IConfiguration configuration) : IPollictService
{
    private const string TableName = "[dbo].[POLLICT]";
    private readonly string _connectionString = configuration.GetConnectionString("ClaimsDb")
        ?? throw new InvalidOperationException("The ClaimsDb connection string is not configured.");

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureTableExistsAsync(connection, cancellationToken);

        await using var command = new SqlCommand($"SELECT * FROM {TableName}", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<IReadOnlyDictionary<string, object?>>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(await ReadRowAsync(reader, cancellationToken));
        }

        return rows;
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var primaryKey = await GetPrimaryKeyAsync(connection, cancellationToken);

        await using var command = new SqlCommand(
            $"SELECT * FROM {TableName} WHERE {QuoteIdentifier(primaryKey)} = @id",
            connection);
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? await ReadRowAsync(reader, cancellationToken)
            : null;
    }

    public async Task<CreatedPollictRow> CreateAsync(
        IReadOnlyDictionary<string, JsonElement> values,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var columns = await GetColumnsAsync(connection, cancellationToken);
        var primaryKey = await GetPrimaryKeyAsync(connection, cancellationToken);
        ValidateValues(values, columns);

        var writableValues = values
            .Where(pair => !IsReadOnlyColumn(pair.Key, columns))
            .ToArray();
        if (writableValues.Length != values.Count)
        {
            throw new ArgumentException("Identity and computed columns cannot be provided.");
        }

        var names = string.Join(", ", writableValues.Select(pair => QuoteIdentifier(pair.Key)));
        var parameters = string.Join(", ", writableValues.Select((_, index) => $"@value{index}"));
        var insert = writableValues.Length == 0
            ? $"INSERT INTO {TableName} OUTPUT INSERTED.* DEFAULT VALUES"
            : $"INSERT INTO {TableName} ({names}) OUTPUT INSERTED.* VALUES ({parameters})";

        await using var command = new SqlCommand(insert, connection);
        for (var index = 0; index < writableValues.Length; index++)
        {
            command.Parameters.AddWithValue(
                $"@value{index}",
                ToSqlValue(writableValues[index].Value));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The inserted POLLICT row was not returned by SQL Server.");
        }

        var row = await ReadRowAsync(reader, cancellationToken);
        var primaryKeyValue = row.First(pair => string.Equals(
            pair.Key,
            primaryKey,
            StringComparison.OrdinalIgnoreCase)).Value;
        return new CreatedPollictRow(
            Convert.ToString(primaryKeyValue, System.Globalization.CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException("The created POLLICT row has a null primary key."),
            row);
    }

    public async Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
        string id,
        IReadOnlyDictionary<string, JsonElement> values,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var columns = await GetColumnsAsync(connection, cancellationToken);
        var primaryKey = await GetPrimaryKeyAsync(connection, cancellationToken);
        ValidateValues(values, columns);

        var writableValues = values
            .Where(pair => !string.Equals(pair.Key, primaryKey, StringComparison.OrdinalIgnoreCase))
            .Where(pair => !IsReadOnlyColumn(pair.Key, columns))
            .ToArray();

        if (writableValues.Length != values.Count)
        {
            throw new ArgumentException("The primary key, identity, and computed columns cannot be updated.");
        }

        if (writableValues.Length == 0)
        {
            throw new ArgumentException("Provide at least one column to update.");
        }

        var assignments = string.Join(
            ", ",
            writableValues.Select((pair, index) => $"{QuoteIdentifier(pair.Key)} = @value{index}"));
        var update = $"UPDATE {TableName} SET {assignments} OUTPUT INSERTED.* WHERE {QuoteIdentifier(primaryKey)} = @id";
        await using var command = new SqlCommand(update, connection);
        command.Parameters.AddWithValue("@id", id);

        for (var index = 0; index < writableValues.Length; index++)
        {
            command.Parameters.AddWithValue(
                $"@value{index}",
                ToSqlValue(writableValues[index].Value));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? await ReadRowAsync(reader, cancellationToken)
            : null;
    }

    private static async Task EnsureTableExistsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string query = """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'POLLICT'
            """;
        await using var command = new SqlCommand(query, connection);
        var count = (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        if (count == 0)
        {
            throw new InvalidOperationException("Table dbo.POLLICT was not found in ClaimsDb.");
        }
    }

    private static async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string query = """
            SELECT COLUMN_NAME,
                   CONVERT(bit, COLUMNPROPERTY(OBJECT_ID(N'dbo.POLLICT'), COLUMN_NAME, 'IsIdentity')),
                   CONVERT(bit, COLUMNPROPERTY(OBJECT_ID(N'dbo.POLLICT'), COLUMN_NAME, 'IsComputed'))
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'POLLICT'
            """;
        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<ColumnInfo>();

        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnInfo(
                reader.GetString(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2)));
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException("Table dbo.POLLICT was not found in ClaimsDb.");
        }

        return columns;
    }

    private static async Task<string> GetPrimaryKeyAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string query = """
            SELECT column_info.name
            FROM sys.indexes AS idx
            INNER JOIN sys.index_columns AS index_column
                ON idx.object_id = index_column.object_id
                AND idx.index_id = index_column.index_id
            INNER JOIN sys.columns AS column_info
                ON index_column.object_id = column_info.object_id
                AND index_column.column_id = column_info.column_id
            WHERE idx.is_primary_key = 1
                AND idx.object_id = OBJECT_ID(N'dbo.POLLICT')
            ORDER BY index_column.key_ordinal
            """;
        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Table dbo.POLLICT must have a primary key.");
        }

        var primaryKey = reader.GetString(0);
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Table dbo.POLLICT must have a single-column primary key.");
        }

        return primaryKey;
    }

    private static void ValidateValues(
        IReadOnlyDictionary<string, JsonElement> values,
        IReadOnlyList<ColumnInfo> columns)
    {
        var knownColumns = columns.Select(column => column.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownColumn = values.Keys.FirstOrDefault(name => !knownColumns.Contains(name));
        if (unknownColumn is not null)
        {
            throw new ArgumentException($"Unknown POLLICT column: {unknownColumn}.");
        }
    }

    private static bool IsReadOnlyColumn(string name, IReadOnlyList<ColumnInfo> columns)
    {
        var column = columns.First(column => string.Equals(
            column.Name,
            name,
            StringComparison.OrdinalIgnoreCase));
        return column.IsIdentity || column.IsComputed;
    }

    private static object ToSqlValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => DBNull.Value,
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.Array or JsonValueKind.Object => value.GetRawText(),
            _ => throw new ArgumentException("Unsupported JSON value.")
        };

    private static async Task<IReadOnlyDictionary<string, object?>> ReadRowAsync(
        SqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            row.Add(
                reader.GetName(index),
                await reader.IsDBNullAsync(index, cancellationToken) ? null : reader.GetValue(index));
        }

        return row;
    }

    private static string QuoteIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private sealed record ColumnInfo(string Name, bool IsIdentity, bool IsComputed);
}
