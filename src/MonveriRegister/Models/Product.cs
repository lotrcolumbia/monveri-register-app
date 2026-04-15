namespace MonveriRegister.Models;

public class Product
{
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string? Upc { get; set; }
    public string? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string UnitOfSale { get; set; } = "piece";
    public decimal? PricePerUnit { get; set; }
    public int Subtract { get; set; } = 1;
    public int IsTaxable { get; set; } = 1;

    // Variant info
    public int IsVariant { get; set; }
    public int? VariantId { get; set; }
    public string? VariantName { get; set; }
    public string? VariantValue { get; set; }
    public string? ParentName { get; set; }

    // Bundle info
    public int IsBundle { get; set; }
    public int? BundleId { get; set; }

    // Service info
    public int IsService { get; set; }
    public int? ServiceId { get; set; }

    // Barcode relationship
    public int QtyMultiplier { get; set; } = 1;

    public string UpdatedAt { get; set; } = string.Empty;

    // Computed bool helpers
    public bool IsTaxableBool => IsTaxable != 0;
    public bool IsVariantBool => IsVariant != 0;
    public bool IsBundleBool => IsBundle != 0;
    public bool IsServiceBool => IsService != 0;

    public string DisplayName => IsVariantBool && !string.IsNullOrEmpty(ParentName)
        ? $"{ParentName} - {VariantValue}"
        : Name;
}
