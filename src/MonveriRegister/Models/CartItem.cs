using CommunityToolkit.Mvvm.ComponentModel;

namespace MonveriRegister.Models;

public partial class CartItem : ObservableObject
{
    [ObservableProperty] private Product _product = null!;
    [ObservableProperty] private int _quantity = 1;
    [ObservableProperty] private decimal? _overridePrice;
    [ObservableProperty] private decimal _discountAmount;
    [ObservableProperty] private string? _discountType; // "percentage" or "fixed"
    [ObservableProperty] private int _qtyMultiplier = 1;

    public decimal UnitPrice => OverridePrice ?? Product.Price;

    public decimal LineSubtotal => UnitPrice * Quantity * QtyMultiplier;

    public decimal LineDiscount
    {
        get
        {
            if (DiscountAmount <= 0 || string.IsNullOrEmpty(DiscountType))
                return 0;

            return DiscountType == "percentage"
                ? Math.Round(LineSubtotal * (DiscountAmount / 100m), 2)
                : Math.Min(DiscountAmount, LineSubtotal);
        }
    }

    public decimal LineTotal => LineSubtotal - LineDiscount;

    public string Sku => Product.Sku;
    public string Name => Product.DisplayName;
    public bool IsTaxable => Product.IsTaxableBool;

    partial void OnProductChanged(Product value)
    {
        OnPropertyChanged(nameof(UnitPrice));
        OnPropertyChanged(nameof(LineSubtotal));
        OnPropertyChanged(nameof(LineDiscount));
        OnPropertyChanged(nameof(LineTotal));
        OnPropertyChanged(nameof(Sku));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsTaxable));
    }

    partial void OnQuantityChanged(int value)
    {
        OnPropertyChanged(nameof(LineSubtotal));
        OnPropertyChanged(nameof(LineDiscount));
        OnPropertyChanged(nameof(LineTotal));
    }

    partial void OnQtyMultiplierChanged(int value)
    {
        OnPropertyChanged(nameof(LineSubtotal));
        OnPropertyChanged(nameof(LineDiscount));
        OnPropertyChanged(nameof(LineTotal));
    }

    partial void OnOverridePriceChanged(decimal? value)
    {
        OnPropertyChanged(nameof(UnitPrice));
        OnPropertyChanged(nameof(LineSubtotal));
        OnPropertyChanged(nameof(LineDiscount));
        OnPropertyChanged(nameof(LineTotal));
    }

    partial void OnDiscountAmountChanged(decimal value)
    {
        OnPropertyChanged(nameof(LineDiscount));
        OnPropertyChanged(nameof(LineTotal));
    }

    partial void OnDiscountTypeChanged(string? value)
    {
        OnPropertyChanged(nameof(LineDiscount));
        OnPropertyChanged(nameof(LineTotal));
    }
}
