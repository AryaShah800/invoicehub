using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using InvoiceHub.Application.Auth;

namespace InvoiceHub.IntegrationTests.Api;

internal static class TestData
{
    public const string Password = "Str0ng!Passw0rd";

    private static readonly Random Random = new();

    public static string NewEmail() => $"{Guid.NewGuid():N}@test.example";

    /// <summary>A GSTIN in the correct format with a random PAN part. State code 27.</summary>
    public static string NewGstin()
    {
        string Letters(int n) => new(Enumerable.Range(0, n).Select(_ => (char)('A' + Random.Next(26))).ToArray());
        return $"27{Letters(5)}{Random.Next(1000, 9999)}{Letters(1)}1Z{Random.Next(1, 9)}";
    }

    public static RegisterTenantRequest NewRegistration(string? email = null, string? gstin = null, string? password = null) =>
        new("Test Traders", gstin ?? NewGstin(), "27", email ?? NewEmail(), password ?? Password);
}

internal static class HttpClientExtensions
{
    public static async Task<AuthResponse> RegisterAsync(this HttpClient client, RegisterTenantRequest? request = null)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", request ?? TestData.NewRegistration());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    public static async Task<AuthResponse> LoginAsync(this HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    public static HttpRequestMessage WithBearer(this HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public static Task<HttpResponseMessage> GetAsync(this HttpClient client, string url, string token) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url).WithBearer(token));

    public static Task<HttpResponseMessage> PostAsync<T>(this HttpClient client, string url, T body, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (token is not null)
            request.WithBearer(token);
        return client.SendAsync(request);
    }
}
