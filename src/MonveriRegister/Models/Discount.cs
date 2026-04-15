namespace MonveriRegister.Models;

public class Discount
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // percent_order, percent_category, buy_x_get_percent, buy_x_get_free, buy_x_get_other_free, buy_x_category_dollar_off
    public decimal DiscountValue { get; set; }
    public decimal MinSpend { get; set; }
    public int BuyQuantity { get; set; }
    public int GetQuantity { get; set; }
    public int? CategoryId { get; set; }
    public string? Sku { get; set; }
    public string? FreeSku { get; set; }
    public int Stackable { get; set; }
    public string BxgyTargetMode { get; set; } = "product";
    public string? CategoryIds { get; set; } // comma-separated
}

public class Category
{
    public string CategoryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentId { get; set; }
}

public class DiscountResult
{
    public List<AppliedDiscount> Discounts { get; set; } = new();
    public decimal TotalSavings { get; set; }
}

public class AppliedDiscount
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Savings { get; set; }
    public int FreeQty { get; set; }
}
