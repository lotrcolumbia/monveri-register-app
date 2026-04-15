using System.Text.Json;
using MonveriRegister.Models;

namespace MonveriRegister.Services;

public interface ISyncService
{
    bool IsOnline { get; }
    string SyncStatus { get; }
    event Action<bool>? OnlineStatusChanged;
    event Action<string>? SyncStatusChanged;
    Task StartAsync();
    void Stop();
    Task SyncNowAsync();
    Task InitialSyncAsync();
}

public class SyncService : ISyncService
{
    private readonly IApiService _api;
    private readonly IDatabaseService _db;
    private Timer? _timer;
    private bool _isSyncing;

    public bool IsOnline { get; private set; }
    public string SyncStatus { get; private set; } = "Idle";
    public event Action<bool>? OnlineStatusChanged;
    public event Action<string>? SyncStatusChanged;

    public SyncService(IApiService api, IDatabaseService db)
    {
        _api = api;
        _db = db;
    }

    public async Task StartAsync()
    {
        await CheckConnectivity();
        _timer = new Timer(async _ => await SyncNowAsync(), null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5));
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public async Task InitialSyncAsync()
    {
        var errors = new List<string>();
        SetStatus("Syncing...");

        // Pull employees
        try
        {
            SetStatus("Syncing employees...");
            var empResult = await _api.GetEmployeesAsync();
            if (empResult.Success && empResult.Data != null)
                _db.SaveEmployees(empResult.Data);
            else if (!empResult.Success)
                errors.Add($"Employees: {empResult.Error}");
        }
        catch (Exception ex) { errors.Add($"Employees: {ex.Message}"); }

        // Pull tax locations
        try
        {
            SetStatus("Syncing tax locations...");
            var taxResult = await _api.GetTaxLocationsAsync();
            if (taxResult.Success && taxResult.Data != null)
            {
                errors.Add($"Tax: got {taxResult.Data.Count} locations");
                _db.SaveTaxLocations(taxResult.Data);
                var verify = _db.GetAllTaxLocations();
                errors.Add($"Tax: saved {verify.Count} to DB");
            }
            else
                errors.Add($"Tax fail: success={taxResult.Success} data={taxResult.Data != null} err={taxResult.Error}");
        }
        catch (Exception ex) { errors.Add($"Tax: {ex.Message}"); }

        // Pull discounts
        try
        {
            SetStatus("Syncing discounts...");
            var discResult = await _api.GetDiscountsAsync();
            if (discResult.Success && discResult.Data != null)
                _db.SaveDiscounts(discResult.Data);
        }
        catch (Exception ex) { errors.Add($"Discounts: {ex.Message}"); }

        // Pull categories
        try
        {
            SetStatus("Syncing categories...");
            var catResult = await _api.GetCategoriesAsync();
            if (catResult.Success && catResult.Data != null)
                _db.SaveCategories(catResult.Data);
        }
        catch (Exception ex) { errors.Add($"Categories: {ex.Message}"); }

        // Pull quick buttons
        try
        {
            SetStatus("Syncing quick buttons...");
            var qbResult = await _api.GetQuickButtonsAsync();
            if (qbResult.Success && qbResult.Data != null)
                _db.SetConfig("quick_buttons", System.Text.Json.JsonSerializer.Serialize(qbResult.Data));
        }
        catch (Exception ex) { errors.Add($"QuickButtons: {ex.Message}"); }

        // Pull settings
        try
        {
            SetStatus("Syncing settings...");
            var settingsResult = await _api.GetSettingsAsync();
            if (settingsResult.Success && settingsResult.Data != null)
            {
                var cfg = settingsResult.Data;
                _db.SetConfig("nickel_rounding", cfg.NickelRoundingEnabled ? "1" : "0");
                _db.SetConfig("loyalty_enabled", cfg.LoyaltyEnabled ? "1" : "0");
                _db.SetConfig("service_fees_enabled", cfg.ServiceFeesEnabled ? "1" : "0");
                _db.SetConfig("service_fee_percent", cfg.ServiceFeePercent.ToString());
                _db.SetConfig("service_fee_taxable", cfg.ServiceFeeTaxable ? "1" : "0");
            }
        }
        catch (Exception ex) { errors.Add($"Settings: {ex.Message}"); }

        // Pull products
        try
        {
            SetStatus("Syncing products...");
            var lastSync = _db.GetConfig("last_product_sync");
            var prodResult = await _api.SyncProductsAsync(lastSync);
            if (prodResult.Success && prodResult.Data != null)
            {
                _db.UpsertProducts(prodResult.Data.Products);
                foreach (var v in prodResult.Data.Variants)
                {
                    v.IsVariant = 1;
                    _db.UpsertProduct(v);
                }
                _db.SetConfig("last_product_sync", DateTime.UtcNow.ToString("o"));
            }
        }
        catch (Exception ex) { errors.Add($"Products: {ex.Message}"); }

        // Pull customers
        try
        {
            SetStatus("Syncing customers...");
            var lastCustSync = _db.GetConfig("last_customer_sync");
            var custResult = await _api.SyncCustomersAsync(lastCustSync);
            if (custResult.Success && custResult.Data != null)
            {
                _db.UpsertCustomers(custResult.Data);
                _db.SetConfig("last_customer_sync", DateTime.UtcNow.ToString("o"));
            }
        }
        catch (Exception ex) { errors.Add($"Customers: {ex.Message}"); }

        SetOnline(true);
        if (errors.Count > 0)
            SetStatus($"Sync issues: {string.Join("; ", errors)}");
        else
            SetStatus("Synced");
    }

    public async Task SyncNowAsync()
    {
        if (_isSyncing) return;
        _isSyncing = true;

        try
        {
            await CheckConnectivity();
            if (!IsOnline) return;

            SetStatus("Syncing...");

            // Push offline queue
            var pending = _db.GetPendingSyncItems();
            if (pending.Count > 0)
            {
                var result = await _api.PushSyncQueueAsync(pending);
                if (result.Success && result.Data != null)
                {
                    foreach (var r in result.Data)
                    {
                        if (r.Success)
                            _db.MarkSynced(r.LocalId);
                        else
                            _db.MarkSyncFailed(r.LocalId, r.Error ?? "Unknown error");
                    }
                }
            }

            // Pull delta updates
            var lastSync = _db.GetConfig("last_product_sync");
            var prodResult = await _api.SyncProductsAsync(lastSync);
            if (prodResult.Success && prodResult.Data != null)
            {
                _db.UpsertProducts(prodResult.Data.Products);
                _db.SetConfig("last_product_sync", DateTime.UtcNow.ToString("o"));
            }

            SetStatus("Synced");
        }
        catch
        {
            SetStatus("Sync error");
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task CheckConnectivity()
    {
        if (!_api.IsConfigured)
        {
            SetOnline(false);
            return;
        }

        try
        {
            var baseUrl = _db.GetConfig("base_url") ?? "";
            var apiKey = _db.GetConfig("store_api_key") ?? "";
            var result = await _api.ValidateStoreKeyAsync(baseUrl, apiKey);
            SetOnline(result.Success);
        }
        catch
        {
            SetOnline(false);
        }
    }

    private void SetOnline(bool online)
    {
        if (IsOnline != online)
        {
            IsOnline = online;
            OnlineStatusChanged?.Invoke(online);
        }
    }

    private void SetStatus(string status)
    {
        SyncStatus = status;
        SyncStatusChanged?.Invoke(status);
    }
}
