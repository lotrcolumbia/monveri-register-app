namespace MonveriRegister.Models;

public class Transaction
{
    public int Id { get; set; }
    public int? ServerId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string Type { get; set; } = "Active"; // Active, Sale, Void, Refund, Suspended
    public string? Payment { get; set; } // Cash, Credit, Split, Other
    public string? PaymentDetail { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public string? DiscountType { get; set; }
    public decimal Tax { get; set; }
    public decimal TaxRate { get; set; }
    public int? TaxLocationId { get; set; }
    public decimal ServiceFee { get; set; }
    public decimal ServiceFeeTax { get; set; }
    public decimal Total { get; set; }
    public decimal? Tendered { get; set; }
    public decimal? ChangeAmount { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string? SuspendedAt { get; set; }
    public string? SuspendedNote { get; set; }
    public bool IsSplitPayment { get; set; }
    public string? ReceiptToken { get; set; }
    public int? SessionId { get; set; }
    public int? OriginalTicketId { get; set; }
    public bool IsSynced { get; set; }

    public List<TransactionItem> Items { get; set; } = new();
    public List<SplitPayment> SplitPayments { get; set; } = new();
}

public class TransactionItem
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public int EmployeeId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Upc { get; set; }
    public decimal Price { get; set; }
    public int Qty { get; set; } = 1;
    public decimal? OverridePrice { get; set; }
    public decimal? Cost { get; set; }
    public bool IsVariant { get; set; }
    public int? VariantId { get; set; }
    public bool IsBundle { get; set; }
    public int? BundleId { get; set; }
    public bool IsService { get; set; }
    public int? ServiceId { get; set; }
    public string UnitOfSale { get; set; } = "piece";
    public decimal? MeasuredQty { get; set; }
    public int QtyMultiplier { get; set; } = 1;
}

public class SplitPayment
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
}
