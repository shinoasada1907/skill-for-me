using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace TemplateAddOnDotNetCore.DAL.ServiceLayer;

/// <summary>
/// HTTP client for SAP B1 Service Layer REST API.
/// Usage:
///   var client = new ServiceLayerClient("https://server:50000");
///   await client.LoginAsync("CompanyDB", "manager", "password");
///   var orders = await client.GetAsync("Orders?$top=10");
///   await client.LogoutAsync();
/// </summary>
public class ServiceLayerClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private string? _sessionId;

    public ServiceLayerClient(string baseUrl)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/'))
        };
    }

    public bool IsLoggedIn => _sessionId != null;

    public async Task<bool> LoginAsync(string companyDb, string userName, string password)
    {
        var payload = new { CompanyDB = companyDb, UserName = userName, Password = password };
        var response = await _httpClient.PostAsJsonAsync("/b1s/v1/Login", payload);

        if (!response.IsSuccessStatusCode)
            return false;

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        _sessionId = result.GetProperty("SessionId").GetString();
        _httpClient.DefaultRequestHeaders.Remove("Cookie");
        _httpClient.DefaultRequestHeaders.Add("Cookie", $"B1SESSION={_sessionId}");
        return true;
    }

    public async Task LogoutAsync()
    {
        if (_sessionId == null) return;

        await _httpClient.PostAsync("/b1s/v1/Logout", null);
        _sessionId = null;
        _httpClient.DefaultRequestHeaders.Remove("Cookie");
    }

    public async Task<T?> GetAsync<T>(string endpoint)
    {
        var response = await _httpClient.GetAsync($"/b1s/v1/{endpoint}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }

    public async Task<JsonElement> GetAsync(string endpoint)
    {
        var response = await _httpClient.GetAsync($"/b1s/v1/{endpoint}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> PostAsync(string endpoint, object payload)
    {
        var response = await _httpClient.PostAsJsonAsync($"/b1s/v1/{endpoint}", payload);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task<JsonElement> PatchAsync(string endpoint, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PatchAsync($"/b1s/v1/{endpoint}", content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task DeleteAsync(string endpoint)
    {
        var response = await _httpClient.DeleteAsync($"/b1s/v1/{endpoint}");
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        LogoutAsync().GetAwaiter().GetResult();
        _httpClient.Dispose();
    }
}
