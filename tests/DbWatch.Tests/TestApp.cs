using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DbWatch.Tests;

public sealed class Thing
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string? Note { get; set; }
}

public sealed class ThingsDbContext(DbContextOptions<ThingsDbContext> options) : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();
}

public sealed record ThingResponse(int Id, string Name, string? Note);

[ApiController]
[Route("api/things")]
public sealed class ThingsController(ThingsDbContext _dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<List<ThingResponse>> List(CancellationToken cancellationToken) =>
        await _dbContext.Things
            .OrderBy(thing => thing.Id)
            .Select(thing => new ThingResponse(thing.Id, thing.Name, thing.Note))
            .ToListAsync(cancellationToken);
}

internal static class TestApp
{
    public static async Task<WebApplication> StartAsync(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();

        builder.Services.AddControllers().AddApplicationPart(typeof(ThingsController).Assembly);
        builder.Services.AddSingleton(_ =>
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            return connection;
        });
        builder.Services.AddDbContext<ThingsDbContext>((services, options) =>
            options.UseSqlite(services.GetRequiredService<SqliteConnection>()));

        builder.AddDbWatch<ThingsDbContext>();

        var app = builder.Build();
        app.UseDbWatch();
        app.MapControllers();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ThingsDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.Things.AddRange(new Thing { Name = "first" }, new Thing { Name = "second" });
            await dbContext.SaveChangesAsync();
        }

        await app.StartAsync();
        return app;
    }
}
