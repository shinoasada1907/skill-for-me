namespace TemplateAddOnDotNetCore.Common.Helpers;

public static class SqlHelper
{
    public static string EscapeString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Replace("'", "''");
    }

    public static bool IsValidDate(object? text)
    {
        try
        {
            string t = text?.ToString()?.Trim() ?? string.Empty;

            if (t.Length == 8)
                t = $"{t[..4]}-{t[4..6]}-{t[6..8]}";
            else if (t.Length > 10)
                t = t[..10];

            _ = Convert.ToDateTime(t);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
