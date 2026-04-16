using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MonveriRegister.Helpers;
using MonveriRegister.Models;
using MonveriRegister.Services;

namespace MonveriRegister.ViewModels;

public partial class RegisterViewModel : ObservableObject
{
    private readonly IApiService _api;
    private readonly IDatabaseService _db;
    private readonly ISyncService _sync;
    private readonly NavigationService _nav;
    private readonly ITaxService _taxService;
    private readonly ITransactionService _transactionService;
    private readonly Employee _employee;
    private readonly RegisterSession _session;

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private ObservableCollection<Product> _searchResults = new();
    [ObservableProperty] private ObservableCollection<CartItem> _cartItems = new();
    [ObservableProperty] private Transaction? _activeTransaction;
    [ObservableProperty] private Customer? _attachedCustomer;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _showPaymentPanel;
    [ObservableProperty] private bool _showCustomerLookup;
    [ObservableProperty] private bool _showSuspendedList;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // Totals display
    [ObservableProperty] private decimal _subtotal;
    [ObservableProperty] private decimal _discountTotal;
    [ObservableProperty] private decimal _taxTotal;
    [ObservableProperty] private decimal _serviceFeeTotalDisplay;
    [ObservableProperty] private decimal _grandTotal;
    [ObservableProperty] private string _discountDescription = string.Empty;

    // Sync indicator
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private string _syncStatus = "Idle";

    // Quick buttons
    [ObservableProperty] private ObservableCollection<QuickButton> _quickButtons = new();

    // Discount overlay
    [ObservableProperty] private bool _showDiscountPanel;

    // Layout info
    public string EmployeeName => _employee.Name;
    public string StoreName => _db.GetConfig("store_name") ?? "Monveri Register";
    public string SessionInfo => $"{_session.Id}";
    public bool IsTraining => _session.IsTraining;
    public string CurrentDateTime => DateTime.Now.ToString("dddd, MMMM d, yyyy  h:mm:ss tt");
    public int LineCount => CartItems.Count;
    public int TotalItemCount => CartItems.Sum(c => c.Quantity);
    public string TaxRateDisplay { get; private set; } = "Tax";

    // Discount entry
    [ObservableProperty] private string _discountValue = string.Empty;
    [ObservableProperty] private string _discountType = "percentage"; // "percentage" or "fixed"

    // Customer search
    [ObservableProperty] private string _customerSearchQuery = string.Empty;
    [ObservableProperty] private ObservableCollection<Customer> _customerSearchResults = new();

    // Suspended transactions
    [ObservableProperty] private ObservableCollection<Transaction> _suspendedTransactions = new();

    public RegisterViewModel(IApiService api, IDatabaseService db, ISyncService sync, NavigationService nav,
        ITaxService taxService, ITransactionService transactionService,
        Employee employee, RegisterSession session)
    {
        _api = api;
        _db = db;
        _sync = sync;
        _nav = nav;
        _taxService = taxService;
        _transactionService = transactionService;
        _employee = employee;
        _session = session;

        IsOnline = sync.IsOnline;
        SyncStatus = sync.SyncStatus;
        sync.OnlineStatusChanged += online => IsOnline = online;
        sync.SyncStatusChanged += status => SyncStatus = status;

        _ = sync.StartAsync();

        // Load quick buttons from cached config
        LoadQuickButtons();

        // Set tax rate display
        var taxLoc = GetTaxLocation();
        if (taxLoc != null)
            TaxRateDisplay = $"Tax ({taxLoc.TaxRate}%)";

        // Start a new transaction
        NewTransaction();
    }

    private TaxLocation? GetTaxLocation()
    {
        return _session.TaxLocationId.HasValue
            ? _db.GetTaxLocation(_session.TaxLocationId.Value)
            : _db.GetDefaultTaxLocation();
    }

