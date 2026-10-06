using IronForge.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IronForge.Application.IntegrationTests.Infrastructure;

public sealed class PostgreSqlDatabaseResetter
{
    private readonly string _connectionString;

    public PostgreSqlDatabaseResetter(
        string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task ResetAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new NpgsqlConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);

        var tables = await GetTablesAsync(
            connection,
            cancellationToken);

        if (tables.Count == 0)
        {
            return;
        }

        var tableList = string.Join(
            ", ",
            tables.Select(QuoteIdentifier));

        var sql = $"TRUNCATE TABLE {tableList} CASCADE;";

        await using var command =
            new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task<List<string>> GetTablesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT tablename
            FROM pg_catalog.pg_tables
            WHERE schemaname = 'public'
              AND tablename <> '__EFMigrationsHistory'
            ORDER BY tablename;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var tables = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static string QuoteIdentifier(
        string identifier)
    {
        return "\"" +
               identifier.Replace("\"", "\"\"") +
               "\"";
    }
}