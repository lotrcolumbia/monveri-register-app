using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MonveriRegister.Models;

namespace MonveriRegister.Services;

public class ApiService : IApiService
{
    private HttpClient? _client;
    private string _baseUrl = string.Empty;
    private string _apiKey = string.Empty;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public bool IsConfigured => _client != null && !string.IsNullOrEmpty(_apiKey);

    public void Configure(string baseUrl, string apiKey)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _client = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl + "/api/register/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        _client.DefaultRequestHeaders.Add("X-Store-Key", _apiKey);
    }

    public async Task<ApiResult<StoreConfig>> ValidateStoreKeyAsync(string baseUrl, string apiKey)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("X-Store-Key", apiKey);
            var response = await client.PostAsync($"{baseUrl.TrimEnd('/')}/api/register/auth/validate-key.php", null);
            if (!response.IsSuccessStatusCode)
                return ApiResult<StoreConfig>.Fail($"Server returned {(int)response.StatusCode}");

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("success", out var s) && s.GetBoolean())
            {
                var config = new StoreConfig
                {
                    ApiKey = apiKey,
                    BaseUrl = baseUrl,
                    StoreName = json.GetProperty("store_name").GetString() ?? "",
                    StoreCode = json.GetProperty("store_code").GetString() ?? "",
                };
                return ApiResult<StoreConfig>.Ok(config);
            }
            var msg = json.TryGetProperty("message", out var m) ? m.GetString() : "Validation failed";
            return ApiResult<StoreConfig>.Fail(msg ?? "Validation failed");
        }
        catch (Exception ex)
        {
            return ApiResult<StoreConfig>.Fail($"Connection failed: {ex.Message}");
        }
    }

    public async Task<ApiResult<Employee>> EmployeeLoginAsync(string pin)
    {
        return await PostAsync<Employee>("auth/employee-login.php", new { pin });
    }

    public async Task<ApiResult<ProductSyncResult>> SyncProductsAsync(string? since = null)
    {
        var url = "products/sync.php";
        if (!string.IsNullOrEmpty(since)) url += $"?since={Uri.EscapeDataString(since)}";
        return await GetAsync<ProductSyncResult>(url);
    }

    public async Task<ApiResult<List<Customer>>> SyncCustomersAsync(string? since = null)
    {
        var url = "customers/sync.php";
        if (!string.IsNullOrEmpty(since)) url += $"?since={Uri.EscapeDataString(since)}";
        return await GetAsync<List<Customer>>(url);
    }

    public async Task<ApiResult<List<TaxLocation>>> GetTaxLocationsAsync()
    {
        return await GetAsync<List<TaxLocation>>("config/tax-locations.php");
    }

    public async Task<ApiResult<List<Employee>>> GetEmployeesAsync()
    {
        return await GetAsync<List<Employee>>("config/employees.php");
    }

    public async Task<ApiResult<StoreConfig>> GetSettingsAsync()
    {
        return await GetAsync<StoreConfig>("config/settings.php");
    }

    public async Task<ApiResult<List<QuickButton>>> GetQuickButtonsAsync()
    {
        return await GetAsync<List<QuickButton>>("config/quick-buttons.php");
    }

    public async Task<ApiResult<List<Discount>>> GetDiscountsAsync()
    {
        return await GetAsync<List<Discount>>("config/discounts.php");
    }

    public async Task<ApiResult<List<Category>>> GetCategoriesAsync()
    {
        return await GetAsync<List<Category>>("config/categories.php");
    }

    public async Task<ApiResult<int>> OpenSessionAsync(RegisterSession session)
    {
        return await PostAsync<int>("session/open.php", session);
    }

    public async Task<ApiResult<object>> CloseSessionAsync(int sessionId, decimal closingCash, string? breakdownJson, string? notes)
    {
        return await PostAsync<object>("session/close.php", new
        {
            session_id = sessionId,
            closing_cash = closingCash,
            closing_breakdown = breakdownJson,
            notes
        });
    }

    public async Task<ApiResult<int>> SubmitTransactionAsync(Transaction transaction)
    {
        return await PostAsync<int>("transactions/submit.php", transaction);
    }

    public async Task<ApiResult<object>> VoidTransactionAsync(int transactionId, int employeeId)
    {
        return await PostAsync<object>("transactions/void.php", new { transaction_id = transactionId, employee_id = employeeId });
    }

    public async Task<ApiResult<List<Transaction>>> GetSuspendedTransactionsAsync()
    {
        return await GetAsync<List<Transaction>>("transactions/suspended.php");
    }

    public async Task<ApiResult<object>> SuspendTransactionAsync(int transactionId, string? note)
    {
        return await PostAsync<object>("transactions/suspend.php", new { transaction_id = transactionId, note });
    }

    public async Task<ApiResult<Transaction>> ResumeTransactionAsync(int transactionId)
    {
        return await PostAsync<Transaction>("transactions/resume.php", new { transaction_id = transactionId });
    }

    public async Task<ApiResult<List<SyncResult>>> PushSyncQueueAsync(List<SyncQueueItem> items)
    {
        return await PostAsync<List<SyncResult>>("sync/push.php", new { items });
    }

    private async Task<ApiResult<T>> GetAsync<T>(string endpoint)
    {
        if (_client == null) return ApiResult<T>.Fail("Not configured");
        try
        {
            var response = await _client.GetAsync(endpoint);
            return await ParseResponse<T>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<T>.Fail(ex.Message);
        }
    }

    private async Task<ApiResult<T>> PostAsync<T>(string endpoint, object payload)
    {
        if (_client == null) return ApiResult<T>.Fail("Not configured");
        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(endpoint, content);
            return await ParseResponse<T>(response);
        }
        catch (Exception ex)
        {
            return ApiResult<T>.Fail(ex.Message);
        }
    }

    private static async Task<ApiResult<T>> ParseResponse<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        // Debug: log raw responses to help diagnose sync issues
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonveriRegister", "logs");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, "api_debug.log");
            var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {typeof(T).Name} ({(int)response.StatusCode}): {(body.Length > 500 ? body[..500] + "..." : body)}\n";
            File.AppendAllText(logFile, logLine);
        }
        catch { /* ignore logging errors */ }

        if (!response.IsSuccessStatusCode)
            return ApiResult<T>.Fail($"HTTP {(int)response.StatusCode}: {body}");

        var json = JsonSerializer.Deserialize<JsonElement>(body);
        if (json.TryGetProperty("success", out var s) && s.GetBoolean())
        {
            if (json.TryGetProperty("data", out var data))
            {
                try
                {
                    var result = JsonSerializer.Deserialize<T>(data.GetRawText(), JsonOptions);
                    return ApiResult<T>.Ok(result!);
                }
                catch (Exception ex)
                {
                    return ApiResult<T>.Fail($"Deserialize error: {ex.Message}");
                }
            }
            return ApiResult<T>.Ok(default!);
        }
        var msg = json.TryGetProperty("message", out var m) ? m.GetString() : "Request failed";
        return ApiResult<T>.Fail(msg ?? "Request failed");
    }
}
