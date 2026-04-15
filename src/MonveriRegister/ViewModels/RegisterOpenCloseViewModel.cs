using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonveriRegister.Helpers;
using MonveriRegister.Models;
using MonveriRegister.Services;

namespace MonveriRegister.ViewModels;

public partial class RegisterOpenCloseViewModel : ObservableObject
{
    private readonly IApiService _api;
    private readonly IDatabaseService _db;
    private readonly NavigationService _nav;
    private readonly ITaxService _taxService;
    private readonly ITransactionService _transactionService;
    private readonly ISyncService _sync;
    private readonly Employee _employee;

    [ObservableProperty] private bool _isOpening;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _isTraining;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private decimal _calculatedTotal;

    // Selected tax location
    [ObservableProperty] private TaxLocation? _selectedTaxLocation;
    public List<TaxLocation> TaxLocations { get; }

    // Session for closing
    private RegisterSession? _session;

    // Denomination counts
    [ObservableProperty] private int _bills100;
    [ObservableProperty] private int _bills50;
    [ObservableProperty] private int _bills20;
    [ObservableProperty] private int _bills10;
    [ObservableProperty] private int _bills5;
    [ObservableProperty] private int _bills1;
    [ObservableProperty] private int _coinsDollar;
    [ObservableProperty] private int _coinsHalfDollar;
    [ObservableProperty] private int _coinsQuarter;
    [ObservableProperty] private int _coinsDime;
    [ObservableProperty] private int _coinsNickel;
    [ObservableProperty] private int _coinsPenny;
    [ObservableProperty] private int _rollsDollar;
    [ObservableProperty] private int _rollsHalfDollar;
    [ObservableProperty] private int _rollsQuarter;
    [ObservableProperty] private int _rollsDime;
    [ObservableProperty] private int _rollsNickel;
    [ObservableProperty] private int _rollsPenny;

    // Straps
    [ObservableProperty] private int _straps20;
    [ObservableProperty] private int _straps10;
    [ObservableProperty] private int _straps5;
    [ObservableProperty] private int _straps1;

    public string Title => IsOpening ? "Open Register" : "Close Register";
    public string EmployeeName => _employee.Name;

    public RegisterOpenCloseViewModel(IApiService api, IDatabaseService db, NavigationService nav,
        ITaxService taxService, ITransactionService transactionService, ISyncService sync,
        Employee employee, bool isOpening, RegisterSession? session = null)
    {
        _api = api;
        _db = db;
        _nav = nav;
        _taxService = taxService;
        _transactionService = transactionService;
        _sync = sync;
        _employee = employee;
        _isOpening = isOpening;
        _session = session;
        TaxLocations = db.GetAllTaxLocations();
        SelectedTaxLocation = TaxLocations.FirstOrDefault(t => t.IsDefaultBool) ?? TaxLocations.FirstOrDefault();
    }

    private DenominationBreakdown GetBreakdown() => new()
    {
        Bills100 = Bills100, Bills50 = Bills50, Bills20 = Bills20,
        Bills10 = Bills10, Bills5 = Bills5, Bills1 = Bills1,
        CoinsDollar = CoinsDollar, CoinsHalfDollar = CoinsHalfDollar, CoinsQuarter = CoinsQuarter,
        CoinsDime = CoinsDime, CoinsNickel = CoinsNickel, CoinsPenny = CoinsPenny,
        RollsDollar = RollsDollar, RollsHalfDollar = RollsHalfDollar, RollsQuarter = RollsQuarter,
        RollsDime = RollsDime, RollsNickel = RollsNickel, RollsPenny = RollsPenny,
        Straps20 = Straps20, Straps10 = Straps10, Straps5 = Straps5, Straps1 = Straps1,
    };

    private void RecalculateTotal()
    {
        CalculatedTotal = GetBreakdown().CalculateTotal();
    }

