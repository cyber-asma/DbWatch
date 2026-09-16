using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace DbWatch.Tests;

public sealed class DbWatchEndToEndTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ThePageIsServedInDevelopment()
    {
        await using var app = await TestApp.StartAsync(Environments.Development);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/db-watch");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>DB Watch</title>", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheStreamShowsTheSqlOfARequest()
    {
        await using var app = await TestApp.StartAsync(Environments.Development);
        using var client = app.GetTestClient();
        using var cancellation = new CancellationTokenSource(Patience);

        await using var stream = await EventStream.OpenAsync(client, "/db-watch/stream", cancellation.Token);
        using var response = await client.GetAsync("/api/things", cancellation.Token);

        var query = await stream.NextAsync(line => line.Contains("\"path\":\"/api/things\"", StringComparison.Ordinal), cancellation.Token);

        Assert.Contains("Things", query, StringComparison.Ordinal);
        Assert.Contains("\"method\":\"GET\"", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCoverageStreamNamesTheFieldsNoRowFilled()
    {
        await using var app = await TestApp.StartAsync(Environments.Development);
        using var client = app.GetTestClient();
        using var cancellation = new CancellationTokenSource(Patience);

        await using var stream = await EventStream.OpenAsync(client, "/db-watch/coverage-stream", cancellation.Token);
        using var response = await client.GetAsync("/api/things", cancellation.Token);

        var coverage = await stream.NextAsync(line => line.StartsWith("data: {", StringComparison.Ordinal), cancellation.Token);

        Assert.Contains("\"dtoType\":\"ThingResponse\"", coverage, StringComparison.Ordinal);
        Assert.Contains("\"rowCount\":2", coverage, StringComparison.Ordinal);
        Assert.Contains("\"missingCount\":1", coverage, StringComparison.Ordinal);
        Assert.Contains("{\"name\":\"note\",\"type\":\"String\",\"status\":\"missing\",\"filledRows\":0}", coverage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/db-watch")]
    [InlineData("/db-watch/stream")]
    [InlineData("/db-watch/coverage-stream")]
    public async Task NothingIsServedOutsideDevelopment(string url)
    {
        await using var app = await TestApp.StartAsync(Environments.Production);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheQueryStoreLookupSaysItNeedsSqlServer()
    {
        await using var app = await TestApp.StartAsync(Environments.Development);
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync("/db-watch/query-store", new { sql = "SELECT 1" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"available\":false", body, StringComparison.Ordinal);
        Assert.Contains("needs SQL Server", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheQueryStoreLookupRejectsAnEmptyStatement()
    {
        await using var app = await TestApp.StartAsync(Environments.Development);
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync("/db-watch/query-store", new { sql = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheQueryStoreLookupIsNotServedOutsideDevelopment()
    {
        await using var app = await TestApp.StartAsync(Environments.Production);
        using var client = app.GetTestClient();

        using var response = await client.PostAsJsonAsync("/db-watch/query-store", new { sql = "SELECT 1" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class EventStream(HttpResponseMessage _response, StreamReader _reader) : IAsyncDisposable
    {
        public static async Task<EventStream> OpenAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var stream = new EventStream(response, new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken)));
            await stream.NextAsync(line => line == "event: ready", cancellationToken);

            return stream;
        }

        public async Task<string> NextAsync(Func<string, bool> match, CancellationToken cancellationToken)
        {
            while (await _reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (match(line))
                {
                    return line;
                }
            }

            throw new InvalidOperationException("The event stream ended before the expected line arrived.");
        }

        public ValueTask DisposeAsync()
        {
            _reader.Dispose();
            _response.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
