using System.Text.Json;
using MonveriRegister.Models;

namespace MonveriRegister.Services;

public interface ITransactionService
{
    Transaction CreateTransaction(int employeeId, string employeeName, int? sessionId);
    TransactionItem AddItem(Transaction transaction, Product product, int qty = 1);
    void RemoveItem(Transaction transaction, TransactionItem item);
    void UpdateItemQuantity(Transaction transaction, TransactionItem item, int newQty);
    void OverrideItemPrice(TransactionItem item, decimal newPrice);
    void ApplyItemDiscount(TransactionItem item, decimal amount, string type);
    void ApplyTransactionDiscount(Transaction transaction, decimal amount, string type);
    TransactionTotals CalculateTotals(Transaction transaction, TaxLocation? taxLocation, bool isCash);
    void FinalizeCash(Transaction transaction, TaxLocation? taxLocation, decimal tendered);
    void FinalizeCredit(Transaction transaction, TaxLocation? taxLocation);
    void VoidTransaction(Transaction transaction);
    void SuspendTransaction(Transaction transaction, string? note);
}

public class TransactionTotals
{
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal DiscountedSubtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal TaxRate { get; set; }
    public decimal AutoDiscount { get; set; }
    public List<AppliedDiscount> AppliedDiscounts { get; set; } = new();
    public decimal ServiceFee { get; set; }
    public decimal ServiceFeeTax { get; set; }
    public decimal Total { get; set; }
}

public class TransactionService : ITransactionService
{
    private readonly ITaxService _taxService;
    private readonly IDatabaseService _db;
    private readonly IApiService _api;
    private readonly IDiscountService _discountService;

    public TransactionService(ITaxService taxService, IDatabaseService db, IApiService api, IDiscountService discountService)
    {
        _taxService = taxService;
        _db = db;
        _api = api;
        _discountService = discountService;
    }

    public Transaction CreateTransaction(int employeeId, string employeeName, int? sessionId)
    {
        var transaction = new Transaction
        {
            EmployeeId = employeeId,
            EmployeeName = employeeName,
            Type = "Active",
            Timestamp = DateTime.UtcNow.ToString("o"),
            SessionId = sessionId,
            ReceiptToken = Guid.NewGuid().ToString("N")[..12].ToUpper(),
        };
        transaction.Id = _db.InsertTransaction(transaction);
        return transaction;
    }

    public TransactionItem AddItem(Transaction transaction, Product product, int qty = 1)
    {
        var item = new TransactionItem
        {
            TransactionId = transaction.Id,
            EmployeeId = transaction.EmployeeId,
            Sku = product.Sku,
            Name = product.DisplayName,
            Upc = product.Upc,
            Price = product.Price,
            Qty = qty,
            IsVariant = product.IsVariantBool,
            VariantId = product.VariantId,
            IsBundle = product.IsBundleBool,
            BundleId = product.BundleId,
            IsService = product.IsServiceBool,
            ServiceId = product.ServiceId,
            UnitOfSale = product.UnitOfSale,
            QtyMultiplier = product.QtyMultiplier,
        };
        transaction.Items.Add(item);
        return item;
    }

    public void RemoveItem(Transaction transaction, TransactionItem item)
    {
        transaction.Items.Remove(item);
    }

    public void UpdateItemQuantity(Transaction transaction, TransactionItem item, int newQty)
    {
        if (newQty <= 0)
            RemoveItem(transaction, item);
        else
            item.Qty = newQty;
    }

    public void OverrideItemPrice(TransactionItem item, decimal newPrice)
    {
        item.OverridePrice = newPrice;
    }

    public void ApplyItemDiscount(TransactionItem item, decimal amount, string type)
    {
        item.OverridePrice = type == "percentage"
            ? Math.Round(item.Price * (1 - amount / 100m), 2)
            : Math.Max(0, item.Price - amount);
    }

    public void ApplyTransactionDiscount(Transaction transaction, decimal amount, string type)
    {
        transaction.Discount = amount;
        transaction.DiscountType = type;
    }

