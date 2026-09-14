namespace DbWatch;

internal static class DbWatchPage
{
    private const string ResourceName = "DbWatch.DbWatch.html";

    public static string Html { get; } = Load();

    private static string Load()
    {
        using var stream = typeof(DbWatchPage).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
