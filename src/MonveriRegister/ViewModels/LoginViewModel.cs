using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonveriRegister.Helpers;
using MonveriRegister.Models;
using MonveriRegister.Services;

namespace MonveriRegister.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IApiService _api;
    private readonly IDatabaseService _db;
    private readonly ISyncService _sync;
    private readonly NavigationService _nav;
    private readonly ITaxService _taxService;
    private readonly ITransactionService _transactionService;

    [ObservableProperty] private string _serverUrl = string.Empty;
    [ObservableProperty] private string _storeKey = string.Empty;
    [ObservableProperty] private string _employeePin = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _storeName = string.Empty;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private bool _isStoreConnected;
    [ObservableProperty] private bool _isLoggingIn;
    [ObservableProperty] private bool _showPinEntry;
    private Task? _syncTask;

    public LoginViewModel(IApiService api, IDatabaseService db, ISyncService sync, NavigationService nav,
        ITaxService taxService, ITransactionService transactionService)
    {
        _api = api;
        _db = db;
        _sync = sync;
        _nav = nav;
        _taxService = taxService;
        _transactionService = transactionService;

        // Load saved connection
        var savedUrl = _db.GetConfig("base_url");
        var savedKey = _db.GetConfig("store_api_key");
        var savedName = _db.GetConfig("store_name");
        if (!string.IsNullOrEmpty(savedUrl) && !string.IsNullOrEmpty(savedKey))
        {
            ServerUrl = savedUrl;
            StoreKey = savedKey;
            StoreName = savedName ?? "";
            _api.Configure(savedUrl, savedKey);
            IsStoreConnected = true;
            ShowPinEntry = true;
            // Sync in background on reconnect
            _syncTask = Task.Run(async () =>
            {
                try { await _sync.InitialSyncAsync(); }
                catch { /* offline is fine */ }
            });
        }
    }

    [RelayCommand]
    private async Task ConnectToStore()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(StoreKey))
        {
            StatusMessage = "Please enter both server URL and store key.";
            return;
        }

        IsConnecting = true;
        StatusMessage = "Connecting to store...";

        var result = await _api.ValidateStoreKeyAsync(ServerUrl, StoreKey);
        if (result.Success && result.Data != null)
        {
            var config = result.Data;
            StoreName = config.StoreName;
            StatusMessage = $"Connected to {config.StoreName}";

            // Save and configure
            _db.SetConfig("base_url", ServerUrl);
            _db.SetConfig("store_api_key", StoreKey);
            _db.SetConfig("store_name", config.StoreName);
            _db.SetConfig("store_code", config.StoreCode);
            _api.Configure(ServerUrl, StoreKey);

            // Run initial sync
            StatusMessage = "Syncing data...";
            await _sync.InitialSyncAsync();

            IsStoreConnected = true;
            ShowPinEntry = true;
            StatusMessage = "Enter your PIN to log in.";
        }
        else
        {
            StatusMessage = result.Error ?? "Failed to connect.";
        }

        IsConnecting = false;
    }

    [RelayCommand]
    private async Task EmployeeLogin()
    {
        if (string.IsNullOrWhiteSpace(EmployeePin))
        {
            StatusMessage = "Please enter your PIN.";
            return;
        }

        IsLoggingIn = true;

        // Always run a fresh sync before navigating
        StatusMessage = "Syncing data...";
        try { await _sync.InitialSyncAsync(); }
        catch { /* offline is fine */ }

        StatusMessage = "Logging in...";

        // Try online first
        var onlineResult = await _api.EmployeeLoginAsync(EmployeePin);
        if (onlineResult.Success && onlineResult.Data != null)
        {
            var emp = onlineResult.Data;
            StatusMessage = $"Welcome, {emp.Name}!";
            NavigateToRegister(emp);
        }
        else
        {
            // Fall back to offline — check cached employees by PIN hash
            var employees = _db.GetAllEmployees();
            var match = employees.FirstOrDefault(e =>
                BCryptVerify(EmployeePin, e.PinHash));

            if (match != null)
            {
                StatusMessage = $"Welcome, {match.Name}! (Offline)";
                NavigateToRegister(match);
            }
            else
            {
                StatusMessage = onlineResult.Error ?? "Invalid PIN.";
            }
        }

        IsLoggingIn = false;
    }

    [RelayCommand]
    private void DisconnectStore()
    {
        _db.SetConfig("base_url", "");
        _db.SetConfig("store_api_key", "");
        _db.SetConfig("store_name", "");
        ServerUrl = string.Empty;
        StoreKey = string.Empty;
        StoreName = string.Empty;
        IsStoreConnected = false;
        ShowPinEntry = false;
        StatusMessage = "Disconnected.";
    }

    [RelayCommand]
    private void AppendPin(string digit)
    {
        if (EmployeePin.Length < 8)
            EmployeePin += digit;
    }

    [RelayCommand]
    private void ClearPin()
    {
        EmployeePin = string.Empty;
    }

    [RelayCommand]
    private void BackspacePin()
    {
        if (EmployeePin.Length > 0)
            EmployeePin = EmployeePin[..^1];
    }

    private void NavigateToRegister(Employee employee)
    {
        var session = _db.GetOpenSession(employee.Id);
        if (session != null)
        {
            _nav.NavigateTo(new RegisterViewModel(
                _api, _db, _sync, _nav, _taxService, _transactionService,
                employee, session));
        }
        else
        {
            _nav.NavigateTo(new RegisterOpenCloseViewModel(
                _api, _db, _nav, _taxService, _transactionService,
                _sync, employee, isOpening: true));
        }
    }

    private static bool BCryptVerify(string pin, string hash)
    {
        // Simple hash comparison for offline mode
        // The server sends bcrypt hashes; for true offline verification
        // we'd need a BCrypt library. For MVP, we compare the raw PIN
        // stored as a SHA256 hash fallback alongside the bcrypt hash.
        try
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(pin));
            var computed = Convert.ToHexString(bytes).ToLower();
            // Check if hash contains a SHA256 fallback (stored as sha256:HASH)
            if (hash.StartsWith("sha256:"))
                return hash[7..] == computed;
            // If it's a bcrypt hash, we can't verify offline without the library
            return false;
        }
        catch
        {
            return false;
        }
    }
}
