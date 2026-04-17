using MonveriRegister.Models;

namespace MonveriRegister.Services;

public interface IApiService
{
    bool IsConfigured { get; }
    void Configure(string baseUrl, string apiKey);

    // Auth
    Task<ApiResult<StoreConfig>> ValidateStoreKeyAsync(string baseUrl, string apiKey);
    Task<ApiResult<Employee>> EmployeeLoginAsync(string pin);

    // Sync
    Task<ApiResult<ProductSyncResult>> SyncProductsAsync(string? since = null);
    Task<ApiResult<List<Customer>>> SyncCustomersAsync(string? since = null);
    Task<ApiResult<List<TaxLocation>>> GetTaxLocationsAsync();
    Task<ApiResult<List<Employee>>> GetEmployeesAsync();
    Task<ApiResult<StoreConfig>> GetSettingsAsync();
    Task<ApiResult<List<QuickButton>>> GetQuickButtonsAsync();
    Task<ApiResult<List<Discount>>> GetDiscountsAsync();
    Task<ApiResult<List<Category>>> GetCategoriesAsync();

    // Register sessions
    Task<ApiResult<int>> OpenSessionAsync(RegisterSession session);
    Task<ApiResult<object>> CloseSessionAsync(int sessionId, decimal closingCash, string? breakdownJson, string? notes);

    // Transactions
    Task<ApiResult<int>> SubmitTransactionAsync(Transaction transaction);
    Task<ApiResult<object>> VoidTransactionAsync(int transactionId, int employeeId);
    Task<ApiResult<List<Transaction>>> GetSuspendedTransactionsAsync();
    Task<ApiResult<object>> SuspendTransactionAsync(int transactionId, string? note);
    Task<ApiResult<Transaction>> ResumeTransactionAsync(int transactionId);

    // Batch sync
    Task<ApiResult<List<SyncResult>>> PushSyncQueueAsync(List<SyncQueueItem> items);
}

public class ApiResult<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }

    public static ApiResult<T> Ok(T data) => new() { Success = true, Data = data };
    public static ApiResult<T> Fail(string error) => new() { Success = false, Error = error };
}

public class ProductSyncResult
{
    public List<Product> Products { get; set; } = new();
    public List<Product> Variants { get; set; } = new();
    public List<BarcodeRelationship> BarcodeRelationships { get; set; } = new();
}

public class BarcodeRelationship
{
    public int Id { get; set; }
    public string ScannedBarcode { get; set; } = string.Empty;
    public string ParentSku { get; set; } = string.Empty;
    public int QtyCount { get; set; } = 1;
}

public class SyncResult
{
    public int LocalId { get; set; }
    public int? ServerId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
