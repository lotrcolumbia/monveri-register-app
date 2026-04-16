using MonveriRegister.Models;

namespace MonveriRegister.Services;

public interface IDiscountService
{
    DiscountResult CheckDiscounts(List<TransactionItem> items);
}

/// <summary>
/// Ports the full PHP checkDiscounts() logic from sections/check_discounts.php.
/// Supports: percent_order, percent_category, buy_x_get_percent, buy_x_get_free,
/// buy_x_get_other_free, buy_x_category_dollar_off.
/// </summary>
public class DiscountService : IDiscountService
{
    private readonly IDatabaseService _db;

    public DiscountService(IDatabaseService db)
    {
        _db = db;
    }

    public DiscountResult CheckDiscounts(List<TransactionItem> items)
    {
        var result = new DiscountResult();
        var appliedTypes = new HashSet<string>();
        var allowStacking = _db.GetConfig("discount_stacking") == "1";

        var discounts = _db.GetAllDiscounts();
        if (discounts.Count == 0 || items.Count == 0)
            return result;

        // Build category lookup cache
        var categories = _db.GetAllCategories().ToDictionary(c => c.CategoryId, c => c);

        // Calculate subtotal
        decimal subtotal = 0;
        foreach (var item in items)
        {
            decimal price = item.OverridePrice ?? item.Price;
            subtotal += price * item.Qty * item.QtyMultiplier;
        }

        foreach (var discount in discounts)
        {
            decimal savings = 0;
            var info = new AppliedDiscount
            {
                Id = discount.Id,
                Name = discount.Name,
                Type = discount.Type,
            };

            switch (discount.Type)
            {
                case "percent_order":
                    savings = subtotal * (discount.DiscountValue / 100m);
                    info.Description = $"{discount.DiscountValue}% off order";
                    break;

                case "percent_category":
                    if (discount.CategoryId.HasValue)
                    {
                        foreach (var item in items)
                        {
                            var itemCatId = _db.GetProductCategoryId(item.Sku);
                            if (itemCatId != null && IsCategoryMatch(itemCatId, discount.CategoryId.Value.ToString(), categories))
                            {
                                decimal price = item.OverridePrice ?? item.Price;
                                int effectiveQty = item.Qty * item.QtyMultiplier;
                                savings += (price * effectiveQty) * (discount.DiscountValue / 100m);
                            }
                        }
                        info.Description = $"{discount.DiscountValue}% off category";
                    }
                    break;

                case "buy_x_get_percent":
                    if (subtotal >= discount.MinSpend)
                    {
                        savings = subtotal * (discount.DiscountValue / 100m);
                        info.Description = $"Spend ${discount.MinSpend:F2}, get {discount.DiscountValue}% off";
                    }
                    break;

                case "buy_x_get_free":
                    savings = CalculateBuyXGetFree(discount, items, categories, info);
                    break;

                case "buy_x_get_other_free":
                    savings = CalculateBuyXGetOtherFree(discount, items, info);
                    break;

                case "buy_x_category_dollar_off":
                    savings = CalculateBuyXCategoryDollarOff(discount, items, categories, info);
                    break;
            }

            if (savings > 0)
            {
                savings = Math.Round(savings, 2);
                info.Savings = savings;

                bool isBuyGetType = discount.Type is "buy_x_get_free" or "buy_x_get_other_free";

                if (allowStacking || (isBuyGetType && discount.Stackable != 0))
                {
                    result.Discounts.Add(info);
                    result.TotalSavings += savings;
                    appliedTypes.Add(discount.Type);
                }
                else if (appliedTypes.Contains(discount.Type))
                {
                    var existing = result.Discounts.FirstOrDefault(d => d.Type == discount.Type);
                    if (existing != null && savings > existing.Savings)
                    {
                        result.TotalSavings -= existing.Savings;
                        existing.Id = info.Id;
                        existing.Name = info.Name;
                        existing.Description = info.Description;
                        existing.Savings = savings;
                        existing.FreeQty = info.FreeQty;
                        result.TotalSavings += savings;
                    }
                }
                else
                {
                    result.Discounts.Add(info);
                    result.TotalSavings += savings;
                    appliedTypes.Add(discount.Type);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Mirrors PHP buy_x_get_free: supports product, category, and subcategory modes.
    /// </summary>
    private decimal CalculateBuyXGetFree(Discount discount, List<TransactionItem> items,
        Dictionary<string, Category> categories, AppliedDiscount info)
    {
        int buyQty = discount.BuyQuantity;
        int freeQty = discount.GetQuantity;
        if (buyQty <= 0 || freeQty <= 0) return 0;

        if (discount.BxgyTargetMode == "product")
        {
            // Buy X of same product, get Y free
            foreach (var item in items)
            {
                if (item.Sku == discount.Sku)
                {
                    int totalQty = item.Qty * item.QtyMultiplier;
                    int sets = totalQty / (buyQty + freeQty);
                    int remainder = totalQty % (buyQty + freeQty);
                    int freeItems = sets * freeQty;
                    if (remainder > buyQty)
                        freeItems += (remainder - buyQty);

                    if (freeItems > 0)
                    {
                        decimal price = item.OverridePrice ?? item.Price;
                        info.Description = $"Buy {buyQty} get {freeQty} free";
                        info.FreeQty = freeItems;
                        return freeItems * price;
                    }
                    break;
                }
            }
        }
        else
        {
            // Category or subcategory mode: aggregate all items in the category
            int? targetCatId = discount.CategoryId;
            if (!targetCatId.HasValue) return 0;

            var matchingPrices = new List<decimal>();
            foreach (var item in items)
            {
                var itemCatId = _db.GetProductCategoryId(item.Sku);
                bool matches = false;

                if (discount.BxgyTargetMode == "category")
                    matches = itemCatId != null && IsCategoryMatch(itemCatId, targetCatId.Value.ToString(), categories);
                else // subcategory: exact match only
                    matches = itemCatId == targetCatId.Value.ToString();

                if (matches)
                {
                    decimal price = item.OverridePrice ?? item.Price;
                    int effectiveQty = item.Qty * item.QtyMultiplier;
                    for (int i = 0; i < effectiveQty; i++)
                        matchingPrices.Add(price);
                }
            }

            int totalQty = matchingPrices.Count;
            if (totalQty > 0)
            {
                int sets = totalQty / (buyQty + freeQty);
                int remainder = totalQty % (buyQty + freeQty);
                int freeItems = sets * freeQty;
                if (remainder > buyQty)
                    freeItems += (remainder - buyQty);

                if (freeItems > 0)
                {
                    // Sort ascending so free items are the cheapest
                    matchingPrices.Sort();
                    decimal savings = 0;
                    for (int i = 0; i < freeItems; i++)
                        savings += matchingPrices[i];

                    info.Description = $"Buy {buyQty} get {freeQty} free (category)";
                    info.FreeQty = freeItems;
                    return savings;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Mirrors PHP buy_x_get_other_free: Buy X of one item, get different item free.
    /// </summary>
    private decimal CalculateBuyXGetOtherFree(Discount discount, List<TransactionItem> items, AppliedDiscount info)
    {
        string? buySku = discount.Sku;
        string? freeSku = discount.FreeSku;
        int buyQty = discount.BuyQuantity;
        int freeQty = discount.GetQuantity;

        if (string.IsNullOrEmpty(buySku) || string.IsNullOrEmpty(freeSku)) return 0;

        int buyItemQty = 0;
        decimal freeItemPrice = 0;
        int freeItemQty = 0;

        foreach (var item in items)
        {
            if (item.Sku == buySku)
                buyItemQty = item.Qty * item.QtyMultiplier;
            if (item.Sku == freeSku)
            {
                freeItemPrice = item.OverridePrice ?? item.Price;
                freeItemQty = item.Qty * item.QtyMultiplier;
            }
        }

        if (buyItemQty >= buyQty && freeItemQty > 0)
        {
            int qualifyingSets = buyItemQty / buyQty;
            int freeItemsEarned = qualifyingSets * freeQty;
            int actualFree = Math.Min(freeItemsEarned, freeItemQty);

            if (actualFree > 0)
            {
                info.Description = $"Buy {buyQty} {buySku}, get {freeSku} free";
                info.FreeQty = actualFree;
                return actualFree * freeItemPrice;
            }
        }

        return 0;
    }

    /// <summary>
    /// Mirrors PHP buy_x_category_dollar_off: Buy X items from categories, get $Y off each.
    /// </summary>
    private decimal CalculateBuyXCategoryDollarOff(Discount discount, List<TransactionItem> items,
        Dictionary<string, Category> categories, AppliedDiscount info)
    {
        int buyQty = discount.BuyQuantity;
        decimal dollarOff = discount.DiscountValue;

        var targetCatIds = new List<string>();
        if (!string.IsNullOrEmpty(discount.CategoryIds))
            targetCatIds.AddRange(discount.CategoryIds.Split(',').Select(s => s.Trim()));
        else if (discount.CategoryId.HasValue)
            targetCatIds.Add(discount.CategoryId.Value.ToString());

        if (targetCatIds.Count == 0 || buyQty <= 0) return 0;

        var matchingPrices = new List<decimal>();
        foreach (var item in items)
        {
            var itemCatId = _db.GetProductCategoryId(item.Sku);
            if (itemCatId == null) continue;

            bool matches = targetCatIds.Any(catId => IsCategoryMatch(itemCatId, catId, categories));
            if (matches)
            {
                decimal price = item.OverridePrice ?? item.Price;
                int effectiveQty = item.Qty * item.QtyMultiplier;
                for (int i = 0; i < effectiveQty; i++)
                    matchingPrices.Add(price);
            }
        }

        if (matchingPrices.Count >= buyQty)
        {
            decimal savings = 0;
            foreach (var price in matchingPrices)
                savings += Math.Min(dollarOff, price);

            if (savings > 0)
            {
                info.Description = $"Buy {buyQty}+ from category, ${dollarOff:F2} off each";
                return savings;
            }
        }

        return 0;
    }

    /// <summary>
    /// Checks if a product's category matches a target category,
    /// walking up the parent chain. Mirrors PHP isCategoryMatch().
    /// </summary>
    private static bool IsCategoryMatch(string itemCategoryId, string targetCategoryId,
        Dictionary<string, Category> categories)
    {
        if (itemCategoryId == targetCategoryId) return true;

        // Walk up parent chain (max 10 levels to prevent infinite loops)
        var current = itemCategoryId;
        for (int i = 0; i < 10; i++)
        {
            if (!categories.TryGetValue(current, out var cat) || string.IsNullOrEmpty(cat.ParentId))
                break;
            if (cat.ParentId == targetCategoryId)
                return true;
            current = cat.ParentId;
        }

        return false;
    }
}
