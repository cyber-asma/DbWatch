using System.Data;
using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DbWatch;

internal sealed record DbWatchQueryStoreRequest(string? Sql);

internal sealed record DbWatchQueryStoreMatch(
    long QueryId,
    int Plans,
    long Executions,
    double AvgMs,
    double AvgLogicalReads,
    DateTimeOffset? LastRunUtc,
    bool HasMissingIndex);

internal sealed record DbWatchQueryStoreResult(
    bool Available,
    string? Reason,
    IReadOnlyList<DbWatchQueryStoreMatch> Matches)
{
    public static DbWatchQueryStoreResult Unavailable(string reason) => new(false, reason, []);

    public static DbWatchQueryStoreResult Found(IReadOnlyList<DbWatchQueryStoreMatch> matches) => new(true, null, matches);
}

internal interface IDbWatchQueryStore
{
    Task<DbWatchQueryStoreResult> FindAsync(string sql, CancellationToken cancellationToken);
}

internal sealed class DbWatchQueryStore<TContext>(
    IServiceScopeFactory _scopeFactory,
    ILogger<DbWatchQueryStore<TContext>> _logger) : IDbWatchQueryStore
    where TContext : DbContext
{
    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    public async Task<DbWatchQueryStoreResult> FindAsync(string sql, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TContext>().Database;

        if (database.ProviderName != SqlServerProvider)
        {
            return DbWatchQueryStoreResult.Unavailable(
                $"Query Store needs SQL Server; this app uses {database.ProviderName}.");
        }

        var appConnection = database.GetDbConnection();
        var databaseName = appConnection.Database;
        var factory = DbProviderFactories.GetFactory(appConnection);

        if (string.IsNullOrEmpty(databaseName) || factory is null)
        {
            return DbWatchQueryStoreResult.Unavailable("DbWatch could not work out which database to look in.");
        }

        try
        {
            await using var connection = factory.CreateConnection()
                ?? throw new InvalidOperationException("The database provider could not create a connection.");
            connection.ConnectionString = database.GetConnectionString();
            await connection.OpenAsync(cancellationToken);
            await connection.ChangeDatabaseAsync("master", cancellationToken);

            var quotedName = "[" + databaseName.Replace("]", "]]", StringComparison.Ordinal) + "]";

            if (!await IsQueryStoreOnAsync(connection, quotedName, cancellationToken))
            {
                return DbWatchQueryStoreResult.Unavailable($"Query Store is off for {databaseName}.");
            }

            return DbWatchQueryStoreResult.Found(await FindMatchesAsync(connection, quotedName, sql, cancellationToken));
        }
        catch (DbException exception)
        {
            _logger.LogWarning(exception, "DbWatch could not read Query Store for {Database}.", databaseName);
            return DbWatchQueryStoreResult.Unavailable(exception.Message);
        }
    }

    private static async Task<bool> IsQueryStoreOnAsync(
        DbConnection connection,
        string quotedName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT actual_state_desc FROM {quotedName}.sys.database_query_store_options;";

        return await command.ExecuteScalarAsync(cancellationToken) is "READ_WRITE" or "READ_ONLY";
    }

    private static async Task<IReadOnlyList<DbWatchQueryStoreMatch>> FindMatchesAsync(
        DbConnection connection,
        string quotedName,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT q.query_id,
                   CAST(COUNT(DISTINCT p.plan_id) AS int),
                   CAST(ISNULL(SUM(rs.count_executions), 0) AS bigint),
                   CAST(ISNULL(SUM(rs.avg_duration * rs.count_executions) / NULLIF(SUM(rs.count_executions), 0), 0) / 1000.0 AS float),
                   CAST(ISNULL(SUM(rs.avg_logical_io_reads * rs.count_executions) / NULLIF(SUM(rs.count_executions), 0), 0) AS float),
                   MAX(rs.last_execution_time),
                   CAST(MAX(CASE WHEN p.query_plan LIKE N'%<MissingIndexes>%' THEN 1 ELSE 0 END) AS bit)
            FROM {quotedName}.sys.query_store_query_text AS qt
            JOIN {quotedName}.sys.query_store_query AS q ON q.query_text_id = qt.query_text_id
            LEFT JOIN {quotedName}.sys.query_store_plan AS p ON p.query_id = q.query_id
            LEFT JOIN {quotedName}.sys.query_store_runtime_stats AS rs ON rs.plan_id = p.plan_id
            WHERE RIGHT(qt.query_sql_text, LEN(@sql + N'x') - 1) = @sql
            GROUP BY q.query_id
            ORDER BY MAX(rs.last_execution_time) DESC;
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@sql";
        parameter.DbType = DbType.String;
        parameter.Size = -1;
        parameter.Value = sql;
        command.Parameters.Add(parameter);

        var matches = new List<DbWatchQueryStoreMatch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            matches.Add(new DbWatchQueryStoreMatch(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetInt64(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                await reader.IsDBNullAsync(5, cancellationToken) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetBoolean(6)));
        }

        return matches;
    }
}
