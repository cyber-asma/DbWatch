using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DbWatch;

internal static class DbWatchCoverageAnalyzer
{
    private static readonly Type[] Wrappers = [typeof(Task<>), typeof(ValueTask<>), typeof(ActionResult<>)];

    public static bool CanAnalyze(Type declaredReturnType, JsonSerializerOptions serializerOptions) =>
        RowContract(declaredReturnType, serializerOptions) is not null;

    public static DbWatchCoverageReport? Analyze(
        Type declaredReturnType,
        ReadOnlyMemory<byte> json,
        JsonSerializerOptions serializerOptions)
    {
        var contract = RowContract(declaredReturnType, serializerOptions);

        if (contract is null)
        {
            return null;
        }

        var filledRows = contract.Properties.ToDictionary(property => property.Name, _ => 0, StringComparer.Ordinal);
        var rowCount = 0;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            IEnumerable<JsonElement> rows = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray() : [root];

            foreach (var row in rows.Where(row => row.ValueKind == JsonValueKind.Object))
            {
                rowCount++;

                foreach (var property in row.EnumerateObject())
                {
                    if (HasValue(property.Value) && filledRows.TryGetValue(property.Name, out var filled))
                    {
                        filledRows[property.Name] = filled + 1;
                    }
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        if (rowCount == 0)
        {
            return null;
        }

        var fields = contract.Properties
            .Select(property => new DbWatchFieldCoverage(
                property.Name,
                FriendlyName(property.PropertyType),
                Status(filledRows[property.Name], rowCount),
                filledRows[property.Name]))
            .ToList();

        return new DbWatchCoverageReport(FriendlyName(contract.Type), rowCount, fields);
    }

    private static JsonTypeInfo? RowContract(Type declaredReturnType, JsonSerializerOptions serializerOptions)
    {
        var rowType = RowType(declaredReturnType);

        if (typeof(IActionResult).IsAssignableFrom(rowType)
            || typeof(IResult).IsAssignableFrom(rowType)
            || typeof(Stream).IsAssignableFrom(rowType))
        {
            return null;
        }

        try
        {
            var contract = serializerOptions.GetTypeInfo(rowType);
            return contract.Kind == JsonTypeInfoKind.Object && contract.Properties.Count > 0 ? contract : null;
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static Type RowType(Type type)
    {
        if (type.IsGenericType && Wrappers.Contains(type.GetGenericTypeDefinition()))
        {
            return RowType(type.GetGenericArguments()[0]);
        }

        if (type == typeof(string))
        {
            return type;
        }

        var itemType = type.IsArray ? type.GetElementType() : EnumerableItemType(type);

        return itemType is null ? type : RowType(itemType);
    }

    private static Type? EnumerableItemType(Type type)
    {
        var enumerable = IsEnumerableOfT(type) ? type : type.GetInterfaces().FirstOrDefault(IsEnumerableOfT);
        return enumerable?.GetGenericArguments()[0];
    }

    private static bool IsEnumerableOfT(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>);

    private static bool HasValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => false,
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Object => value.EnumerateObject().Any(),
        _ => true,
    };

    private static string Status(int filledRows, int rowCount) =>
        filledRows == 0 ? DbWatchFieldStatus.Missing
        : filledRows == rowCount ? DbWatchFieldStatus.Filled
        : DbWatchFieldStatus.Partial;

    private static string FriendlyName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);

        if (underlying is not null)
        {
            return FriendlyName(underlying) + "?";
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FriendlyName))}>";
    }
}
