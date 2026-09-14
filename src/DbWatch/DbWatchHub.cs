using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Microsoft.AspNetCore.Http;

namespace DbWatch;

internal sealed class DbWatchHub
{
    private const int QueryBacklog = 500;
    private const int CoverageBacklog = 200;

    private readonly ConcurrentDictionary<Channel<DbWatchQuery>, byte> _querySubscribers = new();
    private readonly ConcurrentDictionary<Channel<DbWatchCoverage>, byte> _coverageSubscribers = new();
    private long _nextId;

    public bool IsWatchingQueries => !_querySubscribers.IsEmpty;

    public bool IsWatchingCoverage => !_coverageSubscribers.IsEmpty;

    public void PublishQuery(
        HttpContext? request,
        string sql,
        double durationMs,
        IReadOnlyDictionary<string, string?> parameters) =>
        Broadcast(_querySubscribers, new DbWatchQuery(
            NextId(),
            DateTime.UtcNow,
            durationMs,
            sql,
            parameters,
            request?.TraceIdentifier,
            request?.Request.Method,
            request?.Request.Path.Value));

    public void PublishCoverage(HttpContext request, DbWatchCoverageReport report) =>
        Broadcast(_coverageSubscribers, new DbWatchCoverage(
            NextId(),
            DateTime.UtcNow,
            request.TraceIdentifier,
            request.Request.Method,
            request.Request.Path.Value,
            report.DtoType,
            report.RowCount,
            report.MissingCount,
            report.Fields));

    public IAsyncEnumerable<DbWatchQuery> WatchQueries(CancellationToken cancellationToken) =>
        Watch(_querySubscribers, QueryBacklog, cancellationToken);

    public IAsyncEnumerable<DbWatchCoverage> WatchCoverage(CancellationToken cancellationToken) =>
        Watch(_coverageSubscribers, CoverageBacklog, cancellationToken);

    private long NextId() => Interlocked.Increment(ref _nextId);

    private static void Broadcast<T>(ConcurrentDictionary<Channel<T>, byte> subscribers, T item)
    {
        foreach (var subscriber in subscribers)
        {
            subscriber.Key.Writer.TryWrite(item);
        }
    }

    private static IAsyncEnumerable<T> Watch<T>(
        ConcurrentDictionary<Channel<T>, byte> subscribers,
        int backlog,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(backlog)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        subscribers.TryAdd(channel, 0);
        cancellationToken.Register(() => subscribers.TryRemove(channel, out _));

        return Drain(channel, cancellationToken);
    }

    private static async IAsyncEnumerable<T> Drain<T>(
        Channel<T> channel,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await WaitToReadAsync(channel, cancellationToken))
        {
            while (channel.Reader.TryRead(out var item))
            {
                yield return item;
            }
        }
    }

    private static async Task<bool> WaitToReadAsync<T>(Channel<T> channel, CancellationToken cancellationToken)
    {
        try
        {
            return await channel.Reader.WaitToReadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
