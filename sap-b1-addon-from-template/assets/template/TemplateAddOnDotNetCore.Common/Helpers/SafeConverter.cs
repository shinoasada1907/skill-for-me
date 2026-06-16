namespace TemplateAddOnDotNetCore.Common.Helpers;

public static class SafeConverter
{
    public static int ToInt32(object? value)
    {
        try { return Convert.ToInt32(value); }
        catch { return 0; }
    }

    public static double ToDouble(object? value)
    {
        try { return Convert.ToDouble(value); }
        catch { return 0; }
    }

    public static DateTime ToDateTime(object? value)
    {
        try { return Convert.ToDateTime(value); }
        catch { return new DateTime(1900, 1, 1); }
    }

    public static bool ToBoolean(object? value)
    {
        try { return Convert.ToBoolean(value); }
        catch { return false; }
    }

    public static string ToString(object? value)
    {
        return value?.ToString() ?? string.Empty;
    }

    public static string ToBase64(string plainText)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        return Convert.ToBase64String(bytes);
    }

    public static string FromBase64(string base64String)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64String);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch { return base64String; }
    }

    public static string ToJson(object? obj)
    {
        return obj != null
            ? System.Text.Json.JsonSerializer.Serialize(obj)
            : string.Empty;
    }

    public static T? FromJson<T>(string json)
    {
        return !string.IsNullOrEmpty(json)
            ? System.Text.Json.JsonSerializer.Deserialize<T>(json)
            : default;
    }
}
