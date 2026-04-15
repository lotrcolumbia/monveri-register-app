using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonveriRegister.Helpers;
using MonveriRegister.Models;
using MonveriRegister.Services;

namespace MonveriRegister.ViewModels;

public partial class PaymentViewModel : ObservableObject
{
    private readonly IApiService _api;
    private readonly IDatabaseService _db;
    private readonly ISyncService _sync;
    private readonly NavigationService _nav;
    private readonly ITaxService _taxService;
    private readonly ITransactionService _transactionService;
    private readonly Employee _employee;
    private readonly RegisterSession _session;
    private readonly Transaction _transaction;

    [ObservableProperty] private string _paymentMethod;
    [ObservableProperty] private decimal _totalDue;
    [ObservableProperty] private string _tenderedEntry = string.Empty;
    [ObservableProperty] private decimal _tenderedAmount;
    [ObservableProperty] private decimal _changeAmount;
    [ObservableProperty] private bool _showChange;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _receiptToken = string.Empty;

    // Totals breakdown display
    public decimal Subtotal { get; }
    public decimal Discount { get; }
    public decimal Tax { get; }
    public decimal ServiceFee { get; }

    public bool IsCash => PaymentMethod == "Cash";
    public bool IsCredit => PaymentMethod == "Credit";

    public PaymentViewModel(IApiService api, IDatabaseService db, ISyncService sync, NavigationService nav,
        ITaxService taxService, ITransactionService transactionService,
        Employee employee, RegisterSession session, Transaction transaction,
        TransactionTotals totals, string paymentMethod)
    {
        _api = api;
        _db = db;
        _sync = sync;
        _nav = nav;
        _taxService = taxService;
        _transactionService = transactionService;
        _employee = employee;
        _session = session;
        _transaction = transaction;
        _paymentMethod = paymentMethod;

        Subtotal = totals.Subtotal;
        Discount = totals.Discount;
        Tax = totals.Tax;
        ServiceFee = totals.ServiceFee + totals.ServiceFeeTax;
        TotalDue = totals.Total;
        ReceiptToken = transaction.ReceiptToken ?? "";
    }

    #region Numpad for cash entry

    [RelayCommand]
    private void AppendDigit(string digit)
    {
        TenderedEntry += digit;
        UpdateTendered();
    }

    [RelayCommand]
    private void AppendDecimal()
    {
        if (!TenderedEntry.Contains('.'))
            TenderedEntry += ".";
    }

    [RelayCommand]
    private void Backspace()
    {
        if (TenderedEntry.Length > 0)
        {
            TenderedEntry = TenderedEntry[..^1];
            UpdateTendered();
        }
    }

    [RelayCommand]
    private void ClearEntry()
    {
        TenderedEntry = string.Empty;
        TenderedAmount = 0;
        ChangeAmount = 0;
        ShowChange = false;
    }

    [RelayCommand]
    private void QuickCash(string amount)
    {
        if (decimal.TryParse(amount, out var val))
        {
            TenderedEntry = val.ToString("F2");
            UpdateTendered();
        }
    }

    [RelayCommand]
    private void ExactCash()
    {
        TenderedEntry = TotalDue.ToString("F2");
        UpdateTendered();
    }

    private void UpdateTendered()
    {
        if (decimal.TryParse(TenderedEntry, out var val))
        {
            TenderedAmount = val;
            ChangeAmount = Math.Max(0, val - TotalDue);
            ShowChange = val >= TotalDue;
        }
        else
        {
            TenderedAmount = 0;
            ChangeAmount = 0;
            ShowChange = false;
        }
    }

    #endregion

    [RelayCommand]
    private async Task ProcessPayment()
    {
        IsProcessing = true;

        if (IsCash)
        {
            if (TenderedAmount < TotalDue)
            {
                StatusMessage = "Insufficient amount tendered.";
                IsProcessing = false;
                return;
            }

            var taxLocation = _session.TaxLocationId.HasValue
                ? _db.GetTaxLocation(_session.TaxLocationId.Value)
                : _db.GetDefaultTaxLocation();

            _transactionService.FinalizeCash(_transaction, taxLocation, TenderedAmount);
            ChangeAmount = _transaction.ChangeAmount ?? 0;
            ShowChange = true;
        }
        else if (IsCredit)
        {
            var taxLocation = _session.TaxLocationId.HasValue
                ? _db.GetTaxLocation(_session.TaxLocationId.Value)
                : _db.GetDefaultTaxLocation();

            _transactionService.FinalizeCredit(_transaction, taxLocation);
        }

        StatusMessage = "Payment complete!";
        IsComplete = true;
        IsProcessing = false;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void NewTransaction()
    {
        // Navigate back to register with a new transaction
        _nav.NavigateTo(new RegisterViewModel(
            _api, _db, _sync, _nav, _taxService, _transactionService,
            _employee, _session));
    }

    [RelayCommand]
    private void Cancel()
    {
        // Go back to register with existing transaction
        _nav.NavigateTo(new RegisterViewModel(
            _api, _db, _sync, _nav, _taxService, _transactionService,
            _employee, _session));
    }
}
