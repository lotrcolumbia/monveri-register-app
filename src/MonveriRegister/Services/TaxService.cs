using MonveriRegister.Models;

namespace MonveriRegister.Services;

public interface ITaxService
{
    TaxResult CalculateTaxWithBreakdown(decimal taxableAmount, TaxLocation? location);
}

/// <summary>
/// Mirrors PHP calculateTaxWithBreakdown() from tax_helper.php
/// </summary>
public class TaxService : ITaxService
{
    public TaxResult CalculateTaxWithBreakdown(decimal taxableAmount, TaxLocation? location)
    {
        var result = new TaxResult();

        if (location == null || taxableAmount <= 0)
            return result;

        result.LocationId = location.Id;
        result.LocationName = location.LocationName;
        result.Rate = location.TaxRate;

        // Calculate breakdown by tax type, rounding each with AwayFromZero (PHP HALF_UP)
        foreach (var rate in location.Rates)
        {
            decimal typeRate = rate.Rate / 100m;
            decimal typeAmount = Math.Round(taxableAmount * typeRate, 2, MidpointRounding.AwayFromZero);

            result.Breakdown.Add(new TaxBreakdownItem
            {
                TaxTypeName = rate.TaxTypeName,
                Rate = rate.Rate,
                Amount = typeAmount,
            });

            result.Total += typeAmount;
        }

        return result;
    }
}