    private void LoadQuickButtons()
    {
        var json = _db.GetConfig("quick_buttons");
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var buttons = System.Text.Json.JsonSerializer.Deserialize<List<QuickButton>>(json,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                        PropertyNameCaseInsensitive = true,
                    });
                if (buttons != null)
                    QuickButtons = new ObservableCollection<QuickButton>(buttons);
            }
            catch { /* ignore parse errors */ }
        }
    }

    [RelayCommand]
    private void QuickButtonClick(QuickButton button)
    {
        if (button.ButtonType == "product" && !string.IsNullOrEmpty(button.Sku))
        {
            var product = LookupProduct(button.Sku);
            if (product != null)
                AddProductToCart(product);
            else
                StatusMessage = $"Product not found: {button.Sku}";
        }
        else if (button.ButtonType == "category" && !string.IsNullOrEmpty(button.CategoryId))
        {
            // Load products in this category
            SearchQuery = string.Empty;
            var results = _db.GetProductsByCategory(button.CategoryId);
            SearchResults = new ObservableCollection<Product>(results);
            StatusMessage = $"Category: {button.Label} ({results.Count} items)";
        }
    }

    private void NewTransaction()
    {
        ActiveTransaction = _transactionService.CreateTransaction(_employee.Id, _employee.Name, _session.Id);
        CartItems.Clear();
        AttachedCustomer = null;
        DiscountValue = string.Empty;
        RecalculateTotals();
    }

    private void RecalculateTotals()
    {
        if (ActiveTransaction == null) return;

        var totals = _transactionService.CalculateTotals(ActiveTransaction, GetTaxLocation(), isCash: false);
        Subtotal = totals.Subtotal;
        DiscountTotal = totals.Discount;
        TaxTotal = totals.Tax;
        ServiceFeeTotalDisplay = totals.ServiceFee + totals.ServiceFeeTax;
        GrandTotal = totals.Total;
        OnPropertyChanged(nameof(LineCount));
        OnPropertyChanged(nameof(TotalItemCount));

        // Show auto-discount descriptions
        if (totals.AppliedDiscounts.Count > 0)
        {
            DiscountDescription = string.Join(" | ", totals.AppliedDiscounts.Select(d => $"{d.Name}: -${d.Savings:F2}"));
        }
        else
        {
            DiscountDescription = string.Empty;
        }
    }

    #region Product Search & Lookup

    [RelayCommand]
    private void ProductSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        // Try item lookup first (exact scan/entry)
        var product = LookupProduct(SearchQuery.Trim());
        if (product != null)
        {
            AddProductToCart(product);
            SearchQuery = string.Empty;
            SearchResults.Clear();
            return;
        }

        // Fall back to search
        var results = _db.SearchProducts(SearchQuery.Trim());
        SearchResults = new ObservableCollection<Product>(results);
    }

    /// <summary>
    /// Mirrors PHP item_lookup.php lookup chain:
    /// 1. Exact SKU → 2. Exact UPC → 3. Variant SKU/UPC → 4. Barcode relationship → 5. Partial SKU
    /// </summary>
    private Product? LookupProduct(string code)
    {
        // Step 1: Exact SKU
        var product = _db.GetProductBySku(code);
        if (product != null) return product;

        // Step 2: Exact UPC
        product = _db.GetProductByUpc(code);
        if (product != null) return product;

        // Step 3: Variant by SKU or UPC
        product = _db.GetVariantBySkuOrUpc(code);
        if (product != null) return product;

        // Step 4: Barcode relationship
        var barcodeRel = _db.GetBarcodeRelationship(code);
        if (barcodeRel.HasValue)
        {
            var linked = _db.GetProductBySku(barcodeRel.Value.ParentSku);
            if (linked == null) linked = _db.GetVariantBySkuOrUpc(barcodeRel.Value.ParentSku);
            if (linked != null)
            {
                return new Product
                {
                    ProductId = linked.ProductId,
                    Sku = linked.Sku,
                    Name = linked.Name,
                    Price = linked.Price,
                    Quantity = linked.Quantity,
                    Upc = linked.Upc,
                    CategoryId = linked.CategoryId,
                    CategoryName = linked.CategoryName,
                    UnitOfSale = linked.UnitOfSale,
                    PricePerUnit = linked.PricePerUnit,
                    Subtract = linked.Subtract,
                    IsTaxable = linked.IsTaxable,
                    IsVariant = linked.IsVariant,
                    VariantId = linked.VariantId,
                    VariantName = linked.VariantName,
                    VariantValue = linked.VariantValue,
                    ParentName = linked.ParentName,
                    IsBundle = linked.IsBundle,
                    BundleId = linked.BundleId,
                    IsService = linked.IsService,
                    ServiceId = linked.ServiceId,
                    UpdatedAt = linked.UpdatedAt,
                    QtyMultiplier = Math.Max(1, barcodeRel.Value.QtyCount),
                };
            }
        }

        // Step 5: Partial SKU
        product = _db.GetProductByPartialSku(code);
        return product;
    }

    [RelayCommand]
    private void SelectSearchResult(Product product)
    {
        AddProductToCart(product);
        SearchQuery = string.Empty;
        SearchResults.Clear();
    }

    private void AddProductToCart(Product product)
    {
        if (ActiveTransaction == null) return;

        // Check if same SKU already in cart
        var existing = CartItems.FirstOrDefault(ci => ci.Product.Sku == product.Sku && ci.OverridePrice == null);
        if (existing != null)
        {
            existing.Quantity += 1;
            _transactionService.UpdateItemQuantity(ActiveTransaction,
                ActiveTransaction.Items.First(i => i.Sku == product.Sku), existing.Quantity);
        }
        else
        {
            var item = _transactionService.AddItem(ActiveTransaction, product);
            CartItems.Add(new CartItem
            {
                Product = product,
                Quantity = 1,
                QtyMultiplier = product.QtyMultiplier,
            });
        }

        RecalculateTotals();
        StatusMessage = $"Added: {product.DisplayName}";
    }

    #endregion

    #region Cart Operations

    [RelayCommand]
    private void IncreaseQuantity(CartItem cartItem)
    {
        if (ActiveTransaction == null) return;
        cartItem.Quantity++;
        var txnItem = ActiveTransaction.Items.FirstOrDefault(i => i.Sku == cartItem.Sku);
        if (txnItem != null) _transactionService.UpdateItemQuantity(ActiveTransaction, txnItem, cartItem.Quantity);
        RecalculateTotals();
    }

    [RelayCommand]
    private void DecreaseQuantity(CartItem cartItem)
    {
        if (ActiveTransaction == null) return;
        if (cartItem.Quantity <= 1)
        {
            RemoveCartItem(cartItem);
            return;
        }
        cartItem.Quantity--;
        var txnItem = ActiveTransaction.Items.FirstOrDefault(i => i.Sku == cartItem.Sku);
        if (txnItem != null) _transactionService.UpdateItemQuantity(ActiveTransaction, txnItem, cartItem.Quantity);
        RecalculateTotals();
    }

    [RelayCommand]
    private void RemoveCartItem(CartItem cartItem)
    {
        if (ActiveTransaction == null) return;
        var txnItem = ActiveTransaction.Items.FirstOrDefault(i => i.Sku == cartItem.Sku);
        if (txnItem != null) _transactionService.RemoveItem(ActiveTransaction, txnItem);
        CartItems.Remove(cartItem);
        RecalculateTotals();
    }

    [RelayCommand]
    private void ClearCart()
    {
        if (ActiveTransaction == null) return;
        ActiveTransaction.Items.Clear();
        CartItems.Clear();
        RecalculateTotals();
    }

    #endregion

    #region Discounts

    [RelayCommand]
    private void ToggleDiscountPanel()
    {
        ShowDiscountPanel = !ShowDiscountPanel;
    }

    [RelayCommand]
    private void ApplyDiscount()
    {
        if (ActiveTransaction == null || string.IsNullOrWhiteSpace(DiscountValue)) return;
        if (!decimal.TryParse(DiscountValue, out var amount) || amount <= 0) return;

        _transactionService.ApplyTransactionDiscount(ActiveTransaction, amount, DiscountType);
        RecalculateTotals();
        StatusMessage = DiscountType == "percentage" ? $"Discount: {amount}% applied" : $"Discount: ${amount:F2} applied";
    }

    [RelayCommand]
    private void ClearDiscount()
    {
        if (ActiveTransaction == null) return;
        _transactionService.ApplyTransactionDiscount(ActiveTransaction, 0, "");
        DiscountValue = string.Empty;
        RecalculateTotals();
    }

    #endregion

    #region Payment

    [RelayCommand]
    private void OpenPayment()
    {
        if (ActiveTransaction == null || CartItems.Count == 0)
        {
            StatusMessage = "Cart is empty.";
            return;
        }
        ShowPaymentPanel = true;
    }

    [RelayCommand]
    private void ClosePayment()
    {
        ShowPaymentPanel = false;
    }

    [RelayCommand]
    private void PayCash()
    {
        if (ActiveTransaction == null) return;
        var totals = _transactionService.CalculateTotals(ActiveTransaction, GetTaxLocation(), isCash: true);

        _nav.NavigateTo(new PaymentViewModel(
            _api, _db, _sync, _nav, _taxService, _transactionService,
            _employee, _session, ActiveTransaction, totals, "Cash"));
    }

    [RelayCommand]
    private void PayCredit()
    {
        if (ActiveTransaction == null) return;
        var totals = _transactionService.CalculateTotals(ActiveTransaction, GetTaxLocation(), isCash: false);

        _nav.NavigateTo(new PaymentViewModel(
            _api, _db, _sync, _nav, _taxService, _transactionService,
            _employee, _session, ActiveTransaction, totals, "Credit"));
    }

    #endregion

    #region Void / Suspend / Resume

    [RelayCommand]
    private void VoidTransaction()
    {
        if (ActiveTransaction == null) return;
        _transactionService.VoidTransaction(ActiveTransaction);
        StatusMessage = "Transaction voided.";
        NewTransaction();
    }

    [RelayCommand]
    private void SuspendTransaction()
    {
        if (ActiveTransaction == null || CartItems.Count == 0) return;
        _transactionService.SuspendTransaction(ActiveTransaction, null);
        StatusMessage = "Transaction suspended.";
        NewTransaction();
    }

    [RelayCommand]
    private void ShowSuspended()
    {
        var suspended = _db.GetSuspendedTransactions();
        SuspendedTransactions = new ObservableCollection<Transaction>(suspended);
        ShowSuspendedList = true;
    }

    [RelayCommand]
    private void CloseSuspendedList()
    {
        ShowSuspendedList = false;
    }

    [RelayCommand]
    private void ResumeTransaction(Transaction suspended)
    {
        if (ActiveTransaction != null && CartItems.Count > 0)
        {
            _transactionService.SuspendTransaction(ActiveTransaction, "Auto-suspended on resume");
        }

        suspended.Type = "Active";
        suspended.SuspendedAt = null;
        _db.UpdateTransaction(suspended);
        ActiveTransaction = suspended;

        CartItems.Clear();
        foreach (var item in suspended.Items)
        {
            var product = _db.GetProductBySku(item.Sku);
            if (product != null)
            {
                CartItems.Add(new CartItem
                {
                    Product = product,
                    Quantity = item.Qty,
                    OverridePrice = item.OverridePrice,
                    QtyMultiplier = item.QtyMultiplier,
                });
            }
        }

        RecalculateTotals();
        ShowSuspendedList = false;
        StatusMessage = "Transaction resumed.";
    }

    #endregion

    #region Customer

    [RelayCommand]
    private void OpenCustomerLookup()
    {
        ShowCustomerLookup = true;
    }

    [RelayCommand]
    private void CloseCustomerLookup()
    {
        ShowCustomerLookup = false;
    }

    [RelayCommand]
    private void SearchCustomers()
    {
        if (string.IsNullOrWhiteSpace(CustomerSearchQuery)) return;
        var results = _db.SearchCustomers(CustomerSearchQuery.Trim());
        CustomerSearchResults = new ObservableCollection<Customer>(results);
    }

    [RelayCommand]
    private void AttachCustomer(Customer customer)
    {
        if (ActiveTransaction == null) return;
        ActiveTransaction.CustomerId = customer.CustomerId;
        ActiveTransaction.CustomerName = customer.DisplayName;
        AttachedCustomer = customer;
        ShowCustomerLookup = false;
        StatusMessage = $"Customer: {customer.DisplayName}";
    }

    [RelayCommand]
    private void DetachCustomer()
    {
        if (ActiveTransaction == null) return;
        ActiveTransaction.CustomerId = null;
        ActiveTransaction.CustomerName = null;
        AttachedCustomer = null;
    }

    #endregion

    #region Register Close / Logout

    [RelayCommand]
    private void CloseRegister()
    {
        _sync.Stop();
        _nav.NavigateTo(new RegisterOpenCloseViewModel(
            _api, _db, _nav, _taxService, _transactionService, _sync,
            _employee, isOpening: false, session: _session));
    }

    [RelayCommand]
    private void Logout()
    {
        _sync.Stop();
        _nav.NavigateTo(new LoginViewModel(_api, _db, _sync, _nav, _taxService, _transactionService));
    }

    #endregion
}
