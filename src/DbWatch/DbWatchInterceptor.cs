using System.Data.Common;
using System.Globalization;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DbWatch;

internal sealed class DbWatchInterceptor(DbWatchHub _hub, IHttpContextAccessor _httpContextAccessor)
    : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        Capture(command, eventData);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Capture(command, eventData);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        Capture(command, eventData);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Capture(command, eventData);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        Capture(command, eventData);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        Capture(command, eventData);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    private void Capture(DbCommand command, CommandExecutedEventData eventData)
    {
        if (!_hub.IsWatchingQueries)
        {
            return;
        }

        var parameters = new Dictionary<string, string?>(command.Parameters.Count);

        foreach (DbParameter parameter in command.Parameters)
        {
            parameters[parameter.ParameterName] = Display(parameter.Value);
        }

        _hub.PublishQuery(
            _httpContextAccessor.HttpContext,
            command.CommandText,
            eventData.Duration.TotalMilliseconds,
            parameters);
    }

    private static string? Display(object? value) => value switch
    {
        null or DBNull => null,
        byte[] bytes => $"<{bytes.Length} bytes>",
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
