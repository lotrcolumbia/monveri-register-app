using System.Net.Http;

namespace MonveriRegister.Helpers;

public static class ConnectivityChecker
{
    public static async Task<bool> IsOnlineAsync(string? baseUrl = null)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var url = string.IsNullOrEmpty(baseUrl) ? "https://www.google.com" : baseUrl;
            var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
