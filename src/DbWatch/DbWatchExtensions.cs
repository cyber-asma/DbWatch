using System.Net.ServerSentEvents;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DbWatch;

public static class DbWatchExtensions
{
    public static IHostApplicationBuilder AddDbWatch<TContext>(
        this IHostApplicationBuilder builder,
        Action<DbWatchOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.Environment.IsDevelopment())
        {
            return builder;
        }

        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton<DbWatchHub>();
        builder.Services.TryAddSingleton<DbWatchInterceptor>();
        builder.Services.TryAddSingleton<IDbWatchQueryStore, DbWatchQueryStore<TContext>>();
        builder.Services.AddOptions<DbWatchOptions>().Configure(configure ?? (_ => { }));

        builder.Services.ConfigureDbContext<TContext>((services, options) =>
            options.AddInterceptors(services.GetRequiredService<DbWatchInterceptor>()));

        return builder;
    }

    public static WebApplication UseDbWatch(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Services.GetService<DbWatchHub>() is null)
        {
            return app;
        }

        app.UseMiddleware<DbWatchCoverageMiddleware>();

        var options = app.Services.GetRequiredService<IOptions<DbWatchOptions>>().Value;
        var group = app.MapGroup(options.RoutePrefix)
            .AllowAnonymous()
            .ExcludeFromDescription();

        group.MapGet("", () => Results.Content(DbWatchPage.Html, "text/html; charset=utf-8"));

        group.MapGet("stream", (DbWatchHub hub, CancellationToken cancellationToken) =>
            TypedResults.ServerSentEvents(Opened(hub.WatchQueries(cancellationToken))));

        group.MapGet("coverage-stream", (DbWatchHub hub, CancellationToken cancellationToken) =>
            TypedResults.ServerSentEvents(Opened(hub.WatchCoverage(cancellationToken))));

        group.MapPost("query-store", FindInQueryStoreAsync);

        return app;
    }

    private static async Task<Results<Ok<DbWatchQueryStoreResult>, BadRequest>> FindInQueryStoreAsync(
        DbWatchQueryStoreRequest request,
        IDbWatchQueryStore queryStore,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            return TypedResults.BadRequest();
        }

        return TypedResults.Ok(await queryStore.FindAsync(request.Sql, cancellationToken));
    }

    private static async IAsyncEnumerable<SseItem<T?>> Opened<T>(IAsyncEnumerable<T> items)
    {
        yield return new SseItem<T?>(default, "ready");

        await foreach (var item in items)
        {
            yield return new SseItem<T?>(item);
        }
    }
}
