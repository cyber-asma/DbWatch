# DbWatch

A live page that shows the SQL each HTTP request runs through EF Core in an ASP.NET Core app on dev mode.

For every request you see:

- each query with its duration, parameters and main table
- repeated queries: the same SQL shape run more than once, a sign of N+1
- the slowest query
- for `GET /api/...` controller actions: which fields of the response DTO were filled, filled
  in only some rows, or empty in every row

Queries that do not belong to a request, such as those from a background service, are grouped
as `BG`.

## Install

```powershell
dotnet add package DbWatch
```

## Set up

Add two lines to `Program.cs`:

```csharp
using DbWatch;

builder.AddDbWatch<ApplicationDbContext>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseDbWatch();
```

Run the app and open `/db-watch` on the API's own address, for example
`https://localhost:5001/db-watch`. Then use the app and watch the queries arrive.

`AddDbWatch` does nothing outside `Development`, and `UseDbWatch` does nothing when `AddDbWatch`
did not register anything. Both lines can stay in `Program.cs` for every environment.

## Security

The page shows SQL and parameter values, so it must never run in production. That is why it
refuses to switch on in any environment other than `Development`.

The page and its streams allow anonymous access, even when the app requires a login everywhere
else. The browser opens them directly, so they cannot carry a bearer token.

## Options

```csharp
builder.AddDbWatch<ApplicationDbContext>(options =>
{
    options.RoutePrefix = "/db-watch";
    options.ApiPathPrefix = "/api";
    options.MaxResponseBytes = 8 * 1024 * 1024;
});
```

| Option             | Default     | Meaning                                                   |
| ------------------ | ----------- | --------------------------------------------------------- |
| `RoutePrefix`      | `/db-watch` | Where the page and its streams are served.                |
| `ApiPathPrefix`    | `/api`      | Only `GET` requests under this path get a coverage check. |
| `MaxResponseBytes` | 8 MB        | Larger responses are not checked for coverage.            |

## How it works

- An EF Core command interceptor records every command with its duration and the request it
  ran in.
- For `GET` controller actions under `ApiPathPrefix`, a middleware keeps a copy of the JSON
  response and counts, per DTO field, how many rows carry a value.
- The page listens to two server-sent event streams, `/db-watch/stream` and
  `/db-watch/coverage-stream`.
- Nothing is recorded and no response is copied while no page is open.

## Requirements

.NET 10 and EF Core 10, with any relational provider.

## Build and test

```powershell
dotnet test
dotnet pack src/DbWatch -c Release -o artifacts
```

## Release

Pushing a tag such as `v1.0.1` builds, tests and publishes that version to nuget.org. Publishing
uses nuget.org Trusted Publishing, so the repository stores no API key.

## License

[MIT](https://github.com/cyber-asma/DbWatch/blob/main/LICENSE)

