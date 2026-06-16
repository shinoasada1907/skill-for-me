using TemplateAddOnDotNetCore.Common.Enums;
using TemplateAddOnDotNetCore.Common.Helpers;

namespace TemplateAddOnDotNetCore.DAL;

/// <summary>
/// Builds SQL queries compatible with both SAP HANA and SQL Server.
/// </summary>
public static class QueryBuilder
{
    public static string BuildExecuteStore(
        string storeName,
        Dictionary<string, object> parameters,
        DatabaseType dbType)
    {
        var parts = new List<string>();

        foreach (var (key, value) in parameters)
        {
            string formatted = FormatValue(value);

            if (dbType == DatabaseType.Hana)
                parts.Add($"{key}=>{formatted}");
            else
                parts.Add($"@{key}={formatted}");
        }

        string paramString = string.Join(",", parts);

        return dbType == DatabaseType.Hana
            ? $"CALL \"{storeName}\"({paramString})"
            : $"EXEC {storeName} {paramString}";
    }

    public static string BuildUpdateQuery(
        string tableName,
        Dictionary<string, object> values,
        Dictionary<string, object> conditions)
    {
        var setParts = new List<string>();
        foreach (var (key, value) in values)
            setParts.Add($"\"{key}\"={FormatValue(value)}");

        var condParts = new List<string>();
        foreach (var (key, value) in conditions)
            condParts.Add($"\"{key}\"={FormatValue(value)}");

        return $"UPDATE {tableName} SET {string.Join(",", setParts)} WHERE {string.Join(" AND ", condParts)}";
    }

    private static string FormatValue(object? value)
    {
        if (value == null) return "null";
        if (value is bool b) return b.ToString().ToLower();

        string escaped = SqlHelper.EscapeString(value.ToString());
        return $"'{escaped}'";
    }
}
