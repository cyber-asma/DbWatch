namespace DbWatch;

internal sealed record DbWatchQuery(
    long Id,
    DateTime TimestampUtc,
    double DurationMs,
    string Sql,
    IReadOnlyDictionary<string, string?> Parameters,
    string? RequestId,
    string? Method,
    string? Path);

internal sealed record DbWatchCoverage(
    long Id,
    DateTime TimestampUtc,
    string RequestId,
    string Method,
    string? Path,
    string DtoType,
    int RowCount,
    int MissingCount,
    IReadOnlyList<DbWatchFieldCoverage> Fields);
