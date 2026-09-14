using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc;

namespace DbWatch.Tests;

public sealed class DbWatchCoverageAnalyzerTests
{
    public sealed record Row(int Id, string? Name, string? Note, IReadOnlyList<string> Tags);

    public sealed record Renamed([property: JsonPropertyName("code")] string Key);

    [Fact]
    public void EachFieldIsJudgedByHowManyRowsFilledIt()
    {
        var report = Analyze(
            typeof(List<Row>),
            """
            [
              { "id": 1, "name": "a", "note": null, "tags": [] },
              { "id": 2, "name": null, "note": null, "tags": ["x"] }
            ]
            """);

        Assert.Equal(2, report.RowCount);
        Assert.Equal(1, report.MissingCount);
        Assert.Equal(DbWatchFieldStatus.Filled, StatusOf(report, "id"));
        Assert.Equal(DbWatchFieldStatus.Partial, StatusOf(report, "name"));
        Assert.Equal(DbWatchFieldStatus.Missing, StatusOf(report, "note"));
        Assert.Equal(DbWatchFieldStatus.Partial, StatusOf(report, "tags"));
    }

    [Theory]
    [InlineData(typeof(Row))]
    [InlineData(typeof(Task<IReadOnlyList<Row>>))]
    [InlineData(typeof(ValueTask<IEnumerable<Row>>))]
    [InlineData(typeof(Task<ActionResult<Row[]>>))]
    public void TheRowTypeIsFoundInsideTasksResultsAndLists(Type declaredReturnType)
    {
        var report = Analyze(declaredReturnType, """[{ "id": 1 }]""");

        Assert.Equal(nameof(Row), report.DtoType);
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(Task<List<int>>))]
    [InlineData(typeof(Task<IActionResult>))]
    [InlineData(typeof(FileContentResult))]
    [InlineData(typeof(Task<Stream>))]
    public void ResponsesThatAreNotDtosAreNotInspected(Type declaredReturnType)
    {
        Assert.False(DbWatchCoverageAnalyzer.CanAnalyze(declaredReturnType, JsonSerializerOptions.Web));
    }

    [Fact]
    public void FieldsCarryTheNameTheSerializerWrites()
    {
        var report = Analyze(typeof(Renamed), """{ "code": "x" }""");

        Assert.Equal(DbWatchFieldStatus.Filled, StatusOf(report, "code"));
    }

    [Fact]
    public void AnEmptyListHasNothingToJudge()
    {
        var report = DbWatchCoverageAnalyzer.Analyze(typeof(List<Row>), Encoding.UTF8.GetBytes("[]"), JsonSerializerOptions.Web);

        Assert.Null(report);
    }

    private static DbWatchCoverageReport Analyze(Type declaredReturnType, string json)
    {
        var report = DbWatchCoverageAnalyzer.Analyze(declaredReturnType, Encoding.UTF8.GetBytes(json), JsonSerializerOptions.Web);

        Assert.NotNull(report);
        return report;
    }

    private static string StatusOf(DbWatchCoverageReport report, string field) =>
        Assert.Single(report.Fields, coverage => coverage.Name == field).Status;
}
