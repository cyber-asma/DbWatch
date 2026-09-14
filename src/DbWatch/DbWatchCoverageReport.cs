namespace DbWatch;

internal static class DbWatchFieldStatus
{
    public const string Filled = "filled";
    public const string Partial = "partial";
    public const string Missing = "missing";
}

internal sealed record DbWatchFieldCoverage(string Name, string Type, string Status, int FilledRows);

internal sealed record DbWatchCoverageReport(string DtoType, int RowCount, IReadOnlyList<DbWatchFieldCoverage> Fields)
{
    public int MissingCount => Fields.Count(coverage => coverage.Status == DbWatchFieldStatus.Missing);
}