    /// <summary>
    /// Mirrors the PHP cash.php total calculation order:
    /// 1. Subtotal from items
    /// 2. Apply discounts
    /// 3. Tax on discounted subtotal
    /// 4. Nickel rounding for cash
    /// </summary>
    public TransactionTotals CalculateTotals(Transaction transaction, TaxLocation? taxLocation, bool isCash)
    {
        var totals = new TransactionTotals();

        // 1. Subtotal
        totals.Subtotal = 0;
        foreach (var item in transaction.Items)
        {
            decimal unitPrice = item.OverridePrice ?? item.Price;
            totals.Subtotal += unitPrice * item.Qty * item.QtyMultiplier;
        }
        totals.Subtotal = Math.Round(totals.Subtotal, 2);

        // 2a. Auto-discounts (from discount rules)
        var autoDiscountResult = _discountService.CheckDiscounts(transaction.Items);
        totals.AutoDiscount = autoDiscountResult.TotalSavings;
        totals.AppliedDiscounts = autoDiscountResult.Discounts;

        // 2b. Manual/custom discount
        decimal manualDiscount = 0;
        if (transaction.Discount > 0)
        {
            manualDiscount = transaction.DiscountType == "percentage"
                ? Math.Round(totals.Subtotal * (transaction.Discount / 100m), 2)
                : Math.Min(transaction.Discount, totals.Subtotal);
        }

        totals.Discount = totals.AutoDiscount + manualDiscount;

        // Discounted subtotal (floor at 0)
        totals.DiscountedSubtotal = Math.Max(0, totals.Subtotal - totals.Discount);

        // 3. Tax on discounted subtotal
        // Only calculate tax on taxable items
        decimal taxableAmount = 0;
        foreach (var item in transaction.Items)
        {
            // Check if item is from a taxable product
            var product = _db.GetProductBySku(item.Sku);
            if (product == null || product.IsTaxableBool)
            {
                decimal unitPrice = item.OverridePrice ?? item.Price;
                taxableAmount += unitPrice * item.Qty * item.QtyMultiplier;
            }
        }
        // Apply discount proportionally to taxable amount
        if (totals.Subtotal > 0 && totals.Discount > 0)
        {
            decimal discountRatio = totals.Discount / totals.Subtotal;
            taxableAmount = Math.Max(0, taxableAmount - Math.Round(taxableAmount * discountRatio, 2));
        }

        var taxResult = _taxService.CalculateTaxWithBreakdown(taxableAmount, taxLocation);
        totals.Tax = taxResult.Total;
        totals.TaxRate = taxResult.Rate;

        // 4. Service fee (for credit card payments)
        var sfEnabled = _db.GetConfig("service_fees_enabled");
        if (!isCash && sfEnabled == "1")
        {
            var sfPercent = decimal.TryParse(_db.GetConfig("service_fee_percent"), out var pct) ? pct : 0;
            totals.ServiceFee = Math.Round(totals.DiscountedSubtotal * (sfPercent / 100m), 2);

            var sfTaxable = _db.GetConfig("service_fee_taxable");
            if (sfTaxable == "1" && taxLocation != null)
            {
                var sfTax = _taxService.CalculateTaxWithBreakdown(totals.ServiceFee, taxLocation);
                totals.ServiceFeeTax = sfTax.Total;
            }
        }

        // 5. Grand total
        totals.Total = totals.DiscountedSubtotal + totals.Tax + totals.ServiceFee + totals.ServiceFeeTax;

        // Nickel rounding for cash payments - mirrors PHP: round($total / 0.05) * 0.05
        if (isCash)
        {
            totals.Total = Math.Round(totals.Total / 0.05m, MidpointRounding.AwayFromZero) * 0.05m;
            totals.Total = Math.Round(totals.Total, 2);
        }

        return totals;
    }

    public void FinalizeCash(Transaction transaction, TaxLocation? taxLocation, decimal tendered)
    {
        var totals = CalculateTotals(transaction, taxLocation, isCash: true);
        ApplyTotals(transaction, totals, taxLocation);
        transaction.Payment = "Cash";
        transaction.Type = "Sale";
        transaction.Tendered = tendered;
        transaction.ChangeAmount = Math.Max(0, tendered - totals.Total);
        _db.UpdateTransaction(transaction);
        EnqueueForSync(transaction);
    }

    public void FinalizeCredit(Transaction transaction, TaxLocation? taxLocation)
    {
        var totals = CalculateTotals(transaction, taxLocation, isCash: false);
        ApplyTotals(transaction, totals, taxLocation);
        transaction.Payment = "Credit";
        transaction.Type = "Sale";
        _db.UpdateTransaction(transaction);
        EnqueueForSync(transaction);
    }

    public void VoidTransaction(Transaction transaction)
    {
        transaction.Type = "Void";
        _db.UpdateTransaction(transaction);
        EnqueueForSync(transaction);
    }

    public void SuspendTransaction(Transaction transaction, string? note)
    {
        transaction.Type = "Suspended";
        transaction.SuspendedAt = DateTime.UtcNow.ToString("o");
        transaction.SuspendedNote = note;
        _db.UpdateTransaction(transaction);
    }

    private void ApplyTotals(Transaction transaction, TransactionTotals totals, TaxLocation? taxLocation)
    {
        transaction.Subtotal = totals.Subtotal;
        transaction.Discount = totals.Discount;
        transaction.Tax = totals.Tax;
        transaction.TaxRate = totals.TaxRate;
        transaction.TaxLocationId = taxLocation?.Id;
        transaction.ServiceFee = totals.ServiceFee;
        transaction.ServiceFeeTax = totals.ServiceFeeTax;
        transaction.Total = totals.Total;
    }

    private void EnqueueForSync(Transaction transaction)
    {
        var action = transaction.Type switch
        {
            "Void" => "void",
            "Refund" => "refund",
            _ => "create",
        };

        _db.EnqueueSync(new SyncQueueItem
        {
            EntityType = "transaction",
            EntityId = transaction.Id,
            Action = action,
            Payload = JsonSerializer.Serialize(transaction),
        });
    }
}