    // Recalculate on any denomination change
    partial void OnBills100Changed(int value) => RecalculateTotal();
    partial void OnBills50Changed(int value) => RecalculateTotal();
    partial void OnBills20Changed(int value) => RecalculateTotal();
    partial void OnBills10Changed(int value) => RecalculateTotal();
    partial void OnBills5Changed(int value) => RecalculateTotal();
    partial void OnBills1Changed(int value) => RecalculateTotal();
    partial void OnCoinsDollarChanged(int value) => RecalculateTotal();
    partial void OnCoinsHalfDollarChanged(int value) => RecalculateTotal();
    partial void OnCoinsQuarterChanged(int value) => RecalculateTotal();
    partial void OnCoinsDimeChanged(int value) => RecalculateTotal();
    partial void OnCoinsNickelChanged(int value) => RecalculateTotal();
    partial void OnCoinsPennyChanged(int value) => RecalculateTotal();
    partial void OnRollsDollarChanged(int value) => RecalculateTotal();
    partial void OnRollsHalfDollarChanged(int value) => RecalculateTotal();
    partial void OnRollsQuarterChanged(int value) => RecalculateTotal();
    partial void OnRollsDimeChanged(int value) => RecalculateTotal();
    partial void OnRollsNickelChanged(int value) => RecalculateTotal();
    partial void OnRollsPennyChanged(int value) => RecalculateTotal();
    partial void OnStraps20Changed(int value) => RecalculateTotal();
    partial void OnStraps10Changed(int value) => RecalculateTotal();
    partial void OnStraps5Changed(int value) => RecalculateTotal();
    partial void OnStraps1Changed(int value) => RecalculateTotal();

    [RelayCommand]
    private async Task OpenRegister()
    {
        IsProcessing = true;
        StatusMessage = "Opening register...";

        var breakdown = GetBreakdown();
        var session = new RegisterSession
        {
            EmployeeId = _employee.Id,
            EmployeeName = _employee.Name,
            OpenedAt = DateTime.UtcNow.ToString("o"),
            OpeningCash = breakdown.CalculateTotal(),
            OpeningBreakdown = breakdown.ToJson(),
            Status = "open",
            IsTraining = IsTraining,
            TaxLocationId = SelectedTaxLocation?.Id,
        };

        // Save locally first
        session.Id = _db.InsertSession(session);

        // Try to sync to server
        var result = await _api.OpenSessionAsync(session);
        if (result.Success)
        {
            session.ServerId = result.Data;
            session.IsSynced = true;
            _db.UpdateSession(session);
        }

        StatusMessage = "Register opened!";
        IsProcessing = false;

        // Navigate to main register
        _nav.NavigateTo(new RegisterViewModel(
            _api, _db, _sync, _nav, _taxService, _transactionService,
            _employee, session));
    }

    [RelayCommand]
    private async Task CloseRegister()
    {
        if (_session == null) return;

        IsProcessing = true;
        StatusMessage = "Closing register...";

        var breakdown = GetBreakdown();
        _session.ClosedAt = DateTime.UtcNow.ToString("o");
        _session.ClosingCash = breakdown.CalculateTotal();
        _session.ClosingBreakdown = breakdown.ToJson();
        _session.Status = "closed";
        _session.Notes = Notes;
        _db.UpdateSession(_session);

        // Try to sync
        if (_session.ServerId.HasValue)
        {
            await _api.CloseSessionAsync(_session.ServerId.Value, _session.ClosingCash.Value,
                _session.ClosingBreakdown, _session.Notes);
        }

        StatusMessage = "Register closed!";
        IsProcessing = false;

        // Back to login
        _nav.NavigateTo(new LoginViewModel(_api, _db, _sync, _nav));
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsOpening)
            _nav.NavigateTo(new LoginViewModel(_api, _db, _sync, _nav));
        else if (_session != null)
            _nav.NavigateTo(new RegisterViewModel(
                _api, _db, _sync, _nav, _taxService, _transactionService,
                _employee, _session));
    }
}
