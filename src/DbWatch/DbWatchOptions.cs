namespace DbWatch;

public sealed class DbWatchOptions
{
    public string RoutePrefix { get; set; } = "/db-watch";

    public string ApiPathPrefix { get; set; } = "/api";

    public int MaxResponseBytes { get; set; } = 8 * 1024 * 1024;
}
